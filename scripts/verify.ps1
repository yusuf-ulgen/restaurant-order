# Monorepo Full Verification Pipeline
param()

$ErrorActionPreference = "Stop"
Write-Host "=== Starting Full Monorepo Verification ===" -ForegroundColor Cyan

Write-Host "`n1. Building .NET Solution..." -ForegroundColor Yellow
dotnet build RestaurantOrder.sln -c Release
if ($LASTEXITCODE -ne 0) { throw ".NET build failed" }

Write-Host "`n2. Running .NET Tests..." -ForegroundColor Yellow
dotnet test RestaurantOrder.sln -c Release --no-build
if ($LASTEXITCODE -ne 0) { throw ".NET tests failed" }

Write-Host "`n3. Running TypeScript Typecheck..." -ForegroundColor Yellow
pnpm typecheck
if ($LASTEXITCODE -ne 0) { throw "TypeScript typecheck failed" }

Write-Host "`n4. Running ESLint..." -ForegroundColor Yellow
pnpm lint
if ($LASTEXITCODE -ne 0) { throw "ESLint failed" }

Write-Host "`n5. Running Frontend Builds..." -ForegroundColor Yellow
pnpm build
if ($LASTEXITCODE -ne 0) { throw "Frontend build failed" }

Write-Host "`n=== All Verification Checks PASSED ===" -ForegroundColor Green
