# ExitPass v1.3 Device Administration runbook

## Authority and rollout posture

Management Platform is the administrative surface. Central PMS remains the only authority for service identity, device assignment, lifecycle, credential version, Site scope, and runtime trust. The v1.3 patch extends those canonical structures; it does not create an alternate trust store.

Apply `infra/db/patches/ExitPass_DeviceAdministration_v1.3.sql`, then run `infra/db/patches/validation/Validate_DeviceAdministration_v1.3.sql`. The patch is idempotent. Stop rollout if validation fails.

The patch backfills:

- canonical Operator Console bindings as `OPERATOR_CONSOLE` devices with their existing service identity, Site, Site Group, and credential version;
- canonical `DEVICE` service identities with a `PAYMENT_DEVICE` assignment as `ASSISTED_PAYMENT_TERMINAL` devices.

No secret or raw credential is copied into the new device catalog. Existing runtime credentials continue only until an administrator deliberately rotates or replaces the device.

## Existing PITX Operator Console

1. Confirm the backfilled device, service identity, Site, and Site Group in Management Platform → Device Administration.
2. Choose **Rotate credentials**. Rotation invalidates the prior runtime proof immediately and shows one enrollment package once.
3. On the intended PITX workstation, paste the complete package into **Governed workstation enrollment package**.
4. Operator Console sends the package to Central PMS, receives the new browser proof in memory, and immediately exchanges it for the existing HttpOnly device-binding cookie. It does not persist the package or runtime proof in web storage.
5. Sign in with the human operator account. Verify the session binds to the Site/Site Group and RBAC context. An Operator Console shift is not required in v1.3.
6. If enrollment or cookie exchange fails after token consumption, do not reuse the package. Rotate again from Management Platform.

## Existing PITX APT

1. Confirm the backfilled `ASSISTED_PAYMENT_TERMINAL` device and canonical `PAYMENT_DEVICE` Site assignment. If no canonical identity exists, register the APT and its terminal identifier in Management Platform.
2. Choose **Rotate credentials** (or **Provision device** for a new record) and copy the once-only package into a temporary access-controlled file on the intended APT.
3. From the `ExitPass-AssistedPaymentTerminal` repository, enroll through the governed APT launcher:

   ```powershell
   .\scripts\Start-AptPitxLocal.ps1 `
     -ProvisionOnly `
     -CentralPmsUrl https://central-pms.example `
     -ProvisioningPackagePath C:\ExitPass\staging\apt-enrollment.json
   ```

4. The APT creates a non-exportable client-authentication key/certificate in the deployment account's `CurrentUser\My` store, presents that certificate during enrollment, saves only DPAPI CurrentUser-protected governed state, and deletes the consumed package. Central PMS stores only the SHA-256 certificate fingerprint/reference and activates the canonical service identity plus `PAYMENT_DEVICE` assignment.
5. Start the APT with `Start-AptPitxLocal.ps1`. The launcher derives the service identity, terminal, Site, Site Group, credential version, and exact certificate reference from enrolled state and refuses legacy identity configuration. Verify cashier login independently; existing shift ownership, cash custody, and pre-`CASH_RECEIVED` revalidation remain unchanged.
6. A consumed, expired, cancelled, wrong-type, wrong-Site, or wrong-terminal package must not be retried. Request rotation from a System Administrator.

## Lifecycle operations

- **Cancel provisioning** invalidates the outstanding once-only package. Initial enrollment returns to `REGISTERED`; a cancelled credential rotation remains `SUSPENDED` because the previous runtime credential was already invalidated.
- **Suspend** disables the canonical service identity and assignment while preserving history. Eligible reactivation requires a current credential and valid Site assignment.
- **Revoke** is terminal and cancels pending material. Return to service requires replacement or a new identity.
- **Rotate** first invalidates the current runtime credential/version, then issues fresh once-only material.
- **Replace** marks the old identity `REPLACED`, invalidates its runtime trust, preserves lineage and Site scope by default, and creates a fresh successor identity and package.

Every operation records actor, action, target reference, before/after state, Site/Site Group, credential versions, timestamp, correlation, and reason without secrets.

## Fail-closed verification

Before production enablement, prove that replay, expiry, cancellation, mismatched device context, suspended/revoked/replaced identities, and stale credential versions all fail. Confirm list/details and audit output contain no provisioning token, token hash, browser proof, certificate fingerprint, private key, or raw credential.
