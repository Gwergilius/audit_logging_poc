using System.Threading.Tasks;
using AuditLoginPOC.Core.Models;

namespace AuditLoginPOC.Core.Interfaces
{
    /// <summary>
    /// Service for capturing HTTP request data
    /// </summary>
    public interface IRequestCapturingService
    {
        Task<CapturedRequest> CaptureRequestAsync(IHttpRequestWrapper request);
    }
}
