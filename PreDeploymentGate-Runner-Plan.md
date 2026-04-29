# SI360 POS — Local Pre-Deployment Gate Runner Implementation Plan

**Date:** 2026-04-24
**Scope:** Desktop application that runs every Pre-Deployment Gate in `SI360.Tests/PreDeploymentGate/`, aggregates results, computes `SatisfactionScorecard`, and produces a GO / HOLD / NO-GO deployment decision with full failure inventory.
**Out of scope:** Fixing any failing test or gate; only analysis, reporting, scoring, and planning.

---

## 1. Codebase Analysis

### 1.1 Gate Inventory (`SI360.Tests/PreDeploymentGate/`)

| # | File | LOC | `[Fact]` / `[Theory]` | Purpose |
|---|------|----:|----------------------:|---------|
| 1 | `BuildGateTests.cs` | 384 | 17 | Assembly loading, DI registration, interface contracts |
| 2 | `CICDPipelineGateTests.cs` | 1089 | 75 | Pipeline YAML, quality gates, CI stages |
| 3 | `DataIntegrityGateTests.cs` | 395 | 21 | Schema, referential integrity, idempotency |
| 4 | `Phase1FoundationGateTests.cs` | 932 | 48 | DB clone factory, failover surface (`ConnectedToPosDb`, `IsSiteDbReachableAsync`) |
| 5 | `Phase2MonitoringGateTests.cs` | 912 | 50 | Health monitor, telemetry, logging |
| 6 | `Phase3CoverageGateTests.cs` | 896 | 51 | Coverage thresholds, rollback dual-DB support |
| 7 | `Phase4ScaleGateTests.cs` | 807 | 43 | Scale, connection factory cache, async patterns |
| 8 | `PreDeploymentGateTests.cs` | 641 | 1 | **Orchestrator** — runs 5 scenarios + 5 probabilistic; fails when composite < 95 |
| 9 | `ProductionMonitoringGateTests.cs` | 802 | 44 | Runtime alerting + Serilog sinks |
| 10 | `ProductionSafetyGateTests.cs` | 832 | 40 | Payment test mocking, secrets, PCI |
| 11 | `SatisfactionScorecardThresholdTests.cs` | 851 | 49 | Composite formula, grade boundaries, GO/HOLD/NO-GO |
| 12 | `ScenarioWeightGateTests.cs` | 687 | 36 | Scenario weights sum to 100, integrity |
| 13 | `SecurityGateTests.cs` | 480 | 28 | PCI masking, password hashing, SQL safety |
| 14 | `StagedRolloutGateTests.cs` | 861 | 37 | Canary, rollback plan, blue/green |
| 15 | `TestInventoryGateTests.cs` | 883 | 38 | Total test count ≥ threshold, class count thresholds |
| **Total** | | **11,452** | **578** | All share `[Trait("Category","PreDeploymentGate")]` |

### 1.2 Scoring Primitives (`SI360.Tests/Infrastructure/`)

- `SatisfactionScorecard.cs`
  - `ScenarioScore = Σ(passed_scenario_weight)` — weights sum to 100 (see `ScenarioWeightGateTests`).
  - `ProbabilisticScore = avg(ProbabilisticResult.Score())` — each result 0/50/100.
  - `OverallScore = ScenarioScore × 0.6 + ProbabilisticScore × 0.4`.
  - Grade map: `>=95 A+`, `>=90 A`, `>=80 B+`, `>=70 B`, `>=60 C`, else `F`.
  - `SaveToFile(path)` emits markdown dashboard.
- `ProbabilisticResult.cs`
  - Success rate thresholds: green `≥99%` → 100, yellow `≥95%` → 50, else 0.
  - Latency percentiles P50/P95/P99 with `MeetsLatencyThreshold`.
- **Deploy threshold (`PreDeploymentGateTests.FullGate_…`):** `OverallScore >= 95` → A+ → GO.

### 1.3 Inputs to Orchestrator

