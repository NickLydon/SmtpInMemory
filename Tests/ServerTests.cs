using System;
using NUnit;
using NUnit.Framework;
using Server = SMTP.Server;
using System.Collections.Generic;
using System.Net.Mail;
using System.Linq;
using System.Threading.Tasks;

namespace Tests
{
	public class ServerTests
	{
		private static readonly Random rand = new Random();
		private static Server GetSut ()
		{
			return new Server (rand.Next(1000,9001));
		}

		private static async Task<IEnumerable<SMTP.EMail>> BlockReadingEmails(Server sut, int emailCount = 1, int retryCount = 1)
		{
			if (retryCount < 0)
				throw new Exception ("Emails weren't found within the given retryCount");
			
			var emails = sut.GetEmails ();

			if (emails.Count() >= emailCount)
				return emails;
			
			await Task.Delay (100).ConfigureAwait(false);
			return await BlockReadingEmails (sut, retryCount: retryCount - 1).ConfigureAwait(false);
		}

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
			var sentEmail = new MailMessage ("from@a.com", "to@b.com", "subject", "body");

			using (var client = new SmtpClient ("localhost",sut.Port)) 
			{
				client.Send (sentEmail);
			}

			var emails = await BlockReadingEmails (sut);

			var actual = emails.Single();

			var expected = new SMTP.EMail (
				new[] { sentEmail.Body }, 
				sentEmail.Subject, 
				sentEmail.From.ToString(), 
				sentEmail.To.ToString(),
				new Tuple<string,string>[] { });

			Assert.That(actual.From,Is.EqualTo(expected.From));
			Assert.That(actual.To,Is.EqualTo(expected.To));
			Assert.That(actual.Subject,Is.EqualTo(expected.Subject));
			CollectionAssert.AreEqual (expected.Body, actual.Body);
		}

		private static void AssertEmailsAreEqual(SMTP.EMail actual, MailMessage sentEmail)
		{
			var expected = new SMTP.EMail (
               new[] { sentEmail.Body }, 
               sentEmail.Subject, 
               sentEmail.From.ToString (), 
               sentEmail.To.ToString (),
               sentEmail.Headers.AllKeys.Select (k => Tuple.Create (k, sentEmail.Headers.Get (k))).ToArray ());

			foreach (var header in actual.Headers) {
				Console.WriteLine ("Name: {0}; Value: {1}", header.Item1, header.Item2);
			}

			Assert.That(actual.From,Is.EqualTo(expected.From));
			Assert.That(actual.To,Is.EqualTo(expected.To));
			Assert.That(actual.Subject,Is.EqualTo(expected.Subject));
			Console.WriteLine (actual.Headers);
			Console.WriteLine (expected.Headers);
			//TODO: Get this line to work
//			CollectionAssert.AreEqual (expected.Headers, actual.Headers);
			CollectionAssert.AreEqual (expected.Body, actual.Body);
		}

		[Test]
		public async Task Should_return_multiple_emails_when_sent()
		{
			var sut = GetSut ();
			using (var sentEmail = new MailMessage ("from@a.com", "to@b.com", "subject", "body"))
			using (var sentEmail2 = new MailMessage ("from2@a.com", "to2@b.com", "subject2", "body2"))
			using (var client = new SmtpClient ("localhost", sut.Port)) {				
				client.Send (sentEmail);
				client.Send (sentEmail2);			
				
				var emails = (await BlockReadingEmails (sut, emailCount: 2).ConfigureAwait (false)).ToList ();

				Assert.That (emails.Count, Is.EqualTo (2));

				AssertEmailsAreEqual (emails [0], sentEmail2);
				AssertEmailsAreEqual (emails [1], sentEmail);	
			}
		}

		[Test]
		public async Task Should_not_include_empty_lines_in_body()
		{
			var sut = GetSut ();
			using (var sentEmail = new MailMessage ("from@a.com", "to@b.com", "subject", string.Empty))
			using (var client = new SmtpClient ("localhost", sut.Port)) {
				client.Send (sentEmail);			

				var emails = await BlockReadingEmails (sut);

				var actual = emails.Single ();

				var expected = new SMTP.EMail (
					new string[0], 
					sentEmail.Subject, 
					sentEmail.From.ToString (), 
					sentEmail.To.ToString (),
					new Tuple<string,string>[] { });

				Assert.That (actual.From, Is.EqualTo (expected.From));
				Assert.That (actual.To, Is.EqualTo (expected.To));
				Assert.That (actual.Subject, Is.EqualTo (expected.Subject));
				CollectionAssert.AreEqual (expected.Body, actual.Body);
			}
		}

		[TestCase("Subject")]
		[TestCase("From")]
		[TestCase("To")]
		[TestCase("Body")]
		[TestCase("Content-Type")]
		[TestCase("MIME-Version")]
		[TestCase("Priority")]
		[TestCase("Date")]
		public async Task Should_not_overwrite_fields_from_body(string field)
		{
			var sut = GetSut ();
			using (var sentEmail = new MailMessage ("from@a.com", "to@b.com", "subject", field + ": overwritten"))
			using (var client = new SmtpClient ("localhost", sut.Port)) 
			{
				client.Send (sentEmail);			

				var emails = await BlockReadingEmails (sut);

				var actual = emails.Single ();

				var expected = new SMTP.EMail (
					              new[] { sentEmail.Body }, 
					              sentEmail.Subject, 
					              sentEmail.From.ToString (), 
					              sentEmail.To.ToString (),
					              new Tuple<string,string>[] { });

				Console.WriteLine (actual.Body);
				Assert.That (actual.From, Is.EqualTo (expected.From));
				Assert.That (actual.To, Is.EqualTo (expected.To));
				Assert.That (actual.Subject, Is.EqualTo (expected.Subject));
				CollectionAssert.AreEqual (expected.Body, actual.Body);
			}
		}
	}
}