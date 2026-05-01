# Real-Time Sync Health Dashboard - Consolidated Assessment

| Field | Value |
| --- | --- |
| Status | **Final - v1.0** |
| Prepared | 2026-05-01 |
| Audience | SI360 engineering, KDS engineering, SyncHealthHub team, Product/Ops leadership |
| Supersedes | n/a (first consolidated edition) |
| Related | `Real-Time-Sync-Health-Dashboard-Findings.md`, `Production-Testing-Strategy.md` |

Scope reviewed:

- Findings document: `D:\SyncHealthHub\Real-Time-Sync-Health-Dashboard-Findings.md`
- SI360 application: `D:\SI36020WPF`
- KDS application and hub service: `D:\KDS`

Decision basis: 12-question architectural clarification session (2026-05-01) — see [Finalized Architectural And Technical Decisions](#finalized-architectural-and-technical-decisions).

## Executive Summary

The existing findings are confirmed by the SI360 and KDS source code. The current implementation provides best-effort runtime communication, not a monitorable synchronization system. SI360 tablet-to-tablet updates, SI360 offline replay, SI360-to-KDS sends, and KDS display updates are separate flows with no shared event identity, no end-to-end acknowledgement model, no deterministic drift detection, and no durable runtime health contract that SyncHealthHub can consume.

SyncHealthHub should not be built as a log scraper or an inferred-state dashboard. The production-ready path is to add first-class telemetry to SI360 and KDS first, then let SyncHealthHub consume those runtime contracts. GateRunner should remain a deployment validation and synthetic probe consumer; it should not be the first place where runtime sync truth is invented.

The highest risk items are:

1. SI360's checked-in SignalR configuration is internally inconsistent: the UI points to `https://localhost:9000/posHub`, the hub listens on plain Kestrel `ListenAnyIP`, the hub rejects empty API keys, and the UI config does not provide a `SignalR:ApiKey`.
2. SI360 can mark itself `ONLINE` even after `PosSignalRClient.StartAsync` fails, because the client swallows connection failure and the UI unconditionally sets online state.
3. Neither SI360 nor KDS has a connected-device registry with last-seen heartbeat, identity, app version, station, site, terminal, or connection state.
4. No sync event has a durable event id, sequence number, correlation id, expected recipient list, received ack, applied ack, replay state, or drift digest.
5. KDS Hub Service accepts POS HTTP traffic and SignalR display traffic without authentication in the inspected code, uses `AllowAnyOrigin`, and a tracked KDS config file contains `sa` SQL credentials.
6. SI360 solution-level verification is currently unreliable: `dotnet test .\SI360.slnx --no-restore` fails because the solution graph excludes `SIPrintingLibrary` while `SI360.Infrastructure` references it. The direct infrastructure project build succeeds, which points to solution/build graph drift rather than a simple compile issue.

## System Model From Source

### SI360 Tablet Sync

Current flow:

1. A SI360 tablet performs a sale, sale item, discount, tender, customer, or configuration operation.
2. The business path updates the local or site database.
3. Some operations call `IDbChangeNotifier`, implemented by `SignalRDbChangeNotifier`.
4. `SignalRDbChangeNotifier` calls `PosSignalRClient.SendDbChangeAsync`.
5. `PosHub.DbChanged` broadcasts to `site-{SiteId}` if a site id is present, otherwise to all clients.
6. Receiving tablets run `OrderingViewModel.OnDbChangedAsync`, invalidate local caches, and refresh relevant order/open-check views.

Relevant source:

- `D:\SI36020WPF\SI360.SignalRHub\Hubs\PosHub.cs:18` connects clients and records only a static count.
- `D:\SI36020WPF\SI360.SignalRHub\Hubs\PosHub.cs:82` accepts `DbChanged` messages.
- `D:\SI36020WPF\SI360.SignalRHub\Hubs\PosHub.cs:103` sends by `site-{SiteId}` group.
- `D:\SI36020WPF\SI360.SignalRHub\Client\PosSignalRClient.cs:61` starts the client connection.
- `D:\SI36020WPF\SI360.SignalRHub\Client\PosSignalRClient.cs:90` configures reconnect delays.
- `D:\SI36020WPF\SI360.UI\ViewModels\OrderingViewModel.cs:356` starts SignalR from the ordering UI.

### SI360 Offline Queue

Current flow:

1. `SiteDbHealthMonitorService` pings the site database every five seconds.
2. `OfflineSyncFacade` starts the DB health monitor and conditionally starts the offline operation queue.
3. Offline queue replay is enabled only when `Development:EnableOfflineQueue` is true.
4. `OfflineReplayService` replays selected tables using merge/upsert behavior and emits `DbChangeEvent` after replay.

The checked-in SI360 UI config sets `Development:EnableOfflineQueue` to false.

### SI360 to KDS

Current flow:

1. SI360 order send logic enters `SendOrderService`.
2. If QSR/KDS is enabled, the app calls `QsrService`.
3. `QsrService` posts to `Development:QSREndpointURL`.
4. In production, SI360 posts to the third-party KDS Hub. The reviewed `D:\KDS` KDS Hub Service and Local API paths are treated as legacy/development references unless explicitly reintroduced.
5. The KDS Hub creates or forwards ticket DTOs and sends them to kitchen displays/stations.
6. KDS display clients receive SignalR ticket events and update their views.

This is a separate flow from SI360 `DbChanged`. A SyncHealthHub design that monitors only SI360 SignalR will miss the kitchen-critical path.

### KDS Display Sync

Current flow:

1. KDS display clients connect to `KitchenHub`.
2. Displays call `RegisterStation(int stationId)` and join either `Station{stationId}` or `AllStations`.
3. Tickets live in `TicketStore`, with optional persistence.
4. Display actions such as bump, recall, and status update go through `KitchenHub`.
5. Persistence writes are fire-and-forget in several places.

Relevant source:

- `D:\KDS\SI-KDS.Hub.Service\Hubs\KitchenHub.cs:51` logs connection.
- `D:\KDS\SI-KDS.Hub.Service\Hubs\KitchenHub.cs:68` registers a station group.
- `D:\KDS\SI-KDS.Hub.Service\Hubs\KitchenHub.cs:455` exposes a simple `Ping`.
- `D:\KDS\SI-KDS.Hub.Service\Hubs\KitchenHub.cs:463` exposes recipe cache statistics.
- `D:\KDS\SI-KDS.Hub.Service\Controllers\PosController.cs:39` accepts posted checks from POS.
- `D:\KDS\SI-KDS.Hub.Service\Program.cs:53` allows any CORS origin.
- `D:\KDS\SI-KDS.Hub.Service\Program.cs:67` maps `/kitchenhub`.

## Validated Findings From The Original Document

| Finding | Status | Evidence and expansion |
| --- | --- | --- |
| No reliable connected tablet list | Confirmed | `PosHub` keeps only a static connection count. It does not retain connection id, tablet id, terminal, site, app version, IP, last seen, reconnect count, or current group membership. |
| Lifecycle logging only | Confirmed | SI360 and KDS both log connect/disconnect events, but those logs are not exposed as a durable runtime API for SyncHealthHub. |
| Tablet identity gap | Confirmed | `PosHub` reads `tabletId` from query, but `PosSignalRClient.StartAsync` builds the URL with `siteId` and `terminal` only. |
| Reconnect state invisible to monitoring | Confirmed | `PosSignalRClient` uses automatic reconnect, but there is no dashboard-facing reconnect count, outage duration, or last failure reason. |
| Disconnected sends can be lost silently | Confirmed | `PosSignalRClient.SendDbChangeAsync` only sends when connected. The disconnected branch does not create a durable pending event or dashboard-visible failure. |
| No event id or sequence number | Confirmed | `DbChangeEvent` has table/action/key/sale/site/terminal fields, but no durable event id, sequence, correlation id, or schema version. |
| No delivery or apply acknowledgement | Confirmed | `DbChanged` is fire-and-forget fan-out. Receivers refresh UI state but do not ack receipt or successful application. |
| Event payloads are incomplete | Confirmed and expanded | Several direct `SendDbChangeAsync` calls in manager code omit sale id, key, or complete origin details. Some receivers ignore events that do not match current sale context. |
| Drift detection is absent | Confirmed | SI360 has count polling for active sale item count, but no deterministic sale/open-check digest. KDS has no digest for ticket state across hub and displays. |
| Recovery is mostly reconnect and refresh | Confirmed | SI360 reconnects and refreshes open checks/current order; KDS failover resynchronizes tickets from the active hub. Neither system has sequence-gap replay or operator-directed recovery commands. |
| KDS is not represented as a sync participant | Confirmed and expanded | KDS has station registration, but no durable display registry, heartbeat, display ack, ticket rendered ack, print ack, or station health API. |
| GateRunner should not be the first runtime monitor | Confirmed | GateRunner documentation describes deployment validation and dashboard output. Runtime sync truth must originate inside SI360 and KDS services before GateRunner or SyncHealthHub can display it reliably. |

## Newly Discovered Findings

### P0 - Must Fix Before Production Health Claims

1. SI360 SignalR defaults are not self-consistent.

   The SI360 UI config points to `https://localhost:9000/posHub`, while the hub code uses Kestrel `ListenAnyIP(port)` with no TLS setup in code. The hub appsettings has an empty `SignalR:ApiKey`, and `PosHub` rejects missing or mismatched keys. The UI appsettings does not include `SignalR:ApiKey`. In a default checkout, this path cannot be considered operational.

   Evidence:

   - `D:\SI36020WPF\SI360.UI\appsettings.json:89`
   - `D:\SI36020WPF\SI360.SignalRHub\Program.cs:12`
   - `D:\SI36020WPF\SI360.SignalRHub\appsettings.json:11`

2. SI360 can report a false online state.

   `PosSignalRClient.StartAsync` catches connection failure, sets `_isOffline = true`, logs a warning, and returns. `OrderingViewModel.InitializeSignalRAsync` then unconditionally sets `ConnectionStatus = "ONLINE"` and `IsOnline = true`.

   Evidence:

   - `D:\SI36020WPF\SI360.SignalRHub\Client\PosSignalRClient.cs:61`
   - `D:\SI36020WPF\SI360.SignalRHub\Client\PosSignalRClient.cs:124`
   - `D:\SI36020WPF\SI360.UI\ViewModels\OrderingViewModel.cs:372`
   - `D:\SI36020WPF\SI360.UI\ViewModels\OrderingViewModel.cs:373`

3. SI360 solution-level build/test is not trustworthy yet.

   `dotnet test .\SI360.slnx --no-restore --nologo --verbosity minimal` fails because `SI360.Infrastructure` references `SIPrintingLibrary` and KDS DTOs that are not available in the solution build graph. A direct `dotnet build .\SI360.Infrastructure\SI360.Infrastructure.csproj --no-restore` succeeds, which indicates solution graph drift. The machine also used SDK `10.0.300-preview.0.26177.108` because `D:\SI36020WPF` has no `global.json`.

4. KDS transport is not secured in inspected code.

   KDS Hub Service allows any CORS origin and exposes POS HTTP controllers plus SignalR hub endpoints without visible authentication or API key validation. A tracked `SI-KDS\appsettings-SQR.json` contains `User Id=sa;Password=sa12345`.

   Evidence:

   - `D:\KDS\SI-KDS.Hub.Service\Program.cs:53`
   - `D:\KDS\SI-KDS.Hub.Service\Controllers\PosController.cs:39`
   - `D:\KDS\SI-KDS\appsettings-SQR.json:18`
   - `D:\KDS\SI-KDS\appsettings-SQR.json:19`

5. KDS display health is not observable enough for SyncHealthHub.

   `KitchenHub` knows when a connection joins a station group, but it does not maintain a station/display registry with last heartbeat, current ticket count, app version, station id, last ticket rendered, last bump command, or last error. This prevents SyncHealthHub from answering "which kitchen screens are alive and caught up?"

6. SI360-to-KDS is a separate sync plane.

   The kitchen path is not derived from SI360 `DbChanged`; it is an HTTP POST path into KDS Hub Service. SyncHealthHub must monitor SI360 tablet sync and kitchen ticket sync separately, then correlate them through a shared order/check/sale correlation id.

### P1 - High Priority Reliability And Observability Gaps

7. KDS Hub Service and KDS Local API have different observability behavior.

   The older/local API path logs POS API requests into the order repository. The Hub Service POS controller does not appear to write equivalent durable API request records. Production monitoring will see different evidence depending on which mode is deployed.

8. KDS connection abstractions are inconsistent.

   `ConnectionServiceFactory` creates a placeholder `SignalRConnectionService` in SignalR mode, while `MainWindow` uses `HubConnectionManager` for the real dual-hub connection. This can lead to conflicting startup status and makes health reporting harder to reason about.

   Evidence:

   - `D:\KDS\SI-KDS\Services\Core\ConnectionServiceFactory.cs:21`
   - `D:\KDS\SI-KDS\Services\Core\ConnectionServiceFactory.cs:131`
   - `D:\KDS\SI-KDS\Services\Core\ConnectionServiceFactory.cs:148`
   - `D:\KDS\SI-KDS\MainWindow.xaml.cs:53`

9. KDS persistence is not part of the acknowledgement model.

   Ticket state can update in memory while persistence runs fire-and-forget. If persistence fails, displays may look correct temporarily while restart recovery loses state. SyncHealthHub needs to know whether a ticket is accepted, broadcast, displayed, and durably persisted.

10. SI360 offline queue is optional and narrow.

   The offline queue is disabled in checked-in UI configuration and replay covers a limited set of tables. It is not connected to KDS event delivery state or SyncHealthHub status contracts.

11. Current tests do not prove runtime health behavior.

   SI360 has useful SignalR contract and production gate tests, but many are simulated and do not exercise the full runtime telemetry model needed here. The KDS solution builds but includes no test projects in the solution. `HubTests` exists as a console-style project outside the main solution.

### P2 - Medium Priority Maintainability And Operations Gaps

12. Time handling is inconsistent.

   KDS uses `DateTime.Now` in failover and ticket state paths. Cross-machine monitoring should use UTC timestamps and record local offset separately if needed.

13. SI360 drift polling is too narrow.

   Active sale polling checks sale item count only. It will not detect quantity, modifier, discount, void, seat, tender, status, or kitchen-send drift.

14. Configuration is spread and environment-dependent.

   Important values such as site id, terminal, hub URL, KDS endpoint, offline queue enablement, QSR enablement, and API keys live across appsettings, user secrets, comments, and external deployment configuration. SyncHealthHub needs a deployable source of truth for topology.

## Architecture Assessment

### Architecture

The architecture currently has four separate runtime planes:

1. SI360 tablet-to-tablet SignalR updates.
2. SI360 site database health and offline replay.
3. SI360-to-third-party-KDS order sends.
4. Third-party KDS hub-to-display updates.

These planes are valid as implementation boundaries, but they do not share a common monitoring contract. SyncHealthHub must treat them as separate subsystems and correlate them with a shared `CorrelationId`, `SaleId`, `CheckNumber`, `SiteId`, `TerminalId`, KDS `StationId`, and KDS display identifier where the third-party KDS integration exposes one.

### Data Flow

The data flow is mostly event notification plus local refresh. That works for a forgiving UI refresh model, but it is not enough for production health monitoring. A monitor must know:

- What happened.
- Where it originated.
- Which recipients were expected.
- Which recipients received it.
- Which recipients applied it.
- Whether downstream KDS displays rendered it.
- Whether the final state matches the source of truth.

None of those answers can be produced reliably by the current contracts.

### Synchronization Logic

SI360 synchronization is best-effort fan-out. KDS synchronization is in-memory ticket state plus SignalR broadcast. Offline replay exists, but it does not participate in a larger event ledger. Reconnect behavior refreshes state but does not prove missed event recovery.

### Error Handling

The code often favors business continuity by logging and continuing when SignalR or persistence fails. That is a reasonable availability choice for POS operations, but it creates a monitoring blind spot. Failures must be converted into explicit health facts, not just logs.

### Performance

Current refresh and polling behavior is acceptable at small scale but unmeasured:

- SI360 active order count polling runs every three seconds.
- SI360 event handling can trigger broader order/open-check refreshes.
- KDS keeps active tickets in memory and broadcasts to groups.
- KDS recipe/cache statistics exist, but ticket throughput, queue depth, broadcast latency, display render latency, and persistence latency are not exposed.

Before production, define expected site size, max tablets, max displays, peak orders per minute, and acceptable event-to-render latency.

### Maintainability

The main maintainability risks are weak runtime contracts, duplicated connection abstractions, scattered configuration, and tests that do not exercise the intended production health model. The system will become easier to complete once health contracts are implemented as first-class APIs instead of inferred from UI state and logs.

## Prioritized Implementation Recommendations

### P0 - Establish A Reliable Baseline

1. Finalize runtime topology and checked-in config conventions.

   Decide the production scheme, host, ports, TLS behavior, SI360 hub URL, KDS hub URL, KDS POS endpoint URL, site id source, terminal id source, and API key/secrets source. Make local development config work without hidden assumptions, while keeping production secrets out of source.

2. Fix SI360 connection state correctness.

   Change `PosSignalRClient.StartAsync` to return a connection result or throw on initial connection failure. Update `OrderingViewModel.InitializeSignalRAsync` so it sets online only after a successful connection and exposes offline/error states when the hub is unavailable.

3. Add device identity to SI360 SignalR.

   Add `TabletId`, `DeviceName`, `AppVersion`, `User/Operator` where appropriate, `SiteId`, `TerminalId`, and connection start timestamp to the client handshake. Register them in `PosHub`.

4. Secure KDS and SI360 runtime endpoints.

   Add authentication/authorization for POS-to-KDS HTTP, KDS display SignalR, and SI360 SignalR. Remove tracked SQL credentials, use user secrets or deployment secrets, restrict CORS, and document local development exceptions.

5. Repair the build/test baseline.

   Add or align `global.json` for SI360, fix `SI360.slnx` so referenced projects build consistently, address the vulnerable package warning for `System.Security.Cryptography.Xml`, and add KDS test projects to the solution or document the intended verification entry point.

### P1 - Add Runtime Health Contracts

6. Create shared health DTOs/contracts.

   Define versioned contracts for devices, connections, heartbeats, sync events, delivery acknowledgements, drift snapshots, KDS tickets, alerts, and recovery commands. Keep them independent enough for SyncHealthHub, SI360, KDS, and GateRunner to consume.

7. Add connection registries.

   Add a registry to `PosHub` for SI360 tablets and a registry to `KitchenHub` for KDS displays. Required fields:

   - Connection id
   - Device id
   - Site id
   - Terminal id or station id
   - App version
   - Machine name
   - IP/transport info where available
   - Connected at
   - Last heartbeat
   - Reconnect count
   - Last error
   - Current state

8. Add heartbeat messages.

   SI360 tablets and KDS displays should send periodic heartbeat payloads with local queue depth, last applied event id, last sale/check touched, local DB/site DB state, and app version. KDS displays should include active ticket count and last rendered ticket id.

9. Expose health APIs.

   SI360 SignalR Hub Service and KDS Hub Service should expose dashboard APIs such as:

   - `GET /api/sync-health/summary`
   - `GET /api/sync-health/tablets`
   - `GET /api/sync-health/kds-displays`
   - `GET /api/sync-health/events/recent`
   - `GET /api/sync-health/offline-queue`
   - `GET /api/sync-health/site-db`
   - `GET /api/sync-health/alerts`

### P2 - Add Event Ledger And Acknowledgements

10. Extend `DbChangeEvent`.

   Add:

   - `EventId`
   - `Sequence`
   - `CorrelationId`
   - `SchemaVersion`
   - `OriginDeviceId`
   - `OriginTerminalId`
   - `OriginSiteId`
   - `CreatedUtc`
   - `Causation`
   - `EntityType`
   - `EntityId`
   - `ExpectedRecipients`

11. Add SI360 receive/apply acknowledgements.

   Receivers should ack when an event is received and when the local refresh/apply has completed. Failed apply should include error, affected entity, and last known local state.

12. Add KDS delivery acknowledgements.

   Track POS send accepted, KDS hub accepted, display received when available, display visibly rendered, and later bump/completion actions separately. For phase 1 health, KDS "applied" means visibly rendered on the kitchen display.

13. Track expected recipients and timeout alerts.

   For each event, calculate expected SI360 tablets or KDS stations/displays. Raise warnings when expected recipients do not receive/apply/render within configured thresholds.

### P3 - Add Drift Detection And Recovery

14. Add deterministic digests.

   Implement digests for:

   - Open checks per site
   - Active sale detail per sale id
   - KDS active tickets per station
   - KDS ticket detail per check/course/station

   Use canonical ordering and UTC timestamps or version fields so digests are stable.

15. Add sequence gap detection and replay.

   Clients should report last applied sequence. Hubs should detect gaps and provide replay where feasible. Where replay is not possible, trigger a full state refresh and record the event as a recovery.

16. Prepare operator recovery commands for later phases.

   SyncHealthHub phase 1 is read-only monitoring. Later phases can support controlled recovery actions:

   - Force tablet refresh
   - Rejoin SignalR groups
   - Replay missed SI360 events
   - Re-push KDS ticket
   - Resync KDS station
   - Clear stale connection

   These commands need authentication, authorization, audit logging, and safe no-op behavior before they are enabled.

### P4 - Build SyncHealthHub On Top Of The Contracts

17. Use APIs first, not logs.

   SyncHealthHub should consume SI360 and KDS health APIs and optionally durable event tables. Logs can support diagnosis, but they should not be the source of truth.

18. Product views to implement.

   Recommended first production views:

   - Site summary
   - SI360 tablets
   - KDS displays/stations
   - Recent sync events
   - Stuck/missed acknowledgements
   - Drift snapshots
   - Offline queue and replay
   - KDS ticket path
   - Alerts
   - Future recovery actions, shown disabled or omitted in phase 1

19. Define alert thresholds and runbooks.

   Example thresholds:

   - Tablet no heartbeat for 15 seconds: warning.
   - Tablet no heartbeat for 60 seconds: offline.
   - SI360 event not applied by expected tablet in 10 seconds: warning.
   - KDS ticket not visibly rendered in 5 seconds: critical.
   - Offline queue pending count increasing for 2 minutes: warning.
   - Drift digest mismatch across two consecutive checks: critical.

### P5 - Expand Tests And GateRunner Integration

20. Add integration tests for real runtime contracts.

   Add tests for:

   - SI360 hub device registration and heartbeat.
   - SI360 event ack success/failure.
   - SI360 sequence gap and replay behavior.
   - KDS POS post to ticket broadcast to display ack.
   - KDS station offline and recovery.
   - Auth failures on SI360 and KDS endpoints.
   - SyncHealthHub API consumption against test servers.

21. Use GateRunner as a consumer after contracts exist.

   GateRunner can run predeployment checks and synthetic runtime probes, but the source systems must expose the health facts first. GateRunner should validate those APIs rather than infer internal state.

## Finalized Architectural And Technical Decisions

The following decisions are finalized from the 2026-05-01 clarification session and are inputs for SyncHealthHub phase 1 implementation. Each row maps to a specific question in that session.

| # | Topic | Decision | Implementation Impact |
| --- | --- | --- | --- |
| 1 | Production topology | One site SQL server, one SI360 SignalR Hub, one third-party KDS Hub, N tablets, M kitchen displays per site. SI360 Hub and KDS Hub are separate services/boxes. | SyncHealthHub must monitor two distinct hubs and correlate via shared ids. No assumption of co-located services. |
| 2 | KDS production path | Production uses third-party KDS Hub only. Local API and prior KDS Hub Service paths are legacy/dev references. | Retire Local API instrumentation work. SyncHealthHub treats KDS Hub as opaque external dependency with limited visibility. |
| 3 | SI360 `SiteID` source | Provided by installer/deployment tooling (e.g. GateRunner) and stored in user secrets. | Treat deployment metadata as source of truth. Do not hardcode in `appsettings.json`. SyncHealthHub reads `SiteID` from same secret store. |
| 4 | Monitoring scope (v1) | Local-site first. Multi-store aggregation is future work. | v1 hub topology = per-site. Keep `SiteId` mandatory on every signal so a future central tier can aggregate via `/api/sync-health/*`. |
| 5 | Kitchen latency target | Event-to-display under 5 seconds (warning); 10 seconds critical. | Drives KDS applied-ack threshold and pilot SLA validation. |
| 6 | KDS "applied" definition | Ticket visibly rendered on kitchen display. Accepted, received, rendered, bumped, completed tracked as separate lifecycle states. | Applied ack returns only after render. Bump/complete tracked elsewhere. |
| 7 | Behavior when SignalR/KDS down | Continue sales (DB authoritative). Block kitchen-critical sends when delivery cannot be confirmed. | Asymmetric fail policy. UI must surface degraded-mode banner. Disconnected sends counted, not dropped. |
| 8 | Missed kitchen send retry | SI360 owns a durable kitchen-send outbox. Retry until confirmed rendered. | New `KdsOutbox` table + sender loop. Reuses offline-queue pattern. KDS Hub not trusted for retry guarantees. |
| 9 | Alert channels | Dashboard + local visual warnings on tablet/KDS + external notifications (email/SMS/Teams or similar). | Three-channel design. Dashboard ships first (Phase 6). External webhooks Phase 7. Local warnings Phase 5. |
| 10 | Phase 1 recovery scope | Read-only monitoring only. Recovery commands deferred to Phase 5. | No mutating endpoints in v1. Heartbeat ingestion allowed. Recovery actions need stronger auth + audit. |
| 11 | Historical retention | 30 days for sync/health event history (matches existing error log retention). | Hot retention 30d for ledger, acks, outbox failures, connection transitions. Heartbeats 7d. Drift incidents 90d. |
| 12 | Dashboard identifiers / privacy | Technical identifiers only: site, terminal, tablet, station, event id, check/order id. No customer names, no payment refs. | PII stays in audit log. Dashboard deep-links to audit for authorized roles. Truncate IPs to /24. |

## Remaining Architectural And Technical Decisions To Finalize

Grouped by ownership. **Internal** items can be resolved by SI360 + KDS + SyncHealthHub teams without external dependency. **External** items need third-party KDS vendor input or business-stakeholder sign-off.

### Internal — Engineering Decision Required Before Phase 1 Code

| # | Topic | Open Question | Recommendation |
| --- | --- | --- | --- |
| R1 | Telemetry ownership | SI360/KDS DB, SyncHealthHub DB, or both? | Source systems own live truth + durable event records; SyncHealthHub stores dashboard history + derived alerts. |
| R2 | SyncHealthHub hosting model | WPF app, web app, Windows service + UI, or hybrid? | Service/API + web UI. Monitors multiple processes; later needs external alerting + recovery — UI-only too narrow. |
| R3 | Authentication model | API keys, mTLS, Windows auth, or shared-secret? | API keys for v1 (matches existing PosHub pattern). Plan mTLS migration after v2. Same scheme covers SI360, KDS reads, SyncHealthHub. |
| R4 | Device identity and provisioning | Tablet/display ids: manual, first-run generated, or central provisioning? | First-run generated GUID persisted to local config. Site id from deployment tooling. Manual override available. |
| R5 | Event sequence scope | Per site, per terminal, per entity stream, or global per hub? | Per-site monotonic sequence + per-terminal sub-sequence for replay precision. Avoid global per hub (collision risk in multi-hub future). |
| R6 | Drift source of truth | Canonical records during online/offline/recovery? | Online: site DB. Offline: local DB + offline queue head. Recovery: site DB after replay completes successfully. |
| R7 | Time synchronization | NTP requirement, clock-skew detection? | Require NTP / domain time sync at install. Dashboard flags >2s skew between any tablet and hub. UTC in all telemetry. |
| R8 | Retention storage cleanup | Cleanup mechanism (cron job, SQL Agent, Hangfire)? | SQL Agent job nightly. Soft-delete with 7-day grace. Cold archive Phase 2+. |
| R9 | Operator identifier needs | Any privileged support flow that requires server/manager id in dashboard? | None for v1. Audit log drill-down covers privileged investigations. Revisit after v1 user research. |

### External — Awaiting Third-Party / Stakeholder Input

| # | Topic | Open Question | Owner |
| --- | --- | --- | --- |
| R10 | KDS Hub health API surface | What ack/render confirmation does third-party KDS Hub expose per display? | KDS vendor — request API spec + sample payloads |
| R11 | KDS Hub authentication | Auth scheme accepted by KDS Hub for SI360 sends and SyncHealthHub reads? | KDS vendor — request supported auth modes |
| R12 | KDS Hub display identity | Per-display id, station assignment, heartbeat exposed externally? | KDS vendor — confirm before designing dashboard rows |
| R13 | Outbox storage location | Site SQL DB, local terminal DB, or both? | SI360 architecture — depends on R1 telemetry-ownership decision |
| R14 | Deployment metadata schema | Exact `SiteID`, hub URLs, terminal/tablet ids, KDS endpoints schema from GateRunner / installer | DevOps + GateRunner team — required before secret schema lock |
| R15 | First external alert channel | Teams, Slack, email, or SMS for v1? | Operations leadership — depends on existing on-call tooling |
| R16 | KDS-down behavior beyond kitchen sends | Block only new sends, or also warn for edits/voids/re-fires? | Product + Ops — UX decision, not a code blocker |
| R17 | Production capacity limits per site | Max tablets, displays, stations, checks/hour, peak sends/min? | Product — needed for load test baselines, not v1 functionality |

## Assumptions Used In This Assessment

1. SI360 site database state is the canonical state during normal online operation.
2. Local/offline database state is a degraded-mode source that must reconcile back to site state.
3. KDS displays are production-critical sync participants, not passive screens.
4. SyncHealthHub is the phase 2 post-deployment monitoring app used alongside SI360.
5. GateRunner remains deployment-focused and may later consume the same health APIs for synthetic checks.
6. Production KDS behavior depends on third-party KDS Hub capabilities; where those capabilities are missing, SI360 must record durable send/outbox state and SyncHealthHub must show that the downstream confirmation is unavailable or degraded.

## Verification Notes

Commands run:

- `dotnet test .\SI-KDS.sln --no-restore --nologo --verbosity normal` in `D:\KDS`
- `dotnet test .\SI360.slnx --no-restore --nologo --verbosity minimal` in `D:\SI36020WPF`
- `dotnet build .\SI360.Infrastructure\SI360.Infrastructure.csproj --no-restore --nologo --verbosity minimal` in `D:\SI36020WPF`

Results:

- KDS solution build succeeded with 0 warnings and 0 errors, but the solution contains no test projects.
- SI360 solution test/build failed under `SI360.slnx` because of missing `SIPrintingLibrary` and `KdsDTO` references in the solution graph.
- Direct build of `SI360.Infrastructure.csproj` succeeded with 791 warnings, confirming that at least part of the failure is solution graph drift.
- The SI360 restore/build output includes a high-severity vulnerability warning for `System.Security.Cryptography.Xml` 10.0.1.

## Recommended Completion Path

The fastest safe path to production-ready SyncHealthHub is:

1. Fix configuration, security, and build baseline.
2. Add SI360 and KDS runtime registries plus heartbeats.
3. Add SI360 event ids, sequences, acknowledgements, and durable kitchen-send outbox.
4. Add drift digests and read-only health APIs; design recovery APIs for later phases but keep phase 1 read-only.
5. Build SyncHealthHub against those APIs.
6. Expand GateRunner and automated tests to validate the runtime contracts before deployment.

This order keeps SyncHealthHub honest: it displays real health facts emitted by the systems doing the work, instead of trying to infer production truth from partial logs, UI status, or database side effects.

## Phase Mapping (Decision → Phase)

| Decision | Phase 0 | Phase 1 | Phase 2 | Phase 3 | Phase 4 | Phase 5 | Phase 6 | Phase 7 |
| --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Topology + SiteID source (#1, #3) | x | | | | | | | |
| KDS path + outbox (#2, #8) | | | x | | | | | |
| Local-site scope (#4) | | x | | | | | | |
| Latency target + applied definition (#5, #6) | | | x | x | | | | |
| Degraded mode + fail-safe (#7) | x | | | | | x | | |
| Read-only Phase 1 (#10) | | x | x | x | x | | | |
| Retention (#11) | | x | | | | | | |
| Identifiers + privacy (#12) | | x | | | | | x | |
| Alert channels (#9) | | | | | | x | x | x |

## Sign-Off

This document is approved as Final v1.0 once the following sign-offs are recorded.

| Role | Owner | Sign-off | Date |
| --- | --- | --- | --- |
| SI360 engineering lead | _ | ☐ | |
| KDS engineering lead | _ | ☐ | |
| SyncHealthHub engineering lead | _ | ☐ | |
| DevOps / Deployment lead | _ | ☐ | |
| Product owner | _ | ☐ | |
| Security / PCI reviewer | _ | ☐ | |
| Operations leadership | _ | ☐ | |

Changes after sign-off require a numbered amendment block at the bottom of this document, dated, and re-circulated for approval.

## Change History

| Version | Date | Author | Notes |
| --- | --- | --- | --- |
| Draft | 2026-05-01 | SyncHealth review team | Initial consolidated assessment from findings + KDS code review |
| Final v1.0 | 2026-05-01 | SyncHealth review team | Incorporated 12 finalized decisions from clarification session; restructured Remaining items by ownership; added phase mapping and sign-off block |
