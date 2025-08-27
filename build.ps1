# Build script for AuditLoginPOC project
param(
    [switch]$Clean,
    [switch]$Test,
    [switch]$Benchmark,
    [switch]$Run
)

Write-Host "AuditLoginPOC Build Script" -ForegroundColor Green
Write-Host "=========================" -ForegroundColor Green

# Clean if requested
if ($Clean) {
    Write-Host "Cleaning solution..." -ForegroundColor Yellow
    dotnet clean AuditLoginPOC.sln
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Clean failed!" -ForegroundColor Red
        exit 1
    }
}

# Build solution
Write-Host "Building solution..." -ForegroundColor Yellow
dotnet build AuditLoginPOC.sln
if ($LASTEXITCODE -ne 0) {
    Write-Host "Build failed!" -ForegroundColor Red
    exit 1
}

Write-Host "Build completed successfully!" -ForegroundColor Green

# Run tests if requested
if ($Test) {
    Write-Host "Running tests..." -ForegroundColor Yellow
    dotnet test AuditLoginPOC.Tests/ --verbosity normal
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Tests failed!" -ForegroundColor Red
        exit 1
    }
    Write-Host "Tests completed successfully!" -ForegroundColor Green
}

# Run benchmarks if requested
if ($Benchmark) {
    Write-Host "Running benchmarks..." -ForegroundColor Yellow
    dotnet run --project AuditLoginPOC.Benchmarks/ --configuration Release
    if ($LASTEXITCODE -ne 0) {
        Write-Host "Benchmarks failed!" -ForegroundColor Red
        exit 1
    }
    Write-Host "Benchmarks completed successfully!" -ForegroundColor Green
}

# Run Web API if requested
if ($Run) {
    Write-Host "Starting Web API..." -ForegroundColor Yellow
    Write-Host "API will be available at: http://localhost:5000" -ForegroundColor Cyan
    Write-Host "Test endpoints:" -ForegroundColor Cyan
    Write-Host "  POST /api/test/echo" -ForegroundColor White
    Write-Host "  POST /api/test/malformed" -ForegroundColor White
    Write-Host "  POST /api/test/large" -ForegroundColor White
    Write-Host "  POST /api/test/validation" -ForegroundColor White
    Write-Host "  POST /api/test/error" -ForegroundColor White
    Write-Host ""
    Write-Host "Press Ctrl+C to stop the server" -ForegroundColor Yellow
    
    dotnet run --project AuditLoginPOC.WebApi/
}

Write-Host "Script completed!" -ForegroundColor Green
