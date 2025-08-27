using System;
using System.Collections.Generic;
using System.Net;

namespace AuditLoginPOC.Core.Models
{
    /// <summary>
    /// Complete audit context for logging
    /// </summary>
    public class AuditContext
    {
        public CapturedRequest CapturedRequest { get; set; }
        public string UserId { get; set; }
        public Dictionary<string, string> UserClaims { get; set; }
        public string ControllerName { get; set; }
        public string ActionName { get; set; }
        public bool? ModelStateValid { get; set; }
        public List<string> ValidationErrors { get; set; }
        public double ProcessingTimeMs { get; set; }
        public HttpStatusCode? ResponseStatusCode { get; set; }
        public string ExceptionInfo { get; set; }
        public AuditStrategy ProcessingStrategy { get; set; }
        public Dictionary<string, object> AdditionalContext { get; set; }
    }
}
