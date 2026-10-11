using MailKit.Security;

namespace ISH.CustomerPortal.Infrastructure.Email
{
    public enum SmtpAuthentication
    {
        Password,
        OAuth2,
        None
    }

    // Bind from API/appsettings.json: FeedbackEmail.
    // Secrets can be supplied by environment variables, user secrets, or your existing vault.
    public sealed class FeedbackEmailOptions
    {
        public const string SectionName = "FeedbackEmail";
        public string Host { get; set; }
        public int Port { get; set; } = 587;
        public SecureSocketOptions Security { get; set; } = SecureSocketOptions.StartTls;
        public SmtpAuthentication Authentication { get; set; } = SmtpAuthentication.Password;
        public string Username { get; set; }
        public string Password { get; set; }
        public string AccessToken { get; set; }
        public int TimeoutSeconds { get; set; } = 60;
        public string FromAddress { get; set; }
        public string FromName { get; set; } = "Industrial Services Hub";
        public string ToAddress { get; set; }
        public string ApplicationName { get; set; } = "Industrial Services Hub";
        public string CompanyName { get; set; } = "Schneider Electric";
        public string CopyrightOwner { get; set; } = "Schneider Electric Industries SAS";
        public string TimeZoneId { get; set; } = "Asia/Kolkata";
        public string LogoFilePath { get; set; }
        public string TermsUrl { get; set; }
        public string PrivacyUrl { get; set; }
    }
}
