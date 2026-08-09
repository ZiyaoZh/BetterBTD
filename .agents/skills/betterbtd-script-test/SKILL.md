---
name: betterbtd-script-test
description: Orchestrate versioned BetterBTD black-box script scenarios against a real BTD6 window by combining the independent btd6-game-driver workflow with the authenticated BetterBTD Test API. Use to prepare scenario runs, establish Arrange state, execute and observe scripts without interference, evaluate external visual assertions, cancel interrupted operations, authorize Recover input, and produce auditable reports. Never use BetterBTD diagnostics as a pass Oracle.
---

# BetterBTD Script Test

Use the deterministic phase handoffs in `tools\BetterBTD.ScriptTest\script-test.ps1`. Also load `$btd6-game-driver` for Arrange and Recover navigation; do not reproduce Game Driver capture or input logic here.

## Preserve the Oracle boundary

- Determine visible game outcomes only from Game Driver evidence and recognition.
- Keep every Test API response under `nonOracleDiagnostics` in reports.
- Treat API `Completed` as the gate into Assert, never as test success.
- Classify unusable evidence, missing Oracle capability, or an incomplete input-release gate as `InfrastructureError`.
- Classify a completed Act with an unmet external predicate, a forbidden observed state, or a non-Completed operation as `Failed` unless a higher-priority infrastructure error exists.

## Prepare a session

Require both tool environments and a scenario whose `script.path` exists. Require BetterBTD to be running with Test API enabled and a fresh token of at least 32 characters already present in `BETTERBTD_TEST_API_TOKEN` in the process invoking the runner. Never print the token, pass it on a command line, save it in a scenario or artifact, or expose it to Game Driver child processes.

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\BetterBTD.ScriptTest\script-test.ps1 `
  validate <scenario.json> --check-script-path

powershell -NoProfile -ExecutionPolicy Bypass -File tools\BetterBTD.ScriptTest\script-test.ps1 `
  prepare <scenario.json>
```

Record the returned `runId` and `artifactDirectory`. Use that run only once. `prepare` binds the scenario hash and creates an atomic session manifest before any input.

## Arrange

Use Game Driver controls with `--phase arrange`. Store every control trace under a unique directory below `<artifactDirectory>\inputs\arrange\`. Re-observe after each action and continue until all `arrange.readyWhen` predicates are independently visible.

Do not start a BetterBTD script manually. Do not call Test API execute directly. The deterministic runner performs a fresh Arrange observation, validates the real Game Driver catalog, checks Test API health, starts BetterBTD capture against the same window handle, revalidates the script and metadata, and binds the returned SHA-256 to execute.

## Run Act and Assert

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\BetterBTD.ScriptTest\script-test.ps1 `
  run-act-assert <scenario.json> --run-id <run-id>
```

Let this command retain control until it returns. It permits only non-activating Game Driver capture and recognition after execute, polls operation status, paginates logs, evaluates Act/Assert windows, saves positive assertion evidence, and waits for the complete Recover gate.

Interpret exit codes as follows:

- `0`: scenario `Passed`; still inspect `recoverAuthorized` before recovery.
- `1`: scenario `Failed`; recover only when `recoverAuthorized=true`.
- `3`: preflight or `InfrastructureError`; never infer Recover permission from the exit code.

If the runner is interrupted, exits after execute, or returns `recoverAuthorized=false` with the session still in `ActStarting`, `Act`, or `Assert`, run the deterministic cleanup command before any Game Driver input:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\BetterBTD.ScriptTest\script-test.ps1 `
  cancel-and-gate <scenario.json> --run-id <run-id>
```

Treat cancel acceptance as pending. `ActStarting` allows the cleanup command to adopt the current operation only when Test API diagnostics identify the same scenario script and its acceptance time falls within the bounded execute handoff. Only the command's final `recoverAuthorized=true` transfers input. If it remains false, preserve artifacts and restart BetterBTD before attempting another run; never probe the game with a click or key.

## Recover and finalize

When and only when `recoverAuthorized=true`, use Game Driver controls with `--phase recover`. Store each operation below `<artifactDirectory>\inputs\recover\`, re-observe after each input, and reach every `recover.targetWhen` predicate.

Then verify the terminal recovery state independently:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\BetterBTD.ScriptTest\script-test.ps1 `
  verify-recover <scenario.json> --run-id <run-id>
```

Keep `session.json`, `journal.json`, `report.json` or `abort.json`, all referenced screenshot triples and recognition outputs, API log diagnostics, and Arrange/Recover operation traces. Do not delete or overwrite failed runs.

## Refuse unsafe shortcuts

- Do not handcraft HTTP requests or Authorization headers when the runner supports the operation.
- Do not use BetterBTD capture, OCR, logs, checkpoints, retries, or result objects to fill an external assertion.
- Do not invoke `click`, `click-point`, `scroll-point`, `drag-point`, or `press-key` from execute until explicit Recover authorization.
- Do not reuse a run ID, scenario session, validation digest, or Test API token.
- Do not continue after the runner reports a scenario/script metadata mismatch.

Read [the scenario protocol](../../../docs/developer/script-test-scenario.md) for predicate and result semantics, [the Test API protocol](../../../docs/developer/test-api.md) for lifecycle diagnostics, and [the Script Test README](../../../tools/BetterBTD.ScriptTest/README.md) for deterministic command details.
