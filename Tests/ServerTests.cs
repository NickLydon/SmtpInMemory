using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

using MailKit.Net.Smtp;
using MimeKit;

using NUnit.Framework;

using Server = SMTP.Server;

namespace Tests
{
	public class ServerTests
	{		
		const int TIMEOUT = 3000;

		[Test]
		[Timeout(TIMEOUT)]
		public void Should_return_empty_list_when_no_emails_sent()
		{
			var sut = GetSut ();
				
			var actual = sut.GetEmails();

			Assert.That (actual, Is.Empty);
		}

		[Test]
		[Timeout(TIMEOUT)]
		public async Task Should_return_email_when_email_sent()
		{
			var sut = GetSut ();

			var msg = CreateMessage ();

			var emails = await GetEmails (sut, s => s.GetEmails(), () => SendEmailsAsync (sut, msg));

			var actual = emails.Single();

			AssertEmailsAreEqual (actual, msg);
		}

        [Test]
		[Timeout(TIMEOUT)]
		public async Task Should_raise_email_received_event()
        {
            var sut = GetSut();
            var msg = CreateMessage();

            using (var semaphore = new Semaphore(0,1))
            using (sut.EmailReceived.Subscribe(actual =>
            {
                AssertEmailsAreEqual(actual, msg);
                semaphore.Release();
            }))
            {
                await SendEmailsAsync(sut, msg);

                semaphore.WaitOne();
            }
        }

        [Test]
		[Timeout(TIMEOUT)]
		public async Task Should_return_multiple_emails_when_sent()
		{
			var sut = GetSut ();

			var msg = new MimeMessage ();
			msg.From.Add(new MailboxAddress("","from@a.com"));
			msg.To.Add(new MailboxAddress("","to@b.com"));
			msg.Subject = "subject";
			msg.Body = new TextPart("plain") { Text = "body" };

			var msg2 = new MimeMessage ();
			msg2.From.Add(new MailboxAddress("","from2@a.com"));
			msg2.To.Add(new MailboxAddress("","to2@b.com"));
			msg2.Subject = "subject2";
			msg2.Body = new TextPart("plain") { Text = "body2" };

			var emails = 
				(await GetEmails (sut, s => s.GetEmails(), () => SendEmailsAsync (sut, msg), () => SendEmailsAsync (sut, msg2)))
					.ToList();
				
			Assert.That(emails.Count, Is.EqualTo(2));
			AssertEmailsAreEqual(emails[0], msg2);
			AssertEmailsAreEqual(emails[1], msg);
		}

		[Test]
		[Timeout(TIMEOUT)]
		public async Task Should_be_able_to_reset_email_store()
		{
			var sut = GetSut ();

			var msg = CreateMessage ();

			var emails = 
				(await GetEmails(sut, s => s.GetEmailsAndReset(), () => SendEmailsAsync (sut, msg), () => SendEmailsAsync(sut, msg)))
					.ToList();

			Assert.That(emails.Count, Is.EqualTo(2));
							
			emails = 
				(await GetEmails(sut, s => s.GetEmailsAndReset(), () => SendEmailsAsync (sut, msg))).ToList();
			
			Assert.That(emails.Count, Is.EqualTo(1));
		}	

		[Test]
		[Timeout(TIMEOUT)]
		public async Task Should_return_multiple_emails_when_sent_from_same_connection()
		{
			var sut = GetSut();

			var msg = new MimeMessage ();
			msg.From.Add(new MailboxAddress("","from@a.com"));
			msg.To.Add(new MailboxAddress("","to@b.com"));
			msg.Subject = "subject";
			msg.Body = new TextPart("plain") { Text = "body" };

			var msg2 = new MimeMessage ();
			msg2.From.Add(new MailboxAddress("","from2@a.com"));
			msg2.To.Add(new MailboxAddress("","to2@b.com"));
			msg2.Subject = "subject2";
			msg2.Body = new TextPart("plain") { Text = "body2" };

			using (var countdown = new CountdownEvent(2))
			using (sut.EmailReceived.Subscribe(actual =>
				{					
					countdown.Signal();
				}))
			{
				await SendEmailsAsync (sut, msg, msg2);
				countdown.Wait ();

				var emails = sut.GetEmails ().ToList();

				Assert.That(emails.Count, Is.EqualTo(2));

				AssertEmailsAreEqual(emails[0], msg);
				AssertEmailsAreEqual(emails[1], msg2);
			}
		}

		[Test]
		[Timeout(TIMEOUT)]
		public async Task Should_return_empty_list_when_body_is_empty()
		{
			var sut = GetSut ();

			var msg = CreateMessage (body: string.Empty);

			var emails = await GetEmails(sut, s => s.GetEmails(), () => SendEmailsAsync(sut, msg));

			var actual = emails.Single ();

			Assert.That (actual.Body.Count (), Is.EqualTo (0));
		}

		[Test]
		[Timeout(TIMEOUT)]
		public async Task Should_return_empty_string_when_subject_is_not_provided()
		{
			var sut = GetSut ();

			var msg = CreateMessage (subject:null);

			var emails = await GetEmails (sut, s => s.GetEmails (), () => SendEmailsAsync (sut, msg));

			var actual = emails.Single ();

			Assert.That (actual.Subject, Is.EqualTo (string.Empty));
		}

