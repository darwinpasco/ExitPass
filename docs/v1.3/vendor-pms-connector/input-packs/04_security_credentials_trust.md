# Vendor PMS Connector Security, Credentials, and Trust Input Pack

## 1. Purpose

This input pack provides architecture-level security, credential, service identity, trust-boundary, and audit posture for the future Vendor PMS Connector System Design and HikCentral Connector Profile.

It does not finalize mTLS topology, certificate model, vault product, environment variable names, OAuth scopes, exact HikCentral authentication headers, signature implementation, API contracts, database schema, deployment scripts, or implementation classes.

## 2. Source Documents Reviewed

- `docs/v1.3/vendor-pms-connector/ExitPass_Vendor_PMS_Connector_Orchestration_Plan.md`
- `docs/v1.3/ExitPass_System_Design_v1.3.md`
- `docs/v1.3/ExitPass_BRD_v1.3.md`
- `docs/v1.3/continuity/ExitPass_Continuity_BRD_v1.0.md`
- `docs/v1.3/operator-console/ExitPass_Operator_Console_BRD_v1.1.md`
- `docs/v1.3/management-dashboard-reporting/ExitPass_Management_Dashboard_and_Reporting_BRD_v1.0.md`

Optional source status:

- `docs/v1.3/vendor-pms-connector/input-packs/02_hikcentral_api_discovery.md` was not present at review time.
- The local HikCentral vendor guide was not used to finalize API header, signing, or credential implementation details in this pack; those details remain deferred to the HikCentral Connector Profile and later engineering/API work.

## 3. Trust Boundary Overview

The Vendor PMS connector is a dedicated integration boundary between ExitPass-controlled services and a configured external Vendor PMS/HikCentral instance. It must preserve these separations:

- Central PMS remains the platform control authority for payment-linked state, TariffSnapshot recording, platform payment finality, fiscal reference recording, degraded resolve decision under approved policy, and ExitAuthorization.
- Vendor PMS/HCP remains authority for raw parking session lifecycle and normal tariff computation.
- The connector authenticates to Vendor PMS/HCP and normalizes vendor-specific behavior behind the connector boundary.
- Central PMS to connector trust is separate from connector to HikCentral authentication. A valid platform service identity must not be reused as a vendor credential, and a valid vendor credential must not imply platform authority.
- VendorSystem, AdapterMapping, adapter codebase, connector instance, and vendor object references must remain distinct.
- HCP ParkingLotIndexCode remains a vendor-side identity and must not be treated as ExitPass `site_id`.

The connector must fail closed when service identity, vendor authentication, mapping, freshness, or authority context is uncertain.

## 4. Connector Service Identity

Each deployed connector instance should have a distinct service identity aligned to its configured VendorSystem and deployment scope. That identity should be authorized only for the Central PMS and platform workflows that require connector interaction, health reporting, projection ingestion, vendor resolve, and vendor acknowledgment.

Architecture-level requirements:

- Service identity must be authenticated and authorized independently from vendor credentials.
- Connector instance identity must support lifecycle tracking, revocation, and access audit.
- Service identity permissions must be least privilege by service, environment, VendorSystem, Site/Site Group scope where applicable, and action type.
- Connector service identity must not confer payment finality, fiscal issuance, ExitAuthorization, gate operation, discount approval, continuity activation, or reconciliation closure authority.
- Exact service identity implementation, including certificates, tokens, service mesh identity, workload identity, or other mechanism, remains open.

## 5. Vendor Credential Boundary

Vendor credentials must stay inside the connector boundary. Central PMS, Operator Console, Management Dashboard, WebPay, Assisted Payment Terminal, POS Server, and gate/exit systems should not directly store, display, log, export, or use Vendor PMS/HikCentral credentials.

Required posture:

- Vendor credentials are connector-owned integration secrets, not business configuration values.
- Vendor credentials must not appear in repository files, documentation examples, prompts, logs, screenshots, test fixtures, committed configuration, dashboard exports, support notes, or disposable test logs.
- Vendor credentials must not be returned through user-facing APIs or operational dashboards.
- Error handling must normalize and redact vendor errors before exposing them to user-facing channels, Operator Console, Management Dashboard, support views, or logs.
- Vendor credential access must be least privilege where the vendor supports scoped credentials, tenant/application separation, or integration-role restrictions.
- Production credentials must be segregated from test/UAT credentials.

## 6. HikCentral AppKey / AppSecret / AK-SK Handling Posture

HikCentral AppKey/AppSecret or AK/SK-style credentials must be treated as high-sensitivity vendor integration secrets.

Architecture-level requirements:

