import { defineStore } from 'pinia';

const TERMINAL_API_BASE = import.meta.env.VITE_TERMINAL_API_BASE || '/terminal-api';
const DEFAULT_PROFILE_KEY = 'terminal.defaultProfileId';

function readDefaultProfileId() {
  if (typeof window === 'undefined') {
    return '';
  }
  return String(window.localStorage.getItem(DEFAULT_PROFILE_KEY) || '').trim();
}

export const useTerminalProfileStore = defineStore('terminalProfiles', {
  state: () => ({
    profiles: [],
    loading: false,
    error: '',
    defaultProfileId: readDefaultProfileId()
  }),
  getters: {
    defaultProfile(state) {
      return state.profiles.find((x) => x.profileId === state.defaultProfileId) || null;
    }
  },
  actions: {
    setDefaultProfile(profileId) {
      const normalized = String(profileId || '').trim();
      this.defaultProfileId = normalized;
      if (typeof window !== 'undefined') {
        if (normalized) {
          window.localStorage.setItem(DEFAULT_PROFILE_KEY, normalized);
        } else {
          window.localStorage.removeItem(DEFAULT_PROFILE_KEY);
        }
      }
    },

    async loadProfiles() {
      this.loading = true;
      this.error = '';
      try {
        const res = await fetch(`${TERMINAL_API_BASE}/profiles`);
        if (!res.ok) {
          throw new Error(await readError(res, `load profiles failed: ${res.status}`));
        }

        this.profiles = await res.json();
        if (this.defaultProfileId && !this.profiles.some((x) => x.profileId === this.defaultProfileId)) {
          this.setDefaultProfile('');
        }
        if (!this.defaultProfileId && this.profiles.length > 0) {
          const codex = this.profiles.find((x) => x.profileId === 'builtin-codex' || x.cliType === 'codex') || null;
          this.setDefaultProfile(codex?.profileId || this.profiles[0].profileId);
        }
        return this.profiles;
      } catch (err) {
        this.error = String(err?.message || err);
        throw err;
      } finally {
        this.loading = false;
      }
    },

    async createProfile(payload) {
      const res = await fetch(`${TERMINAL_API_BASE}/profiles`, {
        method: 'POST',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(payload)
      });
      if (!res.ok) {
        throw new Error(await readError(res, `create profile failed: ${res.status}`));
      }

      const created = await res.json();
      this.profiles.push(created);
      this.profiles.sort((a, b) => String(a.name).localeCompare(String(b.name)));
      if (!this.defaultProfileId) {
        this.setDefaultProfile(created.profileId);
      }
      return created;
    },

    async updateProfile(profileId, payload) {
      const res = await fetch(`${TERMINAL_API_BASE}/profiles/${encodeURIComponent(profileId)}`, {
        method: 'PUT',
        headers: { 'content-type': 'application/json' },
        body: JSON.stringify(payload)
      });
      if (!res.ok) {
        throw new Error(await readError(res, `update profile failed: ${res.status}`));
      }

      const updated = await res.json();
      const idx = this.profiles.findIndex((x) => x.profileId === profileId);
      if (idx >= 0) {
        this.profiles[idx] = updated;
      } else {
        this.profiles.push(updated);
      }
      return updated;
    },

    async deleteProfile(profileId) {
      const res = await fetch(`${TERMINAL_API_BASE}/profiles/${encodeURIComponent(profileId)}`, {
        method: 'DELETE'
      });
      if (!res.ok) {
        throw new Error(await readError(res, `delete profile failed: ${res.status}`));
      }

      this.profiles = this.profiles.filter((x) => x.profileId !== profileId);
      if (this.defaultProfileId === profileId) {
        this.setDefaultProfile(this.profiles[0]?.profileId || '');
      }
    }
  }
});

async function readError(res, fallback) {
  try {
    const text = await res.text();
    return text || fallback;
  } catch {
    return fallback;
  }
}
