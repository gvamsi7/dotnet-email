# Feedback email — C# / .NET Framework 4.8

This is the retained legacy console example. For the ASP.NET Core project structure shown in your screenshot, follow the [main integration guide](../../README.md).

A feedback notification based on the supplied screenshot: a green Schneider Electric / Industrial Services Hub header, Feedback heading, feedback type, comments, timestamp, submitter, attachments, hardware ID, and a muted footer.

The project targets **.NET Framework 4.8**, uses C# 7.3 and MailKit 4.17.0, and includes a small console runner. The renderer, message factory, and SMTP service can also be copied into an existing .NET Framework application.

## Quick start in Visual Studio

1. Extract this folder. Open `FeedbackEmail.csproj` in Visual Studio with the .NET desktop development workload and .NET Framework 4.8 targeting pack installed.
2. Restore NuGet packages and build. MailKit is already referenced by the project. In an existing project, install it with `Install-Package MailKit -Version 4.17.0`.
3. Run without arguments. It creates `Preview/feedback-preview.html` and `Preview/feedback-preview.eml` under the executable's output directory. It does not connect to SMTP in preview mode.
4. Open the HTML in a browser. Open the EML in Outlook or another compatible mail client to inspect the message and attachments.
5. Configure `App.config` as described below, rebuild, and run with `--send` when you want to send the sample message to the configured recipient.

From a Visual Studio Developer PowerShell, the equivalent commands are:

```powershell
dotnet build .\FeedbackEmail.csproj -c Release
.\bin\Release\net48\FeedbackEmail.exe --preview
```

`Preview/feedback-preview.html` included in this project is an illustrative rendering with fixed sample values. The executable generates fresh previews using your current configuration and the same embedded template.

## SMTP settings

Edit these values in `App.config`. In an existing ASP.NET Framework application, put the `<appSettings>` entries into its `Web.config` instead; merge with its existing configuration.

| Setting | Value to supply |
| --- | --- |
| `Smtp.Host` | Your approved SMTP server hostname |
| `Smtp.Port` | Server port, commonly `587` with STARTTLS or `465` with implicit TLS |
| `Smtp.Security` | `StartTls` or `SslOnConnect`, matching the server |
| `Smtp.Authentication` | `Password`, `OAuth2`, or `None` for an approved relay |
| `Smtp.Username` | SMTP account name, normally an email address |
| `Smtp.TimeoutSeconds` | Overall send timeout, from 1 to 300 seconds |
| `Email.FromAddress` | A sender address your SMTP account is allowed to use |
| `Email.ToAddress` | Your feedback/support mailbox; one configured recipient |

Supply credentials to the **server process** as environment variables:

- `FEEDBACK_SMTP_PASSWORD` for a password or provider-issued app password.
- `FEEDBACK_SMTP_ACCESS_TOKEN` for an existing OAuth2 access token when `Smtp.Authentication` is `OAuth2`.

The project does not store credentials in source or config. Use the authentication method your mail administrator supports. Setting `Password` does not enable password authentication on the provider. For IIS, provision secrets to the application process and recycle the application pool after changing its environment. Do not put secrets in browser JavaScript.

For a local PowerShell session, you can set a password without placing it in command history:

```powershell
$smtpSecret = Read-Host 'SMTP password/app password' -AsSecureString
$smtpCredential = New-Object System.Net.NetworkCredential('', $smtpSecret)
$env:FEEDBACK_SMTP_PASSWORD = $smtpCredential.Password
try {
    .\bin\Release\net48\FeedbackEmail.exe --send
}
finally {
    Remove-Item Env:\FEEDBACK_SMTP_PASSWORD -ErrorAction SilentlyContinue
    $smtpCredential = $null
    $smtpSecret.Dispose()
}
```

OAuth token acquisition, app registration, permissions, and token refresh depend on your provider and are not bundled. A long-running application should inject a token provider that obtains a valid SMTP-scoped access token for each send:

```csharp
var service = new FeedbackEmailService(options, cancellationToken =>
    yourTokenService.GetSmtpAccessTokenAsync(cancellationToken));
```

`yourTokenService` above is your application's provider-specific token service. The environment-token option is useful for a short-lived test with an already-issued token.

## Use from your application

Copy `Models`, `Services`, and `Templates` into the backend project and install MailKit. Add a reference to `System.Configuration`. Set the HTML file's Build Action to **Embedded Resource**. The supplied project uses the resource name `FeedbackEmail.Templates.Feedback.html`; if your project has a different root namespace, update that name in `FeedbackTemplateRenderer.cs`, or explicitly set that resource's `LogicalName` to `FeedbackEmail.Templates.Feedback.html`.

```csharp
using System;
using FeedbackEmail;

// Inside an async method/controller action:
var feedback = new FeedbackSubmission
{
    Type = FeedbackType.GeneralFeedback, // Or FeedbackType.ReportABug
    Comments = "Please add a confirmation after submitting feedback.",
    SubmittedAt = DateTimeOffset.UtcNow,
    SubmittedBy = "user@example.com",
    HardwareId = "ISH-DEVICE-001"
};

var options = FeedbackEmailOptions.FromConfiguration();
var service = new FeedbackEmailService(options);
string messageId = await service.SendAsync(feedback);
```

