using System;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using AuditLoginPOC.Core.Interfaces;
using AuditLoginPOC.Core.Models;
using AuditLoginPOC.Core.Wrappers;

namespace AuditLoginPOC.Core.Handlers
{
    /// <summary>
    /// DelegatingHandler that implements composable audit logging
    /// </summary>
    public class ComposableAuditMessageHandler : DelegatingHandler
    {
        private readonly IRequestCapturingService _capturingService;
        private readonly IDoSProtectionService _protectionService;
        private readonly IAuditLoggingService _auditService;
        
        public ComposableAuditMessageHandler(
            IRequestCapturingService capturingService,
            IDoSProtectionService protectionService,
            IAuditLoggingService auditService)
        {
            _capturingService = capturingService ?? throw new ArgumentNullException(nameof(capturingService));
            _protectionService = protectionService ?? throw new ArgumentNullException(nameof(protectionService));
            _auditService = auditService ?? throw new ArgumentNullException(nameof(auditService));
        }
        
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Create wrapper for unified request handling
            var requestWrapper = new HttpRequestMessageWrapper(request);
            
            // Evaluate DoS protection first
            var protection = await _protectionService.EvaluateRequestAsync(requestWrapper);
            
            if (!protection.IsAllowed)
            {
                return new HttpResponseMessage((HttpStatusCode)429)
                {
                    Content = new StringContent(protection.ReasonIfDenied)
                };
            }
            
            // Capture request based on protection strategy
            CapturedRequest capturedRequest = null;
            
            if (protection.Strategy != AuditStrategy.Skip)
            {
                capturedRequest = await _capturingService.CaptureRequestAsync(requestWrapper);
            }
            
            // Process request
            var response = await base.SendAsync(request, cancellationToken);
            
            // Log audit asynchronously
            if (capturedRequest != null)
            {
                var auditContext = new AuditContext
                {
                    CapturedRequest = capturedRequest,
                    UserId = ExtractUserFromJWT(request),
                    ResponseStatusCode = response.StatusCode,
                    ProcessingStrategy = protection.Strategy
                };
                
                _ = Task.Run(() => _auditService.LogAuditAsync(auditContext));
            }
            
            return response;
        }
        
        private string ExtractUserFromJWT(HttpRequestMessage request)
        {
            try
            {
                var authHeader = request.Headers.Authorization?.ToString();
                if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
                {
                    return null;
                }
                
                var token = authHeader.Substring("Bearer ".Length);
                var handler = new System.IdentityModel.Tokens.Jwt.JwtSecurityTokenHandler();
                var jsonToken = handler.ReadJwtToken(token);
                
                return jsonToken.Claims?.FirstOrDefault(x => x.Type == "sub")?.Value;
            }
            catch
            {
                return null;
            }
        }
    }
}
