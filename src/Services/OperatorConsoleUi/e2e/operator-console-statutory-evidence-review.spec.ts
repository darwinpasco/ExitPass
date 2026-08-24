import { expect, test, type Locator, type Page, type Request } from "@playwright/test";

const detail = (scenario: string) => `/operator-console/statutory-discounts/${scenario}`;

// Retired v1.2 draft-workspace assertions. Canonical evidence access is covered
// through the Central PMS review facade in operator-console-central-pms-statutory-review.spec.ts.
test.describe.skip("Operator Console secure statutory evidence review (obsolete draft path)", () => {
  test("authorized reviewer previews eligible JPEG and PNG without changing review state", async ({ page }) => {
    await trackObjectUrls(page);
    const evidenceRequests: Request[] = [];
    page.on("request", (request) => {
      if (request.url().includes("/statutory-discounts/reviews/")) {
        evidenceRequests.push(request);
      }
    });

    await page.goto(detail("evidence-eligible-jpeg"));
    await expect(page.getByRole("heading", { name: "Secure evidence review" })).toBeVisible();
    await expect(page.getByText(/JPEG image/)).toBeVisible();
    const jpegPreview = page.getByRole("button", { name: /Preview primary identity document/i });
    const jpegResponsePromise = page.waitForResponse((response) => response.url().endsWith("/preview"));
    await jpegPreview.click();
    const jpegResponse = await jpegResponsePromise;
    expect(jpegResponse.headers()["content-type"]).toBe("image/jpeg");
    const jpegBytes = await jpegResponse.body();
    expect(Array.from(jpegBytes.subarray(0, 2))).toEqual([0xff, 0xd8]);
    expect(Array.from(jpegBytes.subarray(-2))).toEqual([0xff, 0xd9]);
    const jpegDialog = page.getByRole("dialog", { name: "Primary identity document" });
    await expect(jpegDialog).toBeVisible();
    const jpegImage = jpegDialog.getByRole("img");
    await expectDecodedImage(jpegImage, 180, 110);
    const jpegObjectUrl = await jpegImage.getAttribute("src");
    expect(await objectUrlWasCreatedFromBlob(page, jpegObjectUrl, "image/jpeg", 3473)).toBe(true);
    expect(await findProhibitedGuidOccurrences(page)).toEqual([]);
    const fittedBox = await jpegImage.boundingBox();
    expect(fittedBox).not.toBeNull();
    await jpegDialog.getByRole("button", { name: "Zoom in" }).click();
    await expect(jpegDialog.getByText("125%")).toBeVisible();
    await expect.poll(async () => (await jpegImage.boundingBox())?.width ?? 0).toBeGreaterThan((fittedBox?.width ?? 0) * 1.2);
    await jpegDialog.getByRole("button", { name: "Fit evidence to view" }).click();
    await expect(jpegDialog.getByText("100%")).toBeVisible();
    await expect.poll(async () => Math.abs(((await jpegImage.boundingBox())?.width ?? 0) - (fittedBox?.width ?? 0))).toBeLessThan(1);
    await jpegDialog.getByRole("button", { name: "Close evidence preview" }).click();
    await expect(jpegPreview).toBeFocused();
    expect(await wasObjectUrlRevoked(page, jpegObjectUrl)).toBe(true);
    await expect(page.locator('img[src^="blob:"]')).toHaveCount(0);
    await expect(page.getByRole("button", { name: "Approve" })).toBeDisabled();

    await page.goto(detail("evidence-eligible-png"));
    const pngPreview = page.getByRole("button", { name: /Preview primary identity document/i });
    const pngResponsePromise = page.waitForResponse((response) => response.url().endsWith("/preview"));
    await pngPreview.click();
    const pngResponse = await pngResponsePromise;
    expect(pngResponse.headers()["content-type"]).toBe("image/png");
    const pngBytes = await pngResponse.body();
    expect(Array.from(pngBytes.subarray(0, 8))).toEqual([137, 80, 78, 71, 13, 10, 26, 10]);
    const pngImage = page.getByRole("dialog").getByRole("img");
    await expectDecodedImage(pngImage, 110, 180);
    const pngObjectUrl = await pngImage.getAttribute("src");
    expect(await objectUrlWasCreatedFromBlob(page, pngObjectUrl, "image/png", 923)).toBe(true);
    expect(await findProhibitedGuidOccurrences(page)).toEqual([]);
    await page.keyboard.press("Escape");
    await expect(page.getByRole("dialog")).toHaveCount(0);
    await expect(pngPreview).toBeFocused();
    expect(await wasObjectUrlRevoked(page, pngObjectUrl)).toBe(true);

    expect(evidenceRequests.length).toBeGreaterThanOrEqual(4);
    expect(evidenceRequests.every((request) => request.method() === "GET")).toBe(true);
    const pageOrigin = new URL(page.url()).origin;
    expect(evidenceRequests.every((request) => new URL(request.url()).origin === pageOrigin)).toBe(true);
    await expectRuntimePrivacy(page);
  });

  test("lifecycle and unsupported-media denials remain distinct and non-previewable", async ({ page }) => {
    const scenarios = [
      ["evidence-unsupported-pdf", "This file type cannot be previewed."],
      ["evidence-validation-pending", "Evidence is still being validated."],
      ["evidence-validation-failed", "Evidence cannot be reviewed because validation failed."],
      ["evidence-scan-pending", "Security scanning is still in progress."],
      ["evidence-scanner-outage", "Security scanning is temporarily unavailable."],
      ["evidence-malware-detected", "unsafe content was detected"],
      ["evidence-not-reviewable", "not available for review"],
      ["evidence-stale", "no longer current"],
      ["evidence-replaced", "has been replaced"],
      ["evidence-deleted", "no longer available"],
      ["evidence-pending-deletion", "pending deletion or unavailable"]
    ] as const;

    for (const [scenario, message] of scenarios) {
      await page.goto(detail(scenario));
      await expect(page.getByText(new RegExp(message, "i"))).toBeVisible();
      await expect(page.getByRole("button", { name: /Preview primary identity document/i })).toBeDisabled();
      await expect(page.getByRole("dialog")).toHaveCount(0);
      await expectRuntimePrivacy(page);
    }
  });

  test("hold and replacement posture remain review-safe metadata", async ({ page }) => {
    await page.goto(detail("evidence-hold-active"));
    await expect(page.getByText("Active hold").first()).toBeVisible();

    await page.goto(detail("evidence-replacement-allowed"));
    await expect(page.getByText(/Replacement allowed/i)).toBeVisible();

    await page.goto(detail("evidence-replacement-denied"));
    await expect(page.getByText(/Replacement prohibited/i)).toBeVisible();
  });

  test("storage outage is safe and retry re-reads current authority", async ({ page }) => {
    await trackObjectUrls(page);
    await page.goto(detail("evidence-preview-storage-outage"));
    await page.getByRole("button", { name: /Preview primary identity document/i }).click();
    const dialog = page.getByRole("dialog");
    await expect(dialog).toContainText("The preview service is temporarily unavailable.");
    await expect(dialog).not.toContainText(/provider|bucket|endpoint/i);
    await dialog.getByRole("button", { name: "Retry preview" }).click();
    const recoveredImage = dialog.getByRole("img");
    await expectDecodedImage(recoveredImage, 110, 180);
    expect(await objectUrlWasCreatedFromBlob(page, await recoveredImage.getAttribute("src"), "image/png", 923)).toBe(true);
    expect(await findProhibitedGuidOccurrences(page)).toEqual([]);
  });

  test("permission, Site, Site Group, and missing evidence denials fail safely", async ({ page }) => {
    await page.goto(detail("evidence-permission-denied"));
    await expect(page.getByRole("alert")).toContainText("no longer have access");
    await expect(page.getByRole("button", { name: /Preview primary identity document/i })).toHaveCount(0);

    for (const scenario of ["evidence-cross-site-denied", "evidence-cross-site-group-denied", "evidence-metadata-not-found"]) {
      await page.goto(detail(scenario));
      await expect(page.getByText(/could not be found or is outside your authorized scope/i)).toBeVisible();
      await expect(page.getByRole("button", { name: /Preview primary identity document/i })).toHaveCount(0);
      await expectRuntimePrivacy(page);
    }
  });

  test("preview not found is non-retryable and decision switch removes stale content", async ({ page }) => {
    await page.goto(detail("evidence-preview-not-found"));
    await page.getByRole("button", { name: /Preview primary identity document/i }).click();
    await expect(page.getByRole("dialog")).toContainText("The evidence could not be found.");
    await expect(page.getByRole("button", { name: "Retry preview" })).toHaveCount(0);

    await page.goto(detail("evidence-decision-switch"));
    await page.getByRole("button", { name: /Preview primary identity document/i }).click();
    await expect(page.getByRole("dialog")).toContainText("Loading the current authorized preview.");
    await page.goto(detail("evidence-eligible-png"));
    await expect(page.getByRole("dialog")).toHaveCount(0);
    expect(await page.locator('img[src^="blob:"]').count()).toBe(0);
  });

  test("review decisions remain separate and no evidence mutation request is issued", async ({ page }) => {
    const mutationRequests: string[] = [];
    page.on("request", (request) => {
      if (request.method() !== "GET" && request.url().includes("/statutory-discounts/reviews/")) {
        mutationRequests.push(`${request.method()} ${request.url()}`);
      }
    });

    await page.goto(detail("evidence-eligible-png"));
    await page.getByRole("button", { name: /Preview primary identity document/i }).click();
    await expectDecodedImage(page.getByRole("dialog").getByRole("img"), 110, 180);
    await expect(page.getByRole("button", { name: "Approve" })).toBeDisabled();
    await expect(page.getByRole("button", { name: "Reject" })).toBeEnabled();
    expect(mutationRequests).toEqual([]);
  });

  test("keyboard, compact, and 200 percent zoom layouts remain usable without persistence", async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto(detail("evidence-eligible-png"));
    const preview = page.getByRole("button", { name: /Preview primary identity document/i });
    await preview.focus();
    await page.keyboard.press("Enter");
    const dialog = page.getByRole("dialog");
    await expect(dialog).toBeVisible();
    await expect(dialog.getByRole("button", { name: "Close evidence preview" })).toBeFocused();
    await expect(dialog).toHaveCSS("max-width", "1040px");
    await page.keyboard.press("Escape");
    await expect(preview).toBeFocused();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1)).toBe(true);

    await page.setViewportSize({ width: 1366, height: 768 });
    await page.evaluate(() => {
      document.documentElement.style.zoom = "2";
    });
    await expect(preview).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1)).toBe(true);

    const storage = await page.evaluate(async () => ({
      local: Object.keys(localStorage),
      session: Object.keys(sessionStorage),
      indexedDb: await indexedDB.databases(),
      caches: "caches" in window ? await caches.keys() : []
    }));
    expect(storage.local).toEqual([]);
    expect(storage.session).toEqual([]);
    expect(storage.indexedDb).toEqual([]);
    expect(storage.caches).toEqual([]);
    await expectRuntimePrivacy(page);
  });
});