- AppKey/AppSecret or AK/SK values are environment or secret-store values, not source-controlled configuration.
- The connector should retrieve and use these credentials only within the connector runtime boundary.
- The connector should sign or authenticate HikCentral requests using the approved HikCentral mechanism once finalized.
- No real AppKey, AppSecret, AK, SK, derived signature material, nonce, timestamp signature output, or reusable sample secret may be included in repository files, docs, tests, screenshots, prompts, logs, or committed config.
- Credential material and derived authentication artifacts must be redacted from application logs, request traces, exception messages, screenshots, and observability payloads.
- Where HikCentral supports least-privilege application permissions or application-level scoping, the credential should be constrained to required parking/session/fee/acknowledgment functions only.

Deferred details:

- Exact HikCentral AK/SK header names and signature construction.
- Exact request canonicalization and timestamp/nonce behavior.
- Exact credential field names and environment variable names.
- Exact rotation process and overlap window.

## 7. Secret Storage and Rotation Requirements at Architecture Level

The connector design should require a managed secret storage posture without selecting the final product.

Architecture-level requirements:

- Secrets must be stored outside source control and outside static committed configuration.
- Secret access must be auditable by connector instance, environment, operator/support actor where human access is involved, and time.
- Secret lifecycle must support provisioning, activation, rotation, revocation, retirement, and emergency disablement.
- Rotation must be possible without changing source code.
- Credential rotation, revocation, lifecycle tracking, and access audit are required at architecture level.
- Test/UAT secrets must be physically or logically segregated from production secrets and must not be reused across environments.
- Break-glass access, if allowed, must be time-bound, approved, strongly audited, and reconciled after use.
- Disposable test logs, debug dumps, screenshots, and exported traces must be treated as leak vectors and must not contain secrets.

Deferred details:

- Final secret store or vault product.
- Exact secret naming convention or environment variable names.
- Exact credential rotation process.
- Exact break-glass approval and access process.
- Whether credential lifecycle/revocation will be tracked in a platform registry.

## 8. Request Signing / Authentication Posture

The connector is responsible for authenticating and signing outbound requests to Vendor PMS/HikCentral where required by the vendor.

Architecture-level requirements:

- Central PMS must call the connector through ExitPass service-to-service trust controls.
- The connector must call HikCentral through vendor-specific authentication controls.
- These two trust relationships must be modeled separately and audited separately.
- Request signing/authentication must be replay-aware and time/skew-aware where the vendor mechanism requires it.
- Failed vendor authentication must be surfaced as a connector/vendor dependency state without exposing secret material or raw vendor-sensitive payloads.
- Vendor authentication failures must not be converted into payment finality, fiscal issuance success, ExitAuthorization, or degraded authorization.
- Request and response logs must redact secrets, signatures, tokens, authorization material, sensitive vendor payloads, and personally sensitive session details.

Deferred details:

- Exact headers.
- Exact signing string.
- Exact clock skew tolerance.
- Exact retry/backoff mechanics for authentication failures.
- Exact code implementation.

## 9. Network and Deployment Trust Posture

The deployment posture must preserve authority separation even if components are co-located.

Architecture-level requirements:

- Connector runtime deployments should be tied to configured VendorSystem instances and AdapterMapping.
- Network paths from Central PMS to connector and connector to Vendor PMS/HCP should be independently controlled, monitored, and audited.
- The connector should expose only the platform-facing interfaces needed for approved connector workflows.
- Vendor-facing network access should be limited to required vendor endpoints and environments.
- Production, test, and UAT network routes and credentials must be segregated.
- Connector unavailability, stale projection, failed polling, high latency, and vendor authentication failure must be observable without exposing secrets.
- Connector deployment must not give operators direct access to vendor credentials or raw unredacted vendor payloads.

Deferred details:

- Final mTLS topology.
- Final certificate model.
- Exact service mesh or network segmentation model.
- Whether device certificates or PKI service identity will be used.
- Exact deployment packaging and runtime secret injection mechanism.

## 10. Audit and Non-Repudiation Requirements

The connector must support traceability without making logs a secret exposure channel.

Audit should capture, at architecture level:

- Connector instance identity.
- VendorSystem identity.
- AdapterMapping or vendor object mapping context where applicable.
- Calling platform service identity.
- Operation category, such as live resolve, projection polling, health check, or vendor acknowledgment.
- Request correlation ID and platform workflow correlation.
- Success, failure, timeout, stale, unavailable, ambiguous, authentication failed, or authorization denied outcome.
- Credential version or credential lifecycle reference if a non-secret reference is available.
- Rotation, revocation, provisioning, and break-glass access events.
- Operator/support access to connector configuration or health state.
- Redaction status for sensitive payload handling where applicable.

Audit must not include real secrets, derived signatures, raw authorization headers, or unredacted sensitive vendor payloads. Audit records must support reconciliation and post-restoration review for vendor acknowledgment failures, connector stale/unavailable states, and continuity-origin activity.

## 11. RBAC / Operations Access Boundaries

