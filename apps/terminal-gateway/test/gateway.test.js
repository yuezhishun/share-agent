import test from 'node:test';
import assert from 'node:assert/strict';
import { randomUUID } from 'node:crypto';
import { mkdtempSync, mkdirSync, rmSync, writeFileSync } from 'node:fs';
import { tmpdir } from 'node:os';
import { join } from 'node:path';
import { WebSocket } from 'ws';
import { buildServer } from '../src/server.js';

const TOKEN = 'it-token';
const WS_TOKEN = 'it-ws-token';

test('gateway spawn and ws output works', async (t) => {
  const { app } = await buildServer({ port: 0, host: '127.0.0.1', internalToken: TOKEN, wsToken: WS_TOKEN });
  await app.listen({ port: 0, host: '127.0.0.1' });
  t.after(async () => { await app.close(); });

  const address = app.server.address();
  const base = `http://127.0.0.1:${address.port}`;

  const sessionId = randomUUID();
  const createRes = await fetch(`${base}/internal/sessions`, {
    method: 'POST',
    headers: { 'content-type': 'application/json', 'x-internal-token': TOKEN },
    body: JSON.stringify({
      sessionId,
      taskId: randomUUID(),
      cliType: 'codex',
      mode: 'execute',
      shell: '/bin/bash',
      cwd: '/tmp',
      command: 'echo hello-gateway',
      env: {},
      cols: 120,
      rows: 30
    })
  });

  assert.equal(createRes.status, 200);

  const messages = [];
  await new Promise((resolve, reject) => {
    const ws = new WebSocket(`ws://127.0.0.1:${address.port}/ws/terminal?sessionId=${sessionId}`);

    const timeout = setTimeout(() => {
      ws.close();
      reject(new Error('timeout waiting ws output'));
    }, 8000);

    ws.on('message', (data) => {
      const msg = JSON.parse(data.toString());
      messages.push(msg);

      if (msg.type === 'output' && msg.data.includes('hello-gateway')) {
        clearTimeout(timeout);
        ws.close();
        resolve();
      }
    });

    ws.on('error', (err) => {
      clearTimeout(timeout);
      reject(err);
    });
  });

  assert.ok(messages.some((x) => x.type === 'ready'));
  assert.ok(messages.some((x) => x.type === 'output' && x.data.includes('hello-gateway')));
});

test('gateway supports reconnect to running session and ping/pong', async (t) => {
  const { app } = await buildServer({ port: 0, host: '127.0.0.1', internalToken: TOKEN, wsToken: WS_TOKEN });
  await app.listen({ port: 0, host: '127.0.0.1' });
  t.after(async () => { await app.close(); });

  const address = app.server.address();
  const base = `http://127.0.0.1:${address.port}`;
  const sessionId = randomUUID();

  const createRes = await fetch(`${base}/internal/sessions`, {
    method: 'POST',
    headers: { 'content-type': 'application/json', 'x-internal-token': TOKEN },
    body: JSON.stringify({
      sessionId,
      taskId: randomUUID(),
      cliType: 'codex',
      mode: 'execute',
      shell: '/bin/bash',
      cwd: '/tmp',
      command: 'echo first; sleep 2; echo second',
      env: {},
      cols: 120,
      rows: 30
    })
  });

  assert.equal(createRes.status, 200);

  const firstWs = new WebSocket(`ws://127.0.0.1:${address.port}/ws/terminal?sessionId=${sessionId}`);
  await new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error('first ws ready timeout')), 4000);
    firstWs.on('message', (data) => {
      const msg = JSON.parse(data.toString());
      if (msg.type === 'ready') {
        clearTimeout(timer);
        resolve();
      }
    });
    firstWs.on('error', reject);
  });
  firstWs.close();

  const received = [];
  await new Promise((resolve, reject) => {
    const ws = new WebSocket(`ws://127.0.0.1:${address.port}/ws/terminal?sessionId=${sessionId}`);
    const timer = setTimeout(() => {
      ws.close();
      reject(new Error('reconnect timeout'));
    }, 8000);

    ws.on('message', (data) => {
      const msg = JSON.parse(data.toString());
      received.push(msg);
      if (msg.type === 'ready') {
        ws.send(JSON.stringify({ type: 'ping', ts: 123 }));
      }
      if (msg.type === 'pong') {
        clearTimeout(timer);
        ws.close();
        resolve();
      }
    });

    ws.on('error', (err) => {
      clearTimeout(timer);
      reject(err);
    });
  });

  assert.ok(received.some((x) => x.type === 'ready'));
  assert.ok(received.some((x) => x.type === 'pong'));
});

