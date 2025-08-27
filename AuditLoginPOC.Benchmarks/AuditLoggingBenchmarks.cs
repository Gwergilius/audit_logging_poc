using System;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using BenchmarkDotNet.Attributes;
using BenchmarkDotNet.Running;
using AuditLoginPOC.Core.Interfaces;
using AuditLoginPOC.Core.Models;
using AuditLoginPOC.Core.Services;
using AuditLoginPOC.Core.Wrappers;

namespace AuditLoginPOC.Benchmarks
{
    [MemoryDiagnoser]
    [SimpleJob]
    public class AuditLoggingBenchmarks
    {
        private IRequestCapturingService _contentReplacementService;
        private IRequestCapturingService _streamWrappingService;
        private IRequestCapturingService _postProcessingService;
        private IDoSProtectionService _sizeLimitProtectionService;
        private MockAuditLoggingService _auditService;
        private HttpRequestMessage _smallRequest;
        private HttpRequestMessage _largeRequest;
        private HttpRequestMessage _malformedRequest;

        [GlobalSetup]
        public void Setup()
        {
            _contentReplacementService = new ContentReplacementCapturingService();
            _streamWrappingService = new StreamWrappingCapturingService();
            _postProcessingService = new PostProcessingCapturingService();
            _sizeLimitProtectionService = new SizeLimitProtectionService();
            _auditService = new MockAuditLoggingService();

            // Create test requests
            _smallRequest = CreateRequest(CreateSmallJson());
            _largeRequest = CreateRequest(CreateLargeJson());
            _malformedRequest = CreateRequest(CreateMalformedJson());
        }

        [Benchmark]
        public async Task ContentReplacement_SmallRequest()
        {
            var wrapper = new HttpRequestMessageWrapper(_smallRequest);
            var captured = await _contentReplacementService.CaptureRequestAsync(wrapper);
            var context = new AuditContext { CapturedRequest = captured };
            await _auditService.LogAuditAsync(context);
        }

        [Benchmark]
        public async Task ContentReplacement_LargeRequest()
        {
            var wrapper = new HttpRequestMessageWrapper(_largeRequest);
            var captured = await _contentReplacementService.CaptureRequestAsync(wrapper);
            var context = new AuditContext { CapturedRequest = captured };
            await _auditService.LogAuditAsync(context);
        }

        [Benchmark]
        public async Task ContentReplacement_MalformedRequest()
        {
            var wrapper = new HttpRequestMessageWrapper(_malformedRequest);
            var captured = await _contentReplacementService.CaptureRequestAsync(wrapper);
            var context = new AuditContext { CapturedRequest = captured };
            await _auditService.LogAuditAsync(context);
        }

        [Benchmark]
        public async Task SizeLimitProtection_SmallRequest()
        {
            var wrapper = new HttpRequestMessageWrapper(_smallRequest);
            var protection = await _sizeLimitProtectionService.EvaluateRequestAsync(wrapper);
            if (protection.IsAllowed)
            {
                var captured = await _contentReplacementService.CaptureRequestAsync(wrapper);
                var context = new AuditContext { CapturedRequest = captured };
                await _auditService.LogAuditAsync(context);
            }
        }

        [Benchmark]
        public async Task SizeLimitProtection_LargeRequest()
        {
            var wrapper = new HttpRequestMessageWrapper(_largeRequest);
            var protection = await _sizeLimitProtectionService.EvaluateRequestAsync(wrapper);
            if (protection.IsAllowed)
            {
                var captured = await _contentReplacementService.CaptureRequestAsync(wrapper);
                var context = new AuditContext { CapturedRequest = captured };
                await _auditService.LogAuditAsync(context);
            }
        }

        [Benchmark]
        public async Task PostProcessing_SmallRequest()
        {
            var wrapper = new HttpRequestMessageWrapper(_smallRequest);
            var captured = await _postProcessingService.CaptureRequestAsync(wrapper);
            var context = new AuditContext { CapturedRequest = captured };
            await _auditService.LogAuditAsync(context);
        }

