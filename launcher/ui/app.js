// DorkNet Launcher UI — single-file JS, no framework. State held in
// `appState`; views toggled via `showView(name)`. C# is reached through
// `bridge.send({type, payload})`; C# pushes events via
// `window.external.receiveMessage(handler)`.

'use strict';

let appState = null;          // mirrors the C# AppState payload
let versions = null;          // versions.json contents
let previousView = null;      // for the settings 'back' button

// ── Message bridge ───────────────────────────────────────────────────
const bridge = {
  send(envelope) {
    // PhotinoNET exposes window.external.sendMessage(string) — the C#
    // side parses the JSON envelope and routes by `type`.
    window.external.sendMessage(JSON.stringify(envelope));
  }
};

window.external.receiveMessage(raw => {
  let env;
  try { env = JSON.parse(raw); }
  catch { console.error('bridge: non-JSON message', raw); return; }
  handleEvent(env.type, env.payload);
});

function handleEvent(type, payload) {
  switch (type) {
    case 'state-changed':
      appState = payload;
      reflectStateInUi();
      break;
    case 'versions':
      versions = payload;
      reflectVersionsInUi();
      break;
    case 'host-status':
      renderHostStatus(payload);
      break;
    case 'join-status':
      renderJoinStatus(payload);
      break;
    case 'download-progress':
      renderDownloadProgress(payload);
      break;
    case 'join-code-decoded':
      renderJoinPreview(payload);
      break;
    case 'error':
      showToast(payload?.message ?? 'Unknown error', 'error');
      break;
    default:
      console.warn('bridge: unhandled event', type, payload);
  }
}

// ── View routing ─────────────────────────────────────────────────────
function showView(name) {
  for (const sec of document.querySelectorAll('section[data-view]')) {
    const matches = sec.dataset.view === name;
    sec.hidden = !matches;
  }
}

function reflectStateInUi() {
  if (!appState) return;

  // First-run check.
  if (appState.mode === 'unset') { showView('first-run'); return; }

  // Mirror persisted values into the right view's inputs.
  const recDisplay = document.getElementById('recroom-path-display');
  const recDisplayJoin = document.getElementById('recroom-path-display-join');
  const path = appState.recRoomPath || '(not picked)';
  if (recDisplay) recDisplay.textContent = path;
  if (recDisplayJoin) recDisplayJoin.textContent = path;

  document.getElementById('photon-app-id').value = appState.photonAppId || '';
  document.getElementById('photon-voice-app-id').value = appState.photonVoiceAppId || '';
  document.getElementById('photon-region').value = appState.photonRegion || 'us';
  document.getElementById('server-name').value = appState.serverName || '';

  // Enable join button only when prerequisites are met.
  refreshJoinReadiness();

  // Show the mode the user picked unless they're in settings.
  if (!isSettingsOpen()) showView(appState.mode);
}

function reflectVersionsInUi() {
  const sel = document.getElementById('version-select');
  if (!sel || !versions) return;
  sel.innerHTML = '';
  for (const v of versions.branches.filter(b => b.supported)) {
    const opt = document.createElement('option');
    opt.value = v.versionKey;
    opt.textContent = `Rec Room ${v.clientBuild} (${v.branch})`;
    sel.appendChild(opt);
  }
  if (appState?.selectedVersion) sel.value = appState.selectedVersion;
}

function isSettingsOpen() {
  const s = document.querySelector('section[data-view="settings"]');
  return s && !s.hidden;
}

// ── Toasts ───────────────────────────────────────────────────────────
function showToast(msg, kind = '') {
  const el = document.createElement('div');
  el.className = `toast ${kind}`.trim();
  el.textContent = msg;
  document.getElementById('toasts').appendChild(el);
  setTimeout(() => el.remove(), 6000);
}

// ── Event wiring ─────────────────────────────────────────────────────
document.addEventListener('click', e => {
  const t = e.target.closest('[data-action], [data-mode-pick]');
  if (!t) return;

  if (t.dataset.modePick) {
    bridge.send({ type: 'set-mode', payload: { mode: t.dataset.modePick } });
    return;
  }

  switch (t.dataset.action) {
    case 'pick-recroom':
      bridge.send({ type: 'pick-recroom' });
      break;
    case 'open-settings':
      previousView = document.querySelector('section[data-view]:not([hidden])')?.dataset.view;
      showView('settings');
      break;
    case 'back-from-settings':
      showView(previousView || appState?.mode || 'first-run');
      break;
    case 'copy-join-code':
      const code = document.getElementById('join-code-value').textContent;
      navigator.clipboard.writeText(code).then(
        () => showToast('Join code copied', 'ok'),
        () => showToast('Copy failed — select + Ctrl+C manually', 'error'));
      break;
    case 'decode-join-code':
      const codeRaw = document.getElementById('join-code-input').value.trim();
      bridge.send({ type: 'decode-join-code', payload: { code: codeRaw } });
      break;
  }
});

