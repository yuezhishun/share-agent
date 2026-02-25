import pty from 'node-pty';
import { createHash, randomBytes, timingSafeEqual } from 'node:crypto';
import { existsSync, mkdirSync, readFileSync, writeFileSync } from 'node:fs';
import { dirname, isAbsolute, resolve } from 'node:path';

const DEFAULT_MAX_OUTPUT_BUFFER_BYTES = 8 * 1024 * 1024;

export class PtyManager {
  constructor(options = {}) {
    this.profileStoreFile = normalizeString(options.profileStoreFile);
    this.settingsStoreFile = normalizeString(options.settingsStoreFile);
    this.maxOutputBufferBytes = clampInt(
      options.maxOutputBufferBytes,
      1024,
      64 * 1024 * 1024,
      DEFAULT_MAX_OUTPUT_BUFFER_BYTES
    );
    this.sessions = new Map();
    this.profiles = new Map();
    this.globalQuickCommands = [];
    this.fsAllowedRoots = normalizeFsAllowedRoots(options.fsAllowedRoots || []);
    this._seedBuiltinProfiles();
    this._loadCustomProfiles();
    this._loadSettings();
  }

  has(sessionId) {
    return this.sessions.has(sessionId);
  }

  listProfiles() {
    const items = Array.from(this.profiles.values()).map(cloneProfile);
    items.sort((a, b) => {
      if (a.isBuiltin !== b.isBuiltin) {
        return a.isBuiltin ? -1 : 1;
      }
      return String(a.name).localeCompare(String(b.name));
    });
    return items;
  }

  createProfile(input = {}) {
    const normalized = normalizeProfileInput(input);
    if (!normalized.name) {
      throw new Error('profile name is required');
    }
    if (!normalized.shell) {
      throw new Error('profile shell is required');
    }
    if (!normalized.cwd) {
      throw new Error('profile cwd is required');
    }
    if (!normalized.profileId) {
      throw new Error('profileId is required');
    }
    if (this.profiles.has(normalized.profileId)) {
      throw new Error(`profile already exists: ${normalized.profileId}`);
    }

    const now = new Date().toISOString();
    const profile = {
      profileId: normalized.profileId,
      name: normalized.name,
      cliType: normalized.cliType || 'custom',
      shell: normalized.shell,
      cwd: normalized.cwd,
      args: normalized.args,
      env: normalized.env,
      startupCommands: normalized.startupCommands,
      quickCommands: normalized.quickCommands,
      cliOptions: normalized.cliOptions,
      icon: normalized.icon,
      color: normalized.color,
      isBuiltin: false,
      createdAt: now,
      updatedAt: now
    };

    this.profiles.set(profile.profileId, profile);
    this._persistProfiles();
    return cloneProfile(profile);
  }

  updateProfile(profileId, updates = {}) {
    const current = this.requireProfile(profileId);
    if (current.isBuiltin) {
      throw new Error('builtin profile is read-only');
    }

    const normalized = normalizeProfileInput({ ...current, ...updates, profileId });
    if (!normalized.name) {
      throw new Error('profile name is required');
    }
    if (!normalized.shell) {
      throw new Error('profile shell is required');
    }
    if (!normalized.cwd) {
      throw new Error('profile cwd is required');
    }

    const next = {
      ...current,
      name: normalized.name,
      cliType: normalized.cliType || 'custom',
      shell: normalized.shell,
      cwd: normalized.cwd,
      args: normalized.args,
      env: normalized.env,
      startupCommands: normalized.startupCommands,
      quickCommands: normalized.quickCommands,
      cliOptions: normalized.cliOptions,
      icon: normalized.icon,
      color: normalized.color,
      updatedAt: new Date().toISOString()
    };

    this.profiles.set(profileId, next);
    this._persistProfiles();
    return cloneProfile(next);
  }

  deleteProfile(profileId) {
    const current = this.requireProfile(profileId);
    if (current.isBuiltin) {
      throw new Error('builtin profile cannot be deleted');
    }

    this.profiles.delete(profileId);
    this._persistProfiles();
    return { ok: true };
  }

  getGlobalQuickCommands() {
    return normalizeQuickCommands(this.globalQuickCommands || []);
  }

  setGlobalQuickCommands(list = []) {
    this.globalQuickCommands = normalizeQuickCommands(list);
    this._persistSettings();
    return this.getGlobalQuickCommands();
  }

  getFsAllowedRoots() {
    return [...(this.fsAllowedRoots || [])];
  }

  setFsAllowedRoots(list = []) {
    const normalized = normalizeFsAllowedRoots(list);
    if (normalized.length === 0) {
      throw new Error('fs allowed roots must not be empty');
    }
    this.fsAllowedRoots = normalized;
    this._persistSettings();
    return this.getFsAllowedRoots();
  }

