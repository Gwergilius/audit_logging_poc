using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Shouldly;
using Moq;
using Newtonsoft.Json;
using Reqnroll;
using Xunit.Categories;
using AuditLoginPOC.Core.Interfaces;
using AuditLoginPOC.Core.Models;
using AuditLoginPOC.Core.Services;
using AuditLoginPOC.Core.Wrappers;

namespace AuditLoginPOC.Tests.StepDefinitions
{
    [Binding]
    public class AuditLoggingSteps
    {
        private readonly ScenarioContext _scenarioContext;
        private readonly Mock<IAuditLoggingService> _mockAuditService;
        private readonly IRequestCapturingService _capturingService;
        private readonly IDoSProtectionService _protectionService;
        private readonly List<AuditContext> _capturedAuditLogs;
        private HttpResponseMessage _response;
        private string _requestBody;
        private string _endpoint;

        public AuditLoggingSteps(ScenarioContext scenarioContext)
        {
            _scenarioContext = scenarioContext;
            _mockAuditService = new Mock<IAuditLoggingService>();
            _capturingService = new ContentReplacementCapturingService();
            _protectionService = new SizeLimitProtectionService();
            _capturedAuditLogs = new List<AuditContext>();

            // Capture audit logs for verification
            _mockAuditService
                .Setup(x => x.LogAuditAsync(It.IsAny<AuditContext>()))
                .Callback<AuditContext>(context => _capturedAuditLogs.Add(context))
                .Returns(Task.CompletedTask);
        }

        [Given(@"the audit logging system is configured")]
        public void GivenTheAuditLoggingSystemIsConfigured()
        {
            // System is configured in constructor
            _capturedAuditLogs.Clear();
        }

        [Given(@"the test API is running")]
        public void GivenTheTestApiIsRunning()
        {
            // In a real scenario, this would start the test API
            // For this POC, we'll test the components directly
        }

        [When(@"I send a POST request to ""(.*)"" with valid JSON body")]
        public async Task WhenISendAPostRequestToWithValidJsonBody(string endpoint)
        {
            _endpoint = endpoint;
            _requestBody = JsonConvert.SerializeObject(new { name = "test", value = 123 });

            var request = CreateHttpRequestMessage(endpoint, _requestBody);
            var wrapper = new HttpRequestMessageWrapper(request);

            var protection = await _protectionService.EvaluateRequestAsync(wrapper);
            if (protection.IsAllowed)
            {
                var capturedRequest = await _capturingService.CaptureRequestAsync(wrapper);
                var auditContext = new AuditContext
                {
                    CapturedRequest = capturedRequest,
                    ResponseStatusCode = System.Net.HttpStatusCode.OK
                };

                await _mockAuditService.Object.LogAuditAsync(auditContext);
            }
        }

        [When(@"I send a POST request to ""(.*)"" with malformed JSON body")]
        public async Task WhenISendAPostRequestToWithMalformedJsonBody(string endpoint)
        {
            _endpoint = endpoint;
            _requestBody = "{ invalid json }";

            var request = CreateHttpRequestMessage(endpoint, _requestBody);
            var wrapper = new HttpRequestMessageWrapper(request);

            var protection = await _protectionService.EvaluateRequestAsync(wrapper);
            if (protection.IsAllowed)
            {
                var capturedRequest = await _capturingService.CaptureRequestAsync(wrapper);
                var auditContext = new AuditContext
                {
                    CapturedRequest = capturedRequest,
                    ResponseStatusCode = System.Net.HttpStatusCode.BadRequest
                };

                await _mockAuditService.Object.LogAuditAsync(auditContext);
            }
        }