| Bucket | Source tests | Weight / iterations |
|--------|--------------|--------------------|
| Scenario 1 — Happy Path Order | `Scenarios/HappyPathOrderScenarioTests` (10 `[Fact]`) | 25 |
| Scenario 2 — Error Recovery | `Scenarios/ErrorRecoveryScenarioTests` (4) | 20 |
| Scenario 3 — Security Stress | `Scenarios/SecurityStressScenarioTests` (5) | 20 |
| Scenario 4 — Multi-Device Sync | `Scenarios/MultiDeviceSyncScenarioTests` (4) | 15 |
| Scenario 5 — Edge Cases | `Scenarios/EdgeCaseScenarioTests` (10) | 20 |
| Probabilistic 1 — Success Rate | `Probabilistic/SuccessRateTests` | 100 iters |
| Probabilistic 2 — Error Recovery Rate | `Probabilistic/ErrorRecoveryRateTests` | 50 iters |
| Probabilistic 3 — State Consistency | `Probabilistic/StateConsistencyTests` | 50 iters |
| Probabilistic 4 — Concurrency Safety | `Probabilistic/ConcurrencySafetyTests` | 50 iters |
| Probabilistic 5 — Performance Distribution | `Probabilistic/PerformanceDistributionTests` | 100 iters |

### 1.4 Current failure baseline (pre-plan reference)

`docs/Test-Failures-2026-04-23.md` snapshot — 28 failing tests / 3682 total; none in `PreDeploymentGate/*` test files, but several gate-adjacent failures (e.g. `ProductionSafetyGateTests.AbsoluteRule_PaymentTests_UseMockedServices`, `Phase1FoundationGateTests` DB clone checks) historically broke when source-shape assertions drifted.

---

## 2. Solution Architecture

### 2.1 New project

```
SI360.GateRunner/                    (WPF, net8.0-windows, x86 — matches SI360.UI)
├── SI360.GateRunner.csproj
├── App.xaml / App.xaml.cs           (DI bootstrap)
├── Views/
│   ├── MainWindow.xaml              (gate list + progress + overall score + grade)
│   ├── GateDetailView.xaml          (per-gate test tree + failure details)
│   ├── FailureInventoryView.xaml    (flat filterable failure list)
│   └── ScorecardView.xaml           (ScenarioScore / ProbabilisticScore / Overall)
├── ViewModels/
│   ├── MainViewModel.cs
│   ├── GateRunViewModel.cs
│   ├── TestResultViewModel.cs
│   ├── ScorecardViewModel.cs
│   └── FailureItemViewModel.cs
├── Services/
│   ├── DotnetTestRunner.cs          (invokes `dotnet test`, streams output)
│   ├── TrxResultParser.cs           (reads .trx into TestOutcome records)
│   ├── BuildErrorCollector.cs       (runs `dotnet build`, parses errors/warnings)
│   ├── GateCatalog.cs               (static list of 15 gates + traits)
│   ├── ScorecardAggregator.cs       (builds SatisfactionScorecard from TRX)
│   └── ReportWriter.cs              (markdown + JSON output)
├── Models/
│   ├── GateDefinition.cs            (name, filter, weight, category)
│   ├── TestOutcome.cs               (name, outcome, duration, error, stack, file)
│   ├── GateResult.cs
│   ├── BuildError.cs
│   └── RunSummary.cs
└── Styles/
    └── SharedStyles.xaml            (import SI360.UI SharedStyles — dark theme, 64px buttons)
```

WPF chosen so the runner matches existing SI360 UI conventions (WCAG AAA palette, SharedStyles resources, MVVM Toolkit pattern) and can be shipped alongside the POS installer. net8.0-windows is already on every workstation with the POS.

### 2.2 External dependencies (all already referenced in `SI360.Tests`)

- `dotnet` CLI (shipped with .NET 8 SDK)
- xUnit TRX logger (`--logger "trx;LogFileName=…"`)
- `Microsoft.TestPlatform.ObjectModel` — optional, for richer TRX parsing; fallback = XDocument.

### 2.3 No modifications to test/production code

