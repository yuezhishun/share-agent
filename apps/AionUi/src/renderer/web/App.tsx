import { Button, Message } from '@arco-design/web-react';
import { useMemo, useState } from 'react';
import { NavLink, Navigate, Route, Routes, useLocation, useNavigate, useParams } from 'react-router-dom';
import { env } from './env';
import { stringifyKeyValue, toTemplateInput, useCliProcesses, useCliTemplates, useMcpConfig, useSessionTerminal, useSessions } from './hooks';
import type { CliTemplateRecord, McpServerDraft } from './types';

const legacyRoutes = [
  '/login',
  '/guid',
  '/settings/gemini',
  '/settings/model',
  '/settings/assistants',
  '/settings/agent',
  '/settings/skills-hub',
  '/settings/display',
  '/settings/webui',
  '/settings/system',
  '/settings/about',
  '/settings/tools',
  '/settings/ext/legacy',
];

function formatDate(value?: string | null) {
  if (!value) return '-';
  try {
    return new Date(value).toLocaleString();
  } catch {
    return value;
  }
}

function TopBar() {
  return (
    <header className="topbar">
      <div>
        <div className="topbar__brand">AionUi Web</div>
        <div className="muted">TerminalGateway.Api only</div>
      </div>
      <nav className="topbar__nav" aria-label="Primary">
        <NavLink className={({ isActive }) => `topbar__link${isActive ? ' active' : ''}`} to="/sessions">
          Sessions
        </NavLink>
        <NavLink className={({ isActive }) => `topbar__link${isActive ? ' active' : ''}`} to="/settings/cli">
          CLI / MCP
        </NavLink>
      </nav>
    </header>
  );
}

