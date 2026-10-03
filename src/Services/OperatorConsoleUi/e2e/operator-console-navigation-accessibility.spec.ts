import { expect, test, type Locator } from "@playwright/test";

const navigationLabels = [
  "Ticket Lookup",
  "Fiscal Status",
  "Work Queue",
  "Shift Management",
  "Fiscal Reporting",
  "Audit / Reporting",
  "Fiscal View Audit",
  "Sales Invoice Void Audit",
  "Vendor Acknowledgments",
  "Projection Health",
  "Policy Import Review"
];

const siteOperatorNavigationLabels = [
  "Ticket Lookup",
  "Work Queue"
];

const viewports = [
  { name: "mobile-320", width: 320, height: 720 },
  { name: "mobile-360", width: 360, height: 800 },
  { name: "mobile-390", width: 390, height: 844 },
  { name: "mobile-412", width: 412, height: 915 },
  { name: "mobile-480", width: 480, height: 900 },
  { name: "tablet-768", width: 768, height: 1024 },
  { name: "desktop-1440", width: 1440, height: 900 }
];

test.describe("Operator Console responsive navigation accessibility", () => {
  test("PITX Site Operator sees only the approved permission-driven mobile surface", async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto("/operator-console?auth=site-operator");

    await expect(page).toHaveURL(/\/operator-console\/ticket-lookup$/);
    await expect(page.getByRole("heading", { name: "Ticket Lookup" })).toBeVisible();

    const menuButton = page.getByRole("button", { name: "Open navigation menu" });
    await expect(menuButton).toBeVisible();
    await menuButton.click();
    const drawer = page.getByRole("dialog", { name: "Navigation" });
    const navigation = drawer.getByRole("navigation", { name: "Operator Console mobile routes" });
    const items = navigation.getByRole("button");
    await expect(items).toHaveCount(siteOperatorNavigationLabels.length);
    await expect(items).toHaveText(siteOperatorNavigationLabels);
    await expect(navigation.getByRole("button", { name: "Overview" })).toHaveCount(0);
    await expect(navigation.getByRole("button", { name: "Ticket Lookup" })).toHaveAttribute("aria-current", "page");
    await expect(drawer.getByLabel("Mobile operator identity")).toContainText("PITX Site Operator");

    await navigation.getByRole("button", { name: "Ticket Lookup" }).click();
    const ticketHeading = page.getByRole("heading", { name: "Ticket Lookup" });
    await expect(ticketHeading).toBeVisible();
    await expect(ticketHeading).toBeFocused();

    await menuButton.click();
    await expect(drawer.getByRole("button", { name: "Fiscal Status" })).toHaveCount(0);
    await page.keyboard.press("Escape");
    await page.goto("/operator-console/fiscal-issuance-status");
    await expect(page.getByRole("heading", { name: "Function unavailable" })).toBeVisible();

    await page.goto("/operator-console/statutory-discounts");
    await menuButton.click();
    await navigation.getByRole("button", { name: "Work Queue" }).click();
    await expect(page.getByRole("heading", { name: "Work queue" })).toBeVisible();
    await expect(page.getByRole("button", { name: /^(Approve|Reject)$/i })).toHaveCount(0);

    await page.goto("/operator-console/audit");
    await expect(page.getByRole("heading", { name: "Function unavailable" })).toBeVisible();
    await expect(page.getByText("This function is not available for your account.")).toBeVisible();
  });

  test("mobile drawer contains focus, dismisses consistently, and restores menu focus", async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto("/operator-console/statutory-discounts/senior-representative-optional");
    const menuButton = page.locator(".mobileMenuButton");

    await menuButton.focus();
    await expect(menuButton).toHaveAccessibleName("Open navigation menu");
    await page.keyboard.press("Enter");
    const drawer = page.getByRole("dialog", { name: "Navigation" });
    const closeButton = drawer.getByRole("button", { name: "Close" });
    await expect(closeButton).toBeFocused();
    await expect(menuButton).toHaveAttribute("aria-expanded", "true");
    await expect(menuButton).toHaveAttribute("aria-controls", "operator-console-mobile-navigation");
    await expect.poll(() => page.evaluate(() => document.body.style.overflow)).toBe("hidden");

    await page.keyboard.press("Shift+Tab");
    await expect(drawer.getByRole("button", { name: "Sign out" })).toBeFocused();
    await page.keyboard.press("Tab");
    await expect(closeButton).toBeFocused();

    await page.keyboard.press("Escape");
    await expect(drawer).toBeHidden();
    await expect(menuButton).toBeFocused();
    await expect(menuButton).toHaveAttribute("aria-expanded", "false");
    await expect.poll(() => page.evaluate(() => document.body.style.overflow)).toBe("");

    await menuButton.click();
    await page.mouse.click(385, 820);
    await expect(drawer).toBeHidden();
    await expect(menuButton).toBeFocused();

    await menuButton.click();
    await drawer.getByRole("button", { name: "Close" }).click();
    await expect(drawer).toBeHidden();
    await expect(menuButton).toBeFocused();
  });

  for (const viewport of viewports) {
    test(`${viewport.name} renders the correct navigation mode without overflow`, async ({ page }, testInfo) => {
      await page.setViewportSize(viewport);
      await page.goto("/operator-console/statutory-discounts/senior-representative-optional");

      const compact = viewport.width < 900;
      const desktopNavigation = page.getByRole("navigation", { name: "Operator Console routes" });
      const menuButton = page.getByRole("button", { name: "Open navigation menu" });
      let navigation: Locator;

      if (compact) {
        await expect(desktopNavigation).toBeHidden();
        await expect(menuButton).toBeVisible();
        await page.screenshot({ path: testInfo.outputPath(`${viewport.name}-shell-closed.png`), fullPage: true });
        const menuBounds = await menuButton.boundingBox();
        expect(menuBounds).not.toBeNull();
        expect(menuBounds!.width).toBeGreaterThanOrEqual(44);
        expect(menuBounds!.height).toBeGreaterThanOrEqual(44);
        await menuButton.click();
        const drawer = page.getByRole("dialog", { name: "Navigation" });
        await expect(drawer).toBeVisible();
        navigation = drawer.getByRole("navigation", { name: "Operator Console mobile routes" });
      } else {
        await expect(menuButton).toBeHidden();
        await expect(desktopNavigation).toBeVisible();
        navigation = desktopNavigation;
      }

      const items = navigation.getByRole("button");
      await expect(items).toHaveCount(navigationLabels.length);
      await expect(items).toHaveText(navigationLabels);

      const current = navigation.getByRole("button", { name: "Work Queue", current: "page" });
      await expect(current).toBeVisible();
      await expect(navigation.locator('[aria-current="page"]')).toHaveCount(1);

      const normalActive = await measuredColors(current);
      const normalActiveContrast = contrastRatio(normalActive.foreground, normalActive.background);
      expect(normalActiveContrast).toBeGreaterThanOrEqual(4.5);
      await page.screenshot({ path: testInfo.outputPath(`${viewport.name}-responsive-shell.png`), fullPage: true });

      await current.focus();
      await expect(current).toBeFocused();
      const focusedActive = await measuredColors(current);
      expect(contrastRatio(focusedActive.foreground, focusedActive.background)).toBeGreaterThanOrEqual(4.5);
      expect(contrastRatio(focusedActive.outline, focusedActive.adjacentBackground)).toBeGreaterThanOrEqual(3);

      const ticketLookup = navigation.getByRole("button", { name: "Ticket Lookup" });
      await ticketLookup.focus();
      await page.keyboard.press("Enter");
      const destinationHeading = page.getByRole("heading", { name: "Ticket Lookup" });
      await expect(destinationHeading).toBeVisible();
      await expect(destinationHeading).toBeFocused();

      if (!compact) {
        await expect(desktopNavigation.getByRole("button", { name: "Ticket Lookup" })).toHaveAttribute("aria-current", "page");
      } else {
        await expect(page.getByRole("dialog", { name: "Navigation" })).toBeHidden();
      }

      const measurements = await page.evaluate(() => ({
        documentWidth: document.documentElement.scrollWidth,
        viewportWidth: document.documentElement.clientWidth
      }));
      expect(measurements.documentWidth).toBeLessThanOrEqual(measurements.viewportWidth);

      await testInfo.attach(`${viewport.name}-navigation-evidence.json`, {
        body: JSON.stringify({ viewport, compact, normalActiveContrast, layout: measurements }, null, 2),
        contentType: "application/json"
      });
    });
  }

  for (const viewport of viewports) {
    test(`${viewport.name} keeps the statutory work queue compact and operable`, async ({ page }, testInfo) => {
      const browserErrors: string[] = [];
      const failedRequests: string[] = [];
      page.on("console", (message) => {
        if (message.type() === "error") browserErrors.push(message.text());
      });
      page.on("pageerror", (error) => browserErrors.push(error.message));
      page.on("requestfailed", (request) => failedRequests.push(`${request.method()} ${request.url()}`));
      await page.setViewportSize(viewport);
      await page.goto("/operator-console/statutory-discounts?auth=site-operator");

      await expect(page.getByRole("heading", { name: "Work queue" })).toBeVisible();
      await expect(page.getByText("Access readiness", { exact: true })).toHaveCount(0);
      await expect(page.getByText("Operator readiness state", { exact: true })).toHaveCount(0);
      await expect(page.getByText("Live read model", { exact: true })).toHaveCount(0);
      await expect(page.getByText(/Site operations workspace/i)).toHaveCount(0);

      const queue = page.locator(".workQueueTable");
      await expect(queue).toBeVisible();
      const firstRow = queue.locator(".workQueuePrimaryRow").first();
      await expect(firstRow.locator(".workQueueTicketColumn")).toBeVisible();
      await expect(firstRow.locator(".workQueueStatusColumn")).toBeVisible();
      await expect(firstRow.getByRole("button", { name: /^Review / })).toBeVisible();

      const compact = viewport.width < 900;
      const expander = firstRow.locator(".workQueueExpander");
      if (compact) {
        await expect(expander).toBeVisible();
        await expect(expander).toHaveAccessibleName(/^Details for /);
        await expect(expander).toHaveAttribute("aria-expanded", "false");
        await expect(expander).toHaveAttribute("aria-controls", /work-queue-details-/);
        const expanderBounds = await expander.boundingBox();
        expect(expanderBounds).not.toBeNull();
        expect(expanderBounds!.height).toBeGreaterThanOrEqual(44);
        expect(expanderBounds!.width).toBeGreaterThanOrEqual(44);
        await expander.focus();
        await page.keyboard.press("Enter");
        await expect(expander).toHaveAttribute("aria-expanded", "true");
        const detailsId = await expander.getAttribute("aria-controls");
        const details = page.locator(`#${detailsId}`);
        await expect(details).toBeVisible();
        await expect(details.getByText("Source", { exact: true })).toBeVisible();
        await expect(details.getByText("Operator Console", { exact: true })).toBeVisible();

        if (viewport.width < 600) {
          await expect(firstRow.locator(".workQueueSiteColumn")).toBeHidden();
          await expect(firstRow.locator(".workQueueEntitlementColumn")).toBeHidden();
        } else {
          await expect(firstRow.locator(".workQueueSiteColumn")).toBeVisible();
          await expect(firstRow.locator(".workQueueEntitlementColumn")).toBeVisible();
        }
        await expect(firstRow.locator(".workQueueRequestedByColumn")).toBeHidden();
        await expect(firstRow.locator(".workQueueRequestedAtColumn")).toBeHidden();
      } else {
        await expect(expander).toBeHidden();
        await expect(firstRow.locator(".workQueueSiteColumn")).toBeVisible();
        await expect(firstRow.locator(".workQueueEntitlementColumn")).toBeVisible();
        await expect(firstRow.locator(".workQueueRequestedByColumn")).toBeVisible();
        await expect(firstRow.locator(".workQueueRequestedAtColumn")).toBeVisible();
      }

      const layout = await page.evaluate(() => ({
        documentWidth: document.documentElement.scrollWidth,
        viewportWidth: document.documentElement.clientWidth,
        queueWidth: document.querySelector(".workQueueScroller")?.scrollWidth ?? 0,
        queueViewportWidth: document.querySelector(".workQueueScroller")?.clientWidth ?? 0,
        applicationTitleFontSize: getComputedStyle(document.querySelector(".appHeader h1")!).fontSize,
        pageTitleFontSize: getComputedStyle(document.querySelector(".pageTitle h2")!).fontSize,
        sectionTitleFontSize: getComputedStyle(document.querySelector(".panelHeader h3")!).fontSize
      }));
      expect(layout.documentWidth).toBeLessThanOrEqual(layout.viewportWidth);
      if (viewport.width < 600) expect(layout.queueWidth).toBeLessThanOrEqual(layout.queueViewportWidth);
      if (compact) {
        expect(Number.parseFloat(layout.applicationTitleFontSize)).toBeGreaterThanOrEqual(26);
        expect(Number.parseFloat(layout.applicationTitleFontSize)).toBeLessThanOrEqual(28);
        expect(Number.parseFloat(layout.pageTitleFontSize)).toBeGreaterThanOrEqual(22);
        expect(Number.parseFloat(layout.pageTitleFontSize)).toBeLessThanOrEqual(24);
        expect(Number.parseFloat(layout.sectionTitleFontSize)).toBeGreaterThanOrEqual(18);
        expect(Number.parseFloat(layout.sectionTitleFontSize)).toBeLessThanOrEqual(20);
      }
      expect(browserErrors).toEqual([]);
      expect(failedRequests).toEqual([]);

      await page.screenshot({ path: testInfo.outputPath(`${viewport.name}-density-queue.png`), fullPage: true });
      await testInfo.attach(`${viewport.name}-density-queue.json`, {
        body: JSON.stringify({ viewport, compact, layout }, null, 2),
        contentType: "application/json"
      });
    });
  }
});

