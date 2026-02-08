import { homedir } from 'node:os';

function parseAllowedRoots(raw) {
  return String(raw || '')
    .split(',')
    .map((x) => x.trim())
    .filter((x) => x.length > 0);
}

function parsePositiveInt(raw, fallback) {
  const value = Number(raw);
  if (!Number.isFinite(value) || value <= 0) {
    return fallback;
  }
  return Math.trunc(value);
}

export function loadConfig() {
  const home = homedir();
  const defaultCodexConfigPath = `${home}/.codex/config.toml`;
  const defaultClaudeConfigPath = `${home}/.claude/config.json`;
  // Security-first default: do not expose /root implicitly.
  const defaultAllowedRoots = ['/home', '/workspace', '/www'];

  const configuredRoots = parseAllowedRoots(process.env.TERMINAL_FS_ALLOWED_ROOTS);

  return {
    port: Number(process.env.PORT || 7300),
    host: process.env.HOST || '0.0.0.0',
    internalToken: process.env.TERMINAL_GATEWAY_TOKEN || 'dev-terminal-token',
    wsToken: process.env.TERMINAL_WS_TOKEN || 'dev-ws-token',
    profileStoreFile: process.env.TERMINAL_PROFILE_STORE_FILE || '',
    settingsStoreFile: process.env.TERMINAL_SETTINGS_STORE_FILE || '/tmp/pty-agent-terminal-settings.json',
    maxOutputBufferBytes: parsePositiveInt(process.env.TERMINAL_MAX_OUTPUT_BUFFER_BYTES, 8 * 1024 * 1024),
    codexConfigPath: process.env.TERMINAL_CODEX_CONFIG_PATH || defaultCodexConfigPath,
    claudeConfigPath: process.env.TERMINAL_CLAUDE_CONFIG_PATH || defaultClaudeConfigPath,
    fsAllowedRoots: configuredRoots.length > 0 ? configuredRoots : defaultAllowedRoots
  };
}
