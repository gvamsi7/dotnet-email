using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

namespace FeedbackEmail
{
    internal static class Program
    {
        private static int Main(string[] args)
        {
            try
            {
                return RunAsync(args).GetAwaiter().GetResult();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("Feedback email failed: " + ex.Message);
                return 1;
            }
        }

        private static async Task<int> RunAsync(string[] args)
        {
            if (args.Length > 1 || (args.Length == 1 && args[0] != "--preview" && args[0] != "--send"))
            {
                Console.Error.WriteLine("Usage: FeedbackEmail.exe [--preview | --send]");
                return 2;
            }

            var options = FeedbackEmailOptions.FromConfiguration();
            var feedback = new FeedbackSubmission
            {
                Type = FeedbackType.GeneralFeedback,
                Comments = "The feedback form is easy to use.\nPlease show a confirmation after submitting feedback.",
                SubmittedAt = DateTimeOffset.UtcNow,
                SubmittedBy = "user@example.com",
                HardwareId = "ISH-DEVICE-001-DF3456HJ77TYIO455"
            };
            feedback.Attachments.Add(new FeedbackAttachment
            {
                FileName = "feedback-context.txt",
                Content = Encoding.UTF8.GetBytes("Sample feedback attachment. Replace this with your validated upload bytes.")
            });

            if (args.Length == 1 && args[0] == "--send")
            {
                var messageId = await new FeedbackEmailService(options).SendAsync(feedback);
                Console.WriteLine("SMTP server accepted the message. Message ID: " + messageId);
                return 0;
            }

            // Default: create local previews without connecting to an SMTP server.
            var directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Preview");
            Directory.CreateDirectory(directory);
            using (var message = new FeedbackMessageFactory(options).Create(feedback))
            {
                message.WriteTo(Path.Combine(directory, "feedback-preview.eml"));
                // HTML preview uses the text brand; EML includes your CID logo if configured.
                var rendered = new FeedbackTemplateRenderer().Render(feedback, options);
                File.WriteAllText(Path.Combine(directory, "feedback-preview.html"), rendered.Html, Encoding.UTF8);
            }
            Console.WriteLine("Preview files created in: " + directory);
            Console.WriteLine("Open the EML in an email client to inspect attachments and an embedded logo.");
            return 0;
        }
    }
}
