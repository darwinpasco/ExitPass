# CODEX_EXITPASS_RULES.md

## Purpose

This file defines the mandatory engineering rules for using Codex on the ExitPass codebase.

Codex must treat this document as the standing instruction set for all ExitPass development work. These rules apply to feature implementation, refactoring, test creation, bug fixing, documentation updates, and pull request preparation.

ExitPass is not a vibe-coded project. It is a requirements-led, design-led, test-driven system. Codex must not invent architecture, relax controls, or introduce implementation shortcuts that conflict with the approved ExitPass baseline.

---

# 1. Active Baseline

## 1.1 Mandatory Baseline Version

The active baseline is:

- ExitPass BRD v1.2
- ExitPass System Design Document v1.2
- ExitPass Database Design v1.2
- ExitPass Full Database Creation DDL v1.2
- ExitPass API Contract Pack v1.2
- ExitPass Engineering Pack v1.2, where applicable

Codex must use only the v1.2 baseline unless the task explicitly asks for comparison against an older version.

## 1.2 Deprecated Baselines

The following must not be used as implementation references:

- ExitPass BRD v1.0
- ExitPass BRD v1.1
- ExitPass System Design v1.0
- ExitPass System Design v1.1
- ExitPass Database Design v1.0
- Earlier DDL drafts
- Earlier API contract drafts

Codex must not introduce references to v1.0 or v1.1 in code comments, tests, documentation, or commit messages unless the task explicitly asks for historical comparison.

## 1.3 Section References Must Be Current

When adding code comments, XML documentation, tests, or implementation notes, Codex must mention the exact ExitPass v1.2 sections being implemented or enforced.

Do not write generic references such as:

- “Implements BRD requirement”
- “Aligned with SDD”
- “Enforces invariant”

Instead, write specific references such as:

- “Implements BRD v1.2 Section 12: Payment Orchestration”
- “Corresponds to SDD v1.2 Section 8.4: Payment Finalization Flow”
- “Enforces System Invariant INV-PAY-001: Provider-verified outcome must precede Central PMS payment finality”

If the exact BRD, SDD, or invariant reference is uncertain, Codex must stop and report the uncertainty instead of inventing a section number.

---

# 2. Core Engineering Principles

## 2.1 Requirements-First Development

Codex must not implement features based only on inferred behavior.

Every implementation must be traceable to:

- A BRD v1.2 functional requirement
- An SDD v1.2 design section
- A database design rule, if persistence is involved
- An API contract, if external or internal service communication is involved
- A system invariant, if the behavior affects authority, payment finality, session lifecycle, audit, security, or exit authorization

## 2.2 Design-Led Implementation

Codex must not introduce architecture that is not present in the approved design.

Codex must preserve the ExitPass architecture, including:

- Central PMS as the authoritative control system
- Vendor PMS as the authoritative source for parking session discovery, tariff computation, and exit eligibility signals where applicable
- Payment Orchestrator API as the payment attempt and provider integration layer
- Gate Integration Service as the controlled interface to physical gate execution
- Audit Event Service as the audit trail and observability support component
- Site Integration Adapter as the just-in-time integration layer to Vendor PMS platforms
- Database constraints as part of the system control model, not merely storage mechanics

## 2.3 Test-Driven Development

Codex must use test-driven development when implementing new behavior.

The expected sequence is:

1. Identify the relevant BRD v1.2 requirement, SDD v1.2 section, and invariant.
2. Add or update failing tests that express the required behavior.
3. Implement the minimum correct code to satisfy the tests.
4. Run the relevant tests.
5. Fix failures without weakening assertions.
6. Explain what changed.

Codex must not skip tests unless the task explicitly states that tests are not required.

## 2.4 Object-Oriented Design

Codex must prefer object-oriented design over procedural scripts.

Use:

- Domain services for business behavior
- Repositories for persistence access
- Value objects for concepts with rules
- DTOs or contracts for transport models
- Validators for request validation
- Explicit interfaces where substitution or testing is required
- Clear method boundaries with single responsibilities

Avoid:

