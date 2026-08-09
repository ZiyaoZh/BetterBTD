$ErrorActionPreference = "Stop"
$toolRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$venvPython = Join-Path $toolRoot ".venv\Scripts\python.exe"
if (-not (Test-Path -LiteralPath $venvPython)) {
    Write-Error "Script Test environment is missing. Run tools\BetterBTD.ScriptTest\setup.ps1 first."
    exit 2
}

$previousPythonPath = $env:PYTHONPATH
try {
    $env:PYTHONPATH = if ([string]::IsNullOrEmpty($previousPythonPath)) {
        $toolRoot
    }
    else {
        "$toolRoot;$previousPythonPath"
    }
    & $venvPython -m betterbtd_script_test @args
    exit $LASTEXITCODE
}
finally {
    $env:PYTHONPATH = $previousPythonPath
}
