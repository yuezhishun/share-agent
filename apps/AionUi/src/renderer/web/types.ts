export type Dictionary = Record<string, string>;

export interface GatewaySessionSummary {
  sessionId: string;
  taskId: string;
  cliType: string;
  mode: string;
  profileId?: string | null;
  title: string;
  shell: string;
  cwd: string;
  args: string[];
  pid: number;
  status: string;
  createdAt: string;
  lastActivityAt: string;
  exitCode?: number | null;
  outputBytes: number;
  outputTruncated: boolean;
  maxOutputBufferBytes: number;
  backend: string;
}

export interface GatewaySessionDetail {
  sessionId: string;
  status: string;
  exitCode?: number | null;
  data: string;
  bytes: number;
  totalBytes: number;
  truncated: boolean;
  maxOutputBufferBytes: number;
  headSeq: number;
  tailSeq: number;
}

export interface GatewaySessionHistory {
  sessionId: string;
  chunks: Array<{
    data: string;
    seqStart: number;
    seqEnd: number;
  }>;
  hasMore: boolean;
  nextBeforeSeq?: number | null;
  truncated: boolean;
}

export interface GatewayTerminalSnapshotRow {
  y: number;
  segs: Array<[string, number]>;
}

export interface GatewayTerminalEvent {
  v?: number;
  type: string;
  instance_id?: string;
  node_id?: string;
  node_name?: string;
  seq?: number;
  ts?: number;
  req_id?: string;
  accepted?: boolean;
  reason?: string | null;
  replay?: boolean;
  data?: string;
  to_seq?: number;
  render_epoch?: number;
  owner_connection_id?: string;
  size?: {
    cols: number;
    rows: number;
  };
  cursor?: {
    x: number;
    y: number;
    visible: boolean;
  };
  rows?: GatewayTerminalSnapshotRow[];
}

export interface CliTemplateRecord {
  templateId: string;
  name: string;
  cliType: string;
  executable: string;
  baseArgs: string[];
  defaultCwd: string;
  defaultEnv: Dictionary;
  description: string;
  icon: string;
  color: string;
  isBuiltin: boolean;
  createdAt: string;
  updatedAt: string;
}

export interface CliProcessRecord {
  processId: string;
  status: string;
  startTime?: string;
  endTime?: string | null;
  durationMs: number;
  command: string;
  templateId?: string;
  templateName?: string;
  cliType?: string;
  label?: string;
  outputCount: number;
  metadata: Record<string, unknown>;
  result?: {
    exitCode?: number | null;
    standardOutput?: string;
    standardError?: string;
  } | null;
}

export interface CliProcessOutputItem {
  timestamp: string;
  processId: string;
  outputType: string;
  content: string;
}

export interface McpServerDraft {
  id: string;
  name: string;
  command: string;
  args: string[];
  env: Dictionary;
  enabled: boolean;
}
