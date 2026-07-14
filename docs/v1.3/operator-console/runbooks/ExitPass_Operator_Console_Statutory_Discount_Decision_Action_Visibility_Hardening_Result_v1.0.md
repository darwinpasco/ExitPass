# ExitPass Operator Console Statutory Discount Decision Action Visibility Hardening Result v1.0

## Result

PASSED.

The Operator Console statutory discount detail view now hides active Approve/Reject controls when the current operator cannot decide the validation. Backend RBAC and requester-vs-approver segregation remain authoritative; this slice only hardens the visible UI controls and read-only messaging.

## Issue Fixed

The aligned-DB UAT proved the requester profile could not approve through backend authorization, but the browser still rendered Decision actions controls before backend denial. The UI now avoids offering those controls when the user or validation state is not eligible.

## Requester Behavior

When the current operator is the requester/creator of the statutory discount validation:

- Decision status remains visible.
- Approve/Reject buttons are not rendered.
- The UI shows: `You cannot approve or reject your own statutory discount request.`
- No decision API call is reachable through visible controls.

## Reviewer Behavior

When the current operator is a different authorized reviewer and the validation is decision-eligible:

- Approve and Reject controls are visible and enabled.
- Evidence/readiness guardrails still disable actions when applicable.
- Backend RBAC and SoD remain final authority.

## No-Permission Behavior

When the current operator is not the requester but lacks statutory discount decision permissions:

- Approve/Reject buttons are not rendered.
- The UI shows: `Decision requires an authorized reviewer.`
- No decision API call is reachable through visible controls.

## Completed-State Behavior

When the validation is already approved/rejected or payable basis has already been applied:

- Decision actions are read-only.
- Approve/Reject buttons are not rendered for the completed decision state.
- Apply-payable-basis behavior is unchanged.

## Implementation Notes

- Added optional Operator Console API-client capability checks for statutory discount approve/reject permissions.
- The HTTP client derives those checks from `VITE_OPERATOR_CONSOLE_PERMISSIONS`.
- The mock client defaults to authorized reviewer behavior to preserve existing reviewer-path tests, with test options for no-permission cases.
- The detail view derives read-only decision state from current operator identity, requester identity, decision permissions, validation status, and payable-basis application status.

## Safety Boundaries

This slice did not change backend RBAC/SoD, statutory discount computation, payable-basis application logic, fiscal issuance behavior, payment behavior, gate behavior, POS Server integration, database objects, or UAT identity switching.

## Validation

```text
npm.cmd --prefix src\Services\OperatorConsoleUi test -- App.test.tsx
Result: PASSED, 85 tests

npm.cmd --prefix src\Services\OperatorConsoleUi run build
Result: PASSED
```

## Manual Browser Recommendation

Manual browser smoke is recommended:

1. Requester profile opens the draft after evidence capture and should not see active Approve/Reject controls.
2. Reviewer profile opens an eligible draft and should see Approve/Reject controls.
3. After approval/apply, decision controls should remain read-only.
4. Final payable should remain PHP 89.29 for the aligned UAT fixture.

## Files Changed

- `src/Services/OperatorConsoleUi/src/App.tsx`
- `src/Services/OperatorConsoleUi/src/apiClient.ts`
- `src/Services/OperatorConsoleUi/src/App.test.tsx`
- `docs/v1.3/operator-console/runbooks/ExitPass_Operator_Console_Statutory_Discount_Decision_Action_Visibility_Hardening_Result_v1.0.md`
