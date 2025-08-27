# HTTP Request Audit Logging Solutions for .NET Framework 4.8 Web API

## Executive Summary

This document analyzes various approaches for implementing comprehensive HTTP request audit logging in a legacy .NET Framework 4.8 MVC Web API application. The primary challenge is capturing the exact raw request data (including malformed JSON) while maintaining normal application flow and preserving the single-read nature of HTTP request streams.

## Requirements

- **Target Platform**: .NET Framework 4.8 MVC Web API
- **Scope**: Complete HTTP request auditing (headers, query parameters, raw body content)
- **Raw Data Preservation**: Capture exact request content as received, including malformed JSON
- **Authentication Context**: JWT Bearer token authentication with user information extraction
- **Non-Intrusive**: Minimal impact on existing controller logic and performance

## Technical Challenges

### The Stream Reading Problem

The fundamental challenge in HTTP request auditing is that `HttpRequest.InputStream` can only be read once. Once the MVC model binding process consumes the stream, the raw content is no longer accessible. This creates a conflict between:

1. **Early Access**: Reading raw content before model binding
2. **Late Context**: Having complete user and validation context after processing
3. **Stream Preservation**: Ensuring controllers can still access request data

## Solution Categories

### Category 1: Pipeline-Level Interception

Solutions that intercept requests early in the HTTP processing pipeline.

### Category 2: Action-Level Processing

Solutions that work at the MVC action filter or controller level.

### Category 3: Hybrid Approaches

Solutions that combine early capture with late processing.

## Detailed Solutions Analysis

### Solution 1: DelegatingHandler with Content Replacement

**Architecture**: HTTP Message Handler → Content Capture → Content Recreation

**Implementation Approach**:
```csharp
public class AuditMessageHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        string requestBody = null;
        
        if (request.Content != null)
        {
            // Capture and recreate request content to solve single-read stream issue
            requestBody = await CaptureRequestBodyAsync(request);
        }
        
        // Extract JWT user info and log audit
        var userInfo = ExtractUserFromJWT(request);
        await LogAuditAsync(request, requestBody, userInfo);
        
        return await base.SendAsync(request, cancellationToken);
    }
    
    private async Task<string> CaptureRequestBodyAsync(HttpRequestMessage request)
    {
        // Read original content
        var requestBody = await request.Content.ReadAsStringAsync();
        
        // Preserve original content type and headers
        var originalContentType = request.Content.Headers.ContentType;
        var originalHeaders = request.Content.Headers.ToList();
        
        // Create new content with same data and headers to solve stream consumption
        request.Content = new StringContent(requestBody, 
            Encoding.UTF8, originalContentType?.MediaType ?? "application/json");
            
        // Restore all original headers
        foreach (var header in originalHeaders)
        {
            request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        
        return requestBody;
    }
}
```

**Advantages**:
- Clean separation from business logic
- Automatic application to all API endpoints
- JWT token accessible at this pipeline stage
- No reflection or low-level stream manipulation required
- Easy to register and configure

**Disadvantages**:
- Limited access to post-processing context (validation errors, model state)
- Content recreation overhead for large payloads
- Headers must be carefully preserved during content replacement
- No access to controller-specific business context

**Registration Requirements**:
```csharp
// In WebApiConfig.cs
config.MessageHandlers.Insert(0, new AuditMessageHandler());
```

### Solution 2: HTTP Module with Stream Wrapping

**Architecture**: HTTP Module → Custom Stream Wrapper → Transparent Capture

**Implementation Approach**:
```csharp
public class AuditHttpModule : IHttpModule
{
    public void Init(HttpApplication context)
    {
        context.BeginRequest += OnBeginRequest;
        context.EndRequest += OnEndRequest;
    }
    
    private void OnBeginRequest(object sender, EventArgs e)
    {
        var httpContext = ((HttpApplication)sender).Context;
        var request = httpContext.Request;
        
        // Replace input stream with auditable wrapper to solve single-read issue
        var capturedData = CaptureRequestBodyAsync(request);
        httpContext.Items["CapturedRequestData"] = capturedData;
    }
    
    private CapturedRequestData CaptureRequestBodyAsync(HttpRequest request)
    {
        // Replace input stream with auditable wrapper
        var originalStream = request.InputStream;
        var auditableStream = new AuditableStream(originalStream);
        
        // Use reflection to replace the internal stream reference
        var inputStreamField = typeof(HttpRequest).GetField("_inputStream", 
            BindingFlags.NonPublic | BindingFlags.Instance);
        inputStreamField?.SetValue(request, auditableStream);
        
        return new CapturedRequestData
        {
            AuditableStream = auditableStream,
            QueryString = request.QueryString.ToString(),
            Headers = request.Headers.AllKeys.ToDictionary(k => k, k => request.Headers[k])
        };
    }
    
    private void OnEndRequest(object sender, EventArgs e)
    {
        var httpContext = ((HttpApplication)sender).Context;
        var capturedData = httpContext.Items["CapturedRequestData"] as CapturedRequestData;
        
        if (capturedData != null)
        {
            var requestBody = capturedData.AuditableStream.GetCapturedData();
            // Log audit with captured data
            LogAuditAsync(httpContext, requestBody, capturedData);
        }
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
    
    // Additional Stream implementation...
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

public class CapturedRequestData
{
    public AuditableStream AuditableStream { get; set; }
    public string QueryString { get; set; }
    public Dictionary<string, string> Headers { get; set; }
}
```

