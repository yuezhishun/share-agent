function trimTrailingSlash(value: string) {
  return value.replace(/\/+$/, '');
}

function normalizeUrl(value: string) {
  if (!value) {
    return '';
  }

  if (/^https?:\/\//i.test(value)) {
    return trimTrailingSlash(value);
  }

  if (typeof window === 'undefined') {
    return trimTrailingSlash(value);
  }

  return trimTrailingSlash(new URL(value, window.location.origin).toString());
}

const gatewayBaseUrl = normalizeUrl(String(import.meta.env.VITE_GATEWAY_BASE_URL || '').trim());
const gatewayHubUrl = normalizeUrl(
  String(import.meta.env.VITE_GATEWAY_HUB_URL || '').trim() || `${gatewayBaseUrl || window.location.origin}/hubs/terminal`
);

export const env = {
  gatewayBaseUrl,
  gatewayHubUrl,
  defaultNodeId: String(import.meta.env.VITE_DEFAULT_NODE_ID || 'local').trim() || 'local',
  enableMcpUi: String(import.meta.env.VITE_ENABLE_MCP_UI || 'true').trim().toLowerCase() !== 'false',
};
