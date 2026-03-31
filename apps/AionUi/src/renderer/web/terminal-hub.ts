import * as signalR from '@microsoft/signalr';
import { env } from './env';
import type { GatewayTerminalEvent } from './types';

export interface TerminalHubClient {
  connect(): Promise<void>;
  disconnect(): Promise<void>;
  join(sessionId: string): Promise<void>;
  leave(sessionId: string): Promise<void>;
  sendInput(sessionId: string, data: string): Promise<void>;
  requestResize(sessionId: string, cols: number, rows: number): Promise<void>;
  requestScreenSync(sessionId: string): Promise<void>;
  requestRawSync(sessionId: string, sinceSeq?: number): Promise<void>;
}

export function createTerminalHubClient(onEvent: (event: GatewayTerminalEvent) => void): TerminalHubClient {
  const connection = new signalR.HubConnectionBuilder()
    .withUrl(env.gatewayHubUrl)
    .withAutomaticReconnect()
    .build();

  connection.on('TerminalEvent', (event: GatewayTerminalEvent) => onEvent(event));

  return {
    async connect() {
      if (connection.state === signalR.HubConnectionState.Disconnected) {
        await connection.start();
      }
    },
    async disconnect() {
      if (connection.state !== signalR.HubConnectionState.Disconnected) {
        await connection.stop();
      }
    },
    async join(sessionId: string) {
      await connection.invoke('JoinInstance', { instanceId: sessionId });
    },
    async leave(sessionId: string) {
      await connection.invoke('LeaveInstance', { instanceId: sessionId });
    },
    async sendInput(sessionId: string, data: string) {
      await connection.invoke('SendInput', { instanceId: sessionId, data });
    },
    async requestResize(sessionId: string, cols: number, rows: number) {
      await connection.invoke('RequestResize', {
        instanceId: sessionId,
        cols,
        rows,
        reqId: `resize-${Date.now()}`,
      });
    },
    async requestScreenSync(sessionId: string) {
      await connection.invoke('RequestSync', { instanceId: sessionId, type: 'screen' });
    },
    async requestRawSync(sessionId: string, sinceSeq?: number) {
      await connection.invoke('RequestSync', {
        instanceId: sessionId,
        type: 'raw',
        reqId: `raw-${Date.now()}`,
        sinceSeq,
      });
    },
  };
}
