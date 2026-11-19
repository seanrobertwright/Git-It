// GitHub API access for Git It!. All network calls to GitHub go through `request`,
// which sets a timeout, refuses redirects, and bypasses the HTTP cache.

const API = 'https://api.github.com';
const TIMEOUT_MS = 15_000;
const HITS_PATH = 'hits.json';
const MAX_ATTEMPTS = 4;
export const DEFAULT_REPO = 'my-gits';

export const OWNER_RE = /^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})$/;
export const REPO_RE = /^[A-Za-z0-9._-]{1,100}$/;

export class GitItError extends Error {
  constructor(message, code = 'error') {
    super(message);
    this.code = code;
  }
}

// The only date helper in the extension: the current instant as an ISO-8601 UTC string.
export function nowIsoUtc() {
  return new Date().toISOString();
}

export function parseFullName(value) {
  if (typeof value !== 'string') throw new GitItError('Invalid repository name.');
  const parts = value.split('/');
  const [owner, repo] = parts;
  if (parts.length !== 2 || !OWNER_RE.test(owner) || !REPO_RE.test(repo) || repo === '.' || repo === '..') {
    throw new GitItError(`Invalid repository name: ${value}`);
  }
  return { owner, repo, fullName: `${owner}/${repo}` };
}

// Validates stored or submitted settings. `owner` may be empty (detected from the token).
export function normalizeConfig(raw) {
  const token = typeof raw?.token === 'string' ? raw.token.trim() : '';
  if (!token) throw new GitItError('Set your GitHub token in the Git It! options.', 'not-configured');
  if (/\s/.test(token)) throw new GitItError('The token must not contain spaces.', 'not-configured');
  const owner = typeof raw.owner === 'string' ? raw.owner.trim() : '';
  const repo = (typeof raw.repo === 'string' ? raw.repo.trim() : '') || DEFAULT_REPO;
  if (owner && !OWNER_RE.test(owner)) throw new GitItError(`Invalid owner: ${owner}`, 'not-configured');
  if (!REPO_RE.test(repo)) throw new GitItError(`Invalid repository name: ${repo}`, 'not-configured');
  return { token, owner, repo };
}

function repoPath(owner, repo) {
  return `/repos/${encodeURIComponent(owner)}/${encodeURIComponent(repo)}`;
}

async function request(path, token, { method = 'GET', body } = {}) {
  const headers = {
    Accept: 'application/vnd.github+json',
    'X-GitHub-Api-Version': '2022-11-28',
  };
  if (token) headers.Authorization = `Bearer ${token}`;
  if (body !== undefined) headers['Content-Type'] = 'application/json';
  try {
    return await fetch(API + path, {
      method,
      headers,
      body: body === undefined ? undefined : JSON.stringify(body),
      redirect: 'error',
      cache: 'no-store',
      signal: AbortSignal.timeout(TIMEOUT_MS),
    });
  } catch (e) {
    if (e?.name === 'TimeoutError') throw new GitItError('GitHub did not respond in time.');
    throw new GitItError(`Could not reach GitHub: ${e?.message ?? e}`);
  }
}

async function failure(res, what) {
  let detail = '';
  try {
    detail = (await res.json())?.message ?? '';
  } catch {
    // body was not JSON; the status code is enough
  }
  if (res.status === 401) {
    return new GitItError('GitHub rejected the token (401). Update it in the Git It! options.', 'not-configured');
  }
  return new GitItError(`${what} failed: HTTP ${res.status}${detail ? ` (${detail})` : ''}`);
}

function encodeUtf8Base64(text) {
  const bytes = new TextEncoder().encode(text);
  let bin = '';
  for (let i = 0; i < bytes.length; i += 0x8000) {
    bin += String.fromCharCode(...bytes.subarray(i, i + 0x8000));
  }
  return btoa(bin);
}

function decodeUtf8Base64(b64) {
  const bin = atob(String(b64).replace(/\s/g, ''));
  return new TextDecoder().decode(Uint8Array.from(bin, (c) => c.charCodeAt(0)));
}

