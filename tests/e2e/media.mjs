// Screenshots and walkthrough frames for the README, from the real stack with realistic data.
// Usage: AUTH_ATTEMPTS_PER_MINUTE=1000 docker compose up --build -d && node media.mjs <output-dir>
import { chromium } from "playwright";
import { mkdirSync } from "node:fs";

const BASE = process.env.BASE_URL ?? "http://localhost:8080";
const OUT = (process.argv[2] ?? new URL("./output/media/", import.meta.url).pathname).replace(/\/?$/, "/");
mkdirSync(`${OUT}frames`, { recursive: true });
const run = Date.now().toString(36);
const password = "media-password-1";
const sleep = (ms) => new Promise((resolve) => setTimeout(resolve, ms));

async function call(path, token, body, method = "POST") {
  const response = await fetch(BASE + path, {
    method,
    headers: { "Content-Type": "application/json", ...(token ? { Authorization: `Bearer ${token}` } : {}) },
    body: body ? JSON.stringify(body) : undefined,
  });
  if (!response.ok) throw new Error(`${method} ${path}: ${response.status} ${await response.text()}`);
  return response.status === 204 ? null : response.json();
}

const names = { ayse: "Ayşe Yılmaz", mehmet: "Mehmet Demir", zeynep: "Zeynep Kaya", can: "Can Öztürk", elif: "Elif Şahin" };
const people = {};
for (const [key, name] of Object.entries(names)) {
  const email = `${key}.${run}@example.com`;
  const session = await call("/api/auth/register", null, { email, displayName: name, password });
  people[key] = { ...session.user, token: session.accessToken, email };
}

const direct = async (a, b) => call("/api/conversations/direct", people[a].token, { userId: people[b].id });
const say = async (who, conversation, body) => {
  await call(`/api/conversations/${conversation.id}/messages`, people[who].token, { clientMessageId: crypto.randomUUID(), body });
  await sleep(250);
};
const read = (who, conversation, seq) => call(`/api/conversations/${conversation.id}/read`, people[who].token, { seq });

// The conversations Ayşe has, oldest activity first so the list ends up in a natural order.
const elif = await direct("ayse", "elif");
await say("elif", elif, "Sunum dosyasını paylaştım, akşam bakabilir misin?");
await say("ayse", elif, "Tabii, yarın sabah dönerim.");
await read("elif", elif, 2);

const zeynep = await direct("ayse", "zeynep");
await say("zeynep", zeynep, "Cuma günkü toplantı 14:00'e alındı.");
await say("zeynep", zeynep, "Takvime ekledim.");

const team = await call("/api/conversations/groups", people.ayse.token, {
  title: "Kargo ekibi",
  memberIds: [people.mehmet.id, people.zeynep.id, people.can.id],
});
await say("ayse", team, "Günaydın! Bugün 140 sipariş çıkacak, depoda son durum nedir?");
await say("can", team, "Paketleme bitti, 12'de kuryeye teslim ediyoruz.");
await say("zeynep", team, "İzmir'e giden 8 sipariş için adres teyidi bekliyorum.");
await say("mehmet", team, "Teyit gelmeyenleri yarına bırakalım mı?");
await read("ayse", team, 2);
await read("can", team, 4);

const chat = await direct("ayse", "mehmet");
await say("mehmet", chat, "Selam Ayşe, SIP-1001 numaralı sipariş müşteriden geri döndü.");
await say("ayse", chat, "Neden döndü? Adres mi yanlıştı?");
await say("mehmet", chat, "Apartman numarası eksikmiş. Müşteriyle konuştum, doğrusu: Atatürk Cad. No: 24/5.");
await say("ayse", chat, "Süper, yeniden çıkışını bugün yapalım.\nKargo takip numarasını da ona iletelim.");
await read("mehmet", chat, 4);

const browser = await chromium.launch();
async function signedIn(person, viewport) {
  const context = await browser.newContext({ viewport, locale: "en-GB", deviceScaleFactor: 1 });
  const page = await context.newPage();
  await page.goto(BASE);
  await page.getByLabel("Email").fill(person.email);
  await page.getByLabel("Password").fill(password);
  await page.getByRole("button", { name: "Sign in" }).click();
  await page.locator(".conversation-row").first().waitFor();
  return page;
}

const shot = async (page, name) => {
  await sleep(300);
  await page.screenshot({ path: `${OUT}${name}.png` });
  console.log(`saved ${name}.png`);
};

