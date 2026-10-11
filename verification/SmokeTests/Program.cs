using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using ISH.CustomerPortal.API;
using ISH.CustomerPortal.Application.Feedback;
using ISH.CustomerPortal.Infrastructure.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using MimeKit;

internal static class Program
{
    private static async Task Main()
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["FeedbackEmail:Host"] = "smtp.example.test",
            ["FeedbackEmail:Port"] = "587",
            ["FeedbackEmail:Security"] = "StartTls",
            ["FeedbackEmail:Authentication"] = "Password",
            ["FeedbackEmail:Username"] = "sender@example.test",
            ["FeedbackEmail:FromAddress"] = "sender@example.test",
            ["FeedbackEmail:ToAddress"] = "support@example.test",
            ["FeedbackEmail:TimeZoneId"] = "Asia/Kolkata"
        }).Build();

        using (var host = new HostBuilder().ConfigureServices(services =>
            services.AddCustomerPortalFeedbackEmail(configuration)).Build())
        {
            await host.StartAsync(); // Runs ValidateOnStart and exercises the API registration.
            var service = host.Services.GetRequiredService<IFeedbackEmailService>();
            Assert(service is SmtpFeedbackEmailService, "Application interface resolves to SMTP implementation.");
            var options = host.Services.GetRequiredService<IOptionsMonitor<FeedbackEmailOptions>>().CurrentValue;
            var factory = new FeedbackMessageFactory(options);
            var alice = new FeedbackSubmission
            {
                Type = FeedbackType.ReportABug,
                Comments = "Alice-only: <script>alert('x')</script> & {{Year}}\nSecond line",
                SubmittedBy = "alice@example.test",
                SubmittedAt = new DateTimeOffset(2026, 10, 11, 6, 0, 0, TimeSpan.Zero),
                HardwareId = "ALICE-DEVICE"
            };
            var fileBytes = Encoding.UTF8.GetBytes("Alice attachment content");
            alice.Attachments.Add(new FeedbackAttachment { FileName = "details.txt", Content = fileBytes });
            var bob = new FeedbackSubmission
            {
                Type = FeedbackType.GeneralFeedback,
                Comments = "Bob-only feedback",
                SubmittedBy = "bob@example.test",
                SubmittedAt = alice.SubmittedAt,
                HardwareId = "BOB-DEVICE"
            };

            using (var a = factory.Create(alice))
            using (var b = factory.Create(bob))
            {
                Assert(a.MessageId != b.MessageId, "Different submissions have distinct message IDs.");
                Assert(a.HtmlBody.Contains("alice@example.test") && !a.HtmlBody.Contains("bob@example.test"), "Alice's identity is isolated.");
                Assert(b.HtmlBody.Contains("bob@example.test") && !b.HtmlBody.Contains("Alice-only"), "Bob's content is isolated.");
                Assert(a.HtmlBody.Contains("Report a Bug") && b.HtmlBody.Contains("General Feedback"), "Each feedback type is rendered.");
                Assert(a.HtmlBody.Contains("&lt;script&gt;") && !a.HtmlBody.Contains("<script>"), "HTML is encoded.");
                Assert(a.HtmlBody.Contains("{{Year}}<br />Second line"), "User text is not reinterpreted as template tokens.");
                Assert(a.HtmlBody.Contains("11/10/2026, 11:30 AM UTC+05:30"), "Timestamp uses configured time zone.");
                Assert(a.TextBody.Contains("Alice-only") && b.TextBody.Contains("Bob-only"), "Plain-text alternatives carry separate content.");
                Assert(a.To.Mailboxes.Single().Address == "support@example.test", "Recipient comes from server configuration.");
                Assert(a.From.Mailboxes.Single().Address == "sender@example.test", "Sender comes from server configuration.");
                Assert(a.Attachments.Count() == 1 && !b.Attachments.Any(), "Attachments do not leak between submissions.");
                using (var bytes = new MemoryStream())
                {
                    ((MimePart)a.Attachments.Single()).Content.DecodeTo(bytes);
                    Assert(bytes.ToArray().SequenceEqual(fileBytes), "MIME attachment bytes are preserved.");
                }
            }

            alice.Attachments[0].DownloadUrl = "https://example.test/files/42?name=a&version=1";
            using (var linked = factory.Create(alice))
                Assert(linked.HtmlBody.Contains("name=a&amp;version=1"), "Download URLs are attribute-encoded.");
            alice.Attachments[0].DownloadUrl = "javascript:alert(1)";
            Expect<ArgumentException>(() => { using (factory.Create(alice)) { } }, "Unsafe attachment link is rejected.");
            alice.Attachments[0].DownloadUrl = null;
            bob.Comments = " ";
            Expect<ArgumentException>(() => { using (factory.Create(bob)) { } }, "Blank feedback is rejected.");
            bob.Comments = "Bob-only feedback";

            // No secret: this must stop before ConnectAsync, so no SMTP/network test is performed.
            await ExpectAsync<InvalidOperationException>(() => service.SendAsync(bob), "Missing SMTP secret fails before connecting.");
            using (var cancellation = new CancellationTokenSource())
            {
                cancellation.Cancel();
                await ExpectAsync<OperationCanceledException>(() => service.SendAsync(bob, cancellation.Token), "Cancellation is honored before sending.");
            }

            options.Security = MailKit.Security.SecureSocketOptions.None;
            Assert(new FeedbackEmailOptionsValidator().Validate(Options.DefaultName, options).Failed, "Plaintext SMTP configuration is rejected.");
            await host.StopAsync();
        }

        var invalidConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string>
        {
            ["FeedbackEmail:Host"] = "smtp.example.com"
        }).Build();
        using (var invalidHost = new HostBuilder().ConfigureServices(services =>
            services.AddCustomerPortalFeedbackEmail(invalidConfig)).Build())
            await ExpectAsync<OptionsValidationException>(() => invalidHost.StartAsync(), "Invalid configuration is rejected at host startup.");

        Console.WriteLine("PASS: layered compilation, DI/startup options, embedded template, separate users, escaping, MIME attachments, and pre-send validation. No SMTP connection was opened.");
    }

    private static void Assert(bool condition, string message)
    {
        if (!condition) throw new Exception("FAILED: " + message);
        Console.WriteLine("PASS: " + message);
    }

    private static void Expect<T>(Action action, string message) where T : Exception
    {
        try { action(); }
        catch (T) { Console.WriteLine("PASS: " + message); return; }
        throw new Exception("FAILED: " + message);
    }

    private static async Task ExpectAsync<T>(Func<Task> action, string message) where T : Exception
    {
        try { await action(); }
        catch (T) { Console.WriteLine("PASS: " + message); return; }
        throw new Exception("FAILED: " + message);
    }
}
