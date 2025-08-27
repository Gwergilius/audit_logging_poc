using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Web.Http;
using Newtonsoft.Json;
using System.ComponentModel.DataAnnotations;
using System.Web.Http.ModelBinding;
using System.Linq;
using FluentValidation;
using AuditLoginPOC.WebApi.Models;
using AuditLoginPOC.WebApi.Validators;

namespace AuditLoginPOC.WebApi.Controllers
{
    /// <summary>
    /// Test controller for demonstrating audit logging functionality
    /// </summary>
    [RoutePrefix("api/test")]
    public class TestController : ApiController
    {
        private readonly IValidator<Person> _personValidator;

        public TestController(IValidator<Person> personValidator)
        {
            _personValidator = personValidator ?? throw new ArgumentNullException(nameof(personValidator));
        }

        /// <summary>
        /// Simple test endpoint that returns the request data
        /// </summary>
        [HttpPost]
        [Route("echo")]
        public IHttpActionResult Echo([FromBody] object requestData)
        {
            return Ok(new
            {
                Message = "Request received and processed",
                Timestamp = DateTime.UtcNow,
                RequestData = requestData,
                Headers = Request.Headers.ToString()
            });
        }

        /// <summary>
        /// Test endpoint for malformed JSON (should still be captured in audit)
        /// </summary>
        [HttpPost]
        [Route("malformed")]
        public IHttpActionResult MalformedJson([FromBody] object requestData)
        {
            return Ok(new { Message = "Malformed JSON request processed", Data = requestData });
        }

        /// <summary>
        /// Test endpoint for large requests
        /// </summary>
        [HttpPost]
        [Route("large")]
        public IHttpActionResult LargeRequest([FromBody] object requestData)
        {
            var dataSize = requestData?.ToString()?.Length ?? 0;
            return Ok(new
            {
                Message = "Large request processed",
                DataSize = dataSize,
                IsLarge = dataSize > 1024 * 1024, // > 1MB
                Timestamp = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Test endpoint for Person validation using FluentValidation
        /// </summary>
        [HttpPost]
        [Route("validation")]
        public IHttpActionResult ValidationTest([FromBody] Person person)
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

            return Ok(new
            {
                Message = "Person validation passed",
                Person = person,
                Timestamp = DateTime.UtcNow
            });
        }

        /// <summary>
        /// Test endpoint that throws an exception
        /// </summary>
        [HttpPost]
        [Route("error")]
        public IHttpActionResult ErrorTest([FromBody] object requestData)
        {
            // Log the request data before throwing exception
            System.Diagnostics.Debug.WriteLine($"ErrorTest called with: {requestData}");
            throw new InvalidOperationException("Test exception for audit logging");
        }
    }

    /// <summary>
    /// Test model for validation testing (kept for backward compatibility)
    /// </summary>
    public class ValidationTestModel
    {
        [Required]
        public string Name { get; set; }

        [Range(1, 100)]
        public int Age { get; set; }

        [EmailAddress]
        public string Email { get; set; }
    }
}
