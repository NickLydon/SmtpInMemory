module SMTP

open System.Net.Sockets
open System.Net
open System.IO
open System

type private Agent<'T> = MailboxProcessor<'T>

type private Header = { Subject: string option; From: string list; To: string list; Headers: string list }

type private EMailBuilder = { Body:string list; Header: Header }

type EMail = { Body: string seq; Subject: string; From: string seq; To: string seq; Headers: string seq }

let private emptyHeader = { Header.Subject=None; From=[]; To=[]; Headers=[] }
let private emptyEmail = { EMailBuilder.Body=[]; Header=emptyHeader }

type private CheckInbox =
    | Get of AsyncReplyChannel<EMail seq>
    | GetAndReset of AsyncReplyChannel<EMail seq>
    | Add of EMail

let private (|From|To|Subject|Header|) (input:string) =
    let fields = ["From";"To";"Subject"]
    let addColon x = x |> List.map(fun x -> x + ": ")
    let trimStart (x:string) = input.Split([| x |], StringSplitOptions.None).[1..] |> Array.fold (+) ""
    let splitCsv (input:string) = input.Split([| ',' |], StringSplitOptions.None) |> Array.map(fun i -> i.Trim()) |> Array.toList
    let matches = 
        fields
        |> addColon
        |> List.map(fun r -> (input.StartsWith(r),trimStart r))
    match matches with
    | (true,input)::_ -> From(input |> splitCsv)
    | _::(true,input)::_ -> To(input |> splitCsv)
    | _::_::(true,input)::_ -> Subject input
    | _ -> Header input

type private Message = | Received of string | Sent of string

type private ReaderLines =
    | Read of AsyncReplyChannel<string>
    | Write of string
    | GetAll of AsyncReplyChannel<Message list>

let private receiveEmails (listener:TcpListener) = async {

    use! client = listener.AcceptTcpClientAsync() |> Async.AwaitTask

    use stream = client.GetStream()
    
    use sr = new StreamReader(stream)
    use wr = new StreamWriter(stream)
    wr.NewLine <- "\r\n"
    wr.AutoFlush <- true
    let writeline (s:string) = wr.WriteLine(s)
    let readline() = sr.ReadLine()

    writeline "220 localhost -- Fake proxy server"
   
    let rec readlines emailBuilder emails =
        let line = readline()

        match line with
        | "DATA" -> 
            writeline "354 Start input, end data with <CRLF>.<CRLF>"

            let readUntilTerminator() =
                ()
                |> Seq.unfold(fun() -> Some(readline(),())) 
                |> Seq.takeWhile (fun line -> [null;".";""] |> List.contains line |> not)

            let header = 
                readUntilTerminator()
                |> Seq.fold(fun header line ->                    
                    let header = { header with Header.Headers=line::header.Headers }
                    let header =
                        match line with
                        | From l -> { header with From = l }
                        | To l -> { header with To = l }
                        | Subject l -> { header with Subject = Some l }
                        | _ -> header
                    header
                ) emailBuilder.Header
            
            let body = readUntilTerminator() |> List.ofSeq
                      
            readlines emptyEmail ({emailBuilder with Header = header; Body = body}::emails)
        | "QUIT" -> 
            writeline "250 OK"
            emails
        | rest ->
            writeline "250 OK"
            readlines emailBuilder emails
                
    let newMessages = readlines emptyEmail []

    client.Close()
    return newMessages }

let private smtpAgent (cachingAgent: Agent<CheckInbox>) port = 
    Agent.Start(fun _ -> 
        let endPoint = new IPEndPoint(IPAddress.Any, port)
        let listener = new TcpListener(endPoint)
        listener.Start()

        let rec loop() = async {
            let! newMessages = receiveEmails listener
            let valueOrEmptyString = function | Some s -> s | None -> ""
            newMessages
            |> List.map(fun newMessage -> 
                {   From=newMessage.Header.From
                    To=newMessage.Header.To
                    Subject=valueOrEmptyString newMessage.Header.Subject
                    Body=newMessage.Body
                    Headers=newMessage.Header.Headers   }
                |> Add
            )
            |> List.iter cachingAgent.Post
            return! loop() }

        loop())

let private cachingAgent() =
    Agent.Start(fun inbox -> 
        let rec loop messages = async {
            let! newMessage = inbox.Receive()
            match newMessage with
            | Get channel -> 
                channel.Reply messages
                return! loop messages
            | GetAndReset channel ->
                channel.Reply messages
                return! loop []
            | Add message -> 
                return! loop (message::messages) }
        loop [])

type Server(port) =
    let cache = cachingAgent()
    let server = smtpAgent cache port

    new() = Server 25

    member this.GetEmails() = cache.PostAndReply Get
    member this.GetEmailsAndReset() = cache.PostAndReply GetAndReset
    member this.Port with get() = port