async function expectDecodedImage(image: Locator, expectedWidth: number, expectedHeight: number) {
  await expect(image).toBeVisible();
  await expect.poll(() => image.evaluate((element) => {
    const candidate = element as HTMLImageElement;
    return candidate.complete && candidate.naturalWidth > 0 && candidate.naturalHeight > 0;
  })).toBe(true);
  expect(await image.evaluate((element) => (element as HTMLImageElement).naturalWidth)).toBe(expectedWidth);
  expect(await image.evaluate((element) => (element as HTMLImageElement).naturalHeight)).toBe(expectedHeight);
}

async function trackObjectUrls(page: Page) {
  await page.addInitScript(() => {
    const runtimeWindow = window as typeof window & {
      __createdEvidenceObjectUrls?: Array<{ url: string; isBlob: boolean; type: string; size: number }>;
      __revokedEvidenceObjectUrls?: string[];
    };
    runtimeWindow.__createdEvidenceObjectUrls = [];
    runtimeWindow.__revokedEvidenceObjectUrls = [];
    const create = URL.createObjectURL.bind(URL);
    const revoke = URL.revokeObjectURL.bind(URL);
    URL.createObjectURL = (object: Blob | MediaSource) => {
      const url = create(object);
      runtimeWindow.__createdEvidenceObjectUrls?.push({
        url,
        isBlob: object instanceof Blob,
        type: object instanceof Blob ? object.type : "",
        size: object instanceof Blob ? object.size : 0
      });
      return url;
    };
    URL.revokeObjectURL = (url: string) => {
      runtimeWindow.__revokedEvidenceObjectUrls?.push(url);
      revoke(url);
    };
  });
}

