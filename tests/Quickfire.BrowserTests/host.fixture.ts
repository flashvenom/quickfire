import { test as base, expect, type Page } from '@playwright/test';
import { spawn, type ChildProcess } from 'node:child_process';
import { randomBytes, randomUUID } from 'node:crypto';
import { cp, mkdtemp, readFile, realpath, rm, writeFile } from 'node:fs/promises';
import { createServer } from 'node:net';
import { tmpdir } from 'node:os';
import { basename, dirname, join, resolve } from 'node:path';

export type SmokeHost = { url: string; email: string; password: string };

function browserSetting(name: string): string | undefined {
  return process.env[`QUICKFIRE_BROWSER_${name}`] ?? process.env[`OPENFIRE_BROWSER_${name}`];
}

async function freePort(): Promise<number> {
  const server = createServer();
  await new Promise<void>((ready, reject) => {
    server.once('error', reject);
    server.listen(0, '127.0.0.1', ready);
  });
  const address = server.address();
  if (!address || typeof address === 'string') throw new Error('Unable to reserve a browser-test port.');
  await new Promise<void>((ready, reject) => server.close(error => error ? reject(error) : ready()));
  return address.port;
}

function testEnvironment(host: SmokeHost, root: string): NodeJS.ProcessEnv {
  const env: NodeJS.ProcessEnv = {};
  // Do not inherit database addresses, admin credentials, desktop markers or API keys.
  const keep = /^(PATH|PATHEXT|SYSTEMROOT|WINDIR|COMSPEC|TEMP|TMP|TMPDIR|HOME|USERPROFILE|LOCALAPPDATA|APPDATA|DOTNET_ROOT(?:_X64)?|LD_LIBRARY_PATH)$/i;
  for (const [key, value] of Object.entries(process.env)) if (keep.test(key)) env[key] = value;
  return {
    ...env,
    ASPNETCORE_ENVIRONMENT: 'Production',
    DOTNET_ENVIRONMENT: 'Production',
    ASPNETCORE_URLS: host.url,
    ConnectionStrings__DefaultConnection: `Data Source=${join(root, 'browser-smoke.db')}`,
    Database__Provider: 'Sqlite',
    ADMIN_EMAIL: host.email,
    ADMIN_USERNAME: host.email,
    ADMIN_PASSWORD: host.password,
    ADMIN_FIRSTNAME: 'Browser',
    ADMIN_LASTNAME: 'Test',
    ...(browserSetting('SYNCFUSION_LICENSE')
      ? { SYNCFUSION: browserSetting('SYNCFUSION_LICENSE') } : {}),
  };
}

async function stop(child: ChildProcess): Promise<void> {
  if (child.exitCode !== null || child.signalCode !== null) return;
  const exited = new Promise<void>(done => child.once('exit', () => done()));
  child.kill();
  await Promise.race([exited, new Promise<void>(done => setTimeout(done, 5000))]);
  if (child.exitCode === null && child.signalCode === null) child.kill('SIGKILL');
}

