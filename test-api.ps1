# Test script for AuditLoginPOC API
param(
    [string]$BaseUrl = "http://localhost:5000"
)

Write-Host "AuditLoginPOC API Test Script" -ForegroundColor Green
Write-Host "=============================" -ForegroundColor Green
Write-Host "Base URL: $BaseUrl" -ForegroundColor Cyan
Write-Host ""

# Function to make HTTP requests
function Invoke-TestRequest {
    param(
        [string]$Endpoint,
        [string]$Method = "POST",
        [string]$Body = "",
        [hashtable]$Headers = @{}
    )
    
    $url = "$BaseUrl$Endpoint"
    $defaultHeaders = @{
        "Content-Type" = "application/json"
    }
    
    # Merge headers
    $allHeaders = $defaultHeaders.Clone()
    foreach ($key in $Headers.Keys) {
        $allHeaders[$key] = $Headers[$key]
    }
    
    try {
        $response = Invoke-RestMethod -Uri $url -Method $Method -Body $Body -Headers $allHeaders -ErrorAction Stop
        Write-Host "✅ $Method $Endpoint - Success" -ForegroundColor Green
        Write-Host "   Response: $($response | ConvertTo-Json -Depth 2)" -ForegroundColor Gray
    }
    catch {
        $statusCode = $_.Exception.Response.StatusCode.value__
        Write-Host "❌ $Method $Endpoint - Failed ($statusCode)" -ForegroundColor Red
        Write-Host "   Error: $($_.Exception.Message)" -ForegroundColor Gray
    }
    Write-Host ""
}

# Test 1: Normal JSON request
Write-Host "Test 1: Normal JSON request" -ForegroundColor Yellow
Invoke-TestRequest -Endpoint "/api/test/echo" -Body '{"name":"test","value":123,"active":true}'

# Test 2: Malformed JSON request
Write-Host "Test 2: Malformed JSON request" -ForegroundColor Yellow
Invoke-TestRequest -Endpoint "/api/test/malformed" -Body '{ invalid json }'

# Test 3: Large JSON request
Write-Host "Test 3: Large JSON request" -ForegroundColor Yellow
$largeData = @{}
for ($i = 0; $i -lt 1000; $i++) {
    $largeData["field$i"] = "value$i"
}
$largeJson = $largeData | ConvertTo-Json
Invoke-TestRequest -Endpoint "/api/test/large" -Body $largeJson

# Test 4: Person validation with extra field (should be ignored by validation but captured in audit)
Write-Host "Test 4: Person validation with extra field" -ForegroundColor Yellow
Invoke-TestRequest -Endpoint "/api/test/validation" -Body '{"FirstName":"John","LastName":"Doe","Email":"john.doe@example.com","Age":32,"Gender":"male"}'

# Test 4b: Person validation error request
Write-Host "Test 4b: Person validation error request" -ForegroundColor Yellow
Invoke-TestRequest -Endpoint "/api/test/validation" -Body '{"FirstName":"","LastName":"","Email":"invalid-email","Age":-1,"Gender":"male"}'

# Test 5: Error request
Write-Host "Test 5: Error request" -ForegroundColor Yellow
Invoke-TestRequest -Endpoint "/api/test/error" -Body '{"test":"data"}'

# Test 6: Request with custom headers
Write-Host "Test 6: Request with custom headers" -ForegroundColor Yellow
Invoke-TestRequest -Endpoint "/api/test/echo" -Body '{"test":"data"}' -Headers @{
    "X-Custom-Header" = "custom-value"
    "X-Request-ID" = [System.Guid]::NewGuid().ToString()
}

# Test 7: Request with JWT token
Write-Host "Test 7: Request with JWT token" -ForegroundColor Yellow
$jwtToken = "eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJzdWIiOiJ0ZXN0LXVzZXIiLCJuYW1lIjoiVGVzdCBVc2VyIiwiaWF0IjoxNTE2MjM5MDIyfQ.SflKxwRJSMeKKF2QT4fwpMeJf36POk6yJV_adQssw5c"
Invoke-TestRequest -Endpoint "/api/test/echo" -Body '{"test":"data"}' -Headers @{
    "Authorization" = "Bearer $jwtToken"
}

Write-Host "All tests completed!" -ForegroundColor Green
Write-Host "Check the console output for audit logs." -ForegroundColor Cyan