function SessionsPage() {
  const navigate = useNavigate();
  const { sessions, createSession, deleteSession, pruneExited, isLoading, error } = useSessions();
  const [form, setForm] = useState({
    title: '',
    shell: '/bin/bash',
    cwd: '/tmp',
    command: '',
    cols: '160',
    rows: '40',
    envText: '',
  });

  async function onCreate() {
    try {
      const created = await createSession({
        title: form.title.trim() || 'New Session',
        shell: form.shell.trim(),
        cwd: form.cwd.trim(),
        command: form.command.trim(),
        cols: Number(form.cols) || 160,
        rows: Number(form.rows) || 40,
        env: Object.fromEntries(
          form.envText
            .split('\n')
            .map((line) => line.trim())
            .filter(Boolean)
            .map((line) => {
              const index = line.indexOf('=');
              return index >= 0 ? [line.slice(0, index).trim(), line.slice(index + 1).trim()] : [line, ''];
            })
        ),
      });
      Message.success(`Session created: ${created.sessionId}`);
      navigate(`/sessions/${created.sessionId}`);
    } catch (createError) {
      Message.error(createError instanceof Error ? createError.message : 'Failed to create session');
    }
  }

  return (
    <main className="page">
      <section className="hero">
        <div>
          <h1>Sessions</h1>
          <p>Only the .NET session APIs and SignalR terminal hub remain in the runtime path.</p>
        </div>
        <div className="meta">
          <span className="badge">REST {env.gatewayBaseUrl || window.location.origin}</span>
          <span className="badge">Hub {env.gatewayHubUrl}</span>
        </div>
      </section>

      <section className="grid grid--sessions">
        <div className="panel">
          <div>
            <h2>Create Session</h2>
            <p>Minimal launch form mapped directly to `POST /sessions`.</p>
          </div>
          <div className="stack">
            <label className="label">
              <span>Title</span>
              <input className="input" value={form.title} onChange={(event) => setForm((current) => ({ ...current, title: event.target.value }))} />
            </label>
            <label className="label">
              <span>Shell</span>
              <input className="input" value={form.shell} onChange={(event) => setForm((current) => ({ ...current, shell: event.target.value }))} />
            </label>
            <label className="label">
              <span>Working Directory</span>
              <input className="input" value={form.cwd} onChange={(event) => setForm((current) => ({ ...current, cwd: event.target.value }))} />
            </label>
            <label className="label">
              <span>Command</span>
              <textarea className="textarea" value={form.command} onChange={(event) => setForm((current) => ({ ...current, command: event.target.value }))} />
            </label>
            <div className="row">
              <label className="label">
                <span>Cols</span>
                <input className="input" value={form.cols} onChange={(event) => setForm((current) => ({ ...current, cols: event.target.value }))} />
              </label>
              <label className="label">
                <span>Rows</span>
                <input className="input" value={form.rows} onChange={(event) => setForm((current) => ({ ...current, rows: event.target.value }))} />
              </label>
            </div>
            <label className="label">
              <span>Env (`KEY=value`, optional)</span>
              <textarea className="textarea" value={form.envText} onChange={(event) => setForm((current) => ({ ...current, envText: event.target.value }))} />
            </label>
            <div className="actions">
              <Button type="primary" onClick={() => void onCreate()}>
                Create Session
              </Button>
              <Button
                onClick={() =>
                  void pruneExited()
                    .then((result) => Message.info(`Removed ${result.removed} exited sessions`))
                    .catch((pruneError) => Message.error(pruneError instanceof Error ? pruneError.message : 'Failed to prune'))
                }
              >
                Prune Exited
              </Button>
            </div>
          </div>
        </div>

        <div className="panel">
          <div>
            <h2>Active Sessions</h2>
            <p>{isLoading ? 'Loading sessions…' : `${sessions.length} session(s)`}</p>
          </div>
          {error ? <div className="empty">{String(error)}</div> : null}
          <div className="list">
            {sessions.map((session) => (
              <div className="session-card" key={session.sessionId}>
                <div className="row" style={{ justifyContent: 'space-between' }}>
                  <div>
                    <strong>{session.title}</strong>
                    <div className="muted">{session.sessionId}</div>
                  </div>
                  <span className={`badge ${session.status === 'running' ? 'badge--running' : 'badge--exited'}`}>{session.status}</span>
                </div>
                <div className="muted">
                  {session.shell} · {session.cwd}
                </div>
                <div className="meta">
                  <span className="badge">CLI {session.cliType}</span>
                  <span className="badge">Created {formatDate(session.createdAt)}</span>
                </div>
                <div className="actions">
                  <Button type="primary" onClick={() => navigate(`/sessions/${session.sessionId}`)}>
                    Open
                  </Button>
                  <Button
                    status="danger"
                    disabled={session.status === 'running'}
                    onClick={() =>
                      void deleteSession(session.sessionId).catch((deleteError) =>
                        Message.error(deleteError instanceof Error ? deleteError.message : 'Delete failed')
                      )
                    }
                  >
                    Delete
                  </Button>
                </div>
              </div>
            ))}
            {!sessions.length && !isLoading ? <div className="empty">No sessions yet.</div> : null}
          </div>
        </div>
      </section>
    </main>
  );
}

