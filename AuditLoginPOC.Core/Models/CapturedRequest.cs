using System;
using System.Collections.Generic;
using AuditLoginPOC.Core.Streams;

namespace AuditLoginPOC.Core.Models
{
    /// <summary>
    /// Represents a captured HTTP request with all relevant audit data
    /// </summary>
    public class CapturedRequest
    {
        public string Method { get; set; }
        public string Url { get; set; }
        public string Body { get; set; }
        public string QueryString { get; set; }
        public Dictionary<string, string> Headers { get; set; }
        public long ContentLength { get; set; }
        public DateTime CapturedAt { get; set; }
        public bool IsTruncated { get; set; }
        public string Hash { get; set; }
        public object OriginalRequest { get; set; }
        public Dictionary<string, object> ProcessedParameters { get; set; }
        public string ControllerName { get; set; }
        public string ActionName { get; set; }
        public AuditableStream AuditableStream { get; set; }
        public Guid? DeferredAuditId { get; set; }

        public static CapturedRequest Empty => new CapturedRequest
        {
            Body = string.Empty,
            QueryString = string.Empty,
            Headers = new Dictionary<string, string>(),
            ContentLength = 0,
            CapturedAt = DateTime.UtcNow,
            IsTruncated = false
        };

        public static CapturedRequest Deferred(Guid auditId) => new CapturedRequest
        {
            DeferredAuditId = auditId,
            CapturedAt = DateTime.UtcNow
        };
    }
}
