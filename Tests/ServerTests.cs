using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

using MailKit.Net.Smtp;
using MimeKit;

using NUnit.Framework;

using Server = SMTP.Server;

namespace Tests
{
	public class ServerTests
	{		
		[Test]
		public void Should_return_empty_list_when_no_emails_sent()
		{
			var sut = GetSut ();
				
			var actual = sut.GetEmails();

			Assert.That (actual, Is.Empty);
		}

		[Test]
		public async Task Should_return_email_when_email_sent()
		{
			var sut = GetSut ();

			var msg = CreateMessage ();
	
			await SendEmailsAsync (sut, msg);

			var emails = await BlockReadingEmails (sut);

			var actual = emails.Single();

			AssertEmailsAreEqual (actual, msg);
		}

		[Test]
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

			await SendEmailsAsync (sut, msg);

			await SendEmailsAsync (sut, msg2);

			var emails = (await BlockReadingEmails(sut, emailCount: 2)).ToList();

			Assert.That(emails.Count, Is.EqualTo(2));
			AssertEmailsAreEqual(emails[0], msg2);
			AssertEmailsAreEqual(emails[1], msg);
		}			

		[Test]
		public async Task Should_be_able_to_reset_email_store()
		{
			var sut = GetSut ();

			var msg = CreateMessage ();

			await SendEmailsAsync (sut, msg, msg);

			var emails = (await BlockReadingAndResettingEmails(sut, emailCount: 2)).ToList();
			Assert.That(emails.Count, Is.EqualTo(2));

			await SendEmailsAsync (sut, msg);
				
			emails = (await BlockReadingAndResettingEmails(sut)).ToList();
			Assert.That(emails.Count, Is.EqualTo(1));
		}	

		[Test]
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

			await SendEmailsAsync (sut, msg, msg2);

			var emails = (await BlockReadingEmails(sut, emailCount: 2)).ToList();

			Assert.That(emails.Count, Is.EqualTo(2));

			AssertEmailsAreEqual(emails[0], msg);
			AssertEmailsAreEqual(emails[1], msg2);
		}

		[Test]
		public async Task Should_return_empty_list_when_body_is_empty()
		{
			var sut = GetSut ();

			var msg = CreateMessage (body: string.Empty);

			await SendEmailsAsync (sut, msg);

			var emails = await BlockReadingEmails (sut);

			var actual = emails.Single ();

			Assert.That (actual.Body.Count (), Is.EqualTo (0));
		}

		[Test]
		public async Task Should_return_multiple_from_addresses()
		{
			var sut = GetSut ();

			var msg = CreateMessage ();
			msg.From.Add(new MailboxAddress("","from2@a.com"));
			Assert.That (msg.From.Count, Is.EqualTo (2));

			await SendEmailsAsync (sut, msg);

			var emails = await BlockReadingEmails (sut);

			var actual = emails.Single ();

			AssertEmailsAreEqual(actual, msg);
		}	

		[Test]
		public async Task Should_return_multiple_to_addresses()
		{
			var sut = GetSut ();

			var msg = CreateMessage ();
			msg.To.Add(new MailboxAddress("","to2@b.com"));
			Assert.That (msg.To.Count, Is.EqualTo (2));

			await SendEmailsAsync (sut, msg);

			var emails = await BlockReadingEmails (sut);

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
		public async Task Should_not_overwrite_headers_from_body(string field)
		{
			var sut = GetSut ();

			var msg = CreateMessage(body: field + ": overwritten");

			await SendEmailsAsync (sut, msg);

			var emails = await BlockReadingEmails (sut);

			var actual = emails.Single ();

			AssertEmailsAreEqual(actual, msg);
		}

		private static MimeMessage CreateMessage(string from = "from@a.com", string to = "to@b.com", string subject = "subject", string body = "body")
		{
			var msg = new MimeMessage ();
			msg.From.Add(new MailboxAddress("",from));
			msg.To.Add(new MailboxAddress("",to));
			msg.Subject = subject;
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

		private static Task<IEnumerable<SMTP.EMail>> BlockReadingEmails(Server sut, int emailCount = 1, int retryCount = 1)
		{
			return BlockReadingEmails (sut.GetEmails, emailCount: emailCount, retryCount: retryCount);
		}

		private static Task<IEnumerable<SMTP.EMail>> BlockReadingAndResettingEmails(Server sut, int emailCount = 1, int retryCount = 1)
		{
			return BlockReadingEmails (sut.GetEmailsAndReset, emailCount: emailCount, retryCount: retryCount);
		}

		private static async Task<IEnumerable<SMTP.EMail>> BlockReadingEmails(Func<IEnumerable<SMTP.EMail>> sut, int emailCount = 1, int retryCount = 1)
		{
			if (retryCount < 0)
				throw new Exception ("Emails weren't found within the given retryCount");

			var emails = sut();

			if (emails.Count() >= emailCount)
				return emails;

			await Task.Delay (50);
			return await BlockReadingEmails (sut, emailCount, retryCount - 1);
		}

		private static readonly Random Rand = new Random();
		private static Server GetSut ()
		{
			return new Server (Rand.Next(1000,9001));
		}

	}
}