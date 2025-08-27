# HTTP Request Audit Logging Solutions for .NET Framework 4.8 Web API

## Executive Summary

This document analyzes various approaches for implementing comprehensive HTTP request audit logging in a legacy .NET Framework 4.8 MVC Web API application. The solutions are architected using composable services that can be combined to meet specific requirements for request capturing, audit processing, and DoS protection.

## Requirements

- **Target Platform**: .NET Framework 4.8 MVC Web API
- **Scope**: Complete HTTP request auditing (headers, query parameters, raw body content)
- **Raw Data Preservation**: Capture exact request content as received, including malformed JSON
- **Authentication Context**: JWT Bearer token authentication with user information extraction
- **Non-Intrusive**: Minimal impact on existing controller logic and performance
- **Composable Architecture**: Mix and match different capturing, processing, and protection strategies

## Technical Challenges

### The Stream Reading Problem

The fundamental challenge in HTTP request auditing is that `HttpRequest.InputStream` can only be read once. Once the MVC model binding process consumes the stream, the raw content is no longer accessible. This creates a conflict between:

1. **Early Access**: Reading raw content before model binding
2. **Late Context**: Having complete user and validation context after processing
3. **Stream Preservation**: Ensuring controllers can still access request data

## Composable Service Architecture

### Core Service Interfaces

```csharp
// Unified request wrapper to abstract HttpRequest vs HttpRequestMessage differences
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

public interface IRequestCapturingService
{
    Task<CapturedRequest> CaptureRequestAsync(IHttpRequestWrapper request);
}

// Note: Alternative generic approach is also viable:
// public interface IRequestCapturingService<T>
// {
//     Task<CapturedRequest> CaptureRequestAsync(T request);
// }
// This approach is simpler but lacks compile-time constraint validation.

public interface IDoSProtectionService
{
    Task<ProtectionResult> EvaluateRequestAsync(IHttpRequestWrapper request);
}

public interface IAuditLoggingService
{
    Task LogAuditAsync(AuditContext context);
}

public class CapturedRequest
{
    public string Body { get; }
    public string QueryString { get; }
    public Dictionary<string, string> Headers { get; }
    public long ContentLength { get; }
    public DateTime CapturedAt { get; }
    public bool IsTruncated { get; }
    public string Hash { get; }
    public object OriginalRequest { get; }
}

public class ProtectionResult
{
    public bool IsAllowed { get; }
    public AuditStrategy Strategy { get; }
    public string ReasonIfDenied { get; }
    
    public static ProtectionResult Allowed(AuditStrategy strategy) 
        => new ProtectionResult { IsAllowed = true, Strategy = strategy };
    
    public static ProtectionResult Denied(string reason) 
        => new ProtectionResult { IsAllowed = false, ReasonIfDenied = reason };
}

public enum AuditStrategy
{
    Full,    // Complete request body audit
    Summary, // Only first N bytes + metadata
    Skip     // Skip audit, log only metadata
}
```

### Request Wrapper Implementations

```csharp
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
    public long ContentLength => _request.ContentLength ?? 0;
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
    public string GetQueryString() => _request.RequestUri?.Query ?? string.Empty;
    public Dictionary<string, string> GetHeaders() 
        => _request.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value));
    public long ContentLength => _request.Content?.Headers.ContentLength ?? 0;
    public string Method => _request.Method.Method;
    public string Url => _request.RequestUri?.ToString() ?? string.Empty;
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
```

## Request Capturing Service Implementations

### ContentReplacementCapturingService

**Strategy**: Read content → Recreate HttpContent → Preserve headers
**Compatible With**: DelegatingHandler, Early Capture + Late Log patterns

```csharp
public class ContentReplacementCapturingService : IRequestCapturingService
{
    public async Task<CapturedRequest> CaptureRequestAsync(IHttpRequestWrapper request)
    {
        // This service only works with HttpRequestMessage-based wrappers
        var httpRequestMessage = request.GetUnderlyingRequest<HttpRequestMessage>();
        if (httpRequestMessage == null)
        {
            throw new InvalidOperationException(
                "ContentReplacementCapturingService requires HttpRequestMessage");
        }

        if (httpRequestMessage.Content == null)
            return CapturedRequest.Empty;

        // Read original content
        var requestBody = await httpRequestMessage.Content.ReadAsStringAsync();
        
        // Preserve original content type and headers
        var originalContentType = httpRequestMessage.Content.Headers.ContentType;
        var originalHeaders = httpRequestMessage.Content.Headers.ToList();
        
        // Recreate content with same data and headers to solve single-read issue
        httpRequestMessage.Content = new StringContent(requestBody, 
            Encoding.UTF8, originalContentType?.MediaType ?? "application/json");
            
        // Restore all original headers
        foreach (var header in originalHeaders)
        {
            httpRequestMessage.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        
        return new CapturedRequest
        {
            Body = requestBody,
            QueryString = request.GetQueryString(),
            Headers = request.GetHeaders(),
            ContentLength = requestBody?.Length ?? 0,
            CapturedAt = request.RequestTime,
            OriginalRequest = httpRequestMessage
        };
    }
}
```

### StreamWrappingCapturingService

**Strategy**: Replace InputStream → Transparent capture → Reflection-based stream swap
**Compatible With**: HTTP Module patterns

