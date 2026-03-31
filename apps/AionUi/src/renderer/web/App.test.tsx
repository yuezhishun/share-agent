import { render, screen } from '@testing-library/react';
import { MemoryRouter } from 'react-router-dom';
import { describe, expect, it, vi } from 'vitest';
import App from './App';

vi.mock('./hooks', async () => {
  const actual = await vi.importActual<typeof import('./hooks')>('./hooks');
  return {
    ...actual,
    useSessions: () => ({
      sessions: [],
      createSession: vi.fn(),
      deleteSession: vi.fn(),
      pruneExited: vi.fn(),
      isLoading: false,
      error: null,
    }),
    useCliTemplates: () => ({
      templates: [],
      error: null,
      isLoading: false,
      refresh: vi.fn(),
      createTemplate: vi.fn(),
      updateTemplate: vi.fn(),
      deleteTemplate: vi.fn(),
    }),
    useCliProcesses: () => ({
      processes: [],
      selectedProcess: null,
      outputItems: [],
      error: null,
      isLoading: false,
      startProcess: vi.fn(),
      stopProcess: vi.fn(),
      waitProcess: vi.fn(),
      deleteProcess: vi.fn(),
      refresh: vi.fn(),
    }),
    useMcpConfig: () => ({
      servers: [],
      upsertServer: vi.fn(),
      deleteServer: vi.fn(),
    }),
    useSessionTerminal: () => ({
      snapshot: null,
      history: null,
      events: [],
      screenText: '',
      rawText: '',
      status: 'connected',
      lastResizeAck: null,
      lastExit: null,
      refreshSnapshot: vi.fn(),
      loadHistory: vi.fn(),
      sendInput: vi.fn(),
      resize: vi.fn(),
      terminate: vi.fn(),
    }),
  };
});

function renderAt(path: string) {
  return render(
    <MemoryRouter initialEntries={[path]}>
      <App />
    </MemoryRouter>
  );
}

describe('App routing', () => {
  it('renders sessions page on the canonical route', () => {
    renderAt('/sessions');
    expect(screen.getByRole('heading', { name: 'Sessions' })).toBeInTheDocument();
  });

  it('redirects legacy routes to sessions', async () => {
    renderAt('/settings/about');
    expect(await screen.findByRole('heading', { name: 'Sessions' })).toBeInTheDocument();
  });

  it('redirects unknown routes to sessions', async () => {
    renderAt('/anything-else');
    expect(await screen.findByRole('heading', { name: 'Sessions' })).toBeInTheDocument();
  });
});
