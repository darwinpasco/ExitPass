# UAT 002 Run Summary

A new disposable normal fiscal issuance UAT identity was seeded and `/run` was invoked exactly once.

The controlled UAT endpoint rejected the new identity before fiscal reference preparation and before POS Server invocation because the application currently approves only the hard-coded first-run identity.

Response:

- HTTP status: `400 Bad Request`
- status: `run_rejected`
- errors: `run_id_not_approved_for_first_run`, `correlation_id_not_approved_for_first_run`, `upstream_finality_ref_not_approved_for_first_run`, `business_day_date_not_approved_for_first_run`
- diagnosticInvoked: `false`
- posServerCallAttempted: `false`

New upstream finality reference:

- `CPS-POS-UAT:CPS-POS-UAT-20260706-DEV-ATC-002:newly_created:001`

Seeded rows:

- parking session: `00000000-0000-4000-8000-000000000313`, `CPS-POS-UAT-PARKING-SESSION-002`
- payment attempt: `00000000-0000-4000-8000-000000000312`, `CPS-POS-UAT-PAYMENT-ATTEMPT-002`
- payment confirmation: `00000000-0000-4000-8000-000000000311`, provider transaction ref matching the new upstream finality ref

No Central PMS fiscal issuance reference was created for the new upstream finality ref. No POS Server fiscal document or idempotency row was created for the new UAT refs.

No forbidden side effects were detected.