  create(options = {}) {
    if (!options.sessionId) {
      throw new Error('sessionId is required');
    }
    if (this.sessions.has(options.sessionId)) {
      throw new Error(`session already exists: ${options.sessionId}`);
    }

    const launch = this.resolveLaunchOptions(options);
    const proc = pty.spawn(launch.shell, launch.args, {
      name: 'xterm-256color',
      cols: launch.cols,
      rows: launch.rows,
      cwd: launch.cwd,
      env: launch.env
    });

    const now = new Date().toISOString();
    const writeToken = generateWriteToken();
    const writeTokenHash = hashWriteToken(writeToken);
    const record = {
      sessionId: options.sessionId,
      taskId: options.taskId,
      cliType: launch.cliType,
      mode: launch.mode,
      profileId: launch.profileId,
      title: launch.title,
      shell: launch.shell,
      cwd: launch.cwd,
      args: [...launch.args],
      proc,
      createdAt: now,
      lastActivityAt: now,
      status: 'running',
      exitCode: null,
      writeTokenHash,
      writerPeer: null,
      outputTruncated: false,
      maxOutputBufferBytes: this.maxOutputBufferBytes,
      outputBytes: 0,
      outputChunks: [],
      headSeq: 1,
      tailSeq: 0,
      nextSeq: 1,
      subscribers: new Set()
    };

    proc.onData((data) => {
      record.lastActivityAt = new Date().toISOString();
      const output = appendOutputChunk(record, data);
      if (output.truncated === true) {
        record.outputTruncated = true;
      }
      for (const sub of record.subscribers) {
        sub.send({
          type: 'output',
          sessionId: record.sessionId,
          stream: 'stdout',
          data,
          seqStart: output.seqStart,
          seqEnd: output.seqEnd,
          truncatedSince: false
        });
      }
    });

    proc.onExit(({ exitCode, signal }) => {
      record.status = 'exited';
      record.exitCode = exitCode;
      record.lastActivityAt = new Date().toISOString();
      for (const sub of record.subscribers) {
        sub.send({ type: 'exit', sessionId: record.sessionId, exitCode, signal });
      }
      for (const sub of record.subscribers) {
        sub.close();
      }
      record.subscribers.clear();
      record.writerPeer = null;
    });

    this.sessions.set(record.sessionId, record);

    if (launch.sendPwdAfterStart) {
      setTimeout(() => {
        if (record.status === 'running') {
          proc.write('pwd\r');
        }
      }, 50);
    }

    if (launch.startupCommands.length > 0) {
      setTimeout(() => {
        if (record.status !== 'running') {
          return;
        }

        for (const cmd of launch.startupCommands) {
          proc.write(`${cmd}\r`);
        }
      }, 100);
    }

    return {
      session: record,
      writeToken
    };
  }

  resolveLaunchOptions(options = {}) {
    const profile = options.profileId ? this.requireProfile(options.profileId) : null;
    const templateContext = {
      workspaceRoot: pickString(options.workspaceRoot, process.cwd()),
      taskId: pickString(options.taskId),
      profileName: pickString(profile?.name)
    };

    const cliType = pickString(options.cliType, profile?.cliType, 'custom');
    const shell = pickString(
      renderTemplate(options.shell, templateContext),
      renderTemplate(profile?.shell, templateContext),
      resolveDefaultExecutable(cliType)
    );
    const cwd = pickString(renderTemplate(options.cwd, templateContext), renderTemplate(profile?.cwd, templateContext), '/tmp');
    const mode = pickString(options.mode, 'execute');
    const title = pickString(
      renderTemplate(options.title, templateContext),
      renderTemplate(profile?.name, templateContext),
      (options.sessionId || '').slice(0, 8)
    );
    const cols = clampInt(options.cols, 40, 400, 160);
    const rows = clampInt(options.rows, 10, 200, 40);
    const env = {
      ...process.env,
      ...renderTemplateEnv(profile?.env || {}, templateContext),
      ...renderTemplateEnv(isObject(options.env) ? options.env : {}, templateContext)
    };
    env.PATH = appendPathEnv(env.PATH, '/www/server/nodejs/v22.22.0/bin');

    const command = normalizeString(renderTemplate(options.command, templateContext));
    const args = Array.isArray(options.args)
      ? normalizeStringArray(options.args).map((x) => renderTemplate(x, templateContext))
      : normalizeStringArray(profile?.args || []).map((x) => renderTemplate(x, templateContext));
    const startupCommands = Array.isArray(options.startupCommands)
      ? normalizeStringArray(options.startupCommands).map((x) => renderTemplate(x, templateContext))
      : normalizeStringArray(profile?.startupCommands || []).map((x) => renderTemplate(x, templateContext));

    let resolvedArgs = [];
    let sendPwdAfterStart = false;
    if (command.length > 0) {
      if (isShellLikeExecutable(shell)) {
        resolvedArgs = ['-lc', command];
      } else {
        resolvedArgs = [command];
      }
    } else if (args.length > 0) {
      resolvedArgs = args;
    } else if (isInteractiveShellExecutable(shell)) {
      resolvedArgs = ['-i'];
      sendPwdAfterStart = startupCommands.length === 0;
    }

    return {
      profileId: profile?.profileId || null,
      title,
      shell,
      cwd,
      cliType,
      mode,
      cols,
      rows,
      env,
      args: resolvedArgs,
      startupCommands,
      sendPwdAfterStart
    };
  }