function parseDoc(text) {
  let doc;
  try {
    doc = JSON.parse(text);
  } catch {
    doc = null;
  }
  if (!doc || typeof doc !== 'object' || !Array.isArray(doc.hits)) {
    throw new GitItError(`${HITS_PATH} is not valid Git It! data. Fix or delete it in the repo.`);
  }
  return doc;
}

function sameRepo(hit, lowerFullName) {
  return typeof hit?.fullName === 'string' && hit.fullName.toLowerCase() === lowerFullName;
}

export async function getLogin(token) {
  const res = await request('/user', token);
  if (!res.ok) throw await failure(res, 'Looking up your GitHub user');
  const login = (await res.json())?.login;
  if (typeof login !== 'string' || !OWNER_RE.test(login)) throw new GitItError('GitHub returned an unexpected user.');
  return login;
}

// Returns the canonical name and description, or the given name with no description
// if the repo can't be read with this token.
export async function getRepoInfo(token, fullName) {
  const { owner, repo } = parseFullName(fullName);
  try {
    const res = await request(repoPath(owner, repo), token);
    if (!res.ok) return { fullName, description: '' };
    const body = await res.json();
    const canonical = parseFullName(body?.full_name).fullName;
    return { fullName: canonical, description: typeof body?.description === 'string' ? body.description : '' };
  } catch {
    return { fullName, description: '' };
  }
}

// Reads hits.json. `sha` is null when the file does not exist yet.
export async function readHits(cfg) {
  const base = repoPath(cfg.owner, cfg.repo);
  const res = await request(`${base}/contents/${HITS_PATH}`, cfg.token);
  if (res.status === 404) {
    const repoRes = await request(base, cfg.token);
    if (!repoRes.ok) {
      throw new GitItError(
        `Can't see ${cfg.owner}/${cfg.repo}. Create it and give the token access to it.`,
        'not-configured',
      );
    }
    return { doc: { version: 1, hits: [] }, sha: null };
  }
  if (!res.ok) throw await failure(res, `Reading ${HITS_PATH}`);
  const meta = await res.json();
  let b64 = meta.content;
  if (meta.encoding !== 'base64') {
    // Files over 1 MB come back without inline content; fetch the blob instead.
    const blobRes = await request(`${base}/git/blobs/${encodeURIComponent(meta.sha)}`, cfg.token);
    if (!blobRes.ok) throw await failure(blobRes, `Reading ${HITS_PATH}`);
    b64 = (await blobRes.json()).content;
  }
  return { doc: parseDoc(decodeUtf8Base64(b64)), sha: meta.sha };
}

// Adds `hit` to the top of hits.json as one commit. The write carries the sha it read,
// so GitHub rejects it if another computer committed in between; we then re-read and retry.
export async function addHit(cfg, hit) {
  const lower = hit.fullName.toLowerCase();
  const url = `${repoPath(cfg.owner, cfg.repo)}/contents/${HITS_PATH}`;
  for (let attempt = 1; attempt <= MAX_ATTEMPTS; attempt++) {
    const { doc, sha } = await readHits(cfg);
    if (doc.hits.some((h) => sameRepo(h, lower))) return { status: 'exists', doc };
    const next = { ...doc, version: doc.version ?? 1, hits: [hit, ...doc.hits] };
    const body = {
      message: `Git It: ${hit.fullName}`,
      content: encodeUtf8Base64(`${JSON.stringify(next, null, 2)}\n`),
    };
    if (sha) body.sha = sha;
    const res = await request(url, cfg.token, { method: 'PUT', body });
    if (res.ok) return { status: 'added', doc: next };
    const conflict = res.status === 409 || res.status === 422;
    if (!conflict || attempt === MAX_ATTEMPTS) throw await failure(res, `Saving ${HITS_PATH}`);
  }
  throw new GitItError(`${HITS_PATH} kept changing while saving. Try again.`);
}

export function hitNames(doc) {
  return new Set(
    doc.hits.filter((h) => typeof h?.fullName === 'string').map((h) => h.fullName.toLowerCase()),
  );
}