function SessionDetailPage() {
  const { sessionId = '' } = useParams();
  const location = useLocation();
  const { sessions, deleteSession } = useSessions();
  const session = sessions.find((item) => item.sessionId === sessionId) || null;
  const terminal = useSessionTerminal(sessionId);
  const [inputValue, setInputValue] = useState('');
  const [cols, setCols] = useState('160');
  const [rows, setRows] = useState('40');

  async function sendLine() {
    if (!inputValue.trim()) {
      return;
    }
    try {
      await terminal.sendInput(`${inputValue}\r`);
      setInputValue('');
    } catch (error) {
      Message.error(error instanceof Error ? error.message : 'Input failed');
    }
  }

  return (
    <main className="page">
      <section className="hero">
        <div>
          <h1>Session Detail</h1>
          <p>{sessionId}</p>
        </div>
        <div className="meta">
          <span className="badge">Status {terminal.status}</span>
          <span className="badge">Route {location.pathname}</span>
        </div>
      </section>

      <section className="split">
        <div className="panel">
          <div>
            <h2>Terminal</h2>
            <p>SignalR events: `term.snapshot`, `term.raw`, `term.exit`, `term.resize.ack`.</p>
          </div>
          <div className="terminal" data-testid="terminal-screen">
            {terminal.screenText || terminal.rawText || terminal.snapshot?.data || 'Waiting for terminal output…'}
          </div>
          <div className="row">
            <label className="label">
              <span>Send Input</span>
              <input
                className="input"
                value={inputValue}
                onChange={(event) => setInputValue(event.target.value)}
                onKeyDown={(event) => {
                  if (event.key === 'Enter') {
                    event.preventDefault();
                    void sendLine();
                  }
                }}
              />
            </label>
          </div>
          <div className="actions">
            <Button type="primary" onClick={() => void sendLine()}>
              Send
            </Button>
            <Button onClick={() => void terminal.refreshSnapshot().catch((error) => Message.error(error instanceof Error ? error.message : 'Snapshot refresh failed'))}>
              Refresh Snapshot
            </Button>
            <Button status="warning" onClick={() => void terminal.terminate().catch((error) => Message.error(error instanceof Error ? error.message : 'Terminate failed'))}>
              Terminate
            </Button>
            <Button
              status="danger"
              disabled={session?.status === 'running'}
              onClick={() =>
                void deleteSession(sessionId)
                  .then(() => Message.success('Session deleted'))
                  .catch((error) => Message.error(error instanceof Error ? error.message : 'Delete failed'))
              }
            >
              Delete Session
            </Button>
          </div>
        </div>

        <div className="panel">
          <div>
            <h2>Replay / Control</h2>
            <p>REST snapshot and history remain available alongside live hub events.</p>
          </div>
          <div className="meta">
            <span className={`badge ${session?.status === 'running' ? 'badge--running' : 'badge--exited'}`}>{session?.status || 'unknown'}</span>
            <span className="badge">Last active {formatDate(session?.lastActivityAt)}</span>
            <span className="badge">Exit {terminal.lastExit?.type === 'term.exit' ? 'received' : session?.exitCode ?? '-'}</span>
          </div>
          <div className="row">
            <label className="label">
              <span>Cols</span>
              <input className="input" value={cols} onChange={(event) => setCols(event.target.value)} />
            </label>
            <label className="label">
              <span>Rows</span>
              <input className="input" value={rows} onChange={(event) => setRows(event.target.value)} />
            </label>
          </div>
          <div className="actions">
            <Button onClick={() => void terminal.resize(Number(cols) || 160, Number(rows) || 40).catch((error) => Message.error(error instanceof Error ? error.message : 'Resize failed'))}>
              Resize
            </Button>
            <Button onClick={() => void terminal.loadHistory(terminal.history?.nextBeforeSeq || undefined).catch((error) => Message.error(error instanceof Error ? error.message : 'History load failed'))}>
              Load History
            </Button>
          </div>
          <div className="stack">
            <div className="session-card">
              <strong>Snapshot</strong>
              <div className="muted">Tail seq: {terminal.snapshot?.tailSeq ?? '-'}</div>
              <div className="muted">Bytes: {terminal.snapshot?.bytes ?? '-'}</div>
            </div>
            <div className="session-card">
              <strong>Resize Ack</strong>
              <div className="muted">
                {terminal.lastResizeAck
                  ? `${terminal.lastResizeAck.accepted ? 'accepted' : 'rejected'} (${terminal.lastResizeAck.size?.cols}x${terminal.lastResizeAck.size?.rows})`
                  : 'No resize ack yet'}
              </div>
            </div>
            <div className="session-card">
              <strong>History</strong>
              <div className="muted">Chunks: {terminal.history?.chunks.length ?? 0}</div>
              <div className="terminal">{terminal.history?.chunks.map((item) => item.data).join('') || 'No history loaded.'}</div>
            </div>
          </div>
        </div>
      </section>
    </main>
  );
}

