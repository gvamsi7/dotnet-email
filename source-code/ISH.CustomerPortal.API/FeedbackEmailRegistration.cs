using ISH.CustomerPortal.Infrastructure.Email;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace ISH.CustomerPortal.API
{
    // Call this from your existing Program.cs or DependencyInjection.cs.
    // Your existing DependencyInjection class does not need to be replaced.
    public static class FeedbackEmailRegistration
    {
        public static IServiceCollection AddCustomerPortalFeedbackEmail(
            this IServiceCollection services, IConfiguration configuration)
        {
            return services.AddFeedbackEmail(configuration);
        }
    }
}
