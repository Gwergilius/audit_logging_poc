using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.Web;
using AuditLoginPOC.Core.Interfaces;
using AuditLoginPOC.Core.Models;
using AuditLoginPOC.Core.Services;

namespace AuditLoginPOC.Core.Services
{
    /// <summary>
    /// DoS protection based on request size limits
    /// </summary>
    public class SizeLimitProtectionService : IDoSProtectionService
    {
        private const int MAX_MEMORY_SIZE = 1024 * 1024; // 1MB
        private const int MAX_TOTAL_SIZE = 50 * 1024 * 1024; // 50MB
        
        public async Task<ProtectionResult> EvaluateRequestAsync(IHttpRequestWrapper request)
        {
            var contentLength = request.ContentLength;
            
            if (contentLength > MAX_TOTAL_SIZE)
            {
                return ProtectionResult.Denied("Request too large for audit logging");
            }
            
            if (contentLength > MAX_MEMORY_SIZE)
            {
                return ProtectionResult.Allowed(AuditStrategy.Summary);
            }
            
            return ProtectionResult.Allowed(AuditStrategy.Full);
        }
        
        public async Task<CapturedRequest> ProcessWithProtectionAsync(IHttpRequestWrapper request)
        {
            var protection = await EvaluateRequestAsync(request);
            
            if (!protection.IsAllowed)
            {
                throw new RequestTooLargeException(protection.ReasonIfDenied);
            }
            
            if (protection.Strategy == AuditStrategy.Summary)
            {
                return await ProcessLargeRequestAsync(request);
            }
            
            // For small requests, delegate to appropriate capturing service
            var capturingService = GetCapturingService(request);
            return await capturingService.CaptureRequestAsync(request);
        }
        
        private async Task<CapturedRequest> ProcessLargeRequestAsync(IHttpRequestWrapper request)
        {
            // This method needs access to the underlying HttpRequestMessage for content manipulation
            var httpRequestMessage = request.GetUnderlyingRequest<HttpRequestMessage>();
            if (httpRequestMessage?.Content == null)
            {
                return CapturedRequest.Empty;
            }
            
            var tempFilePath = Path.GetTempFileName();
            
            try
            {
                var buffer = new byte[8192];
                var summaryBuilder = new StringBuilder();
                var totalBytesRead = 0;
                
                // First, read the content and write to temp file
                using (var fileStream = File.Create(tempFilePath))
                using (var originalStream = await httpRequestMessage.Content.ReadAsStreamAsync())
                {
                    int bytesRead;
                    while ((bytesRead = await originalStream.ReadAsync(buffer, 0, buffer.Length)) > 0)
                    {
                        await fileStream.WriteAsync(buffer, 0, bytesRead);
                        
                        // Keep first 4KB for audit summary
                        if (totalBytesRead < 4096)
                        {
                            var textChunk = Encoding.UTF8.GetString(buffer, 0, 
                                Math.Min(bytesRead, 4096 - totalBytesRead));
                            summaryBuilder.Append(textChunk);
                        }
                        
                        totalBytesRead += bytesRead;
                    }
                }
                
                // Now read the temp file and recreate content
                byte[] fileContent;
                using (var readStream = File.OpenRead(tempFilePath))
                {
                    fileContent = new byte[readStream.Length];
                    await readStream.ReadAsync(fileContent, 0, fileContent.Length);
                }
                httpRequestMessage.Content = new ByteArrayContent(fileContent);
                
                return new CapturedRequest
                {
                    Method = request.Method,
                    Url = request.Url,
                    Body = $"[LARGE_REQUEST: {totalBytesRead} bytes] {summaryBuilder.ToString().Substring(0, Math.Min(1000, summaryBuilder.Length))}...",
                    QueryString = request.GetQueryString(),
                    Headers = request.GetHeaders(),
                    ContentLength = totalBytesRead,
                    CapturedAt = request.RequestTime,
                    IsTruncated = true,
                    OriginalRequest = httpRequestMessage
                };
            }
            finally
            {
                if (File.Exists(tempFilePath))
                    File.Delete(tempFilePath);
            }
        }
        
        private IRequestCapturingService GetCapturingService(IHttpRequestWrapper request)
        {
            // Factory method to get appropriate capturing service based on request type
            var httpRequestMessage = request.GetUnderlyingRequest<HttpRequestMessage>();
            if (httpRequestMessage != null)
            {
                return new ContentReplacementCapturingService();
            }
            
            var httpRequest = request.GetUnderlyingRequest<HttpRequest>();
            if (httpRequest != null)
            {
                return new StreamWrappingCapturingService();
            }
            
            throw new InvalidOperationException("Unsupported request type for SizeLimitProtectionService");
        }
    }

    /// <summary>
    /// Exception thrown when request is too large for processing
    /// </summary>
    public class RequestTooLargeException : Exception
    {
        public RequestTooLargeException(string message) : base(message) { }
    }
}
