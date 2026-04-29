# GateRunner Local Run Runbook

## Purpose

Use this runbook when an engineer or QA owner needs a local pre-deployment gate decision for SI360.

## Assumptions

- .NET 8 SDK is installed.
- The SI360 repository is available locally.
- GateRunner can find `SI360.slnx`, or the user can set paths in the WPF Settings dialog.
- Gate metadata and decision policy are owned by the SI360 release engineering owner.

## Steps

1. Build GateRunner.

   ```powershell
   dotnet build D:\GateRunner\SI360.GateRunner.sln -c Release
   ```

2. Launch the WPF app.

   ```powershell
   dotnet run --project D:\GateRunner\SI360.GateRunner.csproj -c Release
   ```

3. Open Settings and verify:

   - solution path points to `SI360.slnx`
   - test project path points to `SI360.Tests.csproj`
   - results directory is writable
   - restore, build, and gate timeouts are positive

4. Click Run Gates or press `F5`.

5. Review the decision banner, failure inventory, catalog warnings, and generated report.

## Expected Artifacts

- Markdown summary for human review
- JSON summary for automation and trend analysis
- per-gate TRX files and command output under the run artifact directory

## Escalation

- NO-GO: release owner reviews before deployment continues.
- HOLD: release owner decides whether risk is acceptable and records rationale.
- Catalog drift: test owner reconciles GateRunner metadata with SI360 tests.
