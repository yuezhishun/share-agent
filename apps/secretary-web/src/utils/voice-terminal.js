export const DEFAULT_VOICE_COMMIT_DELAY_MS = 360;
export const VOICE_MODE_SHORTCUT_LABEL = 'Ctrl+↓';

export function isVoiceToggleShortcut(event) {
  if (!event?.ctrlKey || event.altKey || event.metaKey || event.shiftKey) {
    return false;
  }

  const code = String(event.code || '');
  const key = String(event.key || '');
  return code === 'ArrowDown' || key === 'ArrowDown' || key === 'Down';
}
