# GateRunner Failure Triage Runbook

## Purpose

Use this runbook when GateRunner returns HOLD or NO-GO.

## Decision Interpretation

- GO: gates and scorecard meet release policy.
- HOLD: release may proceed only with explicit owner approval. Warnings, runtime readiness unknown, and catalog drift are HOLD conditions until reconciled.
- NO-GO: deployment is blocked until failures are fixed or policy is formally overridden.

## Triage Steps

1. Open the Markdown report and review the decision rationale.
2. Check quality issues first. Build errors, metadata errors, and failed probes produce NO-GO; warnings produce strict score penalties and HOLD.
3. Review run history:

   - new failures indicate fresh regressions
   - recurring failures indicate unresolved known issues
   - resolved failures confirm improvement since the prior report

4. Review catalog warnings. Drift means GateRunner metadata may no longer match SI360 test reality.
5. Review the quality issue table for issue -> score impact -> deployment impact traceability.
6. For each failed test, inspect:

   - gate name
   - failure type
   - error message
   - file and line
   - stack trace in the JSON or TRX artifact

7. Assign fixes to the owning SI360 component team.

## Catalog Drift Handling

Catalog drift usually means a test class was renamed, moved, added, or deleted. GateRunner treats unresolved drift as HOLD in reports and as a failed catalog validation step in CI. The release engineering owner should update GateRunner metadata and contract tests before relying on trend comparisons.

## Override Guidance

Overrides should include:

- report JSON path
- failing gates
- reason for override
- owner approval
- follow-up issue link

Overrides should not change GateRunner source unless the decision policy itself needs an approved update.
