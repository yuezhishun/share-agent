import { defineStore } from 'pinia';

const STORAGE_KEY = 'webcli-recipes-v1';

function nowIso() {
  return new Date().toISOString();
}

function toSafeGroup(value) {
  const text = String(value || '').trim();
  return text || 'general';
}

function normalizeArgs(value) {
  if (Array.isArray(value)) {
    return value.map((x) => String(x ?? '')).filter((x) => x.length > 0);
  }

  const text = String(value || '').trim();
  if (!text) {
    return [];
  }

  try {
    const parsed = JSON.parse(text);
    if (Array.isArray(parsed)) {
      return parsed.map((x) => String(x ?? '')).filter((x) => x.length > 0);
    }
  } catch {
  }

  return text.split(/\s+/).filter(Boolean);
}

function normalizeEnv(value) {
  if (!value || typeof value !== 'object' || Array.isArray(value)) {
    return {};
  }

  const env = {};
  for (const [key, raw] of Object.entries(value)) {
    const envKey = String(key || '').trim();
    if (!envKey) {
      continue;
    }
    env[envKey] = String(raw ?? '');
  }
  return env;
}

function normalizeRecipe(raw, fallbackId) {
  const command = String(raw?.command || '').trim() || 'bash';
  const args = normalizeArgs(raw?.args);
  const env = normalizeEnv(raw?.env);
  const cwd = String(raw?.cwd || '').trim();
  const name = String(raw?.name || command).trim() || command;
  const createdAt = String(raw?.createdAt || nowIso());
  const updatedAt = String(raw?.updatedAt || createdAt);

  return {
    id: String(raw?.id || fallbackId || ''),
    name,
    group: toSafeGroup(raw?.group),
    cwd,
    command,
    args,
    env,
    createdAt,
    updatedAt
  };
}

function buildDefaultRecipes() {
  const createdAt = nowIso();
  return [
    normalizeRecipe({
      id: 'r1',
      name: '系统更新',
      group: 'maintenance',
      cwd: '/home/yueyuan',
      command: 'bash',
      args: ['-lc', 'sudo apt update && sudo apt upgrade -y'],
      env: {},
      createdAt,
      updatedAt: createdAt
    }),
    normalizeRecipe({
      id: 'r2',
      name: '查看日志',
      group: 'diagnostics',
      cwd: '/home/yueyuan',
      command: 'bash',
      args: ['-lc', 'journalctl -xe --no-pager | head -n 50'],
      env: {},
      createdAt,
      updatedAt: createdAt
    }),
    normalizeRecipe({
      id: 'r3',
      name: '磁盘占用',
      group: 'diagnostics',
      cwd: '/home/yueyuan',
      command: 'bash',
      args: ['-lc', 'df -h'],
      env: {},
      createdAt,
      updatedAt: createdAt
    })
  ];
}

function inferNextId(items) {
  let max = 0;
  for (const item of items || []) {
    const match = String(item?.id || '').match(/^r(\d+)$/);
    if (!match) {
      continue;
    }
    const value = Number(match[1]);
    if (Number.isFinite(value) && value > max) {
      max = value;
    }
  }
  return max + 1;
}

export const useWebCliRecipesStore = defineStore('webcliRecipes', {
  state: () => ({
    loaded: false,
    nextId: 4,
    items: []
  }),

  getters: {
    groups(state) {
      const map = new Map();
      for (const item of state.items) {
        const group = toSafeGroup(item.group);
        if (!map.has(group)) {
          map.set(group, []);
        }
        map.get(group).push(item);
      }
      return Array.from(map.entries())
        .sort((a, b) => a[0].localeCompare(b[0], 'zh-Hans-CN'))
        .map(([group, items]) => ({
          group,
          items: items.sort((a, b) => String(a.name || '').localeCompare(String(b.name || ''), 'zh-Hans-CN'))
        }));
    }
  },

  actions: {
    hydrate() {
      if (this.loaded) {
        return;
      }
      try {
        const raw = localStorage.getItem(STORAGE_KEY);
        if (raw) {
          const payload = JSON.parse(raw);
          const source = Array.isArray(payload?.items) ? payload.items : [];
          this.items = source.map((item, index) => normalizeRecipe(item, `r${index + 1}`));
          this.nextId = Number(payload?.nextId || inferNextId(this.items));
        } else {
          this.items = buildDefaultRecipes();
          this.nextId = inferNextId(this.items);
        }
      } catch {
        this.items = buildDefaultRecipes();
        this.nextId = inferNextId(this.items);
      }

      if (!Array.isArray(this.items) || this.items.length === 0) {
        this.items = buildDefaultRecipes();
      }
      if (!Number.isFinite(this.nextId) || this.nextId < 1) {
        this.nextId = inferNextId(this.items);
      }
      this.loaded = true;
      this.persist();
    },

    persist() {
      try {
        localStorage.setItem(STORAGE_KEY, JSON.stringify({
          nextId: this.nextId,
          items: this.items
        }));
      } catch {
      }
    },

    addRecipe(input) {
      this.hydrate();
      const command = String(input?.command || '').trim();
      if (!command) {
        throw new Error('command is required');
      }

      const timestamp = nowIso();
      const item = normalizeRecipe({
        ...input,
        id: `r${this.nextId}`,
        createdAt: timestamp,
        updatedAt: timestamp
      });
      this.nextId += 1;
      this.items.push(item);
      this.persist();
      return item;
    },

    updateRecipe(id, input) {
      this.hydrate();
      const targetId = String(id || '').trim();
      const target = this.items.find((item) => item.id === targetId);
      if (!target) {
        throw new Error('recipe not found');
      }

      const nextCommand = String(input?.command ?? target.command).trim();
      if (!nextCommand) {
        throw new Error('command is required');
      }

      const merged = normalizeRecipe({
        ...target,
        ...input,
        id: target.id,
        createdAt: target.createdAt,
        updatedAt: nowIso()
      });

      target.name = merged.name;
      target.group = merged.group;
      target.cwd = merged.cwd;
      target.command = merged.command;
      target.args = merged.args;
      target.env = merged.env;
      target.updatedAt = merged.updatedAt;
      this.persist();
      return target;
    },

    removeRecipe(id) {
      this.hydrate();
      const targetId = String(id || '').trim();
      const sizeBefore = this.items.length;
      this.items = this.items.filter((item) => item.id !== targetId);
      if (this.items.length === sizeBefore) {
        throw new Error('recipe not found');
      }
      this.persist();
    }
  }
});