```csharp
public class StreamWrappingCapturingService : IRequestCapturingService
{
    public Task<CapturedRequest> CaptureRequestAsync(IHttpRequestWrapper request)
    {
        // This service only works with HttpRequest-based wrappers
        var httpRequest = request.GetUnderlyingRequest<HttpRequest>();
        if (httpRequest == null)
        {
            throw new InvalidOperationException(
                "StreamWrappingCapturingService requires HttpRequest");
        }
        
        // Replace input stream with auditable wrapper to solve single-read issue
        var originalStream = httpRequest.InputStream;
        var auditableStream = new AuditableStream(originalStream);
        
        // Use reflection to replace the internal stream reference
        var inputStreamField = typeof(HttpRequest).GetField("_inputStream", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        inputStreamField?.SetValue(httpRequest, auditableStream);
        
        return Task.FromResult(new CapturedRequest
        {
            Body = null, // Will be populated when stream is read
            QueryString = request.GetQueryString(),
            Headers = request.GetHeaders(),
            ContentLength = request.ContentLength,
            CapturedAt = request.RequestTime,
            AuditableStream = auditableStream, // Internal reference for later data retrieval
            OriginalRequest = httpRequest
        });
    }
}

public class AuditableStream : Stream
{
    private readonly Stream _innerStream;
    private readonly MemoryStream _capturedData;
    
    public AuditableStream(Stream innerStream)
    {
        _innerStream = innerStream;
        _capturedData = new MemoryStream();
    }
    
    public override int Read(byte[] buffer, int offset, int count)
    {
        var bytesRead = _innerStream.Read(buffer, offset, count);
        if (bytesRead > 0)
        {
            // Capture data as it flows through the stream
            _capturedData.Write(buffer, offset, bytesRead);
        }
        return bytesRead;
    }
    
    public string GetCapturedData()
    {
        return Encoding.UTF8.GetString(_capturedData.ToArray());
    }
    
    // Stream interface implementation...
    public override bool CanRead => _innerStream.CanRead;
    public override bool CanSeek => _innerStream.CanSeek;
    public override bool CanWrite => _innerStream.CanWrite;
    public override long Length => _innerStream.Length;
    public override long Position 
    { 
        get => _innerStream.Position; 
        set => _innerStream.Position = value; 
    }
    
    public override void Flush() => _innerStream.Flush();
    public override long Seek(long offset, SeekOrigin origin) 
        => _innerStream.Seek(offset, origin);
    public override void SetLength(long value) => _innerStream.SetLength(value);
    public override void Write(byte[] buffer, int offset, int count) 
        => _innerStream.Write(buffer, offset, count);
}
```

### PostProcessingCapturingService

**Strategy**: Access post-model-binding data → No raw body access → Validation context
**Compatible With**: Action Filter patterns (with limitations)

```csharp
public class PostProcessingCapturingService : IRequestCapturingService
{
    public Task<CapturedRequest> CaptureRequestAsync(IHttpRequestWrapper request)
    {
        // Post-processing service works with any wrapper but has limitations
        return Task.FromResult(new CapturedRequest
        {
            Body = null, // Raw body not accessible after model binding
            QueryString = request.GetQueryString(),
            Headers = request.GetHeaders(),
            ContentLength = request.ContentLength,
            CapturedAt = request.RequestTime,
            OriginalRequest = request.GetUnderlyingRequest()
        });
    }
    
    // Extended method for ActionFilter context
    public CapturedRequest CaptureRequest(ActionExecutingContext filterContext)
    {
        var request = filterContext.HttpContext.Request;
        var wrapper = new HttpRequestWrapper(request);
        
        return new CapturedRequest
        {
            Body = null, // Raw body not accessible after model binding
            QueryString = wrapper.GetQueryString(),
            Headers = wrapper.GetHeaders(),
            ContentLength = wrapper.ContentLength,
            CapturedAt = wrapper.RequestTime,
            ProcessedParameters = filterContext.ActionParameters, // Deserialized parameters only
            ControllerName = filterContext.ActionDescriptor.ControllerDescriptor.ControllerName,
            ActionName = filterContext.ActionDescriptor.ActionName,
            OriginalRequest = request
        };
    }
}
```

## DoS Protection Service Implementations

### SizeLimitProtectionService

**Strategy**: Memory threshold → Temporary file fallback → Size-based rejection

```csharp
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
            using (var fileStream = File.Create(tempFilePath))
            using (var originalStream = await httpRequestMessage.Content.ReadAsStreamAsync())
            {
                var buffer = new byte[8192];
                var summaryBuilder = new StringBuilder();
                var totalBytesRead = 0;
                
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
                
                // Recreate content from temp file
                var fileContent = File.ReadAllBytes(tempFilePath);
                httpRequestMessage.Content = new ByteArrayContent(fileContent);
                
                return new CapturedRequest
                {
                    Body = $"[LARGE_REQUEST: {totalBytesRead} bytes] {summaryBuilder.ToString().Substring(0, Math.Min(1000, summaryBuilder.Length))}...",
                    QueryString = request.GetQueryString(),
                    Headers = request.GetHeaders(),
                    ContentLength = totalBytesRead,
                    CapturedAt = request.RequestTime,
                    IsTruncated = true,
                    OriginalRequest = httpRequestMessage
                };
            }
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
```

### StreamingDigestProtectionService

**Strategy**: Chunked reading → Preview capture → Cryptographic hash → Bounded memory

```csharp
public class StreamingDigestProtectionService : IDoSProtectionService
{
    private const int MAX_AUDIT_SIZE = 512 * 1024; // 512KB preview
    private const int CHUNK_SIZE = 8192;
    
    public Task<ProtectionResult> EvaluateRequestAsync(IHttpRequestWrapper request)
    {
        // Always allows requests but with digest strategy
        return Task.FromResult(ProtectionResult.Allowed(AuditStrategy.Summary));
    }
    
    public async Task<CapturedRequest> ProcessWithProtectionAsync(IHttpRequestWrapper request)
    {
        // This service requires HttpRequestMessage for stream access
        var httpRequestMessage = request.GetUnderlyingRequest<HttpRequestMessage>();
        if (httpRequestMessage?.Content == null)
        {
            return CapturedRequest.Empty;
        }
        
        using (var originalStream = await httpRequestMessage.Content.ReadAsStreamAsync())
        using (var sha256 = SHA256.Create())
        using (var previewBuffer = new MemoryStream())
        {
            var buffer = new byte[CHUNK_SIZE];
            var totalBytes = 0;
            var isComplete = true;
            var recreatedContent = new List<byte>();
            
            int bytesRead;
            while ((bytesRead = await originalStream.ReadAsync(buffer, 0, CHUNK_SIZE)) > 0)
            {
                recreatedContent.AddRange(buffer.Take(bytesRead));
                sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
                
                if (totalBytes < MAX_AUDIT_SIZE)
                {
                    var remainingSpace = MAX_AUDIT_SIZE - totalBytes;
                    var bytesToWrite = Math.Min(bytesRead, remainingSpace);
                    previewBuffer.Write(buffer, 0, bytesToWrite);
                    
                    if (bytesToWrite < bytesRead) isComplete = false;
                }
                else
                {
                    isComplete = false;
                }
                
                totalBytes += bytesRead;
            }
            
            sha256.TransformFinalBlock(new byte[0], 0, 0);
            
            // Recreate request content
            httpRequestMessage.Content = new ByteArrayContent(recreatedContent.ToArray());
            
            return new CapturedRequest
            {
                Body = Encoding.UTF8.GetString(previewBuffer.ToArray()),
                QueryString = request.GetQueryString(),
                Headers = request.GetHeaders(),
                ContentLength = totalBytes,
                CapturedAt = request.RequestTime,
                Hash = Convert.ToBase64String(sha256.Hash),
                IsTruncated = !isComplete,
                OriginalRequest = httpRequestMessage
            };
        }
    }
}
```