  get(sessionId) {
    return this.sessions.get(sessionId);
  }

  write(sessionId, data) {
    const s = this.requireSession(sessionId);
    s.proc.write(data);
    s.lastActivityAt = new Date().toISOString();
  }

  resize(sessionId, cols, rows) {
    const s = this.requireSession(sessionId);
    s.proc.resize(Number(cols), Number(rows));
    s.lastActivityAt = new Date().toISOString();
  }

  terminate(sessionId, signal = 'SIGTERM') {
    const s = this.requireSession(sessionId);
    s.proc.kill(signal);
    s.lastActivityAt = new Date().toISOString();
    setTimeout(() => {
      if (s.status === 'running') {
        try {
          s.proc.kill('SIGKILL');
        } catch {
          // ignore
        }
      }
    }, 600);
  }

  remove(sessionId) {
    const s = this.requireSession(sessionId);
    if (s.status === 'running') {
      throw new Error('cannot remove running session');
    }
    this.sessions.delete(sessionId);
    return { ok: true, sessionId };
  }

  pruneExited() {
    let removed = 0;
    for (const [sessionId, s] of this.sessions.entries()) {
      if (s.status !== 'exited') {
        continue;
      }
      this.sessions.delete(sessionId);
      removed += 1;
    }
    return { ok: true, removed };
  }

  snapshot(sessionId, { limitBytes = this.maxOutputBufferBytes } = {}) {
    const s = this.requireSession(sessionId);
    const max = clampInt(limitBytes, 1, this.maxOutputBufferBytes, this.maxOutputBufferBytes);
    const snapshotData = collectTailDataWithinBytes(s.outputChunks, max);
    const snapshotBytes = Buffer.byteLength(snapshotData, 'utf8');
    const truncated = s.outputTruncated === true || s.outputBytes > snapshotBytes;
    return {
      sessionId: s.sessionId,
      status: s.status,
      exitCode: s.exitCode,
      data: snapshotData,
      bytes: snapshotBytes,
      totalBytes: s.outputBytes,
      truncated,
      maxOutputBufferBytes: s.maxOutputBufferBytes,
      headSeq: s.headSeq,
      tailSeq: s.tailSeq
    };
  }

  history(sessionId, { beforeSeq = null, limitBytes = 256 * 1024 } = {}) {
    const s = this.requireSession(sessionId);
    const max = clampInt(limitBytes, 1024, this.maxOutputBufferBytes, 256 * 1024);
    const normalizedBeforeSeq = Number.isFinite(Number(beforeSeq)) ? Math.trunc(Number(beforeSeq)) : null;
    const selected = collectHistoryBeforeSeq(s.outputChunks, normalizedBeforeSeq, max);
    const hasMore = selected.hasMore;
    return {
      sessionId: s.sessionId,
      chunks: selected.chunks,
      hasMore,
      nextBeforeSeq: hasMore && selected.chunks.length > 0 ? selected.chunks[0].seqStart : null,
      truncated: s.outputTruncated === true
    };
  }

