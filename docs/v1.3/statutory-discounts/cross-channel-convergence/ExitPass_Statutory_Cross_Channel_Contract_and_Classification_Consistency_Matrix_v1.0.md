# ExitPass Statutory Cross-Channel Contract and Classification Consistency Matrix v1.0

## Contract Finding
The main statutory nouns have converged on shared meanings, but some channel-specific classifications and legacy compatibility fields remain. The most important contract risk is not enum spelling; it is that some read models still use legacy LGU text while the canonical database now has first-class LGU foreign keys and coverage views.

## Terminology Matrix
| Term | Canonical meaning | WebPay | APT | Operator Console | Central PMS | POS Server | Management Platform | Status |
|---|---|---|---|---|---|---|---|---|
| Senior Citizen | Entitlement type | Same | Same | Same | Same | Fiscal fact | Coverage display | CONVERGED |
| PWD | Entitlement type | Same | Same | Same | Same | Fiscal fact | Coverage display | CONVERGED |
| Policy coverage | LGU/Site statutory applicability | Gate/recovery input | Availability result | Hidden in normal review | Evaluated from registry | Not authority | Read-only display | PARTIALLY_CONVERGED |
| Ordinance availability | Applicable statutory parking policy for Site jurisdiction | Customer-safe gate | Cash-flow gate | Not reviewer-owned | Availability APIs | Not authority | Coverage class | CONVERGED |
| Statutory decision | Central PMS decision command | Rediscovered/read | Read before application | Approved/rejected | Authoritative | Reference only | Not mutated | CONVERGED |
| Payable-basis application | Payment-time Central PMS application | WebPay service call | APT service call | Should not call | Application-v1 | Consumed as final facts | Not mutated | PARTIALLY_CONVERGED |
| Ordinary payment | Non-statutory payment path | Preserved | Preserved | Separate | Independent | Ordinary fiscal v1 | Not applicable | CONVERGED |
| Evidence reference | Future opaque evidence ref | Contract-only | Contract-only | Metadata currently | Future owner | Prohibited except governed refs | Not applicable | NOT_IMPLEMENTED |
| Site | Operational parking scope | Server fact | Server fact | Request fact | Resource scope | Fiscal fact | Scope selector | CONVERGED |
| Site Group | Admin/query scope, not ordinance authority | Server fact | Server fact | Request fact | Resource scope | Fiscal fact | Scope selector | CONVERGED |
| Jurisdiction/LGU | Legal ordinance scope | Derived through Site | Derived through Site | Hidden operationally | Policy authority | Reference only | Coverage display | PARTIALLY_CONVERGED |
| Policy version | Frozen authority/version | Not customer-owned | Not desktop-owned | Hidden operationally | Decision authority | Safe reference | Admin display | CONVERGED by audience |

## Classification Families
| Family | Values observed | Owner | Issue |
|---|---|---|---|
| WebPay rediscovery | `FOUND`, `NOT_FOUND`, `NO_ACTIVE_LIFECYCLE`, `AMBIGUOUS_SESSION`, `SOURCE_UNAVAILABLE`, `MALFORMED_AUTHORITATIVE_STATE`, `ACCESS_DENIED`, `UNEXPECTED_FAILURE` | Central PMS | Good WebPay-safe family; not reused by APT because cash states differ. |
| Management policy coverage | `ACTIVE_COVERED`, `FUTURE_EFFECTIVE`, `EXPIRED`, `INACTIVE`, `INCOMPLETE_CONFIGURATION`, `NO_APPLICABLE_ORDINANCE`, `NO_APPLICABLE_POLICY`, `ENTITLEMENT_NOT_COVERED`, `AUTHORITATIVE_SOURCE_UNAVAILABLE`, `MALFORMED_AUTHORITATIVE_RECORD` | Central PMS | Needs canonical LGU read-model adoption. |
| POS statutory fiscal | `SENIOR_CITIZEN`, `PWD`, benefit classifications, VAT treatments, policy resolution bases, `WEBPAY`, `ASSISTED_PAYMENT_TERMINAL`, `OPERATOR_CONSOLE` | POS Server | `OPERATOR_CONSOLE` source channel should be reviewed. |
| Canonical DB verification | `VERIFIED_OFFICIAL`, `VERIFIED_ACTIVE_OPERATIONAL`, `VERIFIED_SECONDARY`, `LEAD_UNVERIFIED`, `PROPOSED`, `NO_LOCAL_RULE_FOUND` | Canonical DB | Correctly separates research verification from lifecycle and auto-application. |

## Contract Gaps
1. POS Server currently allows `OPERATOR_CONSOLE` as a source payment channel classification in applied statutory fiscal facts. That may be a compatibility placeholder, but it conflicts with the frozen rule that Operator Console does not apply or pay.
2. Central PMS Management Platform coverage repository should consume the I-006 canonical Site-LGU authority and coverage views rather than compatibility `lgu_code` text.
3. APT and WebPay use different safe classification names for similar unavailable or retryable conditions; support documentation should map them.
4. Evidence references are contract-frozen but not implemented; no channel should treat metadata-only evidence as secure evidence bytes.