function TemplateEditor({
  editingTemplate,
  onSubmit,
}: {
  editingTemplate: CliTemplateRecord | null;
  onSubmit: (record: {
    templateId?: string;
    name: string;
    cliType: string;
    executable: string;
    baseArgs: string;
    defaultCwd: string;
    defaultEnv: string;
    description: string;
    icon: string;
    color: string;
  }) => Promise<void>;
}) {
  const [form, setForm] = useState(() => ({
    templateId: editingTemplate?.templateId || '',
    name: editingTemplate?.name || '',
    cliType: editingTemplate?.cliType || 'custom',
    executable: editingTemplate?.executable || '',
    baseArgs: editingTemplate?.baseArgs.join('\n') || '',
    defaultCwd: editingTemplate?.defaultCwd || '/tmp',
    defaultEnv: stringifyKeyValue(editingTemplate?.defaultEnv || {}),
    description: editingTemplate?.description || '',
    icon: editingTemplate?.icon || '',
    color: editingTemplate?.color || '#14b8a6',
  }));

  return (
    <div className="stack">
      <div className="row">
        <label className="label">
          <span>Template ID</span>
          <input className="input" disabled={Boolean(editingTemplate)} value={form.templateId} onChange={(event) => setForm((current) => ({ ...current, templateId: event.target.value }))} />
        </label>
        <label className="label">
          <span>Name</span>
          <input className="input" value={form.name} onChange={(event) => setForm((current) => ({ ...current, name: event.target.value }))} />
        </label>
      </div>
      <div className="row">
        <label className="label">
          <span>CLI Type</span>
          <select className="select" value={form.cliType} onChange={(event) => setForm((current) => ({ ...current, cliType: event.target.value }))}>
            <option value="custom">custom</option>
            <option value="bash">bash</option>
            <option value="codex">codex</option>
          </select>
        </label>
        <label className="label">
          <span>Executable</span>
          <input className="input" value={form.executable} onChange={(event) => setForm((current) => ({ ...current, executable: event.target.value }))} />
        </label>
      </div>
      <label className="label">
        <span>Base Args (one per line)</span>
        <textarea className="textarea" value={form.baseArgs} onChange={(event) => setForm((current) => ({ ...current, baseArgs: event.target.value }))} />
      </label>
      <label className="label">
        <span>Default Cwd</span>
        <input className="input" value={form.defaultCwd} onChange={(event) => setForm((current) => ({ ...current, defaultCwd: event.target.value }))} />
      </label>
      <label className="label">
        <span>Default Env (`KEY=value`)</span>
        <textarea className="textarea" value={form.defaultEnv} onChange={(event) => setForm((current) => ({ ...current, defaultEnv: event.target.value }))} />
      </label>
      <label className="label">
        <span>Description</span>
        <textarea className="textarea" value={form.description} onChange={(event) => setForm((current) => ({ ...current, description: event.target.value }))} />
      </label>
      <div className="row">
        <label className="label">
          <span>Icon</span>
          <input className="input" value={form.icon} onChange={(event) => setForm((current) => ({ ...current, icon: event.target.value }))} />
        </label>
        <label className="label">
          <span>Color</span>
          <input className="input" value={form.color} onChange={(event) => setForm((current) => ({ ...current, color: event.target.value }))} />
        </label>
      </div>
      <div className="actions">
        <Button type="primary" onClick={() => void onSubmit(form)}>
          {editingTemplate ? 'Update Template' : 'Create Template'}
        </Button>
      </div>
    </div>
  );
}

