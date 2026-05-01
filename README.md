# SI360 Pre-Deployment Gate Runner

GateRunner is a WPF and CLI tool that runs the SI360 pre-deployment gate tests, aggregates TRX results, computes the deployment scorecard, and emits GO / HOLD / NO-GO reports.

It is intentionally out-of-tree from `SI360.slnx`: GateRunner reads build output, TRX files, and gate metadata rather than taking a project dependency on SI360 application assemblies.

## Quick Start

GateRunner is pinned to the .NET 8 SDK in `global.json`. Install .NET SDK 8.0.414 or a later .NET 8 feature band compatible with the configured roll-forward policy.

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
Generated build, test, and publish output is intentionally ignored by source control. Keep release-ready files in CI artifacts or the ignored local `artifacts\` directory.

## Settings

GateRunner auto-discovers `SI360.slnx` by probing from the app directory and known development roots. The WPF app also has a Settings dialog for:

- SI360 solution path
- SI360 test project path
- results directory
- restore, build, and gate timeouts
- build configuration, deployment metadata path, probe mode/timeout, retention days, and support bundle path

Settings are saved to `%APPDATA%\SI360.GateRunner\settings.json`.

Environment variables prefixed with `GATERUNNER_` can override configuration for automation, for example `GATERUNNER_BuildConfiguration`, `GATERUNNER_DeploymentMetadataPath`, `GATERUNNER_ProbeMode`, `GATERUNNER_ProbeTimeoutSeconds`, `GATERUNNER_ReportRetentionDays`, and `GATERUNNER_SupportBundleOutputPath`. The CLI also supports:

```powershell
dotnet run --project .\SI360.GateRunner.Cli -- run --solution D:\SI36020WPF\SI360.slnx --test-project D:\SI36020WPF\SI360.Tests\SI360.Tests.csproj --results D:\SI36020WPF\TestResults --configuration Release --probe-mode ReadOnly
```

## Commands

```text
discover                         List discovered pre-deployment gates.
validate-catalog                 Validate catalog expectations against SI360.Tests source.
validate-metadata                Validate installer-supplied deployment metadata JSON.
run-probes                       Run phase-1 read-only synthetic health probes.
run                              Restore, build, run all gates, and emit reports.
summarize [--report <path>]      Print a JSON report.
```

Deployment metadata is owned by installer/deployment tooling and supplied to GateRunner through `DeploymentMetadataPath`, `GATERUNNER_DeploymentMetadataPath`, or `--metadata <path>`. GateRunner validates the versioned contract and records API key presence without requiring secret values.
Synthetic probes are phase-1 read-only checks by default. They use deployment metadata endpoints, bounded timeouts, and redacted diagnostics; any mutating registration workflow must be explicitly added under a non-production mode.

## Report Contract

Each run writes:

- `<ResultsDirectory>\GateRun_<yyyyMMdd_HHmmssZ>.md`
- `<ResultsDirectory>\GateRun_<yyyyMMdd_HHmmssZ>.json`
- per-command and per-gate artifacts under `<ResultsDirectory>\GateRun_<yyyyMMdd_HHmmssZ>\`

The JSON report includes:

- `schemaVersion`: current contract version, currently `2.2`
- `startedAt` in UTC, `durationSeconds`, and environment `LocalUtcOffset`
- `decision`
- `environment`: tool version, machine, OS, runtime, SDK, repo path, branch, commit, artifact directory, command snapshots
- `decisionPolicy`: policy name, version, rationale, and decision-impact issues
- `runtimeReadiness`: runtime readiness decision and rationale
- `healthContracts`: phase-1 contract version labels for SI360, SyncHealthHub, and third-party KDS
- `deploymentMetadata`: installer-supplied metadata plus validation issues
- `syntheticProbes`: read-only probe status, duration, endpoint, contract version, and redacted diagnostics
- `gateCatalogWarnings`
- `qualityIssues` and `gradingImpacts`: issue severity, source location, score impact, and deployment impact
- `scorecard`: scenario/probabilistic score, quality penalty, and final score
- `history`: prior report path plus new, recurring, and resolved failures
- `buildErrors`
- `gates`: status, counts, duration, TRX path, and per-test failures

The Markdown report is for human triage. JSON is the stable input for CI, trend analysis, dashboards, and future tooling.
Known secret patterns are redacted before GateRunner writes live process output, command artifacts, Markdown reports, or JSON reports. Redacted fields keep their structure and replace detected values with `[REDACTED]`.
Report retention defaults to 30 days and prunes old `GateRun_*` reports and per-run artifact directories after each completed report write.

## Decisions

The decision policy is owned by GateRunner and mirrored in tests:

```text
error quality issues       -> NO-GO
warning quality issues     -> HOLD with a 2.00 point penalty per warning
any red/error gate         -> NO-GO
score >= 95 and no holds   -> GO
score >= 85                -> HOLD
otherwise                 -> NO-GO
```

Compiler/MSBuild warnings, catalog drift warnings, and runtime readiness unknown are deployment-grade warnings. Metadata validation errors and failed/error probes are deployment-grade errors. Catalog drift warnings produce a HOLD decision in reports and fail catalog validation in CI until the GateRunner catalog is reconciled with SI360.Tests.

## UI

The WPF app provides:

- primary actions for Run Gates, Cancel, Open Report, Settings, and theme switching
- keyboard shortcuts for primary workflows
- gate status filters and per-gate rerun
- score delta from the prior JSON report
- visible settings and catalog warnings
- quality issue traceability from source location to score and decision impact
- failure inventory with Markdown copy support

## Documentation

Operational runbooks:

- [Local Run Runbook](DOCS/runbooks/gaterunner-local-run.md)
- [CI Run Runbook](DOCS/runbooks/gaterunner-ci-run.md)
- [Failure Triage Runbook](DOCS/runbooks/gaterunner-triage.md)
- [Report Schema Runbook](DOCS/runbooks/gaterunner-report-schema.md)
- [Deployment Runbook](DOCS/runbooks/gaterunner-deployment.md)

Implementation plan: [PreDeploymentGate-Runner-Plan.md](PreDeploymentGate-Runner-Plan.md)

Document ownership:

- `GateRunner.md` is the roadmap and repository-specific requirements tracker.
- `README.md` is the current user guide.
- `DOCS/runbooks/*` are operations procedures.
- Linear issues track implementation execution and should be referenced in `GateRunner.md` when roadmap items ship.

Docs checklist for user-visible changes:

- Update README commands/settings/report contract when behavior changes.
- Update the relevant runbook when operations workflow changes.
- Update `DOCS/runbooks/gaterunner-report-schema.md` for schema additions or compatibility changes.
- Update `GateRunner.md` status and open assumptions for SyncHealthHub roadmap items.

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
