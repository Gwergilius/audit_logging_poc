using System.Threading.Tasks;
using AuditLoginPOC.Core.Models;

namespace AuditLoginPOC.Core.Interfaces
{
    /// <summary>
    /// Service for DoS protection evaluation
    /// </summary>
    public interface IDoSProtectionService
    {
        Task<ProtectionResult> EvaluateRequestAsync(IHttpRequestWrapper request);
        Task<CapturedRequest> ProcessWithProtectionAsync(IHttpRequestWrapper request);
    }
}
