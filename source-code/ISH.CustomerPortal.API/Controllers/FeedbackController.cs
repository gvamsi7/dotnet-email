using System.Net.Sockets;
using ISH.CustomerPortal.API.Models;
using ISH.CustomerPortal.Application.Feedback;
using ISH.CustomerPortal.Infrastructure.Email;
using MailKit;
using MailKit.Net.Smtp;
using MailKit.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace ISH.CustomerPortal.API.Controllers;

[ApiController, Authorize, EnableRateLimiting("feedback")]
[Route("api/feedback")]
public sealed class FeedbackController : ControllerBase
{
    private const long MaxAttachmentBytes = 10 * 1024 * 1024;
    private static readonly HashSet<string> AllowedExtensions = new(StringComparer.OrdinalIgnoreCase)
        { ".png", ".jpg", ".jpeg", ".pdf", ".txt" };
    private readonly IFeedbackEmailService email;
    private readonly IOptionsMonitor<FeedbackEmailOptions> settings;
    private readonly ILogger<FeedbackController> logger;

    public FeedbackController(IFeedbackEmailService email, IOptionsMonitor<FeedbackEmailOptions> settings,
        ILogger<FeedbackController> logger)
    {
        this.email = email;
        this.settings = settings;
        this.logger = logger;
    }

    /// <summary>Send feedback using JSON; optional attachments contain base64 content.</summary>
    [HttpPost, Consumes("application/json")]
    [ProducesResponseType<FeedbackSendResponse>(200)]
    [ProducesResponseType<ValidationProblemDetails>(400)]
    public async Task<IActionResult> Submit([FromBody] FeedbackRequest request, CancellationToken cancellationToken)
    {
        FeedbackSubmission submission;
        try { submission = MapJson(request); }
        catch (ArgumentException error) { return InvalidFeedback(error.Message); }
        return await Send(submission, cancellationToken);
    }

    /// <summary>Send feedback with uploaded screenshots or other supported files.</summary>
    [HttpPost("upload"), Consumes("multipart/form-data")]
    [ProducesResponseType<FeedbackSendResponse>(200)]
    [ProducesResponseType<ValidationProblemDetails>(400)]
    public async Task<IActionResult> Upload([FromForm] FeedbackUploadRequest request, CancellationToken cancellationToken)
    {
        var submission = request.ToSubmission();
        try
        {
            if (request.Attachments.Count > 5 || request.Attachments.Sum(file => file.Length) > MaxAttachmentBytes)
                throw new ArgumentException("Use at most 5 attachments with a combined size of 10 MiB or less.");
            foreach (var file in request.Attachments)
            {
                RequireSupportedFile(file.FileName);
                if (file.Length == 0) throw new ArgumentException("Attachments must not be empty.");
                using var content = new MemoryStream();
                await file.CopyToAsync(content, cancellationToken);
                submission.Attachments.Add(new FeedbackAttachment { FileName = file.FileName, Content = content.ToArray() });
            }
            FeedbackMessageFactory.ValidateSubmission(submission);
        }
        catch (ArgumentException error) { return InvalidFeedback(error.Message); }
        return await Send(submission, cancellationToken);
    }

    /// <summary>Render the email HTML without sending it or saving a message.</summary>
    [HttpPost("preview"), Consumes("application/json")]
    [Produces("text/html")]
    public IActionResult Preview([FromBody] FeedbackRequest request)
    {
        FeedbackSubmission submission;
        try { submission = MapJson(request); }
        catch (ArgumentException error) { return InvalidFeedback(error.Message); }
        using var message = new FeedbackMessageFactory(settings.CurrentValue).Create(submission);
        Response.Headers.CacheControl = "no-store";
        Response.Headers.ContentSecurityPolicy = "default-src 'none'; style-src 'unsafe-inline'; img-src cid: data:; frame-ancestors 'none'";
        Response.Headers.XContentTypeOptions = "nosniff";
        return Content(message.HtmlBody ?? string.Empty, "text/html; charset=utf-8");
    }

    private static FeedbackSubmission MapJson(FeedbackRequest request)
    {
        var submission = request.ToSubmission();
        foreach (var attachment in request.Attachments)
        {
            if (attachment is null) throw new ArgumentException("Attachments must not contain null entries.");
            RequireSupportedFile(attachment.FileName);
            submission.Attachments.Add(new FeedbackAttachment { FileName = attachment.FileName, Content = attachment.Content });
        }
        FeedbackMessageFactory.ValidateSubmission(submission);
        return submission;
    }

    private static void RequireSupportedFile(string fileName)
    {
        if (string.IsNullOrWhiteSpace(fileName) || fileName.Length > 200 || fileName.Any(char.IsControl) ||
            fileName.IndexOfAny(['/', '\\', ':']) >= 0 || !AllowedExtensions.Contains(Path.GetExtension(fileName)))
            throw new ArgumentException("Attachments require a simple PNG, JPG, JPEG, PDF, or TXT filename.");
    }

    private IActionResult InvalidFeedback(string message)
    {
        ModelState.AddModelError("feedback", message);
        return ValidationProblem(ModelState);
    }

    private async Task<IActionResult> Send(FeedbackSubmission submission, CancellationToken cancellationToken)
    {
        try
        {
            var messageId = await email.SendAsync(submission, cancellationToken);
            var status = settings.CurrentValue.DeliveryMode == FeedbackDeliveryMode.Pickup ? "savedLocally" : "smtpAccepted";
            Response.Headers.CacheControl = "no-store";
            return Ok(new FeedbackSendResponse(messageId, status, submission.SubmittedAt));
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return Problem(statusCode: 504, title: "Email operation timed out",
                detail: "The send outcome may be uncertain. Check the mail server before retrying.");
        }
        catch (Exception error) when (error is SmtpCommandException or SmtpProtocolException or
            MailKit.Security.AuthenticationException or SslHandshakeException or SocketException or IOException or
            ServiceNotConnectedException or ServiceNotAuthenticatedException or NotSupportedException)
        {
            // SMTP error messages can contain addresses. Log only the exception type and trace ID.
            logger.LogWarning("Email provider failure {ErrorType}; trace {TraceId}.", error.GetType().Name, HttpContext.TraceIdentifier);
            return Problem(statusCode: 502, title: "Email provider unavailable",
                detail: "The mail operation failed. Check server configuration and delivery logs before retrying.");
        }
        catch (Exception error) when (error is InvalidOperationException or OptionsValidationException)
        {
            logger.LogWarning("Email configuration failure {ErrorType}; trace {TraceId}.", error.GetType().Name, HttpContext.TraceIdentifier);
            return Problem(statusCode: 503, title: "Email service is not configured",
                detail: "Contact the administrator to check SMTP settings and credentials.");
        }
    }
}
