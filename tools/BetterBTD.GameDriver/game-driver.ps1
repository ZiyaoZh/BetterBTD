param(
    [Parameter(ValueFromRemainingArguments = $true)]
    [string[]] $DriverArguments
)

$toolRoot = Split-Path -Parent $MyInvocation.MyCommand.Path
$venvPython = Join-Path $toolRoot ".venv\Scripts\python.exe"
if (-not (Test-Path -LiteralPath $venvPython)) {
    Write-Error "Game Driver environment is missing. Run tools\BetterBTD.GameDriver\setup.ps1 first."
    exit 2
}

& $venvPython (Join-Path $toolRoot "btd6_game_driver.py") @DriverArguments
exit $LASTEXITCODE
