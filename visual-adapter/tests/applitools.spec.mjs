import { BatchInfo, ClassicRunner, Configuration, Eyes, Target } from "@applitools/eyes-playwright";
import { expect, test } from "@playwright/test";

const states = [
  { id: "runtime-attach-console", title: "Runtime attach console" },
  { id: "live-ops-transaction-lab", title: "Live Ops transaction lab" },
  { id: "calibration-symbol-panel", title: "Calibration symbol panel" },
  { id: "save-editor-patch-lab", title: "Save editor patch lab" },
];

for (const state of states) {
  test(`${state.title} visual snapshot`, async ({ page }) => {
    const runner = new ClassicRunner();
    const eyes = new Eyes(runner);
    const configuration = new Configuration();
    configuration.setAppName("SWFOC Desktop Adapter Gallery");
    configuration.setTestName(state.title);
    configuration.setBatch(new BatchInfo(process.env.APPLITOOLS_BATCH_NAME || "SWFOC Desktop Adapter"));
    eyes.setConfiguration(configuration);

    try {
      await page.goto(`/?state=${state.id}`, { waitUntil: "domcontentloaded" });
      await expect(page.getByRole("heading", { name: state.title })).toBeVisible();
      await eyes.open(page, "SWFOC Desktop Adapter Gallery", state.title);
      await eyes.check(state.title, Target.window().fully());
      await eyes.close(false);
      await runner.getAllTestResults(false);
    } finally {
      await eyes.abortIfNotClosed();
    }
  });
}
