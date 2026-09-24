import { randomUUID } from 'node:crypto';
import { test, expect, signIn, waitForInteractive } from './host.fixture';

test('browser sign-in, client create/read/update, and attachment upload/read/delete', async ({ page, host }) => {
  const clientName = `Browser Smoke ${randomUUID().slice(0, 8)}`;
  const updatedName = `${clientName} Updated`;
  const fileName = `browser-attachment-${randomUUID().slice(0, 8)}.txt`;
  const content = Buffer.from('Quickfire browser smoke attachment. Synthetic data only.\n', 'utf8');

  await signIn(page, host);
  await page.goto('/Clients/Create');
  await waitForInteractive(page);
  await page.locator('#clientName').fill(clientName);
  await page.locator('#clientName').press('Tab');
  await page.locator('#state').fill('CA');
  await page.locator('#state').press('Tab');
  await page.locator('#firstname').fill('Synthetic');
  await page.locator('#firstname').press('Tab');
  await page.getByRole('button', { name: 'Create Client', exact: true }).click();
  await expect(page).toHaveURL(/\/Clients\/\d+\/?$/);
  const clientPath = new URL(page.url()).pathname;
  const clientId = clientPath.match(/\d+/)![0];
  await expect(page.getByText(clientName, { exact: true }).first()).toBeVisible();

  await page.goto(`/Clients/Edit/${clientId}`);
  await waitForInteractive(page);
  await page.locator('#clientName').fill(updatedName);
  await page.locator('#clientName').press('Tab');
  await page.getByRole('button', { name: 'Update Client', exact: true }).click();
  await expect(page).toHaveURL(new RegExp(`/Clients/${clientId}/?$`));
  await page.reload();
  await waitForInteractive(page);
  await expect(page.getByText(updatedName, { exact: true }).first()).toBeVisible();

  const attachmentsTab = page.getByRole('tab', { name: 'Attachments', exact: true });
  await attachmentsTab.click();
  await expect(attachmentsTab).toHaveAttribute('aria-selected', 'true');
  const attachments = page.getByRole('tabpanel').filter({ has: page.locator('.attachment-grid-wrapper') });
  const uploadInput = attachments.locator('input[type="file"]');
  // The uploader's DOM is server-rendered before its change handler is ready.
  // Syncfusion creates keyboardModule at the end of wireEvents; observe that
  // binding on this exact input without calling or altering vendor internals.
  await expect.poll(() => uploadInput.evaluate(element =>
    Object.values((window as any).sfBlazor?.instances ?? {}).some((instance: any) =>
      instance.element === element && element.isConnected && Boolean(instance.keyboardModule))))
    .toBe(true);
  await uploadInput.setInputFiles({ name: fileName, mimeType: 'text/plain', buffer: content });
  // Other tabs also contain uploaders with this legacy dialog ID.
  const uploadDialog = attachments.locator('#attachDialog');
  // Fluent's fixed-position shadow content is visible while its host has no box.
  const saveUpload = uploadDialog.getByRole('button', { name: 'Save', exact: true });
  await expect(saveUpload).toBeVisible();
  await saveUpload.click();
  await expect(saveUpload).not.toBeVisible();
  const row = attachments.getByRole('row').filter({ hasText: fileName });
  await expect(row).toBeVisible();
  await row.getByTitle('Preview', { exact: true }).click();
  const preview = page.locator('fluent-dialog:not([hidden])').filter({ has: page.locator('.preview-container') });
  await expect(preview.locator('.attachment-filename')).toHaveText(fileName);
  const downloadUrl = await preview.locator('a[href][target="_blank"]').getAttribute('href');
  expect(Boolean(downloadUrl)).toBe(true);
  const download = await page.request.get(new URL(downloadUrl!, host.url).toString());
  expect(download.ok()).toBe(true);
  expect(await download.body()).toEqual(content);
  await preview.getByRole('button', { name: 'Close', exact: true }).click();
  await row.getByTitle('Delete', { exact: true }).click();
  const deleteDialog = page.locator('fluent-dialog:not([hidden])').filter({ has: page.locator('.delete-attachment-content') });
  await deleteDialog.getByRole('button', { name: 'Delete', exact: true }).click();
  await expect(row).toHaveCount(0);
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth);
  expect(overflow).toBe(false);
  await expect(page.locator('#blazor-error-ui')).not.toBeVisible();
});

