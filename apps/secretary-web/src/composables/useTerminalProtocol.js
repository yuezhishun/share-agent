export function createTerminalProtocolRenderer(term) {
  const state = {
    cols: 80,
    rows: 25,
    visibleRows: [],
    historyRows: [],
    styles: { '0': {} },
    cursor: { x: 0, y: 0, visible: true },
    rawActive: false,
    nextBefore: 'h-1',
    exhausted: false
  };

  function toPlainLine(segs) {
    return (segs || []).map((seg) => (Array.isArray(seg) ? seg[0] || '' : '')).join('');
  }

  function normalizeRows(rows, totalRows) {
    const arr = new Array(totalRows).fill(null).map(() => [['', 0]]);
    for (const row of rows || []) {
      if (Number.isFinite(row?.y) && row.y >= 0 && row.y < totalRows) {
        arr[row.y] = Array.isArray(row.segs) ? row.segs : [['', 0]];
      }
    }
    return arr;
  }

  function syncCursor() {
    const x = Math.max(0, Math.min(state.cols - 1, Number(state.cursor?.x || 0)));
    const y = Math.max(0, Math.min(state.rows - 1, Number(state.cursor?.y || 0)));
    const show = state.rawActive || state.cursor.visible ? '\u001b[?25h' : '\u001b[?25l';
    term.write(`${show}\u001b[${y + 1};${x + 1}H`);
  }

  function renderFull() {
    const merged = state.historyRows.concat(state.visibleRows);
    term.clear();
    if (merged.length === 0) {
      return;
    }

    for (let i = 0; i < merged.length; i += 1) {
      const line = toPlainLine(merged[i]);
      if (i < merged.length - 1) {
        term.write(`${line}\r\n`);
      } else {
        term.write(line);
      }
    }
    syncCursor();
  }

  function renderPatchRows(patches) {
    for (const patch of patches) {
      if (!Number.isFinite(patch?.y) || patch.y < 0 || patch.y >= state.rows) {
        continue;
      }
      const line = toPlainLine(patch.segs || [['', 0]]);
      term.write(`\u001b[${patch.y + 1};1H\u001b[2K${line}`);
    }
    syncCursor();
  }

  function onMessage(message) {
    if (!message || typeof message !== 'object') {
      return;
    }

    if (message.type === 'term.raw') {
      state.rawActive = true;
      if (message.replay) {
        term.reset();
      }
      term.write(String(message.data || ''));
      return;
    }

    if (message.type === 'term.snapshot') {
      state.rawActive = false;
      state.cols = Number(message?.size?.cols || state.cols);
      state.rows = Number(message?.size?.rows || state.rows);
      state.cursor = message.cursor || state.cursor;
      state.styles = message.styles || state.styles;
      state.visibleRows = normalizeRows(message.rows, state.rows);
      state.historyRows = [];
      state.nextBefore = message?.history?.newest_cursor || state.nextBefore;
      term.reset();
      term.resize(state.cols, state.rows);
      renderFull();
      return;
    }

    if (message.type === 'term.patch') {
      state.rawActive = false;
      state.cursor = message.cursor || state.cursor;
      state.styles = message.styles || state.styles;
      const patches = Array.isArray(message.rows) ? message.rows : [];
      for (const patch of patches) {
        if (Number.isFinite(patch?.y) && patch.y >= 0 && patch.y < state.rows) {
          state.visibleRows[patch.y] = patch.segs || [['', 0]];
        }
      }
      renderPatchRows(patches);
      return;
    }

    if (message.type === 'term.history.chunk') {
      const mapped = (message.lines || []).map((x) => x.segs || [['', 0]]);
      state.historyRows = mapped.concat(state.historyRows);
      state.nextBefore = message.next_before || state.nextBefore;
      state.exhausted = message.exhausted === true;
      renderFull();
      return;
    }

    if (message.type === 'term.exit') {
      term.write(`\r\n[exit] code=${message.code}`);
      return;
    }

    if (message.type === 'error') {
      term.write(`\r\n[error] ${String(message.error || message.message || 'unknown error')}`);
    }
  }

  return {
    state,
    onMessage,
    renderFull
  };
}