test('gateway returns exit for exited session reconnect', async (t) => {
  const { app } = await buildServer({ port: 0, host: '127.0.0.1', internalToken: TOKEN, wsToken: WS_TOKEN });
  await app.listen({ port: 0, host: '127.0.0.1' });
  t.after(async () => { await app.close(); });

  const address = app.server.address();
  const base = `http://127.0.0.1:${address.port}`;
  const sessionId = randomUUID();

  const createRes = await fetch(`${base}/internal/sessions`, {
    method: 'POST',
    headers: { 'content-type': 'application/json', 'x-internal-token': TOKEN },
    body: JSON.stringify({
      sessionId,
      taskId: randomUUID(),
      cliType: 'codex',
      mode: 'execute',
      shell: '/bin/bash',
      cwd: '/tmp',
      command: 'echo done-exit',
      env: {},
      cols: 120,
      rows: 30
    })
  });

  assert.equal(createRes.status, 200);

  await new Promise((resolve) => setTimeout(resolve, 600));

  const statusRes = await fetch(`${base}/internal/sessions/${sessionId}`, {
    headers: { 'x-internal-token': TOKEN }
  });
  assert.equal(statusRes.status, 200);
  const statusBody = await statusRes.json();
  assert.equal(statusBody.status, 'exited');

  const messages = [];
  await new Promise((resolve, reject) => {
    const ws = new WebSocket(`ws://127.0.0.1:${address.port}/ws/terminal?sessionId=${sessionId}`);
    const timer = setTimeout(() => {
      ws.close();
      reject(new Error('timeout waiting exit frame'));
    }, 4000);

    ws.on('message', (data) => {
      messages.push(JSON.parse(data.toString()));
    });
    ws.on('close', () => {
      clearTimeout(timer);
      resolve();
    });
    ws.on('error', (err) => {
      clearTimeout(timer);
      reject(err);
    });
  });

  assert.ok(messages.some((x) => x.type === 'exit'));
});

test('public session creation endpoint works', async (t) => {
  const { app } = await buildServer({ port: 0, host: '127.0.0.1', internalToken: TOKEN, wsToken: WS_TOKEN });
  await app.listen({ port: 0, host: '127.0.0.1' });
  t.after(async () => { await app.close(); });

  const address = app.server.address();
  const base = `http://127.0.0.1:${address.port}`;

  const res = await fetch(`${base}/sessions`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({
      command: 'echo public-create-ok',
      cwd: '/tmp'
    })
  });

  assert.equal(res.status, 200);
  const body = await res.json();
  assert.ok(body.sessionId);
  assert.ok(body.writeToken);
  assert.equal(body.status, 'running');
});

test('session list/status should not expose write token', async (t) => {
  const { app } = await buildServer({ port: 0, host: '127.0.0.1', internalToken: TOKEN, wsToken: WS_TOKEN });
  await app.listen({ port: 0, host: '127.0.0.1' });
  t.after(async () => { await app.close(); });

  const address = app.server.address();
  const base = `http://127.0.0.1:${address.port}`;

  const createRes = await fetch(`${base}/sessions`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ command: '', shell: '/bin/bash', cwd: '/tmp' })
  });
  assert.equal(createRes.status, 200);
  const created = await createRes.json();
  assert.ok(created.writeToken);

  const listRes = await fetch(`${base}/sessions?includeExited=1`);
  assert.equal(listRes.status, 200);
  const list = await listRes.json();
  const found = list.find((x) => x.sessionId === created.sessionId);
  assert.ok(found);
  assert.equal(found.writeToken, undefined);
});

test('session owner token controls writable websocket access', async (t) => {
  const { app } = await buildServer({ port: 0, host: '127.0.0.1', internalToken: TOKEN, wsToken: WS_TOKEN });
  await app.listen({ port: 0, host: '127.0.0.1' });
  t.after(async () => { await app.close(); });

  const address = app.server.address();
  const base = `http://127.0.0.1:${address.port}`;

  const createRes = await fetch(`${base}/sessions`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ command: '', shell: '/bin/bash', cwd: '/tmp' })
  });
  assert.equal(createRes.status, 200);
  const created = await createRes.json();
  assert.ok(created.writeToken);

  const readonlyMsgs = [];
  await new Promise((resolve, reject) => {
    const ws = new WebSocket(`ws://127.0.0.1:${address.port}/ws/terminal?sessionId=${created.sessionId}`);
    const timer = setTimeout(() => {
      ws.close();
      reject(new Error('timeout waiting readonly rejection'));
    }, 8000);

    ws.on('message', (data) => {
      const msg = JSON.parse(data.toString());
      readonlyMsgs.push(msg);
      if (msg.type === 'ready') {
        ws.send(JSON.stringify({ type: 'input', data: 'echo should-not-run\\r' }));
      }
      if (msg.type === 'error' && msg.code === 'READ_ONLY') {
        clearTimeout(timer);
        ws.close();
        resolve();
      }
    });
    ws.on('error', (err) => {
      clearTimeout(timer);
      reject(err);
    });
  });
  assert.ok(readonlyMsgs.some((x) => x.type === 'ready' && x.writable === false));
  assert.ok(readonlyMsgs.some((x) => x.type === 'error' && x.code === 'READ_ONLY'));

  await new Promise((resolve, reject) => {
    const ws = new WebSocket(`ws://127.0.0.1:${address.port}/ws/terminal?sessionId=${created.sessionId}&writeToken=${encodeURIComponent(created.writeToken)}`);
    const timer = setTimeout(() => {
      ws.close();
      reject(new Error('timeout waiting writable output'));
    }, 8000);

    ws.on('message', (data) => {
      const msg = JSON.parse(data.toString());
      if (msg.type === 'ready') {
        assert.equal(msg.writable, true);
        ws.send(JSON.stringify({ type: 'input', data: 'echo writable-ok\\r' }));
      }
      if (msg.type === 'output' && String(msg.data).includes('writable-ok')) {
        clearTimeout(timer);
        ws.close();
        resolve();
      }
    });
    ws.on('error', (err) => {
      clearTimeout(timer);
      reject(err);
    });
  });
});