function McpEditor({ record, onSave }: { record?: McpServerDraft; onSave: (record: Omit<McpServerDraft, 'id'> & { id?: string }) => void }) {
  const [form, setForm] = useState({
    id: record?.id || '',
    name: record?.name || '',
    command: record?.command || '',
    args: record?.args.join('\n') || '',
    env: stringifyKeyValue(record?.env || {}),
    enabled: record?.enabled ?? true,
  });

  return (
    <div className="stack">
      <label className="label">
        <span>Name</span>
        <input className="input" value={form.name} onChange={(event) => setForm((current) => ({ ...current, name: event.target.value }))} />
      </label>
      <label className="label">
        <span>Command</span>
        <input className="input" value={form.command} onChange={(event) => setForm((current) => ({ ...current, command: event.target.value }))} />
      </label>
      <label className="label">
        <span>Args (one per line)</span>
        <textarea className="textarea" value={form.args} onChange={(event) => setForm((current) => ({ ...current, args: event.target.value }))} />
      </label>
      <label className="label">
        <span>Env (`KEY=value`)</span>
        <textarea className="textarea" value={form.env} onChange={(event) => setForm((current) => ({ ...current, env: event.target.value }))} />
      </label>
      <label className="row" style={{ alignItems: 'center' }}>
        <input checked={form.enabled} type="checkbox" onChange={(event) => setForm((current) => ({ ...current, enabled: event.target.checked }))} />
        <span>Enabled</span>
      </label>
      <div className="actions">
        <Button
          type="primary"
          onClick={() =>
            onSave({
              id: form.id || undefined,
              name: form.name.trim(),
              command: form.command.trim(),
              args: form.args.split('\n').map((item) => item.trim()).filter(Boolean),
              env: Object.fromEntries(
                form.env
                  .split('\n')
                  .map((line) => line.trim())
                  .filter(Boolean)
                  .map((line) => {
                    const index = line.indexOf('=');
                    return index >= 0 ? [line.slice(0, index).trim(), line.slice(index + 1).trim()] : [line, ''];
                  })
              ),
              enabled: form.enabled,
            })
          }
        >
          Save MCP Server
        </Button>
      </div>
    </div>
  );
}