  attach(sessionId, wsPeer, options = {}) {
    const s = this.requireSession(sessionId);
    const replay = options?.replay === true;
    const replayMode = normalizeReplayMode(options?.replayMode, replay ? 'full' : 'none');
    const sinceSeq = Number.isFinite(Number(options?.sinceSeq)) ? Math.trunc(Number(options.sinceSeq)) : null;
    let writable = this._isWritable(s, options?.writeToken);
    if (writable && s.writerPeer && s.writerPeer !== wsPeer) {
      writable = false;
    }
    if (writable) {
      s.writerPeer = wsPeer;
    }
    wsPeer.writable = writable;
    if (s.status === 'exited') {
      const fullData = joinChunksData(s.outputChunks);
      if ((replay || replayMode === 'full' || replayMode === 'tail') && fullData) {
        wsPeer.send({
          type: 'output',
          sessionId: s.sessionId,
          stream: 'stdout',
          data: fullData,
          replay: true,
          seqStart: s.headSeq,
          seqEnd: s.tailSeq,
          truncatedSince: false
        });
      }
      wsPeer.send({ type: 'exit', sessionId: s.sessionId, exitCode: s.exitCode, signal: null });
      wsPeer.close();
      return summarizeSession(s);
    }

    s.subscribers.add(wsPeer);
    wsPeer.send({
      type: 'ready',
      sessionId: s.sessionId,
      pid: s.proc.pid,
      status: s.status,
      writable,
      taskId: s.taskId,
      profileId: s.profileId,
      title: s.title,
      cwd: s.cwd,
      shell: s.shell,
      args: [...(s.args || [])],
      cliType: s.cliType,
      mode: s.mode,
      outputBytes: s.outputBytes,
      outputTruncated: s.outputTruncated === true,
      maxOutputBufferBytes: s.maxOutputBufferBytes,
      headSeq: s.headSeq,
      tailSeq: s.tailSeq,
      canDeltaReplay: true
    });
    if (replay || replayMode === 'full' || replayMode === 'tail') {
      const fullData = joinChunksData(s.outputChunks);
      if (fullData) {
        wsPeer.send({
          type: 'output',
          sessionId: s.sessionId,
          stream: 'stdout',
          data: fullData,
          replay: true,
          seqStart: s.headSeq,
          seqEnd: s.tailSeq,
          truncatedSince: false
        });
      }
      return summarizeSession(s);
    }

    if (replayMode === 'none' && sinceSeq !== null) {
      const delta = collectDeltaFromSeq(s.outputChunks, sinceSeq, s.headSeq, s.tailSeq);
      if (delta.truncatedSince === true) {
        wsPeer.send({
          type: 'output',
          sessionId: s.sessionId,
          stream: 'stdout',
          data: '',
          replay: false,
          seqStart: s.headSeq,
          seqEnd: s.tailSeq,
          truncatedSince: true
        });
        return summarizeSession(s);
      }
      for (const item of delta.chunks) {
        wsPeer.send({
          type: 'output',
          sessionId: s.sessionId,
          stream: 'stdout',
          data: item.data,
          replay: false,
          seqStart: item.seqStart,
          seqEnd: item.seqEnd,
          truncatedSince: false
        });
      }
    }
    return summarizeSession(s);
  }

  detach(sessionId, wsPeer) {
    const s = this.sessions.get(sessionId);
    if (!s) {
      return;
    }
    s.subscribers.delete(wsPeer);
    if (s.writerPeer === wsPeer) {
      s.writerPeer = null;
    }
  }

  isPeerWritable(sessionId, wsPeer) {
    const s = this.requireSession(sessionId);
    if (!s.writeTokenHash) {
      return true;
    }
    return s.writerPeer === wsPeer;
  }

  status(sessionId) {
    return summarizeSession(this.requireSession(sessionId));
  }

  list({ includeExited = true, profileId = '', taskId = '' } = {}) {
    const items = [];
    for (const s of this.sessions.values()) {
      if (!includeExited && s.status !== 'running') {
        continue;
      }
      if (profileId && s.profileId !== profileId) {
        continue;
      }
      if (taskId && s.taskId !== taskId) {
        continue;
      }

      items.push(summarizeSession(s));
    }

    items.sort((a, b) => String(b.lastActivityAt).localeCompare(String(a.lastActivityAt)));
    return items;
  }

  requireProfile(profileId) {
    const profile = this.profiles.get(profileId);
    if (!profile) {
      throw new Error(`profile not found: ${profileId}`);
    }
    return profile;
  }

  requireSession(sessionId) {
    const s = this.sessions.get(sessionId);
    if (!s) {
      throw new Error(`session not found: ${sessionId}`);
    }
    return s;
  }