For a real form, take `SubmittedBy` from the authenticated user's verified email and set the timestamp on the server. Validate the form and uploaded files before calling the service. Use your existing authorization, upload size limits, permitted file types, and malware checks. This project is the email component; it does not add a public API, screenshot capture, file hosting, or storage.

### Attach a screenshot or another file

Add validated bytes to the submission before `SendAsync`:

```csharp
feedback.Attachments.Add(new FeedbackAttachment
{
    FileName = "Screenshot1.png",
    Content = validatedScreenshotBytes
});
```

`validatedScreenshotBytes` represents the bytes accepted by your upload pipeline. The file becomes a real MIME attachment. The email body shows `Screenshot1.png (attached)`; users open it from the mail client's attachment list. The filename is not a fake hyperlink to a server filesystem path.

To reproduce the screenshot's clickable filename, also set `DownloadUrl` to an existing HTTPS URL generated by your application. The service does not upload the attachment. The link must remain available to the intended recipient; use your own authenticated or appropriately scoped download route. The file is still attached to the message.

Up to 5 attachments and 10 MiB of combined raw attachment data are supported. MIME/base64 encoding increases the transmitted size; adjust the limit to your mail server's policy.

## Branding and layout

- `Templates/Feedback.html` contains the editable design. It uses presentation tables, inline styles, an Outlook width fallback, and optional mobile styles.
- The green is `#3DCD58`. Change it in the template to your approved brand color.
- The default brand is a **text fallback**, because an original logo asset was not supplied. To use the exact company logo, set `Email.LogoFilePath` to a trusted server-side PNG or JPG path. A transparent white logo works well on the green banner. The service embeds it with a Content-ID, so it does not depend on a public image URL.
- `Email.ApplicationName`, `Email.CompanyName`, and `Email.CopyrightOwner` control the text branding.
- `Email.TermsUrl` and `Email.PrivacyUrl` accept actual HTTPS policy links. Empty values show plain text, avoiding broken placeholder links.
- `Email.TimeZoneId` defaults to the Windows time-zone ID `India Standard Time`. The email shows an explicit UTC offset. Use a different Windows ID if needed.
- User comments are HTML-encoded; line breaks are preserved. HTML typed into the form is shown as text. No comment truncation or JavaScript is used.
- A plain-text alternative is included for clients that do not display HTML.

## Files

| File | Purpose |
| --- | --- |
| `Templates/Feedback.html` | Email markup and placeholders |
| `Models/FeedbackSubmission.cs` | Feedback type, fields, and attachment bytes |
| `Services/FeedbackEmailOptions.cs` | App.config / Web.config settings |
| `Services/FeedbackTemplateRenderer.cs` | Encoded HTML and plain-text rendering |
| `Services/FeedbackMessageFactory.cs` | MIME message, embedded logo, attachments, validation |
| `Services/FeedbackEmailService.cs` | Asynchronous TLS SMTP sending with cancellation |
| `Program.cs` | Preview mode and a sample send |
| `App.config` | Configuration template with example addresses |
| `Preview/feedback-preview.html` | Ready-to-open sample layout |

## Delivery and verification

SMTP success means the server accepted the message; it does not guarantee inbox placement. Exceptions propagate to your caller. Log an internal submission ID and the error type without logging credentials or the full feedback body. The service does not automatically retry an ambiguous send, which could deliver duplicate notifications. For background sending in a web application, use your existing durable job/outbox mechanism rather than an unawaited `Task.Run`.

Source/configuration and the static HTML preview were checked during preparation. A .NET compiler and .NET Framework runtime were not available in the creation environment. The repository's legacy Windows workflow job builds the project on Windows and runs preview generation on every push and pull request. Check the Actions tab for the result of the current commit. No SMTP credentials are needed for these checks, and the workflow does not send email. Check a test message in your actual email clients before integrating; rendering varies across clients.

## Reference documentation

- [MailKit 4.17.0 and supported target frameworks](https://www.nuget.org/packages/MailKit/4.17.0)
- [MailKit SMTP connection options](https://mimekit.net/docs/html/M_MailKit_Net_Smtp_SmtpClient_ConnectAsync_2.htm)
- [MimeKit BodyBuilder: HTML, plain text, embedded images, and attachments](https://mimekit.net/docs/html/T_MimeKit_BodyBuilder.htm)
- [MailKit OAuth2 authentication](https://mimekit.net/docs/html/T_MailKit_Security_SaslMechanismOAuth2.htm)
- [Microsoft notes on System.Net.Mail.SmtpClient for new development](https://learn.microsoft.com/en-us/dotnet/api/system.net.mail.smtpclient?view=netframework-4.8.1)
