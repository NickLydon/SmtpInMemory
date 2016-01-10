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
                    lines |> List.rev |> chan.Reply 
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

    use recorder =     
        let sr = new StreamReader(stream)
        let wr = new StreamWriter(stream)
        wr.NewLine <- "\r\n"
        wr.AutoFlush <- true
        new ReaderWriter(sr, wr)

    let writeline = recorder.Write
    let readline = recorder.Read

    writeline "220 localhost -- Fake proxy server"
   
    let rec readlines emailBuilder emails =
        let line = readline()

        match line with
        | "DATA" -> 
            writeline "354 Start input, end data with <CRLF>.<CRLF>"

            let readUntilTerminator() =
                ()
                |> Seq.unfold(fun() -> Some(readline(),())) 
                |> Seq.takeWhile (fun line -> set [null;".";""] |> Set.contains line |> not)

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
    let recorded = recorder.GetAll()

    client.Close()

    return newMessages,recorded }

type public ForwardServerConfig = { Port: int; Host: string }

let private forwardMessages forwardServer messages = 
    async {
        use client = new TcpClient()

        do! client.ConnectAsync(forwardServer.Host, forwardServer.Port) |> Async.AwaitIAsyncResult |> Async.Ignore

        use stream = client.GetStream()
        use streamWriter = new StreamWriter(stream)
        streamWriter.NewLine <- "\r\n"
        streamWriter.AutoFlush <- true
        use streamReader = new StreamReader(stream)

        for message in messages do
            match message with
            | Received m -> do! streamWriter.WriteLineAsync(m) |> Async.AwaitTask
            | Sent m -> do! streamReader.ReadLineAsync() |> Async.AwaitTask |> Async.Ignore

        do! streamWriter.FlushAsync() |> Async.AwaitIAsyncResult |> Async.Ignore

        client.Close()
    }

let private smtpAgent (cachingAgent: Agent<CheckInbox>) port forwardServer = 
    Agent.Start(fun _ -> 
        let endPoint = new IPEndPoint(IPAddress.Any, port)
        let listener = new TcpListener(endPoint)
        listener.Start()

        let rec loop() = async {
            let! newMessages,recorded = receiveEmails listener
            newMessages
            |> List.map(fun newMessage -> 
                {   From=newMessage.Header.From
                    To=newMessage.Header.To
                    Subject=newMessage.Header.Subject |> Option.fold(fun s t -> t) ""
                    Body=newMessage.Body
                    Headers=newMessage.Header.Headers   }
                |> Add
            )
            |> List.iter cachingAgent.Post

            match forwardServer with
            | Some config -> do! forwardMessages config recorded
            | _ -> ()

            return! loop() 
        }

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


let public NoForwardServer = { Port = -1; Host = "" }

type public Server(port, thruServer) =
    let cache = cachingAgent()
    let server = smtpAgent cache port (if thruServer = NoForwardServer then None else Some thruServer)

    new(port) = Server (port, NoForwardServer)
    new() = Server (25, NoForwardServer)

    member this.GetEmails() = cache.PostAndReply Get
    member this.GetEmailsAndReset() = cache.PostAndReply GetAndReset
    member this.Port with get() = port