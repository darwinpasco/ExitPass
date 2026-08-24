import { expect, test, type Page, type Request } from "@playwright/test";

const workspacePath = "/operator-console/statutory-discounts/61000000-0000-0000-0000-000000000002";
const evidenceDeniedPath = "/operator-console/statutory-discounts/61000000-0000-0000-0000-000000000017";
const prohibitedAuthorityHeaders = [
  "authorization",
  "x-operator-user-id",
  "x-exitpass-user-id",
  "x-exitpass-permissions",
  "x-operator-device-binding-id",
  "x-operator-shift-id",
  "x-site-id",
  "x-site-group-id"
];

test.describe("Operator Console I-020 human authentication", () => {
  test("ordinary operator signs in without MFA and refresh rediscovers the server session", async ({ page }) => {
    const requests: Request[] = [];
    page.on("request", (request) => requests.push(request));

    await page.goto(`${workspacePath}?auth=logged-out`);
    await expect(page.getByRole("heading", { name: "Staff sign in" })).toBeVisible();
    await expect(page.getByLabel("Username")).toBeFocused();
    await expect(page.getByLabel(/TOTP|one-time|verification code/i)).toHaveCount(0);

    await page.getByLabel("Username").fill("review.operator");
    await page.getByLabel("Password").fill("operator-password");
    await page.getByRole("button", { name: "Sign in" }).click();

    await expect(page.getByRole("heading", { name: "Person with Disability" })).toBeVisible();
    await expect(page.getByLabel("Operator identity")).toContainText("Review Operator");
    await expect(page.getByLabel("Operator identity")).toContainText("review.operator");
    await expect(page.getByLabel("Operator identity")).toContainText("1 Site, 1 Site Group");
    await expect(page.getByLabel(/TOTP|one-time|verification code/i)).toHaveCount(0);
    expect(await findApplicationGuidOccurrences(page)).toEqual([]);

    const loginRequest = requests.find((request) => request.url().endsWith("/v1/human-authentication/login"));
    expect(loginRequest?.postDataJSON()).toEqual({
      username: "review.operator",
      password: "operator-password",
      audience: "OPERATOR_CONSOLE"
    });

    await page.reload();
    await expect(page.getByRole("heading", { name: "Person with Disability" })).toBeVisible();
    await expect(page.getByLabel("Operator identity")).toContainText("Review Operator");
    expect(await findApplicationGuidOccurrences(page)).toEqual([]);
    expect(requests.filter((request) => request.url().endsWith("/v1/human-authentication/login"))).toHaveLength(1);
    expect(requests.filter((request) => request.url().endsWith("/v1/human-authentication/session")).length).toBeGreaterThanOrEqual(2);
    expect(prohibitedHeaders(requests)).toEqual([]);
    await expectNoAuthenticationAuthorityInStorage(page);
  });

  test("invalid credentials and throttling remain anti-enumerating", async ({ page }) => {
    await page.goto("/operator-console?auth=logged-out");
    await signIn(page, "unknown.operator", "wrong-password");
    await expect(page.getByRole("alert")).toHaveText("The username or password could not be verified.");
    await expect(page.locator("body")).not.toContainText(/does not exist|unknown user|password incorrect/i);

    await signIn(page, "throttled.operator", "any-password");
    await expect(page.getByRole("alert")).toHaveText("Sign-in attempts are temporarily limited. Wait and try again.");
    await expect(page.getByLabel(/TOTP|one-time|verification code/i)).toHaveCount(0);
    await expectNoAuthenticationAuthorityInStorage(page);
  });

  test("logout sends bounded-memory CSRF and clears the protected workspace", async ({ page }) => {
    const requests: Request[] = [];
    page.on("request", (request) => requests.push(request));
    await page.goto(workspacePath);
    await expect(page.getByRole("heading", { name: "Person with Disability" })).toBeVisible();

    await page.getByRole("button", { name: "Sign out" }).click();
    await expect(page.getByRole("heading", { name: "Staff sign in" })).toBeVisible();
    await expect(page.getByRole("alert")).toHaveText("You have signed out.");
    await expect(page.getByRole("heading", { name: "Person with Disability" })).toHaveCount(0);

    const logoutRequest = requests.find((request) => request.url().endsWith("/v1/human-authentication/logout"));
    expect(logoutRequest?.method()).toBe("POST");
    expect(logoutRequest?.headers()["x-csrf-token"]).toBe("operator-console-fixture-csrf");
    expect(prohibitedHeaders(requests, ["x-csrf-token"])).toEqual([]);
    await page.reload();
    await expect(page.getByRole("heading", { name: "Staff sign in" })).toBeVisible();
    await expectNoAuthenticationAuthorityInStorage(page);
  });

  test("expired and revoked sessions fail closed without restoring browser authority", async ({ page }) => {
    for (const [mode, message] of [
      ["expired", "Your session expired. Sign in again."],
      ["revoked", "Your session ended. Sign in again."]
    ] as const) {
      await page.goto(`/operator-console?auth=${mode}`);
      await expect(page.getByRole("heading", { name: "Staff sign in" })).toBeVisible();
      await expect(page.getByRole("alert")).toHaveText(message);
      await expect(page.locator(".platformShell")).toHaveCount(0);
      await expectNoAuthenticationAuthorityInStorage(page);
    }
  });

  test("a backend 403 remains an authenticated safe denial", async ({ page }) => {
    await page.goto(evidenceDeniedPath);
    await expect(page.getByRole("alert")).toContainText("no longer have access");
    await expect(page.getByRole("button", { name: "Sign out" })).toBeVisible();
    await expect(page.getByRole("heading", { name: "Staff sign in" })).toHaveCount(0);
  });

  test("revocation during a protected workflow locks the workspace without replay", async ({ page }) => {
    let evidenceMetadataRequests = 0;
    page.on("request", (request) => {
      if (request.url().endsWith("/evidence")) evidenceMetadataRequests += 1;
    });
    await page.goto(workspacePath);
    await expect(page.getByRole("heading", { name: "Person with Disability" })).toBeVisible();
    const requestsBeforeRevocation = evidenceMetadataRequests;

    await page.context().addCookies([{
      name: "operator_console_fixture_session",
      value: "revoked",
      url: new URL(page.url()).origin,
      httpOnly: true,
      sameSite: "Strict"
    }]);
    await page.getByRole("button", { name: "Refresh secure evidence" }).click();

    await expect(page.getByRole("heading", { name: "Staff sign in" })).toBeVisible();
    await expect(page.getByRole("alert")).toHaveText("Your session ended. Sign in again.");
    await expect(page.getByRole("heading", { name: "Person with Disability" })).toHaveCount(0);
    expect(evidenceMetadataRequests).toBe(requestsBeforeRevocation + 1);
    await page.waitForTimeout(150);
    expect(evidenceMetadataRequests).toBe(requestsBeforeRevocation + 1);
  });

  test("decision requests carry workflow facts but no browser-authored reviewer authority", async ({ page }) => {
    const requests: Request[] = [];
    page.on("request", (request) => requests.push(request));
    await page.goto(workspacePath);
    await page.getByLabel(/I reviewed the required evidence/i).check();
    await page.getByRole("button", { name: "Approve" }).click();
    await page.getByRole("button", { name: "Confirm decision" }).click();
    await expect(page.getByText(/Decision recorded: APPROVED/i)).toBeVisible();

    const decisionRequest = requests.find(
      (request) => request.method() === "POST" && /\/statutory-discounts\/reviews\/[^/]+\/decision$/.test(new URL(request.url()).pathname)
    );
    expect(decisionRequest).toBeDefined();
    const body = decisionRequest?.postDataJSON() as Record<string, unknown>;
    expect(body).not.toHaveProperty("userId");
    expect(body).not.toHaveProperty("reviewerUserId");
    expect(body).not.toHaveProperty("ReviewerUserId");
    expect(body).not.toHaveProperty("permissions");
    expect(prohibitedHeaders(requests)).toEqual([]);
    expect(requests.some((request) => /statutory-discounts\/.*apply/i.test(request.url()))).toBe(false);
  });

  test("login remains keyboard-usable and responsive at 390 by 844", async ({ page }) => {
    await page.setViewportSize({ width: 390, height: 844 });
    await page.goto("/operator-console?auth=logged-out");
    await expect(page.getByLabel("Username")).toBeFocused();
    await page.keyboard.type("review.operator");
    await page.keyboard.press("Tab");
    await expect(page.getByLabel("Password")).toBeFocused();
    await page.keyboard.type("operator-password");
    await page.keyboard.press("Tab");
    await expect(page.getByRole("button", { name: "Sign in" })).toBeFocused();
    await page.keyboard.press("Enter");
    await expect(page.getByRole("heading", { name: "Operator Console" })).toBeVisible();
    expect(await page.evaluate(() => document.documentElement.scrollWidth <= document.documentElement.clientWidth + 1)).toBe(true);
  });
});