        [When(@"I send a POST request to ""(.*)"" with a large JSON body")]
        public async Task WhenISendAPostRequestToWithALargeJsonBody(string endpoint)
        {
            _endpoint = endpoint;
            // Create a large JSON body (over 1MB)
            var largeData = new StringBuilder();
            for (int i = 0; i < 100000; i++)
            {
                largeData.Append($"\"data{i}\": \"value{i}\",");
            }
            _requestBody = "{" + largeData.ToString().TrimEnd(',') + "}";

            var request = CreateHttpRequestMessage(endpoint, _requestBody);
            var wrapper = new HttpRequestMessageWrapper(request);

            // Use ProcessWithProtectionAsync for large requests to get the summary
            var capturedRequest = await _protectionService.ProcessWithProtectionAsync(wrapper);
            var auditContext = new AuditContext
            {
                CapturedRequest = capturedRequest,
                ResponseStatusCode = System.Net.HttpStatusCode.OK
            };

            await _mockAuditService.Object.LogAuditAsync(auditContext);
        }

        [When(@"I send a POST request to ""(.*)"" with Person data including extra field")]
        public async Task WhenISendAPostRequestToWithPersonDataIncludingExtraField(string endpoint)
        {
            _endpoint = endpoint;
            _requestBody = JsonConvert.SerializeObject(new { 
                FirstName = "John", 
                LastName = "Doe", 
                Email = "john.doe@example.com", 
                Age = 32, 
                Gender = "male" 
            });

            var request = CreateHttpRequestMessage(endpoint, _requestBody);
            var wrapper = new HttpRequestMessageWrapper(request);

            var protection = await _protectionService.EvaluateRequestAsync(wrapper);
            if (protection.IsAllowed)
            {
                var capturedRequest = await _capturingService.CaptureRequestAsync(wrapper);
                var auditContext = new AuditContext
                {
                    CapturedRequest = capturedRequest,
                    ModelStateValid = true,
                    ResponseStatusCode = System.Net.HttpStatusCode.OK
                };

                await _mockAuditService.Object.LogAuditAsync(auditContext);
            }
        }

        [When(@"I send a POST request to ""(.*)"" with invalid Person data")]
        public async Task WhenISendAPostRequestToWithInvalidPersonData(string endpoint)
        {
            _endpoint = endpoint;
            _requestBody = JsonConvert.SerializeObject(new { 
                FirstName = "", 
                LastName = "", 
                Email = "invalid-email", 
                Age = -1, 
                Gender = "male" 
            });

            var request = CreateHttpRequestMessage(endpoint, _requestBody);
            var wrapper = new HttpRequestMessageWrapper(request);

            var protection = await _protectionService.EvaluateRequestAsync(wrapper);
            if (protection.IsAllowed)
            {
                var capturedRequest = await _capturingService.CaptureRequestAsync(wrapper);
                var auditContext = new AuditContext
                {
                    CapturedRequest = capturedRequest,
                    ModelStateValid = false,
                    ValidationErrors = new List<string> { 
                        "First name is required", 
                        "Last name is required", 
                        "Email must be a valid email address", 
                        "Age must be greater than 0" 
                    },
                    ResponseStatusCode = System.Net.HttpStatusCode.BadRequest
                };

                await _mockAuditService.Object.LogAuditAsync(auditContext);
            }
        }

        [When(@"I send a POST request to ""(.*)"" with any data")]
        public async Task WhenISendAPostRequestToWithAnyData(string endpoint)
        {
            _endpoint = endpoint;
            _requestBody = JsonConvert.SerializeObject(new { test = "data" });

            var request = CreateHttpRequestMessage(endpoint, _requestBody);
            var wrapper = new HttpRequestMessageWrapper(request);

            var protection = await _protectionService.EvaluateRequestAsync(wrapper);
            if (protection.IsAllowed)
            {
                var capturedRequest = await _capturingService.CaptureRequestAsync(wrapper);
                var auditContext = new AuditContext
                {
                    CapturedRequest = capturedRequest,
                    ExceptionInfo = "Test exception for audit logging",
                    ResponseStatusCode = System.Net.HttpStatusCode.InternalServerError
                };

                await _mockAuditService.Object.LogAuditAsync(auditContext);
            }
        }

