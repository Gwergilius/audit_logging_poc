namespace AuditLoginPOC.Core.Models
{
    /// <summary>
    /// Result of DoS protection evaluation
    /// </summary>
    public class ProtectionResult
    {
        public bool IsAllowed { get; set; }
        public AuditStrategy Strategy { get; set; }
        public string ReasonIfDenied { get; set; }
        
        public static ProtectionResult Allowed(AuditStrategy strategy) 
            => new ProtectionResult { IsAllowed = true, Strategy = strategy };
        
        public static ProtectionResult Denied(string reason) 
            => new ProtectionResult { IsAllowed = false, ReasonIfDenied = reason };
    }

    /// <summary>
    /// Audit strategy for request processing
    /// </summary>
    public enum AuditStrategy
    {
        Full,    // Complete request body audit
        Summary, // Only first N bytes + metadata
        Skip     // Skip audit, log only metadata
    }
}
