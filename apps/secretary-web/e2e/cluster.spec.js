import { expect, test } from '@playwright/test';

const masterApi = 'http://127.0.0.1:8080';

async function listNodes(request) {
  const res = await request.get(`${masterApi}/api/nodes`);
  expect(res.ok()).toBeTruthy();
  const payload = await res.json();
  return Array.isArray(payload?.items) ? payload.items : [];
}

async function waitForNodesOnline(request) {
  await expect.poll(async () => {
    const items = await listNodes(request);
    const summary = {};
    for (const item of items) {
      summary[item.node_id] = item.node_online === true;
    }
    return summary;
  }, { timeout: 45000 }).toEqual({
    'master-1': true,
    'slave-a': true,
    'slave-b': true
  });
}

async function terminateRemoteInstance(request, nodeId, instanceId) {
  const res = await request.delete(`${masterApi}/api/nodes/${encodeURIComponent(nodeId)}/instances/${encodeURIComponent(instanceId)}`);
  expect(res.ok()).toBeTruthy();
}

test('cluster: desktop should route create/connect/input to master + 2 real slaves', async ({ page, request }) => {
  await waitForNodesOnline(request);
  await page.goto('/');

  await expect(page.getByRole('heading', { name: 'WebCLI Desktop' })).toBeVisible();
  await expect(page.getByTestId('node-select')).toBeVisible();
  await expect(page.getByTestId('node-select').locator('option', { hasText: 'Slave A' })).toHaveCount(1);
  await expect(page.getByTestId('node-select').locator('option', { hasText: 'Slave B' })).toHaveCount(1);

  const cleanup = [];
  try {
    await page.getByTestId('command-input').fill('bash');
    await page.getByTestId('args-input').fill('["-i"]');
    await page.getByTestId('cwd-input').fill('/home/yueyuan');

    const createAResponsePromise = page.waitForResponse((res) => {
      return res.request().method() === 'POST' && res.url().includes('/api/nodes/slave-a/instances');
    });
    await page.getByTestId('node-select').selectOption('slave-a');
    await page.getByTestId('create-button').click();
    const createAResponse = await createAResponsePromise;
    expect(createAResponse.ok()).toBeTruthy();
    const createA = await createAResponse.json();
    const idA = String(createA?.instance_id || '');
    expect(idA).not.toBe('');
    cleanup.push({ nodeId: 'slave-a', instanceId: idA });

    const markerA = `CLUSTER_A_${Date.now()}`;
    await page.getByTestId('terminal').click();
    await page.keyboard.type(`echo ${markerA}`);
    await page.keyboard.press('Enter');
    await expect
      .poll(async () => (await page.getByTestId('plain-output').textContent()) || '')
      .toContain(markerA);

    await page.getByRole('button', { name: 'Disconnect', exact: true }).click();

    const createBResponsePromise = page.waitForResponse((res) => {
      return res.request().method() === 'POST' && res.url().includes('/api/nodes/slave-b/instances');
    });
    await page.getByTestId('node-select').selectOption('slave-b');
    await page.getByTestId('create-button').click();
    const createBResponse = await createBResponsePromise;
    expect(createBResponse.ok()).toBeTruthy();
    const createB = await createBResponse.json();
    const idB = String(createB?.instance_id || '');
    expect(idB).not.toBe('');
    cleanup.push({ nodeId: 'slave-b', instanceId: idB });

    const markerB = `CLUSTER_B_${Date.now()}`;
    await page.getByTestId('terminal').click();
    await page.keyboard.type(`echo ${markerB}`);
    await page.keyboard.press('Enter');
    await expect
      .poll(async () => (await page.getByTestId('plain-output').textContent()) || '')
      .toContain(markerB);
  } finally {
    for (const item of cleanup) {
      await terminateRemoteInstance(request, item.nodeId, item.instanceId);
    }
  }
});
