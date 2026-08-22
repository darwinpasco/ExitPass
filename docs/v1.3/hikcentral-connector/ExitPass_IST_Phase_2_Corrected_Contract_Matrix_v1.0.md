# Corrected IST Phase 2 HikCentral Contract Matrix

Status: Prepared, not executed

The prior Phase 2 assumptions `Central PMS -> Session Service`, `Central PMS -> Gate Integration`, and `Gate Integration -> HikCentralFake` are invalid for the approved v1.3 topology. Their failures do not establish product defects in those nonexistent runtime contracts.

| Caller | Callee | Contract | Required evidence |
| --- | --- | --- | --- |
| Central PMS | Site Adapter A | Provider-neutral `/v1/vendor/*`, authenticated Site A scope | Site A route, adapter identity, correlation, controlled errors |
| Central PMS | Site Adapter B | Provider-neutral `/v1/vendor/*`, authenticated Site B scope | Site B route, adapter identity, correlation, controlled errors |
| Site Adapter A | HikCentral mock A | Signed passageway, fee calculation, conditional fee confirmation | Mock A journal only; AppKey A; lot A |
| Site Adapter B | HikCentral mock B | Signed passageway, fee calculation, conditional fee confirmation | Mock B journal only; AppKey B; lot B |
| Central PMS scheduler | Site Adapter A and B | One-minute scoped passageway synchronization | Atomic projection, locks, freshness, source adapter identity |
| WebPay resolution | Central PMS | Site Group-scoped exact identifier | Unique active Site projection, no uncontrolled fan-out |
| Central PMS payment workflow | Same Site Adapter | Immutable tariff and acknowledgment route | No Site/vendor/adapter switch; idempotent retry |
| Services | Audit/Event | Existing authenticated audit/event contract | Correlation, Site, actor/service attribution |

Required negative probes: anonymous adapter access, wrong service identity, wrong Site, wrong Site Group, wrong Vendor System, wrong adapter identity, missing mapping, duplicate active mapping, disabled/expired binding, duplicate cross-Site identifier, stale projection, non-zero HikCentral code, malformed envelope, signature failure, timeout, and confirmation replay.

No corrected matrix row calls Session Service, Gate Integration, a physical gate, or HikCentral directly from Central PMS. The task-owned HikCentral mocks must sit behind real Site Adapter instances. Executing this matrix remains a separately authorized IST activity.