// Photon inputs auto-persist on blur.
['photon-app-id', 'photon-voice-app-id', 'photon-region'].forEach(id => {
  const el = document.getElementById(id);
  if (!el) return;
  el.addEventListener('change', () => {
    bridge.send({
      type: 'set-photon',
      payload: {
        appId: document.getElementById('photon-app-id').value.trim(),
        voiceAppId: document.getElementById('photon-voice-app-id').value.trim(),
        region: document.getElementById('photon-region').value,
      }
    });
  });
});

document.getElementById('server-name').addEventListener('change', e => {
  bridge.send({ type: 'set-server-name', payload: { name: e.target.value.trim() } });
});

// Host start/stop.
document.getElementById('host-start').addEventListener('click', () => {
  if (!appState?.recRoomPath) { showToast('Pick your Rec Room install first', 'error'); return; }
  if (!appState?.photonAppId) { showToast('Photon AppId required (free at dashboard.photonengine.com)', 'error'); return; }
  const versionKey = document.getElementById('version-select').value;
  document.getElementById('host-start').disabled = true;
  document.getElementById('host-stop').hidden = false;
  bridge.send({ type: 'host-start', payload: { versionKey } });
});

document.getElementById('host-stop').addEventListener('click', () => {
  bridge.send({ type: 'host-stop' });
  document.getElementById('host-stop').hidden = true;
  document.getElementById('host-start').disabled = false;
  document.getElementById('join-code-row').hidden = true;
});

// Join apply.
document.getElementById('join-apply').addEventListener('click', () => {
  const code = document.getElementById('join-code-input').value.trim();
  bridge.send({ type: 'join-apply', payload: { code } });
});

document.getElementById('join-code-input').addEventListener('input', refreshJoinReadiness);

function refreshJoinReadiness() {
  const btn = document.getElementById('join-apply');
  if (!btn) return;
  const hasCode = document.getElementById('join-code-input').value.trim().length > 10;
  const hasPath = !!appState?.recRoomPath;
  btn.disabled = !(hasCode && hasPath);
}

// ── Render helpers for incoming events ───────────────────────────────
function renderHostStatus(p) {
  const el = document.getElementById('host-status');
  if (!p) return;
  if (p.stage === 'ready') {
    el.className = 'status ok';
    el.textContent = `Hosting at ${p.publicUrl}`;
    document.getElementById('join-code-value').textContent = p.joinCode;
    document.getElementById('join-code-row').hidden = false;
  } else if (p.stage === 'stopped') {
    el.className = 'status';
    el.textContent = 'Stopped.';
    document.getElementById('host-stop').hidden = true;
    document.getElementById('host-start').disabled = false;
    document.getElementById('join-code-row').hidden = true;
  } else {
    el.className = 'status';
    el.textContent = humanStage(p.stage);
  }
}

function renderJoinStatus(p) {
  const el = document.getElementById('join-status');
  if (!p) return;
  el.className = p.stage === 'ready' ? 'status ok' : 'status';
  el.textContent = p.stage === 'ready'
    ? 'Patched. Launch Rec Room from Steam to connect.'
    : humanStage(p.stage);
}

function renderJoinPreview(p) {
  const el = document.getElementById('join-preview');
  if (!el) return;
  if (!p) {
    el.className = 'status error';
    el.textContent = 'Invalid join code.';
    el.hidden = false;
    return;
  }
  el.className = 'status';
  el.innerHTML = `Connecting to <b>${escapeHtml(p.name || '(unnamed server)')}</b><br>` +
    `Host: <code>${escapeHtml(p.host)}</code><br>` +
    `Version: <code>${escapeHtml(p.v)}</code>`;
  el.hidden = false;
}

function renderDownloadProgress(p) {
  // Could render a progress bar; for v0.1 just flash a status line.
  if (!p || !p.total || p.total <= 0) return;
  const pct = Math.round(p.fraction * 100);
  const mb = (p.bytes / (1024 * 1024)).toFixed(1);
  const totalMb = (p.total / (1024 * 1024)).toFixed(1);
  const hostEl = document.getElementById('host-status');
  const joinEl = document.getElementById('join-status');
  const text = `Downloading… ${mb} / ${totalMb} MB (${pct}%)`;
  if (hostEl && !hostEl.classList.contains('ok')) hostEl.textContent = text;
  if (joinEl && !joinEl.classList.contains('ok')) joinEl.textContent = text;
}

function humanStage(stage) {
  return ({
    'downloading-server': 'Downloading server binary…',
    'opening-tunnel':     'Opening Cloudflare tunnel…',
    'starting-server':    'Starting the server…',
    'patching-client':    'Patching your Rec Room client…',
    'downloading-patcher': 'Downloading client patcher…',
    'ready':              'Ready.',
    'stopped':            'Stopped.',
  }[stage] || stage);
}

function escapeHtml(s) {
  return String(s)
    .replace(/&/g, '&amp;').replace(/</g, '&lt;').replace(/>/g, '&gt;')
    .replace(/"/g, '&quot;').replace(/'/g, '&#39;');
}

// ── Boot ─────────────────────────────────────────────────────────────
// Render an empty 'first-run' until the C# init reply arrives.
showView('first-run');
bridge.send({ type: 'init' });
