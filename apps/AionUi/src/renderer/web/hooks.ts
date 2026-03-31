import { useEffect, useMemo, useRef, useState } from 'react';
import useSWR from 'swr';
import { gatewayApi, type CliProcessStartInput, type CliTemplateInput, type CreateSessionInput } from './api';
import { env } from './env';
import { createTerminalHubClient } from './terminal-hub';
import type {
  CliProcessOutputItem,
  CliProcessRecord,
  CliTemplateRecord,
  Dictionary,
  GatewaySessionHistory,
  GatewaySessionSummary,
  GatewayTerminalEvent,
  McpServerDraft,
} from './types';

const mcpStorageKey = 'aionui.web.mcp.servers.v1';

function normalizeArray(value: string) {
  return value
    .split('\n')
    .map((item) => item.trim())
    .filter(Boolean);
}

export function parseKeyValueText(value: string): Dictionary {
  return Object.fromEntries(
    value
      .split('\n')
      .map((line) => line.trim())
      .filter(Boolean)
      .map((line) => {
        const index = line.indexOf('=');
        if (index < 0) {
          return [line, ''];
        }
        return [line.slice(0, index).trim(), line.slice(index + 1).trim()];
      })
      .filter(([key]) => key.length > 0)
  );
}

export function stringifyKeyValue(value: Dictionary) {
  return Object.entries(value)
    .map(([key, item]) => `${key}=${item}`)
    .join('\n');
}

function readMcpServers(): McpServerDraft[] {
  if (typeof window === 'undefined') {
    return [];
  }

  try {
    const raw = window.localStorage.getItem(mcpStorageKey);
    if (!raw) {
      return [];
    }
    const parsed = JSON.parse(raw);
    if (!Array.isArray(parsed)) {
      return [];
    }
    return parsed.map((item) => ({
      id: String(item?.id || crypto.randomUUID()),
      name: String(item?.name || ''),
      command: String(item?.command || ''),
      args: Array.isArray(item?.args) ? item.args.map((arg: unknown) => String(arg)) : [],
      env: typeof item?.env === 'object' && item?.env ? Object.fromEntries(Object.entries(item.env).map(([k, v]) => [k, String(v)])) : {},
      enabled: item?.enabled !== false,
    }));
  } catch {
    return [];
  }
}

function writeMcpServers(items: McpServerDraft[]) {
  if (typeof window === 'undefined') {
    return;
  }
  window.localStorage.setItem(mcpStorageKey, JSON.stringify(items));
}

export function useSessions() {
  const swr = useSWR<GatewaySessionSummary[]>('sessions', () => gatewayApi.listSessions());

  return {
    ...swr,
    sessions: swr.data || [],
    async createSession(input: CreateSessionInput) {
      const created = await gatewayApi.createSession(input);
      await swr.mutate();
      return created;
    },
    async deleteSession(sessionId: string) {
      await gatewayApi.deleteSession(sessionId);
      await swr.mutate();
    },
    async pruneExited() {
      const result = await gatewayApi.pruneExitedSessions();
      await swr.mutate();
      return result;
    },
  };
}

export function useCliTemplates() {
  const swr = useSWR<{ items: CliTemplateRecord[] }>(['cli-templates', env.defaultNodeId], ([, nodeId]) =>
    gatewayApi.listCliTemplates(nodeId)
  );

  return {
    templates: swr.data?.items || [],
    error: swr.error,
    isLoading: swr.isLoading,
    refresh: () => swr.mutate(),
    async createTemplate(input: CliTemplateInput) {
      await gatewayApi.createCliTemplate(env.defaultNodeId, input);
      await swr.mutate();
    },
    async updateTemplate(templateId: string, input: Omit<CliTemplateInput, 'templateId'>) {
      await gatewayApi.updateCliTemplate(env.defaultNodeId, templateId, input);
      await swr.mutate();
    },
    async deleteTemplate(templateId: string) {
      await gatewayApi.deleteCliTemplate(env.defaultNodeId, templateId);
      await swr.mutate();
    },
  };
}

export function useCliProcesses(selectedProcessId: string) {
  const list = useSWR<{ items: CliProcessRecord[] }>(['cli-processes', env.defaultNodeId], ([, nodeId]) =>
    gatewayApi.listCliProcesses(nodeId)
  );
  const detail = useSWR<CliProcessRecord | null>(
    selectedProcessId ? ['cli-process', env.defaultNodeId, selectedProcessId] : null,
    ([, nodeId, processId]) => gatewayApi.getCliProcess(nodeId, processId)
  );
  const output = useSWR<{ items: CliProcessOutputItem[] }>(
    selectedProcessId ? ['cli-process-output', env.defaultNodeId, selectedProcessId] : null,
    ([, nodeId, processId]) => gatewayApi.getCliProcessOutput(nodeId, processId)
  );

  async function refreshAll() {
    await Promise.all([list.mutate(), detail.mutate(), output.mutate()]);
  }

  return {
    processes: list.data?.items || [],
    selectedProcess: detail.data || null,
    outputItems: output.data?.items || [],
    error: list.error || detail.error || output.error,
    isLoading: list.isLoading,
    async startProcess(input: CliProcessStartInput) {
      const created = await gatewayApi.startCliProcess(env.defaultNodeId, input);
      await refreshAll();
      return created;
    },
    async stopProcess(processId: string, force = false) {
      await gatewayApi.stopCliProcess(env.defaultNodeId, processId, force);
      await refreshAll();
    },
    async waitProcess(processId: string, timeoutMs = 1000) {
      const result = await gatewayApi.waitCliProcess(env.defaultNodeId, processId, timeoutMs);
      await refreshAll();
      return result;
    },
    async deleteProcess(processId: string) {
      await gatewayApi.deleteCliProcess(env.defaultNodeId, processId);
      await refreshAll();
    },
    async refresh() {
      await refreshAll();
    },
  };
}