test('delta replay from sinceSeq only returns newer output', async (t) => {
  const { app } = await buildServer({
    port: 0,
    host: '127.0.0.1',
    internalToken: TOKEN,
    wsToken: WS_TOKEN
  });
  await app.listen({ port: 0, host: '127.0.0.1' });
  t.after(async () => { await app.close(); });

  const address = app.server.address();
  const base = `http://127.0.0.1:${address.port}`;
  const sessionId = randomUUID();
  const createRes = await fetch(`${base}/internal/sessions`, {
    method: 'POST',
    headers: { 'content-type': 'application/json', 'x-internal-token': TOKEN },
    body: JSON.stringify({
      sessionId,
      taskId: randomUUID(),
      cliType: 'custom',
      mode: 'execute',
      shell: '/bin/bash',
      cwd: '/tmp',
      command: '',
      env: {},
      cols: 120,
      rows: 30
    })
  });
  assert.equal(createRes.status, 200);

  let cutoffSeq = 0;
  await new Promise((resolve, reject) => {
    const ws = new WebSocket(`ws://127.0.0.1:${address.port}/ws/terminal?sessionId=${sessionId}&replay=1`);
    const timeout = setTimeout(() => {
      ws.close();
      reject(new Error('timeout waiting first output'));
    }, 8000);

    ws.on('message', (data) => {
      const msg = JSON.parse(data.toString());
      if (msg.type !== 'output') {
        return;
      }
      cutoffSeq = Math.max(cutoffSeq, Number(msg.seqEnd || 0));
      if (msg.data.includes('/tmp')) {
        clearTimeout(timeout);
        ws.close();
        resolve();
      }
    });
    ws.on('error', (err) => {
      clearTimeout(timeout);
      reject(err);
    });
  });

  const inputRes = await fetch(`${base}/internal/sessions/${sessionId}/input`, {
    method: 'POST',
    headers: { 'content-type': 'application/json', 'x-internal-token': TOKEN },
    body: JSON.stringify({ data: 'echo delta-two\r' })
  });
  assert.equal(inputRes.status, 200);

  const deltaMessages = [];
  await new Promise((resolve, reject) => {
    const ws = new WebSocket(`ws://127.0.0.1:${address.port}/ws/terminal?sessionId=${sessionId}&replayMode=none&sinceSeq=${cutoffSeq}`);
    const timeout = setTimeout(() => {
      ws.close();
      reject(new Error('timeout waiting delta output'));
    }, 8000);
    ws.on('message', (data) => {
      const msg = JSON.parse(data.toString());
      deltaMessages.push(msg);
      if (msg.type === 'output' && msg.data.includes('delta-two')) {
        clearTimeout(timeout);
        ws.close();
        resolve();
      }
    });
    ws.on('error', (err) => {
      clearTimeout(timeout);
      reject(err);
    });
  });

  assert.ok(deltaMessages.some((x) => x.type === 'output' && x.data.includes('delta-two')));
  assert.ok(deltaMessages.every((x) => x.type !== 'output' || !x.data.includes('/tmp')));
});

test('delta replay reports truncatedSince when sinceSeq is too old', async (t) => {
  const { app } = await buildServer({
    port: 0,
    host: '127.0.0.1',
    internalToken: TOKEN,
    wsToken: WS_TOKEN,
    maxOutputBufferBytes: 64
  });
  await app.listen({ port: 0, host: '127.0.0.1' });
  t.after(async () => { await app.close(); });

  const address = app.server.address();
  const base = `http://127.0.0.1:${address.port}`;
  const sessionId = randomUUID();
  const createRes = await fetch(`${base}/internal/sessions`, {
    method: 'POST',
    headers: { 'content-type': 'application/json', 'x-internal-token': TOKEN },
    body: JSON.stringify({
      sessionId,
      taskId: randomUUID(),
      cliType: 'custom',
      mode: 'execute',
      shell: '/bin/bash',
      cwd: '/tmp',
      command: "printf 'abcdefghijklmnopqrstuvwxyz0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789'; sleep 5",
      env: {},
      cols: 120,
      rows: 30
    })
  });
  assert.equal(createRes.status, 200);

  await new Promise((resolve) => setTimeout(resolve, 500));

  const messages = [];
  await new Promise((resolve, reject) => {
    const ws = new WebSocket(`ws://127.0.0.1:${address.port}/ws/terminal?sessionId=${sessionId}&replayMode=none&sinceSeq=-1`);
    const timeout = setTimeout(() => {
      ws.close();
      reject(new Error('timeout waiting truncatedSince'));
    }, 8000);
    ws.on('message', (data) => {
      const msg = JSON.parse(data.toString());
      messages.push(msg);
      if (msg.type === 'output' && msg.truncatedSince === true) {
        clearTimeout(timeout);
        ws.close();
        resolve();
      }
    });
    ws.on('error', (err) => {
      clearTimeout(timeout);
      reject(err);
    });
  });

  assert.ok(messages.some((x) => x.type === 'output' && x.truncatedSince === true));
});

