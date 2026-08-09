# BetterBTD Script Test Protocol

This tool validates versioned BetterBTD black-box script test scenarios. It is
independent of the BetterBTD process and does not capture or control BTD6. The
future `betterbtd-script-test` orchestration Skill will consume the same format.

## Setup

From the repository root:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\BetterBTD.ScriptTest\setup.ps1
```

## Validate a scenario

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\BetterBTD.ScriptTest\validate-scenario.ps1 `
  tools\BetterBTD.ScriptTest\examples\easy-standard-victory.scenario.json
```

The command returns exit code `0` when the scenario is valid and its required
Oracle capabilities are currently available, `2` for an invalid scenario, and
`3` when the format is valid but a required capability is unavailable. Use
`--check-script-path` during execution preflight to require the referenced
script file to exist. Script paths are relative to the scenario file; artifact
directory templates are relative to the repository root, must be under
`artifacts/`, and must contain one `{scenarioId}` and one `{runId}` path segment.
Positive assertions use an explicit `Eventually` quantifier and an `Assert` or
`ActAndAssert` observation window, so execution-time and terminal observations
can be combined without treating BetterBTD diagnostics as an Oracle.
Before running a scenario, orchestration must also execute the Game Driver
`catalog` command and compare the Test API validation summary with the scenario
game state. The scenario validator does not replace either runtime preflight.

Run the protocol tests with:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\BetterBTD.ScriptTest\test.ps1
```

The normative lifecycle and Oracle rules are documented in
[`docs/developer/script-test-scenario.md`](../../docs/developer/script-test-scenario.md).