Runner executes `dotnet test` on existing assemblies; reads TRX and build output only. Zero patches to `SI360.Tests/*` or `SI360.Infrastructure/*`.

---

## 3. Data Flow

```
User presses "Run Gates"
       │
       ▼
DotnetTestRunner.RestoreAsync()           → streams stdout/stderr into log pane
       │
       ▼
BuildErrorCollector.Build()               → dotnet build -c Release --no-restore
       │                                     parses "error CS####" lines → BuildError[]
       │                                     STOP if any build errors (cannot gate a broken tree)
       ▼
For each GateDefinition (sequential or parallel-safe set):
   DotnetTestRunner.RunAsync(filter)      → dotnet test SI360.Tests.csproj
                                             --no-build --nologo
                                             --filter "FullyQualifiedName~<gate class>"
                                             --logger "trx;LogFileName=<gate>.trx"
                                             --results-directory <runDir>
   TrxResultParser.Parse(<gate>.trx)      → GateResult { Passed, Failed, Skipped, Outcomes[] }
       │
       ▼
ScorecardAggregator.Build(results)
  - Map `PreDeploymentGateTests.FullGate_*` output lines (telemetry `[GATE] <name>: PASS/FAIL`)
    → scenario pass/fail by name → weight lookup from GateCatalog.
  - Map probabilistic console output (`[GATE] Success Rate: XX.X% …`) → ProbabilisticResult.Score.
  - Fallback: if orchestrator did not run (e.g. user ran just single gate), aggregator runs
    Scenarios/* and Probabilistic/* suites directly and rebuilds scorecard from pass counts.
       │
       ▼
RunSummary
  { BuildErrors, GateResults[], Scorecard, Decision, ReportPath, DurationMs }
       │
       ▼
ReportWriter.WriteAsync(summary)
  - markdown:  TestResults/GateRun_<timestamp>.md     (SatisfactionScorecard.ToMarkdownReport + failure appendix)
  - json:      TestResults/GateRun_<timestamp>.json   (machine-readable for CI piping)
       │
       ▼
ViewModels bind to RunSummary → UI updates
```

### 3.1 Decision mapping

| Overall score | Grade | Decision | Colour (SharedStyles brush) |
|--------------:|-------|----------|------------------------------|
| ≥ 95 | A+ | **GO** — deploy | `SuccessGreenBrush` |
| 85 – 94.9 | A / B+ | **HOLD** — review failures | `WarningAmberBrush` |
| < 85 | B / C / F | **NO-GO** — block deploy | `DangerRedBrush` |

Build errors short-circuit → **NO-GO** regardless of score.

---

## 4. Implementation Phases

### Phase 1 — Skeleton & Runner Core (1 day)
1. Create `SI360.GateRunner.csproj` (`net8.0-windows`, `UseWPF=true`, x86 to match UI).
2. Reference nothing from `SI360.*` projects — runner is out-of-tree so it survives test-project changes.
3. Implement `DotnetTestRunner` — `Process` wrapper: `dotnet test <csproj> --no-build --filter <expr> --logger trx;LogFileName=<gate>.trx --results-directory <runDir>`. Stream stdout line-by-line to an in-memory ring buffer + `IProgress<string>` for UI pane.
4. Implement `BuildErrorCollector` — `dotnet build <sln> -c Release -p:GenerateFullPaths=true -nologo -clp:ErrorsOnly`. Regex `(?<file>.+?)\((?<line>\d+),\d+\): error (?<code>CS\d+): (?<msg>.+)`.
5. Wire up a plain console harness first to prove the parsing loop before putting chrome on it.

