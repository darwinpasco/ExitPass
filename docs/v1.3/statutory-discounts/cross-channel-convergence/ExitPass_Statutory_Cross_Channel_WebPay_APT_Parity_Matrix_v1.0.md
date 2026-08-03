# ExitPass Statutory Cross-Channel WebPay versus APT Parity Matrix v1.0

| Concern | WebPay | APT desktop | Classification | Reason | Remediation |
|---|---|---|---|---|---|
| Parking-session lookup modes | Ticket, plate, and session rediscovery through Central PMS | Session re-resolution and terminal context through Central PMS | CONVERGED | Both rely on Central PMS | Keep shared resolver tests |
| Site display | Server-resolved Site | Server-resolved Site | CONVERGED | Site remains operational scope | None |
| Site Group handling | Scope fact and authorization input | Scope fact and authorization input | CONVERGED | Site Group is not ordinance authority | None |
| Ordinance availability | Central PMS gate/recovery | Central PMS resolve/revalidate | CONVERGED | No local policy engine | Keep mappings aligned |
| Senior Citizen | `SENIOR_CITIZEN` | `SENIOR_CITIZEN` | CONVERGED | Same enum | None |
| PWD | `PWD` | `PWD` | CONVERGED | Same enum | None |
| Pending review | Rediscovery and ordinary payment continuation | Pending state and ordinary payment continuation | PARTIALLY_CONVERGED | WebPay proof is explicit; APT pending restart proof is less centralized | Add APT pending rediscovery proof if missing |
| Approval readback | Existing decision readback | Existing decision/application state readback | CONVERGED | Central PMS owns approval | None |
| Payable-basis application | Payment-time WebPay service call | Pre-cash APT service call | CONVERGED | Both service channels apply through Central PMS | Keep Operator Console out |
| Retryable failures | Safe rediscovery classifications | Safe readiness classifications | PARTIALLY_CONVERGED | Names differ by channel | Publish support mapping |
| Restart recovery | Browser storage non-authority | SQLite non-authority | CONVERGED | Server state wins | None |
| Stale-state clearing | Rediscovery replaces stale lifecycle | Revalidate clears stale readiness | PARTIALLY_CONVERGED | APT needs more automated proof | Add restart tests |
| Ordinary-payment preservation | Preserved | Preserved unless independent cash/fiscal failure | CONVERGED | Statutory path does not block regular payment | None |
| Pre-payment/pre-cash revalidation | Apply immediately before payment | Revalidate ordinance and payable basis before cash | INTENTIONALLY_DIFFERENT | Card/payment and cash custody differ | Keep channel runbooks |
| Duplicate prevention | Decision/application idempotency and rediscovery | Central PMS idempotency plus APT local cash guards | PARTIALLY_CONVERGED | APT spans desktop and server | Cross-process duplicate tests |
| Fiscal readiness | Payment channel must supply POS facts | Statutory cash guarded pending POS facts proof | PARTIALLY_CONVERGED | POS boundary supports facts; APT enablement guarded | Complete APT-to-POS proof |
| Receipt/Sales Invoice | POS readback/render | POS fiscal output/readback | PARTIALLY_CONVERGED | Channel payload linkage needs proof | Controlled UAT fiscal proof |
| Reconciliation identifiers | Decision/application/payment/fiscal refs | Decision/application/tender/fiscal refs | CONVERGED | Tender/payment refs are channel-specific | None |
| Security headers | Service identity server-side | Desktop does not construct service auth | CONVERGED | UI is not authority | None |
| Direct HikCentral access | Prohibited | Prohibited | CONVERGED | APT docs explicitly forbid it | Keep scans |

## Summary
WebPay and APT share the same statutory authority model. Remaining parity gaps are proof and wiring gaps around terminal-cash fiscal linkage, APT restart/readiness proof, and consistent customer-safe classification labels.
