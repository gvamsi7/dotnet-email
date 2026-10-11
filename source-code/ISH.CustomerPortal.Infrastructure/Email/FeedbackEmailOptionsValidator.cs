using System;
using System.Collections.Generic;
using System.Linq;
using MailKit.Security;
using Microsoft.Extensions.Options;
using MimeKit;

namespace ISH.CustomerPortal.Infrastructure.Email
{
    public sealed class FeedbackEmailOptionsValidator : IValidateOptions<FeedbackEmailOptions>
    {
        public ValidateOptionsResult Validate(string name, FeedbackEmailOptions options)
        {
            if (options == null) return ValidateOptionsResult.Fail("FeedbackEmail settings are missing.");
            var errors = new List<string>();
            if (string.IsNullOrWhiteSpace(options.Host) || options.Host == "smtp.example.com")
                errors.Add("FeedbackEmail:Host must be your SMTP server hostname.");
            if (options.Port < 1 || options.Port > 65535)
                errors.Add("FeedbackEmail:Port must be between 1 and 65535.");
            if (options.Security != SecureSocketOptions.StartTls && options.Security != SecureSocketOptions.SslOnConnect)
                errors.Add("FeedbackEmail:Security must be StartTls or SslOnConnect.");
            if (!Enum.IsDefined(typeof(SmtpAuthentication), options.Authentication))
                errors.Add("FeedbackEmail:Authentication must be Password, OAuth2, or None.");
            if (options.Authentication != SmtpAuthentication.None && string.IsNullOrWhiteSpace(options.Username))
                errors.Add("FeedbackEmail:Username is required for authentication.");
            if (options.TimeoutSeconds < 1 || options.TimeoutSeconds > 300)
                errors.Add("FeedbackEmail:TimeoutSeconds must be between 1 and 300.");
            if (!IsEmail(options.FromAddress)) errors.Add("FeedbackEmail:FromAddress must contain one email address.");
            if (!IsEmail(options.ToAddress)) errors.Add("FeedbackEmail:ToAddress must contain one email address.");
            try
            {
                if (string.IsNullOrWhiteSpace(options.TimeZoneId)) throw new ArgumentException();
                TimeZoneInfo.FindSystemTimeZoneById(options.TimeZoneId);
            }
            catch (Exception ex) when (ex is ArgumentException || ex is TimeZoneNotFoundException || ex is InvalidTimeZoneException)
            {
                errors.Add("FeedbackEmail:TimeZoneId must identify a time zone available on the server.");
            }
            foreach (var url in new[] { options.TermsUrl, options.PrivacyUrl })
            {
                if (string.IsNullOrWhiteSpace(url)) continue;
                try { FeedbackTemplateRenderer.RequireHttps(url); }
                catch (ArgumentException) { errors.Add("FeedbackEmail policy links must be absolute HTTPS URLs."); }
            }
            // Passwords/tokens are checked immediately before sending, so a token provider
            // can acquire/refresh credentials at runtime. No secret values enter errors.
            return errors.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(errors);
        }

        private static bool IsEmail(string value)
        {
            MailboxAddress mailbox;
            return !string.IsNullOrWhiteSpace(value) && !value.Any(char.IsControl) &&
                MailboxAddress.TryParse(value, out mailbox) &&
                string.Equals(mailbox.Address, value.Trim(), StringComparison.OrdinalIgnoreCase) &&
                mailbox.Address.IndexOf('@') > 0;
        }
    }
}