Operations access to connector configuration, health, logs, and secret lifecycle must be restricted and audited.

Required boundaries:

- Operator Console may display connector health and projection freshness for authorized operational context, but must not expose secrets or raw sensitive vendor payloads.
- Management Dashboard may show connector status, freshness, availability, poll latency, failed poll count, and vendor acknowledgment backlog where authorized, with source and freshness labels.
- Technical support access should be read-limited by default and scoped to environment, Site/Site Group, VendorSystem, and incident context.
- High-risk actions such as credential provisioning, rotation, revocation, break-glass access, connector disablement, connector reconfiguration, and mapping changes require elevated permission and audit.
- Connector operators must not gain payment finality, fiscal issuance, ExitAuthorization, manual release, continuity activation, discount approval, or gate-control authority by virtue of connector access.
- Evidence, personal data, and sensitive vendor payload access must be minimized, role-protected, redacted where possible, and audited.

Exact permission matrix and operational role mapping remain deferred.

## 12. Security Risks and Mitigations

| Risk | Impact | Architecture-level mitigation |
| --- | --- | --- |
| Vendor credential leakage | Unauthorized vendor access, session manipulation, data exposure, operational disruption. | Keep credentials inside connector boundary; use secret storage; redact logs/traces/errors; prevent secrets in repo/docs/tests/screenshots/prompts. |
| Central PMS service trust confused with vendor authentication | Authority leakage or incorrect incident response. | Model Central PMS-to-connector trust separately from connector-to-HikCentral authentication. |
| AppKey/AppSecret or AK/SK committed to config | Persistent compromise and difficult revocation. | Treat credentials as environment/secret-store values only; validate no secrets are committed. |
| Raw vendor errors surfaced to users | Secret exposure, vendor internals exposure, confusing customer/operator messages. | Normalize and redact vendor errors before user-facing or operator-facing channels. |
| Overprivileged HikCentral credential | Larger blast radius if connector or secret is compromised. | Use least-privilege vendor credentials where supported; segregate by environment and VendorSystem. |
| Rotation not designed | Long-lived credential risk and emergency recovery weakness. | Require lifecycle tracking, rotation, revocation, access audit, and emergency disablement. |
| Test/UAT secret reuse | Production compromise through lower-control environments. | Segregate production and test/UAT credentials, logs, and network routes. |
| Disposable test log leakage | Secrets or sensitive payloads leak through debug artifacts. | Redact by default and prohibit secrets in disposable logs, screenshots, traces, and fixtures. |
| Connector operator overreach | Connector access used to bypass payment, fiscal, exit, continuity, or gate controls. | Enforce RBAC, segregation of duties, non-authority constraints, and audit. |
| Projection or vendor payload treated as financial truth | Incorrect revenue, tariff, fiscal, or exit decisions. | Label projection as operational visibility; preserve Central PMS, POS Server, and Vendor PMS authority boundaries. |
| Stale or unauthenticated vendor responses accepted | Incorrect degraded decisions or operational state. | Fail closed when identity, freshness, authentication, or mapping is uncertain. |

## 13. Open Security Questions

These questions should be preserved for the Lead integration pass and later design work:

- What is the final secret store?
- What is the final certificate model?
- What is the exact mTLS topology?
- What is the exact service identity implementation?
- What are the exact environment variable names?
- What is the exact credential rotation process?
- What is the exact break-glass access process?
- What are the exact HikCentral AK/SK header and signature implementation details?
- What is the exact test secret provisioning process?
- Will device certificates or PKI service identity be used?
- Will credential lifecycle and revocation be tracked in a platform registry?
- What is the exact permission matrix for connector operators, administrators, support users, auditors, and deployment automation?
- What are the exact redaction rules for vendor payloads, vendor errors, support bundles, dashboard views, and disposable test logs?
- What is the exact production versus test/UAT secret and network segregation model?
- What is the exact audit retention period for connector credential lifecycle and access events?

## 14. Summary for Lead

The later Vendor PMS Connector System Design and HikCentral Connector Profile should treat the connector as a security boundary, not just an API adapter. Vendor credentials stay inside that boundary. HikCentral AppKey/AppSecret or AK/SK values are secret-store/environment-provided runtime values and must never be committed, documented as real values, logged, screenshotted, included in prompts, or placed in test fixtures.

The Lead design should preserve separate trust relationships: Central PMS authenticates to the connector using ExitPass service-to-service trust, while the connector authenticates and signs requests to HikCentral using vendor-specific credentials. These identities, audits, and failure modes should remain separate.

Required architecture posture includes least privilege, secret segregation by environment, rotation/revocation/lifecycle tracking, access audit, redaction of secrets and sensitive vendor payloads, normalized vendor errors, restricted connector operator access, and explicit open questions for final secret store, certificate model, mTLS topology, service identity, rotation, break-glass, and HikCentral signing details.
