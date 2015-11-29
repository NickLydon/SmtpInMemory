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

			var msg = new MimeMessage ();
			msg.From.Add(new MailboxAddress("","from@a.com"));
			msg.To.Add(new MailboxAddress("","to@b.com"));
			msg.Subject = "subject";
			msg.Body = new TextPart("plain") { Text = "body" };
	
			using (var client = new SmtpClient ()) 
			{
				await client.ConnectAsync ("localhost", sut.Port, false);

				await client.SendAsync (msg);

				client.Disconnect (true);
			}

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

			using (var client = new SmtpClient ()) 
			{
				await client.ConnectAsync ("localhost", sut.Port, false);

				await client.SendAsync (msg);

				client.Disconnect (true);
			}

			using (var client = new SmtpClient ()) 
			{
				await client.ConnectAsync ("localhost", sut.Port, false);

				await client.SendAsync (msg2);

				client.Disconnect (true);
			}

			var emails = (await BlockReadingEmails(sut, emailCount: 2)).ToList();

			Assert.That(emails.Count, Is.EqualTo(2));
			AssertEmailsAreEqual(emails[0], msg2);
			AssertEmailsAreEqual(emails[1], msg);
		}			

		[Test]
		public async Task Should_be_able_to_reset_email_store()
		{
			var sut = GetSut ();

			var msg = new MimeMessage ();
			msg.From.Add(new MailboxAddress("","from@a.com"));
			msg.To.Add(new MailboxAddress("","to@b.com"));
			msg.Subject = "subject";
			msg.Body = new TextPart("plain") { Text = "body" };

			using (var client = new SmtpClient ()) 
			{
				await client.ConnectAsync ("localhost", sut.Port, false);

				await client.SendAsync (msg);
				await client.SendAsync (msg);

				client.Disconnect (true);
			}

			var emails = (await BlockReadingAndResettingEmails(sut, emailCount: 2)).ToList();
			Assert.That(emails.Count, Is.EqualTo(2));

			using (var client = new SmtpClient ()) 
			{
				await client.ConnectAsync ("localhost", sut.Port, false);

				await client.SendAsync (msg);

				client.Disconnect (true);
			}
				
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

			using (var client = new SmtpClient ()) 
			{
				await client.ConnectAsync ("localhost", sut.Port, false);

				await client.SendAsync (msg);
				await client.SendAsync (msg2);

				client.Disconnect (true);
			}

			var emails = (await BlockReadingEmails(sut, emailCount: 2)).ToList();

			Assert.That(emails.Count, Is.EqualTo(2));

			AssertEmailsAreEqual(emails[0], msg);
			AssertEmailsAreEqual(emails[1], msg2);
		}

		[Test]
		public async Task Should_return_empty_list_when_body_is_empty()
		{
			var sut = GetSut ();

			var msg = new MimeMessage ();
			msg.From.Add(new MailboxAddress("","from@a.com"));
			msg.To.Add(new MailboxAddress("","to@b.com"));
			msg.Subject = "subject";
			msg.Body = new TextPart("plain") { Text = string.Empty };

			using (var client = new SmtpClient ()) 
			{
				await client.ConnectAsync ("localhost", sut.Port, false);

				await client.SendAsync (msg);

				client.Disconnect (true);
			}

			var emails = await BlockReadingEmails (sut);

			var actual = emails.Single ();

			Assert.That (actual.Body.Count (), Is.EqualTo (0));
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

			var msg = new MimeMessage ();
			msg.From.Add(new MailboxAddress("","from@a.com"));
			msg.To.Add(new MailboxAddress("","to@b.com"));
			msg.Subject = "subject";
			msg.Body = new TextPart("plain") { Text = field + ": overwritten" };

			using (var client = new SmtpClient ()) 
			{
				await client.ConnectAsync ("localhost", sut.Port, false);

				await client.SendAsync (msg);

				client.Disconnect (true);
			}

			var emails = await BlockReadingEmails (sut);

			var actual = emails.Single ();

			AssertEmailsAreEqual(actual, msg);
		}
			
		private static void AssertEmailsAreEqual(SMTP.EMail actual, MimeMessage msg)
		{
			var body = msg.GetTextBody (MimeKit.Text.TextFormat.Text);
			var expected = new SMTP.EMail (
				string.IsNullOrEmpty(body) ? new string[0] : new[] { body }, 
				msg.Subject, 
				msg.From.Single().ToString(), 
				msg.To.Single().ToString(),
				new string[] { }); 

			AssertEmailsAreEqual (actual, expected);
		}

		private static void AssertEmailsAreEqual(SMTP.EMail actual, SMTP.EMail expected)
		{
			Assert.That(actual.From,Is.EqualTo(expected.From));
			Assert.That(actual.To,Is.EqualTo(expected.To));
			Assert.That(actual.Subject,Is.EqualTo(expected.Subject));
			Console.WriteLine (actual.Headers);
			Console.WriteLine (expected.Headers);
			//TODO: Get this line to work
			//			CollectionAssert.AreEqual (expected.Headers, actual.Headers);
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