# HTTP Request Audit Logging POC

This POC project demonstrates various implementation approaches for HTTP request audit logging in .NET Framework 4.8 Web API applications.

## Project Structure

```
AuditLoginPOC/
├── AuditLoginPOC.Core/           # Common interfaces and implementations
├── AuditLoginPOC.WebApi/         # Web API application
├── AuditLoginPOC.Tests/          # Reqnroll tests
├── AuditLoginPOC.Benchmarks/     # Performance benchmarks
└── README.md
```

## Main Features

### 1. Request Body Re-reading
- **ContentReplacementCapturingService**: HttpContent re-reading and rebuilding
- **StreamWrappingCapturingService**: Using stream wrapper
- **PostProcessingCapturingService**: Post-model-binding processing

### 2. Original Request Body Logging
- Malformed JSON handling
- Raw request data preservation
- Stream state preservation

### 3. DoS Protection
- **SizeLimitProtectionService**: Size-based protection
- **StreamingDigestProtectionService**: Hash-based protection
- **CircuitBreakerProtectionService**: Rate limiting

### 4. FluentValidation Integration
- **Person Model**: FirstName, LastName, Email, Age validation
- **Extra Field Handling**: Fields not in the contract (e.g., Gender) are silently ignored
- **Audit Logging**: Extra fields remain in the audit log
- **Dependency Injection**: Validators are injected via DI container

### 5. Tests
- Reqnroll acceptance tests (XUnit based)
- Unit tests for critical functionality (XUnit + Shouldly assertion library)
- Performance benchmarks

## Installation and Running

### Prerequisites
- .NET Framework 4.8
- Visual Studio 2019/2022 or .NET CLI

### Build
```bash
dotnet build AuditLoginPOC.sln
```

### Running Tests
```bash
# All tests
dotnet test AuditLoginPOC.Tests/

# Unit tests only
dotnet test AuditLoginPOC.Tests/ --filter "Category=UnitTest"

# Acceptance tests only (Reqnroll)
dotnet test AuditLoginPOC.Tests/ --filter "Category=AcceptanceTest"
```

### Running Benchmarks
```bash
dotnet run --project AuditLoginPOC.Benchmarks/
```

### Running Web API
```bash
dotnet run --project AuditLoginPOC.WebApi/
```

## API Endpoints

### Test Endpoints
- `POST /api/test/echo` - Simple echo endpoint
- `POST /api/test/malformed` - Malformed JSON test
- `POST /api/test/large` - Large request test
- `POST /api/test/validation` - Person validation test (FluentValidation)
- `POST /api/test/error` - Exception test

## Testing Examples

### 1. Normal JSON Request
```bash
curl -X POST http://localhost:5000/api/test/echo \
  -H "Content-Type: application/json" \
  -d '{"name":"test","value":123}'
```

### 2. Malformed JSON Request
```bash
curl -X POST http://localhost:5000/api/test/malformed \
  -H "Content-Type: application/json" \
  -d '{ invalid json }'
```

### 3. Large Request
```bash
curl -X POST http://localhost:5000/api/test/large \
  -H "Content-Type: application/json" \
  -d '{"data":"'$(printf 'x%.0s' {1..1000000})'"}'
```

### 4. Person Validation (Extra Field Test)
```bash
# Successful validation with extra field
curl -X POST http://localhost:5000/api/test/validation \
  -H "Content-Type: application/json" \
  -d '{
    "FirstName": "John",
    "LastName": "Doe", 
    "Email": "john.doe@example.com",
    "Age": 32,
    "Gender": "male"
  }'

# Validation error with extra field
curl -X POST http://localhost:5000/api/test/validation \
  -H "Content-Type: application/json" \
  -d '{
    "FirstName": "",
    "LastName": "",
    "Email": "invalid-email",
    "Age": -1,
    "Gender": "male"
  }'
```

**Note**: The `Gender` extra field is silently ignored by deserialization and validation, but it remains in the audit log.

## Reqnroll Tests

The project contains Reqnroll acceptance tests for the following areas:

### Critical Requirements Testing
- ✅ Request body re-reading (during deserialization and validation)
- ✅ Original request body logging
- ✅ Malformed JSON handling
- ✅ Large request handling
- ✅ Validation error capture
- ✅ Exception handling

### Test Scenarios
1. **Capture normal JSON request body**
2. **Capture malformed JSON request body**
3. **Handle large request with size limit protection**
4. **Capture request with validation errors**
5. **Handle request that throws an exception**
6. **Capture request headers and metadata**
7. **Handle request with JWT authentication**

## Benchmark Results

The benchmarks compare the performance of different audit logging approaches:

### Measured Metrics
- **Mean**: Average execution time
- **Allocated**: Memory allocated per operation
- **Gen0/Gen1/Gen2**: Garbage collection generations

### Compared Approaches
1. **ContentReplacement** - HttpContent capture and rebuilding
2. **SizeLimitProtection** - Adding DoS protection
3. **PostProcessing** - Minimal capture (no raw body)
4. **FullPipeline** - Complete audit logging pipeline

## Architecture

### Composable Service Architecture
```
┌─────────────────┐    ┌──────────────────┐    ┌─────────────────┐
│   Capturing     │    │   Protection     │    │     Logging     │
│    Service      │    │    Service       │    │    Service      │
└─────────────────┘    └──────────────────┘    └─────────────────┘
          │                       │                       │
          └───────────────────────┼───────────────────────┘
                                  │
                     ┌─────────────────┐
                     │   Delegating    │
                     │    Handler      │
                     └─────────────────┘
```

### Service Compatibility Matrix

