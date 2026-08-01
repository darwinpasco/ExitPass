# ExitPass WebPay Pending-Review Regular Payment and Recovery v1.0

## Purpose

This note records the bounded G-005 WebPay lifecycle update for pending statutory review. The implementation adds an explicit regular-payment path while review is pending and keeps statutory privilege authority with Central PMS.

## Regular Payment While Pending

When an authoritative statutory decision is still awaiting review, WebPay shows a distinct `Pay regular amount` action in the statutory request panel. The normal payment button remains blocked for the statutory workflow so the customer cannot silently bypass review.

Before ordinary payment starts, WebPay displays a confirmation warning that states:

- the statutory parking privilege has not been applied
- payment will use the current regular parking amount
- the customer may keep waiting
- approval after payment will not automatically refund or retroactively adjust the transaction

The regular-payment action sends an ordinary `POST /v1/webpay/payment-intents` request without statutory decision or application identifiers. Payment Orchestrator and Central PMS remain authoritative for payable-basis validation, payment-attempt idempotency, provider handoff replay safety, and final payment state.

## Revalidation

Before regular-payment handoff, WebPay:

1. refreshes the statutory decision with `GET /v1/webpay/statutory-discounts/decisions/{statutoryDiscountDecisionCommandId}` when a decision command ID is known
2. refreshes the current parking-session payable basis with `POST /v1/webpay/parking-session`
3. compares the current amount, currency, and tariff snapshot with the amount shown in the warning
4. requires renewed confirmation when the regular amount changes
5. stops stale pending payment if the statutory decision has moved into the approved/application path

The browser does not calculate a discount, does not create a statutory application, and does not trust the displayed amount as payment authority.

## Recovery

Existing browser recovery metadata still restores a known statutory decision command ID by authoritative GET readback after refresh or browser restart. The recovery record remains privacy-minimized metadata and is not authority for approval, application, payable amount, payment finality, fiscal state, or exit authorization.

Manual ticket or plate re-lookup can restore the complete pending panel only when the browser still has a safe recovery record containing the canonical decision command ID. The current merged server contracts do not expose an opaque continuation token or a WebPay-safe recovery endpoint that can rediscover a pending decision by parking session after browser recovery metadata is gone.

Required backend follow-up for complete durable re-lookup recovery:

- Payment Orchestrator-owned opaque continuation token issue and resolve, or
- a WebPay-safe server recovery endpoint that resolves the active canonical statutory decision for the authoritative parking session and entitlement using Central PMS business identity/readback

The frontend must not simulate that missing server authority by storing opaque continuation URLs, decision lookup facts, evidence, or policy state in durable browser storage.

## Finality

If the customer completes ordinary payment while statutory review is pending, WebPay does not promise a future refund and does not imply retroactive adjustment. Later approval remains an eligibility decision only; Central PMS and downstream fiscal services must preserve the amount actually paid.

## Non-Goals

This slice does not implement secure evidence upload, object storage, continuation-token issuance, Operator Console changes, ordinance policy changes, fiscal issuance changes, POS Server changes, APT behavior, HikCentral, gate control, refunds, or retroactive adjustment.
