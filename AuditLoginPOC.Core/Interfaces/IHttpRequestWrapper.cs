using System;
using System.Collections.Generic;

namespace AuditLoginPOC.Core.Interfaces
{
    /// <summary>
    /// Unified request wrapper to abstract HttpRequest vs HttpRequestMessage differences
    /// </summary>
    public interface IHttpRequestWrapper
    {
        string GetBody();
        string GetQueryString();
        Dictionary<string, string> GetHeaders();
        long ContentLength { get; }
        string Method { get; }
        string Url { get; }
        string ContentType { get; }
        DateTime RequestTime { get; }
        
        // Access to underlying request for specific operations
        object GetUnderlyingRequest();
        T GetUnderlyingRequest<T>() where T : class;
    }
}