- Large procedural helper methods
- God classes
- Business logic embedded directly in controllers or endpoints
- Hidden state mutations
- Stringly typed domain logic
- Copy-pasted logic across services

## 2.5 Explicitness Over Cleverness

ExitPass code must be readable, auditable, and traceable.

Codex must prefer:

- Clear names
- Explicit branching
- Defensive validation
- Deterministic behavior
- Strong test assertions
- XML comments for public and important internal types
- Domain-specific exception messages

Codex must avoid:

- Overly clever abstractions
- Magic strings
- Reflection-heavy code
- Implicit conventions that are not documented
- Generic utility abstractions that obscure business meaning

---

# 3. Mandatory Code Comment Standard

## 3.1 Required Comment Content

Every new important class, public method, handler, domain service, validator, repository method, and integration endpoint must include comments or XML documentation stating:

1. Which BRD v1.2 section it implements
2. Which SDD v1.2 section it corresponds to
3. Which system invariant it enforces
4. Any authority boundary it must preserve

## 3.2 XML Comment Format

For C# code, use XML documentation where appropriate.

Example:

```csharp
/// <summary>
/// Finalizes a payment attempt only after the payment provider outcome has been verified.
/// </summary>
/// <remarks>
/// BRD v1.2 Reference:
/// Section 12 - Payment Orchestration, requiring provider-verified payment outcome handling.
///
/// SDD v1.2 Reference:
/// Section 8.4 - Payment Finalization Flow, defining Central PMS finalization after verified provider outcome.
///
/// System Invariant:
/// INV-PAY-001 - A payment attempt must not be finalized by Central PMS unless the provider outcome has been verified
/// through the Payment Orchestrator API or an approved verified provider outcome pathway.
///
/// Authority Boundary:
/// The Payment Orchestrator verifies provider outcome, while Central PMS records payment finality
/// and controls exit authorization eligibility.
/// </remarks>
```

## 3.3 No Stale References

Codex must not write comments referencing:

- BRD v1.0
- BRD v1.1
- SDD v1.0
- SDD v1.1
- Draft-only sections
- Nonexistent section numbers
- Generic “requirement alignment” without exact section references

If Codex cannot verify the correct section reference, it must write:

```csharp
// TODO: Confirm exact BRD v1.2 and SDD v1.2 section references before merging.
```

This is acceptable only as a temporary marker and must be reported in the final task summary.

---

# 4. Authority Model Rules

## 4.1 Central PMS Authority

Central PMS is authoritative for:

- ExitPass payment finality
- Exit authorization issuance
- Exit authorization lifecycle
- System-level session control state
- Audit correlation
- Site group resolution
- Business policy enforcement
- Final eligibility to release an ExitPass-controlled authorization

Codex must not move these responsibilities to the Payment Orchestrator, Vendor PMS Adapter, Gate Integration Service, or WebPay UI.

## 4.2 Vendor PMS Authority

Vendor PMS is authoritative for:

- Native parking session discovery
- Parking fee computation
- Tariff calculation
- Vendor-side session state
- Vendor-side exit eligibility inputs
- Physical gate/barrier logic where controlled by the Vendor PMS

Codex must not cause Central PMS to independently recalculate tariffs unless explicitly required by the approved design.

## 4.3 Payment Orchestrator Authority

The Payment Orchestrator API is responsible for:

- Creating payment attempts
- Reusing eligible payment attempts
- Communicating with payment providers
- Handling provider webhooks
- Verifying provider payment outcomes
- Reporting verified outcomes to Central PMS

Codex must not allow Central PMS to trust raw client-side payment success claims.

## 4.4 Gate Integration Authority

Gate Integration Service is responsible for:

- Receiving exit authorization consume requests
- Coordinating gate execution
- Reporting gate execution result
- Preserving the distinction between authorization and physical gate action

Codex must not treat gate opening as equivalent to payment finality.

## 4.5 WebPay UI Authority

WebPay UI is not authoritative for:

- Session state
- Payment finality
- Exit authorization
- Tariff computation
- Discount approval
- Coupon validation
- Gate execution

The WebPay UI may initiate requests and display status, but it must not be treated as a trusted source of final state.

