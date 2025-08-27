using System;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;
using System.Web;
using AuditLoginPOC.Core.Interfaces;
using AuditLoginPOC.Core.Models;
using AuditLoginPOC.Core.Streams;
using AuditLoginPOC.Core.Wrappers;

namespace AuditLoginPOC.Core.Services
{
    /// <summary>
    /// Captures request by wrapping the input stream
    /// Compatible with HTTP Module patterns
    /// </summary>
    public class StreamWrappingCapturingService : IRequestCapturingService
    {
        public Task<CapturedRequest> CaptureRequestAsync(IHttpRequestWrapper request)
        {
            // This service only works with HttpRequest-based wrappers
            var httpRequest = request.GetUnderlyingRequest<HttpRequest>();
            if (httpRequest == null)
            {
                throw new InvalidOperationException(
                    "StreamWrappingCapturingService requires HttpRequest");
            }
            
            // Replace input stream with auditable wrapper to solve single-read issue
            var originalStream = httpRequest.InputStream;
            var auditableStream = new AuditableStream(originalStream);
            
            // Use reflection to replace the internal stream reference
            var inputStreamField = typeof(HttpRequest).GetField("_inputStream", 
                BindingFlags.NonPublic | BindingFlags.Instance);
            inputStreamField?.SetValue(httpRequest, auditableStream);
            
            return Task.FromResult(new CapturedRequest
            {
                Method = request.Method,
                Url = request.Url,
                Body = null, // Will be populated when stream is read
                QueryString = request.GetQueryString(),
                Headers = request.GetHeaders(),
                ContentLength = request.ContentLength,
                CapturedAt = request.RequestTime,
                AuditableStream = auditableStream, // Internal reference for later data retrieval
                OriginalRequest = httpRequest
            });
        }
    }
}