test('history endpoint returns paged chunks before a seq', async (t) => {
  const { app } = await buildServer({
    port: 0,
    host: '127.0.0.1',
    internalToken: TOKEN,
    wsToken: WS_TOKEN
  });
  await app.listen({ port: 0, host: '127.0.0.1' });
  t.after(async () => { await app.close(); });

  const address = app.server.address();
  const base = `http://127.0.0.1:${address.port}`;
  const sessionId = randomUUID();
  const createRes = await fetch(`${base}/internal/sessions`, {
    method: 'POST',
    headers: { 'content-type': 'application/json', 'x-internal-token': TOKEN },
    body: JSON.stringify({
      sessionId,
      taskId: randomUUID(),
      cliType: 'custom',
      mode: 'execute',
      shell: '/bin/bash',
      cwd: '/tmp',
      command: "echo line-one; echo line-two; echo line-three",
      env: {},
      cols: 120,
      rows: 30
    })
  });
  assert.equal(createRes.status, 200);

  await new Promise((resolve) => setTimeout(resolve, 500));

  const snapshotRes = await fetch(`${base}/sessions/${sessionId}/snapshot?limitBytes=1024`);
  assert.equal(snapshotRes.status, 200);
  const snapshot = await snapshotRes.json();
  assert.ok(Number(snapshot.tailSeq) > 0);

  const historyRes = await fetch(`${base}/sessions/${sessionId}/history?beforeSeq=${snapshot.tailSeq}&limitBytes=64`);
  assert.equal(historyRes.status, 200);
  const history = await historyRes.json();
  assert.ok(Array.isArray(history.chunks));
  assert.ok(history.chunks.length >= 1);
  assert.ok(history.chunks.every((x) => Number(x.seqEnd) < Number(snapshot.tailSeq)));
});

test('only one writable peer is allowed for the same session at a time', async (t) => {
  const { app } = await buildServer({ port: 0, host: '127.0.0.1', internalToken: TOKEN, wsToken: WS_TOKEN });
  await app.listen({ port: 0, host: '127.0.0.1' });
  t.after(async () => { await app.close(); });

  const address = app.server.address();
  const base = `http://127.0.0.1:${address.port}`;

  const createRes = await fetch(`${base}/sessions`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ command: '', shell: '/bin/bash', cwd: '/tmp' })
  });
  assert.equal(createRes.status, 200);
  const created = await createRes.json();

  const writer = new WebSocket(`ws://127.0.0.1:${address.port}/ws/terminal?sessionId=${created.sessionId}&writeToken=${encodeURIComponent(created.writeToken)}`);
  await new Promise((resolve, reject) => {
    const timer = setTimeout(() => reject(new Error('writer ready timeout')), 5000);
    writer.on('message', (data) => {
      const msg = JSON.parse(data.toString());
      if (msg.type === 'ready') {
        clearTimeout(timer);
        assert.equal(msg.writable, true);
        resolve();
      }
    });
    writer.on('error', (err) => {
      clearTimeout(timer);
      reject(err);
    });
  });

  await new Promise((resolve, reject) => {
    const ws = new WebSocket(`ws://127.0.0.1:${address.port}/ws/terminal?sessionId=${created.sessionId}&writeToken=${encodeURIComponent(created.writeToken)}`);
    const timer = setTimeout(() => {
      ws.close();
      reject(new Error('second peer timeout'));
    }, 6000);

    ws.on('message', (data) => {
      const msg = JSON.parse(data.toString());
      if (msg.type === 'ready') {
        assert.equal(msg.writable, false);
        ws.send(JSON.stringify({ type: 'input', data: 'echo lock-check\\r' }));
      }
      if (msg.type === 'error' && msg.code === 'READ_ONLY') {
        clearTimeout(timer);
        ws.close();
        resolve();
      }
    });
    ws.on('error', (err) => {
      clearTimeout(timer);
      reject(err);
    });
  });

  writer.close();
});

test('public session with empty command stays interactive', async (t) => {
  const { app } = await buildServer({ port: 0, host: '127.0.0.1', internalToken: TOKEN, wsToken: WS_TOKEN });
  await app.listen({ port: 0, host: '127.0.0.1' });
  t.after(async () => { await app.close(); });

  const address = app.server.address();
  const base = `http://127.0.0.1:${address.port}`;

  const createRes = await fetch(`${base}/sessions`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({
      command: '',
      shell: '/bin/bash',
      cwd: '/tmp'
    })
  });
  assert.equal(createRes.status, 200);
  const created = await createRes.json();
  assert.ok(created.sessionId);

  await new Promise((resolve) => setTimeout(resolve, 300));

  const listRes = await fetch(`${base}/sessions?includeExited=1`);
  assert.equal(listRes.status, 200);
  const list = await listRes.json();
  const current = list.find((x) => x.sessionId === created.sessionId);
  assert.ok(current);
  assert.equal(current.status, 'running');
});