// Sign-in page.
{
  const context = await browser.newContext({ viewport: { width: 1280, height: 800 } });
  const page = await context.newPage();
  await page.goto(BASE);
  await page.getByRole("heading", { name: "Sign in" }).waitFor();
  await shot(page, "sign-in");
  await context.close();
}

// Ayşe on a laptop, Mehmet next to her; the group has unread messages for Ayşe.
const ayse = await signedIn(people.ayse, { width: 1280, height: 800 });
const mehmet = await signedIn(people.mehmet, { width: 1280, height: 800 });
await ayse.locator(`[data-conversation="${names.mehmet}"]`).click();
await ayse.locator(".bubble").filter({ hasText: "Kargo takip numarasını" }).first().waitFor();
await ayse.getByLabel("Read", { exact: true }).first().waitFor();
await shot(ayse, "conversation");

await mehmet.locator(`[data-conversation="${names.ayse}"]`).click();
await mehmet.getByLabel("Message", { exact: true }).pressSequentially("Tamam, hemen", { delay: 30 });
await ayse.getByText(/is typing…/).waitFor();
await shot(ayse, "typing");
await mehmet.getByLabel("Message", { exact: true }).fill("");

await ayse.locator(`[data-conversation="Kargo ekibi"]`).click();
await ayse.locator(".bubble").filter({ hasText: "Teyit gelmeyenleri" }).first().waitFor();
await shot(ayse, "group");

await ayse.getByRole("button", { name: "New", exact: true }).click();
await ayse.getByLabel("Search people").fill("Elif");
await ayse.locator("dialog").getByRole("button", { name: names.elif }).click();
await shot(ayse, "new-conversation");
await ayse.keyboard.press("Escape");

// Phone.
{
  const phone = await signedIn(people.ayse, { width: 390, height: 844 });
  await shot(phone, "phone-list");
  await phone.locator(`[data-conversation="${names.mehmet}"]`).click();
  await phone.locator(".bubble").filter({ hasText: "Kargo takip numarasını" }).first().waitFor();
  await shot(phone, "phone");
  await phone.context().close();
}

// API reference.
{
  const context = await browser.newContext({ viewport: { width: 1280, height: 800 } });
  const page = await context.newPage();
  await page.goto(`${BASE}/scalar`);
  await page.waitForLoadState("networkidle");
  await sleep(1500);
  await shot(page, "api-reference");
  await context.close();
}

// Walkthrough frames: Ayşe and Mehmet side by side, each in a narrow window.
await ayse.setViewportSize({ width: 640, height: 720 });
await mehmet.setViewportSize({ width: 640, height: 720 });
let frame = 0;
async function frames(count = 1) {
  for (let i = 0; i < count; i++) {
    const n = String(frame++).padStart(3, "0");
    await Promise.all([
      ayse.screenshot({ path: `${OUT}frames/${n}-a.png` }),
      mehmet.screenshot({ path: `${OUT}frames/${n}-b.png` }),
    ]);
  }
}

// The narrow window shows one pane: go back to the list if needed and open the direct conversation on both sides.
for (const [page, other] of [[ayse, names.mehmet], [mehmet, names.ayse]]) {
  if (await page.getByLabel("Back to conversations").isVisible()) await page.getByLabel("Back to conversations").click();
  await page.locator(`[data-conversation="${other}"]`).click();
  await page.getByLabel("Back to conversations").waitFor();
}
await sleep(500);
await frames(4);
await mehmet.getByLabel("Message", { exact: true }).pressSequentially("Kargo şirketi 14:00'te alacak.", { delay: 45 });
await ayse.getByText(/is typing…/).waitFor();
await frames(4);
await mehmet.getByLabel("Message", { exact: true }).press("Enter");
await ayse.locator(".bubble").filter({ hasText: "Kargo şirketi 14:00'te alacak." }).first().waitFor();
await sleep(400);
await frames(5);
await ayse.getByLabel("Message", { exact: true }).pressSequentially("Harika, teşekkürler!", { delay: 45 });
await mehmet.getByText(/is typing…/).waitFor();
await frames(3);
await ayse.getByLabel("Message", { exact: true }).press("Enter");
await mehmet.locator(".bubble").filter({ hasText: "Harika, teşekkürler!" }).first().waitFor();
await ayse.locator(".bubble.mine").filter({ hasText: "Harika" }).getByLabel("Read", { exact: true }).waitFor();
await sleep(400);
await frames(8);
console.log(`saved ${frame} frame pairs`);

await browser.close();
