[CmdletBinding()]
param(
    [string]$Configuration = "Release",
    [string]$Filter = "Category=Unit|Category=Architecture",
    [switch]$NoBuild,
    [string]$ResultsDirectory = "./TestResults"
)

$ErrorActionPreference = "Stop"

Write-Host "=================================================" -ForegroundColor Cyan
Write-Host " Mock Data Studio - Test Runner                  " -ForegroundColor Cyan
Write-Host " Configuration: $Configuration                   " -ForegroundColor Cyan
Write-Host " Filter:        $Filter                          " -ForegroundColor Cyan
Write-Host " Results Dir:   $ResultsDirectory                " -ForegroundColor Cyan
Write-Host "=================================================" -ForegroundColor Cyan

$solutionFile = Join-Path $PSScriptRoot "MockDataStudio.slnx"
if (-not (Test-Path $solutionFile)) {
    $solutionFile = Join-Path $PSScriptRoot "MockDataStudio.sln"
}

$testArgs = @(
    "test",
    $solutionFile,
    "-c", $Configuration,
    "--filter", $Filter,
    "--logger", "trx;LogFileName=test-results.trx",
    "--results-directory", $ResultsDirectory
)

if ($NoBuild) {
    $testArgs += "--no-build"
}

Write-Host "`nExecutando testes com dotnet test..." -ForegroundColor Yellow
dotnet @testArgs
$exitCode = $LASTEXITCODE

if ($exitCode -eq 0) {
    Write-Host "`nTodos os testes foram executados com sucesso!" -ForegroundColor Green
} else {
    Write-Error "`nFalha na execução dos testes (Código de saída: $exitCode)."
}

exit $exitCode