---

# 5. Payment Rules

## 5.1 Provider Verification Required

A payment must not be finalized based only on:

- Browser redirect success
- Client-side callback
- User screenshot
- Query string status
- Unverified frontend signal
- Unverified webhook body

A payment may be finalized only after provider outcome verification through an approved server-side path.

## 5.2 Payment Attempt Lifecycle

Codex must preserve the approved payment attempt lifecycle.

A payment attempt must have clear state transitions and must not skip required states merely to make the implementation simpler.

Do not introduce new payment statuses unless explicitly instructed and aligned with:

- BRD v1.2
- SDD v1.2
- Database enum definitions
- API contracts
- Existing integration tests

## 5.3 Create or Reuse Behavior

When implementing create-or-reuse payment attempt logic, Codex must preserve these rules:

- Reuse only eligible active attempts
- Do not reuse expired attempts
- Do not reuse failed attempts
- Do not reuse attempts linked to a different session
- Do not reuse attempts where amount, currency, provider, or session context no longer matches
- Preserve idempotency where required
- Avoid duplicate payable attempts for the same active session unless explicitly permitted

## 5.4 Payment Finalization

Payment finalization must:

- Be server-side
- Be provider-verified
- Be idempotent where applicable
- Be auditable
- Preserve the link between payment attempt, parking session, tariff snapshot, and exit authorization
- Not create duplicate exit authorizations for the same finalized payment unless explicitly allowed by the design

## 5.5 Webhooks

Webhook handlers must:

- Verify webhook signature where supported
- Reject malformed payloads
- Reject unrecognized provider events unless explicitly mapped
- Be idempotent
- Handle duplicate deliveries safely
- Avoid trusting client-provided status
- Record audit-relevant events
- Avoid leaking secrets in logs

## 5.6 Payment Provider Abstraction

Provider-specific logic must be isolated.

Codex must not scatter PayMongo, AUB, or other provider-specific logic across domain services.

Use provider adapters, provider clients, or integration services as appropriate.

---

# 6. Parking Session Rules

## 6.1 Session Discovery

Parking session discovery must follow the approved Site Group model.

Codex must preserve:

- Site group resolution
- Property-level aliasing where used by the business layer
- Mapping from a parker-provided plate number or ticket reference to the correct Vendor PMS session
- Just-in-time Vendor PMS query behavior where applicable

## 6.2 Vendor PMS Fee Authority

The Vendor PMS is responsible for computing the parking fee unless the approved design explicitly states otherwise.

Codex must not introduce Central PMS fee computation as a shortcut.

## 6.3 Tariff Snapshot

Tariff snapshots must preserve the tariff context used for payment.

Codex must not overwrite tariff snapshot behavior without checking the SDD and database design.

If a tariff snapshot expires during payment flow, Codex must follow the approved expiry handling rules.

## 6.4 Active Session Rules

Codex must not allow payment, discount, coupon, or exit authorization logic to proceed against an invalid, closed, cancelled, or stale session unless the approved design explicitly allows it.

---

# 7. Exit Authorization Rules

## 7.1 Exit Authorization Is Not Payment

Payment finality and exit authorization are related but distinct.

Codex must preserve this distinction.

A successful payment does not automatically mean the gate has opened. It means the system may issue or activate an exit authorization subject to the approved rules.

## 7.2 Exit Authorization Creation

Exit authorization creation must be:

- Based on finalized payment or approved exception flow
- Linked to the parking session
- Linked to the relevant payment attempt where applicable
- Auditable
- Time-bound or lifecycle-controlled according to the approved design
- Safe against duplicate issuance

## 7.3 Exit Authorization Consumption

Exit authorization consumption must:

- Be idempotent where required
- Record the gate, lane, site, or device context where applicable
- Preserve audit trail
- Avoid double consumption
- Distinguish authorization consumption from physical gate success
- Handle failed gate execution separately from authorization validity

## 7.4 Gate Execution Result

Gate execution result must not rewrite payment finality.

Gate execution errors must be handled as operational events, not payment reversals, unless the approved design explicitly defines such behavior.

---

