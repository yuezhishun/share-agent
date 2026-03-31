import { useEffect, useMemo, useState } from 'react';
import useSWR from 'swr';
import {
  Alert,
  Button,
  Card,
  Divider,
  Empty,
  Form,
  Input,
  List,
  Message,
  Select,
  Space,
  Spin,
  Tag,
  Typography
} from '@arco-design/web-react';

type NodeItem = {
  node_id: string;
  node_name: string;
  node_role?: string;
  node_online?: boolean;
  instance_count?: number;
};

type CliTemplate = {
  template_id: string;
  name: string;
  cli_type: string;
  executable: string;
  base_args: string[];
  default_cwd: string;
  default_env: Record<string, string>;
  description?: string;
  color?: string;
  is_builtin: boolean;
};

type CliProcess = {
  process_id: string;
  status: string;
  start_time?: string;
  end_time?: string | null;
  command?: string;
  template_id?: string;
  template_name?: string;
  cli_type?: string;
  label?: string;
  output_count?: number;
  result?: {
    standard_output?: string;
    standard_error?: string;
  } | null;
};

type CliOutputItem = {
  timestamp: string;
  process_id: string;
  output_type: string;
  content: string;
};

const apiBase = String(import.meta.env.VITE_API_BASE || '').trim();
const TextArea = Input.TextArea;

async function fetchJson<T>(path: string, init?: RequestInit): Promise<T> {
  const response = await fetch(`${apiBase}${path}`, {
    ...init,
    headers: {
      'Content-Type': 'application/json',
      ...(init?.headers || {})
    }
  });
  if (!response.ok) {
    const text = await response.text();
    throw new Error(text || `request failed: ${response.status}`);
  }
  return response.json() as Promise<T>;
}

function parseJsonMap(input: string): Record<string, string> {
  const raw = input.trim();
  if (!raw) {
    return {};
  }
  const parsed = JSON.parse(raw);
  if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
    throw new Error('环境变量必须是 JSON 对象');
  }
  return Object.fromEntries(Object.entries(parsed).map(([key, value]) => [key, String(value ?? '')]));
}

function parseJsonArray(input: string): string[] {
  const raw = input.trim();
  if (!raw) {
    return [];
  }
  const parsed = JSON.parse(raw);
  if (!Array.isArray(parsed)) {
    throw new Error('参数必须是 JSON 数组');
  }
  return parsed.map((item) => String(item ?? '')).filter(Boolean);
}

function formatTime(value?: string | null): string {
  if (!value) {
    return '-';
  }
  try {
    return new Date(value).toLocaleString('zh-CN');
  } catch {
    return value;
  }
}

const defaultTemplateForm = {
  templateId: '',
  name: '',
  cliType: 'bash',
  executable: 'bash',
  baseArgsJson: '[]',
  defaultCwd: '',
  defaultEnvJson: '{}',
  description: '',
  color: '#1ea7a4'
};

const defaultLaunchForm = {
  cwdOverride: '',
  extraArgsJson: '[]',
  envOverridesJson: '{}',
  label: '',
  timeoutMs: '300000'
};

