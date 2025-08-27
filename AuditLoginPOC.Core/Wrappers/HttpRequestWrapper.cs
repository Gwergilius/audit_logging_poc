using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Web;
using AuditLoginPOC.Core.Interfaces;

namespace AuditLoginPOC.Core.Wrappers
{
    /// <summary>
    /// Wrapper for System.Web.HttpRequest
    /// </summary>
    public class HttpRequestWrapper : IHttpRequestWrapper
    {
        private readonly HttpRequest _request;
        private readonly Lazy<string> _body;
        
        public HttpRequestWrapper(HttpRequest request)
        {
            _request = request ?? throw new ArgumentNullException(nameof(request));
            _body = new Lazy<string>(ReadBody);
            RequestTime = DateTime.UtcNow;
        }
        
        public string GetBody() => _body.Value;
        public string GetQueryString() => _request.QueryString.ToString();
        public Dictionary<string, string> GetHeaders() 
            => _request.Headers.AllKeys.ToDictionary(k => k, k => _request.Headers[k]);
        public long ContentLength => _request.ContentLength;
        public string Method => _request.HttpMethod;
        public string Url => _request.Url.ToString();
        public string ContentType => _request.ContentType ?? string.Empty;
        public DateTime RequestTime { get; }
        
        public object GetUnderlyingRequest() => _request;
        public T GetUnderlyingRequest<T>() where T : class => _request as T;
        
        private string ReadBody()
        {
            if (_request.ContentLength == 0) return string.Empty;
            
            // Note: This will only work if stream hasn't been consumed yet
            // For post-consumption scenarios, return null or empty
            try
            {
                var originalPosition = _request.InputStream.Position;
                _request.InputStream.Position = 0;
                
                using (var reader = new StreamReader(_request.InputStream, Encoding.UTF8, true, 1024, true))
                {
                    var content = reader.ReadToEnd();
                    _request.InputStream.Position = originalPosition;
                    return content;
                }
            }
            catch
            {
                return null; // Stream already consumed
            }
        }
    }
}