### Phase 2 — TRX Parsing + Gate Catalog (1 day)
6. `TrxResultParser` — `XDocument.Load(path)` → `UnitTestResult` nodes. Extract `testName`, `outcome` (Passed / Failed / NotExecuted), `duration`, `StdOut`, `Output/ErrorInfo/Message`, `Output/ErrorInfo/StackTrace`. Stack-trace regex `in (.+?):line (\d+)` → `TestOutcome.FilePath` + `LineNumber`.
7. `GateCatalog` — hard-coded list of 15 gate definitions (matches §1.1 table). Each entry: `Id`, `DisplayName`, `TestClassFilter`, `ExpectedTestCount`, `CategoryTrait = "PreDeploymentGate"`, `Notes`. Add tooltip text describing what the gate enforces.
8. `GateResult` aggregation per gate; a gate is **GREEN** iff all its tests pass, **YELLOW** iff ≤ 10 % fail, **RED** otherwise. Independent from SatisfactionScorecard (which is scenario+probabilistic only).

### Phase 3 — Scorecard Integration (1 day)
9. Parse `[GATE] …` telemetry lines from `PreDeploymentGateTests.FullGate_…` StdOut into `ScenarioPass[]` and `ProbabilisticResult[]`. Fallback path: run Scenarios/ + Probabilistic/ suites separately if orchestrator did not execute.
10. `ScorecardAggregator` mirrors `SatisfactionScorecard` from the test project — same weights, same 60/40 formula, same grade boundaries. **Do not reuse the type directly** — duplicating the tiny class avoids coupling the runner to `SI360.Tests` internals.
11. Deploy-decision logic: `(buildErrors == 0) && (overallScore >= 95)` ⇒ GO; `overallScore >= 85` ⇒ HOLD; else NO-GO.

### Phase 4 — WPF Shell & ViewModels (2 days)
12. `MainWindow` layout (dark theme, imports `/SI360.UI;component/Styles/SharedStyles.xaml` if co-installed, else embedded copy):
    - Header: **Run Gates** button (64 px high, `PrimaryButtonStyle`), **Cancel**, **Open Report** (disabled until complete).
    - Left: gate list with per-gate spinner + RED/YELLOW/GREEN LED + duration.
    - Right top: live `TextBox` tailing dotnet output (monospace, 16 px).
    - Right bottom: scorecard tile (Overall/Grade/Decision + colour).
13. `GateDetailView` — DataGrid of tests: Name, Outcome, Duration, Error excerpt; double-click jumps to failure panel with full stack.
14. `FailureInventoryView` — flat filterable list across all gates (fields matching the §5 schema).
15. `ScorecardView` — mirrors `SatisfactionScorecard.ToMarkdownReport` as a WPF page with scenarios/probabilistic tables.
16. MVVM Toolkit `[ObservableProperty]` / `[RelayCommand]`, `INotifyPropertyChanged` on progress.

### Phase 5 — Reporting + Export (0.5 day)
17. `ReportWriter` emits `TestResults/GateRun_<yyyyMMdd_HHmmss>.md` (markdown) **and** `.json`. Markdown reuses the `SatisfactionScorecard.ToMarkdownReport` shape for consistency with existing CI artifacts. Appendices: per-gate test counts; full failure list (see §5); build-error list if any.
18. **Open Report** button: launches default `.md` handler via `Process.Start`.

### Phase 6 — Packaging (0.5 day)
19. Single-file publish: `dotnet publish SI360.GateRunner -c Release -r win-x86 --self-contained false -p:PublishSingleFile=true`. Outputs `SI360.GateRunner.exe` portable binary.
20. Optional: add Inno Setup `.iss` entry alongside the existing SI360 installer.

**Total estimate:** ~6 days for 1 engineer.

---

## 5. Failure Inventory Schema

Every failed test — whether in a gate file or a scenario/probabilistic suite — is flattened into a uniform record:

```csharp
public sealed record FailureRecord(
    string GateName,          // e.g. "Phase1FoundationGateTests" or "PreDeploymentGateTests"
    string TestName,          // FullyQualifiedName from TRX
    string FailureType,       // "Assertion", "Moq.MockException", "Build CS####",
                              //  "NullReferenceException", "Timeout", "Gate composite < 95"
    string ErrorMessage,      // first non-whitespace line from ErrorInfo/Message
    string? StackTrace,       // truncated to 40 lines
    string? FilePath,         // from "in X:line Y"
    int?    LineNumber,
    string  ComponentHint     // e.g. "SI360.Infrastructure/Services/GiftCardService.cs"
);
```