export default function App() {
  const [messageApi, contextHolder] = Message.useMessage();
  const { data: nodesPayload, error: nodesError, isLoading: nodesLoading } = useSWR<{ items: NodeItem[] }>('/api/nodes', fetchJson, {
    revalidateOnFocus: false
  });
  const nodes = nodesPayload?.items ?? [];
  const [nodeId, setNodeId] = useState('');
  const [selectedTemplateId, setSelectedTemplateId] = useState('');
  const [selectedProcessId, setSelectedProcessId] = useState('');
  const [templateForm, setTemplateForm] = useState(defaultTemplateForm);
  const [launchForm, setLaunchForm] = useState(defaultLaunchForm);
  const [outputFilter, setOutputFilter] = useState('all');
  const [submittingTemplate, setSubmittingTemplate] = useState(false);
  const [submittingProcess, setSubmittingProcess] = useState(false);
  const [stoppingProcess, setStoppingProcess] = useState(false);

  useEffect(() => {
    if (!nodeId && nodes[0]?.node_id) {
      const preferred = nodes.find((item) => item.node_role === 'master' && item.node_online !== false) ?? nodes[0];
      setNodeId(preferred.node_id);
    }
  }, [nodeId, nodes]);

  const templatesKey = nodeId ? `/api/nodes/${encodeURIComponent(nodeId)}/cli/templates` : null;
  const processesKey = nodeId ? `/api/nodes/${encodeURIComponent(nodeId)}/cli/processes` : null;
  const processDetailKey = nodeId && selectedProcessId ? `/api/nodes/${encodeURIComponent(nodeId)}/cli/processes/${encodeURIComponent(selectedProcessId)}` : null;
  const outputKey = nodeId && selectedProcessId ? `/api/nodes/${encodeURIComponent(nodeId)}/cli/processes/${encodeURIComponent(selectedProcessId)}/output` : null;

  const { data: templatesPayload, error: templatesError, isLoading: templatesLoading, mutate: mutateTemplates } = useSWR<{ items: CliTemplate[] }>(
    templatesKey,
    fetchJson,
    { refreshInterval: 0 }
  );
  const { data: processesPayload, error: processesError, isLoading: processesLoading, mutate: mutateProcesses } = useSWR<{ items: CliProcess[] }>(
    processesKey,
    fetchJson,
    { refreshInterval: selectedProcessId ? 1500 : 3000 }
  );
  const { data: processDetail, mutate: mutateProcessDetail } = useSWR<CliProcess>(processDetailKey, fetchJson, {
    refreshInterval: selectedProcessId ? 1500 : 0
  });
  const { data: outputPayload, mutate: mutateOutput } = useSWR<{ items: CliOutputItem[] }>(outputKey, fetchJson, {
    refreshInterval: selectedProcessId ? 1500 : 0
  });

  const templates = templatesPayload?.items ?? [];
  const processes = processesPayload?.items ?? [];
  const outputItems = outputPayload?.items ?? [];

  useEffect(() => {
    if (!selectedTemplateId && templates[0]?.template_id) {
      setSelectedTemplateId(templates[0].template_id);
    }
  }, [selectedTemplateId, templates]);

  useEffect(() => {
    if (!selectedProcessId && processes[0]?.process_id) {
      setSelectedProcessId(processes[0].process_id);
    }
  }, [processes, selectedProcessId]);

  const selectedNode = nodes.find((item) => item.node_id === nodeId) ?? null;
  const selectedTemplate = templates.find((item) => item.template_id === selectedTemplateId) ?? null;

  const filteredOutput = useMemo(() => {
    if (outputFilter === 'all') {
      return outputItems;
    }
    return outputItems.filter((item) => item.output_type === outputFilter);
  }, [outputFilter, outputItems]);

  async function handleCreateOrUpdateTemplate() {
    if (!nodeId) {
      return;
    }
    setSubmittingTemplate(true);
    try {
      const payload = {
        template_id: templateForm.templateId.trim() || undefined,
        name: templateForm.name.trim(),
        cli_type: templateForm.cliType,
        executable: templateForm.executable.trim(),
        base_args: parseJsonArray(templateForm.baseArgsJson),
        default_cwd: templateForm.defaultCwd.trim(),
        default_env: parseJsonMap(templateForm.defaultEnvJson),
        description: templateForm.description.trim(),
        color: templateForm.color.trim()
      };
      if (templateForm.templateId.trim()) {
        await fetchJson(`/api/nodes/${encodeURIComponent(nodeId)}/cli/templates/${encodeURIComponent(templateForm.templateId.trim())}`, {
          method: 'PUT',
          body: JSON.stringify(payload)
        });
        messageApi.success('模板已更新');
      } else {
        await fetchJson(`/api/nodes/${encodeURIComponent(nodeId)}/cli/templates`, {
          method: 'POST',
          body: JSON.stringify(payload)
        });
        messageApi.success('模板已创建');
      }
      setTemplateForm(defaultTemplateForm);
      await mutateTemplates();
    } catch (error) {
      messageApi.error(String(error));
    } finally {
      setSubmittingTemplate(false);
    }
  }

  async function handleDeleteTemplate(templateId: string) {
    if (!nodeId) {
      return;
    }
    try {
      await fetchJson(`/api/nodes/${encodeURIComponent(nodeId)}/cli/templates/${encodeURIComponent(templateId)}`, {
        method: 'DELETE'
      });
      if (selectedTemplateId === templateId) {
        setSelectedTemplateId('');
      }
      messageApi.success('模板已删除');
      await mutateTemplates();
    } catch (error) {
      messageApi.error(String(error));
    }
  }

  async function handleStartProcess() {
    if (!nodeId || !selectedTemplateId) {
      return;
    }
    setSubmittingProcess(true);
    try {
      const payload = {
        template_id: selectedTemplateId,
        cwd_override: launchForm.cwdOverride.trim() || undefined,
        extra_args: parseJsonArray(launchForm.extraArgsJson),
        env_overrides: parseJsonMap(launchForm.envOverridesJson),
        label: launchForm.label.trim() || undefined,
        timeout_ms: launchForm.timeoutMs.trim() ? Number(launchForm.timeoutMs.trim()) : undefined
      };
      const created = await fetchJson<{ process_id: string }>(`/api/nodes/${encodeURIComponent(nodeId)}/cli/processes`, {
        method: 'POST',
        body: JSON.stringify(payload)
      });
      setSelectedProcessId(created.process_id);
      messageApi.success('进程已启动');
      await mutateProcesses();
      await mutateProcessDetail();
      await mutateOutput();
    } catch (error) {
      messageApi.error(String(error));
    } finally {
      setSubmittingProcess(false);
    }
  }

  async function handleStopProcess() {
    if (!nodeId || !selectedProcessId) {
      return;
    }
    setStoppingProcess(true);
    try {
      await fetchJson(`/api/nodes/${encodeURIComponent(nodeId)}/cli/processes/${encodeURIComponent(selectedProcessId)}/stop`, {
        method: 'POST',
        body: JSON.stringify({ force: true })
      });
      messageApi.success('已停止进程');
      await mutateProcesses();
      await mutateProcessDetail();
    } catch (error) {
      messageApi.error(String(error));
    } finally {
      setStoppingProcess(false);
    }
  }

  async function handleDeleteProcess() {
    if (!nodeId || !selectedProcessId) {
      return;
    }
    try {
      await fetchJson(`/api/nodes/${encodeURIComponent(nodeId)}/cli/processes/${encodeURIComponent(selectedProcessId)}`, {
        method: 'DELETE'
      });
      setSelectedProcessId('');
      messageApi.success('已删除进程');
      await mutateProcesses();
      await mutateProcessDetail(undefined, { revalidate: false });
      await mutateOutput(undefined, { revalidate: false });
    } catch (error) {
      messageApi.error(String(error));
    }
  }

  return (
    <div className="proc-react-shell">
      {contextHolder}
      <header className="page-header">
        <div>
          <Typography.Title heading={3} style={{ marginBottom: 4 }}>
            CLI Process Manager
          </Typography.Title>
          <Typography.Text type="secondary">React + Arco standalone console for node-scoped CLI templates and managed processes.</Typography.Text>
        </div>
        <Space>
          <Select
            placeholder="选择节点"
            loading={nodesLoading}
            value={nodeId}
            style={{ width: 260 }}
            onChange={(value) => {
              setNodeId(String(value));
              setSelectedTemplateId('');
              setSelectedProcessId('');
            }}
          >
            {nodes.map((item) => (
              <Select.Option key={item.node_id} value={item.node_id}>
                {item.node_name} ({item.node_role || 'node'})
              </Select.Option>
            ))}
          </Select>
          {selectedNode ? <Tag color={selectedNode.node_online === false ? 'red' : 'green'}>{selectedNode.node_online === false ? 'offline' : 'online'}</Tag> : null}
        </Space>
      </header>

      {(nodesError || templatesError || processesError) ? (
        <Alert
          type="error"
          content={String(nodesError || templatesError || processesError)}
          style={{ marginBottom: 16 }}
        />
      ) : null}

      <main className="proc-grid">
        <Card className="column-card" title="CLI Templates">
          <Spin loading={templatesLoading}>
            <Form layout="vertical">
              <Form.Item label="模板 ID">
                <Input
                  placeholder="留空则自动生成"
                  value={templateForm.templateId}
                  onChange={(value) => setTemplateForm((prev) => ({ ...prev, templateId: value }))}
                />
              </Form.Item>
              <Form.Item label="名称">
                <Input value={templateForm.name} onChange={(value) => setTemplateForm((prev) => ({ ...prev, name: value }))} />
              </Form.Item>
              <Form.Item label="CLI 类型">
                <Select value={templateForm.cliType} onChange={(value) => setTemplateForm((prev) => ({ ...prev, cliType: String(value) }))}>
                  <Select.Option value="bash">bash</Select.Option>
                  <Select.Option value="codex">codex</Select.Option>
                  <Select.Option value="custom">custom</Select.Option>
                </Select>
              </Form.Item>
              <Form.Item label="Executable">
                <Input value={templateForm.executable} onChange={(value) => setTemplateForm((prev) => ({ ...prev, executable: value }))} />
              </Form.Item>
              <Form.Item label="Base Args JSON">
                <TextArea value={templateForm.baseArgsJson} onChange={(value) => setTemplateForm((prev) => ({ ...prev, baseArgsJson: value }))} autoSize={{ minRows: 2 }} />
              </Form.Item>
              <Form.Item label="默认工作目录">
                <Input value={templateForm.defaultCwd} onChange={(value) => setTemplateForm((prev) => ({ ...prev, defaultCwd: value }))} />
              </Form.Item>
              <Form.Item label="默认环境变量 JSON">
                <TextArea value={templateForm.defaultEnvJson} onChange={(value) => setTemplateForm((prev) => ({ ...prev, defaultEnvJson: value }))} autoSize={{ minRows: 2 }} />
              </Form.Item>
              <Form.Item label="描述">
                <TextArea value={templateForm.description} onChange={(value) => setTemplateForm((prev) => ({ ...prev, description: value }))} autoSize={{ minRows: 2 }} />
              </Form.Item>
              <Space>
                <Button type="primary" loading={submittingTemplate} onClick={handleCreateOrUpdateTemplate}>
                  保存模板
                </Button>
                <Button onClick={() => setTemplateForm(defaultTemplateForm)}>重置</Button>
              </Space>
            </Form>
            <Divider />
            <List
              dataSource={templates}
              render={(item) => (
                <List.Item
                  key={item.template_id}
                  actions={[
                    <Button key="use" size="mini" onClick={() => setSelectedTemplateId(item.template_id)}>
                      使用
                    </Button>,
                    <Button
                      key="edit"
                      size="mini"
                      onClick={() =>
                        setTemplateForm({
                          templateId: item.is_builtin ? '' : item.template_id,
                          name: item.name,
                          cliType: item.cli_type,
                          executable: item.executable,
                          baseArgsJson: JSON.stringify(item.base_args, null, 2),
                          defaultCwd: item.default_cwd,
                          defaultEnvJson: JSON.stringify(item.default_env, null, 2),
                          description: item.description || '',
                          color: item.color || '#1ea7a4'
                        })
                      }
                    >
                      填充
                    </Button>,
                    !item.is_builtin ? (
                      <Button key="delete" size="mini" status="danger" onClick={() => handleDeleteTemplate(item.template_id)}>
                        删除
                      </Button>
                    ) : null
                  ]}
                >
                  <List.Item.Meta
                    title={
                      <Space>
                        <span>{item.name}</span>
                        <Tag color={item.color || 'arcoblue'}>{item.cli_type}</Tag>
                        {item.is_builtin ? <Tag bordered color="gray">builtin</Tag> : null}
                      </Space>
                    }
                    description={`${item.executable} ${item.base_args.join(' ')}`.trim()}
                  />
                </List.Item>
              )}
            />
          </Spin>
        </Card>

        <Card className="column-card" title="Managed Processes">
          <Form layout="vertical">
            <Form.Item label="当前模板">
              <Select value={selectedTemplateId} onChange={(value) => setSelectedTemplateId(String(value))}>
                {templates.map((item) => (
                  <Select.Option key={item.template_id} value={item.template_id}>
                    {item.name}
                  </Select.Option>
                ))}
              </Select>
            </Form.Item>
            <Form.Item label="工作目录覆盖">
              <Input value={launchForm.cwdOverride} onChange={(value) => setLaunchForm((prev) => ({ ...prev, cwdOverride: value }))} />
            </Form.Item>
            <Form.Item label="额外参数 JSON">
              <TextArea value={launchForm.extraArgsJson} onChange={(value) => setLaunchForm((prev) => ({ ...prev, extraArgsJson: value }))} autoSize={{ minRows: 2 }} />
            </Form.Item>
            <Form.Item label="环境变量覆盖 JSON">
              <TextArea value={launchForm.envOverridesJson} onChange={(value) => setLaunchForm((prev) => ({ ...prev, envOverridesJson: value }))} autoSize={{ minRows: 2 }} />
            </Form.Item>
            <Form.Item label="标签">
              <Input value={launchForm.label} onChange={(value) => setLaunchForm((prev) => ({ ...prev, label: value }))} />
            </Form.Item>
            <Form.Item label="超时毫秒">
              <Input value={launchForm.timeoutMs} onChange={(value) => setLaunchForm((prev) => ({ ...prev, timeoutMs: value }))} />
            </Form.Item>
            <Button type="primary" loading={submittingProcess} onClick={handleStartProcess}>
              启动本地 CLI
            </Button>
          </Form>
          <Divider />
          <Spin loading={processesLoading}>
            {processes.length === 0 ? (
              <Empty description="暂无进程" />
            ) : (
              <List
                dataSource={processes}
                render={(item) => (
                  <List.Item
                    key={item.process_id}
                    className={item.process_id === selectedProcessId ? 'active-process' : ''}
                    actions={[
                      <Button key="select" size="mini" onClick={() => setSelectedProcessId(item.process_id)}>
                        详情
                      </Button>
                    ]}
                  >
                    <List.Item.Meta
                      title={
                        <Space>
                          <span>{item.label || item.template_name || item.process_id}</span>
                          <Tag color={item.status === 'running' ? 'green' : item.status === 'failed' ? 'red' : 'arcoblue'}>{item.status}</Tag>
                        </Space>
                      }
                      description={`${item.command || ''} | ${formatTime(item.start_time)}`}
                    />
                  </List.Item>
                )}
              />
            )}
          </Spin>
        </Card>

        <Card className="column-card" title="Process Detail / Output">
          {selectedProcessId && processDetail ? (
            <>
              <Space direction="vertical" size={8} style={{ width: '100%' }}>
                <Typography.Text>Process ID: {processDetail.process_id}</Typography.Text>
                <Typography.Text>Template: {processDetail.template_name || '-'}</Typography.Text>
                <Typography.Text>Status: {processDetail.status}</Typography.Text>
                <Typography.Text>Start: {formatTime(processDetail.start_time)}</Typography.Text>
                <Typography.Text>End: {formatTime(processDetail.end_time)}</Typography.Text>
                <Typography.Paragraph copyable>{processDetail.command || ''}</Typography.Paragraph>
                <Space>
                  <Button status="warning" loading={stoppingProcess} onClick={handleStopProcess}>
                    停止
                  </Button>
                  <Button status="danger" onClick={handleDeleteProcess}>
                    删除
                  </Button>
                </Space>
              </Space>
              <Divider />
              <Space style={{ marginBottom: 12 }}>
                {['all', 'standardoutput', 'standarderror', 'systemmessage'].map((item) => (
                  <Button key={item} type={outputFilter === item ? 'primary' : 'secondary'} size="mini" onClick={() => setOutputFilter(item)}>
                    {item}
                  </Button>
                ))}
              </Space>
              <div className="output-panel">
                {filteredOutput.length === 0 ? (
                  <Empty description="当前过滤条件下没有输出" />
                ) : (
                  filteredOutput.map((item, index) => (
                    <div className={`output-entry output-${item.output_type}`} key={`${item.timestamp}-${index}`}>
                      <div className="output-meta">
                        <span>{formatTime(item.timestamp)}</span>
                        <Tag size="small">{item.output_type}</Tag>
                      </div>
                      <pre>{item.content}</pre>
                    </div>
                  ))
                )}
              </div>
            </>
          ) : (
            <Empty description={selectedTemplate ? `选择模板 ${selectedTemplate.name} 并启动进程后，这里会显示详情与输出。` : '请选择节点和模板。'} />
          )}
        </Card>
      </main>
    </div>
  );
}
