import Fastify from 'fastify';
import websocketPlugin from '@fastify/websocket';
import { randomUUID } from 'node:crypto';
import { loadConfig } from './config.js';
import { listDirectories } from './fs-browser.js';
import { discoverProjects } from './project-discovery.js';
import { PtyManager } from './pty-manager.js';

export async function buildServer(overrides = {}) {
  const config = { ...loadConfig(), ...overrides };
  const app = Fastify({ logger: false });
  const manager = new PtyManager({
    profileStoreFile: config.profileStoreFile,
    settingsStoreFile: config.settingsStoreFile,
    fsAllowedRoots: config.fsAllowedRoots,
    maxOutputBufferBytes: config.maxOutputBufferBytes
  });

  await app.register(websocketPlugin);

  app.addHook('onClose', async () => {
    manager.dispose();
  });

  app.addHook('onRequest', async (request, reply) => {
    if (!request.url.startsWith('/internal/')) {
      return;
    }

    const token = request.headers['x-internal-token'];
    if (token !== config.internalToken) {
      return reply.code(401).send({ error: 'unauthorized' });
    }
  });

  app.get('/healthz', async () => ({ status: 'ok' }));

  app.get('/projects/discover', async () => {
    return discoverProjects({
      codexConfigPath: config.codexConfigPath,
      claudeConfigPath: config.claudeConfigPath
    });
  });

  app.get('/fs/dirs', async (request, reply) => {
    try {
      const path = String(request.query?.path || '').trim();
      return listDirectories(path, { allowedRoots: manager.getFsAllowedRoots() });
    } catch (err) {
      reply.code(400);
      return { error: String(err.message || err) };
    }
  });

  app.get('/profiles', async () => {
    return manager.listProfiles();
  });

  app.post('/profiles', async (request, reply) => {
    try {
      const body = request.body || {};
      const profile = manager.createProfile({
        profileId: body.profileId || randomUUID(),
        name: body.name,
        cliType: body.cliType,
        shell: body.shell,
        cwd: body.cwd,
        args: body.args,
        env: body.env,
        startupCommands: body.startupCommands,
        icon: body.icon,
        color: body.color
      });
      return profile;
    } catch (err) {
      reply.code(400);
      return { error: String(err.message || err) };
    }
  });

  app.put('/profiles/:profileId', async (request, reply) => {
    try {
      return manager.updateProfile(request.params.profileId, request.body || {});
    } catch (err) {
      const message = String(err.message || err);
      if (message.includes('not found')) {
        reply.code(404);
      } else {
        reply.code(400);
      }
      return { error: message };
    }
  });

  app.delete('/profiles/:profileId', async (request, reply) => {
    try {
      return manager.deleteProfile(request.params.profileId);
    } catch (err) {
      const message = String(err.message || err);
      if (message.includes('not found')) {
        reply.code(404);
      } else {
        reply.code(400);
      }
      return { error: message };
    }
  });

  app.get('/settings/global-quick-commands', async () => {
    return {
      quickCommands: manager.getGlobalQuickCommands()
    };
  });

  app.put('/settings/global-quick-commands', async (request, reply) => {
    try {
      return {
        quickCommands: manager.setGlobalQuickCommands(request.body?.quickCommands || [])
      };
    } catch (err) {
      reply.code(400);
      return { error: String(err.message || err) };
    }
  });

  app.get('/settings/fs-allowed-roots', async () => {
    return {
      fsAllowedRoots: manager.getFsAllowedRoots()
    };
  });

  app.put('/settings/fs-allowed-roots', async (request, reply) => {
    try {
      return {
        fsAllowedRoots: manager.setFsAllowedRoots(request.body?.fsAllowedRoots || [])
      };
    } catch (err) {
      reply.code(400);
      return { error: String(err.message || err) };
    }
  });

  app.get('/sessions', async (request) => {
    const includeExited = String(request.query?.includeExited ?? '1') !== '0';
    const profileId = String(request.query?.profileId ?? '').trim();
    const taskId = String(request.query?.taskId ?? '').trim();
    return manager.list({ includeExited, profileId, taskId });
  });

  app.get('/sessions/:sessionId/snapshot', async (request, reply) => {
    try {
      const limitBytes = request.query?.limitBytes;
      return manager.snapshot(request.params.sessionId, { limitBytes });
    } catch (err) {
      reply.code(404);
      return { error: String(err.message || err) };
    }
  });

  app.get('/sessions/:sessionId/history', async (request, reply) => {
    try {
      const beforeSeq = request.query?.beforeSeq;
      const limitBytes = request.query?.limitBytes;
      return manager.history(request.params.sessionId, { beforeSeq, limitBytes });
    } catch (err) {
      reply.code(404);
      return { error: String(err.message || err) };
    }
  });

  // Public lightweight endpoint for web terminal creation (no auth in current phase).
  app.post('/sessions', async (request, reply) => {
    try {
      const body = request.body || {};
      const created = manager.create({
        sessionId: body.sessionId || randomUUID(),
        taskId: body.taskId || randomUUID(),
        cliType: body.cliType,
        mode: body.mode,
        profileId: body.profileId,
        title: body.title,
        shell: body.shell,
        cwd: body.cwd,
        command: body.command,
        workspaceRoot: body.workspaceRoot,
        args: body.args,
        env: body.env,
        startupCommands: body.startupCommands,
        cols: body.cols,
        rows: body.rows
      });
      return {
        ...manager.status(created.session.sessionId),
        writeToken: created.writeToken
      };
    } catch (err) {
      reply.code(400);
      return { error: String(err.message || err) };
    }
  });

  app.post('/sessions/:sessionId/terminate', async (request, reply) => {
    try {
      manager.terminate(request.params.sessionId, request.body?.signal || 'SIGTERM');
      return { ok: true };
    } catch (err) {
      reply.code(404);
      return { error: String(err.message || err) };
    }
  });

  app.delete('/sessions/:sessionId', async (request, reply) => {
    try {
      return manager.remove(request.params.sessionId);
    } catch (err) {
      const message = String(err.message || err);
      if (message.includes('cannot remove running session')) {
        reply.code(409);
      } else if (message.includes('not found')) {
        reply.code(404);
      } else {
        reply.code(400);
      }
      return { error: message };
    }
  });

  app.post('/sessions/prune-exited', async () => {
    return manager.pruneExited();
  });

  app.post('/internal/sessions', async (request, reply) => {
    try {
      const body = request.body || {};
      const created = manager.create(body);
      return {
        ...manager.status(created.session.sessionId),
        writeToken: created.writeToken
      };
    } catch (err) {
      reply.code(400);
      return { error: String(err.message || err) };
    }
  });

  app.get('/internal/sessions/:sessionId', async (request, reply) => {
    try {
      return manager.status(request.params.sessionId);
    } catch (err) {
      reply.code(404);
      return { error: String(err.message || err) };
    }
  });

  app.post('/internal/sessions/:sessionId/input', async (request, reply) => {
    try {
      manager.write(request.params.sessionId, request.body?.data || '');
      return { ok: true };
    } catch (err) {
      reply.code(404);
      return { error: String(err.message || err) };
    }
  });

  app.post('/internal/sessions/:sessionId/resize', async (request, reply) => {
    try {
      manager.resize(request.params.sessionId, request.body?.cols, request.body?.rows);
      return { ok: true };
    } catch (err) {
      reply.code(404);
      return { error: String(err.message || err) };
    }
  });

  app.post('/internal/sessions/:sessionId/terminate', async (request, reply) => {
    try {
      manager.terminate(request.params.sessionId, request.body?.signal || 'SIGTERM');
      return { ok: true };
    } catch (err) {
      reply.code(404);
      return { error: String(err.message || err) };
    }
  });

  app.get('/ws/terminal', { websocket: true }, (conn, request) => {
    const socket = conn?.socket ?? conn;
    const q = request.query || {};
    const sessionId = q.sessionId;
    const replay = String(q.replay ?? '0') === '1';
    const replayMode = String(q.replayMode ?? '').trim();
    const sinceSeq = q.sinceSeq;
    const writeToken = String(q.writeToken ?? '');

    if (!sessionId) {
      if (socket?.readyState === 1) {
        socket.send(JSON.stringify({ type: 'error', code: 'SESSION_REQUIRED', message: 'sessionId is required' }));
      }
      socket?.close();
      return;
    }

    const peer = {
      send(obj) {
        if (socket?.readyState === 1) {
          socket.send(JSON.stringify(obj));
        }
      },
      close() {
        if (socket?.readyState === 1) {
          socket.close();
        }
      }
    };

    try {
      manager.attach(sessionId, peer, { replay, replayMode, sinceSeq, writeToken });
    } catch (err) {
      peer.send({ type: 'error', code: 'SESSION_NOT_FOUND', message: String(err.message || err) });
      peer.close();
      return;
    }

    socket.on('message', (raw) => {
      try {
        const msg = JSON.parse(raw.toString());
        if (msg.type === 'input') {
          if (!manager.isPeerWritable(sessionId, peer)) {
            peer.send({ type: 'error', code: 'READ_ONLY', message: 'session is read-only for this connection' });
            return;
          }
          manager.write(sessionId, msg.data || '');
        } else if (msg.type === 'resize') {
          manager.resize(sessionId, msg.cols, msg.rows);
        } else if (msg.type === 'ping') {
          peer.send({ type: 'pong', ts: msg.ts ?? Date.now() });
        }
      } catch (err) {
        peer.send({ type: 'error', code: 'BAD_MESSAGE', message: String(err.message || err) });
      }
    });

    socket.on('close', () => {
      manager.detach(sessionId, peer);
    });
  });

  return { app, config };
}

if (import.meta.url === `file://${process.argv[1]}`) {
  const { app, config } = await buildServer();
  await app.listen({ port: config.port, host: config.host });
}
