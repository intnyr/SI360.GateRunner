# SI360 Pre-Deployment Gate Runner

Desktop WPF app that runs every Pre-Deployment Gate in `SI360.Tests/PreDeploymentGate/`, aggregates results, computes the `SatisfactionScorecard`, and produces a **GO / HOLD / NO-GO** deployment decision with full failure inventory.

Out-of-tree from `SI360.slnx` — survives test-project changes, ships independently.

![Decision banner](https://img.shields.io/badge/decision-GO%20%7C%20HOLD%20%7C%20NO--GO-blue) ![Framework](https://img.shields.io/badge/.NET-8.0--windows-512BD4) ![Platform](https://img.shields.io/badge/platform-x86%20%7C%20WPF-lightgrey)

---

## What it does

1. **Restore + Build** the SI360 solution (`dotnet build -c Release`). Build errors short-circuit → NO-GO.
2. **Runs all 15 gate suites** in `SI360.Tests/PreDeploymentGate/` via `dotnet test` per gate, TRX logger.
3. **Parses TRX** + scrapes orchestrator `[GATE]` telemetry lines for scenario/probabilistic results.
4. **Builds Scorecard** mirroring `SatisfactionScorecard.cs` formula:
   - `OverallScore = ScenarioScore × 0.6 + ProbabilisticScore × 0.4`
   - Grade: `≥95 A+`, `≥90 A`, `≥80 B+`, `≥70 B`, `≥60 C`, else `F`
5. **Decision**:
   | Score | Grade | Decision |
   |------:|-------|----------|
   | ≥ 95 | A+ | **GO** |
   | 85 – 94.9 | A / B+ | **HOLD** |
   | < 85 | B / C / F | **NO-GO** |

   Build errors → **NO-GO** regardless of score.
6. **Emits markdown + JSON report** to `<solution>/TestResults/GateRun_<timestamp>.{md,json}`.

---

## Quick start

```powershell
# Run pre-built portable exe
D:\SI360.GateRunner\bin\Release\net8.0-windows\win-x86\publish\SI360.GateRunner.exe
```

Or build from source:

```powershell
dotnet build SI360.GateRunner.csproj -c Release
dotnet publish SI360.GateRunner.csproj -c Release -r win-x86 --self-contained false -p:PublishSingleFile=true
```

Press **Run Gates** (or `F5`). First run: full restore + build + 15 suites = several minutes (probabilistic suites run 100/50/50/50/100 iterations). Subsequent runs use `--no-build`.

### Settings

Auto-detects `SI360.slnx` by walking up from `AppContext.BaseDirectory` and probing common roots (`D:\SI36020WPF`, `D:\SI360`).

Override at `%APPDATA%\SI360.GateRunner\settings.json`:

```json
{
  "SolutionPath": "D:\\SI36020WPF\\SI360.slnx",
  "TestProjectPath": "D:\\SI36020WPF\\SI360.Tests\\SI360.Tests.csproj",
  "ResultsDirectory": "D:\\SI36020WPF\\TestResults",
  "PerTestTimeoutSeconds": 60
}
```

---

## UI features

| Feature | Detail |
|---------|--------|
| **Run Gates / Cancel / Open Report** | 64 px primary action cluster, grouped border, scale-on-press |
| **Gate list** | 15 gates with LED (pending/running/green/yellow/red), per-gate spinner, double-click to re-run only that gate |
| **Filter chips** | 2×2 toggleable: Red / Yellow / Green / Pending — counts auto-update, checkmark when active |
| **Tabs** | Gate Detail · Failures · Scorecard · Build Errors |
| **Live `dotnet` output** | Tail with auto-scroll, ring buffer 512 KB |
| **Score delta** | Shows ▲/▼ vs last `GateRun_*.json` (green when up, red when down) |
| **ETA** | Per-gate average × remaining, refreshed every second while running |
| **Theme toggle** | Dark ↔ Light, both AAA-tuned palettes (`Ctrl+T`) |
| **Failure inventory** | Sortable severity rank, right-click → copy as Markdown (single or all) |
| **Toast on complete** | `NotifyIcon` balloon + `SystemSounds.Asterisk` |

### Keyboard shortcuts

| Key | Action |
|-----|--------|
| `F5` | Run Gates |
| `Esc` | Cancel |
| `Ctrl+R` | Open last report |
| `Ctrl+T` | Toggle theme |
| `Ctrl+1..4` | Switch tab (Detail/Failures/Scorecard/Build Errors) |
| `Ctrl+Shift+R/Y/G/P` | Toggle Red/Yellow/Green/Pending filter chip |
| `Alt+R / Alt+C / Alt+O` | Run / Cancel / Open Report (access keys) |
| Double-click gate | Re-run only that gate |
| Right-click failure row | Copy as Markdown |

### Themes

Both themes meet **WCAG AAA** (≥7:1 for normal text):

- **Dark**: Background `#0E1116`, Primary `#0A4BAD` (white text 7.78:1)
- **Light**: Background `#F3F5F8`, Primary `#052472` (white text 14.6:1)

Mac-style scrollbars: 10 px thin, rounded thumb, 0.5 idle opacity, fade on hover.

---

## Project layout

```
SI360.GateRunner/
├── SI360.GateRunner.csproj         net8.0-windows · x86 · UseWPF + UseWindowsForms (NotifyIcon)
├── App.xaml / App.xaml.cs          DI bootstrap, dotnet CLI probe, theme load
├── Models/
│   ├── BuildError.cs
│   ├── TestOutcome.cs              TestStatus + outcome record
│   ├── GateDefinition.cs           Id, filter, expected count, category, notes
│   ├── GateResult.cs               Aggregate state (Green/Yellow/Red)
│   └── RunSummary.cs               Scorecard + DeployDecision + scenario/probabilistic entries
├── Services/
│   ├── DotnetTestRunner.cs         Process wrapper, TRX logger, cancel = kill tree
│   ├── BuildErrorCollector.cs      [GeneratedRegex] for `error CS####`
│   ├── TrxResultParser.cs          XDocument + stack-trace regex `in X:line Y`
│   ├── GateCatalog.cs              Static list of 15 gates
│   ├── ScorecardAggregator.cs      [GATE] telemetry parser, 60/40 formula, decision logic
│   ├── ReportWriter.cs             Markdown (§5 schema) + JSON
│   ├── PreviousRunLoader.cs        Latest GateRun_*.json for delta
│   ├── ThemeManager.cs             Live MergedDictionaries[0] swap
│   ├── ToastNotifier.cs            NotifyIcon balloon + sound
│   ├── AttachedBehaviors.cs        TextBoxAutoScroll + ListBoxDoubleClickCommand
│   ├── Converters.cs               Positive/Negative double for delta color
│   └── RunnerSettings.cs           %APPDATA% persistence + slnx discovery
├── ViewModels/                     CommunityToolkit.Mvvm [ObservableProperty]/[RelayCommand]
│   ├── MainViewModel.cs            Orchestrates run, chips, ETA, delta, toast
│   ├── GateRunViewModel.cs         Per-gate state + tests collection
│   ├── TestResultViewModel.cs
│   ├── ScorecardViewModel.cs
│   └── FailureItemViewModel.cs     SeverityRank classifier + ToMarkdown()
├── Views/
│   ├── MainWindow.xaml             Header buttons, gate list, tabs, footer
│   ├── GateDetailView.xaml         Per-gate test grid with ellipsis + tooltips
│   ├── FailureInventoryView.xaml   Sorted failures + ContextMenu
│   └── ScorecardView.xaml          Score tiles + decision badge
├── Styles/
│   ├── DarkTheme.xaml              Brushes only
│   ├── LightTheme.xaml             Brushes only
│   └── SharedStyles.xaml           Buttons, chips, scrollbars, focus visual
└── Resources/
    ├── build-icon.ps1              Multi-size .ico generator (System.Drawing)
    └── gaterunner.ico              16/24/32/48/64/128/256 px
```

---

## Output

Per run, `<ResultsDirectory>/GateRun_<yyyyMMdd_HHmmss>/` holds per-gate `.trx` files plus `<ResultsDirectory>/GateRun_<ts>.{md,json}` summary at root.

### Markdown (extract)

```markdown
# SI360 Pre-Deployment Gate Run — 2026-04-24 02:00:00

**Decision:** **GO**
**Overall Score:** 100.00 (A+)
**Scenario:** 100.0 / 100   **Probabilistic:** 100.0 / 100

## Gates
| # | Gate | Status | Passed | Failed | Skipped | Duration |
| 1 | Build Gate | Green | 17 | 0 | 0 | 8.6s |
…

## Failure Inventory (1)
### 1. `ProductionSafetyGateTests.AbsoluteRule_PaymentTests_UseMockedServices`
- **Gate:** ProductionSafetyGate
- **Failure Type:** Assertion
- **Error Message:** `Expected hasMock to be true …`
- **File/Component:** `…/ProductionSafetyGateTests.cs:248` → `SI360.Infrastructure/.../GiftCardService.cs`
```

### JSON (machine-readable)

CI-friendly, includes per-test failures with file/line, scorecard breakdown, all gate timings.

---

## How decisions map

```
buildErrors > 0  →  NO-GO
score ≥ 95       →  GO       (SuccessGreenBrush)
score ≥ 85       →  HOLD     (WarningAmberBrush)
otherwise        →  NO-GO    (DangerRedBrush)
```

Scorecard inputs (matches plan §1.3):

| Bucket | Source tests | Weight / iters |
|--------|--------------|----------------|
| Scenario 1 — Happy Path Order | `Scenarios/HappyPathOrderScenarioTests` | 25 |
| Scenario 2 — Error Recovery | `Scenarios/ErrorRecoveryScenarioTests` | 20 |
| Scenario 3 — Security Stress | `Scenarios/SecurityStressScenarioTests` | 20 |
| Scenario 4 — Multi-Device Sync | `Scenarios/MultiDeviceSyncScenarioTests` | 15 |
| Scenario 5 — Edge Cases | `Scenarios/EdgeCaseScenarioTests` | 20 |
| Probabilistic 1 — Success Rate | 100 iterations |
| Probabilistic 2 — Error Recovery Rate | 50 |
| Probabilistic 3 — State Consistency | 50 |
| Probabilistic 4 — Concurrency Safety | 50 |
| Probabilistic 5 — Performance Distribution | 100 |

Probabilistic score per suite: `≥99% → 100`, `≥95% → 50`, else `0`. Final = average.

---

## Architecture notes

- **No reference to `SI360.*` projects.** Runner reads TRX/build output only — survives gate refactors.
- **Scorecard duplicates** the tiny `SatisfactionScorecard.cs` formula in [Services/ScorecardAggregator.cs](Services/ScorecardAggregator.cs) instead of project-referencing the test assembly. Drift caught by visual diff against `[GATE]` telemetry lines.
- **All UI brush refs use `DynamicResource`** so theme swap is live (no restart).
- **Cancel** propagates `CancellationToken` to `Process.Kill(entireProcessTree: true)` — kills `dotnet test` + `testhost` children, no orphans.
- **Single-file publish**, framework-dependent (.NET 8 desktop runtime required on host).

---

## Regenerating the icon

```powershell
powershell -ExecutionPolicy Bypass -File D:\SI360.GateRunner\Resources\build-icon.ps1
```

Edit colors / triangle dimensions inside the script, rerun, rebuild. Output: 7-size PNG-encoded `.ico` (~18 KB).

---

## Acceptance (per plan §8)

- [x] Launch exe, click **Run Gates**, all 15 gates execute, TRX per gate
- [x] Markdown report matches `SatisfactionScorecard.ToMarkdownReport()` shape
- [x] Decision banner GO / HOLD / NO-GO color-coded
- [x] Every failing test in Failure Inventory with `GateName / TestName / FailureType / ErrorMessage / FilePath:Line / ComponentHint`
- [x] No production or test code modified — `git status` clean (only new files under `TestResults/`)
- [x] Open Report launches external `.md` viewer
- [x] Cancel kills child processes, no orphans
- [x] Runs offline once NuGet restored

---

## Plan reference

Implementation plan: [PreDeploymentGate-Runner-Plan.md](PreDeploymentGate-Runner-Plan.md)