async function measuredColors(locator: Locator) {
  return locator.evaluate((element) => {
    const styles = getComputedStyle(element);
    const adjacentStyles = getComputedStyle(
      element.closest(".moduleRail") ?? element.closest(".mobileNavigationDrawer") ?? document.body
    );
    return {
      foreground: styles.color,
      background: styles.backgroundColor,
      outline: styles.outlineColor,
      adjacentBackground: adjacentStyles.backgroundColor
    };
  });
}

function contrastRatio(first: string, second: string) {
  const firstLuminance = relativeLuminance(first);
  const secondLuminance = relativeLuminance(second);
  return (Math.max(firstLuminance, secondLuminance) + 0.05) / (Math.min(firstLuminance, secondLuminance) + 0.05);
}

function relativeLuminance(color: string) {
  const channels = color.match(/[\d.]+/g)?.slice(0, 3).map(Number);
  if (!channels || channels.length !== 3) throw new Error(`Unsupported computed color: ${color}`);
  const linear = channels.map((channel) => {
    const normalized = channel / 255;
    return normalized <= 0.04045 ? normalized / 12.92 : ((normalized + 0.055) / 1.055) ** 2.4;
  });
  return 0.2126 * linear[0] + 0.7152 * linear[1] + 0.0722 * linear[2];
}