        [Benchmark]
        public async Task PostProcessing_LargeRequest()
        {
            var wrapper = new HttpRequestMessageWrapper(_largeRequest);
            var captured = await _postProcessingService.CaptureRequestAsync(wrapper);
            var context = new AuditContext { CapturedRequest = captured };
            await _auditService.LogAuditAsync(context);
        }

        [Benchmark]
        public async Task FullPipeline_SmallRequest()
        {
            var wrapper = new HttpRequestMessageWrapper(_smallRequest);
            var protection = await _sizeLimitProtectionService.EvaluateRequestAsync(wrapper);
            if (protection.IsAllowed)
            {
                var captured = await _contentReplacementService.CaptureRequestAsync(wrapper);
                var context = new AuditContext 
                { 
                    CapturedRequest = captured,
                    UserId = "test-user",
                    ResponseStatusCode = System.Net.HttpStatusCode.OK,
                    ProcessingTimeMs = 10.5
                };
                await _auditService.LogAuditAsync(context);
            }
        }

        [Benchmark]
        public async Task FullPipeline_LargeRequest()
        {
            var wrapper = new HttpRequestMessageWrapper(_largeRequest);
            var protection = await _sizeLimitProtectionService.EvaluateRequestAsync(wrapper);
            if (protection.IsAllowed)
            {
                var captured = await _contentReplacementService.CaptureRequestAsync(wrapper);
                var context = new AuditContext 
                { 
                    CapturedRequest = captured,
                    UserId = "test-user",
                    ResponseStatusCode = System.Net.HttpStatusCode.OK,
                    ProcessingTimeMs = 150.2
                };
                await _auditService.LogAuditAsync(context);
            }
        }

        private HttpRequestMessage CreateRequest(string body)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, "/api/test/echo");
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            return request;
        }

        private string CreateSmallJson()
        {
            return "{\"name\":\"test\",\"value\":123,\"active\":true}";
        }

        private string CreateLargeJson()
        {
            var sb = new StringBuilder();
            sb.Append("{");
            for (int i = 0; i < 10000; i++)
            {
                if (i > 0) sb.Append(",");
                sb.Append($"\"field{i}\":\"value{i}\"");
            }
            sb.Append("}");
            return sb.ToString();
        }

        private string CreateMalformedJson()
        {
            return "{ invalid json with missing quotes and commas }";
        }
    }

    /// <summary>
    /// Mock audit logging service for benchmarks
    /// </summary>
    public class MockAuditLoggingService : IAuditLoggingService
    {
        public Task LogAuditAsync(AuditContext context)
        {
            // Simulate minimal logging overhead
            return Task.CompletedTask;
        }
    }

    /// <summary>
    /// Program entry point for running benchmarks
    /// </summary>
    public class Program
    {
        public static void Main(string[] args)
        {
            Console.WriteLine("Running Audit Logging Benchmarks...");
            Console.WriteLine("This will compare performance of different audit logging approaches.");
            Console.WriteLine();

            var summary = BenchmarkRunner.Run<AuditLoggingBenchmarks>();

            Console.WriteLine();
            Console.WriteLine("Benchmark Results:");
            Console.WriteLine("==================");
            Console.WriteLine("The benchmarks compare:");
            Console.WriteLine("1. ContentReplacement - Captures and recreates HttpContent");
            Console.WriteLine("2. SizeLimitProtection - Adds DoS protection layer");
            Console.WriteLine("3. PostProcessing - Minimal capture (no raw body)");
            Console.WriteLine("4. FullPipeline - Complete audit logging pipeline");
            Console.WriteLine();
            Console.WriteLine("Key metrics:");
            Console.WriteLine("- Mean: Average execution time");
            Console.WriteLine("- Allocated: Memory allocated per operation");
            Console.WriteLine("- Gen0/Gen1/Gen2: Garbage collection generations");
        }
    }
}
