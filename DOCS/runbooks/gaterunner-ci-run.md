# GateRunner CI Run Runbook

## Purpose

Use this runbook to run GateRunner headlessly in CI and publish the report artifacts.

## Recommended Pipeline Flow

1. Install the .NET SDK pinned by `global.json`.
2. Restore `SI360.GateRunner.sln`.
3. Build GateRunner in Release.
4. Run `SI360.GateRunner.Tests`.
5. Run `validate-catalog`.
6. Run the CLI `run` command when SI360 sources are available.
7. Publish GateRunner reports as pipeline artifacts.

## CLI Example

```powershell
dotnet run --project D:\GateRunner\SI360.GateRunner.Cli\SI360.GateRunner.Cli.csproj -c Release -- run --solution D:\SI36020WPF\SI360.slnx --test-project D:\SI36020WPF\SI360.Tests\SI360.Tests.csproj --results D:\SI36020WPF\TestResults
```

## Exit Codes

- `0`: GO
- `1`: HOLD
- `2`: NO-GO
- `3`: catalog validation warnings
- `4`: canceled
- `5`: command or configuration failure

CI should publish artifacts for all outcomes. A HOLD or NO-GO from a gate run is a release decision signal, not an infrastructure failure by itself. Catalog validation warnings fail the validation step because unresolved drift means GateRunner metadata is not authoritative enough for release decisions.

## Artifact Handoff

Publish:

- `GateRun_*.md`
- `GateRun_*.json`
- `GateRun_*` run directories with TRX and command logs

The JSON report is the stable machine-readable contract. The Markdown report is the release-review handoff.