| Service Type | Compatible Patterns | Raw Body Access | Complexity |
|--------------|-------------------|-----------------|------------|
| ContentReplacement | DelegatingHandler | ✅ Full | Medium |
| StreamWrapping | HTTP Module | ✅ Full | High |
| PostProcessing | Action Filter | ❌ Processed Only | Low |

## Configuration

### Dependency Injection
```csharp
private static void ConfigureDependencyInjection(HttpConfiguration config)
{
    var container = new SimpleDependencyResolver();
    
    // Register validators
    container.Register<IValidator<Person>, PersonValidator>();
    
    // Register services
    container.Register<IRequestCapturingService, ContentReplacementCapturingService>();
    container.Register<IDoSProtectionService, SizeLimitProtectionService>();
    container.Register<IAuditLoggingService, ConsoleAuditLoggingService>();
    
    config.DependencyResolver = container;
}
```

### Controller Dependency Injection
```csharp
public class TestController : ApiController
{
    private readonly IValidator<Person> _personValidator;

    public TestController(IValidator<Person> personValidator)
    {
        _personValidator = personValidator ?? throw new ArgumentNullException(nameof(personValidator));
    }

    [HttpPost]
    [Route("validation")]
    public async Task<IHttpActionResult> ValidationTest([FromBody] Person person)
    {
        if (person == null)
        {
            return BadRequest("Person data is required");
        }

        var validationResult = _personValidator.Validate(person);
        
        if (!validationResult.IsValid)
        {
            var errors = validationResult.Errors.Select(e => e.ErrorMessage).ToList();
            return BadRequest(string.Join(", ", errors));
        }

        return Ok(new { Message = "Person validation passed", Person = person });
    }
}
```

### WebApiConfig.cs
```csharp
private static void RegisterAuditServices(HttpConfiguration config)
{
    // Get services from DI container
    var capturingService = config.DependencyResolver.GetService(typeof(IRequestCapturingService)) as IRequestCapturingService;
    var protectionService = config.DependencyResolver.GetService(typeof(IDoSProtectionService)) as IDoSProtectionService;
    var auditService = config.DependencyResolver.GetService(typeof(IAuditLoggingService)) as IAuditLoggingService;

    var auditHandler = new ComposableAuditMessageHandler(
        capturingService, protectionService, auditService);

    config.MessageHandlers.Insert(0, auditHandler);
}
```

## Performance Optimizations

### Memory Usage
- **ContentReplacement**: Double memory usage during request body processing
- **StreamWrapping**: Incremental memory usage
- **SizeLimitProtection**: Temp file fallback for large requests

### Processing Overhead
- **Early Pipeline**: Minimal impact on business logic performance
- **Stream Manipulation**: CPU overhead varies by strategy

## Security Considerations

### Sensitive Data Exposure
- Raw request logging may capture sensitive data
- JWT token information requires careful handling
- Selective field masking or encryption recommended

### DoS Protection
- Size-based protection against large requests
- Rate limiting based on client behavior
- Circuit breaker pattern against abuse

## Next Steps

### Phase 1: Basic Implementation
- DelegatingHandler + ContentReplacement + SizeLimit

### Phase 2: Enhanced Security
- Adding CircuitBreaker pattern

### Phase 3: Scale Optimization
- BackgroundQueue or StreamingDigest implementation

### Phase 4: Enterprise Features
- Early Capture + Late Log architecture

## Developer Guide

### Implementing New Services
1. Implement the appropriate interface
2. Add to service compatibility matrix
3. Write tests for critical functionality
4. Benchmark for performance verification

### Testing Strategy
- **Unit tests**: XUnit framework for isolated components (`[UnitTest]` attribute)
- **Acceptance tests**: Reqnroll + XUnit for end-to-end flow validation (`[AcceptanceTest]` attribute)
- **Performance tests**: BenchmarkDotNet under load
- **Security tests**: Sensitive data handling

### Test Categorization
The project uses XUnit.Categories to categorize tests:
- **UnitTest**: Isolated component tests (5 tests)
- **AcceptanceTest**: End-to-end Reqnroll tests (8 tests)

### Assertion Library
The project uses the **Shouldly** assertion library instead of FluentAssertions because:
- **Free**: FluentAssertions 8.x and above are no longer free
- **Expressive**: Similar readable syntax
- **Good error messages**: Detailed error messages during testing

**Example of Shouldly usage:**
```csharp
// Instead of FluentAssertions
result.Should().NotBeNull();
result.Should().BeOfType<OkResult>();
content.Should().Contain("expected text");

// Using Shouldly
result.ShouldNotBeNull();
result.ShouldBeOfType<OkResult>();
content.ShouldContain("expected text");
```

### Type Checking and Pattern Matching
In tests, we use **type checking and pattern matching** instead of reflection:

```csharp
// Instead of reflection (avoid)
var contentProperty = result.GetType().GetProperty("Content");
var content = contentProperty?.GetValue(result);

// Type checking and pattern matching (recommended)
if (result is OkNegotiatedContentResult<object> okResult)
{
    var content = okResult.Content; // Type-safe access
    content.ShouldNotBeNull();
}
```

**Benefits:**
- **Type safety**: Compile-time checking
- **Performance**: No reflection overhead
- **Readability**: Clear code
- **IDE support**: IntelliSense and refactoring

## Related Documentation

- [docs/audit_logging_analysis-v4.md](docs/audit_logging_analysis-v4.md) - Detailed technical analysis
- [Reqnroll Documentation](https://reqnroll.net/) - Acceptance testing framework
- [XUnit Documentation](https://xunit.net/) - Unit testing framework
- [Shouldly Documentation](https://shouldly.io/) - Assertion library
- [BenchmarkDotNet](https://benchmarkdotnet.org/) - Performance benchmarking

## License

This project is licensed under the MIT License.