  _seedBuiltinProfiles() {
    const now = new Date().toISOString();
    const builtins = [
      {
        profileId: 'builtin-bash',
        name: 'bash',
        cliType: 'custom',
        shell: '/bin/bash',
        cwd: '/tmp',
        args: [],
        env: {},
        startupCommands: [],
        quickCommands: [],
        cliOptions: {},
        icon: 'terminal',
        color: '#1ea7a4'
      },
      {
        profileId: 'builtin-codex',
        name: 'codex',
        cliType: 'codex',
        shell: 'codex',
        cwd: '/tmp',
        args: [],
        env: {},
        startupCommands: [],
        quickCommands: [],
        cliOptions: {},
        icon: 'bot',
        color: '#3a90e5'
      },
      {
        profileId: 'builtin-mcp-tools',
        name: 'mcp-tools',
        cliType: 'custom',
        shell: '/bin/bash',
        cwd: '/workspace/tools/mcp',
        args: [],
        env: {},
        startupCommands: ['pwd'],
        quickCommands: [],
        cliOptions: {},
        icon: 'tool',
        color: '#ff9f1a'
      },
      {
        profileId: 'builtin-skills-runner',
        name: 'skills-runner',
        cliType: 'custom',
        shell: '/bin/bash',
        cwd: '/workspace/skills',
        args: [],
        env: {},
        startupCommands: ['pwd'],
        quickCommands: [],
        cliOptions: {},
        icon: 'book',
        color: '#9cdb43'
      }
    ];

    for (const item of builtins) {
      this.profiles.set(item.profileId, {
        ...item,
        isBuiltin: true,
        createdAt: now,
        updatedAt: now
      });
    }
  }

  _loadCustomProfiles() {
    if (!this.profileStoreFile || !existsSync(this.profileStoreFile)) {
      return;
    }
    try {
      const raw = readFileSync(this.profileStoreFile, 'utf8');
      const parsed = JSON.parse(raw);
      if (!Array.isArray(parsed)) {
        return;
      }

      for (const item of parsed) {
        const normalized = normalizeProfileInput(item || {});
        if (!normalized.profileId || !normalized.name || !normalized.shell || !normalized.cwd) {
          continue;
        }
        if (this.profiles.has(normalized.profileId)) {
          continue;
        }
        this.profiles.set(normalized.profileId, {
          profileId: normalized.profileId,
          name: normalized.name,
          cliType: normalized.cliType || 'custom',
          shell: normalized.shell,
          cwd: normalized.cwd,
          args: normalized.args,
          env: normalized.env,
          startupCommands: normalized.startupCommands,
          quickCommands: normalized.quickCommands,
          cliOptions: normalized.cliOptions,
          icon: normalized.icon,
          color: normalized.color,
          isBuiltin: false,
          createdAt: normalizeString(item?.createdAt) || new Date().toISOString(),
          updatedAt: normalizeString(item?.updatedAt) || new Date().toISOString()
        });
      }
    } catch {
      // ignore malformed profile store
    }
  }

  _persistProfiles() {
    if (!this.profileStoreFile) {
      return;
    }
    const rows = Array.from(this.profiles.values())
      .filter((x) => x.isBuiltin !== true)
      .map((x) => ({
        profileId: x.profileId,
        name: x.name,
        cliType: x.cliType || 'custom',
        shell: x.shell,
        cwd: x.cwd,
        args: [...(x.args || [])],
        env: { ...(x.env || {}) },
        startupCommands: [...(x.startupCommands || [])],
        quickCommands: normalizeQuickCommands(x.quickCommands || []),
        cliOptions: normalizeCliOptions(x.cliOptions || {}),
        icon: x.icon || '',
        color: x.color || '',
        createdAt: x.createdAt || new Date().toISOString(),
        updatedAt: x.updatedAt || new Date().toISOString()
      }));
    try {
      mkdirSync(dirname(this.profileStoreFile), { recursive: true });
      writeFileSync(this.profileStoreFile, JSON.stringify(rows, null, 2), 'utf8');
    } catch {
      // ignore persistence failures for now
    }
  }

  _isWritable(session, writeToken = '') {
    if (!session.writeTokenHash) {
      return true;
    }

    const candidateHash = hashWriteToken(writeToken);
    const expectedHash = session.writeTokenHash;
    if (!candidateHash || !expectedHash || candidateHash.length !== expectedHash.length) {
      return false;
    }

    try {
      return timingSafeEqual(Buffer.from(candidateHash, 'hex'), Buffer.from(expectedHash, 'hex'));
    } catch {
      return false;
    }
  }

  _loadSettings() {
    if (!this.settingsStoreFile || !existsSync(this.settingsStoreFile)) {
      this.globalQuickCommands = [];
      return;
    }
    try {
      const raw = readFileSync(this.settingsStoreFile, 'utf8');
      const parsed = JSON.parse(raw);
      this.globalQuickCommands = normalizeQuickCommands(parsed?.globalQuickCommands || []);
      const configuredRoots = normalizeFsAllowedRoots(parsed?.fsAllowedRoots || []);
      if (configuredRoots.length > 0) {
        this.fsAllowedRoots = configuredRoots;
      }
    } catch {
      this.globalQuickCommands = [];
    }
  }

