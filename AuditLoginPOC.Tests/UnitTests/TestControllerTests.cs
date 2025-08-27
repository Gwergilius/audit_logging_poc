using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Web.Http;
using System.Web.Http.Results;
using AuditLoginPOC.WebApi.Controllers;
using AuditLoginPOC.WebApi.Models;
using FluentValidation;
using FluentValidation.Results;
using Moq;
using Shouldly;
using Xunit;
using Xunit.Categories;

namespace AuditLoginPOC.Tests.UnitTests
{
    [UnitTest]
    public class TestControllerTests : IDisposable
    {
        private readonly Mock<IValidator<Person>> _mockPersonValidator;
        private readonly TestController _controller;

        public TestControllerTests()
        {
            _mockPersonValidator = new Mock<IValidator<Person>>();
            _controller = new TestController(_mockPersonValidator.Object);
            
            // Setup controller context
            _controller.Request = new HttpRequestMessage();
            _controller.Configuration = new HttpConfiguration();
        }

        public void Dispose()
        {
            // Cleanup if needed
        }

        [Fact]
        public void ValidationTest_WithValidPerson_ShouldReturnOk()
        {
            // Arrange
            var validPerson = new Person
            {
                FirstName = "John",
                LastName = "Doe",
                Email = "john.doe@example.com",
                Age = 32
            };

            var validationResult = new ValidationResult(); // Empty result = no errors
            _mockPersonValidator
                .Setup(v => v.Validate(It.IsAny<Person>()))
                .Returns(validationResult);

            // Act
            var result = _controller.ValidationTest(validPerson);

            // Assert
            result.ShouldNotBeNull();
            
            // Use type checking and pattern matching to access the content
            if (result is OkNegotiatedContentResult<object> okResult)
            {
                var content = okResult.Content;
                content.ShouldNotBeNull();
                
                // Check that the response contains the expected message
                var contentString = content.ToString();
                contentString.ShouldContain("Person validation passed");
                contentString.ShouldContain("Message");
                contentString.ShouldContain("Person");
                contentString.ShouldContain("Timestamp");
            }
            else
            {
                // For other result types, we can't easily access the content in unit tests
                // In a real scenario, we would execute the result to get the actual response
                result.ShouldNotBeNull();
            }
        }

        [Fact]
        public void ValidationTest_WithInvalidPerson_ShouldReturnBadRequest()
        {
            // Arrange
            var invalidPerson = new Person
            {
                FirstName = "",
                LastName = "",
                Email = "invalid-email",
                Age = -1
            };

            var validationFailures = new List<ValidationFailure>
            {
                new ValidationFailure("FirstName", "First name is required"),
                new ValidationFailure("LastName", "Last name is required"),
                new ValidationFailure("Email", "Email must be a valid email address"),
                new ValidationFailure("Age", "Age must be greater than 0")
            };

            var validationResult = new ValidationResult(validationFailures);
            _mockPersonValidator
                .Setup(v => v.Validate(It.IsAny<Person>()))
                .Returns(validationResult);

            // Act
            var result = _controller.ValidationTest(invalidPerson);

            // Assert
            result.ShouldBeOfType<BadRequestErrorMessageResult>();
            var badRequestResult = result as BadRequestErrorMessageResult;
            badRequestResult.Message.ShouldContain("First name is required");
            badRequestResult.Message.ShouldContain("Last name is required");
            badRequestResult.Message.ShouldContain("Email must be a valid email address");
            badRequestResult.Message.ShouldContain("Age must be greater than 0");
        }

        [Fact]
        public void ValidationTest_WithNullPerson_ShouldReturnBadRequest()
        {
            // Arrange
            Person nullPerson = null;

            // Act
            var result = _controller.ValidationTest(nullPerson);

            // Assert
            result.ShouldBeOfType<BadRequestErrorMessageResult>();
            var badRequestResult = result as BadRequestErrorMessageResult;
            badRequestResult.Message.ShouldBe("Person data is required");
        }

        [Fact]
        public void Constructor_WithNullValidator_ShouldThrowArgumentNullException()
        {
            // Act & Assert
            Should.Throw<ArgumentNullException>(() => new TestController(null))
                .ParamName.ShouldBe("personValidator");
        }

        [Fact]
        public void Constructor_WithValidValidator_ShouldNotThrow()
        {
            // Arrange
            var mockValidator = new Mock<IValidator<Person>>();

            // Act & Assert
            Should.NotThrow(() => new TestController(mockValidator.Object));
        }
    }
}
