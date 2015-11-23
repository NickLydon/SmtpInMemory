module SMTP

open System.Net.Sockets
open System.Net
open System.IO

type private Agent<'T> = MailboxProcessor<'T>

type private Header = { Subject: string option; From: string option; To: string option; Headers: string list }

type private EMailBuilder = { Body:string list; Header: Header }

type EMail = { Body: string seq; Subject: string; From: string; To: string; Headers: string seq }

let private emptyHeader = { Header.Subject=None; From=None; To=None; Headers=[] }
let private emptyEmail = { EMailBuilder.Body=[]; Header=emptyHeader }

type private CheckInbox =
    | Get of AsyncReplyChannel<EMail seq>
    | Add of EMail

let private (|From|To|Subject|Header|) (input:string) =
    let fields = ["From";"To";"Subject"]
    let addColon x = x |> List.map(fun x -> x + ": ")
    let trimStart (x:string) = input.Split([| x |], System.StringSplitOptions.None).[1..] |> Array.fold (+) ""
    let matches = 
        fields
        |> addColon
        |> List.map(fun r -> (input.StartsWith(r),trimStart r))
    match matches with
    | (true,input)::_ -> From input
    | _::(true,input)::_ -> To input
    | _::_::(true,input)::_ -> Subject input
    | _ -> Header input

type private Message = | Received of string | Sent of string

type private ReaderLines =
    | Read of AsyncReplyChannel<string>
    | Write of string
    | GetAll of AsyncReplyChannel<Message list>

type private ReaderWriter (sr:StreamReader, wr:StreamWriter) =
    let agent = 
        Agent.Start(fun inbox ->
            let rec loop lines = async {
                let! msg = inbox.Receive()
                match msg with
                | Read chan -> 
                    let line = sr.ReadLine()
                    chan.Reply line
                    return! loop (Received(line)::lines)
                | Write msg ->
                    wr.WriteLine(msg)
                    return! loop (Sent(msg)::lines)
                | GetAll chan -> 
                    chan.Reply lines
                    return! loop lines }
            loop [])

    member this.Read() = agent.PostAndReply Read
    member this.Write line = agent.Post(Write line)
    member this.GetAll() = agent.PostAndReply GetAll

    interface System.IDisposable with
        member this.Dispose() = 
                    sr.Dispose()
                    wr.Dispose() 

let private receiveEmails (listener:TcpListener) = async {

    use! client = listener.AcceptTcpClientAsync() |> Async.AwaitTask

    use stream = client.GetStream()
    
    use readWriter = 
        let sr = new StreamReader(stream)
        let wr = new StreamWriter(stream)
        wr.NewLine <- "\r\n"
        wr.AutoFlush <- true
        new ReaderWriter(sr, wr)

    readWriter.Write "220 localhost -- Fake proxy server"
   
    let rec readlines emailBuilder emails =
        let line = readWriter.Read()

        match line with
        | "DATA" -> 
            readWriter.Write "354 Start input, end data with <CRLF>.<CRLF>"
            
            let header = 
                emailBuilder.Header
                |> Seq.unfold(fun header ->
                    let line = readWriter.Read()
                    if line = null || line = "." || line = ""
                    then None
                    else
                        let header =
                            match line with
                            | From l -> { header with From = Some l }
                            | To l ->{ header with To = Some l }
                            | Subject l -> { header with Subject = Some l }
                            | Header l -> {header with Headers=l::header.Headers }
                        Some(header, header)
                )
                |> Seq.last
            
            let body = 
                readWriter.Read()
                |> Seq.unfold(fun line ->
                    if line = null || line = "." || line = ""
                    then None
                    else Some(line, readWriter.Read())
                )
                |> List.ofSeq
                      
            readlines emptyEmail ({emailBuilder with Header = header; Body = body}::emails)
        | "QUIT" -> 
            readWriter.Write "250 OK"
            emails
        | rest ->
            readWriter.Write "250 OK"
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
                {   From=valueOrEmptyString newMessage.Header.From
                    To=valueOrEmptyString newMessage.Header.To
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
                channel.Reply(messages)
                return! loop messages
            | Add message -> 
                return! loop (message::messages) }
        loop [])

type Server(port) =
    let cache = cachingAgent()
    let server = smtpAgent cache port

    new() = Server 25

    member this.GetEmails() = cache.PostAndReply Get
    member this.Port with get() = port