# 8. Coupon and Discount Rules

## 8.1 Coupon Governance

Codex must preserve coupon governance rules, including:

- Merchant-controlled coupon issuance
- Merchant wallet or funding rules where applicable
- Denomination-based discount behavior
- Restrictions on full parking waiver privileges
- Coupon validity windows
- Coupon use limits
- Coupon audit trail

## 8.2 Coupon Application

Coupon application must:

- Be validated before payment finalization
- Be linked to the correct parking session
- Not exceed allowed benefit
- Not apply beyond the approved baseline minimum-hour rule unless explicitly allowed
- Be auditable

## 8.3 Statutory Discounts

Statutory discounts must follow the approved rules.

Codex must preserve:

- Validation before application
- Sensitive data handling
- Optional image capture controls where applicable
- No re-evaluation by Central PMS after Vendor PMS has validated the applicable discount, unless the approved design states otherwise
- One use per active session, where required

## 8.4 Discount Abuse Prevention

Codex must not remove or weaken controls that prevent abuse of:

- Senior citizen discounts
- PWD discounts
- Free-hours behavior
- Coupon stacking
- Re-entry abuse
- Merchant-funded full waivers

---

# 9. Database Rules

## 9.1 Database Is Part of the Control Model

Database constraints are not optional.

Codex must treat the database design as part of the system’s control and audit model.

Do not bypass database constraints in application code.

## 9.2 No Schema Changes Without Instruction

Codex must not modify:

- DDL
- Enum definitions
- Foreign keys
- Check constraints
- Unique constraints
- Indexes
- Reference data
- Stored functions
- Stored procedures
- Schema ownership rules

unless the task explicitly requires it.

If Codex discovers that a schema change appears necessary, it must stop and report:

- The required change
- Why the current schema blocks the task
- Which BRD v1.2 section requires the change
- Which SDD v1.2 section supports the change
- Which tests would be affected

## 9.3 Enum Consistency

Application enums must match database enums.

Codex must not create new enum values in C# without verifying the corresponding PostgreSQL enum or lookup table.

## 9.4 Stored Routines

If the design uses PostgreSQL functions or stored procedures for control-sensitive operations, Codex must not bypass them unless explicitly instructed.

Where stored routines enforce concurrency, idempotency, or transaction boundaries, Codex must preserve those guarantees.

## 9.5 Transactions

Database writes affecting payment, session state, exit authorization, coupons, discounts, or audit correlation must use appropriate transaction boundaries.

Codex must not split atomic operations into unsafe multi-step writes without compensation or locking.

## 9.6 Concurrency

Codex must consider concurrency for:

- Payment attempt creation
- Payment attempt reuse
- Payment finalization
- Exit authorization issuance
- Exit authorization consumption
- Coupon redemption
- Discount application
- Webhook duplicate delivery
- Retry handling

Use database constraints, row-level locks, idempotency keys, or transaction isolation where appropriate.

---

# 10. API and Contract Rules

## 10.1 Contract Alignment

API changes must align with the ExitPass API Contract Pack v1.2.

Codex must not change request or response contracts casually.

If an API contract must change, Codex must identify:

- The affected endpoint
- The current contract
- The proposed contract
- The affected consumers
- The required test updates
- The BRD and SDD basis for the change

## 10.2 DTO Discipline

DTOs must not leak persistence models directly unless explicitly approved.

Use separate models for:

- API requests
- API responses
- Domain objects
- Persistence records
- Provider-specific payloads

## 10.3 Internal APIs

Internal service APIs must preserve service boundaries.

Codex must not allow one service to reach into another service’s database directly unless the approved architecture explicitly allows it.

## 10.4 Error Responses

Error responses must be:

- Deterministic
- Safe
- Non-leaking
- Useful for diagnostics
- Mapped to appropriate HTTP status codes
- Auditable where relevant

Do not expose secrets, provider raw errors, stack traces, or internal database details in public responses.

---

# 11. Security Rules

## 11.1 Secret Handling

Codex must never hardcode:

- API keys
- Provider secret keys
- Webhook secret keys
- Database passwords
- JWT signing keys
- mTLS private keys
- Production credentials

