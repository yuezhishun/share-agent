export function createTerminalProtocolRenderer(term) {
  const RAW_WRITE_CHUNK_SIZE = 12 * 1024;
  const state = {
    rawActive: true,
    writeToken: 0,
    writing: false,
    pendingWrites: []
  };

  function pumpWriteQueue(token) {
    if (token !== state.writeToken) {
      return;
    }

    const next = state.pendingWrites.shift();
    if (typeof next !== 'string' || next.length === 0) {
      state.writing = false;
      return;
    }

    term.write(next, () => {
      pumpWriteQueue(token);
    });
  }

  function queueWrite(text) {
    const chunk = String(text || '');
    if (!chunk) {
      return;
    }
    state.pendingWrites.push(chunk);
    if (state.writing) {
      return;
    }
    state.writing = true;
    pumpWriteQueue(state.writeToken);
  }

  function queueChunkedWrite(text) {
    const payload = String(text || '');
    if (!payload) {
      return;
    }
    for (let index = 0; index < payload.length; index += RAW_WRITE_CHUNK_SIZE) {
      queueWrite(payload.slice(index, index + RAW_WRITE_CHUNK_SIZE));
    }
  }

  function hardReset() {
    state.writeToken += 1;
    state.pendingWrites = [];
    state.writing = false;
    term.reset();
  }

  function decodeSegText(segs) {
    if (!Array.isArray(segs)) {
      return '';
    }
    return segs
      .map((item) => {
        if (!Array.isArray(item)) {
          return '';
        }
        return String(item[0] || '');
      })
      .join('');
  }

  function normalizeCursorCoord(value) {
    if (!Number.isFinite(value)) {
      return 0;
    }
    return Math.max(0, Math.floor(value));
  }

  function buildSnapshotCursorControl(cursor) {
    const x = normalizeCursorCoord(cursor?.x);
    const y = normalizeCursorCoord(cursor?.y);
    let control = `\u001b[${y + 1};${x + 1}H`;
    if (cursor?.visible === false) {
      control += '\u001b[?25l';
    } else if (cursor?.visible === true) {
      control += '\u001b[?25h';
    }
    return control;
  }

  function renderSnapshot(message) {
    hardReset();
    const rows = Array.isArray(message?.rows) ? message.rows : [];
    const mapped = [];
    let maxY = -1;
    for (const row of rows) {
      if (!Number.isFinite(row?.y) || row.y < 0) {
        continue;
      }
      const y = Math.floor(row.y);
      if (y > maxY) {
        maxY = y;
      }
      mapped[y] = decodeSegText(row.segs);
    }
    let text = '';
    if (maxY >= 0) {
      const lines = [];
      for (let y = 0; y <= maxY; y += 1) {
        lines.push(String(mapped[y] || ''));
      }
      text = lines.join('\r\n');
    }

    queueWrite(`${text}${buildSnapshotCursorControl(message?.cursor)}`);
  }

  function onMessage(message) {
    if (!message || typeof message !== 'object') {
      return;
    }

    if (message.type === 'term.snapshot') {
      state.rawActive = false;
      renderSnapshot(message);
      return;
    }

    if (message.type === 'term.raw') {
      state.rawActive = true;
      if (message.replay && message.reset) {
        hardReset();
      }
      queueChunkedWrite(String(message.data || ''));
      return;
    }

    if (message.type === 'term.exit') {
      queueWrite(`\r\n[exit] code=${message.code}`);
      return;
    }

    if (message.type === 'error') {
      queueWrite(`\r\n[error] ${String(message.error || message.message || 'unknown error')}`);
    }
  }

  return {
    state,
    onMessage
  };
}
