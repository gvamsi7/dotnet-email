# ISH feedback email integration

Email-only integration for the project layout shown in the screenshot:
`ISH.CustomerPortal.API`, `ISH.CustomerPortal.Application`, and `ISH.CustomerPortal.Infrastructure`.

The screenshot shows an ASP.NET Core Web SDK project with `appsettings.json`, `Program.cs`, and `DependencyInjection.cs`. Use the **source-code** files below for that application. The earlier .NET Framework 4.8 console example is retained in [examples/legacy-net48](../examples/legacy-net48).

The repository also includes a runnable API; this guide describes adding only the email component to your existing company application. Your existing handler supplies each user's feedback and calls the email service.

## Where each file belongs

Paths below are relative to your existing `source-code` folder.

| Existing project / location | Add or merge |
| --- | --- |
| `ISH.CustomerPortal.Application/Feedback` | `IFeedbackEmailService.cs` and `FeedbackSubmission.cs`: the interface, feedback data, and attachment bytes |
| `ISH.CustomerPortal.Infrastructure/Email` | SMTP implementation, options, validation, renderer, message factory, token-provider interface, and DI extension |
| `ISH.CustomerPortal.Infrastructure/Email/Templates` | `Feedback.html`: the email layout |
| `ISH.CustomerPortal.API` | `FeedbackEmailRegistration.cs`: a small registration extension |
| `ISH.CustomerPortal.API/appsettings.json` | Merge the `FeedbackEmail` section from the [configuration example](appsettings.feedback-email.example.json) |
| `ISH.CustomerPortal.API/Program.cs` or its existing `DependencyInjection.cs` | Call `AddCustomerPortalFeedbackEmail(...)` once |
| Your existing feedback handler/controller | Inject `IFeedbackEmailService` and call `SendAsync` with real submission data |

`Domain`, persistence/SQL Server, authentication, WebSocket, EmptyOffer, and `ISH.Shared.Service` do not need changes for email sending. The screenshot also contains `ISH.CustomerPortal.Infrastructure.EcoS.Notification`; its existing implementation was not provided. The new SMTP implementation lives in the general Infrastructure project. If your team already routes all mail through EcoS.Notification, adapt that existing sender behind `IFeedbackEmailService` instead of registering two senders.

## Integrate into your existing solution

### 1. Copy the new files

Copy only `Application/Feedback`, `Infrastructure/Email`, and `API/FeedbackEmailRegistration.cs` from [source-code](../source-code), preserving the full ISH project names. Merge the configuration and project-reference additions described below. The runnable example also contains project files, controllers, authentication, `Program.cs`, and appsettings; keep your existing application startup and project files when adding only the email component.

The screenshot does not reveal `<TargetFramework>`. **Keep the target framework in your real projects.** The reference checks in this repo target .NET 8 to validate the files independently; they are not a requirement to retarget your application. The runnable API defaults to .NET 10. If your projects target an older runtime or `netstandard`, align the Microsoft.Extensions references with that runtime before integrating.

### 2. Update project references and embed the template

Merge the entries in [Infrastructure.csproj.additions.xml](Infrastructure.csproj.additions.xml) into `ISH.CustomerPortal.Infrastructure.csproj`. They add:

- A reference to `ISH.CustomerPortal.Application`, if missing.
- MailKit 4.17.0 in **Infrastructure only**.
- `Microsoft.AspNetCore.App` as a framework reference for the ASP.NET Core configuration, DI, options, and logging APIs. Add only if missing. This form is for an Infrastructure project targeting modern .NET; if it targets `netstandard`, use the compatible Microsoft.Extensions packages already used by your solution instead.
- An explicit embedded resource for the HTML:

```xml
<EmbeddedResource Include="Email\Templates\Feedback.html">
  <LogicalName>ISH.CustomerPortal.Infrastructure.Email.Templates.Feedback.html</LogicalName>
</EmbeddedResource>
```

With central package management, add the MailKit version to `Directory.Packages.props` and omit `Version` on the `PackageReference`.

Ensure the API references Application and Infrastructure; [API.csproj.additions.xml](API.csproj.additions.xml) shows those entries. Add only references that are missing.

The renderer reads the template from **its own Infrastructure assembly**. The API's `EnableDefaultContentItems=false` setting shown in the screenshot does not affect this embedded resource. There is no dependency on `wwwroot`, the process working directory, or a copied HTML file beside the application.

### 3. Configure SMTP in the API project

Merge [appsettings.feedback-email.example.json](appsettings.feedback-email.example.json) into the existing API `appsettings.json`. Replace the example SMTP host, username, sender, and support mailbox. Preserve all your application's other settings. `Host` is deliberately validated at startup so an unconfigured email feature fails with a clear message.

Put credentials in your existing secret provider or server environment:

| Environment variable | Configuration key |
| --- | --- |
| `FeedbackEmail__Password` | `FeedbackEmail:Password` |
| `FeedbackEmail__AccessToken` | `FeedbackEmail:AccessToken` |

These are ASP.NET Core configuration keys. The legacy example's `FEEDBACK_SMTP_PASSWORD` variable and `App.config` loader are not used by this version. Your existing normal ASP.NET Core configuration pipeline loads environment variables; if your app uses a custom pipeline, ensure that provider is included.