Use environment variables, secret stores, or local development placeholders.

## 11.2 Logging Secrets

Codex must not log:

- Provider secrets
- Full authorization headers
- Webhook signature secrets
- Full payment tokens
- Full personally identifiable information
- Sensitive ID document data
- Raw payloads containing secrets or sensitive personal data

## 11.3 Authentication and Authorization

Codex must preserve role-based access control.

Do not bypass authorization for:

- Admin actions
- Merchant actions
- Coupon management
- Wallet or funding controls
- Payment operations
- Exit authorization operations
- Manual override operations
- Operational fallback activation

## 11.4 Sensitive Personal Data

Sensitive personal data must be minimized.

Codex must follow privacy-by-design rules for:

- Plate numbers
- Contact details
- ID details
- Senior citizen or PWD validation data
- Optional image capture
- Audit logs
- Retention rules

## 11.5 Webhook Security

Webhook endpoints must:

- Verify provider signatures where available
- Reject replay or duplicate events where required
- Handle idempotency safely
- Avoid trusting user-controlled metadata as final truth
- Record verification outcome

---

# 12. Audit and Observability Rules

## 12.1 Audit Trail Required

Control-sensitive actions must generate audit-relevant records or events.

This includes:

- Session discovery
- Payment attempt creation
- Payment provider outcome verification
- Payment finalization
- Exit authorization issuance
- Exit authorization consumption
- Coupon application
- Discount application
- Manual override
- BCP/MoPS activation
- Gate execution result
- Admin configuration changes

## 12.2 Audit Events Must Be Meaningful

Audit events must include useful context, such as:

- Correlation ID
- Session ID
- Payment attempt ID
- Provider reference, if safe to store
- Site ID
- Site group ID
- Merchant ID, where applicable
- Actor or service identity
- Event timestamp
- Outcome
- Failure reason, where safe

## 12.3 Observability

Codex must preserve or improve observability for:

- Logs
- Metrics
- Traces
- Correlation IDs
- Error diagnostics
- Payment provider failures
- Webhook processing
- Gate execution failures
- Database failures

## 12.4 No Noisy Logging

Do not add excessive logs that obscure real issues.

Logs must be useful for operations, support, audit, and debugging.

---

# 13. Exception and Degraded Mode Rules

## 13.1 No Silent Fallback

Codex must not introduce silent fallback behavior.

Fallback modes must be explicit, auditable, and aligned with BRD v1.2 and SDD v1.2.

## 13.2 MoPS and BCP

Manual or degraded operations must follow approved governance.

Codex must not allow manual payment confirmation, manual gate release, or fallback authorization without the required control trail.

## 13.3 Provider Outage

Payment provider outages must not be hidden as successful payment outcomes.

Codex must distinguish:

- Provider timeout
- Provider unavailable
- Provider declined
- Provider pending
- Provider paid
- Provider verification failed
- Unknown provider state

## 13.4 Vendor PMS Outage

Vendor PMS outage handling must follow the approved degraded mode design.

Do not invent cached tariff behavior, manual session creation, or exit eligibility assumptions unless already defined in the approved baseline.

---

# 14. Testing Rules

## 14.1 Required Test Types

Codex must add or update tests based on the nature of the change.

Use unit tests for:

- Domain rules
- Validators
- State transitions
- Idempotency logic
- Mapping logic
- Error classification

Use integration tests for:

- Database routines
- API endpoints
- Repository behavior
- Service-to-service behavior
- Webhook flows
- Transaction boundaries
- Concurrency-sensitive behavior

Use contract tests where API compatibility is affected.

## 14.2 Test Naming

Test names must be descriptive and behavior-focused.

Preferred format:

```csharp
MethodName_StateUnderTest_ExpectedBehavior()
```

Example:

```csharp
FinalizePaymentAttempt_WhenProviderOutcomeIsNotVerified_ShouldRejectFinalization()
```

## 14.3 Tests Must Not Be Weakened

Codex must not:

