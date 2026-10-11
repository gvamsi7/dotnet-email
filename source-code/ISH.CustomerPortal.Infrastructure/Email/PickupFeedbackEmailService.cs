using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using ISH.CustomerPortal.Application.Feedback;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace ISH.CustomerPortal.Infrastructure.Email
{
    // Development delivery: produces the same MIME message without connecting to SMTP.
    public sealed class PickupFeedbackEmailService : IFeedbackEmailService
    {
        private readonly IOptionsMonitor<FeedbackEmailOptions> settings;
        private readonly IHostEnvironment environment;
        private readonly ILogger<PickupFeedbackEmailService> logger;

        public PickupFeedbackEmailService(IOptionsMonitor<FeedbackEmailOptions> settings,
            IHostEnvironment environment, ILogger<PickupFeedbackEmailService> logger)
        {
            this.settings = settings;
            this.environment = environment;
            this.logger = logger;
        }

        public async Task<string> SendAsync(FeedbackSubmission feedback,
            CancellationToken cancellationToken = default(CancellationToken))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var options = settings.CurrentValue;
            var directory = Path.GetFullPath(options.PickupDirectory, environment.ContentRootPath);
            Directory.CreateDirectory(directory);
            var temporary = Path.Combine(directory, Guid.NewGuid().ToString("N") + ".tmp");
            var destination = Path.ChangeExtension(temporary, ".eml");
            try
            {
                using (var message = new FeedbackMessageFactory(options).Create(feedback))
                {
                    using (var output = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write,
                        FileShare.None, 81920, FileOptions.Asynchronous))
                        await message.WriteToAsync(output, cancellationToken).ConfigureAwait(false);

                    cancellationToken.ThrowIfCancellationRequested();
                    File.Move(temporary, destination);
                    logger.LogInformation("Feedback notification {MessageId} saved locally in Pickup mode.", message.MessageId);
                    return message.MessageId;
                }
            }
            finally
            {
                if (File.Exists(temporary)) File.Delete(temporary);
            }
        }
    }
}
