import test from 'node:test';
import assert from 'node:assert/strict';
import { compressPath, formatInstanceSummary, resolveEditorKind } from './desktop-terminal.js';
import {
  DEFAULT_VOICE_COMMIT_DELAY_MS,
  VOICE_MODE_SHORTCUT_LABEL,
  isVoiceToggleShortcut
} from './voice-terminal.js';

test('resolveEditorKind should detect markdown and image previews', () => {
  assert.equal(resolveEditorKind('/tmp/readme.md'), 'markdown-ir');
  assert.equal(resolveEditorKind('/tmp/screenshot.PNG'), 'image');
  assert.equal(resolveEditorKind('/tmp/logo.svg'), 'image');
  assert.equal(resolveEditorKind('/tmp/server.log'), 'code');
});

test('voice terminal helpers should expose the expected defaults', () => {
  assert.equal(DEFAULT_VOICE_COMMIT_DELAY_MS, 360);
  assert.equal(VOICE_MODE_SHORTCUT_LABEL, 'Ctrl+↓');
});

test('voice terminal shortcut should require ctrl plus arrow down only', () => {
  assert.equal(isVoiceToggleShortcut({ ctrlKey: true, code: 'ArrowDown', key: 'ArrowDown' }), true);
  assert.equal(isVoiceToggleShortcut({ ctrlKey: true, code: 'KeyJ', key: 'j' }), false);
  assert.equal(isVoiceToggleShortcut({ ctrlKey: false, code: 'ArrowDown', key: 'ArrowDown' }), false);
  assert.equal(isVoiceToggleShortcut({ ctrlKey: true, altKey: true, code: 'ArrowDown', key: 'ArrowDown' }), false);
});

test('compressPath should normalize windows separators', () => {
  assert.equal(compressPath('D:\\workspace\\code'), 'D:/workspace/code');
});

test('formatInstanceSummary should shorten verbose shell launch command', () => {
  assert.equal(
    formatInstanceSummary({
      command: 'bash /home/yueyuan/pty-agent/apps/terminal-gateway-dotnet/TerminalGateway.Api/D:\\workspace\\code',
      cwd: 'D:\\workspace\\code'
    }),
    'bash D:\\workspace\\code'
  );
});
