using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using AuditLoginPOC.Core.Interfaces;
using AuditLoginPOC.Core.Models;

namespace AuditLoginPOC.Core.Services
{
    /// <summary>
    /// Captures request by reading content and recreating HttpContent
    /// Compatible with DelegatingHandler patterns
    /// </summary>
    public class ContentReplacementCapturingService : IRequestCapturingService
    {
        public async Task<CapturedRequest> CaptureRequestAsync(IHttpRequestWrapper request)
        {
            // This service only works with HttpRequestMessage-based wrappers
            var httpRequestMessage = request.GetUnderlyingRequest<HttpRequestMessage>();
            if (httpRequestMessage == null)
            {
                throw new InvalidOperationException(
                    "ContentReplacementCapturingService requires HttpRequestMessage");
            }

            if (httpRequestMessage.Content == null)
                return CapturedRequest.Empty;

            // Read original content
            var requestBody = await httpRequestMessage.Content.ReadAsStringAsync();
            
            // Preserve original content type and headers
            var originalContentType = httpRequestMessage.Content.Headers.ContentType;
            var originalHeaders = httpRequestMessage.Content.Headers.ToList();
            
            // Recreate content with same data and headers to solve single-read issue
            httpRequestMessage.Content = new StringContent(requestBody, 
                Encoding.UTF8, originalContentType?.MediaType ?? "application/json");
                
            // Restore all original headers
            foreach (var header in originalHeaders)
            {
                httpRequestMessage.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            
            return new CapturedRequest
            {
                Method = request.Method,
                Url = request.Url,
                Body = requestBody,
                QueryString = request.GetQueryString(),
                Headers = request.GetHeaders(),
                ContentLength = requestBody?.Length ?? 0,
                CapturedAt = request.RequestTime,
                OriginalRequest = httpRequestMessage
            };
        }
    }
}
