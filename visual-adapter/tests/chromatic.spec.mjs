import { expect, test } from "@chromatic-com/playwright";

const states = [
  { id: "runtime-attach-console", title: "Runtime attach console" },
  { id: "live-ops-transaction-lab", title: "Live Ops transaction lab" },
  { id: "calibration-symbol-panel", title: "Calibration symbol panel" },
  { id: "save-editor-patch-lab", title: "Save editor patch lab" },
];

for (const state of states) {
  test(`${state.title} renders in the adapter gallery`, async ({ page }) => {
    await page.goto(`/?state=${state.id}`, { waitUntil: "domcontentloaded" });
    await expect(page.getByRole("heading", { name: /SWFOC Desktop Adapter Gallery/i })).toBeVisible();
    await expect(page.getByRole("heading", { name: state.title })).toBeVisible();
    await expect(page.locator("[data-state-id]")) .toHaveCount(1);
  });
}
