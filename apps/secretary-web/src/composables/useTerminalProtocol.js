export function createTerminalProtocolRenderer(term) {
  const RAW_WRITE_CHUNK_SIZE = 12 * 1024;
  const state = {
    renderSuspended: false,
    baselineSeq: 0,
    writeToken: 0,
    writing: false,
    pendingWrites: [],
    historyLines: [],
    lastSnapshot: null
  };

  function normalizeSeq(value, fallback = 0) {
    const seq = Number(value);
    if (!Number.isFinite(seq)) {
      return Math.max(0, Number(fallback) || 0);
    }
    return Math.max(0, Math.floor(seq));
  }

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

  function applyMessageSize(message) {
    const cols = Math.max(0, Number(message?.size?.cols) || 0);
    const rows = Math.max(0, Number(message?.size?.rows) || 0);
    if (cols <= 0 || rows <= 0 || typeof term?.resize !== 'function') {
      return;
    }
    if (Number(term.cols) === cols && Number(term.rows) === rows) {
      return;
    }
    term.resize(cols, rows);
  }

  function buildSnapshotCursorControl(cursor) {
    const x = normalizeCursorCoord(cursor?.x);
    const y = normalizeCursorCoord(cursor?.y);
    let control = `\u001b[${y + 1};${x + 1}H`;
    if (cursor?.visible === false) {
      control += '\u001b[?25l';
    } else {
      control += '\u001b[?25h';
    }
    return control;
  }

  function renderSnapshot(message) {
    applyMessageSize(message);
    hardReset();
    const ansi = String(message?.ansi || '');
    if (ansi) {
      queueChunkedWrite(ansi);
      return;
    }
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

    const lines = [];
    for (let y = 0; y <= maxY; y += 1) {
      lines.push(String(mapped[y] || ''));
    }

    queueWrite(`${lines.join('\r\n')}${buildSnapshotCursorControl(message?.cursor)}`);
  }

  function renderWithHistory(snapshot, historyLines) {
    hardReset();

    const rows = Array.isArray(snapshot?.rows) ? snapshot.rows : [];
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

    const visibleLines = [];
    for (let y = 0; y <= maxY; y += 1) {
      visibleLines.push(String(mapped[y] || ''));
    }

    const combined = [...historyLines, ...visibleLines];
    queueWrite(`${combined.join('\r\n')}${buildSnapshotCursorControl(snapshot?.cursor)}`);
  }

  function onMessage(message) {
    if (!message || typeof message !== 'object') {
      return;
    }

    if (message.type === 'term.snapshot') {
      state.renderSuspended = false;
      state.baselineSeq = normalizeSeq(message?.seq, normalizeSeq(message?.base_seq, state.baselineSeq));
      state.lastSnapshot = message;
      state.historyLines = [];
      renderSnapshot(message);
      return;
    }

    if (message.type === 'term.history.chunk') {
      if (!state.lastSnapshot) {
        return;
      }
      const lines = Array.isArray(message?.lines)
        ? message.lines.map((line) => decodeSegText(line?.segs))
        : [];
      state.historyLines = [...lines, ...state.historyLines];
      renderWithHistory(state.lastSnapshot, state.historyLines);
      return;
    }

    if (message.type === 'term.patch') {
      applyMessageSize(message);
    }

    if (message.type === 'term.raw') {
      const rawSeq = normalizeSeq(message?.seq, normalizeSeq(message?.to_seq, 0));
      if (rawSeq > 0 && rawSeq <= state.baselineSeq) {
        return;
      }
      if (state.renderSuspended && message.replay !== true) {
        return;
      }
      if (rawSeq > state.baselineSeq) {
        state.baselineSeq = rawSeq;
      }
      queueChunkedWrite(String(message.data || ''));
      return;
    }

    if (message.type === 'term.sync.required') {
      state.renderSuspended = true;
      return;
    }

    if (message.type === 'term.resize.ack' && message.accepted === true) {
      state.renderSuspended = true;
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