test('public session list endpoint works', async (t) => {
  const { app } = await buildServer({ port: 0, host: '127.0.0.1', internalToken: TOKEN, wsToken: WS_TOKEN });
  await app.listen({ port: 0, host: '127.0.0.1' });
  t.after(async () => { await app.close(); });

  const address = app.server.address();
  const base = `http://127.0.0.1:${address.port}`;

  const createRes = await fetch(`${base}/sessions`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ command: 'echo list-check', cwd: '/tmp' })
  });
  assert.equal(createRes.status, 200);

  const listRes = await fetch(`${base}/sessions?includeExited=0`);
  assert.equal(listRes.status, 200);
  const list = await listRes.json();
  assert.ok(Array.isArray(list));
  assert.ok(list.length >= 1);
  assert.ok(list.some((x) => x.status === 'running'));
});

test('public terminate endpoint stops session', async (t) => {
  const { app } = await buildServer({ port: 0, host: '127.0.0.1', internalToken: TOKEN, wsToken: WS_TOKEN });
  await app.listen({ port: 0, host: '127.0.0.1' });
  t.after(async () => { await app.close(); });

  const address = app.server.address();
  const base = `http://127.0.0.1:${address.port}`;

  const createRes = await fetch(`${base}/sessions`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ command: '', shell: '/bin/bash', cwd: '/tmp' })
  });
  assert.equal(createRes.status, 200);
  const created = await createRes.json();

  const terminateRes = await fetch(`${base}/sessions/${created.sessionId}/terminate`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ signal: 'SIGTERM' })
  });
  assert.equal(terminateRes.status, 200);

  const start = Date.now();
  let status = 'running';
  while (Date.now() - start < 5000) {
    const listRes = await fetch(`${base}/sessions?includeExited=1`);
    const list = await listRes.json();
    const target = list.find((x) => x.sessionId === created.sessionId);
    if (!target) {
      break;
    }
    status = target.status;
    if (status === 'exited') {
      break;
    }
    await new Promise((resolve) => setTimeout(resolve, 250));
  }
  assert.equal(status, 'exited');
});

test('public remove endpoint removes exited session only', async (t) => {
  const { app } = await buildServer({ port: 0, host: '127.0.0.1', internalToken: TOKEN, wsToken: WS_TOKEN });
  await app.listen({ port: 0, host: '127.0.0.1' });
  t.after(async () => { await app.close(); });

  const address = app.server.address();
  const base = `http://127.0.0.1:${address.port}`;

  const createRes = await fetch(`${base}/sessions`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({
      command: 'echo remove-me',
      shell: '/bin/bash',
      cwd: '/tmp'
    })
  });
  assert.equal(createRes.status, 200);
  const created = await createRes.json();

  await new Promise((resolve) => setTimeout(resolve, 600));

  const removeRes = await fetch(`${base}/sessions/${encodeURIComponent(created.sessionId)}`, {
    method: 'DELETE'
  });
  assert.equal(removeRes.status, 200);

  const listRes = await fetch(`${base}/sessions?includeExited=1`);
  assert.equal(listRes.status, 200);
  const list = await listRes.json();
  assert.ok(!list.some((x) => x.sessionId === created.sessionId));
});

test('public prune-exited endpoint removes all exited sessions', async (t) => {
  const { app } = await buildServer({ port: 0, host: '127.0.0.1', internalToken: TOKEN, wsToken: WS_TOKEN });
  await app.listen({ port: 0, host: '127.0.0.1' });
  t.after(async () => { await app.close(); });

  const address = app.server.address();
  const base = `http://127.0.0.1:${address.port}`;

  const a = await fetch(`${base}/sessions`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ command: 'echo prune-a', shell: '/bin/bash', cwd: '/tmp' })
  });
  const b = await fetch(`${base}/sessions`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ command: 'echo prune-b', shell: '/bin/bash', cwd: '/tmp' })
  });
  assert.equal(a.status, 200);
  assert.equal(b.status, 200);

  await new Promise((resolve) => setTimeout(resolve, 600));

  const pruneRes = await fetch(`${base}/sessions/prune-exited`, { method: 'POST' });
  assert.equal(pruneRes.status, 200);
  const pruneBody = await pruneRes.json();
  assert.ok(Number(pruneBody.removed) >= 2);
});

test('attach should replay previous output after reconnect', async (t) => {
  const { app } = await buildServer({ port: 0, host: '127.0.0.1', internalToken: TOKEN, wsToken: WS_TOKEN });
  await app.listen({ port: 0, host: '127.0.0.1' });
  t.after(async () => { await app.close(); });

  const address = app.server.address();
  const base = `http://127.0.0.1:${address.port}`;

  const createRes = await fetch(`${base}/sessions`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({
      command: 'echo replay-line; sleep 2',
      shell: '/bin/bash',
      cwd: '/tmp'
    })
  });
  assert.equal(createRes.status, 200);
  const created = await createRes.json();

  await new Promise((resolve) => setTimeout(resolve, 500));

  await new Promise((resolve, reject) => {
    const ws = new WebSocket(`ws://127.0.0.1:${address.port}/ws/terminal?sessionId=${created.sessionId}&replay=1`);
    const timeout = setTimeout(() => {
      ws.close();
      reject(new Error('timeout waiting replay output'));
    }, 6000);

    ws.on('message', (data) => {
      const msg = JSON.parse(data.toString());
      if (msg.type === 'output' && String(msg.data).includes('replay-line')) {
        clearTimeout(timeout);
        ws.close();
        resolve();
      }
    });

    ws.on('error', (err) => {
      clearTimeout(timeout);
      reject(err);
    });
  });
});

