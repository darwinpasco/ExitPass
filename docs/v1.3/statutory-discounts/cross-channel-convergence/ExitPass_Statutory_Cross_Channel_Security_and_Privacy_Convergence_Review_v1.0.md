# ExitPass Statutory Cross-Channel Security and Privacy Convergence Review v1.0

## Security Findings
| Area | Current posture | Status | Gap or risk |
|---|---|---|---|
| Service authentication | Central PMS WebPay rediscovery and APT endpoints require service identity; UI does not construct authority | PARTIALLY_CONVERGED | Runtime service-identity persistence and administration remain incomplete. |
| Human reviewer authentication | Operator Console review endpoints require human policy and service principals are denied where implemented | PARTIALLY_CONVERGED | Broad compatibility permissions such as `reconciliation.manage` should be reviewed. |
| Site and Site Group scope | Central PMS enforces server-side scope on WebPay, APT, Operator Console, and Management Platform routes | PARTIALLY_CONVERGED | Durable Site/Site Group grant persistence is not fully implemented. |
| Browser header boundary | WebPay and Management Platform treat browser state/scope as non-authoritative | CONVERGED | Continue to avoid browser-provided permissions. |
| Desktop header boundary | APT docs state desktop does not create service-auth headers and local SQLite is advisory | CONVERGED | Needs continued runtime proof after packaging. |
| Direct database access | Channels use APIs | CONVERGED | None found in audited statutory source. |
| Direct HikCentral access | APT statutory docs prohibit direct HikCentral access | CONVERGED | Keep validation scan. |
| Raw exception exposure | Central PMS statutory APIs use safe error envelopes | CONVERGED | Keep leak scans. |
| Evidence leakage | POS rejects sensitive evidence markers; evidence bytes are not implemented in channel runtime | PARTIALLY_CONVERGED | Secure evidence runtime absent. |
| Customer ID leakage | Operator Console compact UI and POS privacy checks hide raw IDs/evidence | PARTIALLY_CONVERGED | Future preview must add cache and access controls. |
| Local/browser persistence | WebPay browser recovery and APT SQLite are non-authoritative | CONVERGED_BY_CONTRACT | Runtime evidence upload not yet available. |
| Logs and support refs | Correlation IDs and support refs are safe identifiers | PARTIALLY_CONVERGED | Cross-channel support-reference naming is not fully normalized. |
| Secrets and connection strings | No configs modified by audit | CONVERGED | Continue repository secret scans. |
| POS Server privacy boundary | POS accepts final applied facts and rejects raw statutory evidence/reviewer data | CONVERGED | End-to-end channel payload proof remains required. |
| Management Platform mutation authority | Policy coverage workspace is read-only | CONVERGED | Policy administration remains not implemented. |

## Privacy Boundary
- Evidence bytes, ID images, signed URLs, raw IDs, reviewer notes, and object-storage locators must not enter POS Server, browser storage, APT SQLite, payment payloads, or fiscal payloads.
- POS Server statutory fiscal facts are reference-only and monetary/fiscal only.
- Management Platform may display policy and coverage metadata to authorized administrative users; normal customer/reviewer channels must not display policy internals unnecessarily.
- Paranaque Senior Citizen coverage remains verified operational coverage with unavailable online source text, not unverified or no-rule.

## Required Security Remediation
1. Remove or hard-deny the legacy Operator Console payable-basis application route from human reviewer authority.
2. Complete durable RBAC persistence and service identity lifecycle before production RBAC administration.
3. Implement secure evidence runtime before any channel accepts protected evidence images.
4. Review POS `SourcePaymentChannel` allowance for `OPERATOR_CONSOLE`.
5. Add cross-channel leak scans for customer-facing and fiscal payloads before controlled UAT.