**Advantages**:
- Completely transparent to application code
- Perfect byte-for-byte capture without content modification
- No performance overhead from content duplication
- Guaranteed capture of all data that passes through the stream
- Works with any content type or encoding

**Disadvantages**:
- Requires reflection to replace internal stream reference
- More complex implementation and debugging
- Potential compatibility issues with future framework updates
- Stream wrapper must implement complete Stream interface
- Coordination needed with other HTTP modules

**Registration Requirements**:
```xml
<!-- In web.config -->
<system.webServer>
  <modules>
    <add name="AuditHttpModule" type="YourNamespace.AuditHttpModule" />
  </modules>
</system.webServer>
```

### Solution 3: Action Filter with Post-Processing Audit

**Architecture**: Action Filter → Model Binding → Post-Process Audit

**Implementation Approach**:
```csharp
public class AuditActionFilter : ActionFilterAttribute
{
    public override void OnActionExecuting(ActionExecutingContext filterContext)
    {
        // Capture available request metadata (raw body is no longer accessible)
        var request = filterContext.HttpContext.Request;
        
        var auditData = new ActionAuditData
        {
            HttpMethod = request.HttpMethod,
            Url = request.Url.ToString(),
            QueryString = request.QueryString.ToString(),
            Headers = request.Headers.AllKeys.ToDictionary(
                k => k, k => request.Headers[k]),
            ContentType = request.ContentType,
            ContentLength = request.ContentLength,
            Timestamp = DateTime.UtcNow,
            
            // Deserialized action parameters (NOT raw JSON)
            ActionParameters = filterContext.ActionParameters,
            ControllerName = filterContext.ActionDescriptor.ControllerDescriptor.ControllerName,
            ActionName = filterContext.ActionDescriptor.ActionName
        };
        
        // Store for post-action processing
        filterContext.HttpContext.Items["ActionAuditData"] = auditData;
    }
    
    public override void OnActionExecuted(ActionExecutedContext filterContext)
    {
        var auditData = filterContext.HttpContext.Items["ActionAuditData"] as ActionAuditData;
        if (auditData == null) return;
        
        // Now we have complete processing context
        var completeAudit = new CompleteActionAudit
        {
            // Pre-processing data
            RequestMetadata = auditData,
            
            // Post-processing context
            UserId = GetUserFromContext(filterContext),
            UserClaims = GetUserClaims(filterContext),
            ModelStateValid = filterContext.Controller.ViewData.ModelState.IsValid,
            ValidationErrors = GetValidationErrors(filterContext.Controller.ViewData.ModelState),
            ProcessingTimeMs = (DateTime.UtcNow - auditData.Timestamp).TotalMilliseconds,
            
            // Response information
            ResponseStatusCode = GetResponseStatusCode(filterContext),
            ExceptionInfo = filterContext.Exception?.ToString()
        };
        
        // Log complete audit with business context
        LogCompleteAudit(completeAudit);
    }
    
    private Dictionary<string, string[]> GetValidationErrors(ModelStateDictionary modelState)
    {
        return modelState
            .Where(x => x.Value.Errors.Count > 0)
            .ToDictionary(
                x => x.Key, 
                x => x.Value.Errors.Select(e => e.ErrorMessage).ToArray()
            );
    }
}
```

**Advantages**:
- Granular control at action level with selective application
- Access to complete MVC processing context (model state, validation errors, user context)
- Full authentication and authorization context available
- Business logic integration capabilities
- Excellent for compliance and debugging scenarios
- Easy unit testing and debugging
- No stream manipulation complexity

