import test from 'node:test';
import assert from 'node:assert/strict';
import { resolveEditorKind } from './desktop-terminal.js';

test('resolveEditorKind should detect markdown and image previews', () => {
  assert.equal(resolveEditorKind('/tmp/readme.md'), 'markdown-ir');
  assert.equal(resolveEditorKind('/tmp/screenshot.PNG'), 'image');
  assert.equal(resolveEditorKind('/tmp/logo.svg'), 'image');
  assert.equal(resolveEditorKind('/tmp/server.log'), 'code');
});
