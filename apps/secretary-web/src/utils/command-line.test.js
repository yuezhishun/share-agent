import test from 'node:test';
import assert from 'node:assert/strict';
import { formatCommandLine, parseCommandLine } from './command-line.js';

test('parseCommandLine should parse plain shell-style command lines', () => {
  assert.deepEqual(parseCommandLine('python3 -m http.server 9000'), {
    command: 'python3',
    args: ['-m', 'http.server', '9000']
  });
});

test('parseCommandLine should support quoted arguments and escapes', () => {
  assert.deepEqual(parseCommandLine('bash -lc "echo \\"hello world\\""'), {
    command: 'bash',
    args: ['-lc', 'echo "hello world"']
  });

  assert.deepEqual(parseCommandLine(String.raw`node script.js --path /tmp/my\ folder`), {
    command: 'node',
    args: ['script.js', '--path', '/tmp/my folder']
  });
});

test('parseCommandLine should support JSON array input', () => {
  assert.deepEqual(parseCommandLine('["bash","-lc","npm run dev"]'), {
    command: 'bash',
    args: ['-lc', 'npm run dev']
  });
});

test('parseCommandLine should reject empty and malformed input', () => {
  assert.throws(() => parseCommandLine(''), /命令不能为空/);
  assert.throws(() => parseCommandLine('   '), /命令不能为空/);
  assert.throws(() => parseCommandLine('"unterminated'), /未闭合/);
  assert.throws(() => parseCommandLine('[]'), /命令不能为空/);
});

test('formatCommandLine should preserve argument boundaries for editing', () => {
  assert.equal(
    formatCommandLine('bash', ['-lc', 'npm run dev']),
    'bash -lc "npm run dev"'
  );

  assert.equal(
    formatCommandLine('python3', ['script.py', '--name', 'hello world']),
    'python3 script.py --name "hello world"'
  );
});
