import { expect, test, type Locator, type Page, type Request } from "@playwright/test";

const route = "/management-platform/sales-invoice-profiles";
const forbiddenConsoleTokens = [
  "DEV-TIN",
  "Managed Development Address",
  "DEV-BIR",
  "DEV-PTU",
  "mutation payload",
  "Registered Business object",
  "Sales Invoice Setup object",
  "token",
  "API key",
  "raw claims",
  "authorization",
  "raw downstream error body"
];
const forbiddenControlNames = /Activate|Retire|Delete|Reactivate|Create New Version/i;
const consoleMessages = new WeakMap<Page, string[]>();

test.beforeEach(async ({ page }) => {
  const messages: string[] = [];
  consoleMessages.set(page, messages);
  page.on("console", (message) => messages.push(message.text()));
});

test.afterEach(async ({ page }) => {
  const combined = (consoleMessages.get(page) ?? []).join("\n");
  for (const forbidden of forbiddenConsoleTokens) {
    expect(combined).not.toContain(forbidden);
  }
});

test.describe("Management Platform Sales Invoice Profile Manage UI E2E", () => {
  test("read-only permission posture preserves read surfaces and hides mutation controls", async ({ page }) => {
    await gotoScenario(page, "read-only");
    await expect(page.getByRole("button", { name: "2026.01" })).toBeVisible();
    await page.getByRole("button", { name: "2026.01" }).click();
    await expect(page.getByRole("button", { name: "Validate configuration" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Sales Invoice readiness" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Issuance history" })).toBeVisible();
    await expect(page.getByRole("button", { name: "Create Registered Business" })).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Edit Registered Business" })).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Create Draft Sales Invoice Setup" })).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Edit Draft Sales Invoice Setup" })).toHaveCount(0);
    await expect(page.getByRole("button", { name: forbiddenControlNames })).toHaveCount(0);
  });

  test("manage permission exposes create controls, DRAFT edit, and no approval or retirement controls", async ({ page }) => {
    await gotoScenario(page, "manage");
    await expect(page.getByRole("button", { name: "Create Registered Business" })).toBeVisible();
    await expect(page.getByRole("button", { name: "Create Draft Sales Invoice Setup" })).toBeVisible();
    await page.getByRole("button", { name: "2026.01" }).click();
    await expect(page.getByRole("button", { name: "Edit Draft Sales Invoice Setup" })).toBeVisible();
    await expect(page.getByRole("button", { name: forbiddenControlNames })).toHaveCount(0);

    await gotoScenario(page, "approved-read-only");
    await page.getByRole("button", { name: "2026.01" }).click();
    await expect(page.getByRole("status", { name: "Active setup is read-only" })).toBeVisible();
    await expect(page.getByRole("button", { name: "Edit Draft Sales Invoice Setup" })).toHaveCount(0);

    await gotoScenario(page, "retired-read-only");
    await page.getByRole("button", { name: "2025.12" }).click();
    await expect(page.getByRole("status", { name: "Retired setup is read-only" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Issuance history" })).toBeVisible();
    await expect(page.getByRole("button", { name: "Edit Draft Sales Invoice Setup" })).toHaveCount(0);
  });

  test("Registered Business form shape, success, conflict, update, and immutable conflict are safe", async ({ page }) => {
    await gotoScenario(page, "fiscal-identity-create-success");
    await page.getByRole("button", { name: "Create Registered Business" }).click();
    const createForm = page.getByRole("form", { name: "Create Registered Business" });
    await expect(createForm.getByLabel("Registered business name *")).toBeVisible();
    await expect(createForm.getByLabel("Registered business address *")).toBeVisible();
    await expect(createForm.getByLabel("TIN *")).toBeVisible();
    await expect(createForm.getByLabel("Taxpayer/VAT registration posture *")).toBeVisible();
    await expect(createForm).not.toContainText(/createdByRef|updatedByRef|approvedByRef|retiredByRef|actor ID|Terminal ID|POS Server API key|POS Server URL/i);
    await fillFiscalIdentity(createForm);
    await createForm.getByRole("button", { name: "Create Registered Business" }).dblclick();
    await expect(page.getByRole("status", { name: "Development mutation attempts" })).toContainText("1");
    await expect(page.getByRole("status", { name: "Registered business created" })).toContainText("Authoritative Sales Invoice Configuration state has been refreshed.");
    await expect(page.locator("#fiscal-result-title")).toHaveText("Registered business created");
    await expect(page.getByText("fiscal-dev-identity-created")).toBeVisible();
    await expect(page.getByText("2026-07-20T05:00:00Z").first()).toBeVisible();
    await page.getByRole("button", { name: "Create Draft Sales Invoice Setup" }).click();
    await expect(page.getByRole("form", { name: "Create Draft Sales Invoice Setup" }).getByLabel("Registered Business ID *")).toHaveValue("fiscal-dev-identity-created");

    await gotoScenario(page, "fiscal-identity-create-conflict");
    await page.getByRole("button", { name: "Create Registered Business" }).click();
    const conflictForm = page.getByRole("form", { name: "Create Registered Business" });
    await fillFiscalIdentity(conflictForm);
    await conflictForm.getByRole("button", { name: "Create Registered Business" }).click();
    await expect(page.getByRole("alert", { name: "Changes failed safely" })).toContainText("dev-fiscal-create-conflict");
    await expect(conflictForm.getByLabel("Registered business name *")).toHaveValue("Managed Development Parking");
    await expect(page.getByRole("status", { name: "Development mutation attempts" })).toContainText("1");
    await expect(page.getByText("fiscal-dev-identity-created")).toHaveCount(0);

    await gotoScenario(page, "fiscal-identity-update-success");
    await page.getByRole("button", { name: "2026.01" }).click();
    await page.getByRole("button", { name: "Edit Registered Business" }).click();
    const updateForm = page.getByRole("form", { name: "Save Registered Business" });
    await expect(updateForm).not.toContainText(/createdByRef|updatedByRef|actor ID/i);
    await updateForm.getByLabel("Registered business name *").fill("Updated Development Parking");
    await updateForm.getByRole("button", { name: "Save Registered Business" }).click();
    await expect(page.getByRole("status", { name: "Changes saved" })).toContainText("Authoritative Sales Invoice Configuration state has been refreshed.");
    await expect(page.getByText("2026-07-20T05:10:00Z")).toBeVisible();
    await expect(page.getByRole("status", { name: "Development mutation attempts" })).toContainText("1");

    await gotoScenario(page, "fiscal-identity-update-immutable");
    await page.getByRole("button", { name: "2026.01" }).click();
    await page.getByRole("button", { name: "Edit Registered Business" }).click();
    const immutableForm = page.getByRole("form", { name: "Save Registered Business" });
    await immutableForm.getByLabel("Registered business name *").fill("Immutable Unsaved Name");
    await immutableForm.getByRole("button", { name: "Save Registered Business" }).click();
    await expect(page.getByRole("alert", { name: "Changes failed safely" })).toContainText("dev-fiscal-update-conflict");
    await expect(immutableForm.getByLabel("Registered business name *")).toHaveValue("Immutable Unsaved Name");
    await expect(page.getByRole("status", { name: "Development mutation attempts" })).toContainText("1");
  });

  test("Draft Sales Invoice Setup create form, controlled versions, Site scope, success, conflict, and timeout", async ({ page }) => {
    await gotoScenario(page, "profile-create-success");
    await page.getByRole("button", { name: "Create Draft Sales Invoice Setup" }).click();
    const form = page.getByRole("form", { name: "Create Draft Sales Invoice Setup" });
    for (const heading of ["Registered Business and scope", "Supported template versions", "Device registration", "Parking-location display", "BIR accreditation", "PTU", "Sales Invoice wording", "Effective period"]) {
      await expect(form.getByText(heading, { exact: true })).toBeVisible();
    }
    for (const label of ["Registered Business ID *", "Setup version *", "Template version", "Presentation version", "POS serial number *", "Machine Identification Number *", "Parking-location display *", "BIR accreditation number *", "BIR accreditation date issued *", "BIR accreditation valid until *", "PTU number *", "PTU date issued *", "Sales Invoice legal statement *", "Customer-service footer", "Effective from *", "Effective to"]) {
      await expect(form.getByLabel(label)).toBeVisible();
    }
    await expect(form).not.toContainText(/terminalId|createdByRef|updatedByRef|approvedByRef|retiredByRef|APPROVED|RETIRED/i);
    await expect(form.getByLabel("Template version")).toHaveValue("digital-sales-invoice-json-v1");
    await expect(form.getByLabel("Presentation version")).toHaveValue("digital-sales-invoice-presentation-json-v1");
    await expect(form.getByLabel("Site ID")).toHaveValue("71000000-0000-0000-0000-000000000101");
    await expect(form.getByLabel("Site POS Server ID")).toHaveValue("72000000-0000-0000-0000-000000000101");
    expect(await form.getByLabel("Template version").evaluate((element) => element.tagName)).toBe("SELECT");
    expect(await form.getByLabel("Presentation version").evaluate((element) => element.tagName)).toBe("SELECT");
    await fillProfile(form);
    await form.getByRole("button", { name: "Create Draft Sales Invoice Setup" }).dblclick();
    await expect(page.getByRole("status", { name: "Development mutation attempts" })).toContainText("1");
    await expect(page.getByRole("status", { name: "Draft Sales Invoice Setup created" })).toContainText("Authoritative Sales Invoice Configuration state has been refreshed.");
    await expect(page.getByRole("heading", { name: "Sales Invoice Setup details" })).toBeVisible();
    await expect(page.getByText("Draft").first()).toBeVisible();
    await expect(page.getByRole("button", { name: forbiddenControlNames })).toHaveCount(0);

    await gotoScenario(page, "profile-create-conflict");
    await page.getByRole("button", { name: "Create Draft Sales Invoice Setup" }).click();
    const conflictForm = page.getByRole("form", { name: "Create Draft Sales Invoice Setup" });
    await fillProfile(conflictForm);
    await conflictForm.getByRole("button", { name: "Create Draft Sales Invoice Setup" }).click();
    await expect(page.getByRole("alert", { name: "Changes failed safely" })).toContainText("dev-profile-create-conflict");
    await expect(conflictForm.getByLabel("Setup version *")).toHaveValue("2026.02");
    await expect(page.getByRole("status", { name: "Development mutation attempts" })).toContainText("1");
    await expect(page.getByText("2026.03")).toHaveCount(0);

    await gotoScenario(page, "profile-create-timeout");
    await page.getByRole("button", { name: "Create Draft Sales Invoice Setup" }).click();
    const timeoutForm = page.getByRole("form", { name: "Create Draft Sales Invoice Setup" });
    await fillProfile(timeoutForm);
    await timeoutForm.getByRole("button", { name: "Create Draft Sales Invoice Setup" }).click();
    await expect(page.getByRole("status", { name: "Result uncertain" })).toContainText("Refresh and verify");
    await expect(page.getByRole("status", { name: "Result uncertain" })).toContainText("dev-profile-create-timeout");
    await expect(page.getByRole("status", { name: "Development mutation attempts" })).toContainText("1");
    await expect(page.getByRole("status", { name: "Draft Sales Invoice Setup created" })).toHaveCount(0);
  });

  test("DRAFT edit success and conflict keep lifecycle controlled and request count bounded", async ({ page }) => {
    await gotoScenario(page, "draft-edit-success");
    await page.getByRole("button", { name: "2026.01" }).click();
    await page.getByRole("button", { name: "Edit Draft Sales Invoice Setup" }).click();
    const form = page.getByRole("form", { name: "Save Draft Changes" });
    await expect(form).not.toContainText("Sales Invoice Setup ID");
    await expect(form).not.toContainText("Status");
    await expect(form.getByLabel("Site ID")).toHaveAttribute("readonly", "");
    await form.getByLabel("Parking-location display *").fill("Updated Development Parking");
    await form.getByRole("button", { name: "Save Draft Changes" }).click();
    await expect(page.getByRole("status", { name: "Changes saved" })).toContainText("Authoritative Sales Invoice Configuration state has been refreshed.");
    await expect(page.getByRole("status", { name: "Development mutation attempts" })).toContainText("1");
    await expect(page.getByText("Draft").first()).toBeVisible();

    await gotoScenario(page, "draft-edit-conflict");
    await page.getByRole("button", { name: "2026.01" }).click();
    await page.getByRole("button", { name: "Edit Draft Sales Invoice Setup" }).click();
    const conflictForm = page.getByRole("form", { name: "Save Draft Changes" });
    await conflictForm.getByLabel("Parking-location display *").fill("Conflict Development Parking");
    await conflictForm.getByRole("button", { name: "Save Draft Changes" }).click();
    await expect(page.getByRole("alert", { name: "Changes failed safely" })).toContainText("dev-draft-edit-conflict");
    await expect(conflictForm.getByLabel("Parking-location display *")).toHaveValue("Conflict Development Parking");
    await expect(page.getByRole("status", { name: "Development mutation attempts" })).toContainText("1");
  });

  test("safe disabled, forbidden, and unavailable manage postures are displayed", async ({ page }) => {
    await gotoScenario(page, "forbidden-manage");
    await expect(page.getByRole("alert", { name: "Sales Invoice Setups unavailable" })).toContainText("permission");
    await expect(page.getByRole("form")).toHaveCount(0);

    await gotoScenario(page, "disabled-manage");
    await expect(page.getByRole("alert", { name: "Sales Invoice Setups unavailable" })).toContainText("not enabled");
    await expect(page.getByText(/https?:\/\//i)).toHaveCount(0);
    await expect(page.getByText(/API key|X-PosServer/i)).toHaveCount(0);

    await gotoScenario(page, "unavailable-manage");
    await expect(page.getByRole("alert", { name: "Sales Invoice Setups unavailable" })).toContainText("dev-unavailable-correlation");
    await expect(page.getByText(/stack trace|<html|internal exception/i)).toHaveCount(0);
  });

  test("cancel, unsaved Site switch confirmation, and pending mutation Site switch posture are safe", async ({ page }) => {
    await gotoScenario(page, "manage");
    await page.getByRole("button", { name: "Create Registered Business" }).click();
    const fiscalForm = page.getByRole("form", { name: "Create Registered Business" });
    await fiscalForm.getByLabel("Registered business name *").fill("Discard Me");
    await fiscalForm.getByRole("button", { name: "Cancel" }).click();
    await expect(page.getByRole("form", { name: "Create Registered Business" })).toHaveCount(0);
    await expect(page.getByRole("status", { name: "Development mutation attempts" })).toContainText("0");

    await page.getByRole("button", { name: "Create Draft Sales Invoice Setup" }).click();
    const profileForm = page.getByRole("form", { name: "Create Draft Sales Invoice Setup" });
    await profileForm.getByLabel("Setup version *").fill("Discard Me");
    await profileForm.getByRole("button", { name: "Cancel" }).click();
    await expect(page.getByRole("form", { name: "Create Draft Sales Invoice Setup" })).toHaveCount(0);
    await expect(page.getByRole("status", { name: "Development mutation attempts" })).toContainText("0");

    await gotoScenario(page, "manage", "multi-site");
    await page.getByRole("button", { name: "Create Registered Business" }).click();
    await page.getByRole("form", { name: "Create Registered Business" }).getByLabel("Registered business name *").fill("Unsaved Site Switch");
    page.once("dialog", async (dialog) => {
      expect(dialog.message()).toContain("Discard unsaved");
      await dialog.dismiss();
    });
    await page.getByLabel("Current Site").selectOption("71000000-0000-0000-0000-000000000102");
    await expect(page.getByLabel("Current Site")).toHaveValue("71000000-0000-0000-0000-000000000101");
    await expect(page.getByRole("form", { name: "Create Registered Business" })).toBeVisible();
    page.once("dialog", async (dialog) => dialog.accept());
    await page.getByLabel("Current Site").selectOption("71000000-0000-0000-0000-000000000102");
    await expect(page.getByLabel("Current Site")).toHaveValue("71000000-0000-0000-0000-000000000102");
    await expect(page.getByRole("form", { name: "Create Registered Business" })).toHaveCount(0);
    await expect(page.getByText("Development Site Alpha").last()).not.toBeVisible();

    await gotoScenario(page, "profile-create-success", "multi-site", "&mpProfileDelayMs=500");
    await page.getByRole("button", { name: "Create Draft Sales Invoice Setup" }).click();
    const pendingForm = page.getByRole("form", { name: "Create Draft Sales Invoice Setup" });
    await fillProfile(pendingForm);
    await pendingForm.getByRole("button", { name: "Create Draft Sales Invoice Setup" }).click();
    await expect(page.getByRole("status", { name: "Development mutation attempts" })).toContainText("1");
  });

  test("double-submit prevention covers every mutation class", async ({ page }) => {
    await assertSingleAttempt(page, "fiscal-identity-create-success", "Create Registered Business", fillFiscalIdentity);
    await assertSingleAttempt(page, "profile-create-success", "Create Draft Sales Invoice Setup", fillProfile);

    await gotoScenario(page, "fiscal-identity-update-success");
    await page.getByRole("button", { name: "2026.01" }).click();
    await expect(page.getByRole("button", { name: "Edit Registered Business" })).toBeVisible();
    await page.getByRole("button", { name: "Edit Registered Business" }).click();
    const fiscalUpdateForm = page.getByRole("form", { name: "Save Registered Business" });
    await expect(fiscalUpdateForm).toBeVisible();
    await fiscalUpdateForm.getByLabel("Registered business name *").fill("Double Submit Fiscal Update");
    await fiscalUpdateForm.getByRole("button", { name: "Save Registered Business" }).dblclick();
    await expect(page.getByRole("status", { name: "Development mutation attempts" })).toContainText("1");

    await gotoScenario(page, "draft-edit-success");
    await page.getByRole("button", { name: "2026.01" }).click();
    await page.getByRole("button", { name: "Edit Draft Sales Invoice Setup" }).click();
    await page.getByRole("form", { name: "Save Draft Changes" }).getByLabel("Parking-location display *").fill("Double Submit Draft Update");
    await page.keyboard.press("Enter");
    await page.keyboard.press("Enter");
    await expect(page.getByRole("status", { name: "Development mutation attempts" })).toContainText("1");
  });

  test("responsive layout and keyboard accessibility remain usable", async ({ page }) => {
    for (const viewport of [{ width: 768, height: 900 }, { width: 1024, height: 768 }]) {
      await page.setViewportSize(viewport);
      await gotoScenario(page, "profile-create-success");
      await page.getByRole("button", { name: "Create Draft Sales Invoice Setup" }).click();
      const form = page.getByRole("form", { name: "Create Draft Sales Invoice Setup" });
      await form.getByRole("button", { name: "Create Draft Sales Invoice Setup" }).scrollIntoViewIfNeeded();
      await expect(form.getByRole("button", { name: "Create Draft Sales Invoice Setup" })).toBeInViewport();
      await expect(form.getByRole("button", { name: "Cancel" })).toBeInViewport();
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1)).toBe(true);
      await expect(form.getByLabel("BIR accreditation date issued *")).toBeVisible();
      await expect(form.getByLabel("PTU date issued *")).toBeVisible();
    }

    await page.setViewportSize({ width: 1366, height: 768 });
    await gotoScenario(page, "manage");
    await page.keyboard.press("Tab");
    await expect(page.getByRole("button", { name: "Overview" })).toBeFocused();
    await page.keyboard.press("Tab");
    await expect(page.getByRole("button", { name: /Sales Invoice Configuration/i })).toBeFocused();
    await page.keyboard.press("Tab");
    await expect(page.getByLabel("Current Site")).toBeFocused();
    await page.keyboard.press("Tab");
    await expect(page.getByRole("button", { name: "Create Registered Business" })).toBeFocused();
    await page.keyboard.press("Tab");
    await expect(page.getByRole("button", { name: "Create Draft Sales Invoice Setup" })).toBeFocused();
    await page.getByRole("button", { name: "Create Registered Business" }).click();
    await expect(page.getByRole("form", { name: "Create Registered Business" }).getByLabel("Registered business name *")).toBeVisible();
  });

  test("approve-authorized activation is validation-gated, single-submit, and user friendly", async ({ page }) => {
    await gotoScenario(page, "read-only");
    await page.getByRole("button", { name: "2026.01" }).click();
    await expect(page.getByRole("button", { name: "Activate Sales Invoice Setup" })).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Retire Sales Invoice Setup" })).toHaveCount(0);

    await gotoScenario(page, "manage-without-approve");
    await page.getByRole("button", { name: "2026.01" }).click();
    await expect(page.getByRole("button", { name: "Activate Sales Invoice Setup" })).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Retire Sales Invoice Setup" })).toHaveCount(0);

    await gotoScenario(page, "approve-draft-incomplete");
    await page.getByRole("button", { name: "2026.01" }).click();
    await page.getByRole("button", { name: "Validate configuration" }).click();
    await expect(page.getByRole("status", { name: "Validation result" })).toContainText("Configuration completeness: Incomplete");
    await expect(page.getByRole("button", { name: "Activate Sales Invoice Setup" })).toHaveCount(0);

    await gotoScenario(page, "approve-success");
    await page.getByRole("button", { name: "2026.01" }).click();
    await page.getByRole("button", { name: "Validate configuration" }).click();
    await page.getByRole("button", { name: "Activate Sales Invoice Setup" }).click();
    const dialog = page.getByRole("dialog", { name: "Activate Sales Invoice Setup?" });
    await expect(dialog).toContainText("Setup version");
    await expect(dialog).toContainText("Registered Business");
    await expect(dialog).toContainText("BIR accreditation valid until");
    await expect(dialog).not.toContainText(/approvedByRef|actor ID|POS Server API key/i);
    await dialog.getByRole("button", { name: "Activate Sales Invoice Setup" }).dblclick();
    await expect(page.getByRole("status", { name: "Development mutation attempts" })).toContainText("1");
    await expect(page.getByRole("status", { name: "Sales Invoice Setup activated" })).toBeVisible();
    await expect(page.getByText("Active").first()).toBeVisible();
    await expect(page.getByText("Mutation accepted")).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Edit Draft Sales Invoice Setup" })).toHaveCount(0);
  });

  test("activation conflict and timeout preserve Draft and do not retry", async ({ page }) => {
    await gotoScenario(page, "approve-conflict");
    await page.getByRole("button", { name: "2026.01" }).click();
    await page.getByRole("button", { name: "Validate configuration" }).click();
    await page.getByRole("button", { name: "Activate Sales Invoice Setup" }).click();
    await page.getByRole("dialog", { name: "Activate Sales Invoice Setup?" }).getByRole("button", { name: "Activate Sales Invoice Setup" }).click();
    await expect(page.getByRole("alert", { name: "Status change failed safely" })).toContainText("dev-approve-conflict");
    await expect(page.getByText("Draft").first()).toBeVisible();
    await expect(page.getByRole("status", { name: "Development mutation attempts" })).toContainText("1");

    await gotoScenario(page, "approve-timeout");
    await page.getByRole("button", { name: "2026.01" }).click();
    await page.getByRole("button", { name: "Validate configuration" }).click();
    await page.getByRole("button", { name: "Activate Sales Invoice Setup" }).click();
    await page.getByRole("dialog", { name: "Activate Sales Invoice Setup?" }).getByRole("button", { name: "Activate Sales Invoice Setup" }).click();
    await expect(page.getByRole("status", { name: "Result uncertain" })).toContainText("Refresh and verify");
    await expect(page.getByRole("status", { name: "Result uncertain" })).toContainText("dev-approve-timeout");
    await expect(page.getByText("Draft").first()).toBeVisible();
    await expect(page.getByRole("status", { name: "Development mutation attempts" })).toContainText("1");
  });

  test("approve-authorized retirement is single-submit, preserves history, and handles failures safely", async ({ page }) => {
    await gotoScenario(page, "retire-approved");
    await page.getByRole("button", { name: "2026.01" }).click();
    await expect(page.getByRole("button", { name: "Retire Sales Invoice Setup" })).toBeVisible();
    await expect(page.getByRole("button", { name: "Activate Sales Invoice Setup" })).toHaveCount(0);

    await gotoScenario(page, "retire-success");
    await page.getByRole("button", { name: "2026.01" }).click();
    await page.getByRole("button", { name: "Retire Sales Invoice Setup" }).click();
    const dialog = page.getByRole("dialog", { name: "Retire Sales Invoice Setup?" });
    await expect(dialog).toContainText("Historical Sales Invoices");
    await expect(dialog).toContainText("not deleted");
    await expect(dialog).not.toContainText(/retiredByRef|actor ID/i);
    await dialog.getByRole("button", { name: "Retire Sales Invoice Setup" }).dblclick();
    await expect(page.getByRole("status", { name: "Development mutation attempts" })).toContainText("1");
    await expect(page.getByRole("status", { name: "Sales Invoice Setup retired" })).toBeVisible();
    await expect(page.getByText("Retired").first()).toBeVisible();
    await expect(page.getByRole("heading", { name: "Issuance history" })).toBeVisible();

    await gotoScenario(page, "retire-conflict");
    await page.getByRole("button", { name: "2026.01" }).click();
    await page.getByRole("button", { name: "Retire Sales Invoice Setup" }).click();
    await page.getByRole("dialog", { name: "Retire Sales Invoice Setup?" }).getByRole("button", { name: "Retire Sales Invoice Setup" }).click();
    await expect(page.getByRole("alert", { name: "Status change failed safely" })).toContainText("dev-retire-conflict");
    await expect(page.getByText("Active").first()).toBeVisible();
    await expect(page.getByRole("status", { name: "Development mutation attempts" })).toContainText("1");

    await gotoScenario(page, "retire-timeout");
    await page.getByRole("button", { name: "2026.01" }).click();
    await page.getByRole("button", { name: "Retire Sales Invoice Setup" }).click();
    await page.getByRole("dialog", { name: "Retire Sales Invoice Setup?" }).getByRole("button", { name: "Retire Sales Invoice Setup" }).click();
    await expect(page.getByRole("status", { name: "Result uncertain" })).toContainText("Refresh and verify");
    await expect(page.getByRole("status", { name: "Result uncertain" })).toContainText("dev-retire-timeout");
    await expect(page.getByText("Active").first()).toBeVisible();
    await expect(page.getByRole("status", { name: "Development mutation attempts" })).toContainText("1");
  });

  test("pending lifecycle mutations block Site switching, dialogs are keyboard usable, and terminology is business friendly", async ({ page }) => {
    await page.setViewportSize({ width: 768, height: 900 });
    await gotoScenario(page, "approve-success", "multi-site", "&mpProfileDelayMs=500");
    await page.getByRole("button", { name: "2026.01" }).click();
    await page.getByRole("button", { name: "Validate configuration" }).click();
    await page.getByRole("button", { name: "Activate Sales Invoice Setup" }).click();
    await expect(page.getByRole("dialog", { name: "Activate Sales Invoice Setup?" })).toBeInViewport();
    await page.keyboard.press("Tab");
    await page.keyboard.press("Enter");
    await expect(page.getByLabel("Current Site")).toBeDisabled();
    await expect(page.getByRole("status", { name: "Development mutation attempts" })).toContainText("1");

    await gotoScenario(page, "retire-success", "multi-site", "&mpProfileDelayMs=500");
    await page.getByRole("button", { name: "2026.01" }).click();
    await page.getByRole("button", { name: "Retire Sales Invoice Setup" }).click();
    await expect(page.getByRole("dialog", { name: "Retire Sales Invoice Setup?" })).toBeInViewport();
    await page.keyboard.press("Escape");
    await expect(page.getByRole("dialog", { name: "Retire Sales Invoice Setup?" })).toHaveCount(0);
    await page.getByRole("button", { name: "Retire Sales Invoice Setup" }).click();
    await page.getByRole("dialog", { name: "Retire Sales Invoice Setup?" }).getByRole("button", { name: "Retire Sales Invoice Setup" }).click();
    await expect(page.getByLabel("Current Site")).toBeDisabled();
    await expect(page.getByRole("status", { name: "Development mutation attempts" })).toContainText("1");

    const visibleText = await page.locator("body").innerText();
    expect(visibleText).toContain("Effective period and status history");
    expect(visibleText).not.toMatch(/Approve Profile|Fiscal Identity|Header Profile|Effective and lifecycle metadata|Effective readiness|Immutable usage|Mutation accepted/);
  });

  test("browser route, header, storage, and production scenario boundaries hold", async ({ page, browser }) => {
    const apiRequests: Request[] = [];
    await stubCentralPmsApi(page);
    page.on("request", (request) => {
      const url = request.url();
      if (url.includes("/v1/management-platform")) {
        apiRequests.push(request);
      }
      expect(url).not.toContain("/v1/admin/");
      expect(url).not.toContain("pos-server");
      const headerNames = Object.keys(request.headers()).map((header) => header.toLowerCase());
      expect(headerNames).not.toContain("x-posserver-admin-key");
      expect(headerNames).not.toContain("x-posserver-admin-permission");
    });

    await page.goto(route);
    await expect(page.getByRole("button", { name: "ROUTE-2026" })).toBeVisible();
    await page.getByRole("button", { name: "ROUTE-2026" }).click();
    await expect(page.getByRole("heading", { name: "Sales Invoice Setup details" })).toBeVisible();
    expect(apiRequests.length).toBeGreaterThan(0);
    for (const request of apiRequests) {
      const url = new URL(request.url());
      expect(url.pathname).toMatch(/^\/v1\/management-platform\//);
      expect(request.headers()["x-correlation-id"]).toBeTruthy();
    }
    await assertBrowserStorageSafe(page);

    const productionPort = Number(process.env.MANAGEMENT_PLATFORM_E2E_PRODUCTION_PORT ?? 5180);
    const productionPage = await browser.newPage();
    try {
      await productionPage.goto(`http://127.0.0.1:${productionPort}${route}?mpScenario=authenticated&mpProfileScenario=manage`);
      await expect(productionPage.getByRole("status", { name: "Development scenario" })).toHaveCount(0);
      await expect(productionPage.getByRole("status", { name: "Development profile scenario" })).toHaveCount(0);
      await expect(productionPage.getByText(/Scenario selector/i)).toHaveCount(0);
    } finally {
      await productionPage.close();
    }
  });
});

async function gotoScenario(page: Page, profileScenario: string, mpScenario = "authenticated", extraQuery = "") {
  await page.goto(`${route}?mpScenario=${mpScenario}&mpProfileScenario=${profileScenario}${extraQuery}`);
  await expect(page.locator("#sales-profile-title")).toHaveText("Sales Invoice Setups");
}

async function fillFiscalIdentity(form: Locator) {
  await form.getByLabel("Registered business name *").fill("Managed Development Parking");
  await form.getByLabel("Registered business address *").fill("Managed Development Address");
  await form.getByLabel("TIN *").fill("MANAGED-TIN-001");
  await form.getByLabel("Taxpayer/VAT registration posture *").fill("VAT_REGISTERED");
}

async function fillProfile(form: Locator) {
  await form.getByLabel("Registered Business ID *").fill("fiscal-dev-identity-001");
  await form.getByLabel("Setup version *").fill("2026.02");
  await form.getByLabel("POS serial number *").fill("DEV-POS-002");
  await form.getByLabel("Machine Identification Number *").fill("DEV-MIN-002");
  await form.getByLabel("Parking-location display *").fill("Managed Development Parking");
  await form.getByLabel("BIR accreditation number *").fill("DEV-BIR-002");
  await form.getByLabel("BIR accreditation date issued *").fill("2026-02-01");
  await form.getByLabel("BIR accreditation valid until *").fill("2027-02-01");
  await form.getByLabel("PTU number *").fill("DEV-PTU-002");
  await form.getByLabel("PTU date issued *").fill("2026-02-02");
  await form.getByLabel("Sales Invoice legal statement *").fill("Managed development legal statement.");
  await form.getByLabel("Customer-service footer").fill("Managed development footer.");
  await form.getByLabel("Effective from *").fill("2026-02-01T08:00");
  await form.getByLabel("Effective to").fill("2026-12-31T23:59");
}

async function assertSingleAttempt(page: Page, scenario: string, buttonName: string, fill: (form: Locator) => Promise<void>) {
  await gotoScenario(page, scenario, "authenticated", "&mpProfileDelayMs=1000");
  await page.getByRole("button", { name: buttonName }).click();
  const form = page.getByRole("form", { name: buttonName });
  await fill(form);
  const submit = form.getByRole("button", { name: buttonName });
  await submit.dblclick();
  await expect(page.getByRole("status", { name: "Development mutation attempts" })).toContainText("1");
}

async function assertBrowserStorageSafe(page: Page) {
  const storageText = await page.evaluate(async () => {
    const indexedDbNames = typeof indexedDB.databases === "function"
      ? (await indexedDB.databases()).map((db) => db.name ?? "")
      : [];
    return JSON.stringify({
      localStorage: { ...localStorage },
      sessionStorage: { ...sessionStorage },
      indexedDbNames
    });
  });
  expect(storageText).not.toMatch(/Registered Business|Sales Invoice Setup|MANAGED-TIN|Managed Development Address|DEV-BIR|DEV-PTU|API key|POS Server|actor|site authorization/i);
}

async function stubCentralPmsApi(page: Page) {
  await page.route("**/v1/management-platform/**", async (routeRequest) => {
    const request = routeRequest.request();
    const url = new URL(request.url());
    const path = url.pathname;
    const headers = { "Content-Type": "application/json", "X-Correlation-Id": request.headers()["x-correlation-id"] ?? "e2e-correlation" };
    if (path.endsWith("/sales-invoice-header-profiles") && request.method() === "GET") {
      return routeRequest.fulfill({ status: 200, headers, json: [{
        salesInvoiceHeaderProfileId: "route-profile-001",
        fiscalIdentityId: "route-fiscal-001",
        siteId: "77000000-0000-0000-0000-000000000002",
        sitePosServerId: "88000000-0000-0000-0000-000000000002",
        profileVersion: "ROUTE-2026",
        templateVersion: "digital-sales-invoice-json-v1",
        presentationVersion: "digital-sales-invoice-presentation-json-v1",
        parkingLocationDisplay: "Route Boundary Parking",
        lifecycleState: "DRAFT",
        effectiveFrom: "2026-01-01T00:00:00Z",
        effectiveTo: "2026-12-31T23:59:59Z",
        updatedAt: "2026-07-20T01:00:00Z",
        fiscalIdentityDisplayName: "Route Boundary Identity"
      }] });
    }
    if (path.endsWith("/effective-readiness")) {
      return routeRequest.fulfill({ status: 200, headers, json: {
        siteId: "77000000-0000-0000-0000-000000000002",
        sitePosServerId: "88000000-0000-0000-0000-000000000002",
        effectiveAt: "2026-07-20T04:30:00Z",
        resolutionStatus: "READY",
        effectiveProfileId: "route-profile-001",
        profileVersion: "ROUTE-2026",
        fiscalIdentityId: "route-fiscal-001",
        lifecycleState: "DRAFT",
        isComplete: true,
        enforcementRequired: true,
        missingOrInvalidFieldCodes: [],
        birAccreditationValidityPosture: "VALID",
        ptuCompletenessPosture: "COMPLETE",
        supportedVersionPosture: "SUPPORTED",
        overlapOrAmbiguityPosture: "NONE",
        lastUpdatedAt: "2026-07-20T04:35:00Z",
        correlationId: "route-readiness"
      } });
    }
    if (path.endsWith("/usage")) {
      return routeRequest.fulfill({ status: 200, headers, json: {
        salesInvoiceHeaderProfileId: "route-profile-001",
        profileVersion: "ROUTE-2026",
        fiscalIdentityId: "route-fiscal-001",
        fiscalDocumentCount: 0,
        safeFiscalDocumentIds: [],
        destructiveMutationBlocked: false,
        correlationId: "route-usage"
      } });
    }
    if (path.includes("/fiscal-identities/")) {
      return routeRequest.fulfill({ status: 200, headers, json: {
        fiscalIdentityId: "route-fiscal-001",
        registeredBusinessName: "Route Boundary Identity",
        registeredBusinessAddress: "Route Boundary Address",
        tin: "ROUTE-TIN",
        taxpayerRegistrationPosture: "VAT_REGISTERED",
        lifecycleState: "ACTIVE",
        createdAt: "2026-01-01T00:00:00Z",
        updatedAt: "2026-07-20T01:00:00Z"
      } });
    }
    if (path.includes("/sales-invoice-header-profiles/")) {
      return routeRequest.fulfill({ status: 200, headers, json: {
        salesInvoiceHeaderProfileId: "route-profile-001",
        fiscalIdentityId: "route-fiscal-001",
        siteId: "77000000-0000-0000-0000-000000000002",
        sitePosServerId: "88000000-0000-0000-0000-000000000002",
        profileVersion: "ROUTE-2026",
        templateVersion: "digital-sales-invoice-json-v1",
        presentationVersion: "digital-sales-invoice-presentation-json-v1",
        posSerialNumber: "ROUTE-POS",
        machineIdentificationNumber: "ROUTE-MIN",
        parkingLocationDisplay: "Route Boundary Parking",
        birAccreditationNumber: "ROUTE-BIR",
        birAccreditationIssuedDate: "2026-01-15",
        birAccreditationValidUntil: "2027-01-31",
        ptuNumber: "ROUTE-PTU",
        ptuIssuedDate: "2026-01-20",
        salesInvoiceLegalStatement: "Route boundary legal statement.",
        customerServiceFooter: "Route boundary footer.",
        effectiveFrom: "2026-01-01T00:00:00Z",
        effectiveTo: "2026-12-31T23:59:59Z",
        lifecycleState: "DRAFT",
        createdAt: "2026-01-01T00:00:00Z",
        updatedAt: "2026-07-20T01:00:00Z"
      } });
    }
    return routeRequest.fulfill({ status: 404, headers, json: { code: "NOT_FOUND" } });
  });
}
