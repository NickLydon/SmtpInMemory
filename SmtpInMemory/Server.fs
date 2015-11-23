module SMTP

open System.Net.Sockets
open System.Net
open System.IO

type private Agent<'T> = MailboxProcessor<'T>

type private EMailBuilder = { Body:string list; Subject:string option; From:string option; To:string option; Headers:(string*string) list }

type EMail = { Body: string seq; Subject: string; From: string; To: string; Headers: (string*string) seq }

let private emptyEmail = { EMailBuilder.Body=[]; Subject=None; From=None; To=None; Headers=[] }

type private CheckInbox =
    | Get of AsyncReplyChannel<EMail seq>
    | Add of EMail

let private (|From|To|Subject|Body|Header|) (input:string) =
    let fields = ["From";"To";"Subject"]
    let headers = ["Content-Type";"MIME-Version";"Priority";"Date"]
    let addColon x = x |> List.map(fun x -> x + ": ")
    let trimStart (x:string) = input.TrimStart(x.ToCharArray())
    let matches = 
        fields
        |> addColon
        |> List.map(fun r -> (input.StartsWith(r),trimStart r))
    match matches with
    | (true,input)::_ -> From input
    | _::(true,input)::_ -> To input
    | _::_::(true,input)::_ -> Subject input
    | _ -> 
        let matches = 
            headers
            |> List.zip (headers |> addColon)
            |> List.filter (fst >> input.StartsWith)
        match matches with
        | [] -> Body input
        | (withColon,name)::_ -> Header (name,(trimStart withColon))

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
            let rec readdata (email:EMailBuilder) =
                let line = sr.ReadLine()
                if line = null || line = "." 
                then email
                else 
                    let email =
                        match line with
                        | From l ->
                            match email.From with
                            | None -> { email with From = Some l }
                            | _ -> { email with Body=line::email.Body }
                        | To l ->
                            match email.To with
                            | None -> { email with To = Some l }
                            | _ -> { email with Body=line::email.Body }
                        | Subject l -> 
                            match email.Subject with
                            | None -> { email with Subject = Some l }
                            | _ -> { email with Body=line::email.Body }                        
                        | Header (name,_) when email.Headers |> Seq.map fst |> Seq.contains name -> {email with Body=line::email.Body}
                        | Header l -> {email with Headers=l::email.Headers}
                        | Body l when l <> "" -> {email with Body=l::email.Body}
                        | Body _ -> email
                    readdata email

            let newlines = readdata email
            wr.WriteLine("250 OK")
            readlines newlines
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
            {   From=valueOrEmptyString newMessage.From
                To=valueOrEmptyString newMessage.To
                Subject=valueOrEmptyString newMessage.Subject
                Body=newMessage.Body
                Headers=newMessage.Headers   }
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