- Remove failing tests to make the build pass
- Weaken assertions without justification
- Replace specific assertions with broad “not null” checks
- Ignore exceptions without validating behavior
- Mark tests as skipped unless explicitly approved
- Use arbitrary delays instead of deterministic synchronization

## 14.4 Test Data

Test data helpers must:

- Reflect ExitPass v1.2 schema
- Use current enum values
- Avoid stale v1.0 or v1.1 assumptions
- Be explicit about session, payment, tariff, site, and authorization relationships
- Avoid hidden magic defaults that affect test correctness

## 14.5 Integration Test Database

Integration tests must target the correct ExitPass v1.2 database schema.

Codex must not assume the v1.0 database is still valid.

If a test fails because the database schema is stale, Codex must report the mismatch clearly.

---

# 15. Refactoring Rules

## 15.1 Safe Refactoring

Refactoring is allowed only when behavior is preserved or the task explicitly requires behavior change.

Codex must:

- Preserve public contracts unless instructed
- Preserve test behavior unless tests are stale or incorrect
- Keep diffs focused
- Avoid unrelated cleanup
- Avoid broad renaming unless requested
- Explain changed behavior clearly

## 15.2 No Architecture Drift

Codex must not use refactoring as an opportunity to change the architecture.

Do not move business authority across service boundaries.

Do not collapse domain, persistence, and API layers into convenience methods.

## 15.3 Refactor With Tests

Codex must run tests after refactoring.

If tests do not exist for the affected area, Codex should add characterization tests before refactoring where practical.

---

# 16. Pull Request Rules

## 16.1 Required PR Summary

Every Codex-generated pull request must include:

- Summary of changes
- Files changed
- BRD v1.2 sections referenced
- SDD v1.2 sections referenced
- System invariants enforced
- Tests added or updated
- Commands run
- Known limitations
- Items requiring human review

## 16.2 Required PR Checklist

Use this checklist in every PR description:

```md
## ExitPass v1.2 Compliance Checklist

- [ ] Uses only ExitPass v1.2 baseline references
- [ ] Does not reference BRD v1.0 or v1.1
- [ ] Does not reference SDD v1.0 or v1.1
- [ ] Mentions exact BRD v1.2 sections in important code comments
- [ ] Mentions exact SDD v1.2 sections in important code comments
- [ ] Identifies the system invariant enforced
- [ ] Preserves Central PMS authority
- [ ] Preserves Vendor PMS authority
- [ ] Preserves Payment Orchestrator authority
- [ ] Preserves Gate Integration authority
- [ ] Does not introduce unauthorized schema changes
- [ ] Does not introduce unauthorized enum values
- [ ] Adds or updates unit tests
- [ ] Adds or updates integration tests where required
- [ ] Does not weaken existing tests
- [ ] Runs relevant test commands
- [ ] Does not hardcode secrets
- [ ] Does not leak sensitive data in logs
- [ ] Keeps the diff focused on the requested task
```

## 16.3 Human Review Required

Codex must explicitly flag for human review any change involving:

- Payment finality
- Exit authorization
- Database schema
- Enums
- Stored routines
- Security
- Authentication
- Authorization
- Coupon governance
- Discount rules
- Manual override
- MoPS or BCP behavior
- Provider integration
- Gate integration
- Audit logging
- Sensitive personal data

---

# 17. File-Specific Guidance

## 17.1 Handlers

Handlers must:

- Contain orchestration logic only
- Delegate domain decisions to domain services where practical
- Validate inputs
- Preserve idempotency
- Emit or trigger audit events where required
- Avoid direct provider-specific logic unless the handler itself is provider-specific

## 17.2 Domain Services

Domain services must:

- Enforce business rules
- Be testable
- Avoid infrastructure concerns
- Use clear domain language
- Preserve authority boundaries

## 17.3 Repositories

Repositories must:

- Encapsulate persistence details
- Avoid business decision-making
- Respect database constraints
- Use transactions where required
- Return meaningful domain or persistence models

## 17.4 Controllers and Endpoints

Controllers and endpoints must:

- Be thin
- Validate transport-level input
- Call application services or handlers
- Return safe, deterministic responses
- Avoid embedding business logic

## 17.5 Test Helpers