export function useMcpConfig() {
  const [servers, setServers] = useState<McpServerDraft[]>(() => readMcpServers());

  useEffect(() => {
    writeMcpServers(servers);
  }, [servers]);

  return {
    servers,
    upsertServer(input: Omit<McpServerDraft, 'id'> & { id?: string }) {
      setServers((current) => {
        const id = input.id || crypto.randomUUID();
        const nextRecord: McpServerDraft = { ...input, id };
        const existingIndex = current.findIndex((item) => item.id === id);
        if (existingIndex < 0) {
          return [nextRecord, ...current];
        }
        return current.map((item) => (item.id === id ? nextRecord : item));
      });
    },
    deleteServer(id: string) {
      setServers((current) => current.filter((item) => item.id !== id));
    },
  };
}

function rowsToText(event: GatewayTerminalEvent) {
  if (!event.rows?.length) {
    return '';
  }
  return event.rows
    .map((row) => (Array.isArray(row.segs) ? row.segs.map((segment) => segment[0] || '').join('') : ''))
    .join('\n');
}

export function useSessionTerminal(sessionId?: string) {
  const [snapshot, setSnapshot] = useState<Awaited<ReturnType<typeof gatewayApi.getSessionSnapshot>> | null>(null);
  const [history, setHistory] = useState<GatewaySessionHistory | null>(null);
  const [events, setEvents] = useState<GatewayTerminalEvent[]>([]);
  const [screenText, setScreenText] = useState('');
  const [rawText, setRawText] = useState('');
  const [status, setStatus] = useState('disconnected');
  const [lastResizeAck, setLastResizeAck] = useState<GatewayTerminalEvent | null>(null);
  const [lastExit, setLastExit] = useState<GatewayTerminalEvent | null>(null);
  const lastSeqRef = useRef<number>(0);

  const client = useMemo(
    () =>
      createTerminalHubClient((event) => {
        setEvents((current) => [...current.slice(-99), event]);
        if (event.type === 'term.snapshot') {
          setScreenText(rowsToText(event) || event.data || '');
          if (typeof event.seq === 'number') {
            lastSeqRef.current = event.seq;
          }
        }
        if (event.type === 'term.raw') {
          setRawText((current) => `${current}${event.data || ''}`);
          if (typeof event.to_seq === 'number') {
            lastSeqRef.current = event.to_seq;
          }
          if (typeof event.seq === 'number') {
            lastSeqRef.current = event.seq;
          }
        }
        if (event.type === 'term.resize.ack') {
          setLastResizeAck(event);
        }
        if (event.type === 'term.exit') {
          setLastExit(event);
        }
      }),
    []
  );

  useEffect(() => {
    if (!sessionId) {
      return;
    }

    let disposed = false;

    async function connect() {
      setStatus('connecting');
      try {
        const [snapshotData, historyData] = await Promise.all([
          gatewayApi.getSessionSnapshot(sessionId),
          gatewayApi.getSessionHistory(sessionId),
        ]);
        if (disposed) {
          return;
        }
        setSnapshot(snapshotData);
        setHistory(historyData);
        setRawText(snapshotData.data || '');
        await client.connect();
        await client.join(sessionId);
        await client.requestScreenSync(sessionId);
        await client.requestRawSync(sessionId, lastSeqRef.current);
        if (!disposed) {
          setStatus('connected');
        }
      } catch (error) {
        if (!disposed) {
          setStatus(error instanceof Error ? error.message : 'failed');
        }
      }
    }

    void connect();

    return () => {
      disposed = true;
      void client.leave(sessionId).catch(() => undefined);
      void client.disconnect().catch(() => undefined);
    };
  }, [client, sessionId]);

  return {
    snapshot,
    history,
    events,
    screenText,
    rawText,
    status,
    lastResizeAck,
    lastExit,
    async refreshSnapshot() {
      if (!sessionId) return;
      const next = await gatewayApi.getSessionSnapshot(sessionId);
      setSnapshot(next);
      setRawText(next.data || '');
      await client.requestScreenSync(sessionId);
    },
    async loadHistory(beforeSeq?: number) {
      if (!sessionId) return;
      const next = await gatewayApi.getSessionHistory(sessionId, beforeSeq);
      setHistory(next);
      return next;
    },
    async sendInput(value: string) {
      if (!sessionId || !value) return;
      await client.sendInput(sessionId, value);
    },
    async resize(cols: number, rows: number) {
      if (!sessionId) return;
      await client.requestResize(sessionId, cols, rows);
    },
    async terminate() {
      if (!sessionId) return;
      await gatewayApi.terminateSession(sessionId);
    },
  };
}

export function toTemplateInput(form: {
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
}): CliTemplateInput {
  return {
    templateId: form.templateId || undefined,
    name: form.name.trim(),
    cliType: form.cliType.trim(),
    executable: form.executable.trim(),
    baseArgs: normalizeArray(form.baseArgs),
    defaultCwd: form.defaultCwd.trim(),
    defaultEnv: parseKeyValueText(form.defaultEnv),
    description: form.description.trim(),
    icon: form.icon.trim(),
    color: form.color.trim(),
  };
}
