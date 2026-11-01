# sac-test

First Smart App Control (SAC) smoke test, run on the laptop on 2026-11-01 at about 10:57 local time, before any other probe.

| File | What |
|---|---|
| `hello.cs` | One line: prints "hello from an unsigned, locally compiled exe". |

## Command

```
%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe /nologo /out:hello.exe hello.cs
hello.exe
```

(The compile line was not recorded; the research notes say the exe was compiled locally with `csc`.)

## Result

- SAC was ON (enforced): `VerifiedAndReputablePolicyState=1`, Defender `SmartAppControlState` On.
- The unsigned, freshly compiled `hello.exe` ran and printed its line. Nothing was blocked.
- This one pass proved little. Later the same day SAC blocked 1 of 10 freshly compiled unsigned probe executables (see section 10 of `../../phase1-diagnostics-laptop.md`). A representative gate is therefore specified in `docs/ARCHITECTURE.md` section 7 (step 4).

## Check the SAC state and blocks on another PC

```
reg query HKLM\SYSTEM\CurrentControlSet\Control\CI\Policy /v VerifiedAndReputablePolicyState
Get-MpComputerStatus | Select-Object SmartAppControlState
Get-WinEvent -LogName Microsoft-Windows-CodeIntegrity/Operational -MaxEvents 50 | Where-Object { $_.Id -in 3033,3077,3118 } | Format-List TimeCreated,Id,Message
```

`VerifiedAndReputablePolicyState`: 0 off, 1 on (enforcement), 2 evaluation. Turning SAC off is effectively one-way on Windows 11 (re-enabling needs a Windows reset per Microsoft Learn), so never change it without the owner's decision.

## Capture-endpoint check

Result: no violation. `hello.cs` does not touch audio at all.

## Caveats
- A single trivial exe. SAC is reputation based and decisions can change over time.
- The SAC state of the Ryzen 7 5800X machine (h0wzy-pc) is unknown; check it before assuming the same behaviour.
