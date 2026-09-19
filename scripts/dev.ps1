# Local Development Startup Script
param(
    [switch]$WithDocker
)

$ErrorActionPreference = "Stop"
Write-Host "=== Restaurant Order Development Environment ===" -ForegroundColor Cyan

if ($WithDocker) {
    Write-Host "Starting Docker Compose dependencies (Postgres & Redis)..." -ForegroundColor Yellow
    docker compose -f deploy/docker-compose.yml up -d
}

Write-Host "`nTo start services in development:"
Write-Host "  API:          dotnet run --project apps/api"
Write-Host "  Worker:       dotnet run --project apps/worker"
Write-Host "  Customer:     pnpm dev:customer   (http://localhost:3001)"
Write-Host "  Operations:   pnpm dev:operations (http://localhost:3002)"
Write-Host "  Admin:        pnpm dev:admin      (http://localhost:3003)"
