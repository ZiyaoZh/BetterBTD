# BetterBTD Script Test

This tool validates and runs versioned BetterBTD black-box script test scenarios. It orchestrates the authenticated BetterBTD Test API and the independent BetterBTD.GameDriver CLI without implementing game capture or input itself.

The deterministic runner owns API authentication, script digest binding, Act/Assert observation, predicate evaluation, log pagination, cancellation, Recover authorization, and report writing. An Agent still chooses Arrange and Recover navigation through the Game Driver because scenario-v1 describes target states rather than input sequences.

## Setup

From the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\BetterBTD.ScriptTest\setup.ps1
```

Set up `tools\BetterBTD.GameDriver` separately before a live run. BetterBTD must already be running with Test API enabled. Supply its fresh token only through `BETTERBTD_TEST_API_TOKEN` in the process invoking `script-test.ps1`; do not put the token in a scenario, command argument, or artifact.

## Validate a scenario

The compatibility wrapper remains available:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\BetterBTD.ScriptTest\validate-scenario.ps1 `
  tools\BetterBTD.ScriptTest\examples\easy-standard-victory.scenario.json
```

Formal preflight must require the script file:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\BetterBTD.ScriptTest\script-test.ps1 `
  validate <scenario.json> --check-script-path
```

Validation returns `0` when the scenario is valid and all required Oracle capabilities are available, `2` for an invalid scenario, and `3` when the format is valid but a capability is unavailable. It does not connect to BetterBTD or control BTD6.

## Run a scenario

Create the evidence directory and immutable session binding before Arrange:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\BetterBTD.ScriptTest\script-test.ps1 `
  prepare <scenario.json>
```

Use the returned `runId` and `artifactDirectory` for Game Driver Arrange traces. After the game independently satisfies `arrange.readyWhen`, hand off Act and Assert:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\BetterBTD.ScriptTest\script-test.ps1 `
  run-act-assert <scenario.json> --run-id <run-id>
```

The runner performs a fresh Arrange capture, validates the live Game Driver catalog, verifies Test API health, starts BetterBTD capture against the same window handle, validates script metadata, passes the returned SHA-256 to execute, and uses only non-activating Game Driver observations until the operation is terminal. It writes `journal.json` during execution and an atomic `report.json` at completion.

Exit code `0` is `Passed`, `1` is `Failed`, and `3` is a preflight or infrastructure error. None of these codes independently authorizes Game Driver input. Recover requires the JSON result field `recoverAuthorized=true`.

If the runner is interrupted after execute, or an execute response is lost while `session.json` remains `ActStarting`, cancel and wait for the full input-release gate:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\BetterBTD.ScriptTest\script-test.ps1 `
  cancel-and-gate <scenario.json> --run-id <run-id>
```

This command writes `abort.json` and always returns the infrastructure-error exit code. For an `ActStarting` session without a stored ID, it adopts only a current operation whose diagnostics name the same resolved script path and whose acceptance time falls within the bounded execute handoff. It may still return `recoverAuthorized=true` after safe cancellation. A false value forbids all Game Driver input and normally requires restarting BetterBTD.

After authorized Agent recovery reaches `recover.targetWhen`, save a fresh independent verification:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\BetterBTD.ScriptTest\script-test.ps1 `
  verify-recover <scenario.json> --run-id <run-id>
```

`report.json` classifies the tested Act and external assertions. `session.json` reaches `Completed` only after recovery verification; a recovery navigation failure leaves the session in `Recover` so it can be corrected without rewriting the test result.

## Security and evidence

Test API responses remain under `nonOracleDiagnostics`. The Game Driver subprocess receives a scrubbed environment without token, password, credential, authorization, private-key, or secret variables. API clients refuse proxies, redirects, hostnames, non-loopback addresses, HTTPS, and non-root base URLs.

Every positive assertion records the actual screenshot metadata and recognition paths that satisfied it. `unknown`, `ambiguous`, warned, malformed, and non-Oracle samples fail closed. Failed and infrastructure runs preserve all observations, status history, available logs, and input traces; the runner never overwrites an existing run directory.

Run tests with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\BetterBTD.ScriptTest\test.ps1
```

The normative lifecycle and Oracle rules are documented in [`docs/developer/script-test-scenario.md`](../../docs/developer/script-test-scenario.md). The repository Skills are [`btd6-game-driver`](../../.agents/skills/btd6-game-driver/SKILL.md) and [`betterbtd-script-test`](../../.agents/skills/betterbtd-script-test/SKILL.md).
