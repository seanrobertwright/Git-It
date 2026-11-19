// Injects the "Git It!" button on GitHub repository pages. GitHub navigates without
// full page loads, so the button is re-checked whenever the page changes.
(() => {
  'use strict';

  const BTN_ID = 'git-it-button';
  const OWNER_RE = /^[A-Za-z0-9](?:[A-Za-z0-9-]{0,38})$/;
  const REPO_RE = /^[A-Za-z0-9._-]{1,100}$/;
  // First path segments that are GitHub pages, not users or orgs.
  const RESERVED = new Set([
    'about', 'account', 'apps', 'codespaces', 'collections', 'copilot', 'customer-stories',
    'dashboard', 'enterprise', 'events', 'explore', 'features', 'github-copilot', 'issues',
    'login', 'logout', 'marketplace', 'new', 'notifications', 'organizations', 'orgs',
    'pricing', 'pulls', 'readme', 'resources', 'search', 'security', 'settings', 'signup',
    'site', 'solutions', 'sponsors', 'stars', 'team', 'topics', 'trending', 'users', 'watching',
  ]);

  const LABELS = {
    idle: 'Git It!',
    busy: 'Getting…',
    got: 'Got It ✓',
    error: 'Git It! (retry)',
  };

  function repoFromPage() {
    const segs = location.pathname.split('/').filter(Boolean);
    if (segs.length < 2) return null;
    const [owner, repo] = segs;
    if (RESERVED.has(owner.toLowerCase()) || !OWNER_RE.test(owner) || !REPO_RE.test(repo)) return null;
    const nwo = document.querySelector('meta[name="octolytics-dimension-repository_nwo"]')?.content;
    if (nwo) return nwo.toLowerCase() === `${owner}/${repo}`.toLowerCase() ? nwo : null;
    const header = document.querySelector('#repository-container-header, [data-testid="repository-container-header"]');
    return header ? `${owner}/${repo}` : null;
  }

  function send(msg) {
    return new Promise((resolve) => {
      try {
        chrome.runtime.sendMessage(msg, (res) => {
          if (chrome.runtime.lastError) resolve({ ok: false, error: chrome.runtime.lastError.message });
          else resolve(res ?? { ok: false, error: 'No response from the extension.' });
        });
      } catch {
        resolve({ ok: false, error: 'Git It! was updated. Reload this page.' });
      }
    });
  }

  function setState(btn, state, title = '') {
    btn.dataset.state = state;
    btn.textContent = LABELS[state];
    btn.title = title;
    btn.setAttribute('aria-busy', state === 'busy' ? 'true' : 'false');
  }

  function headerSlot() {
    return document.querySelector('ul.pagehead-actions');
  }

  function wrapperOf(btn) {
    return btn.closest('[data-git-it-wrapper]') ?? btn;
  }

  function place(btn) {
    const slot = headerSlot();
    const wrapper = wrapperOf(btn);
    if (slot) {
      if (wrapper.parentElement === slot) return;
      const li = wrapper.tagName === 'LI' ? wrapper : document.createElement('li');
      li.dataset.gitItWrapper = '';
      btn.classList.remove('git-it-floating');
      btn.classList.add('btn', 'btn-sm');
      li.appendChild(btn);
      slot.prepend(li);
    } else if (!btn.classList.contains('git-it-floating')) {
      btn.classList.remove('btn', 'btn-sm');
      btn.classList.add('git-it-floating');
      document.body.appendChild(btn);
      if (wrapper !== btn) wrapper.remove();
    }
  }

  async function refreshStatus(btn, fullName) {
    const res = await send({ type: 'status', fullName });
    if (btn.dataset.repo !== fullName || btn.dataset.state === 'busy') return;
    if (res.ok && res.got) setState(btn, 'got', `Already in ${res.repo}`);
    else if (!res.ok && res.code === 'not-configured') setState(btn, 'idle', res.error);
  }

  async function onClick(event) {
    const btn = event.currentTarget;
    const fullName = btn.dataset.repo;
    if (btn.dataset.state === 'busy' || btn.dataset.state === 'got') return;
    setState(btn, 'busy');
    const res = await send({ type: 'add', fullName });
    if (btn.dataset.repo !== fullName) return; // navigated to another repo meanwhile
    if (res.ok) {
      setState(btn, 'got', res.status === 'exists' ? `Already in ${res.repo}` : `Saved to ${res.repo}`);
    } else if (res.code === 'not-configured') {
      setState(btn, 'error', res.error);
      send({ type: 'openOptions' });
    } else {
      setState(btn, 'error', res.error);
    }
  }

  function createButton(fullName) {
    const btn = document.createElement('button');
    btn.id = BTN_ID;
    btn.type = 'button';
    btn.className = 'git-it-button';
    btn.dataset.repo = fullName;
    setState(btn, 'idle');
    btn.addEventListener('click', onClick);
    return btn;
  }

  function sync() {
    const fullName = repoFromPage();
    let btn = document.getElementById(BTN_ID);
    if (btn && btn.dataset.repo !== fullName) {
      wrapperOf(btn).remove();
      btn = null;
    }
    if (!fullName) return;
    if (!btn) {
      btn = createButton(fullName);
      place(btn);
      refreshStatus(btn, fullName);
    } else {
      place(btn);
    }
  }

  let scheduled = false;
  function scheduleSync() {
    if (scheduled) return;
    scheduled = true;
    requestAnimationFrame(() => {
      scheduled = false;
      sync();
    });
  }

  sync();
  document.addEventListener('turbo:load', scheduleSync);
  window.addEventListener('popstate', scheduleSync);
  new MutationObserver(scheduleSync).observe(document.body, { childList: true, subtree: true });
})();
