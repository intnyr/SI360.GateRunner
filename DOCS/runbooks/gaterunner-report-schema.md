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

## Ownership

GateRunner maintainers own schema changes, report contract tests, and schema documentation. CI and dashboard owners should treat unknown fields as optional extensions.