test('profile issues a helper key once, selects the intended helper, and revokes it', async ({ page, host }) => {
  await signIn(page, host);
  await page.goto('/Profile');
  await waitForInteractive(page);
  const helpers = page.locator('section.connected-helpers');
  await expect(helpers).toBeVisible();
  for (const name of ['Browser helper one', 'Browser helper two']) {
    await helpers.getByLabel('Helper name', { exact: true }).fill(name);
    await helpers.getByRole('button', { name: 'Create helper key', exact: true }).click();
    // Never put a credential into an assertion message, trace, screenshot, or attachment.
    try {
      await expect(helpers.getByText('Save this key now. It will not be shown again.', { exact: true })).toBeVisible();
      const hasCredential = (await helpers.getByRole('textbox', { name: 'Helper key', exact: true }).inputValue()).length > 32;
      expect(hasCredential).toBe(true);
    } finally {
      const dismiss = helpers.getByRole('button', { name: "I've saved the key", exact: true });
      try {
        if (await dismiss.isVisible()) {
          await dismiss.click({ timeout: 5000 });
          await expect(helpers.getByRole('textbox', { name: 'Helper key', exact: true })).toHaveCount(0, { timeout: 5000 });
        }
      } finally {
        // A broken circuit must not leave a key in Playwright's failure context snapshot.
        if (!page.isClosed() && await helpers.getByRole('textbox', { name: 'Helper key', exact: true }).count()) await page.goto('about:blank');
      }
    }
    await expect(helpers.getByRole('textbox', { name: 'Helper key', exact: true })).toHaveCount(0);
  }
  const first = helpers.getByRole('listitem').filter({ hasText: 'Browser helper one' });
  const second = helpers.getByRole('listitem').filter({ hasText: 'Browser helper two' });
  await expect(first).toContainText('Selected for Office actions');
  await expect(second).toContainText('Key active');
  await second.getByRole('button', { name: 'Use for Office actions', exact: true }).click();
  await expect(second).toContainText('Selected for Office actions');
  await expect(first).not.toContainText('Selected for Office actions');
  await page.reload();
  await waitForInteractive(page);
  await expect(helpers.getByRole('textbox', { name: 'Helper key', exact: true })).toHaveCount(0);
  await second.getByRole('button', { name: 'Revoke', exact: true }).click();
  await expect(second).toContainText('Revoked');
  await expect(second.getByRole('button', { name: 'Use for Office actions', exact: true })).toHaveCount(0);
  const overflow = await page.evaluate(() => document.documentElement.scrollWidth > document.documentElement.clientWidth);
  expect(overflow).toBe(false);
  await expect(page.locator('#blazor-error-ui')).not.toBeVisible();
});

test('a deleted account returns its existing session to a usable login page', async ({ page, host }) => {
  await signIn(page, host);
  // A valid existing session still follows the normal login return URL.
  await page.goto('/Account/Login?returnUrl=%2FHome');
  await expect(page).toHaveURL(/\/Home$/);
  await waitForInteractive(page);
  const hasSessionCookie = (await page.context().cookies()).some(cookie => cookie.name === '.AspNetCore.Identity.Application');
  expect(hasSessionCookie).toBe(true);

  // Each test has a fresh disposable host. Only its synthetic account is removed.
  await page.goto('/Users');
  await waitForInteractive(page);
  const userRow = page.locator('.users-table__row').filter({ hasText: host.email });
  await userRow.locator('.users-grid__delete').click();
  const confirmation = page.locator('fluent-dialog:not([hidden])').filter({ hasText: 'This immediately revokes their access.' });
  await confirmation.getByRole('button', { name: 'Delete', exact: true }).click();
  await expect(userRow).toHaveCount(0);

  await page.goto('/Home');
  await expect(page).toHaveURL(/\/Account\/Login\?returnUrl=/);
  await expect(page.locator('[id="Input.Email"]')).toBeVisible();
  const keepsDeletedSession = (await page.context().cookies()).some(cookie => cookie.name === '.AspNetCore.Identity.Application');
  expect(keepsDeletedSession).toBe(false);
  await page.reload();
  await expect(page.locator('[id="Input.Password"]')).toBeVisible();
  await page.locator('[id="Input.Email"]').fill(host.email);
  await page.locator('[id="Input.Password"]').fill(host.password);
  const [loginAttempt] = await Promise.all([
    page.waitForResponse(response => response.request().method() === 'POST' && new URL(response.url()).pathname === '/Account/Login'),
    page.getByRole('button', { name: 'Log in', exact: true }).click(),
  ]);
  expect(loginAttempt.status()).toBe(200);
  await expect(page.getByRole('alert').filter({ hasText: 'Error: Invalid login attempt.' })).toBeVisible();
  await expect(page.locator('#blazor-error-ui')).not.toBeVisible();
});