Test helpers must:

- Make test setup easier without hiding important state
- Use explicit defaults
- Avoid stale schema assumptions
- Avoid creating invalid domain state unless the test is specifically testing invalid state

---

# 18. Naming Rules

## 18.1 Domain Language

Use ExitPass domain language consistently.

Preferred terms include:

- ParkingSession
- TariffSnapshot
- PaymentAttempt
- ProviderPaymentOutcome
- VerifiedPaymentOutcome
- ExitAuthorization
- GateExecutionResult
- SiteGroup
- VendorPmsSession
- CouponRedemption
- StatutoryDiscountApplication

Avoid vague names such as:

- Data
- Info
- Manager
- Helper
- Processor
- Thing
- Result2
- NewStatus

unless there is an existing convention or a specific reason.

## 18.2 Avoid Misleading Names

Do not use names that imply incorrect authority.

For example:

- Do not name a WebPay response `PaymentFinalizedResponse` unless payment finality has actually been recorded by the authoritative backend.
- Do not name a gate call `FinalizeExit` if it only requests physical gate opening.
- Do not name a provider webhook model `TrustedPaymentResult` unless it has passed verification.

---

# 19. Error Handling Rules

## 19.1 Domain Errors

Domain errors must be specific.

Examples:

- `PaymentAttemptNotEligibleForReuse`
- `ProviderOutcomeNotVerified`
- `ParkingSessionNotPayable`
- `TariffSnapshotExpired`
- `ExitAuthorizationAlreadyConsumed`
- `CouponNotApplicable`
- `DiscountAlreadyAppliedForSession`

Avoid generic errors such as:

- `InvalidOperationException`
- `Exception`
- `BadRequest`
- `Failed`

unless wrapped or mapped meaningfully.

## 19.2 Retryable vs Non-Retryable

Codex must distinguish retryable and non-retryable failures.

Examples of potentially retryable failures:

- Provider timeout
- Vendor PMS timeout
- Temporary database connectivity issue
- Webhook delivery duplication
- Transient network failure

Examples of generally non-retryable failures:

- Invalid session
- Expired tariff snapshot
- Invalid provider signature
- Coupon already redeemed
- Exit authorization already consumed
- Unauthorized actor

## 19.3 No Exception Swallowing

Codex must not swallow exceptions silently.

If an exception is intentionally handled, the handling must be:

- Logged where appropriate
- Converted to a domain result where appropriate
- Auditable for control-sensitive flows
- Covered by tests

---

# 20. Development Environment Rules

## 20.1 Local Configuration

Local development must use environment variables or development configuration files.

Do not commit local secrets.

## 20.2 Docker

When changing Docker or compose files, Codex must preserve:

- Service names
- Expected internal ports
- Health checks
- Environment variable mappings
- PostgreSQL configuration
- RabbitMQ configuration
- OpenTelemetry configuration
- Reverse proxy routing

unless explicitly instructed.

## 20.3 Database Port Awareness

Codex must not assume that local PostgreSQL always runs on host port 5432.

If connection issues arise, verify whether another PostgreSQL instance or container is already using the port.

## 20.4 Observability Stack

Codex must avoid breaking:

- OpenTelemetry collector configuration
- Prometheus scraping
- Grafana dashboards
- Trace correlation
- Structured logging

---

# 21. Documentation Rules

## 21.1 Documentation Must Match Code

If Codex changes behavior, it must identify whether documentation needs to be updated.

Affected documents may include:

- BRD
- SDD
- Database Design
- API Contract Pack
- Engineering Pack
- README
- Developer setup guide
- Operational runbooks

Codex must not update canonical business documents unless explicitly instructed.

## 21.2 No Unsupported Documentation Claims

Codex must not write documentation that claims a feature is implemented unless the code and tests support it.

## 21.3 Diagrams

If PlantUML diagrams are updated, Codex must preserve:

- Clear labels
- Correct service boundaries
- Correct authority model
- Correct sequence ordering
- No unlabeled arrows
- No invented components

---

# 22. Prohibited Behaviors

Codex must not:

