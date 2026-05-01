# GateRunner Required Changes

Repository: `D:\GateRunner`

## Role In The Final Architecture

GateRunner remains the deployment validation and synthetic probe tool. It should not be the runtime source of truth for SyncHealthHub. Its expanded role is:

- Provide or validate deployment metadata.
- Run pre-deployment SI360 gates.
- Validate SyncHealthHub/SI360/KDS health contracts after they exist.
- Emit JSON reports that SyncHealthHub or CI can consume.

## Current Strengths

- WPF and CLI entry points exist.
- Core services are separated from UI.
- JSON and Markdown reports exist.
- Decision policy is explicit.
- Gate catalog discovery and validation exist.
- GateRunner tests pass after restore/build: 36 tests passed after the latest modernization pass.
- Catalog validation against current `D:\SI36020WPF` pre-deployment gates exits successfully.

## Documentation Ownership

- `GateRunner.md` is the repository-specific roadmap and requirements tracker for SyncHealthHub alignment.
- `README.md` is the current user guide for local, CLI, reporting, configuration, and support-bundle workflows.
- `DOCS/runbooks/*` are operations procedures for local runs, CI, triage, report schema, and deployment.
- Linear is the execution tracker. When roadmap work ships, update this file with the issue identifier and keep README/runbooks aligned with user-visible behavior.

## Implementation Status By Linear Issue

- TAS-118: Done - SDK pinned with `global.json`; CI uses the pinned SDK.
- TAS-119: Done - `.gitignore` added and generated `.vs`, `bin`, and `obj` artifacts removed from tracking.
- TAS-120: Done - catalog drift now fails validation and produces HOLD decisions.
- TAS-121: Done - command snapshots now come from the same command factory used for execution.
- TAS-122: Done - deployment metadata contract and validator added.
- TAS-123: Done - phase-1 read-only synthetic probes added.
- TAS-124: Done - schema `2.1` includes metadata, probes, health contracts, and runtime readiness.
- TAS-125: Done - centralized redaction protects process artifacts, reports, logs, and support bundles.
- TAS-126: Done - UTC report naming/timestamps, local offset, and retention pruning added.
- TAS-127: Done - WPF metadata/probe views and redacted support bundle export added.
- TAS-128: In Progress - documentation synchronization.
- TAS-129: Done - CLI/settings/environment configuration surface expanded.

## P0 Required Changes

1. Add repo build reproducibility.

   The repo currently resolves to a .NET 10 preview SDK on this machine.

   Required work:

   - Add `global.json` aligned with CI.
   - Decide whether GateRunner should use .NET 8 SDK or move CI and docs to the intended SDK.
   - Ensure `dotnet test .\SI360.GateRunner.sln --no-restore` works after CI restore.

2. Remove generated artifacts from source.

   `git ls-files bin obj .vs` reports 354 tracked files.

   Required work:

   - Remove `.vs`, `bin`, and `obj` from source control.
   - Add `.gitignore` rules.
   - Keep publish artifacts under `artifacts/` or CI artifact storage only.

3. Define deployment metadata schema.

   `SiteID` is finalized as installer/deployment-tooling supplied.

   GateRunner or installer tooling should emit/validate:

   - Site id
   - Site SQL connection reference, not secret
   - SI360 SignalR Hub URL
   - SI360 SignalR API key presence, not value
   - SyncHealthHub endpoint
   - Third-party KDS endpoint
   - Terminal ids
   - Tablet ids
   - KDS station/display ids where known
   - Environment name
   - Deployment version

4. Make catalog drift enforceable.

   Current CI runs `validate-catalog` with `continueOnError: true`.

   Required work:

   - Fail CI or produce HOLD for unresolved gate catalog drift.
   - Keep warnings in reports, but do not silently accept drift for release decisions.

## P1 Runtime Contract Validation

After SI360 and SyncHealthHub expose health APIs, add GateRunner probes for:

- SI360 Hub reachable.
- SI360 health summary available.
- Tablet registry accepts/registers a synthetic client or test client.
- Event ledger endpoint available.
- Kitchen outbox endpoint available.
- SyncHealthHub ingestion endpoint available.
- Third-party KDS health endpoint reachable.
- Third-party KDS rendered-confirmation capability available.
- Alert provider configuration present.

These probes should be synthetic and read-only unless explicitly running in a non-production test mode.

## P1 Report Schema Additions

Extend GateRunner JSON reports with:

- Deployment metadata block.
- SyncHealthHub health contract version.
- SI360 health API version.
- Third-party KDS contract version.
- Synthetic probe results.
- Site id.
- Hub endpoints.
- Redacted auth/config validation results.
- Runtime readiness decision separate from pre-deployment gate score.

Keep schema versioning explicit. Current schema version is `2.1`.

## P1 Deterministic Commands

Current implementation mismatch:

- `RunEnvironmentCollector` records solution restore and build with `--no-restore`.
- `BuildErrorCollector` restores the test project and then runs solution build without `--no-restore`.

Required work:

- Make actual commands match reported command snapshots.
- Restore the solution once, then build with `--no-restore`.
- Include configuration and SDK information in report.
- Preserve per-command artifacts.

## P2 Security And Secret Redaction

`ProcessRunner` writes command lines, stdout, stderr, and exit files. This is valuable, but future health probes may include tokens.

Required work:

- Redact API keys, tokens, passwords, and connection strings in command artifacts.
- Avoid logging secret values.
- Record presence/validity instead of values.

## P2 Time And History

Required work:

- Use UTC timestamps in reports.
- Include local offset when useful for support.
- Keep report retention aligned with SyncHealthHub 30-day support window.

## P2 UX And Operations

Required work:

- Add a deployment metadata validation view.
- Add probe results view.
- Add "export support bundle" that includes reports but redacts secrets.
- Make SyncHealthHub phase 1 status explicit: read-only monitoring.

## Definition Of Done

GateRunner is ready for the SyncHealthHub program when:

- Build artifacts are no longer tracked.
- SDK is pinned and CI matches local behavior.
- It emits or validates deployment metadata including `SiteID`.
- Catalog drift blocks or holds release decisions.
- Reports include deployment metadata and synthetic probe results.
- It validates SI360/SyncHealthHub/third-party KDS health contracts without becoming the runtime source of truth.
