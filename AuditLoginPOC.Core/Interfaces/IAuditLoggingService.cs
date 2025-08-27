using System.Threading.Tasks;
using AuditLoginPOC.Core.Models;

namespace AuditLoginPOC.Core.Interfaces
{
    /// <summary>
    /// Service for logging audit data
    /// </summary>
    public interface IAuditLoggingService
    {
        Task LogAuditAsync(AuditContext context);
    }
}