### CircuitBreakerProtectionService

**Strategy**: Client identification → Rate limiting → Adaptive audit strategy

```csharp
public class CircuitBreakerProtectionService : IDoSProtectionService
{
    private static readonly ConcurrentDictionary<string, ClientMetrics> _clientMetrics = new();
    private const int MAX_REQUESTS_PER_MINUTE = 10;
    private const int MAX_TOTAL_SIZE_PER_MINUTE = 10 * 1024 * 1024; // 10MB
    
    public Task<ProtectionResult> EvaluateRequestAsync(IHttpRequestWrapper request)
    {
        var clientId = GetClientIdentifier(request);
        var metrics = _clientMetrics.GetOrAdd(clientId, _ => new ClientMetrics());
        
        if (metrics.IsRateLimited())
        {
            return Task.FromResult(ProtectionResult.Denied("Rate limit exceeded for audit logging"));
        }
        
        var contentLength = request.ContentLength;
        metrics.RecordRequest(contentLength);
        
        var strategy = DetermineAuditStrategy(metrics, contentLength);
        return Task.FromResult(ProtectionResult.Allowed(strategy));
    }
    
    private AuditStrategy DetermineAuditStrategy(ClientMetrics metrics, long contentLength)
    {
        if (metrics.RequestCount > MAX_REQUESTS_PER_MINUTE * 0.8) // 80% of limit
            return AuditStrategy.Summary;
            
        if (metrics.TotalSize > MAX_TOTAL_SIZE_PER_MINUTE * 0.8) // 80% of limit
            return AuditStrategy.Summary;
            
        if (contentLength > 1024 * 1024) // > 1MB
            return AuditStrategy.Summary;
            
        return AuditStrategy.Full;
    }
    
    private string GetClientIdentifier(IHttpRequestWrapper request)
    {
        var headers = request.GetHeaders();
        var ip = GetClientIP(request);
        var userAgent = headers.GetValueOrDefault("User-Agent", "unknown");
        var jwtSubject = GetJWTSubject(request) ?? "anonymous";
        
        return $"{ip}|{userAgent.GetHashCode()}|{jwtSubject}";
    }
    
    private string GetClientIP(IHttpRequestWrapper request)
    {
        var headers = request.GetHeaders();
        
        // Check common proxy headers first
        var forwardedFor = headers.GetValueOrDefault("X-Forwarded-For");
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            return forwardedFor.Split(',')[0].Trim();
        }
        
        var realIP = headers.GetValueOrDefault("X-Real-IP");
        if (!string.IsNullOrEmpty(realIP))
        {
            return realIP;
        }
        
        // Fallback to request-specific IP extraction
        var httpRequest = request.GetUnderlyingRequest<HttpRequest>();
        if (httpRequest != null)
        {
            return httpRequest.UserHostAddress;
        }
        
        // For HttpRequestMessage, IP extraction is more complex and would need OWIN context
        return "unknown";
    }
    
    private string GetJWTSubject(IHttpRequestWrapper request)
    {
        var headers = request.GetHeaders();
        var authHeader = headers.GetValueOrDefault("Authorization");
        
        if (string.IsNullOrEmpty(authHeader) || !authHeader.StartsWith("Bearer "))
        {
            return null;
        }
        
        try
        {
            var token = authHeader.Substring("Bearer ".Length);
            var handler = new JwtSecurityTokenHandler();
            var jsonToken = handler.ReadJwtToken(token);
            
            return jsonToken.Claims?.FirstOrDefault(x => x.Type == "sub")?.Value;
        }
        catch
        {
            return null;
        }
    }
}

public class ClientMetrics
{
    private readonly Queue<DateTime> _requestTimes = new();
    private readonly Queue<(DateTime, long)> _sizeTimes = new();
    private readonly object _lock = new object();
    
    public int RequestCount { get; private set; }
    public long TotalSize { get; private set; }
    
    public void RecordRequest(long size)
    {
        lock (_lock)
        {
            var now = DateTime.UtcNow;
            var cutoff = now.AddMinutes(-1);
            
            // Clean old entries
            while (_requestTimes.Count > 0 && _requestTimes.Peek() < cutoff)
                _requestTimes.Dequeue();
                
            while (_sizeTimes.Count > 0 && _sizeTimes.Peek().Item1 < cutoff)
                _sizeTimes.Dequeue();
            
            // Add new entry
            _requestTimes.Enqueue(now);
            _sizeTimes.Enqueue((now, size));
            
            // Update counters
            RequestCount = _requestTimes.Count;
            TotalSize = _sizeTimes.Sum(x => x.Item2);
        }
    }
    
    public bool IsRateLimited()
    {
        return RequestCount > MAX_REQUESTS_PER_MINUTE || 
               TotalSize > MAX_TOTAL_SIZE_PER_MINUTE;
    }
}
```

### BackgroundQueueProtectionService

**Strategy**: Immediate small request handling → Large request queuing → Background processing

