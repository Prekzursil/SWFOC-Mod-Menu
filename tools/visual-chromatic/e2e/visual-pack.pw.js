import { expect, test } from '@chromatic-com/playwright';

test('visual evidence gallery renders', async ({ page }) => {
  await page.goto('/tools/visual-chromatic/site/index.html', { waitUntil: 'domcontentloaded' });
  await expect(page.getByRole('heading', { name: /visual pack overview/i })).toBeVisible();
  await expect(page.getByAltText('Baseline main capture')).toBeVisible();
  await expect(page.getByAltText('Candidate main capture')).toBeVisible();
  await expect(page.getByAltText('Candidate new capture')).toBeVisible();
});
