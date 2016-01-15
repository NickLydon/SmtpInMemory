[![Build status](https://ci.appveyor.com/api/projects/status/64aw6gj1onwdu60d/branch/master?svg=true)](https://ci.appveyor.com/project/NickLydon/smtpinmemory/branch/master)

[![NuGet version](https://badge.fury.io/nu/SmtpInMemory.svg)](https://badge.fury.io/nu/SmtpInMemory)


# SmtpInMemory
Simple SMTP server - can query received emails in memory

##In F#

    let port = 9000
    let server = SMTP.Server(port) //port is optional - will default to 25
    //send emails
    let emails = server.GetEmails()

##In C#

    var port = 9000;
    var server = new SMTP.Server(port); //port is optional - will default to 25
    //send emails
    var emails = server.GetEmails()

##As a proxy email server
The server can forward emails onwards if the hostname and port number of the destination server are provided:

    let proxyPort = 9000
    let destinationServer = { Port = 25; Host = "mail.mycompany.com" }
    let server = SMTP.Server(proxyPort,destinationServer)
    //send emails
    let emails = server.GetEmails()


[Look at the tests](https://github.com/NickLydon/SmtpInMemory/blob/master/Tests/ServerTests.cs) to see emails being sent. [MailKit](https://github.com/jstedfast/MailKit) is the library used to send emails.

[MIT Licensed](https://opensource.org/licenses/MIT)