Rendered to markdown (reuses the §2 document style from `docs/Test-Failures-2026-04-23.md`):

```markdown
### <n>. `<TestName>`
- **Failure Type:** <FailureType>
- **Error Message:** `<ErrorMessage>`
- **File/Component:** `<FilePath>:<LineNumber>` → `<ComponentHint>`
```

Build errors emitted in a dedicated section:

```markdown
## Build Errors
| File | Line | Code | Message |
|------|-----:|------|---------|
| ... | ... | CSxxxx | ... |
```

---

## 6. UX Considerations

- **Non-blocking UI** — all CLI work on background threads; `Dispatcher.Invoke` only for VM updates. No `async void`.
- **Cancel mid-run** — `CancellationTokenSource` threaded into `DotnetTestRunner`; on cancel, `Process.Kill(entireProcessTree: true)` cleans orphan `dotnet test` and `testhost` children.
- **Progress granularity** — per-gate progress bar + global determinate bar (`CurrentGate / TotalGates`). Live stdout tail for long-running probabilistic suites (the full gate takes minutes).
- **Accessibility** — match SI360 conventions: `AutomationProperties.Name` + `AutomationId` on every control, `KeyboardNavigation.TabNavigation="Cycle"`, min font 16 px, SharedStyles brushes for 7:1 contrast.
- **Safety rails** — never auto-launch anything destructive; "Open Report" prompts before opening external process if path contains spaces; no remote publishing of reports from the runner.
- **Repeatability** — TRX file per gate named `<GateName>_<timestamp>.trx` kept under `TestResults/` for regression comparison. JSON summary is diffable.
- **Error surface** — dotnet CLI missing → friendly dialog with install link (`dotnet --version` probed at startup). Test project build failure → surface `BuildError[]` and offer "Retry Build" before attempting to run gates.
- **Theming** — imports `SharedStyles.xaml` at runtime if co-installed with SI360 UI; fallback to embedded dark theme with same palette keys.
- **First-run** — auto-detects solution root via walking up from `AppContext.BaseDirectory` looking for `SI360.slnx`; user can override via `Settings` panel (path persisted to `%APPDATA%\SI360.GateRunner\settings.json`).

---

## 7. Risks & Mitigations

| Risk | Mitigation |
|------|-----------|
| `dotnet test` changes output format between SDK minors | TRX is the source of truth; stdout scraping used only for scorecard telemetry lines prefixed `[GATE]` |
| Integration gates (`SqlConnectionFactoryOfflineTests`) need real network | Runner honours existing test code as-is; flaky tests tracked in `FailureRecord` and marked "Environment" on the UI. No retries — transparent reporting |
| `SI360.Tests.dll` referenced by another running `testhost` | Runner detects lock, offers kill-stale-testhost button |
| Scorecard drift vs. `SatisfactionScorecard.cs` in test project | Unit test inside runner project that loads the reference impl via reflection and asserts identical output for a fixed fixture; fails loudly when formulas diverge |
| Long gate runtime (minutes) masks hangs | Per-test timeout (default 60 s) surfaced as `FailureType="Timeout"` |

---

## 8. Acceptance Criteria

1. Launch `SI360.GateRunner.exe`, click **Run Gates**, with a clean checkout:
   - Build completes with 0 errors OR failures listed in report.
   - All 15 gate suites execute; TRX written per gate.
   - Scorecard section in markdown matches `SatisfactionScorecard.ToMarkdownReport()` shape.
   - Decision banner is one of GO / HOLD / NO-GO, colour-coded per §3.1.
2. Every failing test appears in the Failure Inventory with the five required fields (§5).
3. No production or test code is modified; `git status` remains clean after a run (only new files under `TestResults/`).
4. Report opens in external viewer via **Open Report** button.
5. Cancel kills all child processes and leaves no orphans.
6. Runs offline (no internet required) once NuGet packages are restored.

---

*Planning artifact only. No gates executed, no failures fixed.*