  _persistSettings() {
    if (!this.settingsStoreFile) {
      return;
    }
    const data = {
      globalQuickCommands: normalizeQuickCommands(this.globalQuickCommands || []),
      fsAllowedRoots: normalizeFsAllowedRoots(this.fsAllowedRoots || [])
    };
    try {
      mkdirSync(dirname(this.settingsStoreFile), { recursive: true });
      writeFileSync(this.settingsStoreFile, JSON.stringify(data, null, 2), 'utf8');
    } catch {
      // ignore persistence failures for now
    }
  }
}

function resolveDefaultExecutable(cliType) {
  const normalized = normalizeString(cliType).toLowerCase();
  if (normalized === 'codex') {
    return 'codex';
  }
  if (normalized === 'claude') {
    return 'claude';
  }
  if (normalized === 'bash') {
    return '/bin/bash';
  }
  return '/bin/bash';
}

function isShellLikeExecutable(value) {
  const shell = normalizeString(value).toLowerCase();
  return shell.includes('bash') || shell.includes('zsh') || shell.endsWith('/sh') || shell === 'sh';
}

function isInteractiveShellExecutable(value) {
  const shell = normalizeString(value).toLowerCase();
  return shell.includes('bash') || shell.includes('zsh');
}

function summarizeSession(s) {
  return {
    sessionId: s.sessionId,
    taskId: s.taskId,
    cliType: s.cliType,
    mode: s.mode,
    profileId: s.profileId,
    title: s.title,
    shell: s.shell,
    cwd: s.cwd,
    args: [...(s.args || [])],
    pid: s.proc.pid,
    status: s.status,
    createdAt: s.createdAt,
    lastActivityAt: s.lastActivityAt,
    exitCode: s.exitCode,
    outputBytes: s.outputBytes,
    outputTruncated: s.outputTruncated === true,
    maxOutputBufferBytes: s.maxOutputBufferBytes,
    backend: 'node-pty'
  };
}

function cloneProfile(profile) {
  return {
    ...profile,
    args: [...(profile.args || [])],
    env: { ...(profile.env || {}) },
    startupCommands: [...(profile.startupCommands || [])],
    quickCommands: normalizeQuickCommands(profile.quickCommands || []),
    cliOptions: normalizeCliOptions(profile.cliOptions || {})
  };
}

function normalizeProfileInput(input = {}) {
  return {
    profileId: normalizeString(input.profileId),
    name: normalizeString(input.name),
    cliType: normalizeString(input.cliType),
    shell: normalizeString(input.shell),
    cwd: normalizeString(input.cwd),
    args: normalizeStringArray(input.args || []),
    env: normalizeEnv(input.env),
    startupCommands: normalizeStringArray(input.startupCommands || []),
    quickCommands: normalizeQuickCommands(input.quickCommands || []),
    cliOptions: normalizeCliOptions(input.cliOptions || {}),
    icon: normalizeString(input.icon),
    color: normalizeString(input.color)
  };
}

function normalizeQuickCommands(value) {
  if (!Array.isArray(value)) {
    return [];
  }
  const items = [];
  for (const item of value) {
    if (typeof item === 'string') {
      const content = normalizeString(item);
      if (!content) {
        continue;
      }
      items.push({
        id: randomBytes(8).toString('hex'),
        label: content.length > 24 ? `${content.slice(0, 24)}...` : content,
        content,
        sendMode: 'auto',
        enabled: true,
        order: items.length
      });
      continue;
    }

    if (!isObject(item)) {
      continue;
    }
    const content = normalizeString(item.content);
    if (!content) {
      continue;
    }
    const id = normalizeString(item.id) || randomBytes(8).toString('hex');
    const label = normalizeString(item.label) || (content.length > 24 ? `${content.slice(0, 24)}...` : content);
    const sendMode = ['auto', 'enter', 'raw'].includes(normalizeString(item.sendMode)) ? normalizeString(item.sendMode) : 'auto';
    const enabled = item.enabled !== false;
    const order = Number.isFinite(Number(item.order)) ? Math.trunc(Number(item.order)) : items.length;
    items.push({ id, label, content, sendMode, enabled, order });
  }
  items.sort((a, b) => a.order - b.order);
  return items.map((item, index) => ({ ...item, order: index }));
}

function normalizeFsAllowedRoots(list = []) {
  if (!Array.isArray(list)) {
    return [];
  }
  const out = [];
  for (const raw of list) {
    const value = normalizeString(raw);
    if (!value || !isAbsolute(value)) {
      continue;
    }
    out.push(resolve(value));
  }
  return Array.from(new Set(out));
}

function normalizeCliOptions(value) {
  if (!isObject(value)) {
    return {};
  }
  return {
    executable: normalizeString(value.executable),
    defaultArgs: normalizeStringArray(value.defaultArgs || []),
    cwdStrategy: normalizeString(value.cwdStrategy),
    env: normalizeEnv(value.env)
  };
}

