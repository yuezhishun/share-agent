export function isTerminalViewportRenderable(activeCenterTab, hostElement) {
  if (activeCenterTab !== 'terminal' || !hostElement) {
    return false;
  }

  if (typeof hostElement.getBoundingClientRect !== 'function') {
    return false;
  }

  const rect = hostElement.getBoundingClientRect();
  return Number(rect.width) > 0 && Number(rect.height) > 0;
}