**Disadvantages**:
- **CRITICAL LIMITATION: Raw request body is not accessible due to single-read stream consumption by model binding**
- **Cannot capture malformed JSON or extra fields that were rejected during deserialization**
- **Only sees successfully parsed and validated data, not the original raw request**
- Must be applied to each controller or action individually (unless used globally)
- Limited to post-model-binding audit scenarios
- Cannot capture request data that failed early validation

### Solution 4: Early Capture with Late Logging (Hybrid)

**Architecture**: Early Handler → Data Storage → Controller-Level Logging

**Implementation Approach**:
```csharp
// Early capture component
public class RequestCaptureHandler : DelegatingHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        // Capture raw request data early in pipeline to solve single-read issue
        var auditData = new AuditCaptureData
        {
            RawBody = await CaptureRequestBodyAsync(request),
            QueryString = request.RequestUri?.Query,
            Headers = request.Headers.ToDictionary(h => h.Key, h => string.Join(", ", h.Value)),
            Method = request.Method.Method,
            Url = request.RequestUri?.ToString(),
            Timestamp = DateTime.UtcNow
        };
        
        // Store for later access
        request.Properties["AuditCaptureData"] = auditData;
        
        return await base.SendAsync(request, cancellationToken);
    }
    
    private async Task<string> CaptureRequestBodyAsync(HttpRequestMessage request)
    {
        if (request.Content == null) return null;
        
        // Read original content
        var requestBody = await request.Content.ReadAsStringAsync();
        
        // Preserve original content type and headers
        var originalContentType = request.Content.Headers.ContentType;
        var originalHeaders = request.Content.Headers.ToList();
        
        // Recreate content to solve stream consumption issue
        request.Content = new StringContent(requestBody, 
            Encoding.UTF8, originalContentType?.MediaType ?? "application/json");
            
        // Restore all original headers
        foreach (var header in originalHeaders)
        {
            request.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
        }
        
        return requestBody;
    }
}

// Late logging component
public abstract class AuditableControllerBase : ApiController
{
    protected override void Initialize(ControllerContext controllerContext)
    {
        base.Initialize(controllerContext);
        // Setup post-action audit logging
    }
    
    protected virtual async Task LogAuditAsync()
    {
        // Retrieve early captured data
        var auditData = Request.Properties["AuditCaptureData"] as AuditCaptureData;
        if (auditData == null) return;
        
        // Combine with late-available context
        var completeAuditEntry = new AuditLogEntry
        {
            // Early captured raw data
            RawBody = auditData.RawBody,
            QueryString = auditData.QueryString,
            Headers = auditData.Headers,
            
            // Late available context
            UserId = User?.Identity?.Name,
            UserClaims = GetUserClaimsFromJWT(),
            ControllerName = ControllerContext.ControllerDescriptor.ControllerName,
            ActionName = ActionContext.ActionDescriptor.ActionName,
            ModelState = ModelState.IsValid,
            ValidationErrors = GetValidationErrors(),
            ProcessingTimeMs = (DateTime.UtcNow - auditData.Timestamp).TotalMilliseconds,
            
            // Controller-specific context
            AdditionalContext = GetAdditionalAuditContext()
        };
        
        await _auditService.LogAsync(completeAuditEntry);
    }
    
    // Override in derived controllers for business-specific audit data
    protected virtual Dictionary<string, object> GetAdditionalAuditContext() 
        => new Dictionary<string, object>();
}
```

**Advantages**:
- Complete separation of concerns between capture and logging
- Maximum available context (raw data + processed results + business context)
- Highly customizable per controller or business domain
- Clean architecture with testable components
- No interference between stream handling and business logic
- Fault isolation - audit failures don't affect business processing

**Disadvantages**:
- More complex architecture requiring coordination between components
- Memory overhead from storing captured data throughout request lifecycle
- Requires discipline to ensure all controllers inherit from base class
- Additional complexity in error handling and cleanup
- Potential for memory leaks if audit data isn't properly cleaned up

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

## Security and DoS Protection Strategies

### The Memory Exhaustion Problem

All basic audit logging solutions share a critical vulnerability: they read the entire request body into memory, making the application susceptible to Denial of Service (DoS) attacks through large request payloads. An attacker could send multiple large requests simultaneously to exhaust server memory.

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

The choice of audit logging solution must balance security, performance, and maintainability requirements. DoS protection is not optional—any production implementation must include mechanisms to prevent memory exhaustion attacks. The combination matrix provides a structured approach to selecting the appropriate solution based on specific application constraints and requirements.

Key decision factors:
- **Security threat model and DoS protection requirements**
- **Performance and scalability requirements**
- **Development complexity tolerance**
- **Existing architecture constraints**
- **Team expertise with different .NET Framework components**
- **Future migration plans to .NET Core/.NET 5+**