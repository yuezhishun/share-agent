import { env } from './env';
import type {
  CliProcessOutputItem,
  CliProcessRecord,
  CliTemplateRecord,
  Dictionary,
  GatewaySessionDetail,
  GatewaySessionHistory,
  GatewaySessionSummary,
} from './types';

type HttpMethod = 'GET' | 'POST' | 'PUT' | 'DELETE';

async function parseResponse<T>(response: Response): Promise<T> {
  if (!response.ok) {
    let message = `${response.status} ${response.statusText}`;
    try {
      const data = await response.json();
      message = String(data?.error || message);
    } catch {
      const text = await response.text();
      if (text) {
        message = text;
      }
    }
    throw new Error(message);
  }

  if (response.status === 204) {
    return undefined as T;
  }

  return (await response.json()) as T;
}

async function request<T>(method: HttpMethod, path: string, body?: unknown): Promise<T> {
  const url = `${env.gatewayBaseUrl}${path}`;
  const response = await fetch(url, {
    method,
    headers: body === undefined ? undefined : { 'Content-Type': 'application/json' },
    body: body === undefined ? undefined : JSON.stringify(body),
  });

  return parseResponse<T>(response);
}

export interface CreateSessionInput {
  title: string;
  shell: string;
  cwd: string;
  command: string;
  cols: number;
  rows: number;
  env: Dictionary;
}

export interface CliTemplateInput {
  templateId?: string;
  name: string;
  cliType: string;
  executable: string;
  baseArgs: string[];
  defaultCwd: string;
  defaultEnv: Dictionary;
  description: string;
  icon: string;
  color: string;
}

export interface CliProcessStartInput {
  templateId: string;
  cwdOverride?: string;
  envOverrides?: Dictionary;
  extraArgs?: string[];
  label?: string;
  timeoutMs?: number;
}

export const gatewayApi = {
  listSessions() {
    return request<GatewaySessionSummary[]>('GET', '/sessions');
  },
  createSession(input: CreateSessionInput) {
    return request<GatewaySessionSummary & { writeToken: string }>('POST', '/sessions', {
      title: input.title,
      shell: input.shell,
      cwd: input.cwd,
      command: input.command,
      cols: input.cols,
      rows: input.rows,
      env: input.env,
    });
  },
  deleteSession(sessionId: string) {
    return request<{ ok: boolean }>('DELETE', `/sessions/${sessionId}`);
  },
  terminateSession(sessionId: string) {
    return request<{ ok: boolean }>('POST', `/sessions/${sessionId}/terminate`, { signal: 'SIGTERM' });
  },
  pruneExitedSessions() {
    return request<{ ok: boolean; removed: number }>('POST', '/sessions/prune-exited');
  },
  getSessionSnapshot(sessionId: string) {
    return request<GatewaySessionDetail>('GET', `/sessions/${sessionId}/snapshot`);
  },
  getSessionHistory(sessionId: string, beforeSeq?: number) {
    const search = beforeSeq ? `?beforeSeq=${beforeSeq}` : '';
    return request<GatewaySessionHistory>('GET', `/sessions/${sessionId}/history${search}`);
  },
  listCliTemplates(nodeId: string) {
    return request<{ items: CliTemplateRecord[] }>('GET', `/api/nodes/${nodeId}/cli/templates`);
  },
  createCliTemplate(nodeId: string, input: CliTemplateInput) {
    return request<CliTemplateRecord>('POST', `/api/nodes/${nodeId}/cli/templates`, input);
  },
  updateCliTemplate(nodeId: string, templateId: string, input: Omit<CliTemplateInput, 'templateId'>) {
    return request<CliTemplateRecord>('PUT', `/api/nodes/${nodeId}/cli/templates/${templateId}`, input);
  },
  deleteCliTemplate(nodeId: string, templateId: string) {
    return request<{ ok: boolean }>('DELETE', `/api/nodes/${nodeId}/cli/templates/${templateId}`);
  },
  listCliProcesses(nodeId: string) {
    return request<{ items: CliProcessRecord[] }>('GET', `/api/nodes/${nodeId}/cli/processes`);
  },
  startCliProcess(nodeId: string, input: CliProcessStartInput) {
    return request<{ processId: string; templateId: string; templateName: string; status: string }>(
      'POST',
      `/api/nodes/${nodeId}/cli/processes`,
      input
    );
  },
  getCliProcess(nodeId: string, processId: string) {
    return request<CliProcessRecord>('GET', `/api/nodes/${nodeId}/cli/processes/${processId}`);
  },
  getCliProcessOutput(nodeId: string, processId: string) {
    return request<{ items: CliProcessOutputItem[] }>('GET', `/api/nodes/${nodeId}/cli/processes/${processId}/output`);
  },
  waitCliProcess(nodeId: string, processId: string, timeoutMs = 1000) {
    return request<{ completed: boolean; status: string }>(
      'POST',
      `/api/nodes/${nodeId}/cli/processes/${processId}/wait?timeout_ms=${timeoutMs}`
    );
  },
  stopCliProcess(nodeId: string, processId: string, force = false) {
    return request<{ ok: boolean; status: string }>('POST', `/api/nodes/${nodeId}/cli/processes/${processId}/stop`, {
      force,
    });
  },
  deleteCliProcess(nodeId: string, processId: string) {
    return request<{ ok: boolean }>('DELETE', `/api/nodes/${nodeId}/cli/processes/${processId}`);
  },
};
