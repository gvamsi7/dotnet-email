using System;
using ISH.CustomerPortal.Application.Feedback;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace ISH.CustomerPortal.Infrastructure.Email
{
    public static class FeedbackEmailServiceCollectionExtensions
    {
        public static IServiceCollection AddFeedbackEmail(this IServiceCollection services, IConfiguration configuration)
        {
            if (services == null) throw new ArgumentNullException(nameof(services));
            if (configuration == null) throw new ArgumentNullException(nameof(configuration));

            services.AddLogging();
            services.AddOptions<FeedbackEmailOptions>()
                .Bind(configuration.GetSection(FeedbackEmailOptions.SectionName))
                .ValidateOnStart();
            services.TryAddEnumerable(ServiceDescriptor.Singleton<IValidateOptions<FeedbackEmailOptions>, FeedbackEmailOptionsValidator>());
            services.TryAddTransient<IFeedbackEmailService, SmtpFeedbackEmailService>();
            return services;
        }
    }
}
