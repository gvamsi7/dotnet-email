using System;
using System.Configuration;
using System.Globalization;
using MailKit.Security;

namespace FeedbackEmail
{
    public enum SmtpAuthentication
    {
        Password,
        OAuth2,
        None
    }

    public sealed class FeedbackEmailOptions
    {
        public string Host { get; set; }
        public int Port { get; set; } = 587;
        public SecureSocketOptions Security { get; set; } = SecureSocketOptions.StartTls;
        public SmtpAuthentication Authentication { get; set; } = SmtpAuthentication.Password;
        public string Username { get; set; }
        public int TimeoutSeconds { get; set; } = 60;
        public string FromAddress { get; set; }
        public string FromName { get; set; } = "Industrial Services Hub";
        public string ToAddress { get; set; }
        public string ApplicationName { get; set; } = "Industrial Services Hub";
        public string CompanyName { get; set; } = "Schneider Electric";
        public string CopyrightOwner { get; set; } = "Schneider Electric Industries SAS";
        public string TimeZoneId { get; set; } = "India Standard Time";
        public string LogoFilePath { get; set; }
        public string TermsUrl { get; set; }
        public string PrivacyUrl { get; set; }

        public static FeedbackEmailOptions FromConfiguration()
        {
            return new FeedbackEmailOptions
            {
                Host = Read("Smtp.Host"),
                Port = int.Parse(Read("Smtp.Port", "587"), CultureInfo.InvariantCulture),
                Security = (SecureSocketOptions)Enum.Parse(typeof(SecureSocketOptions),
                    Read("Smtp.Security", "StartTls"), true),
                Authentication = (SmtpAuthentication)Enum.Parse(typeof(SmtpAuthentication),
                    Read("Smtp.Authentication", "Password"), true),
                Username = Read("Smtp.Username"),
                TimeoutSeconds = int.Parse(Read("Smtp.TimeoutSeconds", "60"), CultureInfo.InvariantCulture),
                FromAddress = Read("Email.FromAddress"),
                FromName = Read("Email.FromName", "Industrial Services Hub"),
                ToAddress = Read("Email.ToAddress"),
                ApplicationName = Read("Email.ApplicationName", "Industrial Services Hub"),
                CompanyName = Read("Email.CompanyName", "Schneider Electric"),
                CopyrightOwner = Read("Email.CopyrightOwner", "Schneider Electric Industries SAS"),
                TimeZoneId = Read("Email.TimeZoneId", "India Standard Time"),
                LogoFilePath = Read("Email.LogoFilePath"),
                TermsUrl = Read("Email.TermsUrl"),
                PrivacyUrl = Read("Email.PrivacyUrl")
            };
        }

        private static string Read(string key, string fallback = "")
        {
            return ConfigurationManager.AppSettings[key] ?? fallback;
        }
    }
}