```csharp
public class BackgroundQueueProtectionService : IDoSProtectionService
{
    private readonly IBackgroundTaskQueue _auditQueue;
    private const int MAX_IMMEDIATE_SIZE = 64 * 1024; // 64KB
    
    public Task<ProtectionResult> EvaluateRequestAsync(HttpRequestMessage request)
    {
        var contentLength = request.Content?.Headers.ContentLength ?? 0;
        
        if (contentLength <= MAX_IMMEDIATE_SIZE)
        {
            return Task.FromResult(ProtectionResult.Allowed(AuditStrategy.Full));
        }
        
        return Task.FromResult(ProtectionResult.Allowed(AuditStrategy.Skip)); // Process in background
    }
    
    public async Task<CapturedRequest> ProcessWithProtectionAsync(HttpRequestMessage request)
    {
        var protection = await EvaluateRequestAsync(request);
        
        if (protection.Strategy == AuditStrategy.Full)
        {
            // Small requests - process immediately
            return await _contentCapturingService.CaptureRequestAsync(request);
        }
        else
        {
            // Large requests - queue for background processing
            var auditId = Guid.NewGuid();
            await StreamToTempFileAsync(request, auditId);
            
            _auditQueue.QueueBackgroundWorkItem(async token =>
            {
                await ProcessLargeAuditAsync(auditId, request);
            });
            
            return CapturedRequest.Deferred(auditId);
        }
    }
}
```

## Audit Processing Pattern Implementations

### DelegatingHandler Pattern

**Architecture**: HTTP Message Handler → Service Composition → Early Pipeline Processing

```csharp
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
        _capturingService = capturingService;
        _protectionService = protectionService;
        _auditService = auditService;
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
            return new HttpResponseMessage(HttpStatusCode.TooManyRequests)
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
}
```

**Registration**:
```csharp
// In WebApiConfig.cs - Dependency Injection Setup
var container = new Container();
container.RegisterSingleton<IRequestCapturingService, ContentReplacementCapturingService>();
container.RegisterSingleton<IDoSProtectionService, SizeLimitProtectionService>();
container.RegisterSingleton<IAuditLoggingService, DatabaseAuditLoggingService>();

config.MessageHandlers.Insert(0, new ComposableAuditMessageHandler(
    container.GetInstance<IRequestCapturingService>(),
    container.GetInstance<IDoSProtectionService>(),
    container.GetInstance<IAuditLoggingService>()
));
```

### HTTP Module Pattern

**Architecture**: HTTP Module → Service Composition → System-Level Processing

```csharp
public class ComposableAuditHttpModule : IHttpModule
{
    private IRequestCapturingService _capturingService;
    private IDoSProtectionService _protectionService;
    private IAuditLoggingService _auditService;
    
    public void Init(HttpApplication context)
    {
        // Resolve services from DI container
        _capturingService = DependencyResolver.Current.GetService<IRequestCapturingService>();
        _protectionService = DependencyResolver.Current.GetService<IDoSProtectionService>();
        _auditService = DependencyResolver.Current.GetService<IAuditLoggingService>();
        
        context.BeginRequest += OnBeginRequest;
        context.EndRequest += OnEndRequest;
    }
    
    private async void OnBeginRequest(object sender, EventArgs e)
    {
        var httpContext = ((HttpApplication)sender).Context;
        var request = httpContext.Request;
        var requestWrapper = new HttpRequestWrapper(request);
        
        try
        {
            // Evaluate DoS protection
            var protection = await _protectionService.EvaluateRequestAsync(requestWrapper);
            
            if (!protection.IsAllowed)
            {
                httpContext.Response.StatusCode = 429;
                httpContext.Response.Write(protection.ReasonIfDenied);
                httpContext.Response.End();
                return;
            }
            
            // Capture request data
            if (protection.Strategy != AuditStrategy.Skip)
            {
                var capturedRequest = await _capturingService.CaptureRequestAsync(requestWrapper);
                httpContext.Items["CapturedAuditRequest"] = capturedRequest;
            }
            
            httpContext.Items["AuditProtectionStrategy"] = protection.Strategy;
        }
        catch (Exception ex)
        {
            // Log error but don't break the request pipeline
            httpContext.Items["AuditCaptureError"] = ex;
        }
    }
    
    private async void OnEndRequest(object sender, EventArgs e)
    {
        var httpContext = ((HttpApplication)sender).Context;
        var capturedRequest = httpContext.Items["CapturedAuditRequest"] as CapturedRequest;
        
        if (capturedRequest != null)
        {
            var auditContext = new AuditContext
            {
                CapturedRequest = capturedRequest,
                UserId = ExtractUserFromContext(httpContext),
                ResponseStatusCode = (HttpStatusCode)httpContext.Response.StatusCode,
                ProcessingStrategy = (AuditStrategy)httpContext.Items["AuditProtectionStrategy"]
            };
            
            await _auditService.LogAuditAsync(auditContext);
        }
    }
}
```

### Action Filter Pattern

**Architecture**: Action Filter → Service Composition → Post-Processing Focus

```csharp
public class ComposableAuditActionFilter : ActionFilterAttribute
{
    private readonly IRequestCapturingService _capturingService;
    private readonly IAuditLoggingService _auditService;
    
    public ComposableAuditActionFilter()
    {
        // Note: PostProcessingCapturingService is the only compatible service
        _capturingService = new PostProcessingCapturingService();
        _auditService = DependencyResolver.Current.GetService<IAuditLoggingService>();
    }
    
    public override void OnActionExecuting(ActionExecutingContext filterContext)
    {
        try
        {
            var request = filterContext.HttpContext.Request;
            var requestWrapper = new HttpRequestWrapper(request);
            var capturedRequest = await _capturingService.CaptureRequestAsync(requestWrapper);
            
            filterContext.HttpContext.Items["CapturedAuditRequest"] = capturedRequest;
        }
        catch (Exception ex)
        {
            filterContext.HttpContext.Items["AuditCaptureError"] = ex;
        }
    }
    
    public override void OnActionExecuted(ActionExecutedContext filterContext)
    {
        var capturedRequest = filterContext.HttpContext.Items["CapturedAuditRequest"] as CapturedRequest;
        
        if (capturedRequest != null)
        {
            var auditContext = new AuditContext
            {
                CapturedRequest = capturedRequest,
                UserId = GetUserFromContext(filterContext),
                UserClaims = GetUserClaims(filterContext),
                ModelStateValid = filterContext.Controller.ViewData.ModelState.IsValid,
                ValidationErrors = GetValidationErrors(filterContext.Controller.ViewData.ModelState),
                ProcessingTimeMs = (DateTime.UtcNow - capturedRequest.CapturedAt).TotalMilliseconds,
                ResponseStatusCode = GetResponseStatusCode(filterContext),
                ExceptionInfo = filterContext.Exception?.ToString(),
                ProcessingStrategy = AuditStrategy.Summary // Post-processing only
            };
            
            Task.Run(() => _auditService.LogAuditAsync(auditContext));
        }
    }
}
```

