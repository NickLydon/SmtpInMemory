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

let private receiveEmail (listener:TcpListener) = async {

    use! client = listener.AcceptTcpClientAsync() |> Async.AwaitTask

    use stream = client.GetStream()
    use sr = new StreamReader(stream)
    use wr = new StreamWriter(stream)
    wr.NewLine <- "\r\n"
    wr.AutoFlush <- true

    wr.WriteLine("220 localhost -- Fake proxy server")
   
    let rec readlines email =
        let line = sr.ReadLine()

        match line with
        | "DATA" -> 
            wr.WriteLine("354 Start input, end data with <CRLF>.<CRLF>")
            
            let header = 
                email.Header
                |> Seq.unfold(fun header ->
                    let line = sr.ReadLine()
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
                sr.ReadLine()
                |> Seq.unfold(fun line ->
                    if line = null || line = "." || line = ""
                    then None
                    else Some(line, sr.ReadLine())
                )
                |> List.ofSeq
                      
            wr.WriteLine("250 OK")
            readlines {email with Header = header; Body = body}
        | "QUIT" -> 
            wr.WriteLine("250 OK")
            email
        | _ -> 
            wr.WriteLine("250 OK")
            readlines email 
                
    let newMessage = readlines emptyEmail

    client.Close()
    return newMessage }

let private smtpAgent (cachingAgent: Agent<CheckInbox>) port = 
    Agent.Start(fun _ -> 
        let endPoint = new IPEndPoint(IPAddress.Any, port)
        let listener = new TcpListener(endPoint)
        listener.Start()

        let rec loop() = async {
            let! newMessage = receiveEmail listener
            let valueOrEmptyString = function | Some s -> s | None -> ""
            {   From=valueOrEmptyString newMessage.Header.From
                To=valueOrEmptyString newMessage.Header.To
                Subject=valueOrEmptyString newMessage.Header.Subject
                Body=newMessage.Body
                Headers=newMessage.Header.Headers   }
            |> Add
            |> cachingAgent.Post
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