import assert from "node:assert/strict";
import { chromium } from "playwright";

const username = required("I022_PROOF_USERNAME");
const password = required("I022_PROOF_PASSWORD");
const managementPlatformUrl = required("I022_MANAGEMENT_PLATFORM_URL");
const operatorConsoleUrl = required("I022_OPERATOR_CONSOLE_URL");

const browser = await chromium.launch({ headless: false });
try {
  const managementContext = await browser.newContext();
  const managementPage = await managementContext.newPage();
  await managementPage.goto(managementPlatformUrl, { waitUntil: "networkidle" });
  await managementPage.getByLabel("Username").fill(username);
  await managementPage.getByLabel("Password").fill(password);
  await managementPage.getByRole("button", { name: "Sign in", exact: true }).click();
  await managementPage.getByRole("button", { name: "Sign out", exact: true }).waitFor();
  assert.match(await managementPage.locator("body").innerText(), /I-022 Browser Proof User/);

  const managementSession = await readSession(managementPage);
  assert.equal(managementSession.audience, "MANAGEMENT_PLATFORM");
  assert.ok(Array.isArray(managementSession.permissions));
  assert.ok(Array.isArray(managementSession.siteReferences));
  assert.ok(Array.isArray(managementSession.siteGroupReferences));
  assert.equal(managementSession.hasGlobalScope, false);

  const usersResponse = managementPage.waitForResponse((response) =>
    response.request().method() === "GET" &&
    response.url().includes("/v1/management-platform/identity/users")
  );
  await managementPage.getByRole("button", { name: /User Administration/ }).click();
  await managementPage.getByRole("heading", { name: "User Administration" }).waitFor();
  assert.equal((await usersResponse).status(), 200);

  await managementPage.goto(operatorConsoleUrl, { waitUntil: "networkidle" });
  await managementPage.getByRole("heading", { name: "Staff sign in" }).waitFor();

  const operatorContext = await browser.newContext();
  const operatorPage = await operatorContext.newPage();
  await operatorPage.goto(operatorConsoleUrl, { waitUntil: "networkidle" });
  await operatorPage.getByLabel("Username").fill(username);
  await operatorPage.getByLabel("Password").fill(password);
  await operatorPage.getByRole("button", { name: "Sign in", exact: true }).click();
  await operatorPage.getByRole("button", { name: "Sign out", exact: true }).waitFor();
  assert.match(await operatorPage.locator("body").innerText(), /I-022 Browser Proof User/);

  const operatorSession = await readSession(operatorPage);
  assert.equal(operatorSession.audience, "OPERATOR_CONSOLE");
  assert.equal(operatorSession.hasGlobalScope, false);

  await operatorPage.goto(managementPlatformUrl, { waitUntil: "networkidle" });
  await operatorPage.getByRole("heading", { name: "Sign-in service unavailable" }).waitFor();

  await managementPage.goto(managementPlatformUrl, { waitUntil: "networkidle" });
  await managementPage.getByRole("button", { name: "Sign out", exact: true }).click();
  await managementPage.getByRole("heading", { name: "Sign in", exact: true }).waitFor();

  await operatorPage.goto(operatorConsoleUrl, { waitUntil: "networkidle" });
  await operatorPage.getByRole("button", { name: "Sign out", exact: true }).click();
  await operatorPage.getByRole("heading", { name: "Staff sign in" }).waitFor();

  await managementContext.close();
  await operatorContext.close();
  console.log("I-022 hosted Management Platform and Operator Console browser proof passed.");
} finally {
  await browser.close();
}

async function readSession(page) {
  return page.evaluate(async () => {
    const response = await fetch("/v1/human-authentication/session", {
      credentials: "include",
      headers: { Accept: "application/json" }
    });
    if (!response.ok) throw new Error(`Current-session read failed with ${response.status}.`);
    const body = await response.json();
    return body.session ?? body;
  });
}

function required(name) {
  const value = process.env[name];
  if (!value) throw new Error(`Required environment variable ${name} is missing.`);
  return value;
}
