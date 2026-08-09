# ExitPass I-022 Browser and Windows Headed Validation Runbook v1.0

## Preconditions

1. Rebuild an `exitpass_i022_` disposable PostgreSQL 16 database from `build/generated/exitpass-full-object.generated.sql`.
2. Seed only synthetic users with `ExitPass.I022.ProofSeed`; provide its password through a process environment variable and do not log it.
3. Run Central PMS with `ASPNETCORE_ENVIRONMENT=Production`, fixture headers disabled, RBAC enabled, loopback binding, and explicit allowed web origins.
4. Run consumer source copies on loopback with `/v1` proxied to that Central PMS host.
5. Because Production authentication cookies and antiforgery cookies are Secure, host Central PMS with TLS. A runtime-only self-signed certificate may be accepted only by the disposable loopback proxies and must be deleted after proof.

## Management Platform

1. Open `/management-platform/` and confirm the login screen, not a manual scenario.
2. Sign in with the synthetic ordinary account. Confirm username/display name and Site/Site Group scope derive from current session.
3. Confirm Identity Administration availability follows server permissions.
4. Submit one safe read and confirm no identity, permission, Site, or Site Group authority header is sent.
5. Sign out and confirm a protected view returns to login without password replay.
6. For privileged coverage, use automated TOTP tests; no TOTP seed is retained in this runbook.

The automated headed flow is `scripts/i022/I022HostedBrowserProof.mjs`. Supply the synthetic username/password and both loopback consumer URLs through process environment variables. Copy it into the disposable Management Platform source copy so it resolves that copy's Playwright dependency; do not place credentials in the script or command output.

## Operator Console

1. Open `/operator-console`, sign in with the synthetic account, and confirm no TOTP prompt.
2. Confirm displayed reviewer identity and scope match current session.
3. Confirm evidence/review commands remain permission and scope guarded and payable application is unavailable.
4. Sign out and confirm review routes fail closed.

## Windows APT

Run `scripts\Invoke-AptHumanSessionProof.ps1` from a disposable copy of the merged APT repository. The run must launch the real packaged-assets WebView2 host and return `APT human-session proof passed.` Verify the native credential boundary, canonical four permissions, device binding, own shift/custody, revocation/expiry, outage fail-closed behavior, and no password/token authority in web content.

## Negative checks

Use a Production API request with `X-ExitPass-User-Id` or `X-ExitPass-Permissions`; expect `400 FIXTURE_IDENTITY_HEADER_PROHIBITED`. Revoke the session and retry without re-entering credentials; expect no authentication and no automatic password replay.

Delete the database, processes, listeners, temporary consumer copies, credentials, logs, and test output after observation.