### Early Capture + Late Log Pattern

**Architecture**: Early Handler → Data Storage → Controller-Level Logging

```csharp
public class EarlyCaptureMessageHandler : DelegatingHandler
{
    private readonly IRequestCapturingService _capturingService;
    private readonly IDoSProtectionService _protectionService;
    
    public EarlyCaptureMessageHandler(
        IRequestCapturingService capturingService,
        IDoSProtectionService protectionService)
    {
        _capturingService = capturingService;
        _protectionService = protectionService;
    }
    
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Evaluate protection and capture early
        var protection = await _protectionService.EvaluateRequestAsync(request);
        
        if (!protection.IsAllowed)
        {
            return new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent(protection.ReasonIfDenied)
            };
        }
        
        if (protection.Strategy != AuditStrategy.Skip)
        {
            var capturedRequest = await _capturingService.CaptureRequestAsync(request);
            request.Properties["EarlyCapturedAuditData"] = capturedRequest;
            request.Properties["AuditProtectionStrategy"] = protection.Strategy;
        }
        
        return await base.SendAsync(request, cancellationToken);
    }
}

public abstract class ComposableAuditControllerBase : ApiController
{
    private readonly IAuditLoggingService _auditService;
    
    protected ComposableAuditControllerBase()
    {
        _auditService = DependencyResolver.Current.GetService<IAuditLoggingService>();
    }
    
    protected override void Initialize(ControllerContext controllerContext)
    {
        base.Initialize(controllerContext);
        
        // Register for cleanup after action execution
        controllerContext.Controller.ActionInvoker = 
            new AuditActionInvoker(controllerContext.Controller.ActionInvoker, LogAuditAsync);
    }
    
    protected virtual async Task LogAuditAsync()
    {
        var capturedRequest = Request.Properties["EarlyCapturedAuditData"] as CapturedRequest;
        if (capturedRequest == null) return;
        
        var auditContext = new AuditContext
        {
            CapturedRequest = capturedRequest,
            UserId = User?.Identity?.Name,
            UserClaims = GetUserClaimsFromJWT(),
            ControllerName = ControllerContext.ControllerDescriptor.ControllerName,
            ActionName = ActionContext.ActionDescriptor.ActionName,
            ModelStateValid = ModelState.IsValid,
            ValidationErrors = GetValidationErrors(),
            ProcessingTimeMs = (DateTime.UtcNow - capturedRequest.CapturedAt).TotalMilliseconds,
            ProcessingStrategy = (AuditStrategy)Request.Properties["AuditProtectionStrategy"],
            AdditionalContext = GetAdditionalAuditContext()
        };
        
        await _auditService.LogAuditAsync(auditContext);
    }
    
    protected virtual Dictionary<string, object> GetAdditionalAuditContext() 
        => new Dictionary<string, object>();
}
```

## Stream Handling Strategies Comparison

| Strategy | Complexity | Performance | Reliability | Compatibility |
|----------|------------|-------------|-------------|---------------|
| Content Replacement | Medium | Medium | High | High |
| Stream Wrapping | High | High | Medium | Medium |
| Position Reset | Low | High | Low | Medium |

## Performance Considerations

### Memory Usage
- **Content Replacement**: Doubles memory usage for request body during processing
- **Stream Wrapping**: Incremental memory usage as data flows through
- **Position Reset**: Minimal additional memory overhead
- **Hybrid Approach**: Persistent storage of raw data until request completion

### Processing Overhead
- **Early Pipeline**: Minimal impact on business logic performance
- **Action Level**: Potential delay in action execution start
- **Stream Manipulation**: CPU overhead varies by strategy

### Scalability Factors
- Large file uploads require careful memory management
- High-frequency APIs need efficient audit data storage
- Concurrent request handling must consider thread safety

## Security Implications

### Sensitive Data Exposure
- Raw request logging may capture sensitive information (passwords, API keys)
- Consider selective field masking or encryption for audit storage
- JWT token information requires careful handling in logs

### Performance Impact on Authentication
- Early pipeline capture occurs before authentication
- Late logging has full security context but may miss rejected requests

## Integration Requirements

### Database Schema Considerations
```sql
-- Example audit table structure
CREATE TABLE AuditLog (
    Id UNIQUEIDENTIFIER PRIMARY KEY,
    Timestamp DATETIME2 NOT NULL,
    UserId NVARCHAR(450),
    Method NVARCHAR(10) NOT NULL,
    Url NVARCHAR(2048) NOT NULL,
    QueryString NVARCHAR(MAX),
    Headers NVARCHAR(MAX),
    RequestBody NVARCHAR(MAX),
    ModelStateValid BIT,
    ValidationErrors NVARCHAR(MAX),
    ProcessingTimeMs FLOAT,
    AdditionalContext NVARCHAR(MAX)
);
```

### Configuration Requirements
Each solution requires specific configuration in different parts of the application:

- **DelegatingHandler**: WebApiConfig registration
- **HTTP Module**: web.config registration
- **Action Filter**: Global filter or attribute-based application
- **Hybrid**: Multiple component coordination

## Testing Strategies

### Unit Testing Considerations
- Mock HTTP context and streams for isolated component testing
- Test stream state preservation across different scenarios
- Validate audit data accuracy under various content types

### Integration Testing Requirements
- End-to-end request flow validation
- Performance testing under load
- Security testing for sensitive data handling