function normalizeEnv(value) {
  if (!isObject(value)) {
    return {};
  }

  const env = {};
  for (const [key, v] of Object.entries(value)) {
    const k = normalizeString(key);
    if (!k) {
      continue;
    }
    env[k] = String(v ?? '');
  }
  return env;
}

function renderTemplateEnv(env, context) {
  const out = {};
  for (const [key, value] of Object.entries(env || {})) {
    const normalizedKey = normalizeString(key);
    if (!normalizedKey) {
      continue;
    }
    out[normalizedKey] = renderTemplate(String(value ?? ''), context);
  }
  return out;
}

function normalizeString(value) {
  return String(value ?? '').trim();
}

function normalizeStringArray(value) {
  if (!Array.isArray(value)) {
    return [];
  }
  const items = [];
  for (const item of value) {
    const normalized = normalizeString(item);
    if (normalized) {
      items.push(normalized);
    }
  }
  return items;
}

function pickString(...values) {
  for (const value of values) {
    const normalized = normalizeString(value);
    if (normalized) {
      return normalized;
    }
  }
  return '';
}

function isObject(value) {
  return value !== null && typeof value === 'object' && !Array.isArray(value);
}

function clampInt(value, min, max, fallback) {
  const num = Number(value);
  if (!Number.isFinite(num)) {
    return fallback;
  }
  return Math.min(max, Math.max(min, Math.trunc(num)));
}

function renderTemplate(value, context) {
  const source = String(value ?? '');
  if (!source) {
    return '';
  }

  const replacements = {
    '${workspaceRoot}': String(context.workspaceRoot || ''),
    '${taskId}': String(context.taskId || ''),
    '${profileName}': String(context.profileName || '')
  };

  let rendered = source;
  for (const [token, actual] of Object.entries(replacements)) {
    rendered = rendered.split(token).join(actual);
  }
  return rendered.trim();
}

function appendOutputChunk(session, chunk) {
  const data = String(chunk || '');
  if (!data) {
    return { seqStart: session.nextSeq, seqEnd: session.nextSeq - 1, truncated: false };
  }

  let normalizedData = data;
  let seqStart = session.nextSeq;
  let seqEnd = seqStart + normalizedData.length - 1;
  let bytes = Buffer.byteLength(normalizedData, 'utf8');
  let truncated = false;
  if (bytes > session.maxOutputBufferBytes) {
    const trimmed = trimTailByBytes(normalizedData, session.maxOutputBufferBytes);
    const droppedChars = normalizedData.length - trimmed.length;
    normalizedData = trimmed;
    seqStart += droppedChars;
    seqEnd = seqStart + normalizedData.length - 1;
    bytes = Buffer.byteLength(normalizedData, 'utf8');
    truncated = true;
  }
  session.outputChunks.push({ data: normalizedData, bytes, seqStart, seqEnd });
  session.outputBytes += bytes;
  session.nextSeq = seqEnd + 1;
  session.headSeq = session.outputChunks[0]?.seqStart ?? session.nextSeq;
  session.tailSeq = session.outputChunks[session.outputChunks.length - 1]?.seqEnd ?? (session.nextSeq - 1);

  while (session.outputBytes > session.maxOutputBufferBytes && session.outputChunks.length > 0) {
    const dropped = session.outputChunks.shift();
    if (!dropped) {
      break;
    }
    session.outputBytes -= dropped.bytes;
    truncated = true;
  }

  if (session.outputBytes < 0) {
    session.outputBytes = 0;
  }
  session.headSeq = session.outputChunks[0]?.seqStart ?? session.nextSeq;
  session.tailSeq = session.outputChunks[session.outputChunks.length - 1]?.seqEnd ?? (session.nextSeq - 1);
  return { seqStart, seqEnd, truncated };
}

function collectTailDataWithinBytes(chunks, limitBytes) {
  if (!Array.isArray(chunks) || chunks.length === 0) {
    return '';
  }
  const selected = [];
  let usedBytes = 0;
  for (let idx = chunks.length - 1; idx >= 0; idx -= 1) {
    const chunk = chunks[idx];
    if (!chunk) {
      continue;
    }
    if (usedBytes + chunk.bytes <= limitBytes) {
      selected.push(chunk.data);
      usedBytes += chunk.bytes;
      continue;
    }
    const remaining = limitBytes - usedBytes;
    if (remaining > 0) {
      selected.push(trimTailByBytes(chunk.data, remaining));
    }
    break;
  }
  return selected.reverse().join('');
}

