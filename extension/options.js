const form = document.getElementById('form');
const tokenInput = document.getElementById('token');
const ownerInput = document.getElementById('owner');
const repoInput = document.getElementById('repo');
const saveBtn = document.getElementById('save');
const result = document.getElementById('result');

function show(text, ok) {
  result.textContent = text;
  result.className = ok ? 'ok' : 'err';
}

async function load() {
  const { config } = await chrome.storage.local.get('config');
  if (!config) return;
  ownerInput.value = config.owner ?? '';
  repoInput.value = config.repo ?? '';
  if (config.token) tokenInput.placeholder = 'Saved. Leave blank to keep it.';
}

form.addEventListener('submit', async (event) => {
  event.preventDefault();
  saveBtn.disabled = true;
  show('Checking…', true);
  try {
    const res = await chrome.runtime.sendMessage({
      type: 'saveConfig',
      config: { token: tokenInput.value, owner: ownerInput.value, repo: repoInput.value },
    });
    if (res?.ok) {
      ownerInput.value = res.owner;
      repoInput.value = res.repo;
      tokenInput.value = '';
      tokenInput.placeholder = 'Saved. Leave blank to keep it.';
      show(`Saved. ${res.owner}/${res.repo} has ${res.count} bookmark${res.count === 1 ? '' : 's'}.`, true);
    } else {
      show(res?.error ?? 'Saving failed.', false);
    }
  } catch (e) {
    show(e?.message ?? String(e), false);
  } finally {
    saveBtn.disabled = false;
  }
});

load();
