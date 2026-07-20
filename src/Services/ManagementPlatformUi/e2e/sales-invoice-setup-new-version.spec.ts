import { expect, test, type Locator, type Page } from "@playwright/test";

const route = "/management-platform/sales-invoice-profiles";
const forbiddenBrowserWords = /Fiscal Identity|Header Profile|Profile administration|Mutation accepted|Clone Profile|Lifecycle metadata|Immutable usage|Effective readiness|Clone|Duplicate Profile|Copy Header Profile|Copy Setup|Duplicate Setup/i;

test.describe("Management Platform Sales Invoice Setup Create New Version E2E", () => {
  test("permission and source status eligibility are governed", async ({ page }) => {
    await gotoScenario(page, "new-version-read-only");
    await selectVersion(page, "2026.01");
    await expect(page.getByRole("button", { name: "Create New Setup Version" })).toHaveCount(0);

    await gotoScenario(page, "new-version-approve-only");
    await selectVersion(page, "2026.01");
    await expect(page.getByRole("button", { name: "Create New Setup Version" })).toHaveCount(0);

    await gotoScenario(page, "new-version-manage");
    await selectVersion(page, "2026.01");
    await expect(page.getByRole("button", { name: "Create New Setup Version" })).toBeVisible();

    await gotoScenario(page, "manage");
    await selectVersion(page, "2026.01");
    await expect(page.getByRole("button", { name: "Create New Setup Version" })).toHaveCount(0);

    await gotoScenario(page, "retired-read-only");
    await selectVersion(page, "2025.12");
    await expect(page.getByRole("button", { name: "Create New Setup Version" })).toHaveCount(0);

    await gotoScenario(page, "new-version-unknown-status");
    await selectVersion(page, "2026.01");
    await expect(page.getByRole("button", { name: "Create New Setup Version" })).toHaveCount(0);
  });

  test("Site mismatch and unavailable sources block without a mutation", async ({ page }) => {
    await gotoScenario(page, "new-version-site-mismatch");
    await selectVersion(page, "2026.01");
    await expect(page.getByRole("status", { name: "Site scope does not match" })).toContainText("Create New Setup Version is blocked");
    await expect(page.getByRole("button", { name: "Create New Setup Version" })).toHaveCount(0);
    await expect(page.getByRole("status", { name: "Development mutation attempts" })).toContainText("0");

    await gotoScenario(page, "new-version-source-not-active");
    await selectVersion(page, "2026.01");
    await expect(page.getByText("Draft").first()).toBeVisible();
    await expect(page.getByRole("button", { name: "Create New Setup Version" })).toHaveCount(0);
    await expect(page.getByRole("status", { name: "Development mutation attempts" })).toContainText("0");

    await gotoScenario(page, "new-version-source-not-found");
    await selectVersion(page, "2026.01");
    await expect(page.getByRole("alert", { name: "Sales Invoice Setup unavailable" })).toContainText("dev-new-version-source-not-found");
    await expect(page.getByRole("status", { name: "Development mutation attempts" })).toContainText("0");
  });

  test("source summary is read-only and form copies only permitted configuration", async ({ page }) => {
    await gotoScenario(page, "new-version-manage");
    await openNewVersion(page);
    const form = page.getByRole("form", { name: "Create Draft Setup" });

    await expect(page.getByRole("heading", { name: "Create New Sales Invoice Setup Version" })).toBeVisible();
    for (const text of [
      "Source Sales Invoice Setup ID",
      "Source setup version",
      "Source status",
      "Registered Business",
      "Site",
      "Site POS Server",
      "Source effective period",
      "A new Draft setup will be created",
      "previously issued Sales Invoices will remain unchanged",
      "Overlap is checked authoritatively"
    ]) {
      await expect(form).toContainText(text);
    }
    await expect(form.getByLabel("Registered Business *")).toHaveAttribute("readonly", "");
    await expect(form.getByLabel("Site *")).toHaveAttribute("readonly", "");
    await expect(form.getByLabel("Site POS Server *")).toHaveAttribute("readonly", "");
    await expect(form.getByLabel("New setup version *")).toHaveValue("");
    await expect(form.getByLabel("Effective from *")).toHaveValue("");
    await expect(form.getByLabel("Effective to")).toHaveValue("");
    await expect(form.getByLabel("Template version")).toHaveValue("digital-sales-invoice-json-v1");
    await expect(form.getByLabel("Presentation version")).toHaveValue("digital-sales-invoice-presentation-json-v1");
    await expect(form.getByLabel("POS serial number *")).toHaveValue("DEV-POS-SERIAL-001");
    await expect(form.getByLabel("Machine Identification Number *")).toHaveValue("DEV-MIN-001");
    await expect(form.getByLabel("BIR accreditation date issued *")).toHaveValue("2026-01-15");
    await expect(form.getByLabel("BIR accreditation valid until *")).toHaveValue("2027-01-31");
    await expect(form.getByLabel("PTU date issued *")).toHaveValue("2026-01-20");
    await expect(form).not.toContainText(/Terminal ID|actor|lifecycle|Activate after creation|Retire source|Active status|Retired status/i);
  });

  test("version validation requires explicit distinct value and never suggests an increment", async ({ page }) => {
    await gotoScenario(page, "new-version-manage");
    await openNewVersion(page);
    const form = page.getByRole("form", { name: "Create Draft Setup" });
    await form.getByRole("button", { name: "Create Draft Setup" }).click();
    await expect(page.getByRole("alert", { name: "Form validation summary" })).toContainText("New setup version is required.");
    await expect(form.getByLabel("New setup version *")).toHaveValue("");
    await form.getByLabel("New setup version *").fill("2026.01");
    await form.getByLabel("Effective from *").fill("2026-08-01T00:00");
    await form.getByRole("button", { name: "Create Draft Setup" }).click();
    await expect(page.getByRole("alert", { name: "Form validation summary" })).toContainText("must differ");
    await expect(form.getByLabel("New setup version *")).toHaveValue("2026.01");
    await expect(form.getByText(/2026\.02|v2|current version plus one|semantic version/i)).toHaveCount(0);
    await expect(page.getByRole("status", { name: "Development mutation attempts" })).toContainText("0");
  });

  test("success creates one Draft, selects it, and leaves source history and lifecycle separate", async ({ page }) => {
    await gotoScenario(page, "new-version-success", "authenticated", "&mpProfileDelayMs=300");
    await openNewVersion(page);
    const form = page.getByRole("form", { name: "Create Draft Setup" });
    await fillNewVersion(form, "2026.02");
    await form.getByRole("button", { name: "Create Draft Setup" }).dblclick();
    await expect(page.getByRole("status", { name: "Development mutation attempts" })).toContainText("1");
    await expect(page.getByRole("status", { name: "Draft Sales Invoice Setup created" })).toBeVisible();
    await expect(page.getByText("sip-dev-profile-new-version")).toBeVisible();
    await expect(page.getByText("Draft").first()).toBeVisible();
    await expect(page.getByRole("button", { name: "Activate Sales Invoice Setup" })).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Retire Sales Invoice Setup" })).toHaveCount(0);
    await expect(page.getByRole("heading", { name: "Issuance history" }).first()).toBeVisible();
  });

  test("source preservation scenario shows source values unchanged before creation", async ({ page }) => {
    await gotoScenario(page, "new-version-source-preserved");
    await selectVersion(page, "2026.01");
    const detail = page.getByRole("region", { name: "Sales Invoice Setup details" });
    await expect(detail).toContainText("sip-dev-profile-001");
    await expect(detail).toContainText("2026.01");
    await expect(detail).toContainText("Active");
    await expect(detail).toContainText("2026-02-01T00:00:00Z");
    await expect(detail).toContainText("2026-01-25T04:00:00Z");
    await expect(page.getByRole("heading", { name: "Issuance history" })).toBeVisible();
  });

  test("duplicate, overlap, and timeout postures preserve form and request count", async ({ page }) => {
    await gotoScenario(page, "new-version-duplicate-conflict");
    await openNewVersion(page);
    let form = page.getByRole("form", { name: "Create Draft Setup" });
    await fillNewVersion(form, "2026.02");
    await form.getByRole("button", { name: "Create Draft Setup" }).click();
    await expect(page.getByRole("alert", { name: "Changes failed safely" })).toContainText("dev-new-version-duplicate-conflict");
    await expect(form.getByLabel("New setup version *")).toHaveValue("2026.02");
    await expect(page.getByRole("status", { name: "Development mutation attempts" })).toContainText("1");

    await gotoScenario(page, "new-version-overlap-conflict");
    await openNewVersion(page);
    form = page.getByRole("form", { name: "Create Draft Setup" });
    await fillNewVersion(form, "2026.03");
    await form.getByRole("button", { name: "Create Draft Setup" }).click();
    await expect(page.getByRole("alert", { name: "Changes failed safely" })).toContainText("dev-new-version-overlap-conflict");
    await expect(form.getByLabel("Effective from *")).toHaveValue("2026-08-01T00:00");
    await expect(form.getByLabel("Effective to")).toHaveValue("2026-12-31T23:59");
    await expect(page.getByRole("status", { name: "Development mutation attempts" })).toContainText("1");

    await gotoScenario(page, "new-version-timeout");
    await openNewVersion(page);
    form = page.getByRole("form", { name: "Create Draft Setup" });
    await fillNewVersion(form, "2026.04");
    await form.getByRole("button", { name: "Create Draft Setup" }).click();
    await expect(page.getByRole("status", { name: "Result uncertain" })).toContainText("Refresh and verify whether the Draft was created");
    await expect(page.getByRole("status", { name: "Result uncertain" })).toContainText("dev-new-version-timeout");
    await expect(page.getByRole("status", { name: "Development mutation attempts" })).toContainText("1");
    await expect(page.getByRole("status", { name: "Draft Sales Invoice Setup created" })).toHaveCount(0);
  });

  test("cancel, unsaved Site switch, pending Site switch, double-submit, repeated Enter, and storage are safe", async ({ page }) => {
    await gotoScenario(page, "new-version-cancel");
    await openNewVersion(page);
    await page.getByRole("form", { name: "Create Draft Setup" }).getByRole("button", { name: "Cancel" }).click();
    await expect(page.getByRole("form", { name: "Create Draft Setup" })).toHaveCount(0);
    await expect(page.getByRole("status", { name: "Development mutation attempts" })).toContainText("0");

    await gotoScenario(page, "new-version-unsaved-site-switch", "multi-site");
    await openNewVersion(page);
    const unsavedForm = page.getByRole("form", { name: "Create Draft Setup" });
    await unsavedForm.getByLabel("New setup version *").fill("2026.02");
    page.once("dialog", async (dialog) => {
      expect(dialog.message()).toContain("Discard unsaved");
      await dialog.dismiss();
    });
    await page.getByLabel("Current Site").selectOption("71000000-0000-0000-0000-000000000102");
    await expect(page.getByLabel("Current Site")).toHaveValue("71000000-0000-0000-0000-000000000101");
    await expect(unsavedForm.getByLabel("New setup version *")).toHaveValue("2026.02");
    page.once("dialog", async (dialog) => dialog.accept());
    await page.getByLabel("Current Site").selectOption("71000000-0000-0000-0000-000000000102");
    await expect(page.getByLabel("Current Site")).toHaveValue("71000000-0000-0000-0000-000000000102");
    await expect(page.getByRole("form", { name: "Create Draft Setup" })).toHaveCount(0);

    await gotoScenario(page, "new-version-pending-site-switch", "multi-site", "&mpProfileDelayMs=500");
    await openNewVersion(page);
    const pendingForm = page.getByRole("form", { name: "Create Draft Setup" });
    await fillNewVersion(pendingForm, "2026.05");
    await pendingForm.getByRole("button", { name: "Create Draft Setup" }).click();
    await expect(page.getByLabel("Current Site")).toBeDisabled();

    await gotoScenario(page, "new-version-double-submit", "authenticated", "&mpProfileDelayMs=500");
    await openNewVersion(page);
    const doubleForm = page.getByRole("form", { name: "Create Draft Setup" });
    await fillNewVersion(doubleForm, "2026.06");
    await doubleForm.getByRole("button", { name: "Create Draft Setup" }).dblclick();
    await page.keyboard.press("Enter");
    await expect(page.getByRole("status", { name: "Development mutation attempts" })).toContainText("1");
    await assertBrowserStorageSafe(page);
  });

  test("keyboard focus, responsive layout, production scenario ignoring, security, and terminology hold", async ({ page, browser }) => {
    for (const viewport of [{ width: 768, height: 900 }, { width: 1024, height: 768 }]) {
      await page.setViewportSize(viewport);
      await gotoScenario(page, "new-version-manage");
      await openNewVersion(page);
      const form = page.getByRole("form", { name: "Create Draft Setup" });
      await expect(page.locator(".mutationPanel")).toBeFocused();
      await form.getByRole("button", { name: "Create Draft Setup" }).scrollIntoViewIfNeeded();
      await expect(form.getByLabel("New setup version *")).toBeVisible();
      await expect(form.getByLabel("Effective from *")).toBeVisible();
      await expect(form.getByLabel("Effective to")).toBeVisible();
      await expect(form.getByRole("button", { name: "Create Draft Setup" })).toBeInViewport();
      await expect(form.getByRole("button", { name: "Cancel" })).toBeInViewport();
      expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1)).toBe(true);
    }

    await page.setViewportSize({ width: 1366, height: 768 });
    await gotoScenario(page, "new-version-manage");
    await selectVersion(page, "2026.01");
    await page.getByRole("button", { name: "Create New Setup Version" }).focus();
    await expect(page.getByRole("button", { name: "Create New Setup Version" })).toBeFocused();
    await page.keyboard.press("Enter");
    await expect(page.getByRole("form", { name: "Create Draft Setup" })).toBeVisible();
    const bodyText = await page.locator("body").innerText();
    expect(bodyText).toContain("Sales Invoice Configuration");
    expect(bodyText).not.toMatch(forbiddenBrowserWords);
    expect(bodyText).not.toMatch(/automatic activation|automatically activate|Terminal ID|actor field|source-retirement/i);

    const productionPort = Number(process.env.MANAGEMENT_PLATFORM_E2E_PRODUCTION_PORT ?? 5180);
    const productionPage = await browser.newPage();
    try {
      await productionPage.goto(`http://127.0.0.1:${productionPort}${route}?mpScenario=authenticated&mpProfileScenario=new-version-success`);
      await expect(productionPage.getByRole("status", { name: "Development profile scenario" })).toHaveCount(0);
      await expect(productionPage.getByText("new-version-success")).toHaveCount(0);
    } finally {
      await productionPage.close();
    }
  });
});

async function gotoScenario(page: Page, profileScenario: string, mpScenario = "authenticated", extraQuery = "") {
  await page.goto(`${route}?mpScenario=${mpScenario}&mpProfileScenario=${profileScenario}${extraQuery}`);
  await expect(page.locator("#sales-profile-title")).toHaveText("Sales Invoice Setups");
}

async function selectVersion(page: Page, version: string) {
  await page.getByRole("button", { name: version }).click();
}

async function openNewVersion(page: Page) {
  await selectVersion(page, "2026.01");
  await page.getByRole("button", { name: "Create New Setup Version" }).click();
}

async function fillNewVersion(form: Locator, version: string) {
  await form.getByLabel("New setup version *").fill(version);
  await form.getByLabel("Effective from *").fill("2026-08-01T00:00");
  await form.getByLabel("Effective to").fill("2026-12-31T23:59");
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
  expect(storageText).not.toMatch(/Registered Business|Sales Invoice Setup|DEV-BIR|DEV-PTU|API key|POS Server|actor|site authorization/i);
}
