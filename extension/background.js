import {
  GitItError,
  addHit,
  getLogin,
  getRepoInfo,
  hitNames,
  normalizeConfig,
  nowIsoUtc,
  parseFullName,
  readHits,
} from './lib/github.js';

const CACHE_MS = 60_000;
let cache = null; // { key, at, names: Set<string> }

function cacheKey(cfg) {
  return `${cfg.owner}/${cfg.repo}`.toLowerCase();
}

async function loadConfig() {
  const { config } = await chrome.storage.local.get('config');
  const cfg = normalizeConfig(config);
  if (!cfg.owner) throw new GitItError('Open the Git It! options and save your settings.', 'not-configured');
  return cfg;
}

async function knownNames(cfg) {
  const key = cacheKey(cfg);
  if (cache && cache.key === key && Date.now() - cache.at < CACHE_MS) return cache.names;
  const { doc } = await readHits(cfg);
  cache = { key, at: Date.now(), names: hitNames(doc) };
  return cache.names;
}

async function handleStatus(msg) {
  const { fullName } = parseFullName(msg.fullName);
  const cfg = await loadConfig();
  const names = await knownNames(cfg);
  return { got: names.has(fullName.toLowerCase()), repo: cfg.repo };
}

async function handleAdd(msg) {
  const { fullName } = parseFullName(msg.fullName);
  const cfg = await loadConfig();
  const info = await getRepoInfo(cfg.token, fullName);
  const hit = {
    fullName: info.fullName,
    url: `https://github.com/${info.fullName}`,
    description: info.description,
    addedAt: nowIsoUtc(),
  };
  const result = await addHit(cfg, hit);
  cache = { key: cacheKey(cfg), at: Date.now(), names: hitNames(result.doc) };
  return { status: result.status, repo: cfg.repo };
}

async function handleSaveConfig(msg) {
  const { config: existing } = await chrome.storage.local.get('config');
  const submitted = msg.config ?? {};
  const merged = { ...submitted, token: submitted.token?.trim() ? submitted.token : existing?.token };
  const cfg = normalizeConfig(merged);
  if (!cfg.owner) cfg.owner = await getLogin(cfg.token);
  const { doc } = await readHits(cfg);
  await chrome.storage.local.set({ config: cfg });
  cache = { key: cacheKey(cfg), at: Date.now(), names: hitNames(doc) };
  return { owner: cfg.owner, repo: cfg.repo, count: doc.hits.length };
}

function isExtensionPage(sender) {
  return typeof sender.url === 'string' && sender.url.startsWith(chrome.runtime.getURL(''));
}

const handlers = {
  status: handleStatus,
  add: handleAdd,
  openOptions: async () => {
    await chrome.runtime.openOptionsPage();
    return {};
  },
  saveConfig: async (msg, sender) => {
    if (!isExtensionPage(sender)) throw new GitItError('Settings can only be changed from the options page.');
    return handleSaveConfig(msg);
  },
};

chrome.runtime.onMessage.addListener((msg, sender, sendResponse) => {
  if (sender.id !== chrome.runtime.id) return false;
  const handler = typeof msg?.type === 'string' && Object.hasOwn(handlers, msg.type) ? handlers[msg.type] : null;
  if (!handler) return false;
  handler(msg, sender).then(
    (result) => sendResponse({ ok: true, ...result }),
    (err) => sendResponse({ ok: false, error: err?.message ?? String(err), code: err?.code ?? 'error' }),
  );
  return true; // keep the channel open for the async response
});

chrome.action.onClicked.addListener(() => chrome.runtime.openOptionsPage());

chrome.runtime.onInstalled.addListener(async ({ reason }) => {
  if (reason === 'install') await chrome.runtime.openOptionsPage();
});
