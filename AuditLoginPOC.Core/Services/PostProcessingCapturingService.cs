using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AuditLoginPOC.Core.Interfaces;
using AuditLoginPOC.Core.Models;
using AuditLoginPOC.Core.Wrappers;

namespace AuditLoginPOC.Core.Services
{
    /// <summary>
    /// Captures request data after model binding (limited to processed data)
    /// Compatible with Web API patterns
    /// </summary>
    public class PostProcessingCapturingService : IRequestCapturingService
    {
        public Task<CapturedRequest> CaptureRequestAsync(IHttpRequestWrapper request)
        {
            // Post-processing service works with any wrapper but has limitations
            return Task.FromResult(new CapturedRequest
            {
                Method = request.Method,
                Url = request.Url,
                Body = null, // Raw body not accessible after model binding
                QueryString = request.GetQueryString(),
                Headers = request.GetHeaders(),
                ContentLength = request.ContentLength,
                CapturedAt = request.RequestTime,
                OriginalRequest = request.GetUnderlyingRequest()
            });
        }
    }
}
