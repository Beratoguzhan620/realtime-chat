// Two people chat in two browsers against the Compose stack (nginx in front of two API containers).
// Usage: docker compose up --build -d && cd tests/e2e && npm install && npx playwright install chromium && node chat.mjs
import { chromium } from "playwright";
import { mkdirSync } from "node:fs";

const BASE = process.env.BASE_URL ?? "http://localhost:8080";
const OUT = new URL("./output/", import.meta.url).pathname;
mkdirSync(OUT, { recursive: true });

const run = Date.now().toString(36);
const people = {
  ayse: { name: `Ayşe Yılmaz ${run}`, email: `ayse.${run}@example.com` },
  mehmet: { name: `Mehmet Demir ${run}`, email: `mehmet.${run}@example.com` },
};
const password = "e2e-test-password";

const browser = await chromium.launch();
const errors = [];
let failures = 0;

async function step(name, action) {
  try {
    await action();
    console.log(`ok   ${name}`);
  } catch (error) {
    failures++;
    console.log(`FAIL ${name}\n     ${String(error.message ?? error).split("\n")[0]}`);
  }
}

async function open(viewport = { width: 1280, height: 800 }) {
  const context = await browser.newContext({ viewport, locale: "en-GB" });
  const page = await context.newPage();
  page.on("pageerror", (error) => errors.push(error.message));
  page.on("console", (message) => {
    if (message.type() === "error") errors.push(message.text());
  });
  return page;
}

async function register(page, person) {
  await page.goto(BASE);
  await page.getByRole("button", { name: "New here? Create an account" }).click();
  await page.getByLabel("Email").fill(person.email);
  await page.getByLabel("Display name").fill(person.name);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Create account" }).click();
  await page.getByText("Online", { exact: true }).first().waitFor();
}

const ayse = await open();
const mehmet = await open();

await step("the sign-in page comes first and refuses a wrong password", async () => {
  await ayse.goto(BASE);
  await ayse.getByRole("heading", { name: "Sign in" }).waitFor();
  await ayse.getByLabel("Email").fill("nobody@example.com");
  await ayse.getByLabel("Password").fill("wrong-password-1");
  await ayse.getByRole("button", { name: "Sign in" }).click();
  await ayse.getByRole("alert").filter({ hasText: "incorrect" }).waitFor();
});

await step("two people register and see an empty list", async () => {
  await register(ayse, people.ayse);
  await register(mehmet, people.mehmet);
  await ayse.getByText("No conversations yet").waitFor();
});

await step("Ayşe finds Mehmet by name and starts a chat", async () => {
  await ayse.getByRole("button", { name: "New", exact: true }).click();
  await ayse.getByLabel("Search people").fill(`Mehmet Demir ${run}`);
  await ayse.getByRole("button", { name: people.mehmet.name }).click();
  await ayse.getByRole("button", { name: "Start chat" }).click();
  await ayse.getByRole("heading", { name: people.mehmet.name }).waitFor();
  await ayse.getByText("No messages yet").waitFor();
});

await step("the new conversation appears for Mehmet without a reload, with him shown online", async () => {
  await mehmet.locator(`[data-conversation="${people.ayse.name}"]`).waitFor();
  await ayse.locator(".conversation-header .online-dot").waitFor();
});

await step("a message arrives live and shows as unread", async () => {
  await ayse.getByLabel("Message").fill("Merhaba! Kargo bugün çıktı mı?");
  await ayse.getByLabel("Message").press("Enter");
  await ayse.locator(".bubble.mine").filter({ hasText: "Kargo bugün" }).waitFor();
  const row = mehmet.locator(`[data-conversation="${people.ayse.name}"]`);
  await row.getByLabel("1 unread").waitFor();
  await row.getByText("Merhaba! Kargo bugün çıktı mı?").waitFor();
});

await step("opening it reads it, and Ayşe sees the read ticks", async () => {
  await mehmet.locator(`[data-conversation="${people.ayse.name}"]`).click();
  await mehmet.getByText("Merhaba! Kargo bugün çıktı mı?").waitFor();
  await ayse.getByLabel("Read", { exact: true }).waitFor();
  await mehmet.locator(`[data-conversation="${people.ayse.name}"] .badge`).waitFor({ state: "detached" });
});