function collectHistoryBeforeSeq(chunks, beforeSeq, limitBytes) {
  if (!Array.isArray(chunks) || chunks.length === 0) {
    return { chunks: [], hasMore: false };
  }
  const boundary = Number.isFinite(beforeSeq) ? Math.trunc(beforeSeq) : Number.POSITIVE_INFINITY;
  const selected = [];
  let usedBytes = 0;
  let hasMore = false;
  for (let idx = chunks.length - 1; idx >= 0; idx -= 1) {
    const chunk = chunks[idx];
    if (!chunk || chunk.seqStart >= boundary) {
      continue;
    }
    let candidateData = chunk.data;
    let candidateSeqStart = chunk.seqStart;
    let candidateSeqEnd = chunk.seqEnd;
    if (candidateSeqEnd >= boundary) {
      const keepLen = Math.max(0, boundary - candidateSeqStart);
      candidateData = candidateData.slice(0, keepLen);
      candidateSeqEnd = boundary - 1;
    }
    if (!candidateData || candidateSeqEnd < candidateSeqStart) {
      continue;
    }
    let candidateBytes = Buffer.byteLength(candidateData, 'utf8');
    if (usedBytes + candidateBytes <= limitBytes) {
      selected.push({ data: candidateData, bytes: candidateBytes, seqStart: candidateSeqStart, seqEnd: candidateSeqEnd });
      usedBytes += candidateBytes;
      continue;
    }
    const remaining = limitBytes - usedBytes;
    if (remaining > 0) {
      const trimmed = trimTailByBytes(candidateData, remaining);
      const shift = candidateData.length - trimmed.length;
      selected.push({
        data: trimmed,
        bytes: Buffer.byteLength(trimmed, 'utf8'),
        seqStart: candidateSeqStart + shift,
        seqEnd: candidateSeqEnd
      });
      usedBytes = limitBytes;
    }
    hasMore = idx > 0;
    break;
  }
  if (selected.length > 0) {
    return { chunks: selected.reverse().map((x) => ({ data: x.data, seqStart: x.seqStart, seqEnd: x.seqEnd })), hasMore };
  }
  return { chunks: [], hasMore: false };
}

function collectDeltaFromSeq(chunks, sinceSeq, headSeq, tailSeq) {
  if (!Number.isFinite(sinceSeq)) {
    return { chunks: [], truncatedSince: false };
  }
  if (sinceSeq < headSeq - 1) {
    return { chunks: [], truncatedSince: true };
  }
  if (sinceSeq >= tailSeq) {
    return { chunks: [], truncatedSince: false };
  }

  const items = [];
  for (const chunk of chunks) {
    if (!chunk || chunk.seqEnd <= sinceSeq) {
      continue;
    }
    if (chunk.seqStart > sinceSeq) {
      items.push({ data: chunk.data, seqStart: chunk.seqStart, seqEnd: chunk.seqEnd });
      continue;
    }
    const cut = sinceSeq - chunk.seqStart + 1;
    const partial = chunk.data.slice(cut);
    if (!partial) {
      continue;
    }
    items.push({ data: partial, seqStart: sinceSeq + 1, seqEnd: chunk.seqEnd });
  }
  return { chunks: items, truncatedSince: false };
}

function joinChunksData(chunks) {
  if (!Array.isArray(chunks) || chunks.length === 0) {
    return '';
  }
  return chunks.map((x) => x.data || '').join('');
}

function trimTailByBytes(data, maxBytes) {
  const text = String(data || '');
  if (!text || maxBytes <= 0) {
    return '';
  }
  if (Buffer.byteLength(text, 'utf8') <= maxBytes) {
    return text;
  }
  let start = 0;
  let out = text;
  while (out && Buffer.byteLength(out, 'utf8') > maxBytes && start < text.length) {
    start += 1;
    out = text.slice(start);
  }
  return out;
}

function normalizeReplayMode(value, fallback = 'none') {
  const mode = normalizeString(value).toLowerCase();
  if (mode === 'none' || mode === 'tail' || mode === 'full') {
    return mode;
  }
  return fallback;
}

function generateWriteToken() {
  return randomBytes(24).toString('base64url');
}

function hashWriteToken(token = '') {
  const normalized = normalizeString(token);
  if (!normalized) {
    return '';
  }
  return createHash('sha256').update(normalized).digest('hex');
}

function appendPathEnv(currentPath = '', binDir = '') {
  const dir = normalizeString(binDir);
  if (!dir) {
    return normalizeString(currentPath);
  }
  const source = normalizeString(currentPath);
  if (!source) {
    return dir;
  }
  const parts = source.split(':').filter(Boolean);
  if (parts.includes(dir)) {
    return source;
  }
  return `${dir}:${source}`;
}
