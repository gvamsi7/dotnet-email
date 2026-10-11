using System.ComponentModel.DataAnnotations;
using ISH.CustomerPortal.Application.Feedback;

namespace ISH.CustomerPortal.API.Models;

public class FeedbackFields
{
    [Required, EnumDataType(typeof(FeedbackType))]
    public FeedbackType? Type { get; set; }

    [Required, StringLength(10_000, MinimumLength = 1)]
    public string Comments { get; set; } = string.Empty;

    [Required, EmailAddress, StringLength(254)]
    public string SubmittedBy { get; set; } = string.Empty;

    [StringLength(512)]
    public string? HardwareId { get; set; }

    public FeedbackSubmission ToSubmission() => new()
    {
        Type = Type!.Value,
        Comments = Comments,
        SubmittedBy = SubmittedBy,
        HardwareId = HardwareId,
        SubmittedAt = DateTimeOffset.UtcNow
    };
}

public sealed class FeedbackRequest : FeedbackFields
{
    [Required, MaxLength(5)]
    public List<FeedbackAttachmentRequest> Attachments { get; set; } = [];
}

public sealed class FeedbackAttachmentRequest
{
    [Required, StringLength(200)]
    public string FileName { get; set; } = string.Empty;

    // System.Text.Json accepts base64. Malformed base64 produces HTTP 400.
    [Required, MinLength(1), MaxLength(10 * 1024 * 1024)]
    public byte[] Content { get; set; } = [];
}

public sealed class FeedbackUploadRequest : FeedbackFields
{
    [MaxLength(5)]
    public List<IFormFile> Attachments { get; set; } = [];
}

public sealed record FeedbackSendResponse(string MessageId, string Status, DateTimeOffset SubmittedAt);
