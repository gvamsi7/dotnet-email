using System.Threading;
using System.Threading.Tasks;

namespace ISH.CustomerPortal.Application.Feedback
{
    // Application-layer contract. No SMTP, MailKit, HTTP, or configuration dependencies.
    public interface IFeedbackEmailService
    {
        // Returns the Message-ID after SMTP acceptance; this is not an inbox-delivery guarantee.
        Task<string> SendAsync(FeedbackSubmission feedback,
            CancellationToken cancellationToken = default(CancellationToken));
    }
}
