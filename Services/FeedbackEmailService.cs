using System;
using System.Threading;
using System.Threading.Tasks;
using MailKit.Net.Smtp;
using MailKit.Security;

namespace FeedbackEmail
{
    public sealed class FeedbackEmailService
    {
        private readonly FeedbackEmailOptions options;
        private readonly FeedbackMessageFactory factory;
        private readonly Func<CancellationToken, Task<string>> accessTokenProvider;

        // OAuth callers can inject a provider that obtains/refreshes a token per send.
        public FeedbackEmailService(FeedbackEmailOptions options,
            Func<CancellationToken, Task<string>> accessTokenProvider = null)
        {
            this.options = options ?? throw new ArgumentNullException(nameof(options));
            this.factory = new FeedbackMessageFactory(options);
            this.accessTokenProvider = accessTokenProvider;
        }

        public async Task<string> SendAsync(FeedbackSubmission feedback,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            ValidateSmtpOptions();
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            using (var message = factory.Create(feedback))
            using (var client = new SmtpClient())
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
                client.Timeout = checked(options.TimeoutSeconds * 1000);
                var token = timeout.Token;
                string secret = null;
                if (options.Authentication == SmtpAuthentication.Password)
                    secret = Environment.GetEnvironmentVariable("FEEDBACK_SMTP_PASSWORD");
                else if (options.Authentication == SmtpAuthentication.OAuth2)
                    secret = accessTokenProvider == null
                        ? Environment.GetEnvironmentVariable("FEEDBACK_SMTP_ACCESS_TOKEN")
                        : await accessTokenProvider(token).ConfigureAwait(false);

                if (options.Authentication != SmtpAuthentication.None && string.IsNullOrWhiteSpace(secret))
                    throw new InvalidOperationException("Set the SMTP password/access-token environment variable, or supply an OAuth token provider.");

                // Strict TLS: never use Auto/StartTlsWhenAvailable or disable certificate validation.
                await client.ConnectAsync(options.Host, options.Port, options.Security, token).ConfigureAwait(false);

                if (options.Authentication == SmtpAuthentication.Password)
                {
                    client.AuthenticationMechanisms.Remove("XOAUTH2");
                    client.AuthenticationMechanisms.Remove("OAUTHBEARER");
                    await client.AuthenticateAsync(options.Username, secret, token).ConfigureAwait(false);
                }
                else if (options.Authentication == SmtpAuthentication.OAuth2)
                {
                    await client.AuthenticateAsync(new SaslMechanismOAuth2(options.Username, secret), token)
                        .ConfigureAwait(false);
                }

                await client.SendAsync(message, token).ConfigureAwait(false);
                // Success means the SMTP server accepted the message, not guaranteed inbox delivery.
                // Dispose closes the connection, including on an exception.
                // No automatic retry: a lost SMTP acknowledgement can otherwise cause duplicates.
                return message.MessageId;
            }
        }

        private void ValidateSmtpOptions()
        {
            if (string.IsNullOrWhiteSpace(options.Host) || options.Host == "smtp.example.com")
                throw new ArgumentException("Configure your real Smtp.Host before sending.");
            if (options.Port < 1 || options.Port > 65535)
                throw new ArgumentException("SMTP port must be between 1 and 65535.");
            if (options.Security != SecureSocketOptions.StartTls && options.Security != SecureSocketOptions.SslOnConnect)
                throw new ArgumentException("SMTP security must be StartTls or SslOnConnect.");
            if (!Enum.IsDefined(typeof(SmtpAuthentication), options.Authentication))
                throw new ArgumentException("SMTP authentication must be Password, OAuth2, or None.");
            if (options.Authentication != SmtpAuthentication.None && string.IsNullOrWhiteSpace(options.Username))
                throw new ArgumentException("An SMTP username is required.");
            if (options.TimeoutSeconds < 1 || options.TimeoutSeconds > 300)
                throw new ArgumentException("SMTP timeout must be between 1 and 300 seconds.");
        }
    }
}
