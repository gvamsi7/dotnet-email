using System.Threading;
using System.Threading.Tasks;

namespace ISH.CustomerPortal.Infrastructure.Email
{
    // Optional infrastructure adapter for your existing identity/token service.
    // Implement and register this only when your SMTP provider requires OAuth2.
    public interface IFeedbackSmtpTokenProvider
    {
        Task<string> GetAccessTokenAsync(CancellationToken cancellationToken);
    }
}