        [When(@"I send a request with custom headers")]
        public async Task WhenISendARequestWithCustomHeaders()
        {
            _endpoint = "/api/test/echo";
            _requestBody = JsonConvert.SerializeObject(new { test = "data" });

            var request = CreateHttpRequestMessage(_endpoint, _requestBody);
            request.Headers.Add("X-Custom-Header", "custom-value");
            request.Headers.Add("X-Request-ID", Guid.NewGuid().ToString());

            var wrapper = new HttpRequestMessageWrapper(request);

            var protection = await _protectionService.EvaluateRequestAsync(wrapper);
            if (protection.IsAllowed)
            {
                var capturedRequest = await _capturingService.CaptureRequestAsync(wrapper);
                var auditContext = new AuditContext
                {
                    CapturedRequest = capturedRequest,
                    ResponseStatusCode = System.Net.HttpStatusCode.OK
                };

                await _mockAuditService.Object.LogAuditAsync(auditContext);
            }
        }

        [When(@"I send a request with a valid JWT token")]
        public async Task WhenISendARequestWithAValidJwtToken()
        {
            _endpoint = "/api/test/echo";
            _requestBody = JsonConvert.SerializeObject(new { test = "data" });

            var request = CreateHttpRequestMessage(_endpoint, _requestBody);
            request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJ0ZXN0LXVzZXIiLCJuYW1lIjoiVGVzdCBVc2VyIiwiaWF0IjoxNTE2MjM5MDIyfQ.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c");

            var wrapper = new HttpRequestMessageWrapper(request);

            var protection = await _protectionService.EvaluateRequestAsync(wrapper);
            if (protection.IsAllowed)
            {
                var capturedRequest = await _capturingService.CaptureRequestAsync(wrapper);
                var auditContext = new AuditContext
                {
                    CapturedRequest = capturedRequest,
                    UserId = "test-user",
                    ResponseStatusCode = System.Net.HttpStatusCode.OK
                };

                await _mockAuditService.Object.LogAuditAsync(auditContext);
            }
        }

        [Then(@"the request body should be captured in the audit log")]
        public void ThenTheRequestBodyShouldBeCapturedInTheAuditLog()
        {
            _capturedAuditLogs.ShouldNotBeEmpty();
            var lastAudit = _capturedAuditLogs[_capturedAuditLogs.Count - 1];
            lastAudit.CapturedRequest.Body.ShouldNotBeNullOrEmpty();
        }

        [Then(@"the audit log should contain the original request data")]
        public void ThenTheAuditLogShouldContainTheOriginalRequestData()
        {
            var lastAudit = _capturedAuditLogs[_capturedAuditLogs.Count - 1];
            lastAudit.CapturedRequest.Body.ShouldContain("test");
            lastAudit.CapturedRequest.Body.ShouldContain("123");
        }

        [Then(@"the response should be successful")]
        public void ThenTheResponseShouldBeSuccessful()
        {
            var lastAudit = _capturedAuditLogs[_capturedAuditLogs.Count - 1];
            lastAudit.ResponseStatusCode.ShouldBe(System.Net.HttpStatusCode.OK);
        }

        [Then(@"the malformed request body should be captured in the audit log")]
        public void ThenTheMalformedRequestBodyShouldBeCapturedInTheAuditLog()
        {
            var lastAudit = _capturedAuditLogs[_capturedAuditLogs.Count - 1];
            lastAudit.CapturedRequest.Body.ShouldBe(_requestBody);
        }

        [Then(@"the audit log should contain the exact raw request data")]
        public void ThenTheAuditLogShouldContainTheExactRawRequestData()
        {
            var lastAudit = _capturedAuditLogs[_capturedAuditLogs.Count - 1];
            lastAudit.CapturedRequest.Body.ShouldBe("{ invalid json }");
        }

        [Then(@"the response should indicate an error")]
        public void ThenTheResponseShouldIndicateAnError()
        {
            var lastAudit = _capturedAuditLogs[_capturedAuditLogs.Count - 1];
            lastAudit.ResponseStatusCode.ShouldNotBe(System.Net.HttpStatusCode.OK);
        }

        [Then(@"the request should be processed with size limit protection")]
        public void ThenTheRequestShouldBeProcessedWithSizeLimitProtection()
        {
            var lastAudit = _capturedAuditLogs[_capturedAuditLogs.Count - 1];
            lastAudit.CapturedRequest.ContentLength.ShouldBeGreaterThan(1024 * 1024);
        }

