$ErrorActionPreference = "Stop"
$toolRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$venvPython = Join-Path $toolRoot ".venv\Scripts\python.exe"
if (-not (Test-Path -LiteralPath $venvPython)) {
    Write-Error "Game Driver environment is missing. Run tools\BetterBTD.GameDriver\setup.ps1 first."
    exit 2
}

Push-Location $toolRoot
try {
    & $venvPython -m unittest discover -s tests -v
    exit $LASTEXITCODE
}
finally {
    Pop-Location
}