- Use ExitPass v1.0 or v1.1 as the active implementation baseline
- Invent BRD or SDD section numbers
- Invent system invariants
- Introduce schema changes without instruction
- Introduce enum values without checking the database
- Move payment finality outside Central PMS rules
- Trust client-side payment success
- Treat provider webhook payloads as verified without validation
- Treat gate opening as payment finality
- Bypass Vendor PMS tariff authority
- Bypass database constraints
- Hardcode secrets
- Remove or weaken tests to make builds pass
- Add broad, unrelated refactoring
- Hide uncertainty
- Make security-sensitive changes without flagging for review
- Introduce convenience shortcuts that weaken auditability
- Implement fallback behavior without governance
- Change API contracts without explaining impact
- Generate code with generic BRD or SDD references
- Produce large diffs for a small task

---

# 23. Required Task Response Format

At the end of every Codex task, provide a response using this format:

````md
## Summary

Briefly describe what was changed.

## Files Changed

- `path/to/file.cs` - Description of change
- `path/to/test.cs` - Description of test change

## ExitPass v1.2 References

### BRD v1.2

- Section X.X - Description

### SDD v1.2

- Section X.X - Description

### System Invariants

- INV-XXX-000 - Description

## Tests

Commands run:

```bash
dotnet test path/to/TestProject.csproj
```

Result:

- Passed / Failed
- If failed, explain why

## Human Review Notes

List any items that require review.

## Known Limitations

List any limitations or assumptions.

## Not Changed

List important things intentionally left untouched.
````

---

# 24. Standard Prompt Template for Codex Tasks

Use this format when assigning work to Codex:

```md
You are working on the ExitPass codebase.

Use only the ExitPass v1.2 baseline:
- BRD v1.2
- SDD v1.2
- Database Design v1.2
- DDL v1.2
- API Contract Pack v1.2
- Engineering Pack v1.2 where applicable

Do not use or reference v1.0 or v1.1 unless explicitly asked for comparison.

Task:
[Describe the exact task.]

Scope:
[Describe the files, services, or modules involved.]

Requirements:
- Use object-oriented design.
- Use test-driven development.
- Add or update unit tests.
- Add or update integration tests if persistence, API, service-to-service behavior, or concurrency is affected.
- Do not modify the database schema unless explicitly required.
- Do not introduce new enum values unless verified against the v1.2 database schema.
- Do not weaken existing tests.
- Do not hardcode secrets.
- Keep the diff focused.
- Preserve ExitPass authority boundaries.
- Mention exact BRD v1.2 sections, SDD v1.2 sections, and system invariants in important code comments.
- If exact references are uncertain, stop and report the uncertainty.

Validation:
- Run the relevant tests.
- Report the test commands and results.

Output:
- Provide a summary.
- List files changed.
- List BRD v1.2 references.
- List SDD v1.2 references.
- List invariants enforced.
- List tests run.
- List human review notes.
```

---

# 25. Standard Review Prompt for Codex

Use this when asking Codex to review a branch or pull request:

```md
Review this ExitPass branch against the active v1.2 baseline.

Check for:
- Stale BRD v1.0 or v1.1 references
- Stale SDD v1.0 or v1.1 references
- Missing exact BRD v1.2 references
- Missing exact SDD v1.2 references
- Missing system invariant references
- Authority model violations
- Payment finality violations
- Client-side payment trust issues
- Provider webhook verification gaps
- Database schema mismatches
- Enum mismatches
- Weak or missing tests
- Weakened assertions
- Unauthorized schema changes
- Unauthorized API contract changes
- Hardcoded secrets
- Sensitive data leakage in logs
- Overbroad refactoring
- Hidden behavior changes
- Missing audit trail
- Missing idempotency
- Concurrency risks

Do not change code yet.

Return findings grouped as:
- Critical
- High
- Medium
- Low
- Questions for Human Review
```

---

# 26. Final Rule

When in doubt, Codex must choose correctness, traceability, auditability, and explicit human review over speed.

ExitPass must remain a disciplined engineering project.

No shortcuts.
No stale references.
No silent assumptions.
No hidden authority leaks.
No vibe coding.
