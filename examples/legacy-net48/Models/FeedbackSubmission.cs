using System;
using System.Collections.Generic;

namespace FeedbackEmail
{
    public enum FeedbackType
    {
        GeneralFeedback,
        ReportABug
    }

    public sealed class FeedbackSubmission
    {
        public FeedbackType Type { get; set; }
        public string Comments { get; set; }
        public DateTimeOffset SubmittedAt { get; set; } = DateTimeOffset.UtcNow;
        public string SubmittedBy { get; set; }
        public string HardwareId { get; set; }
        public List<FeedbackAttachment> Attachments { get; } = new List<FeedbackAttachment>();
    }

    public sealed class FeedbackAttachment
    {
        // Pass validated upload bytes or bytes loaded from trusted server storage.
        // Never accept an arbitrary server file path from a browser request.
        public string FileName { get; set; }
        public byte[] Content { get; set; }

        // Optional existing HTTPS download link. The service does not host files.
        // Without a link, the filename is shown with "(attached)" in the body.
        public string DownloadUrl { get; set; }
    }
}