async function objectUrlWasCreatedFromBlob(page: Page, objectUrl: string | null, type: string, size: number) {
  return page.evaluate(({ url, expectedType, expectedSize }) => {
    const runtimeWindow = window as typeof window & {
      __createdEvidenceObjectUrls?: Array<{ url: string; isBlob: boolean; type: string; size: number }>;
    };
    return runtimeWindow.__createdEvidenceObjectUrls?.some(
      (entry) => entry.url === url && entry.isBlob && entry.type === expectedType && entry.size === expectedSize
    ) ?? false;
  }, { url: objectUrl, expectedType: type, expectedSize: size });
}

async function wasObjectUrlRevoked(page: Page, objectUrl: string | null) {
  return page.evaluate((url) => {
    const runtimeWindow = window as typeof window & { __revokedEvidenceObjectUrls?: string[] };
    return url !== null && (runtimeWindow.__revokedEvidenceObjectUrls ?? []).includes(url);
  }, objectUrl);
}

async function expectRuntimePrivacy(page: Page) {
  expect(await findProhibitedGuidOccurrences(page)).toEqual([]);
  const html = await page.locator("body").evaluate((body) => body.outerHTML);
  expect(html).not.toMatch(/object key|bucket|checksum|signed url|scanner endpoint|provider credential|storage endpoint/i);
  expect(html).not.toMatch(/data:image\/(?:png|jpeg);base64/i);

  const storageValues = await page.evaluate(() => [
    ...Object.entries(localStorage),
    ...Object.entries(sessionStorage)
  ].flat());
  expect(storageValues.join(" ")).not.toMatch(/\b[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}\b/i);
}