## Service Compatibility Matrix

The following matrix shows which service implementations can be combined with each processing pattern:

| Processing Pattern | Compatible Capturing Services | Compatible Protection Services | Raw Body Access | Complexity Level |
|--------------------|-------------------------------|--------------------------------|-----------------|------------------|
| DelegatingHandler | ContentReplacement | SizeLimit, StreamingDigest, CircuitBreaker, BackgroundQueue | ✅ Full | Medium |
| HTTP Module | StreamWrapping | SizeLimit, StreamingDigest, CircuitBreaker | ✅ Full | High |
| Action Filter | PostProcessing | CircuitBreaker | ❌ Processed Only | Low |
| Early Capture + Late Log | ContentReplacement | SizeLimit, StreamingDigest, CircuitBreaker, BackgroundQueue | ✅ Full | High |

### Detailed Compatibility Rules

**DelegatingHandler Pattern**:
- ✅ **ContentReplacementCapturingService**: Works with HttpRequestMessageWrapper
- ❌ **StreamWrappingCapturingService**: Requires HttpRequestWrapper with HttpRequest
- ❌ **PostProcessingCapturingService**: No ActionContext available
- ✅ **All Protection Services**: Early pipeline position supports all strategies

**HTTP Module Pattern**:
- ❌ **ContentReplacementCapturingService**: Requires HttpRequestMessage (not available in HTTP Module)
- ✅ **StreamWrappingCapturingService**: Works with HttpRequestWrapper with HttpRequest
- ❌ **PostProcessingCapturingService**: No ActionContext available
- ✅ **Most Protection Services**: System-level access supports most strategies
- ❌ **BackgroundQueueProtectionService**: Complex integration with module lifecycle

**Action Filter Pattern**:
- ❌ **ContentReplacementCapturingService**: Stream already consumed by model binding
- ❌ **StreamWrappingCapturingService**: Stream already consumed by model binding
- ✅ **PostProcessingCapturingService**: Works with any wrapper but provides no raw body
- ✅ **CircuitBreakerProtectionService**: Can implement client tracking
- ❌ **Other Protection Services**: Limited request access, no stream control

**Early Capture + Late Log Pattern**:
- ✅ **ContentReplacementCapturingService**: Early handler uses HttpRequestMessageWrapper
- ❌ **StreamWrappingCapturingService**: Early handler doesn't use HttpRequest
- ❌ **PostProcessingCapturingService**: Early capture phase has no ActionContext
- ✅ **All Protection Services**: Flexible architecture supports all strategies

### Request Wrapper Compatibility

**HttpRequestMessageWrapper**:
- Used in DelegatingHandler and Early Capture patterns
- Supports ContentReplacementCapturingService
- Compatible with all protection services that handle HttpRequestMessage

**HttpRequestWrapper**:
- Used in HTTP Module patterns  
- Supports StreamWrappingCapturingService
- Compatible with protection services that can work with HttpRequest

**Both Wrappers**:
- PostProcessingCapturingService works with any wrapper (but provides limited functionality)
- All services receive unified interface regardless of underlying request type
- Generic service implementations possible: `IRequestCapturingService<HttpRequestMessageWrapper>`, `IRequestCapturingService<HttpRequestWrapper>`

## Recommended Service Combinations

### For General Purpose APIs
```csharp
// Configuration: DelegatingHandler + ContentReplacement + SizeLimit
services.AddSingleton<IRequestCapturingService, ContentReplacementCapturingService>();
services.AddSingleton<IDoSProtectionService, SizeLimitProtectionService>();
services.AddSingleton<IAuditLoggingService, DatabaseAuditLoggingService>();
```

**Benefits**: Balanced complexity, predictable resource usage, full raw data access

### For High-Security Environments
```csharp
// Configuration: HTTP Module + StreamWrapping + StreamingDigest
services.AddSingleton<IRequestCapturingService, StreamWrappingCapturingService>();
services.AddSingleton<IDoSProtectionService, StreamingDigestProtectionService>();
services.AddSingleton<IAuditLoggingService, EncryptedAuditLoggingService>();
```

**Benefits**: Maximum data integrity, cryptographic verification, transparent capture

### For Public APIs with Abuse Potential
```csharp
// Configuration: DelegatingHandler + ContentReplacement + CircuitBreaker
services.AddSingleton<IRequestCapturingService, ContentReplacementCapturingService>();
services.AddSingleton<IDoSProtectionService, CircuitBreakerProtectionService>();
services.AddSingleton<IAuditLoggingService, DatabaseAuditLoggingService>();
```

**Benefits**: Adaptive protection, client behavior tracking, attack mitigation

### For High-Throughput Systems
```csharp
// Configuration: Early Capture + ContentReplacement + BackgroundQueue
services.AddSingleton<IRequestCapturingService, ContentReplacementCapturingService>();
services.AddSingleton<IDoSProtectionService, BackgroundQueueProtectionService>();
services.AddSingleton<IAuditLoggingService, QueuedAuditLoggingService>();
```

**Benefits**: Non-blocking processing, scalable background handling, optimal performance

### For Legacy System Integration
```csharp
// Configuration: Action Filter + PostProcessing + CircuitBreaker
services.AddSingleton<IRequestCapturingService, PostProcessingCapturingService>();
services.AddSingleton<IDoSProtectionService, CircuitBreakerProtectionService>();
services.AddSingleton<IAuditLoggingService, DatabaseAuditLoggingService>();
```

**Benefits**: Minimal code changes, selective application, validation context access  
**Limitations**: No raw request body access, only processed data

### Protection Strategy 1: Size Limits with Temp File Fallback

**Architecture**: Memory threshold → Temporary file storage → Cleanup

