# Desktop Clone

BetterBTD's desktop clone runs the primary application and BTD6 in a Windows Child Session. It is a session-isolation feature, not a background-input implementation.

## Runtime chain

The primary instance enables Child Sessions with `WTSEnableChildSessions`, then connects an embedded `MsRdpClient10` control to `localhost` with `ConnectToChildSession=true`. The RDP control is hosted by WPF through `WindowsFormsHost` and uses a 1920x1080 desktop with SmartSizing enabled.

After RDP login, the primary instance creates a temporary Task Scheduler task. `RunEx` with `TASK_RUN_USE_SESSION_ID` starts the second BetterBTD instance in the Child Session with:

```text
--instance child-session --root-session-id <primary-session-id> --child-session-pipe <pipe-name>
```

The child instance connects to the named pipe, launches BTD6 through the existing `GameLaunchService`, and starts the existing WGC capture service. Child-session input is forced to standard `SendInput` at runtime. Hardware input is disabled for the child role.

## Ownership and lifecycle

Only one BetterBTD instance may control the game. Once a child instance is ready, the primary instance blocks script execution, automatic tasks, Robot control, Test API control, and input simulation. The primary also becomes read-only for shared configuration, script assets, bindings, and detection rules. Child instances may read these files but do not persist changes.

Diagnostics are stored below a role and Windows session directory, for example `Logs/<group>/Primary-Session-1/<date>` or `Logs/<group>/Child-Session-2/<date>`.

Closing the clone window hides it and leaves the RDP connection and Child Session alive. `Log Off Clone` disconnects the RDP control and calls `WTSLogoffSession`. A later primary startup can detect an existing Child Session and reconnect to it.

The named pipe is intentionally small: `ready` announces child readiness and `exit` announces child shutdown. It is not a general cross-process control protocol.

## Requirements and limitations

- Windows 10/11 Pro or another edition that supports Windows Child Sessions and the required RDP ActiveX control.
- A local interactive user session and the standard Windows Terminal Services/RDP components.
- BTD6 must run in a normal 1920x1080-compatible window. Minimized Child Session BTD6 is unsupported.
- Steam and Epic installations must be validated separately because their launch and account-state behavior differs.
- Hiding the clone window is supported; minimizing BTD6 inside the Child Session is not a supported capture state.
- Disconnect/reconnect and crash cleanup depend on Windows session state and must be tested on a real Windows 10/11 Pro machine.

## Verification status

Unit coverage covers argument parsing, role state, primary control blocking, and child read-only behavior. The repository build does not prove the Windows integration. Before release, validate Steam and Epic BTD6 on real Windows 10/11 Pro, including launch, account state, WGC fresh frames, standard SendInput, hidden-RDP execution, primary-desktop mouse isolation, RDP reconnect, and crash cleanup.
