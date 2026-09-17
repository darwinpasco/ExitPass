# Human Authentication Current State and Gap Assessment

## Current state before this workstream

- Local passwords use Argon2id and TOTP secrets use an AES GCM protected envelope.
- Sessions carry an application audience plus credential and authorization version snapshots.
- Password replacement increments the credential version and revokes active sessions.
- `CHANGE_REQUIRED` sessions suppress permissions and scopes.
- Management Platform TOTP was conditional on a privileged role.
- Operator Console and APT logins used username and password without TOTP.
- Account activation and forgotten password recovery used emailed or administrator issued bearer links.
- TOTP enrollment was self service, only for privileged Management Platform users.
- Local credentials had no temporary password expiry timestamp.

## Gaps against the approved v1.3 policy

- Management Platform assurance was role based instead of audience based.
- Password mutation from operational sessions did not require TOTP.
- Reset and activation paths did not authenticate with TOTP.
- The email reset route and bearer reset contract conflicted with the approved recovery policy.
- Temporary passwords did not expire after 72 hours.
- Account creation did not atomically generate a temporary password and TOTP authenticator for every human.
- Native Parking App was missing from the audience vocabulary.

## Required H1 contract

H2 requires an application audience authorization decision with this shape:

`IsUserAuthorizedForAudience(userReference, audience, evaluatedAt) -> allowed/denied`

The decision must be supplied by H1's identity and RBAC boundary. Authentication must fail closed on denial or unavailable policy data. H2 does not infer audience access from role names.

### Exact integration seam

When H1 supplies its abstraction, H2 will invoke it from `HumanAuthenticationService` immediately before every normal application session issuance:

- initial login, before `IssueSessionAsync` creates a normal session;
- session continuation, before `RotateSessionAsync` creates a replacement normal session;
- fresh reauthentication, before `RotateSessionAsync` creates a replacement normal session.

An `ALLOWED` decision permits session issuance to continue. `DENIED`, unavailable, error, or unknown results deny issuance. Password-change-required bootstrap sessions do not become normal sessions at this seam: they retain empty permissions and scopes, remain blocked from business routes, and password replacement revokes them without issuing a replacement session. H2 will not derive application eligibility from role names.

## Native Parking App password lifecycle

- Native Parking App uses the audience-neutral web route `POST /v1/human-authentication/password/change`. The secure web session cookie and antiforgery token authenticate the request.
- An unexpired temporary password creates a `NATIVE_PARKING_APP` session with `PASSWORD_CHANGE_REQUIRED` assurance and empty permissions, Site scopes, and Site Group scopes. The global restricted-session guard denies Native Parking business routes while that state is present.
- The shared password-change service validates the current temporary or permanent password, an active TOTP authenticator and code, and the new password. It accepts both bootstrap and active Native Parking sessions without deriving eligibility from role names.
- Successful password replacement advances the credential version and revokes every active session for the user. The endpoint deletes the web session cookie and returns no replacement credential, so the user must log in again with the new password.
- Normal Native Parking login uses username and password without TOTP. TOTP remains mandatory for later password changes and username-based password recovery. Active recovery requires username, TOTP, and a new password; expired bootstrap recovery additionally requires the expired temporary password.
- Central PMS currently has no Native Parking device or Site binding in the human-session contract. The generic web route therefore preserves the existing model and does not accept caller-supplied device context. H2 has not introduced a device-binding design; any authoritative Native Parking trust or audience-eligibility contract must be supplied by its owning workstream and H1.
