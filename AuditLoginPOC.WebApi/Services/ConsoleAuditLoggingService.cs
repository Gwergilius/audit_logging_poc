using System;
using System.Threading.Tasks;
using AuditLoginPOC.Core.Interfaces;
using AuditLoginPOC.Core.Models;
using Newtonsoft.Json;
using System.Net;

namespace AuditLoginPOC.WebApi.Services
{
    /// <summary>
    /// Simple console-based audit logging service for demonstration
    /// </summary>
    public class ConsoleAuditLoggingService : IAuditLoggingService
    {
        public Task LogAuditAsync(AuditContext context)
        {
            var auditLog = new
            {
                Timestamp = DateTime.UtcNow,
                UserId = context.UserId,
                Method = context.CapturedRequest?.Method,
                Url = context.CapturedRequest?.Url,
                QueryString = context.CapturedRequest?.QueryString,
                Headers = context.CapturedRequest?.Headers,
                RequestBody = context.CapturedRequest?.Body,
                ContentLength = context.CapturedRequest?.ContentLength,
                IsTruncated = context.CapturedRequest?.IsTruncated,
                Hash = context.CapturedRequest?.Hash,
                ControllerName = context.ControllerName,
                ActionName = context.ActionName,
                ModelStateValid = context.ModelStateValid,
                ValidationErrors = context.ValidationErrors,
                ProcessingTimeMs = context.ProcessingTimeMs,
                ResponseStatusCode = context.ResponseStatusCode,
                ExceptionInfo = context.ExceptionInfo,
                ProcessingStrategy = context.ProcessingStrategy,
                AdditionalContext = context.AdditionalContext
            };

            var json = JsonConvert.SerializeObject(auditLog, Formatting.Indented);
            Console.WriteLine("=== AUDIT LOG ===");
            Console.WriteLine(json);
            Console.WriteLine("=================");

            return Task.CompletedTask;
        }
    }
}
