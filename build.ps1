[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [switch]$Clean,
    [switch]$NoRestore
)

$ErrorActionPreference = "Stop"

Write-Host "=================================================" -ForegroundColor Cyan
Write-Host " Mock Data Studio - Build Script                 " -ForegroundColor Cyan
Write-Host " Configuration: $Configuration                   " -ForegroundColor Cyan
Write-Host "=================================================" -ForegroundColor Cyan

$solutionFile = Join-Path $PSScriptRoot "MockDataStudio.slnx"
if (-not (Test-Path $solutionFile)) {
    $solutionFile = Join-Path $PSScriptRoot "MockDataStudio.sln"
}

if ($Clean) {
    Write-Host "`n[1/3] Limpando artefatos anteriores..." -ForegroundColor Yellow
    dotnet clean $solutionFile -c $Configuration
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Falha ao limpar a solução."
        exit $LASTEXITCODE
    }
}

if (-not $NoRestore) {
    Write-Host "`n[2/3] Restaurando pacotes NuGet..." -ForegroundColor Yellow
    dotnet restore $solutionFile
    if ($LASTEXITCODE -ne 0) {
        Write-Error "Falha ao restaurar dependências NuGet."
        exit $LASTEXITCODE
    }
}

Write-Host "`n[3/3] Compilando solução ($Configuration)..." -ForegroundColor Yellow
$buildArgs = @("build", $solutionFile, "-c", $Configuration)
if (-not $NoRestore) {
    $buildArgs += "--no-restore"
}

dotnet @buildArgs
if ($LASTEXITCODE -ne 0) {
    Write-Error "Falha na compilação da solução."
    exit $LASTEXITCODE
}

Write-Host "`nCompilação concluída com sucesso!" -ForegroundColor Green
exit 0