async function findProhibitedGuidOccurrences(page: Page) {
  return page.evaluate(() => {
    const guidPattern = /\b[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}\b/gi;
    const findings: Array<{ type: string; tag: string; attribute?: string; value: string }> = [];
    const runtimeWindow = window as typeof window & {
      __createdEvidenceObjectUrls?: Array<{ url: string; isBlob: boolean; type: string; size: number }>;
    };

    for (const element of document.querySelectorAll("*")) {
      for (const attribute of element.attributes) {
        guidPattern.lastIndex = 0;
        const permittedBlobUrl =
          element instanceof HTMLImageElement &&
          attribute.name === "src" &&
          attribute.value.startsWith("blob:") &&
          runtimeWindow.__createdEvidenceObjectUrls?.some(
            (entry) => entry.url === attribute.value && entry.isBlob && entry.type.startsWith("image/") && entry.size > 0
          );
        if (guidPattern.test(attribute.value) && !permittedBlobUrl) {
          findings.push({ type: "attribute", tag: element.tagName, attribute: attribute.name, value: attribute.value });
        }
      }

      for (const node of element.childNodes) {
        guidPattern.lastIndex = 0;
        if (node.nodeType === Node.TEXT_NODE && node.textContent && guidPattern.test(node.textContent)) {
          findings.push({ type: "text", tag: element.tagName, value: node.textContent });
        }
      }

      if (element instanceof HTMLInputElement || element instanceof HTMLTextAreaElement || element instanceof HTMLSelectElement) {
        guidPattern.lastIndex = 0;
        if (guidPattern.test(element.value)) {
          findings.push({ type: "form-value", tag: element.tagName, value: element.value });
        }
      }
    }

    return findings;
  });
}