test('profiles CRUD works for custom profile', async (t) => {
  const { app } = await buildServer({ port: 0, host: '127.0.0.1', internalToken: TOKEN, wsToken: WS_TOKEN });
  await app.listen({ port: 0, host: '127.0.0.1' });
  t.after(async () => { await app.close(); });

  const address = app.server.address();
  const base = `http://127.0.0.1:${address.port}`;

  const listRes = await fetch(`${base}/profiles`);
  assert.equal(listRes.status, 200);
  const builtins = await listRes.json();
  assert.ok(Array.isArray(builtins));
  assert.ok(builtins.length >= 4);

  const createRes = await fetch(`${base}/profiles`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({
      name: 'custom-tools',
      cliType: 'custom',
      shell: '/bin/bash',
      cwd: '/tmp',
      startupCommands: ['pwd']
    })
  });
  assert.equal(createRes.status, 200);
  const created = await createRes.json();
  assert.ok(created.profileId);
  assert.equal(created.name, 'custom-tools');

  const updateRes = await fetch(`${base}/profiles/${encodeURIComponent(created.profileId)}`, {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ cwd: '/var/tmp', startupCommands: ['pwd', 'ls'] })
  });
  assert.equal(updateRes.status, 200);
  const updated = await updateRes.json();
  assert.equal(updated.cwd, '/var/tmp');
  assert.equal(updated.startupCommands.length, 2);

  const deleteRes = await fetch(`${base}/profiles/${encodeURIComponent(created.profileId)}`, { method: 'DELETE' });
  assert.equal(deleteRes.status, 200);
});

test('session create supports profileId and list filtering', async (t) => {
  const { app } = await buildServer({ port: 0, host: '127.0.0.1', internalToken: TOKEN, wsToken: WS_TOKEN });
  await app.listen({ port: 0, host: '127.0.0.1' });
  t.after(async () => { await app.close(); });

  const address = app.server.address();
  const base = `http://127.0.0.1:${address.port}`;

  const profileRes = await fetch(`${base}/profiles`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({
      name: 'profile-for-filter',
      shell: '/bin/bash',
      cwd: '/tmp',
      startupCommands: ['echo profile-started']
    })
  });
  assert.equal(profileRes.status, 200);
  const profile = await profileRes.json();

  const taskId = randomUUID();
  const createRes = await fetch(`${base}/sessions`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({
      profileId: profile.profileId,
      taskId,
      command: '',
      cols: 120,
      rows: 30
    })
  });
  assert.equal(createRes.status, 200);
  const created = await createRes.json();
  assert.equal(created.profileId, profile.profileId);

  const listByProfileRes = await fetch(`${base}/sessions?includeExited=1&profileId=${encodeURIComponent(profile.profileId)}`);
  assert.equal(listByProfileRes.status, 200);
  const listByProfile = await listByProfileRes.json();
  assert.ok(listByProfile.some((x) => x.sessionId === created.sessionId));

  const listByTaskRes = await fetch(`${base}/sessions?includeExited=1&taskId=${encodeURIComponent(taskId)}`);
  assert.equal(listByTaskRes.status, 200);
  const listByTask = await listByTaskRes.json();
  assert.ok(listByTask.some((x) => x.sessionId === created.sessionId));
});

test('session snapshot returns replayable output buffer', async (t) => {
  const { app } = await buildServer({ port: 0, host: '127.0.0.1', internalToken: TOKEN, wsToken: WS_TOKEN });
  await app.listen({ port: 0, host: '127.0.0.1' });
  t.after(async () => { await app.close(); });

  const address = app.server.address();
  const base = `http://127.0.0.1:${address.port}`;

  const createRes = await fetch(`${base}/sessions`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({
      shell: '/bin/bash',
      cwd: '/tmp',
      command: 'echo snapshot-line; sleep 1'
    })
  });
  assert.equal(createRes.status, 200);
  const created = await createRes.json();

  await new Promise((resolve) => setTimeout(resolve, 350));

  const snapshotRes = await fetch(`${base}/sessions/${created.sessionId}/snapshot?limitBytes=4096`);
  assert.equal(snapshotRes.status, 200);
  const snapshot = await snapshotRes.json();
  assert.equal(snapshot.sessionId, created.sessionId);
  assert.ok(String(snapshot.data).includes('snapshot-line'));
  assert.equal(snapshot.truncated, false);
});

