using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using ISH.CustomerPortal.API;
using ISH.CustomerPortal.API.Security;
using ISH.CustomerPortal.Infrastructure.Email;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.OpenApi;

var builder = WebApplication.CreateBuilder(args);

// Covers 10 MiB raw attachments after base64 encoding, plus form/JSON overhead.
builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 16 * 1024 * 1024);
builder.Services.Configure<FormOptions>(options =>
{
    options.MultipartBodyLengthLimit = 16 * 1024 * 1024;
    options.ValueLengthLimit = 12_000;
    options.ValueCountLimit = 20;
});

builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false)));
builder.Services.AddProblemDetails();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "ISH Feedback SMTP API",
        Version = "v1",
        Description = "Dynamic feedback notifications with HTML, plain text, and MIME attachments."
    });
    options.AddSecurityDefinition("ApiKey", new OpenApiSecurityScheme
    {
        Type = SecuritySchemeType.ApiKey,
        In = ParameterLocation.Header,
        Name = ApiKeyAuthenticationHandler.HeaderName,
        Description = "Server API key. In Development: local-development-key-change-me"
    });
    options.AddSecurityRequirement(document => new OpenApiSecurityRequirement
    {
        [new OpenApiSecuritySchemeReference("ApiKey", document)] = []
    });
});

builder.Services.AddOptions<ApiSecurityOptions>()
    .Bind(builder.Configuration.GetSection(ApiSecurityOptions.SectionName))
    .Validate(options => !string.IsNullOrWhiteSpace(options.ApiKey) && options.ApiKey.Length <= 512,
        "ApiSecurity:ApiKey must contain 1 to 512 characters.")
    .Validate(options => builder.Environment.IsDevelopment() ||
        (!string.IsNullOrWhiteSpace(options.ApiKey) && options.ApiKey.Length >= 32 && options.ApiKey != ApiSecurityOptions.DevelopmentKey),
        "Outside Development, configure an API key with at least 32 characters using a secret provider.")
    .ValidateOnStart();
builder.Services.AddAuthentication(ApiKeyAuthenticationHandler.SchemeName)
    .AddScheme<AuthenticationSchemeOptions, ApiKeyAuthenticationHandler>(ApiKeyAuthenticationHandler.SchemeName, _ => { });
builder.Services.AddAuthorization();

builder.Services.AddCustomerPortalFeedbackEmail(builder.Configuration);
builder.Services.AddOptions<FeedbackEmailOptions>()
    .Validate(options => builder.Environment.IsDevelopment() || options.DeliveryMode == FeedbackDeliveryMode.Smtp,
        "Pickup delivery is for Development only. Configure SMTP outside Development.")
    .Validate(options => options.DeliveryMode != FeedbackDeliveryMode.Smtp ||
        options.Authentication != SmtpAuthentication.Password || !string.IsNullOrWhiteSpace(options.Password),
        "FeedbackEmail:Password is required for SMTP Password authentication. Use a secret provider.")
    .ValidateOnStart();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddPolicy("feedback", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
        _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = 10,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(_ =>
        RateLimitPartition.GetConcurrencyLimiter("mail", _ => new ConcurrencyLimiterOptions
        {
            PermitLimit = 4,
            QueueLimit = 0
        }));
    options.OnRejected = async (context, token) =>
    {
        if (context.Lease.TryGetMetadata(MetadataName.RetryAfter, out var retryAfter))
            context.HttpContext.Response.Headers.RetryAfter = Math.Ceiling(retryAfter.TotalSeconds).ToString();
        await Results.Problem(statusCode: 429, title: "Too many requests",
            detail: "Wait before submitting more feedback.").ExecuteAsync(context.HttpContext);
    };
});

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI(options => options.SwaggerEndpoint("/swagger/v1/swagger.json", "Feedback SMTP API v1"));
}
else
{
    app.UseHttpsRedirection();
}
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.Use(async (context, next) =>
{
    if (context.Request.ContentLength > 16 * 1024 * 1024)
    {
        await Results.Problem(statusCode: 413, title: "Request body is too large",
            detail: "The maximum HTTP request body size is 16 MiB.").ExecuteAsync(context);
        return;
    }
    await next(context);
});
app.UseRateLimiter();
app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "ok" })).AllowAnonymous();
app.MapGet("/", () => app.Environment.IsDevelopment()
    ? Results.Redirect("/swagger")
    : Results.Ok(new { service = "Feedback SMTP API", health = "/health" })).AllowAnonymous();
app.Run();
