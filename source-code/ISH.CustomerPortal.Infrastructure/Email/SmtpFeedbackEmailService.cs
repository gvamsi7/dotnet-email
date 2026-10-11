using System;
using System.Threading;
using System.Threading.Tasks;
using ISH.CustomerPortal.Application.Feedback;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ISH.CustomerPortal.Infrastructure.Email
{
    public sealed class SmtpFeedbackEmailService : IFeedbackEmailService
    {
        private readonly IOptionsMonitor<FeedbackEmailOptions> settings;
        private readonly ILogger<SmtpFeedbackEmailService> logger;
        private readonly IFeedbackSmtpTokenProvider tokenProvider;

        public SmtpFeedbackEmailService(IOptionsMonitor<FeedbackEmailOptions> settings,
            ILogger<SmtpFeedbackEmailService> logger, IFeedbackSmtpTokenProvider tokenProvider = null)
        {
            this.settings = settings ?? throw new ArgumentNullException(nameof(settings));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.tokenProvider = tokenProvider;
        }

        public async Task<string> SendAsync(FeedbackSubmission feedback,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var options = settings.CurrentValue;
            var validation = new FeedbackEmailOptionsValidator().Validate(Options.DefaultName, options);
            if (validation.Failed)
                throw new OptionsValidationException(Options.DefaultName, typeof(FeedbackEmailOptions), validation.Failures);

            // Each call owns its message/client, so different users' submissions are isolated.
            using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken))
            using (var message = new FeedbackMessageFactory(options).Create(feedback))
            using (var client = new SmtpClient())
            {
                timeout.CancelAfter(TimeSpan.FromSeconds(options.TimeoutSeconds));
                client.Timeout = checked(options.TimeoutSeconds * 1000);
                var token = timeout.Token;
                string secret = null;
                if (options.Authentication == SmtpAuthentication.Password)
                    secret = options.Password;
                else if (options.Authentication == SmtpAuthentication.OAuth2)
                    secret = tokenProvider == null ? options.AccessToken :
                        await tokenProvider.GetAccessTokenAsync(token).ConfigureAwait(false);

                if (options.Authentication != SmtpAuthentication.None && string.IsNullOrWhiteSpace(secret))
                    throw new InvalidOperationException("Configure the SMTP password/access token using your server's secret provider, or register IFeedbackSmtpTokenProvider.");

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
                logger.LogInformation("Feedback notification {MessageId} accepted by SMTP.", message.MessageId);
                return message.MessageId;
                // Dispose closes the connection on success or failure. Exceptions reach the
                // caller; no automatic retry can duplicate a send with a lost acknowledgement.
            }
        }
    }
}
