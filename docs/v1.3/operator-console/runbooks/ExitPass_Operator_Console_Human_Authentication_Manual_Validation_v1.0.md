# Operator Console Human Authentication Manual Validation v1.0

## Setup

Use synthetic fixture data only. The harness owns and stops only the Node process it starts.

```powershell
cd D:\wt\H008\src\Services\OperatorConsoleUi
npm.cmd ci
npx.cmd playwright install chromium
npm.cmd run build
npm.cmd run test:browser-smoke:server
```

Keep the server command open. Use `http://127.0.0.1:5197` unless `OPERATOR_CONSOLE_ORDINANCE_BROWSER_SMOKE_PORT` overrides it.

## Walkthrough

1. Open `http://127.0.0.1:5197/operator-console?auth=logged-out`.
2. Verify username receives initial focus, password follows by Tab, no TOTP control appears, and invalid credentials show one anti-enumerating message.
3. Sign in with synthetic credentials `review.operator` / `operator-password`.
4. Verify the header shows `Review Operator`, `review.operator`, and the server-returned Site/Site Group scope count.
5. Refresh the browser and verify the workspace is rediscovered without another login request.
6. Open `http://127.0.0.1:5197/operator-console/statutory-discounts/senior-representative-optional`; approve or reject and verify no apply/payable-basis action appears.
7. Open `http://127.0.0.1:5197/operator-console/statutory-discounts/evidence-eligible-png`; verify secure preview still works and remains separate from decisions.
8. Open the evidence permission and cross-scope fixture URLs listed by the server; verify safe denial and no false logout for `403`.
9. Sign out and verify the protected workspace disappears. Refresh and verify it remains signed out.
10. Open the `auth=expired` and `auth=revoked` URLs and verify each returns to sign in with a controlled message.
11. Repeat login at 390 x 844 using keyboard only. Verify no horizontal page overflow and that review actions remain reachable.

## Browser proof

In Network inspection verify:

- session/login/logout use same-origin `/v1/human-authentication/*` routes;
- the session is cookie-mediated;
- logout sends `X-CSRF-Token`;
- no `Authorization`, `X-Operator-User-Id`, `X-ExitPass-User-Id`, `X-ExitPass-Permissions`, `X-Site-Id`, or `X-Site-Group-Id` authority header is sent;
- decision JSON has no `userId` or `reviewerUserId`;
- no Operator Console statutory payable-application request occurs.

In Application inspection verify localStorage, sessionStorage, IndexedDB, and Cache Storage contain no authentication, permission, scope, password, TOTP, session-token, or CSRF authority.

## Cleanup

Press Enter in the server terminal. Confirm port 5197 is no longer listening. Generated `.local/operator-console-ordinance-browser-smoke` evidence is ignored and may be removed after review without using `git clean`.

Manual result remains pending until Darwin records the direct headed-browser observations.