test('session output truncation is reflected in list and snapshot', async (t) => {
  const { app } = await buildServer({
    port: 0,
    host: '127.0.0.1',
    internalToken: TOKEN,
    wsToken: WS_TOKEN,
    maxOutputBufferBytes: 1024
  });
  await app.listen({ port: 0, host: '127.0.0.1' });
  t.after(async () => { await app.close(); });

  const address = app.server.address();
  const base = `http://127.0.0.1:${address.port}`;

  const createRes = await fetch(`${base}/sessions`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({
      shell: '/bin/bash',
      cwd: '/tmp',
      command: "head -c 8192 /dev/zero | tr '\\0' 'x'"
    })
  });
  assert.equal(createRes.status, 200);
  const created = await createRes.json();

  await new Promise((resolve) => setTimeout(resolve, 450));

  const listRes = await fetch(`${base}/sessions?includeExited=1`);
  assert.equal(listRes.status, 200);
  const sessions = await listRes.json();
  const target = sessions.find((x) => x.sessionId === created.sessionId);
  assert.ok(target);
  assert.equal(target.outputTruncated, true);
  assert.equal(target.maxOutputBufferBytes, 1024);

  const snapshotRes = await fetch(`${base}/sessions/${created.sessionId}/snapshot?limitBytes=4096`);
  assert.equal(snapshotRes.status, 200);
  const snapshot = await snapshotRes.json();
  assert.equal(snapshot.truncated, true);
  assert.equal(snapshot.maxOutputBufferBytes, 1024);
});

test('profile templates resolve workspaceRoot/taskId/profileName', async (t) => {
  const { app } = await buildServer({ port: 0, host: '127.0.0.1', internalToken: TOKEN, wsToken: WS_TOKEN });
  await app.listen({ port: 0, host: '127.0.0.1' });
  t.after(async () => { await app.close(); });

  const address = app.server.address();
  const base = `http://127.0.0.1:${address.port}`;

  const createProfileRes = await fetch(`${base}/profiles`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({
      name: 'tpl-profile',
      shell: '/bin/bash',
      cwd: '/tmp',
      startupCommands: ['echo p=${profileName} t=${taskId} w=${workspaceRoot}'],
      env: { TEST_SCOPE: '${profileName}-${taskId}' }
    })
  });
  assert.equal(createProfileRes.status, 200);
  const profile = await createProfileRes.json();

  const taskId = randomUUID();
  const createSessionRes = await fetch(`${base}/sessions`, {
    method: 'POST',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({
      profileId: profile.profileId,
      taskId,
      workspaceRoot: '/workspace/demo',
      command: ''
    })
  });
  assert.equal(createSessionRes.status, 200);
  const session = await createSessionRes.json();

  await new Promise((resolve) => setTimeout(resolve, 450));

  const snapshotRes = await fetch(`${base}/sessions/${session.sessionId}/snapshot?limitBytes=8192`);
  assert.equal(snapshotRes.status, 200);
  const snapshot = await snapshotRes.json();
  assert.ok(String(snapshot.data).includes(`p=tpl-profile t=${taskId} w=/workspace/demo`));
});

test('global quick commands settings persist on backend', async (t) => {
  const tempDir = mkdtempSync(join(tmpdir(), 'pty-gw-settings-'));
  t.after(() => {
    rmSync(tempDir, { recursive: true, force: true });
  });
  const settingsStoreFile = join(tempDir, 'settings.json');

  const first = await buildServer({
    port: 0,
    host: '127.0.0.1',
    internalToken: TOKEN,
    wsToken: WS_TOKEN,
    settingsStoreFile
  });
  await first.app.listen({ port: 0, host: '127.0.0.1' });
  const address1 = first.app.server.address();
  const base1 = `http://127.0.0.1:${address1.port}`;

  const putRes = await fetch(`${base1}/settings/global-quick-commands`, {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({
      quickCommands: [
        { label: 'list', content: 'ls', sendMode: 'auto', enabled: true }
      ]
    })
  });
  assert.equal(putRes.status, 200);
  await first.app.close();

  const second = await buildServer({
    port: 0,
    host: '127.0.0.1',
    internalToken: TOKEN,
    wsToken: WS_TOKEN,
    settingsStoreFile
  });
  await second.app.listen({ port: 0, host: '127.0.0.1' });
  t.after(async () => {
    await second.app.close();
  });
  const address2 = second.app.server.address();
  const base2 = `http://127.0.0.1:${address2.port}`;

  const getRes = await fetch(`${base2}/settings/global-quick-commands`);
  assert.equal(getRes.status, 200);
  const body = await getRes.json();
  assert.equal(Array.isArray(body.quickCommands), true);
  assert.equal(body.quickCommands.length, 1);
  assert.equal(body.quickCommands[0].content, 'ls');
});