**Implementation Approach**:
```csharp
public class SafeAuditMessageHandler : DelegatingHandler
{
    private const int MAX_MEMORY_SIZE = 1024 * 1024; // 1MB
    private const int MAX_TOTAL_SIZE = 50 * 1024 * 1024; // 50MB
    
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (request.Content == null) 
            return await base.SendAsync(request, cancellationToken);

        var contentLength = request.Content.Headers.ContentLength ?? 0;
        
        // Reject oversized requests immediately
        if (contentLength > MAX_TOTAL_SIZE)
        {
            return new HttpResponseMessage(HttpStatusCode.RequestEntityTooLarge)
            {
                Content = new StringContent("Request too large for audit logging")
            };
        }

        string auditData;
        
        if (contentLength <= MAX_MEMORY_SIZE)
        {
            // Small requests - keep in memory
            auditData = await request.Content.ReadAsStringAsync();
            request.Content = new StringContent(auditData, 
                Encoding.UTF8, request.Content.Headers.ContentType?.MediaType);
        }
        else
        {
            // Large requests - use temporary file with summary
            auditData = await ProcessLargeRequestWithSummary(request);
        }

        await LogAuditAsync(request, auditData, contentLength);
        return await base.SendAsync(request, cancellationToken);
    }
}
```

**Advantages**:
- Fixed maximum memory usage per request
- Complete audit data preserved for small requests
- Graceful handling of large requests with summaries
- Immediate rejection of oversized requests

**Disadvantages**:
- Disk I/O overhead for large requests
- Temporary file management complexity
- Potential disk space exhaustion
- Summary may miss important audit details

### Protection Strategy 2: Streaming with Content Digest

**Architecture**: Chunked reading → Preview capture → Cryptographic hash → Bounded memory

**Implementation Approach**:
```csharp
public class DigestAuditHandler : DelegatingHandler
{
    private const int MAX_AUDIT_SIZE = 512 * 1024; // 512KB preview
    private const int CHUNK_SIZE = 8192;
    
    private async Task<AuditInfo> ProcessStreamWithDigest(HttpRequestMessage request)
    {
        using (var originalStream = await request.Content.ReadAsStreamAsync())
        using (var sha256 = SHA256.Create())
        using (var previewBuffer = new MemoryStream())
        {
            var buffer = new byte[CHUNK_SIZE];
            var totalBytes = 0;
            var isComplete = true;
            var recreatedContent = new List<byte>();
            
            int bytesRead;
            while ((bytesRead = await originalStream.ReadAsync(buffer, 0, CHUNK_SIZE)) > 0)
            {
                recreatedContent.AddRange(buffer.Take(bytesRead));
                sha256.TransformBlock(buffer, 0, bytesRead, null, 0);
                
                if (totalBytes < MAX_AUDIT_SIZE)
                {
                    var remainingSpace = MAX_AUDIT_SIZE - totalBytes;
                    var bytesToWrite = Math.Min(bytesRead, remainingSpace);
                    previewBuffer.Write(buffer, 0, bytesToWrite);
                    
                    if (bytesToWrite < bytesRead) isComplete = false;
                }
                else
                {
                    isComplete = false;
                }
                
                totalBytes += bytesRead;
            }
            
            sha256.TransformFinalBlock(new byte[0], 0, 0);
            request.Content = new ByteArrayContent(recreatedContent.ToArray());
            
            return new AuditInfo
            {
                Preview = Encoding.UTF8.GetString(previewBuffer.ToArray()),
                TotalSize = totalBytes,
                SHA256Hash = Convert.ToBase64String(sha256.Hash),
                IsComplete = isComplete,
                TruncatedAt = isComplete ? null : MAX_AUDIT_SIZE
            };
        }
    }
}
```

**Advantages**:
- Guaranteed bounded memory usage
- Cryptographic integrity verification
- Streaming processing without disk I/O
- Forensic value through content hashing

**Disadvantages**:
- Incomplete audit data for large requests
- CPU overhead for hash calculation
- Preview may not capture critical data at end of request
- Complex stream reconstruction logic

### Protection Strategy 3: Circuit Breaker with Client Tracking

**Architecture**: Client identification → Rate limiting → Adaptive audit strategy

**Implementation Approach**:
```csharp
public class CircuitBreakerAuditHandler : DelegatingHandler
{
    private static readonly ConcurrentDictionary<string, ClientMetrics> _clientMetrics = new();
    private const int MAX_REQUESTS_PER_MINUTE = 10;
    private const int MAX_TOTAL_SIZE_PER_MINUTE = 10 * 1024 * 1024; // 10MB
    
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var clientId = GetClientIdentifier(request);
        var metrics = _clientMetrics.GetOrAdd(clientId, _ => new ClientMetrics());
        
        if (metrics.IsRateLimited())
        {
            return new HttpResponseMessage(HttpStatusCode.TooManyRequests)
            {
                Content = new StringContent("Rate limit exceeded for audit logging")
            };
        }
        
        var auditStrategy = DetermineAuditStrategy(metrics, 
            request.Content?.Headers.ContentLength ?? 0);
        
        return await ExecuteAuditStrategy(auditStrategy, request, cancellationToken);
    }
}

public enum AuditStrategy
{
    Full,    // Complete request body audit
    Summary, // Only first N bytes + metadata
    Skip     // Skip audit, log only metadata
}
```

**Advantages**:
- Adaptive protection based on client behavior
- Maintains audit capability under normal conditions
- Graceful degradation under attack
- Client-specific rate limiting

**Disadvantages**:
- Complex client tracking and metrics management
- Memory overhead for client state storage
- Potential for legitimate clients to be rate limited
- Requires careful tuning of thresholds

### Protection Strategy 4: Background Queue Processing

**Architecture**: Immediate small request handling → Large request queuing → Background processing

**Implementation Approach**:
```csharp
public class QueuedAuditHandler : DelegatingHandler
{
    private readonly IBackgroundTaskQueue _auditQueue;
    private const int MAX_IMMEDIATE_SIZE = 64 * 1024; // 64KB
    
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        var contentLength = request.Content?.Headers.ContentLength ?? 0;
        
        if (contentLength <= MAX_IMMEDIATE_SIZE)
        {
            // Small requests - process immediately
            var body = await request.Content.ReadAsStringAsync();
            request.Content = new StringContent(body, Encoding.UTF8, 
                request.Content.Headers.ContentType?.MediaType);
                
            _auditQueue.QueueBackgroundWorkItem(async token =>
            {
                await LogAuditAsync(request, body);
            });
        }
        else
        {
            // Large requests - queue for background processing
            var auditId = Guid.NewGuid();
            await StreamToTempStorage(request, auditId);
            
            _auditQueue.QueueBackgroundWorkItem(async token =>
            {
                await ProcessLargeAuditAsync(auditId, request);
            });
        }
        
        return await base.SendAsync(request, cancellationToken);
    }
}
```