async function signIn(page: Page, username: string, password: string) {
  await page.getByLabel("Username").fill(username);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
}

function prohibitedHeaders(requests: Request[], allowed: string[] = []) {
  const allow = new Set(allowed);
  return requests.flatMap((request) =>
    prohibitedAuthorityHeaders
      .filter((header) => !allow.has(header) && request.headers()[header] !== undefined)
      .map((header) => `${request.method()} ${new URL(request.url()).pathname}: ${header}`)
  );
}

async function expectNoAuthenticationAuthorityInStorage(page: Page) {
  const storage = await page.evaluate(async () => ({
    local: Object.entries(localStorage),
    session: Object.entries(sessionStorage),
    indexedDb: await indexedDB.databases(),
    caches: "caches" in window ? await caches.keys() : []
  }));
  expect(storage).toEqual({ local: [], session: [], indexedDb: [], caches: [] });
}

async function findApplicationGuidOccurrences(page: Page) {
  return page.evaluate(() => {
    const pattern = /\b[0-9a-f]{8}(?:-[0-9a-f]{4}){3}-[0-9a-f]{12}\b/gi;
    const findings: Array<{ type: string; tag: string; value: string }> = [];
    for (const element of document.querySelectorAll("*")) {
      for (const attribute of element.attributes) {
        pattern.lastIndex = 0;
        if (pattern.test(attribute.value) && !(attribute.name === "src" && attribute.value.startsWith("blob:"))) {
          findings.push({ type: `attribute:${attribute.name}`, tag: element.tagName, value: attribute.value });
        }
      }
      for (const node of element.childNodes) {
        pattern.lastIndex = 0;
        if (node.nodeType === Node.TEXT_NODE && node.textContent && pattern.test(node.textContent)) {
          findings.push({ type: "text", tag: element.tagName, value: node.textContent });
        }
      }
    }
    return findings;
  });
}