For local development, use the API project's existing User Secrets setup or initialize it with `dotnet user-secrets init --project source-code/ISH.CustomerPortal.API`. Set `FeedbackEmail:Password` in that store. Do not commit passwords or tokens to JSON.

`Security` accepts `StartTls` (commonly port 587) or `SslOnConnect` (commonly 465). Authentication accepts `Password`, `OAuth2`, or `None` for an administrator-approved TLS relay. Use the mode your provider supports. The sender must be permitted for the SMTP account.

For OAuth2, implement and register `IFeedbackSmtpTokenProvider` using your existing identity service to acquire a valid SMTP-scoped access token for each send. `AccessToken` from configuration is also supported for short-lived tests. App registration, consent, permissions, and refresh are provider-specific and are not included.

### 4. Register the service once

In the **existing** API `Program.cs`, after the application's normal configuration setup and before `builder.Build()`:

```csharp
using ISH.CustomerPortal.API;

builder.Services.AddCustomerPortalFeedbackEmail(builder.Configuration);
```

Or put this inside your **existing** `DependencyInjection.cs` registration method:

```csharp
services.AddCustomerPortalFeedbackEmail(configuration);
```

Use one location. Keep your existing authentication, middleware, controllers, service registrations, and application startup.

### 5. Call from your existing feedback handler

Inject the Application interface through your existing constructor:

```csharp
using ISH.CustomerPortal.Application.Feedback;

private readonly IFeedbackEmailService _feedbackEmail;

// Add this parameter and assignment to your existing constructor:
// IFeedbackEmailService feedbackEmail
// _feedbackEmail = feedbackEmail;
```

After your existing validation (and feedback save, if you already store it), await:

```csharp
var submission = new FeedbackSubmission
{
    Type = model.IsBug ? FeedbackType.ReportABug : FeedbackType.GeneralFeedback,
    Comments = model.Comments,
    SubmittedBy = currentUser.Email,
    SubmittedAt = DateTimeOffset.UtcNow,
    HardwareId = model.HardwareId
};

string messageId = await _feedbackEmail.SendAsync(submission, cancellationToken);
```

`model`, `currentUser`, and `cancellationToken` above refer to your existing request model, trusted authenticated-user context, and handler/request cancellation token. Map their names to your application. Use the verified user's email from your server-side identity service; do not treat an arbitrary form field as verified identity.

The configured application SMTP account sends each notification to the configured support mailbox. The body identifies the individual user in `Submitted By`. Each call creates a separate message and SMTP client, and shared feedback data is not stored in the sender.

Add screenshots from your existing validated upload flow before sending:

```csharp
submission.Attachments.Add(new FeedbackAttachment
{
    FileName = "Screenshot1.png",
    Content = validatedScreenshotBytes
});
```

`validatedScreenshotBytes` must come from your upload validation/storage pipeline, not an arbitrary server file path supplied by a caller. Up to 5 files and 10 MiB of combined raw attachment data are allowed. The mail contains actual MIME attachments. An optional `DownloadUrl` can reference an existing HTTPS route managed by your application; without a URL the body shows the filename with `(attached)`.

If feedback is already persisted and SMTP fails, handle notification status separately so users are not told that the stored feedback was lost. Use your existing durable job/outbox system if mail must be delivered in the background. Do not use an unawaited `Task.Run` inside an HTTP request. The component propagates exceptions and does not automatically retry a send with an uncertain outcome.

## Customization

- Edit `Infrastructure/Email/Templates/Feedback.html` for the layout.
- The default green is `#3DCD58`; the brand defaults to a text fallback. Set `LogoFilePath` to a trusted absolute PNG/JPG path to embed your approved logo using CID.
- Supply real HTTPS `TermsUrl` and `PrivacyUrl` values to turn the footer text into links.
- `TimeZoneId` defaults to `Asia/Kolkata`; choose an ID available on your server.
- User-entered text is HTML-encoded, newlines are preserved, and the MIME message includes a plain-text version.
- SMTP credentials and the feedback body are not logged by the sender. Successful SMTP acceptance logs only the generated message ID.

## Verify without sending email

The [verification](../verification) projects compile the exact source files as separate Application, Infrastructure, and API assemblies. They do not replace your application's project files. Run:

```powershell
dotnet run --project verification/SmokeTests/FeedbackEmail.SmokeTests.csproj --configuration Release
```

Checks cover DI/configuration startup, the embedded template after publish, separate users' rendered messages, HTML escaping, attachment content, cancellation, and rejection of invalid feedback/unsafe SMTP configuration. These original component smoke tests do not open an SMTP connection. The separate API verification script can test an ephemeral local SMTP server over TLS. GitHub Actions runs the checks on Windows and Linux and also builds the retained .NET Framework example on Windows.

These checks verify this component; your complete company application and SMTP server still need an integration test using their real configuration.

## Documentation

- [ASP.NET Core options and DI](https://learn.microsoft.com/en-us/aspnet/core/fundamentals/configuration/options)
- [Development secret storage](https://learn.microsoft.com/en-us/aspnet/core/security/app-secrets)
- [MailKit SMTP and TLS](https://mimekit.net/docs/html/M_MailKit_Net_Smtp_SmtpClient_ConnectAsync_2.htm)
