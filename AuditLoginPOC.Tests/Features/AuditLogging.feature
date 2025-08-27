@AcceptanceTest
Feature: HTTP Request Audit Logging
  As a security administrator
  I want to capture and log all HTTP requests
  So that I can audit and monitor application usage

  Background:
    Given the audit logging system is configured
    And the test API is running

  Scenario: Capture normal JSON request body
    When I send a POST request to "/api/test/echo" with valid JSON body
    Then the request body should be captured in the audit log
    And the audit log should contain the original request data
    And the response should be successful

  Scenario: Capture malformed JSON request body
    When I send a POST request to "/api/test/malformed" with malformed JSON body
    Then the malformed request body should be captured in the audit log
    And the audit log should contain the exact raw request data
    And the response should indicate an error

  Scenario: Handle large request with size limit protection
    When I send a POST request to "/api/test/large" with a large JSON body
    Then the request should be processed with size limit protection
    And the audit log should contain a summary of the large request
    And the response should be successful

  Scenario: Capture Person validation with extra field
    When I send a POST request to "/api/test/validation" with Person data including extra field
    Then the request body should be captured in the audit log including the extra field
    And the Person validation should pass
    And the extra field should be ignored by validation but captured in audit
    And the response should be successful

  Scenario: Capture request with Person validation errors
    When I send a POST request to "/api/test/validation" with invalid Person data
    Then the request body should be captured in the audit log
    And the audit log should contain FluentValidation error information
    And the response should indicate validation errors

  Scenario: Handle request that throws an exception
    When I send a POST request to "/api/test/error" with any data
    Then the request body should be captured in the audit log
    And the audit log should contain exception information
    And the response should indicate an error

  Scenario: Capture request headers and metadata
    When I send a request with custom headers
    Then the audit log should contain all request headers
    And the audit log should contain request metadata
    And the response should be successful

  Scenario: Handle request with JWT authentication
    When I send a request with a valid JWT token
    Then the audit log should contain the user ID from the JWT
    And the request body should be captured in the audit log
    And the response should be successful
