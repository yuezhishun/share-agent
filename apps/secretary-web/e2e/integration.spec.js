import { expect, test } from '@playwright/test';

const apiBase = 'http://127.0.0.1:8080';

async function listInstances(request) {
  const res = await request.get(`${apiBase}/api/instances`);
  expect(res.ok()).toBeTruthy();
  const payload = await res.json();
  return Array.isArray(payload?.items) ? payload.items : [];
}

async function cleanupInstances(request) {
  const items = await listInstances(request);
  for (const item of items) {
    await request.delete(`${apiBase}/api/instances/${encodeURIComponent(item.id)}`);
  }
}

function p95(values) {
  const sorted = [...values].sort((a, b) => a - b);
  if (sorted.length === 0) {
    return 0;
  }
  const rank = Math.max(1, Math.ceil(sorted.length * 0.95));
  return sorted[Math.min(rank - 1, sorted.length - 1)];
}

async function startSwitchProbe(page, marker) {
  await page.evaluate((nextMarker) => {
    globalThis.__E2E_SWITCH_PROBE_CLEANUP__?.();
    const statusNode = document.querySelector('[data-testid="status"]');
    const outputNode = document.querySelector('[data-testid="plain-output"]');
    const probe = {
      startedAt: performance.now(),
      firstScreenAt: null,
      doneAt: null
    };
    const commit = () => {
      const statusText = String(statusNode?.textContent || '');
      const outputText = String(outputNode?.textContent || '');
      if (probe.firstScreenAt === null && outputText.includes(nextMarker)) {
        probe.firstScreenAt = performance.now();
      }
      if (probe.doneAt === null && probe.firstScreenAt !== null && statusText.includes('Connected')) {
        probe.doneAt = performance.now();
      }
      if (probe.doneAt !== null) {
        cleanup();
      }
    };
    const statusObserver = new MutationObserver(commit);
    const outputObserver = new MutationObserver(commit);
    statusObserver.observe(statusNode, { childList: true, subtree: true, characterData: true });
    outputObserver.observe(outputNode, { childList: true, subtree: true, characterData: true });
    const cleanup = () => {
      statusObserver.disconnect();
      outputObserver.disconnect();
      globalThis.__E2E_SWITCH_PROBE_CLEANUP__ = undefined;
    };
    globalThis.__E2E_SWITCH_PROBE__ = probe;
    globalThis.__E2E_SWITCH_PROBE_CLEANUP__ = cleanup;
    commit();
  }, marker);
}

async function collectSwitchMetrics(page, cdp) {
  const start = await cdp.send('Runtime.evaluate', {
    expression: 'performance.now()',
    returnByValue: true
  });

  await page.waitForFunction(() => {
    return globalThis.__E2E_SWITCH_PROBE__ && globalThis.__E2E_SWITCH_PROBE__.doneAt !== null;
  }, { timeout: 5000 });

  const end = await cdp.send('Runtime.evaluate', {
    expression: 'performance.now()',
    returnByValue: true
  });

  const probe = await page.evaluate(() => globalThis.__E2E_SWITCH_PROBE__);
  const firstScreenMs = Math.max(0, Number(probe?.firstScreenAt || 0) - Number(probe?.startedAt || 0));
  const switchMs = Math.max(0, Number(end?.result?.value || 0) - Number(start?.result?.value || 0));
  return { switchMs, firstScreenMs };
}

test.beforeEach(async ({ request }) => {
  await cleanupInstances(request);
});

test.afterEach(async ({ request }) => {
  await cleanupInstances(request);
});

test('integration: create terminal, check status, and execute common commands', async ({ page, request }) => {
  await page.goto('/');

  await expect(page.getByRole('heading', { name: 'WebCLI Desktop' })).toBeVisible();

  await page.getByTestId('command-input').fill('bash', { force: true });
  await page.getByTestId('args-input').fill('["-i"]', { force: true });
  await page.getByTestId('cwd-input').fill('/home/yueyuan', { force: true });
  await page.getByTestId('create-button').click();

  await expect(page.getByTestId('status')).toContainText('Connected');

  await expect
    .poll(async () => (await listInstances(request)).length)
    .toBe(1);

  await expect
    .poll(async () => {
      const rows = await listInstances(request);
      return rows[0]?.status || '';
    })
    .toBe('running');

  const terminal = page.getByTestId('terminal');
  await terminal.click();

  await page.keyboard.type('pwd');
  await page.keyboard.press('Enter');
  await page.keyboard.type('echo integration-ok');
  await page.keyboard.press('Enter');
  await page.keyboard.type('ls');
  await page.keyboard.press('Enter');

  await expect
    .poll(async () => (await page.getByTestId('plain-output').textContent()) || '')
    .toContain('integration-ok');

  await expect
    .poll(async () => (await page.getByTestId('plain-output').textContent()) || '')
    .toContain('/home/yueyuan');
});

test('integration: terminate terminal should remove instance and update status', async ({ page, request }) => {
  await page.goto('/');

  await page.getByTestId('command-input').fill('bash', { force: true });
  await page.getByTestId('args-input').fill('["-i"]', { force: true });
  await page.getByTestId('cwd-input').fill('/home/yueyuan', { force: true });
  await page.getByTestId('create-button').click();

  await expect(page.getByTestId('status')).toContainText('Connected');
  await expect.poll(async () => (await listInstances(request)).length).toBe(1);

  await page.getByRole('button', { name: 'Terminate', exact: true }).click();

  await expect(page.getByTestId('status')).toContainText('Terminated');
  await expect.poll(async () => (await listInstances(request)).length).toBe(0);
  await expect(page.locator('#instance-list li')).toHaveCount(0);
});

test('integration: desktop switch performance should satisfy local acceptance thresholds', async ({ page }) => {
  await page.goto('/');
  const cdp = await page.context().newCDPSession(page);
  await cdp.send('Runtime.enable');

  for (let i = 0; i < 3; i += 1) {
    await page.getByTestId('command-input').fill('bash', { force: true });
    await page.getByTestId('args-input').fill('["-i"]', { force: true });
    await page.getByTestId('cwd-input').fill('/home/yueyuan', { force: true });
    await page.getByTestId('create-button').click();
    await expect(page.getByTestId('status')).toContainText('Connected');
  }

  const instanceItems = page.locator('#instance-list .terminal-item');
  await expect(instanceItems).toHaveCount(3);

  const markers = [];
  for (let i = 0; i < 3; i += 1) {
    const marker = `PERF_MARK_${i}_${Date.now()}`;
    markers.push(marker);
    await instanceItems.nth(i).click();
    await expect(page.getByTestId('status')).toContainText('Connected');
    await page.getByTestId('terminal').click();
    await page.keyboard.type(`echo ${marker}`);
    await page.keyboard.press('Enter');
    await expect
      .poll(async () => (await page.getByTestId('plain-output').textContent()) || '')
      .toContain(marker);
  }

  const switchSamples = [];
  const firstScreenSamples = [];

  for (let i = 0; i < 30; i += 1) {
    const targetIndex = i % 3;
    await startSwitchProbe(page, markers[targetIndex]);
    await instanceItems.nth(targetIndex).click();
    const sample = await collectSwitchMetrics(page, cdp);
    switchSamples.push(sample.switchMs);
    firstScreenSamples.push(sample.firstScreenMs);
  }

  expect(p95(switchSamples)).toBeLessThanOrEqual(5000);
  expect(p95(firstScreenSamples)).toBeLessThanOrEqual(1500);
});