**Advantages**:
- Non-blocking processing for application requests
- Scalable background processing
- Resource utilization optimization
- Fault isolation between audit and business logic

**Disadvantages**:
- Delayed audit logging
- Queue management complexity
- Potential audit data loss if queue fails
- Resource coordination between processes

## Valid Solution Combinations Matrix

The following table shows viable combinations of audit strategies, stream handling methods, and DoS protection mechanisms:

| Audit Strategy | Stream Method | DoS Protection | Complexity | Memory Safety | Performance | Raw Body Access | Recommended Use Case |
|----------------|---------------|----------------|------------|---------------|-------------|-----------------|----------------------|
| DelegatingHandler | Content Replacement | Size Limits + Temp Files | Medium | High | Medium | ✅ Full | General purpose APIs with mixed payload sizes |
| DelegatingHandler | Content Replacement | Streaming Digest | Medium | Very High | Medium | ✅ Full | High-security environments requiring integrity |
| DelegatingHandler | Content Replacement | Circuit Breaker | High | High | High | ✅ Full | Public APIs with potential abuse |
| HTTP Module | Stream Wrapping | Size Limits + Temp Files | High | High | High | ✅ Full | Legacy systems requiring minimal code changes |
| HTTP Module | Stream Wrapping | Streaming Digest | Very High | Very High | Medium | ✅ Full | Maximum security with performance trade-offs |
| ActionFilter | Post-Processing | Circuit Breaker | Medium | High | High | ❌ Processed Only | Business logic and validation audit |
| Early Capture + Late Log | Content Replacement | Background Queue | High | High | Very High | ✅ Full | High-throughput systems |
| Early Capture + Late Log | Content Replacement | Size Limits + Circuit Breaker | Very High | High | High | ✅ Full | Enterprise applications with comprehensive audit requirements |

### Combination Details

#### Low Complexity Combinations
**ActionFilter + Post-Processing + Circuit Breaker**
- Suitable for business logic and validation audit scenarios
- **Limited to processed data only - cannot capture raw malformed requests**
- Good performance characteristics for post-validation auditing

#### Medium Complexity Combinations  
**DelegatingHandler + Content Replacement + Size Limits**
- Balanced approach for most applications requiring raw request capture
- Predictable resource usage
- Straightforward implementation and testing
- Full access to original request content

#### High Complexity Combinations
**Early Capture + Late Log + Background Queue + Circuit Breaker**
- Enterprise-grade solution with complete raw data capture
- Maximum flexibility and performance
- Requires significant architectural investment

### Decision Framework

**Choose based on priority ranking:**

1. **Raw Data Access Required**: HTTP Module + Stream Wrapping + Streaming Digest
2. **Performance First**: Early Capture + Late Log + Background Queue  
3. **Simplicity First**: DelegatingHandler + Content Replacement + Size Limits
4. **Business Logic Audit Only**: ActionFilter + Post-Processing + Circuit Breaker *(Note: No raw request body access)*

### Important Raw Data Access Considerations

**Solutions that CAN capture raw request body (including malformed JSON):**
- DelegatingHandler with Content Replacement
- HTTP Module with Stream Wrapping  
- Early Capture + Late Log hybrid approaches

**Solutions that CANNOT capture raw request body:**
- ActionFilter approaches *(limited to post-model-binding processed data only)*

**Critical Decision Point**: If your audit requirements include capturing malformed JSON, extra fields, or exact raw request content, you must choose a solution that operates **before** MVC model binding occurs.

## Implementation Recommendations

### Phase 1: Basic Implementation
Start with **DelegatingHandler + Content Replacement + Size Limits** for initial deployment and monitoring.

### Phase 2: Enhanced Security
Add **Circuit Breaker** patterns based on observed traffic patterns and attack vectors.

### Phase 3: Scale Optimization
Implement **Background Queue Processing** or **Streaming Digest** based on performance requirements.

### Phase 4: Enterprise Features
Add **Early Capture + Late Log** architecture for comprehensive audit context.

## Conclusion

The composable service architecture provides a flexible foundation for implementing HTTP request audit logging in .NET Framework 4.8 Web API applications. By separating concerns into discrete services (request capturing, DoS protection, and audit logging), teams can:

1. **Mix and match** components based on specific requirements
2. **Evolve the solution** incrementally through implementation phases  
3. **Test components** in isolation for better reliability
4. **Scale individual aspects** (capturing vs. protection vs. logging) independently
5. **Adapt to changing requirements** without architectural rewrites

### Key Decision Factors

**For Raw Request Body Access Requirements**:
- Choose DelegatingHandler, HTTP Module, or Early Capture + Late Log patterns
- Avoid Action Filter pattern (processes only deserialized data)

**For DoS Protection Priority**:
- SizeLimitProtectionService: Predictable resource usage
- StreamingDigestProtectionService: Memory-bounded with integrity verification  
- CircuitBreakerProtectionService: Adaptive protection against abuse
- BackgroundQueueProtectionService: Non-blocking large request handling

**For Development Complexity Tolerance**:
- Low: Action Filter + PostProcessing + CircuitBreaker  
- Medium: DelegatingHandler + ContentReplacement + SizeLimit
- High: HTTP Module + StreamWrapping + StreamingDigest
- Very High: Early Capture + Late Log + BackgroundQueue + CircuitBreaker

**For Performance Requirements**:
- Memory-sensitive: StreamingDigest or BackgroundQueue protection
- Throughput-sensitive: Early Capture + Late Log with BackgroundQueue
- Latency-sensitive: DelegatingHandler with SizeLimit protection

The service compatibility matrix and implementation phases provide a structured approach to selecting and evolving the audit solution based on specific application constraints, security requirements, and team capabilities.
