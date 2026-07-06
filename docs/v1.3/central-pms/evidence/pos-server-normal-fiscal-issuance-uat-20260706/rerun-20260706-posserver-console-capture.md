# Rerun 2026-07-06 POS Server Console Capture

POS Server was reachable at `http://localhost:5000`, but it is not running as a Docker container in `docker ps`, so container console logs are not available to capture from this session.

The Central PMS `/run` response for this rerun returned HTTP 409 before diagnostic invocation:

- status: `fiscal_reference_prepare_rejected`
- errors: `fiscal_reference_not_startable_state`, `FiscalIssuanceFailedService`
- diagnosticInvoked: `false`
- posServerCallAttempted: `false`

Because Central PMS did not attempt POS Server POST for this rerun, there is no POS Server console POST `/v1/fiscal-documents/` result for this rerun.

See:

- `rerun-20260706-run-error.json`
- `rerun-20260706-run-response-failed.json`
- `rerun-20260706-docker-ps.txt`
- `rerun-20260706-posserver-fiscal-documents-filtered.txt`
- `rerun-20260706-posserver-idempotency-records-filtered.txt`