		[Test]
		[Timeout(TIMEOUT)]
		public async Task Should_return_multiple_from_addresses()
		{
			var sut = GetSut ();

			var msg = CreateMessage ();
			msg.From.Add(new MailboxAddress("","from2@a.com"));
			Assert.That (msg.From.Count, Is.EqualTo (2));

			var emails = await GetEmails (sut, s => s.GetEmails (), () => SendEmailsAsync (sut, msg));

			var actual = emails.Single ();

			AssertEmailsAreEqual(actual, msg);
		}	

		[Test]
		[Timeout(TIMEOUT)]
		public async Task Should_return_multiple_to_addresses()
		{
			var sut = GetSut ();

			var msg = CreateMessage ();
			msg.To.Add(new MailboxAddress("","to2@b.com"));
			Assert.That (msg.To.Count, Is.EqualTo (2));

			var emails = await GetEmails (sut, s => s.GetEmails (), () => SendEmailsAsync (sut, msg));

			var actual = emails.Single ();

			AssertEmailsAreEqual(actual, msg);
		}

		[TestCase("Subject")]
		[TestCase("From")]
		[TestCase("To")]
		[TestCase("Body")]
		[TestCase("Content-Type")]
		[TestCase("MIME-Version")]
		[TestCase("Priority")]
		[TestCase("Date")]
		[Timeout(TIMEOUT)]
		public async Task Should_not_overwrite_headers_from_body(string field)
		{
			var sut = GetSut ();

			var msg = CreateMessage(body: field + ": overwritten");

			var emails = await GetEmails (sut, s => s.GetEmails (), () => SendEmailsAsync (sut, msg));

			var actual = emails.Single ();

			AssertEmailsAreEqual(actual, msg);
		}

		[Test]
		[Timeout(TIMEOUT)]
		public async Task Should_forward_emails()
		{
			var forwardServer = new Server (RandomPortNumber());
			var sut = new Server (RandomPortNumber(), new SMTP.ForwardServerConfig (forwardServer.Port, "localhost"));

			var msg = CreateMessage ();

			var emails = await GetEmails (forwardServer, s => s.GetEmails (), () => SendEmailsAsync (sut, msg));

			var actual = emails.Single ();

			AssertEmailsAreEqual(actual, msg);
		}
			
		private static MimeMessage CreateMessage(string from = "from@a.com", string to = "to@b.com", string subject = "subject", string body = "body")
		{
			var msg = new MimeMessage ();
			msg.From.Add(new MailboxAddress("",from));
			msg.To.Add(new MailboxAddress("",to));
			if(subject != null) msg.Subject = subject;
			msg.Body = new TextPart("plain") { Text = body };
			return msg;
		}
			
		private static void AssertEmailsAreEqual(SMTP.EMail actual, MimeMessage msg)
		{
			var body = msg.GetTextBody (MimeKit.Text.TextFormat.Text);
			var expected = new SMTP.EMail (
				string.IsNullOrEmpty (body) ? new string[0] : new[] { body }, 
				msg.Subject, 
				msg.From.Select (s => s.ToString ()), 
				msg.To.Select (s => s.ToString ()),
				msg.Headers
					.Select (h => h.Field + ": " + h.Value)
					.Concat (msg.Body.Headers.Select (h => h.ToString ())));

			AssertEmailsAreEqual (actual, expected);
		}

		private static async Task SendEmailsAsync (Server sut, params MimeMessage[] msgs)
		{
			using (var client = new SmtpClient ()) 
			{
				await client.ConnectAsync ("localhost", sut.Port, false);
				foreach (var msg in msgs) 
				{
					await client.SendAsync (msg);
				}
				client.Disconnect (true);
			}
		}

		private static void AssertEmailsAreEqual(SMTP.EMail actual, SMTP.EMail expected)
		{
			CollectionAssert.AreEqual (actual.From, expected.From);
			Assert.That(actual.To,Is.EqualTo(expected.To));
			Assert.That(actual.Subject,Is.EqualTo(expected.Subject));
			CollectionAssert.AreEquivalent (expected.Headers, actual.Headers);
			CollectionAssert.AreEqual (expected.Body, actual.Body);
		}

		private static readonly Random Rand = new Random();

		static int RandomPortNumber ()
		{
			return Rand.Next (TIMEOUT, 9001);
		}

		private static Server GetSut ()
		{
			return new Server (RandomPortNumber ());
		}

		private static async Task<IEnumerable<SMTP.EMail>> GetEmails(
			SMTP.Server sut, 
			Func<SMTP.Server, IEnumerable<SMTP.EMail>> getEmailsFunc, 
			params Func<Task>[] sendEmails)
		{
			using (var countdown = new CountdownEvent(sendEmails.Count()))
			using (sut.EmailReceived.Subscribe(actual =>
				{					
					countdown.Signal();
				}))
			{
				foreach (var t in sendEmails)
					await t();
				countdown.Wait ();
				return getEmailsFunc(sut);
			}
		}

	}
}