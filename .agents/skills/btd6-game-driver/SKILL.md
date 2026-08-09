---
name: btd6-game-driver
description: Independently observe and control a real Windows BTD6 window with BetterBTD.GameDriver. Use to find, launch, restore, or activate BTD6; capture auditable client screenshots; recognize bundled page, view-state, element, state, and number IDs; establish Arrange state; or recover the game after BetterBTD releases input. Enforce external-Oracle evidence and never use Game Driver input during a BetterBTD script's Act or Assert phase. Do not use this Skill to call the BetterBTD Test API or orchestrate a full script scenario.
---

# BTD6 Game Driver

Operate only through `tools\BetterBTD.GameDriver\game-driver.ps1` from the repository root. Keep the driver independent of BetterBTD assemblies, services, screenshots, OCR, and runtime state.

## Establish the boundary

- Treat the saved real BTD6 client PNG as raw evidence and Game Driver recognition as the black-box Oracle.
- Treat BetterBTD screenshots, OCR, UI state, logs, script progress, and return values as non-Oracle diagnostics.
- Allow Game Driver input only in `Arrange` or `Recover`.
- During `Act` and `Assert`, allow only `catalog`, `capture --no-activate`, and `recognize`.
- Enter `Recover` only after the Script Test runner or Test API reports a terminal operation plus `inputOwner=None`, `inputControlReleased=true`, and `canGameDriverRecover=true`.

## Prepare the driver

Require Windows with a visible, unobscured BTD6 window on an interactive desktop. The current visual catalog is primarily verified for Chinese 16:9 gameplay.

Run setup only when `.venv` is missing:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\BetterBTD.GameDriver\setup.ps1
```

Validate the bundled independent catalog before a formal run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\BetterBTD.GameDriver\game-driver.ps1 catalog
```

Do not substitute an arbitrary `--catalog`. Do not rebuild baselines during a test run.

## Observe

Locate only BTD6 windows unless the user explicitly requests broader discovery:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\BetterBTD.GameDriver\game-driver.ps1 windows
```

Capture to a new evidence path and recognize through its metadata JSON:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\BetterBTD.GameDriver\game-driver.ps1 capture `
  --output artifacts\game-driver\<run-id>\<sample-id>.png

powershell -NoProfile -ExecutionPolicy Bypass -File tools\BetterBTD.GameDriver\game-driver.ps1 recognize `
  --evidence artifacts\game-driver\<run-id>\<sample-id>.json
```

Add `--no-activate` for Act/Assert observation. Pin subsequent samples with the returned window handle when more than one matching window may exist.

Accept an observation only when all required evidence files and hashes validate, `recognition.status=matched`, and `recognition.oracleEligible=true`. Independently require the relevant view state, element state, or number result to be matched and Oracle eligible. A command exit code of zero alone is insufficient.

## Control

Prefer stable element IDs. Always provide the true phase, a unique output directory, and a modeled final page or view when known:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools\BetterBTD.GameDriver\game-driver.ps1 click `
  --element <page.element> `
  --phase arrange `
  --output-dir artifacts\script-tests\<scenario>\<run>\inputs\arrange\<step> `
  --expect-page <page-id>
```

Use `--phase recover` only after explicit Recover authorization. Re-observe after every input; never assume a successful Win32 call means BTD6 accepted it.

Use `click-point`, `scroll-point`, `drag-point`, or `press-key` only for explicit exploration or navigation that lacks a safe element action. These commands do not create element-level Oracle evidence. Do not use `--allow-no-change` as proof of success. Do not lower visual thresholds unless a committed, real trace documents the calibrated value.

## Fail closed

- Stop sending input when recognition is `unknown`, `ambiguous`, warned, malformed, or non-Oracle.
- Never guess a coordinate after an element click is rejected.
- Never use annotated images or transition `expectationProbe` frames as assertion evidence.
- Never use `--overwrite` in a formal run; preserve failed evidence and operation traces.
- Never enumerate all desktop windows with `windows --all` unless explicitly needed.
- Never send `F10` or system-level key combinations; rely on the CLI key allowlist.

The driver currently has no text input command and no general wait-for-element command. Implement bounded observation through the Script Test runner instead of ad hoc polling during a scenario.

Read [the Game Driver guide](../../../docs/developer/game-driver.md) for the evidence, coordinate, catalog, and input protocols. Read [the CLI README](../../../tools/BetterBTD.GameDriver/README.md) for exact command options and verified limitations.