await step("typing shows on the other side", async () => {
  await mehmet.getByLabel("Message").pressSequentially("Evet, akşa", { delay: 40 });
  await ayse.getByText(/is typing…/).waitFor();
});

await step("a reply with several lines keeps its line breaks", async () => {
  await mehmet.getByLabel("Message").fill("Evet, akşam teslim edilir.");
  await mehmet.getByLabel("Message").press("Shift+Enter");
  await mehmet.getByLabel("Message").pressSequentially("Takip no: YK123456789");
  await mehmet.getByLabel("Message").press("Enter");
  const bubble = ayse.locator(".bubble").filter({ hasText: "YK123456789" });
  await bubble.waitFor();
  const text = await bubble.locator(".body").innerText();
  if (!text.includes("\n")) throw new Error("the line break was lost");
});

await step("the sender edits a message and the other side sees the change", async () => {
  const bubble = ayse.locator(".bubble.mine").filter({ hasText: "Kargo bugün" });
  await bubble.hover();
  await bubble.getByLabel("Edit message").click();
  await ayse.getByLabel("Edited text").fill("Merhaba! Kargo bugün yola çıktı mı?");
  await ayse.getByLabel("Save").click();
  await mehmet.getByText("Merhaba! Kargo bugün yola çıktı mı?").waitFor();
  await mehmet.locator(".bubble").filter({ hasText: "yola çıktı" }).getByText("edited").waitFor();
});

await step("a reload keeps the session and the history", async () => {
  await mehmet.reload();
  await mehmet.locator(`[data-conversation="${people.ayse.name}"]`).click();
  await mehmet.getByText("Takip no: YK123456789").waitFor();
});

await step("messages sent while someone is away are there when they come back", async () => {
  await mehmet.close();
  for (const text of ["Bir şey daha", "Adres değişti, haber veririm"]) {
    await ayse.getByLabel("Message").fill(text);
    await ayse.getByLabel("Message").press("Enter");
    await ayse.locator(".bubble.mine").filter({ hasText: text }).locator(".ticks").waitFor();
  }
  const back = await open();
  await back.goto(BASE);
  await back.getByLabel("Email").fill(people.mehmet.email);
  await back.getByLabel("Password").fill(password);
  await back.getByRole("button", { name: "Sign in" }).click();
  const row = back.locator(`[data-conversation="${people.ayse.name}"]`);
  await row.getByLabel("2 unread").waitFor();
  await row.click();
  await back.getByText("Adres değişti, haber veririm").waitFor();
  await back.close();
});

await step("the layout holds on a phone", async () => {
  const phone = await open({ width: 390, height: 844 });
  await phone.goto(BASE);
  await phone.getByLabel("Email").fill(people.ayse.email);
  await phone.getByLabel("Password").fill(password);
  await phone.getByRole("button", { name: "Sign in" }).click();
  await phone.locator(`[data-conversation="${people.mehmet.name}"]`).click();
  await phone.getByLabel("Back to conversations").waitFor();
  await phone.getByText("Adres değişti, haber veririm").waitFor();
  const overflow = await phone.evaluate(() => document.documentElement.scrollWidth > window.innerWidth);
  if (overflow) throw new Error("the page scrolls sideways");
  await phone.screenshot({ path: `${OUT}phone.png` });
  await phone.getByLabel("Back to conversations").click();
  await phone.getByLabel("Filter conversations").waitFor();
  await phone.close();
});

await step("signing out returns to the sign-in page", async () => {
  await ayse.getByLabel("Sign out").click();
  await ayse.getByRole("heading", { name: "Sign in" }).waitFor();
});

await step("no script errors along the way", async () => {
  const real = errors.filter((e) => !e.includes("401") && !e.includes("Failed to load resource"));
  if (real.length > 0) throw new Error(real.join(" | "));
});

await browser.close();
console.log(failures === 0 ? "\nall steps passed" : `\n${failures} step(s) failed`);
process.exit(failures === 0 ? 0 : 1);
