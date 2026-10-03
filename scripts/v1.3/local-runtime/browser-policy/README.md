# Operator Console managed Chrome policy

The shared Operator Console terminal uses Google Chrome with mandatory machine-level policy. The application disables credential autocomplete and keeps credentials only in transient component state, but browser policy is required to suppress Chrome password saving, autofill, synchronization, account sign-in, and unapproved extensions.

## Supported shared-terminal deployments

### `WINDOWS_SHARED_OPERATOR_CONSOLE`

Supported when all of the following are true:

- Chrome is enterprise managed at machine scope.
- `PasswordManagerEnabled=false`.
- `SyncDisabled=true`.
- `BrowserSignin=0`.
- Arbitrary extensions are blocked.
- Application-level credential non-persistence passes.
- Shared-terminal human acceptance passes.

### `ANDROID_SHARED_OPERATOR_CONSOLE`

Not production-supported until all of the following are true:

- An Android Enterprise EMM product is selected by the organization.
- An organization-owned device is cleanly enrolled as a dedicated or fully managed device.
- Managed Chrome is deployed without a personal Google account.
- Chrome password saving, browser sign-in, and synchronization are disabled.
- Android Autofill is disabled.
- Android Credential Manager providers are disabled or disallowed.
- Application installation is restricted and third-party password managers are prohibited.
- Shared-terminal human acceptance passes.

**EMM PRODUCT: TBD / organization decision required.**

No vendor-specific Android EMM configuration is supplied by this repository until that decision is made. Do not use a personal Google account or convert an existing personal Chrome profile into the production shared-terminal profile. Start with a clean managed device/profile or complete administrator-controlled credential remediation before enrollment and production use.

### `UNMANAGED_ANDROID_CHROME`

Unsupported for shared Operator Console use. HTML autocomplete controls cannot prevent an unmanaged personal Chrome profile, Google Password Manager, Android Autofill, Credential Manager, or a third-party password manager from offering a prior operator's credentials.

The application controls remain common across supported platforms: credential fields disable autocomplete, credential values remain transient, password state is cleared after authentication success or failure, username and password state are cleared on logout/session expiry/reset, no Remember Me control is provided, and the secure session-cookie architecture is unchanged.

## Deploy

1. Use a dedicated Operator Console Windows workstation and a dedicated Chrome profile. Do not apply these scripts to an unrelated personal workstation or profile.
2. Open an elevated PowerShell prompt from the repository root.
3. Preview the policy writes:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\scripts\v1.3\local-runtime\browser-policy\Install-ChromeOperatorConsolePolicy.ps1 -WhatIf
   ```

4. Apply the policy:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\scripts\v1.3\local-runtime\browser-policy\Install-ChromeOperatorConsolePolicy.ps1 -Confirm:$false
   ```

5. Close every Chrome window and background Chrome process, then restart Chrome.
6. Open `chrome://policy`, reload policies, and confirm that each policy is machine-scoped and mandatory:

   | Policy | Required value |
   | --- | --- |
   | `PasswordManagerEnabled` | `false` |
   | `SyncDisabled` | `true` |
   | `ExtensionInstallBlocklist` | `["*"]` |
   | `BrowserSignin` | `0` |

7. Run the read-only validator:

   ```powershell
   powershell -ExecutionPolicy Bypass -File .\scripts\v1.3\local-runtime\browser-policy\Test-ChromeOperatorConsolePolicy.ps1
   ```

The installer is idempotent and only writes the four approved Chrome policy values under `HKLM:\SOFTWARE\Policies\Google\Chrome`. It does not run from Vite or `Start-OperatorConsole.ps1`, and it does not read or modify browser profiles or password databases.

## Existing credentials and extensions

`PasswordManagerEnabled=false` prevents new Chrome password-manager use but does not remove credentials that were saved previously. Before the terminal enters service, use either:

- a new clean dedicated Chrome profile with no historical credentials; or
- administrator-controlled remediation of previously saved Operator Console credentials.

Do not use repository automation to inspect, export, or delete credentials from arbitrary or personal Chrome profiles.

`ExtensionInstallBlocklist=["*"]` blocks unapproved extensions. After policy refresh, inspect `chrome://extensions` or approved enterprise-management metadata and confirm that no third-party password-manager extension is enabled. Do not inspect extension private data and do not silently uninstall extensions. Any existing enabled password-manager extension blocks acceptance until an administrator resolves it.

## Human acceptance

1. Confirm the validator reports `PASS` and `chrome://policy` shows all four policies as mandatory machine policy.
2. Confirm the dedicated profile is clean or historical Operator Console credentials were administratively remediated.
3. Open the Operator Console. Username and password must be blank.
4. Sign in with an operator account. Chrome must not offer to save the password.
5. Sign out. Username and password must again be blank.
6. Close all Chrome windows, reopen Chrome, and revisit the Operator Console.
7. Confirm there is no username/password autofill, saved-password suggestion, or prior-user credential.
8. Sign in with a different operator account and confirm the previous operator's credentials are not exposed.

Human acceptance fails if Chrome offers to save a password, autofills credentials, suggests a prior operator, synchronizes credentials, or permits an unapproved password-manager extension.

## Android Enterprise acceptance gate

After an EMM is selected, configure the platform through that authoritative EMM rather than adding browser-specific application workarounds. Acceptance must verify the effective device posture, not only the presence of a Chrome setting:

1. Begin with a clean organization-owned Android Enterprise dedicated or fully managed device/profile.
2. Confirm managed Chrome is installed through the selected EMM and no personal Google account is active.
3. Confirm Chrome password saving, synchronization, and browser sign-in are disabled.
4. Confirm Android Autofill and Credential Manager providers are disabled or disallowed.
5. Confirm application installation is restricted and no third-party password manager is available.
6. Open the Operator Console and confirm the login form is blank.
7. Sign in as Operator A and confirm no password-save prompt appears.
8. Sign out and confirm the login form is blank.
9. Close and reopen Chrome; confirm no username, password, Google Password Manager suggestion, or third-party credential suggestion appears.
10. Sign in as Operator B and confirm Operator A's credentials are not visible or available.

Android shared-terminal acceptance fails if any password-save or credential-autofill interface appears. Until the EMM is selected, configured, and this acceptance procedure passes, Android status remains `PENDING EMM SELECTION AND DEPLOYMENT`; unmanaged Android Chrome remains `FAIL / unsupported deployment`.
