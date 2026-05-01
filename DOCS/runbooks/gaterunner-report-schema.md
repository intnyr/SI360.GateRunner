# GateRunner Report Schema Runbook

## Purpose

This document describes the intended consumers and compatibility expectations for GateRunner JSON reports.

## Consumers

- release pipeline artifact publishing
- trend dashboards
- failure triage tooling
- release approval records

## Compatibility

The JSON report includes `schemaVersion`. Additive fields are allowed in minor updates. Removing or renaming existing fields requires a schema version bump and test updates.

## Required Fields

- `schemaVersion`
- `startedAt`
- `durationSeconds`
- `decision`
- `environment`
- `decisionPolicy`
- `runtimeReadiness`
- `healthContracts`
- `deploymentMetadata`
- `syntheticProbes`
- `qualityIssues`
- `gradingImpacts`
- `scorecard`
- `history`
- `buildErrors`
- `gates`

## History Semantics

History compares the current run to the most recent prior `GateRun_*.json` in the same results directory.

- `newFailures`: present now, absent in prior run
- `recurringFailures`: present now and in prior run
- `resolvedFailures`: absent now, present in prior run

The comparison fingerprint is gate name, test name, file path, and line number.

## Timestamp And Retention Semantics

Report `startedAt` values and `GateRun_<yyyyMMdd_HHmmssZ>` filenames use UTC. The environment block also includes `LocalUtcOffset` for support correlation.

GateRunner prunes old `GateRun_*` Markdown reports, JSON reports, and per-run artifact directories using the configured retention window. The default is 30 days.

## Runtime Readiness Semantics

`runtimeReadiness` summarizes deployment metadata plus synthetic probe readiness and is also folded into the pre-deployment `decision` through the quality issue ledger.

- `Ready`: deployment metadata is valid and configured probes passed or were skipped.
- `NotReady`: deployment metadata validation failed or one or more probes failed/errored. This is an error quality issue and produces NO-GO.
- `Unknown`: metadata was not configured or probes are disabled. This is a warning quality issue and produces HOLD unless a future release mode explicitly allows it.

`deploymentMetadata` contains installer/deployment-tooling supplied values and validation issues. Secret values are not required and should not be present; API key state is represented as presence/validity only.

`syntheticProbes` contains read-only phase-1 probe results with endpoint, status, duration, contract version, and redacted diagnostics.

## Quality Issue And Grading Semantics

Schema `2.2` adds a deployment-integrity ledger:

- `qualityIssues`: every warning or error that affects GateRunner grading.
- `gradingImpacts`: the same issue set shaped for consumers that need issue-to-score-to-decision traceability.
- `decisionPolicy.impacts`: the subset of quality issues that directly caused the final GO/HOLD/NO-GO decision.

Each quality issue includes severity, source, source location, code, message, numeric score impact, decision impact, and optional artifact path.

Errors fail hard and produce NO-GO. Warnings apply a deterministic quality penalty of 2.00 points each and produce HOLD until resolved or formally approved. The scorecard includes `baseOverallScore`, `qualityPenalty`, and final `overallScore`.

Build output must capture warnings and errors. Compiler/MSBuild warnings are not informational-only; they are quality issues with source locations and grading impact.

## Redaction Semantics

GateRunner redacts known secret patterns before writing process logs, command artifacts, Markdown reports, and JSON reports.

Redacted values are replaced with `[REDACTED]`. The report schema keeps the same field names and object structure so downstream consumers can continue parsing reports while avoiding accidental credential disclosure.

Covered patterns include bearer tokens, API key or token key-value pairs, sensitive query string values, and connection string password fields.

## Ownership

GateRunner maintainers own schema changes, report contract tests, and schema documentation. CI and dashboard owners should treat unknown fields as optional extensions.