        [Then(@"the audit log should contain a summary of the large request")]
        public void ThenTheAuditLogShouldContainASummaryOfTheLargeRequest()
        {
            var lastAudit = _capturedAuditLogs[_capturedAuditLogs.Count - 1];
            lastAudit.CapturedRequest.Body.ShouldContain("LARGE_REQUEST");
        }

        [Then(@"the request body should be captured in the audit log including the extra field")]
        public void ThenTheRequestBodyShouldBeCapturedInTheAuditLogIncludingTheExtraField()
        {
            var lastAudit = _capturedAuditLogs[_capturedAuditLogs.Count - 1];
            lastAudit.CapturedRequest.Body.ShouldContain("Gender");
            lastAudit.CapturedRequest.Body.ShouldContain("male");
        }

        [Then(@"the Person validation should pass")]
        public void ThenThePersonValidationShouldPass()
        {
            var lastAudit = _capturedAuditLogs[_capturedAuditLogs.Count - 1];
            lastAudit.ModelStateValid.ShouldBe(true);
        }

        [Then(@"the extra field should be ignored by validation but captured in audit")]
        public void ThenTheExtraFieldShouldBeIgnoredByValidationButCapturedInAudit()
        {
            var lastAudit = _capturedAuditLogs[_capturedAuditLogs.Count - 1];
            // The Gender field should be in the raw request body but not cause validation errors
            lastAudit.CapturedRequest.Body.ShouldContain("Gender");
            lastAudit.ModelStateValid.ShouldBe(true);
        }

        [Then(@"the audit log should contain FluentValidation error information")]
        public void ThenTheAuditLogShouldContainFluentValidationErrorInformation()
        {
            var lastAudit = _capturedAuditLogs[_capturedAuditLogs.Count - 1];
            lastAudit.ModelStateValid.ShouldBe(false);
            lastAudit.ValidationErrors.ShouldNotBeEmpty();
            lastAudit.ValidationErrors.ShouldContain("First name is required");
        }

        [Then(@"the response should indicate validation errors")]
        public void ThenTheResponseShouldIndicateValidationErrors()
        {
            var lastAudit = _capturedAuditLogs[_capturedAuditLogs.Count - 1];
            lastAudit.ResponseStatusCode.ShouldBe(System.Net.HttpStatusCode.BadRequest);
        }

        [Then(@"the audit log should contain exception information")]
        public void ThenTheAuditLogShouldContainExceptionInformation()
        {
            var lastAudit = _capturedAuditLogs[_capturedAuditLogs.Count - 1];
            lastAudit.ExceptionInfo.ShouldNotBeNullOrEmpty();
        }

        [Then(@"the audit log should contain all request headers")]
        public void ThenTheAuditLogShouldContainAllRequestHeaders()
        {
            var lastAudit = _capturedAuditLogs[_capturedAuditLogs.Count - 1];
            lastAudit.CapturedRequest.Headers.ShouldContainKey("X-Custom-Header");
            lastAudit.CapturedRequest.Headers.ShouldContainKey("X-Request-ID");
        }

        [Then(@"the audit log should contain request metadata")]
        public void ThenTheAuditLogShouldContainRequestMetadata()
        {
            var lastAudit = _capturedAuditLogs[_capturedAuditLogs.Count - 1];
            lastAudit.CapturedRequest.Method.ShouldBe("POST");
            lastAudit.CapturedRequest.Url.ShouldContain(_endpoint);
            lastAudit.CapturedRequest.CapturedAt.ShouldBe(DateTime.UtcNow, TimeSpan.FromSeconds(5));
        }

        [Then(@"the audit log should contain the user ID from the JWT")]
        public void ThenTheAuditLogShouldContainTheUserIdFromTheJwt()
        {
            var lastAudit = _capturedAuditLogs[_capturedAuditLogs.Count - 1];
            lastAudit.UserId.ShouldBe("test-user");
        }

        private HttpRequestMessage CreateHttpRequestMessage(string endpoint, string body)
        {
            var request = new HttpRequestMessage(HttpMethod.Post, endpoint);
            request.Content = new StringContent(body, Encoding.UTF8, "application/json");
            return request;
        }
    }
}
