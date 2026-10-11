using ISH.CustomerPortal.Application.Feedback;
using System;
using System.IO;
using System.Linq;
using MimeKit;
using MimeKit.Utils;

namespace ISH.CustomerPortal.Infrastructure.Email
{
    public sealed class FeedbackMessageFactory
    {
        // Raw file budget. Base64/MIME overhead adds about one third on the wire.
        private const long MaxAttachmentBytes = 10L * 1024 * 1024;
        private readonly FeedbackEmailOptions options;
        private readonly FeedbackTemplateRenderer renderer = new FeedbackTemplateRenderer();

        public FeedbackMessageFactory(FeedbackEmailOptions options)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
        }

        // Caller owns and disposes the returned message.
        public MimeMessage Create(FeedbackSubmission feedback)
        {
            ValidateSubmission(feedback);
            var from = ParseEmail(options.FromAddress, "FromAddress");
            var to = ParseEmail(options.ToAddress, "ToAddress");
            var body = new BodyBuilder();
            string logoContentId = null;

            if (!string.IsNullOrWhiteSpace(options.LogoFilePath))
            {
                var logo = new FileInfo(options.LogoFilePath);
                if (!logo.Exists) throw new FileNotFoundException("Configured logo was not found.");
                if (logo.Length > 1024 * 1024) throw new ArgumentException("Use a logo smaller than 1 MiB.");
                var extension = logo.Extension.ToLowerInvariant();
                if (extension != ".png" && extension != ".jpg" && extension != ".jpeg")
                    throw new ArgumentException("The embedded logo must be a PNG or JPG.");
                var resource = body.LinkedResources.Add(logo.Name, File.ReadAllBytes(logo.FullName));
                resource.ContentId = MimeUtils.GenerateMessageId();
                logoContentId = resource.ContentId;
            }

            var rendered = renderer.Render(feedback, options, logoContentId);
            body.HtmlBody = rendered.Html;
            body.TextBody = rendered.Text;
            foreach (var attachment in feedback.Attachments)
                body.Attachments.Add(attachment.FileName, attachment.Content);

            var message = new MimeMessage
            {
                Subject = "Feedback: New Feedback has been submitted",
                Date = DateTimeOffset.UtcNow,
                MessageId = MimeUtils.GenerateMessageId(),
                Body = body.ToMessageBody()
            };
            message.From.Add(new MailboxAddress(options.FromName ?? string.Empty, from.Address));
            message.To.Add(to);
            // Sender and destination come from server configuration, never the form.
            message.Headers.Add("Auto-Submitted", "auto-generated");
            return message;
        }

        public static void ValidateSubmission(FeedbackSubmission feedback)
        {
            if (feedback == null) throw new ArgumentNullException(nameof(feedback));
            if (!Enum.IsDefined(typeof(FeedbackType), feedback.Type))
                throw new ArgumentException("Choose GeneralFeedback or ReportABug.");
            if (string.IsNullOrWhiteSpace(feedback.Comments) || feedback.Comments.Length > 10000)
                throw new ArgumentException("Feedback comments must contain 1 to 10,000 characters.");
            if (feedback.SubmittedAt == default(DateTimeOffset))
                throw new ArgumentException("A submission timestamp is required.");
            ParseEmail(feedback.SubmittedBy, "SubmittedBy");
            if (feedback.HardwareId != null && feedback.HardwareId.Length > 512)
                throw new ArgumentException("Hardware ID must be 512 characters or fewer.");
            if (feedback.Attachments.Count > 5)
                throw new ArgumentException("A maximum of 5 attachments is supported.");

            long total = 0;
            foreach (var attachment in feedback.Attachments)
            {
                if (attachment == null || string.IsNullOrWhiteSpace(attachment.FileName) ||
                    attachment.FileName.Length > 200 || attachment.FileName.Any(char.IsControl) ||
                    attachment.FileName.IndexOfAny(new[] { '/', '\\', ':' }) >= 0 ||
                    attachment.FileName == "." || attachment.FileName == ".." ||
                    attachment.Content == null || attachment.Content.Length == 0)
                    throw new ArgumentException("Each attachment needs a simple filename and non-empty content.");
                total += attachment.Content.LongLength;
                if (total > MaxAttachmentBytes)
                    throw new ArgumentException("Combined attachment size must not exceed 10 MiB.");
                if (!string.IsNullOrWhiteSpace(attachment.DownloadUrl))
                    FeedbackTemplateRenderer.RequireHttps(attachment.DownloadUrl);
            }
        }

        private static MailboxAddress ParseEmail(string value, string field)
        {
            MailboxAddress mailbox;
            if (string.IsNullOrWhiteSpace(value) || value.Any(char.IsControl) ||
                !MailboxAddress.TryParse(value, out mailbox) ||
                !string.Equals(mailbox.Address, value.Trim(), StringComparison.OrdinalIgnoreCase) ||
                mailbox.Address.IndexOf('@') <= 0)
                throw new ArgumentException(field + " must contain one email address without a display name.");
            return mailbox;
        }
    }
}
