using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

namespace FeedbackEmail
{
    public sealed class RenderedFeedback
    {
        public string Html { get; internal set; }
        public string Text { get; internal set; }
    }

    public sealed class FeedbackTemplateRenderer
    {
        private static readonly Regex Tokens = new Regex(@"\{\{([A-Za-z]+)\}\}", RegexOptions.Compiled);
        private readonly string template;

        public FeedbackTemplateRenderer()
        {
            using (var stream = typeof(FeedbackTemplateRenderer).Assembly
                .GetManifestResourceStream("FeedbackEmail.Templates.Feedback.html"))
            {
                if (stream == null)
                    throw new InvalidOperationException("Embed Templates/Feedback.html in the project.");
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                    template = reader.ReadToEnd();
            }
        }

        public RenderedFeedback Render(FeedbackSubmission feedback, FeedbackEmailOptions options,
            string logoContentId = null)
        {
            if (feedback == null) throw new ArgumentNullException(nameof(feedback));
            if (options == null) throw new ArgumentNullException(nameof(options));

            var localTime = TimeZoneInfo.ConvertTime(feedback.SubmittedAt,
                TimeZoneInfo.FindSystemTimeZoneById(options.TimeZoneId));
            var timestamp = localTime.ToString("dd/MM/yyyy, hh:mm tt 'UTC'zzz", CultureInfo.InvariantCulture);
            var type = feedback.Type == FeedbackType.ReportABug ? "Report a Bug" : "General Feedback";
            var hardware = string.IsNullOrWhiteSpace(feedback.HardwareId) ? "Not provided" : feedback.HardwareId;
            var brand = string.IsNullOrEmpty(logoContentId)
                ? "<span style=\"font-size:20px;line-height:23px;font-weight:bold;color:#ffffff;\">" +
                  Encode(options.CompanyName) + "</span>"
                : "<img src=\"cid:" + Encode(logoContentId) + "\" alt=\"" + Encode(options.CompanyName) +
                  "\" width=\"142\" style=\"display:block;width:142px;max-width:100%;height:auto;border:0;color:#ffffff;\" />";

            var attachmentsHtml = feedback.Attachments.Count == 0
                ? "<span style=\"color:#747b78;\">No attachments</span>"
                : string.Join("<br />", feedback.Attachments.Select(a =>
                    string.IsNullOrWhiteSpace(a.DownloadUrl)
                        ? Encode(a.FileName) + " <span style=\"color:#747b78;font-size:12px;\">(attached)</span>"
                        : "<a href=\"" + Encode(RequireHttps(a.DownloadUrl)) +
                          "\" style=\"color:#357c92;text-decoration:underline;\">" + Encode(a.FileName) + "</a>"));

            var values = new Dictionary<string, string>
            {
                ["Brand"] = brand,
                ["ApplicationName"] = Encode(options.ApplicationName),
                ["FeedbackType"] = Encode(type),
                ["Comments"] = Encode(feedback.Comments).Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "<br />"),
                ["Timestamp"] = Encode(timestamp),
                ["SubmittedBy"] = Encode(feedback.SubmittedBy),
                ["Attachments"] = attachmentsHtml,
                ["HardwareId"] = Encode(hardware),
                ["Terms"] = FooterLink("Terms of Use", options.TermsUrl),
                ["Privacy"] = FooterLink("Privacy policy", options.PrivacyUrl),
                ["Year"] = localTime.Year.ToString(CultureInfo.InvariantCulture),
                ["CopyrightOwner"] = Encode(options.CopyrightOwner)
            };

            // Replace once, so text such as {{Year}} inside a comment stays literal.
            var html = Tokens.Replace(template, match =>
            {
                string value;
                if (!values.TryGetValue(match.Groups[1].Value, out value))
                    throw new InvalidOperationException("Unknown email template token: " + match.Value);
                return value;
            });

            var text = new StringBuilder()
                .AppendLine(options.CompanyName + " | " + options.ApplicationName)
                .AppendLine().AppendLine("Feedback").AppendLine()
                .AppendLine("Feedback type: " + type)
                .AppendLine("Feedback Comments: " + feedback.Comments)
                .AppendLine().AppendLine("Timestamp: " + timestamp)
                .AppendLine("Submitted By: " + feedback.SubmittedBy)
                .AppendLine("Attachments: " + (feedback.Attachments.Count == 0 ? "No attachments" :
                    string.Join(", ", feedback.Attachments.Select(a => a.FileName +
                        (string.IsNullOrWhiteSpace(a.DownloadUrl) ? " (attached)" : " (" + a.DownloadUrl + ")")))))
                .AppendLine("Hardware ID: " + hardware).AppendLine();
            if (!string.IsNullOrWhiteSpace(options.TermsUrl))
                text.AppendLine("Terms of Use: " + RequireHttps(options.TermsUrl));
            if (!string.IsNullOrWhiteSpace(options.PrivacyUrl))
                text.AppendLine("Privacy policy: " + RequireHttps(options.PrivacyUrl));
            text.AppendLine("This is an autogenerated e-mail. Please do not reply.")
                .AppendLine("Copyright " + localTime.Year + " " + options.CopyrightOwner + ". All rights reserved.");

            return new RenderedFeedback { Html = html, Text = text.ToString() };
        }

        private static string FooterLink(string label, string url)
        {
            return string.IsNullOrWhiteSpace(url)
                ? Encode(label)
                : "<a href=\"" + Encode(RequireHttps(url)) +
                  "\" style=\"color:#357c92;text-decoration:underline;\">" + Encode(label) + "</a>";
        }

        internal static string RequireHttps(string value)
        {
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) ||
                uri.Scheme != Uri.UriSchemeHttps || string.IsNullOrEmpty(uri.Host) ||
                !string.IsNullOrEmpty(uri.UserInfo))
                throw new ArgumentException("Email download and footer links must be absolute HTTPS URLs.");
            return uri.AbsoluteUri;
        }

        private static string Encode(string value)
        {
            return WebUtility.HtmlEncode(value ?? string.Empty);
        }
    }
}