export const test = base.extend<{ host: SmokeHost }>({
  host: [async ({}, use) => {
    const source = browserSetting('HOST_DIR');
    if (!source) throw new Error('Set QUICKFIRE_BROWSER_HOST_DIR to the published Quickfire.Blazor directory. See this folder\'s README.md.');
    const publishRoot = await realpath(resolve(source));
    await readFile(join(publishRoot, 'Quickfire.Blazor.runtimeconfig.json'));
    const root = await mkdtemp(join(tmpdir(), 'quickfire-browser-'));
    let child: ChildProcess | undefined;
    const host: SmokeHost = {
      url: `http://127.0.0.1:${await freePort()}`,
      email: `browser-${randomUUID()}@example.test`,
      password: `Browser!9a${randomBytes(24).toString('hex')}`,
    };
    let logs = '';
    try {
      await cp(publishRoot, root, {
        recursive: true,
        filter: path => !/^\.env(?:\.|$)/i.test(basename(path)) && !/\.(?:db|sqlite|sqlite3)(?:-(?:wal|shm))?$/i.test(path),
      });
      // Published developer settings must never select a non-test database or service.
      await writeFile(join(root, 'appsettings.json'), JSON.stringify({
        Database: { Provider: 'Sqlite' },
        Logging: { LogLevel: { Default: 'Warning', 'Microsoft.Hosting.Lifetime': 'Information' } },
      }));
      await writeFile(join(root, 'appsettings.Production.json'), '{}');
      child = spawn(browserSetting('DOTNET') ?? 'dotnet', [
        join(root, 'Quickfire.Blazor.dll'), '--contentRoot', root,
      ], { cwd: root, env: testEnvironment(host, root), windowsHide: true, stdio: ['ignore', 'pipe', 'pipe'] });
      const capture = (data: Buffer) => {
        logs += data.toString();
        for (const secret of [host.password, browserSetting('SYNCFUSION_LICENSE')])
          if (secret) logs = logs.replaceAll(secret, '[redacted]');
        logs = logs.slice(-8000);
      };
      child.stdout!.on('data', capture);
      child.stderr!.on('data', capture);
      let launchError: Error | undefined;
      child.once('error', error => { launchError = error; });
      const deadline = Date.now() + 90_000;
      let ready = false;
      while (Date.now() < deadline) {
        if (launchError) throw launchError;
        if (child.exitCode !== null) throw new Error(`Isolated web host exited before startup.\n${logs}`);
        try {
          const response = await fetch(`${host.url}/Account/Login`, { signal: AbortSignal.timeout(2000) });
          if (response.status === 200 && (await response.text()).includes('Input.Email')) { ready = true; break; }
        } catch { /* Startup may still be applying the synthetic database migrations. */ }
        await new Promise(done => setTimeout(done, 250));
      }
      if (!ready) throw new Error(`Isolated web host did not show its sign-in page.\n${logs}`);
      await use(host);
    } finally {
      if (child) await stop(child);
      // Only the exact mkdtemp-created child of the OS temp directory is removed.
      if (dirname(root) !== resolve(tmpdir()) || !basename(root).startsWith('quickfire-browser-'))
        throw new Error('Refusing to remove an unexpected browser-test directory.');
      await rm(root, { recursive: true, force: true, maxRetries: 8, retryDelay: 500 });
    }
  }, { timeout: 180_000 }],
  baseURL: async ({ host }, use) => use(host.url),
  page: async ({ page, host }, use) => {
    // The vendor's unlicensed modal has no dismiss button. Fail with the actual
    // prerequisite instead of allowing an unrelated app click to time out.
    await page.addLocatorHandler(page.getByText('Claim your FREE account and get a key in less than a minute', { exact: true }), async () => {
      throw new Error('Syncfusion is displaying its license dialog. Configure QUICKFIRE_BROWSER_SYNCFUSION_LICENSE with a valid Quickfire license through the environment or CI secret store, then rerun. The tests do not dismiss or alter license checks.');
    });
    const pageErrors = new Set<string>();
    page.on('pageerror', error => {
      let message = error.stack ?? error.message;
      for (const secret of [host.password, browserSetting('SYNCFUSION_LICENSE')])
        if (secret) message = message.replaceAll(secret, '[redacted]');
      message = message.replace(/[a-f\d]{32}\.[A-Za-z\d_-]{40,}/g, '[redacted helper key]');
      pageErrors.add(message.slice(0, 2000));
    });
    await use(page);
    expect([...pageErrors], 'The application raised an unhandled browser exception.').toEqual([]);
  },
});

export { expect };

export async function waitForInteractive(page: Page): Promise<void> {
  await expect(page.locator('[data-quickfire-interactive="true"]')).toBeVisible();
}

export async function signIn(page: Page, host: SmokeHost): Promise<void> {
  await page.goto('/Account/Login');
  await page.locator('[id="Input.Email"]').fill(host.email);
  await page.locator('[id="Input.Password"]').fill(host.password);
  await page.getByRole('button', { name: 'Log in', exact: true }).click();
  await expect(page).not.toHaveURL(/\/Account\/Login/);
  await waitForInteractive(page);
  await expect(page.locator('#blazor-error-ui')).not.toBeVisible();
}
