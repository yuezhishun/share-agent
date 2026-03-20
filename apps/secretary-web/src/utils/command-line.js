function isWhitespace(char) {
  return /\s/.test(char);
}

function quoteShellToken(token) {
  const text = String(token ?? '');
  if (!text) {
    return '""';
  }
  if (/^[A-Za-z0-9_./:@%+=,-]+$/.test(text)) {
    return text;
  }
  return `"${text
    .replace(/\\/g, '\\\\')
    .replace(/"/g, '\\"')
    .replace(/\n/g, '\\n')
    .replace(/\r/g, '\\r')
    .replace(/\t/g, '\\t')}"`;
}

export function formatCommandLine(command, args = []) {
  const parts = [String(command || '').trim(), ...(Array.isArray(args) ? args : []).map((item) => String(item ?? ''))]
    .filter((item, index) => index === 0 ? item.length > 0 : true);
  return parts.map((item) => quoteShellToken(item)).join(' ').trim();
}

export function parseCommandLine(input) {
  const raw = String(input || '').trim();
  if (!raw) {
    throw new Error('命令不能为空');
  }

  if (raw.startsWith('[')) {
    let parsed;
    try {
      parsed = JSON.parse(raw);
    } catch {
      parsed = null;
    }
    if (Array.isArray(parsed)) {
      const tokens = parsed.map((item) => String(item ?? ''));
      const [command, ...args] = tokens;
      if (!command || !String(command).trim()) {
        throw new Error('命令不能为空');
      }
      return { command: String(command).trim(), args };
    }
  }

  const tokens = [];
  let current = '';
  let quote = '';
  let escaping = false;

  for (let index = 0; index < raw.length; index += 1) {
    const char = raw[index];

    if (escaping) {
      if (char === 'n') {
        current += '\n';
      } else if (char === 'r') {
        current += '\r';
      } else if (char === 't') {
        current += '\t';
      } else {
        current += char;
      }
      escaping = false;
      continue;
    }

    if (quote === "'") {
      if (char === "'") {
        quote = '';
      } else {
        current += char;
      }
      continue;
    }

    if (char === '\\') {
      escaping = true;
      continue;
    }

    if (quote === '"') {
      if (char === '"') {
        quote = '';
      } else {
        current += char;
      }
      continue;
    }

    if (char === '"' || char === "'") {
      quote = char;
      continue;
    }

    if (isWhitespace(char)) {
      if (current.length > 0) {
        tokens.push(current);
        current = '';
      }
      continue;
    }

    current += char;
  }

  if (escaping) {
    current += '\\';
  }

  if (quote) {
    throw new Error('命令行包含未闭合的引号');
  }

  if (current.length > 0) {
    tokens.push(current);
  }

  const [command, ...args] = tokens;
  if (!command) {
    throw new Error('命令不能为空');
  }

  return {
    command,
    args
  };
}