function SettingsCliPage() {
  const templates = useCliTemplates();
  const [selectedTemplate, setSelectedTemplate] = useState<CliTemplateRecord | null>(null);
  const [selectedProcessId, setSelectedProcessId] = useState('');
  const processes = useCliProcesses(selectedProcessId);
  const mcp = useMcpConfig();
  const [launchForm, setLaunchForm] = useState({
    templateId: '',
    cwdOverride: '',
    extraArgs: '',
    envOverrides: '',
    label: '',
    timeoutMs: '1000',
  });

  const currentTemplate = useMemo(
    () => templates.templates.find((item) => item.templateId === launchForm.templateId) || null,
    [launchForm.templateId, templates.templates]
  );

  async function saveTemplate(form: {
    templateId?: string;
    name: string;
    cliType: string;
    executable: string;
    baseArgs: string;
    defaultCwd: string;
    defaultEnv: string;
    description: string;
    icon: string;
    color: string;
  }) {
    try {
      const input = toTemplateInput(form);
      if (selectedTemplate) {
        const { templateId: _ignored, ...updates } = input;
        await templates.updateTemplate(selectedTemplate.templateId, updates);
        Message.success(`Template updated: ${selectedTemplate.templateId}`);
      } else {
        await templates.createTemplate(input);
        Message.success(`Template created: ${input.templateId}`);
      }
      setSelectedTemplate(null);
    } catch (error) {
      Message.error(error instanceof Error ? error.message : 'Template save failed');
    }
  }

  async function startProcess() {
    try {
      const created = await processes.startProcess({
        templateId: launchForm.templateId,
        cwdOverride: launchForm.cwdOverride || undefined,
        extraArgs: launchForm.extraArgs.split('\n').map((item) => item.trim()).filter(Boolean),
        envOverrides: Object.fromEntries(
          launchForm.envOverrides
            .split('\n')
            .map((line) => line.trim())
            .filter(Boolean)
            .map((line) => {
              const index = line.indexOf('=');
              return index >= 0 ? [line.slice(0, index).trim(), line.slice(index + 1).trim()] : [line, ''];
            })
        ),
        label: launchForm.label || undefined,
        timeoutMs: Number(launchForm.timeoutMs) || undefined,
      });
      setSelectedProcessId(created.processId);
      Message.success(`Process started: ${created.processId}`);
    } catch (error) {
      Message.error(error instanceof Error ? error.message : 'Process start failed');
    }
  }

  return (
    <main className="page">
      <section className="hero">
        <div>
          <h1>CLI / MCP</h1>
          <p>CLI templates and process execution are backed by the gateway. MCP stays local to the browser for now.</p>
        </div>
        <div className="meta">
          <span className="badge">Node {env.defaultNodeId}</span>
          <span className="badge">MCP UI {env.enableMcpUi ? 'enabled' : 'disabled'}</span>
        </div>
      </section>

      <section className="grid grid--settings">
        <div className="panel">
          <div>
            <h2>CLI Templates</h2>
            <p>CRUD mapped to `/api/nodes/{env.defaultNodeId}/cli/templates`.</p>
          </div>
          <TemplateEditor key={selectedTemplate?.templateId || 'new'} editingTemplate={selectedTemplate} onSubmit={saveTemplate} />
          <div className="list">
            {templates.templates.map((template) => (
              <div className="session-card" key={template.templateId}>
                <div className="row" style={{ justifyContent: 'space-between' }}>
                  <div>
                    <strong>{template.name}</strong>
                    <div className="muted">{template.templateId}</div>
                  </div>
                  <span className="badge">{template.cliType}</span>
                </div>
                <div className="muted">{template.executable}</div>
                <div className="actions">
                  <Button onClick={() => setSelectedTemplate(template)}>Edit</Button>
                  <Button onClick={() => setLaunchForm((current) => ({ ...current, templateId: template.templateId }))}>Use For Process</Button>
                  <Button
                    status="danger"
                    disabled={template.isBuiltin}
                    onClick={() =>
                      void templates
                        .deleteTemplate(template.templateId)
                        .then(() => Message.success(`Deleted ${template.templateId}`))
                        .catch((error) => Message.error(error instanceof Error ? error.message : 'Delete failed'))
                    }
                  >
                    Delete
                  </Button>
                </div>
              </div>
            ))}
          </div>
        </div>

        <div className="panel">
          <div>
            <h2>CLI Processes</h2>
            <p>Start, inspect, stop, wait, and remove managed gateway processes.</p>
          </div>
          <div className="stack">
            <label className="label">
              <span>Template</span>
              <select className="select" value={launchForm.templateId} onChange={(event) => setLaunchForm((current) => ({ ...current, templateId: event.target.value }))}>
                <option value="">Select template</option>
                {templates.templates.map((template) => (
                  <option key={template.templateId} value={template.templateId}>
                    {template.name}
                  </option>
                ))}
              </select>
            </label>
            <label className="label">
              <span>Cwd Override</span>
              <input className="input" value={launchForm.cwdOverride} onChange={(event) => setLaunchForm((current) => ({ ...current, cwdOverride: event.target.value }))} />
            </label>
            <label className="label">
              <span>Extra Args</span>
              <textarea className="textarea" value={launchForm.extraArgs} onChange={(event) => setLaunchForm((current) => ({ ...current, extraArgs: event.target.value }))} />
            </label>
            <label className="label">
              <span>Env Overrides</span>
              <textarea className="textarea" value={launchForm.envOverrides} onChange={(event) => setLaunchForm((current) => ({ ...current, envOverrides: event.target.value }))} />
            </label>
            <div className="row">
              <label className="label">
                <span>Label</span>
                <input className="input" value={launchForm.label} onChange={(event) => setLaunchForm((current) => ({ ...current, label: event.target.value }))} />
              </label>
              <label className="label">
                <span>Timeout ms</span>
                <input className="input" value={launchForm.timeoutMs} onChange={(event) => setLaunchForm((current) => ({ ...current, timeoutMs: event.target.value }))} />
              </label>
            </div>
            <div className="actions">
              <Button type="primary" disabled={!launchForm.templateId} onClick={() => void startProcess()}>
                Start Process
              </Button>
              <Button onClick={() => void processes.refresh().catch(() => undefined)}>Refresh</Button>
            </div>
          </div>
          {currentTemplate ? (
            <div className="session-card">
              <strong>Launch Template</strong>
              <div className="muted">{currentTemplate.executable}</div>
            </div>
          ) : null}
          <div className="list">
            {processes.processes.map((process) => (
              <div className={`session-card${process.processId === selectedProcessId ? ' session-card--active' : ''}`} key={process.processId}>
                <div className="row" style={{ justifyContent: 'space-between' }}>
                  <div>
                    <strong>{process.label || process.templateName || process.processId}</strong>
                    <div className="muted">{process.processId}</div>
                  </div>
                  <span className="badge">{process.status}</span>
                </div>
                <div className="muted">{process.command}</div>
                <div className="actions">
                  <Button onClick={() => setSelectedProcessId(process.processId)}>Inspect</Button>
                  <Button onClick={() => void processes.waitProcess(process.processId).catch((error) => Message.error(error instanceof Error ? error.message : 'Wait failed'))}>
                    Wait
                  </Button>
                  <Button status="warning" onClick={() => void processes.stopProcess(process.processId).catch((error) => Message.error(error instanceof Error ? error.message : 'Stop failed'))}>
                    Stop
                  </Button>
                  <Button
                    status="danger"
                    onClick={() =>
                      void processes
                        .deleteProcess(process.processId)
                        .then(() => {
                          if (selectedProcessId === process.processId) {
                            setSelectedProcessId('');
                          }
                        })
                        .catch((error) => Message.error(error instanceof Error ? error.message : 'Delete failed'))
                    }
                  >
                    Delete
                  </Button>
                </div>
              </div>
            ))}
          </div>
          <div className="session-card">
            <strong>Selected Process</strong>
            <div className="muted">{processes.selectedProcess?.processId || 'None selected'}</div>
            <div className="terminal">{processes.outputItems.map((item) => `[${item.outputType}] ${item.content}`).join('\n') || 'No process output loaded.'}</div>
          </div>
        </div>
      </section>

      {env.enableMcpUi ? (
        <section className="panel">
          <div>
            <h2>MCP Configuration</h2>
            <p>Browser-local only. No legacy runtime bridge or backend execution path is preserved.</p>
          </div>
          <div className="grid grid--settings">
            <div className="panel panel--tight">
              <McpEditor
                onSave={(record) => {
                  mcp.upsertServer(record);
                  Message.success(`Saved MCP server: ${record.name}`);
                }}
              />
            </div>
            <div className="panel panel--tight">
              <div className="list">
                {mcp.servers.map((server) => (
                  <div className="session-card" key={server.id}>
                    <div className="row" style={{ justifyContent: 'space-between' }}>
                      <div>
                        <strong>{server.name}</strong>
                        <div className="muted">{server.command}</div>
                      </div>
                      <span className="badge">{server.enabled ? 'enabled' : 'disabled'}</span>
                    </div>
                    <div className="terminal">{server.args.join('\n') || 'No args'}</div>
                    <div className="actions">
                      <Button onClick={() => mcp.upsertServer({ ...server, id: undefined })}>Duplicate</Button>
                      <Button status="danger" onClick={() => mcp.deleteServer(server.id)}>
                        Delete
                      </Button>
                    </div>
                  </div>
                ))}
                {!mcp.servers.length ? <div className="empty">No MCP servers saved in local storage.</div> : null}
              </div>
            </div>
          </div>
        </section>
      ) : null}
    </main>
  );
}

function RedirectToSessions() {
  return <Navigate replace to="/sessions" />;
}

export function AppRoutes() {
  return (
    <Routes>
      <Route path="/" element={<RedirectToSessions />} />
      <Route path="/sessions" element={<SessionsPage />} />
      <Route path="/sessions/:sessionId" element={<SessionDetailPage />} />
      <Route path="/settings/cli" element={<SettingsCliPage />} />
      <Route path="/conversation/:id" element={<RedirectToSessions />} />
      <Route path="/settings/ext/:tabId" element={<RedirectToSessions />} />
      {legacyRoutes.map((path) => (
        <Route element={<RedirectToSessions />} key={path} path={path} />
      ))}
      <Route path="*" element={<RedirectToSessions />} />
    </Routes>
  );
}

export default function App() {
  return (
    <div className="app-shell">
      <TopBar />
      <AppRoutes />
    </div>
  );
}
