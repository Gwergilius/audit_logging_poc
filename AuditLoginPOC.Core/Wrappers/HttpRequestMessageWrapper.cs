using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using AuditLoginPOC.Core.Interfaces;

namespace AuditLoginPOC.Core.Wrappers
{
    /// <summary>
    /// Wrapper for System.Net.Http.HttpRequestMessage
    /// </summary>
    public class HttpRequestMessageWrapper : IHttpRequestWrapper
    {
        private readonly HttpRequestMessage _request;
        private readonly Lazy<Task<string>> _body;
        
        public HttpRequestMessageWrapper(HttpRequestMessage request)
        {
            _request = request ?? throw new ArgumentNullException(nameof(request));
            _body = new Lazy<Task<string>>(ReadBodyAsync);
            RequestTime = DateTime.UtcNow;
        }
        
        public string GetBody() => _body.Value.GetAwaiter().GetResult();
        public string GetQueryString() 
        {
            if (_request.RequestUri == null) return string.Empty;
            
            // Handle both absolute and relative URIs
            if (_request.RequestUri.IsAbsoluteUri)
            {
                return _request.RequestUri.Query ?? string.Empty;
            }
            else
            {
                // For relative URIs, extract query string manually
                var originalString = _request.RequestUri.OriginalString;
                var queryIndex = originalString.IndexOf('?');
                return queryIndex >= 0 ? originalString.Substring(queryIndex) : string.Empty;
            }
        }
        public Dictionary<string, string> GetHeaders() 
            => _request.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value));
        public long ContentLength => _request.Content?.Headers.ContentLength ?? 0;
        public string Method => _request.Method.Method;
        public string Url 
        {
            get
            {
                if (_request.RequestUri == null) return string.Empty;
                return _request.RequestUri.IsAbsoluteUri 
                    ? _request.RequestUri.ToString() 
                    : _request.RequestUri.OriginalString;
            }
        }
        public string ContentType => _request.Content?.Headers.ContentType?.MediaType ?? string.Empty;
        public DateTime RequestTime { get; }
        
        public object GetUnderlyingRequest() => _request;
        public T GetUnderlyingRequest<T>() where T : class => _request as T;
        
        private async Task<string> ReadBodyAsync()
        {
            if (_request.Content == null) return string.Empty;
            
            try
            {
                return await _request.Content.ReadAsStringAsync();
            }
            catch
            {
                return null; // Content already consumed
            }
        }
    }
}