test('fs allowed roots settings persist on backend', async (t) => {
  const tempDir = mkdtempSync(join(tmpdir(), 'pty-gw-fsroots-'));
  t.after(() => {
    rmSync(tempDir, { recursive: true, force: true });
  });
  const settingsStoreFile = join(tempDir, 'settings.json');

  const first = await buildServer({
    port: 0,
    host: '127.0.0.1',
    internalToken: TOKEN,
    wsToken: WS_TOKEN,
    settingsStoreFile
  });
  await first.app.listen({ port: 0, host: '127.0.0.1' });
  const address1 = first.app.server.address();
  const base1 = `http://127.0.0.1:${address1.port}`;

  const putRes = await fetch(`${base1}/settings/fs-allowed-roots`, {
    method: 'PUT',
    headers: { 'content-type': 'application/json' },
    body: JSON.stringify({ fsAllowedRoots: ['/home', '/workspace'] })
  });
  assert.equal(putRes.status, 200);
  await first.app.close();

  const second = await buildServer({
    port: 0,
    host: '127.0.0.1',
    internalToken: TOKEN,
    wsToken: WS_TOKEN,
    settingsStoreFile
  });
  await second.app.listen({ port: 0, host: '127.0.0.1' });
  t.after(async () => {
    await second.app.close();
  });
  const address2 = second.app.server.address();
  const base2 = `http://127.0.0.1:${address2.port}`;

  const getRes = await fetch(`${base2}/settings/fs-allowed-roots`);
  assert.equal(getRes.status, 200);
  const body = await getRes.json();
  assert.equal(Array.isArray(body.fsAllowedRoots), true);
  assert.ok(body.fsAllowedRoots.includes('/home'));
  assert.ok(body.fsAllowedRoots.includes('/workspace'));
});

test('project discovery reads codex projects from config.toml', async (t) => {
  const tempDir = mkdtempSync(join(tmpdir(), 'pty-gw-projects-'));
  t.after(() => {
    rmSync(tempDir, { recursive: true, force: true });
  });
  const codexDir = join(tempDir, '.codex');
  mkdirSync(codexDir, { recursive: true });
  const codexConfigPath = join(codexDir, 'config.toml');
  writeFileSync(codexConfigPath, `
model = "gpt-5.3-codex"
[projects."/workspace/demo-a"]
trust_level = "trusted"
[projects."/workspace/demo-b"]
trust_level = "trusted"
`);

  const { app } = await buildServer({
    port: 0,
    host: '127.0.0.1',
    internalToken: TOKEN,
    wsToken: WS_TOKEN,
    codexConfigPath
  });
  await app.listen({ port: 0, host: '127.0.0.1' });
  t.after(async () => { await app.close(); });

  const address = app.server.address();
  const base = `http://127.0.0.1:${address.port}`;
  const res = await fetch(`${base}/projects/discover`);
  assert.equal(res.status, 200);
  const body = await res.json();
  assert.equal(Array.isArray(body.items), true);
  assert.ok(body.items.some((x) => x.path === '/workspace/demo-a' && x.source === 'codex'));
  assert.ok(body.items.some((x) => x.path === '/workspace/demo-b' && x.source === 'codex'));
});

test('project discovery returns empty when codex config is missing', async (t) => {
  const { app } = await buildServer({
    port: 0,
    host: '127.0.0.1',
    internalToken: TOKEN,
    wsToken: WS_TOKEN,
    codexConfigPath: '/tmp/pty-gw-missing-config.toml'
  });
  await app.listen({ port: 0, host: '127.0.0.1' });
  t.after(async () => { await app.close(); });

  const address = app.server.address();
  const base = `http://127.0.0.1:${address.port}`;
  const res = await fetch(`${base}/projects/discover`);
  assert.equal(res.status, 200);
  const body = await res.json();
  assert.equal(Array.isArray(body.items), true);
  assert.equal(body.items.length, 0);
});

test('fs dir browser validates input path and allowed roots', async (t) => {
  const tempDir = mkdtempSync(join(tmpdir(), 'pty-gw-dirs-'));
  const settingsStoreFile = join(tempDir, 'settings.json');
  t.after(() => {
    rmSync(tempDir, { recursive: true, force: true });
  });
  mkdirSync(join(tempDir, 'alpha'), { recursive: true });
  mkdirSync(join(tempDir, 'beta', 'child'), { recursive: true });
  writeFileSync(join(tempDir, 'note.txt'), 'not-a-dir');

  const { app } = await buildServer({
    port: 0,
    host: '127.0.0.1',
    internalToken: TOKEN,
    wsToken: WS_TOKEN,
    settingsStoreFile,
    fsAllowedRoots: [tempDir]
  });
  await app.listen({ port: 0, host: '127.0.0.1' });
  t.after(async () => { await app.close(); });

  const address = app.server.address();
  const base = `http://127.0.0.1:${address.port}`;

  const invalidRes = await fetch(`${base}/fs/dirs?path=relative/path`);
  assert.equal(invalidRes.status, 400);

  const deniedRes = await fetch(`${base}/fs/dirs?path=${encodeURIComponent('/etc')}`);
  assert.equal(deniedRes.status, 400);

  const okRes = await fetch(`${base}/fs/dirs?path=${encodeURIComponent(tempDir)}`);
  assert.equal(okRes.status, 200);
  const body = await okRes.json();
  assert.equal(body.path.length > 0, true);
  assert.equal(Array.isArray(body.items), true);
  assert.ok(body.items.some((x) => x.name === 'alpha'));
  assert.ok(body.items.some((x) => x.name === 'beta' && x.hasChildren === true));
  assert.ok(!body.items.some((x) => x.name === 'note.txt'));
});
