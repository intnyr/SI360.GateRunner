# SI360 Pre-Deployment Gate Runner

GateRunner is a WPF and CLI tool that runs the SI360 pre-deployment gate tests, aggregates TRX results, computes the deployment scorecard, and emits GO / HOLD / NO-GO reports.

It is intentionally out-of-tree from `SI360.slnx`: GateRunner reads build output, TRX files, and gate metadata rather than taking a project dependency on SI360 application assemblies.

## Quick Start

```powershell
dotnet restore SI360.GateRunner.sln
dotnet build SI360.GateRunner.sln -c Release
dotnet run --project .\SI360.GateRunner.Cli\SI360.GateRunner.Cli.csproj -- validate-catalog
```

For the desktop app:

```powershell
dotnet run --project .\SI360.GateRunner.csproj -c Release
```

For a release artifact:

```powershell
powershell -ExecutionPolicy Bypass -File .\Scripts\Publish-GateRunner.ps1 -Version 1.0.0
```

The publish script restores, builds, tests, and publishes framework-dependent `win-x86` WPF output plus CLI output under `artifacts\GateRunner\<version>`.

## Settings

GateRunner auto-discovers `SI360.slnx` by probing from the app directory and known development roots. The WPF app also has a Settings dialog for:

- SI360 solution path
- SI360 test project path
- results directory
- restore, build, and gate timeouts

Settings are saved to `%APPDATA%\SI360.GateRunner\settings.json`.

Environment variables prefixed with `GATERUNNER_` can override configuration for automation. The CLI also supports:

```powershell
dotnet run --project .\SI360.GateRunner.Cli -- run --solution D:\SI36020WPF\SI360.slnx --test-project D:\SI36020WPF\SI360.Tests\SI360.Tests.csproj --results D:\SI36020WPF\TestResults
```

## Commands

```text
discover                         List discovered pre-deployment gates.
validate-catalog                 Validate catalog expectations against SI360.Tests source.
run                              Restore, build, run all gates, and emit reports.
summarize [--report <path>]      Print a JSON report.
```

## Report Contract

Each run writes:

- `<ResultsDirectory>\GateRun_<yyyyMMdd_HHmmss>.md`
- `<ResultsDirectory>\GateRun_<yyyyMMdd_HHmmss>.json`
- per-command and per-gate artifacts under `<ResultsDirectory>\GateRun_<yyyyMMdd_HHmmss>\`

The JSON report includes:

- `schemaVersion`: current contract version, currently `2.0`
- `startedAt` and `durationSeconds`
- `decision`
- `environment`: tool version, machine, OS, runtime, SDK, repo path, branch, commit, artifact directory, command snapshots
- `decisionPolicy`: policy name, version, and rationale
- `gateCatalogWarnings`
- `scorecard`
- `history`: prior report path plus new, recurring, and resolved failures
- `buildErrors`
- `gates`: status, counts, duration, TRX path, and per-test failures

The Markdown report is for human triage. JSON is the stable input for CI, trend analysis, dashboards, and future tooling.

## Decisions

The decision policy is owned by GateRunner and mirrored in tests:

```text
build errors present       -> NO-GO
any red/error gate         -> NO-GO
score >= 95 and no holds   -> GO
score >= 85                -> HOLD
otherwise                 -> NO-GO
```

Catalog drift warnings do not automatically block a run, but release owners should treat unresolved drift as a triage item before relying on the decision.

## UI

The WPF app provides:

- primary actions for Run Gates, Cancel, Open Report, Settings, and theme switching
- keyboard shortcuts for primary workflows
- gate status filters and per-gate rerun
- score delta from the prior JSON report
- visible settings and catalog warnings
- failure inventory with Markdown copy support

## Documentation

Operational runbooks:

- [Local Run Runbook](DOCS/runbooks/gaterunner-local-run.md)
- [CI Run Runbook](DOCS/runbooks/gaterunner-ci-run.md)
- [Failure Triage Runbook](DOCS/runbooks/gaterunner-triage.md)
- [Report Schema Runbook](DOCS/runbooks/gaterunner-report-schema.md)
- [Deployment Runbook](DOCS/runbooks/gaterunner-deployment.md)

Implementation plan: [PreDeploymentGate-Runner-Plan.md](PreDeploymentGate-Runner-Plan.md)

## Project Layout

```text
SI360.GateRunner.csproj              WPF host
SI360.GateRunner.Core                shared models, runners, report writer, policy
SI360.GateRunner.Cli                 headless automation entry point
SI360.GateRunner.Tests               contract and service tests
Views, ViewModels, Styles            WPF presentation
Scripts                              release automation
.azure-pipelines                     CI validation
DOCS/runbooks                        operational documentation
```
