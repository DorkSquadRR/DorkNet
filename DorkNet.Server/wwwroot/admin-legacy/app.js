// DorkNet admin SPA. Stays single-origin to api.rec.net so no CORS:
// login at /api/admin/v1/login, then every other call carries the
// returned JWT in Authorization: Bearer.

const API = '/api/admin/v1';
const TOKEN_KEY = 'dorknet.admin.token';
const ME_KEY = 'dorknet.admin.me';
const STORE_STOREFRONTS = [
    'main', 'watch', 'all', 'rro', 'season:1',
    'giftdrop:1', 'giftdrop:2', 'giftdrop:100', 'giftdrop:101',
    'giftdrop:102', 'giftdrop:103', 'giftdrop:200', 'giftdrop:300',
    'giftdrop:400', 'giftdrop:401', 'giftdrop:402', 'giftdrop:403',
    'giftdrop:404', 'giftdrop:405', 'giftdrop:406', 'giftdrop:500',
    'giftdrop:600', 'giftdrop:700',
];

const $ = (id) => document.getElementById(id);
const el = (tag, attrs, ...children) => {
    const e = document.createElement(tag);
    for (const k in attrs) {
        if (k === 'class') e.className = attrs[k];
        else if (k === 'on') for (const ev in attrs.on) e.addEventListener(ev, attrs.on[ev]);
        else if (k === 'html') e.innerHTML = attrs[k];
        else if (k.startsWith('data-')) e.setAttribute(k, attrs[k]);
        else if (k === 'hidden' && attrs[k]) e.hidden = true;
        else e[k] = attrs[k];
    }
    for (const c of children.flat()) if (c != null) e.append(c?.nodeType ? c : document.createTextNode(String(c)));
    return e;
};

// ── HTTP ─────────────────────────────────────────────────────────────

function token() { return localStorage.getItem(TOKEN_KEY); }

async function api(path, opts = {}) {
    const headers = { ...(opts.headers || {}) };
    const t = token();
    if (t) headers.Authorization = `Bearer ${t}`;
    if (opts.body && !(opts.body instanceof FormData) && typeof opts.body !== 'string') {
        headers['Content-Type'] = 'application/json';
        opts.body = JSON.stringify(opts.body);
    }
    const ctrl = new AbortController();
    const timeoutMs = opts.timeout ?? 10000;
    const timer = setTimeout(() => ctrl.abort(), timeoutMs);
    let res;
    try {
        res = await fetch(API + path, { ...opts, headers, signal: ctrl.signal });
    } catch (err) {
        if (err.name === 'AbortError') throw new Error(`request timed out after ${timeoutMs}ms (server unreachable?)`);
        throw err;
    } finally {
        clearTimeout(timer);
    }
    if (res.status === 401) {
        logout();
        throw new Error('Unauthorized — please sign in again');
    }
    if (!res.ok) {
        let msg = `${res.status} ${res.statusText}`;
        try { const body = await res.json(); if (body?.error) msg += ` — ${body.error}`; if (body?.error_description) msg += ` (${body.error_description})`; } catch {}
        throw new Error(msg);
    }
    if (res.status === 204) return null;
    const ct = res.headers.get('content-type') || '';
    return ct.includes('application/json') ? res.json() : res.text();
}

function toast(msg, kind = '') {
    const t = $('toast');
    t.textContent = msg;
    t.className = 'toast ' + kind;
    t.hidden = false;
    clearTimeout(toast._h);
    toast._h = setTimeout(() => { t.hidden = true; }, 3000);
}

// ── Login / logout ───────────────────────────────────────────────────

$('loginForm').addEventListener('submit', async (ev) => {
    ev.preventDefault();
    $('loginError').textContent = '';
    try {
        const res = await fetch(API + '/login', {
            method: 'POST',
            headers: { 'Content-Type': 'application/json' },
            body: JSON.stringify({
                username: $('loginUsername').value,
                password: $('loginPassword').value,
            }),
        });
        if (!res.ok) {
            let msg = 'Sign-in failed';
            try { const body = await res.json(); if (body?.error === 'not_admin') msg = 'That account is not an admin.'; else if (body?.error === 'invalid_credentials') msg = 'Invalid username or password.'; } catch {}
            throw new Error(msg);
        }
        const data = await res.json();
        localStorage.setItem(TOKEN_KEY, data.access_token);
        localStorage.setItem(ME_KEY, JSON.stringify({ id: data.account_id, username: data.username, displayName: data.display_name }));
        showApp();
    } catch (err) {
        $('loginError').textContent = err.message;
    }
});

$('logoutBtn').addEventListener('click', logout);

function logout() {
    localStorage.removeItem(TOKEN_KEY);
    localStorage.removeItem(ME_KEY);
    $('app').hidden = true;
    $('login').hidden = false;
}

function showApp() {
    $('login').hidden = true;
    $('app').hidden = false;
    const me = JSON.parse(localStorage.getItem(ME_KEY) || '{}');
    $('meName').textContent = me.displayName || me.username || `#${me.id}`;
    activate('dashboard');
}

// ── Tabs ─────────────────────────────────────────────────────────────

document.querySelectorAll('nav .tab').forEach(btn => {
    btn.addEventListener('click', () => activate(btn.dataset.tab));
});

function activate(name) {
    document.querySelectorAll('nav .tab').forEach(b => b.classList.toggle('active', b.dataset.tab === name));
    document.querySelectorAll('.tab-panel').forEach(p => p.hidden = p.id !== `tab-${name}`);
    if (name === 'dashboard') loadDashboard();
    else if (name === 'players') loadPlayers();
    else if (name === 'store') loadStore();
    else if (name === 'reports') loadReports();
    else if (name === 'ipbans') loadIpBans();
    else if (name === 'audit') loadAudit();
    else if (name === 'playerlogs') initPlayerLogs();
    else if (name === 'importroom') { loadImportOwners(); loadMirrorRooms(); }
}

// ── Dashboard ────────────────────────────────────────────────────────

async function loadDashboard() {
    try {
        const s = await api('/stats');
        $('statsGrid').replaceChildren(
            stat('Players total', s.Players.Total),
            stat('Online now', s.Players.OnlineNow, 'good'),
            stat('Banned', s.Players.BannedNow, s.Players.BannedNow > 0 ? 'warn' : ''),
            stat('Rooms', s.Rooms.Total),
            stat('Inventions', s.Inventions),
            stat('Open reports', s.Moderation.OpenReports, s.Moderation.OpenReports > 0 ? 'warn' : ''),
            stat('Active IP bans', s.Moderation.ActiveIpBans),
        );
        renderTable($('topRoomsTable'),
            ['ID', 'Name', 'Visits', 'Visitors', 'Cheers'],
            s.Rooms.TopByVisits,
            r => [r.Id, r.Name,
                  (r.VisitCount ?? 0).toLocaleString(),
                  (r.VisitorCount ?? 0).toLocaleString(),
                  (r.CheerCount ?? 0).toLocaleString()]);
        renderTable($('recentJoinsTable'),
            ['ID', 'Username', 'Joined'],
            s.RecentJoins, r => [r.Id, r.Username, fmtDate(r.CreatedAt)]);
    } catch (err) { toast(err.message, 'error'); }
}

function stat(label, value, kind = '') {
    return el('div', { class: 'stat' },
        el('div', { class: 'label' }, label),
        el('div', { class: 'value ' + kind }, value));
}

// ── Players ──────────────────────────────────────────────────────────

let playerSearchTimer;
$('playerSearch').addEventListener('input', () => {
    clearTimeout(playerSearchTimer);
    playerSearchTimer = setTimeout(loadPlayers, 200);
});
$('refreshPlayers').addEventListener('click', loadPlayers);

async function loadPlayers() {
    try {
        const q = $('playerSearch').value.trim();
        const path = q ? `/players?query=${encodeURIComponent(q)}` : '/players';
        const rows = await api(path);
        renderTable($('playersTable'),
            ['ID', 'Username', 'Display', 'Lvl', 'Flags', 'Status', 'Last seen', 'Actions'],
            rows, p => [
                p.Id,
                p.Username,
                p.DisplayName,
                `${p.Level} (${p.XP} xp)`,
                flagsBadges(p),
                statusBadge(p),
                fmtDate(p.LastSeenAt),
                rowActions(p),
            ]);
    } catch (err) { toast(err.message, 'error'); }
}

function flagsBadges(p) {
    const badges = [];
    if (p.IsAdmin) badges.push(el('span', { class: 'badge admin' }, 'admin'));
    if (p.IsDeveloper) badges.push(el('span', { class: 'badge admin' }, 'dev'));
    if (p.IsVerified) badges.push(el('span', { class: 'badge admin' }, 'verified'));
    if (p.IsJunior) badges.push(el('span', { class: 'badge offline' }, 'jr'));
    return el('span', {}, ...badges);
}

function statusBadge(p) {
    if (p.BannedUntil && new Date(p.BannedUntil) > new Date())
        return el('span', { class: 'badge banned' }, 'banned');
    return el('span', { class: 'badge ' + (p.Online ? 'online' : 'offline') }, p.Online ? 'online' : 'offline');
}

function rowActions(p) {
    return el('div', { class: 'actions' },
        el('button', { on: { click: () => openPlayer(p.Id) } }, 'edit'),
        el('button', {
            class: p.Online ? '' : 'danger',
            on: { click: () => kickPlayer(p) },
        }, 'kick'),
    );
}

async function kickPlayer(p) {
    const reason = prompt(`Kick ${p.Username}? Optional reason:`, '');
    if (reason === null) return;
    try {
        await api(`/players/${p.Id}/kick`, { method: 'POST', body: { Reason: reason } });
        toast(`Kicked ${p.Username}`, 'good');
    } catch (err) { toast(err.message, 'error'); }
}

// ── Player modal: edit / grant outfit / currency ─────────────────────

async function openPlayer(id) {
    try {
        const p = await api(`/players/${id}`);
        $('modalTitle').textContent = `${p.DisplayName} (#${p.Id})`;
        const body = $('modalBody');
        body.replaceChildren(
            playerSummarySection(p),
            avatarSection(p),
            currencySection(p),
            grantsSection(p),
            flagsSection(p),
            bansSection(p),
        );
        $('playerModal').hidden = false;
    } catch (err) { toast(err.message, 'error'); }
}

document.querySelectorAll('[data-close]').forEach(b => b.addEventListener('click', () => $('playerModal').hidden = true));
$('playerModal').addEventListener('click', (ev) => { if (ev.target.id === 'playerModal') $('playerModal').hidden = true; });

function playerSummarySection(p) {
    return el('div', { class: 'section' },
        el('h3', {}, 'Profile'),
        el('div', { class: 'kvp' },
            el('span', { class: 'k' }, 'Username'), el('span', {}, p.Username),
            el('span', { class: 'k' }, 'Email'), el('span', {}, p.Email || '—'),
            el('span', { class: 'k' }, 'Level / XP'), el('span', {}, `${p.Level} / ${p.XP}`),
            el('span', { class: 'k' }, 'Created'), el('span', {}, fmtDate(p.CreatedAt)),
            el('span', { class: 'k' }, 'Last seen'), el('span', {}, fmtDate(p.LastSeenAt)),
            el('span', { class: 'k' }, 'Last IP'), el('span', {}, p.LastIp || '—'),
            el('span', { class: 'k' }, 'Online'), statusBadge(p),
        ));
}

function avatarSection(p) {
    const a = p.Avatar || {};
    const outfit = el('input', { value: a.OutfitSelections || '', placeholder: '<head-guid>,<torso>,<legs>,<feet>,<accessory>' });
    outfit.classList.add('mono');
    const hair = el('input', { value: a.HairColor || '', placeholder: '81_c6R0my0qK9hYM_0a7LQ' });
    hair.classList.add('mono');
    const skin = el('input', { value: a.SkinColor || '', placeholder: 'cl2EzJ4v6kW3g4Oo9ZQ3hA' });
    skin.classList.add('mono');
    const face = el('input', { value: a.FaceFeatures || '', placeholder: '{"eyeId":0,…}' });
    face.classList.add('mono');

    const hairSwatch = el('div', { class: 'swatch' });
    const skinSwatch = el('div', { class: 'swatch' });
    const updateSwatch = (sw, val) => { sw.style.background = parseColor(val) || ''; };
    updateSwatch(hairSwatch, hair.value);
    updateSwatch(skinSwatch, skin.value);
    hair.addEventListener('input', () => updateSwatch(hairSwatch, hair.value));
    skin.addEventListener('input', () => updateSwatch(skinSwatch, skin.value));

    return el('div', { class: 'section' },
        el('h3', {}, 'Avatar / outfit'),
        el('label', {}, 'Outfit selections (5 slot GUIDs, comma-separated)', outfit),
        el('label', {}, 'Hair color', el('div', { class: 'swatch-input' }, hair, hairSwatch)),
        el('label', {}, 'Skin color', el('div', { class: 'swatch-input' }, skin, skinSwatch)),
        el('label', {}, 'Face features (opaque JSON)', face),
        el('button', {
            class: 'primary',
            on: { click: async () => {
                try {
                    await api(`/players/${p.Id}/avatar`, {
                        method: 'POST',
                        body: {
                            OutfitSelections: outfit.value,
                            HairColor: hair.value,
                            SkinColor: skin.value,
                            FaceFeatures: face.value,
                        },
                    });
                    toast('Avatar saved', 'good');
                } catch (err) { toast(err.message, 'error'); }
            }},
        }, 'Save avatar'),
    );
}

// Convert "r,g,b,a" (0-1 floats) into a CSS background. Accepts "r,g,b"
// too. Returns null when unparseable so the swatch falls back to its
// checker-pattern default.
function parseColor(s) {
    if (!s) return null;
    const parts = s.split(',').map(t => parseFloat(t.trim()));
    if (parts.length < 3 || parts.some(isNaN)) return null;
    const [r, g, b, a = 1] = parts;
    const c = (n) => Math.round(Math.max(0, Math.min(1, n)) * 255);
    return `rgba(${c(r)}, ${c(g)}, ${c(b)}, ${a})`;
}

function currencySection(p) {
    const balances = (p.Balances || []).map(b =>
        el('div', { class: 'kvp' },
            el('span', { class: 'k' }, `Currency #${b.CurrencyType}`),
            el('span', {}, b.Balance.toLocaleString())));
    const type = el('input', { type: 'number', value: 2, placeholder: 'currency type' });
    const amount = el('input', { type: 'number', value: 100, placeholder: 'amount (negative deducts)' });
    const reason = el('input', { value: 'admin_grant', placeholder: 'reason' });
    return el('div', { class: 'section' },
        el('h3', {}, 'Currency'),
        ...(balances.length ? balances : [el('p', { class: 'muted' }, 'No balances yet.')]),
        el('div', { class: 'row' }, type, amount, reason,
            el('button', {
                class: 'primary',
                on: { click: async () => {
                    try {
                        const r = await api(`/players/${p.Id}/currency`, { method: 'POST', body: {
                            CurrencyType: parseInt(type.value, 10),
                            Amount: parseInt(amount.value, 10),
                            Reason: reason.value,
                        }});
                        toast(`New balance: ${r.balance}`, 'good');
                        openPlayer(p.Id);
                    } catch (err) { toast(err.message, 'error'); }
                }},
            }, 'Adjust currency'),
        ),
    );
}

function grantsSection(p) {
    const itemId = el('input', { value: '', placeholder: 'item GUID' });
    const qty = el('input', { type: 'number', value: 1, min: 1, placeholder: 'qty' });
    const xpAmount = el('input', { type: 'number', value: 100, placeholder: 'xp amount' });
    const xpReason = el('input', { value: 'admin_grant', placeholder: 'reason' });
    return el('div', { class: 'section' },
        el('h3', {}, 'Grants'),
        el('div', { class: 'row' }, itemId, qty,
            el('button', {
                class: 'primary',
                on: { click: async () => {
                    if (!itemId.value.trim()) return toast('Item GUID required', 'error');
                    try {
                        await api(`/players/${p.Id}/inventory/grant`, { method: 'POST', body: {
                            ItemId: itemId.value.trim(), Quantity: parseInt(qty.value, 10),
                        }});
                        toast(`Granted ${itemId.value} x${qty.value}`, 'good');
                    } catch (err) { toast(err.message, 'error'); }
                }},
            }, 'Grant item'),
        ),
        el('div', { class: 'row' }, xpAmount, xpReason,
            el('button', {
                on: { click: async () => {
                    try {
                        const r = await api(`/players/${p.Id}/xp`, { method: 'POST', body: {
                            Amount: parseInt(xpAmount.value, 10), Reason: xpReason.value,
                        }});
                        toast(`Now level ${r.level} (${r.xp} xp)`, 'good');
                        openPlayer(p.Id);
                    } catch (err) { toast(err.message, 'error'); }
                }},
            }, 'Grant XP'),
        ),
    );
}

function flagsSection(p) {
    const verified = el('input', { type: 'checkbox', checked: p.IsVerified });
    const dev = el('input', { type: 'checkbox', checked: p.IsDeveloper });
    const junior = el('input', { type: 'checkbox', checked: p.IsJunior });
    const display = el('input', { value: p.DisplayName });
    const checkbox = (lbl, input) => el('label', { class: 'checkbox-label' }, input, lbl);
    return el('div', { class: 'section' },
        el('h3', {}, 'Flags / display name'),
        el('div', { class: 'row' }, checkbox('Verified', verified), checkbox('Developer', dev), checkbox('Junior', junior),
            el('button', {
                on: { click: async () => {
                    try {
                        await api(`/players/${p.Id}/flags`, { method: 'POST', body: {
                            IsVerified: verified.checked, IsDeveloper: dev.checked, IsJunior: junior.checked,
                        }});
                        toast('Flags updated', 'good');
                    } catch (err) { toast(err.message, 'error'); }
                }},
            }, 'Save flags'),
        ),
        el('div', { class: 'row' }, display,
            el('button', {
                on: { click: async () => {
                    try {
                        await api(`/players/${p.Id}/displayName`, { method: 'POST', body: { DisplayName: display.value }});
                        toast('Display name updated', 'good');
                    } catch (err) { toast(err.message, 'error'); }
                }},
            }, 'Set display name'),
        ),
    );
}

function bansSection(p) {
    const days = el('input', { type: 'number', value: 7, min: 1, max: 3650 });
    const reason = el('input', { value: '', placeholder: 'reason' });
    const banned = p.BannedUntil && new Date(p.BannedUntil) > new Date();
    const banInfo = banned
        ? el('p', { class: 'muted' }, `Banned until ${fmtDate(p.BannedUntil)}`)
        : el('p', { class: 'muted' }, 'Not banned.');
    return el('div', { class: 'section' },
        el('h3', {}, 'Moderation'),
        banInfo,
        el('div', { class: 'row' }, days, reason,
            el('button', {
                class: 'danger',
                on: { click: async () => {
                    try {
                        await api(`/players/${p.Id}/ban`, { method: 'POST', body: {
                            DurationDays: parseInt(days.value, 10), Reason: reason.value,
                        }});
                        toast(`Banned ${p.Username}`, 'good');
                        openPlayer(p.Id);
                    } catch (err) { toast(err.message, 'error'); }
                }},
            }, 'Ban'),
            el('button', {
                on: { click: async () => {
                    try {
                        await api(`/players/${p.Id}/unban`, { method: 'POST', body: { Reason: reason.value }});
                        toast('Unbanned', 'good');
                        openPlayer(p.Id);
                    } catch (err) { toast(err.message, 'error'); }
                }},
            }, 'Unban'),
            el('button', {
                on: { click: async () => {
                    if (!confirm(`Promote ${p.Username} to admin?`)) return;
                    try {
                        await api(`/players/${p.Id}/promote`, { method: 'POST' });
                        toast('Promoted to admin', 'good');
                        openPlayer(p.Id);
                    } catch (err) { toast(err.message, 'error'); }
                }},
            }, 'Promote admin'),
            el('button', {
                on: { click: async () => {
                    if (!confirm(`Demote ${p.Username} from admin?`)) return;
                    try {
                        await api(`/players/${p.Id}/demote`, { method: 'POST' });
                        toast('Demoted', 'good');
                        openPlayer(p.Id);
                    } catch (err) { toast(err.message, 'error'); }
                }},
            }, 'Demote'),
        ),
    );
}

// ── Reports ──────────────────────────────────────────────────────────

$('refreshReports').addEventListener('click', loadReports);

async function loadReports() {
    try {
        const rows = await api('/reports');
        renderTable($('reportsTable'),
            ['ID', 'Reporter', 'Target', 'Category', 'Message', 'When', 'Actions'],
            rows, r => [
                r.Id, r.ReporterPlayerId, r.TargetPlayerId, r.ReportCategory,
                truncate(r.Message, 60), fmtDate(r.CreatedAt),
                el('div', { class: 'actions' },
                    el('button', { on: { click: () => resolveReport(r) }}, 'resolve'),
                    el('button', { on: { click: () => openPlayer(r.TargetPlayerId) }}, 'view target'),
                ),
            ]);
    } catch (err) { toast(err.message, 'error'); }
}

async function resolveReport(r) {
    const note = prompt(`Resolve report #${r.Id}? Note:`, '');
    if (note === null) return;
    try {
        await api(`/reports/${r.Id}/resolve`, { method: 'POST', body: { Note: note }});
        toast('Resolved', 'good');
        loadReports();
    } catch (err) { toast(err.message, 'error'); }
}

// ── IP bans ──────────────────────────────────────────────────────────

$('ipBanForm').addEventListener('submit', async (ev) => {
    ev.preventDefault();
    const cidr = $('cidrInput').value.trim();
    const reason = $('cidrReason').value.trim();
    const days = $('cidrDays').value;
    try {
        await api('/ipbans', { method: 'POST', body: {
            Cidr: cidr, Reason: reason, DurationDays: days ? parseInt(days, 10) : null,
        }});
        toast('IP ban added', 'good');
        $('cidrInput').value = '';
        $('cidrReason').value = '';
        $('cidrDays').value = '';
        loadIpBans();
    } catch (err) { toast(err.message, 'error'); }
});

async function loadIpBans() {
    try {
        const rows = await api('/ipbans');
        renderTable($('ipBansTable'),
            ['ID', 'CIDR', 'Reason', 'Banned at', 'Until', 'Actions'],
            rows, r => [
                r.Id, r.Cidr, r.Reason || '—', fmtDate(r.BannedAt),
                r.Until ? fmtDate(r.Until) : 'forever',
                el('div', { class: 'actions' },
                    el('button', { class: 'danger', on: { click: () => removeIpBan(r) }}, 'remove')),
            ]);
    } catch (err) { toast(err.message, 'error'); }
}

async function removeIpBan(r) {
    if (!confirm(`Remove ban on ${r.Cidr}?`)) return;
    try {
        await api(`/ipbans/${r.Id}`, { method: 'DELETE' });
        toast('Removed', 'good');
        loadIpBans();
    } catch (err) { toast(err.message, 'error'); }
}

// ── Audit ────────────────────────────────────────────────────────────

$('refreshAudit').addEventListener('click', loadAudit);

async function loadAudit() {
    try {
        const rows = await api('/audit');
        renderTable($('auditTable'),
            ['When', 'Admin', 'Action', 'Target', 'Reason'],
            rows, r => [fmtDate(r.Timestamp), r.AdminPlayerId, r.Action, `${r.TargetType}:${r.TargetId}`, truncate(r.Reason, 80)]);
    } catch (err) { toast(err.message, 'error'); }
}

// ── Broadcast ────────────────────────────────────────────────────────

$('broadcastForm').addEventListener('submit', async (ev) => {
    ev.preventDefault();
    const msg = $('broadcastMessage').value.trim();
    if (!msg) return;
    try {
        await api('/broadcast', { method: 'POST', body: { Message: msg }});
        toast('Broadcast sent', 'good');
        $('broadcastMessage').value = '';
    } catch (err) { toast(err.message, 'error'); }
});

// ── Import room ─────────────────────────────────────────────────────
//
// Pairs every uploaded .room file with a scene-folder name. Folder picker
// (webkitdirectory) auto-fills folders via File.webkitRelativePath. The
// individual-file picker fallback derives the scene name from the file
// stem (everything before .room) so single-file uploads still work.

const importState = {
    // Each entry: { file, sceneName }
    entries: [],
    // Cached player list for the owner dropdown — fetched once when the
    // tab opens, re-filtered client-side as the user types.
    players: null,
};

async function loadImportOwners(force = false) {
    // Re-fetch on every tab switch unless we have a populated cache —
    // that way a transient network blip is recovered the next time the
    // user opens the tab. (Old code cached an empty [] on failure and
    // never retried.)
    if (!force && Array.isArray(importState.players) && importState.players.length > 0) {
        renderOwnerDropdown('');
        return;
    }
    importState.ownersError = null;
    importState.players = null;
    renderOwnerDropdown('');  // shows "loading…"
    try {
        // 200 is the server-side max page size for /admin/v1/players.
        const list = await api('/players?take=200');
        importState.players = Array.isArray(list) ? list : [];
    } catch (err) {
        importState.ownersError = err.message || String(err);
        importState.players = [];
        toast(`Couldn't load player list: ${importState.ownersError}`, 'error');
    }
    renderOwnerDropdown('');
}

function renderOwnerDropdown(filter) {
    const sel = $('importOwner');
    const f = (filter || '').trim().toLowerCase();
    const players = importState.players;
    const matched = (players || []).filter(p =>
        !f ||
        (p.Username || '').toLowerCase().includes(f) ||
        (p.DisplayName || '').toLowerCase().includes(f) ||
        String(p.Id).includes(f)
    );
    // Preserve current selection if still in the filtered list.
    const current = sel.value;
    sel.innerHTML = '';
    let placeholder;
    if (importState.ownersError) placeholder = `— failed: ${importState.ownersError} —`;
    else if (players === null) placeholder = '— loading accounts… —';
    else if (players.length === 0) placeholder = '— no accounts in DB —';
    else if (matched.length === 0) placeholder = '— no matches —';
    else placeholder = `— pick an account (${matched.length}) —`;
    sel.append(el('option', { value: '' }, placeholder));
    for (const p of matched) {
        const label = `${p.DisplayName || p.Username} (${p.Username}, id=${p.Id})${p.IsAdmin ? ' [admin]' : ''}`;
        sel.append(el('option', { value: String(p.Id) }, label));
    }
    if (matched.some(p => String(p.Id) === current)) sel.value = current;
}

function refreshImportPreview() {
    const summary = $('importFileSummary');
    const table = $('importPreviewTable');
    const tbody = table.querySelector('tbody');
    tbody.innerHTML = '';
    if (importState.entries.length === 0) {
        summary.textContent = 'No files selected.';
        table.hidden = true;
        $('importSubmit').disabled = true;
        return;
    }
    for (const e of importState.entries) {
        const tr = document.createElement('tr');
        tr.append(
            el('td', { class: 'mono' }, e.sceneName),
            el('td', {}, e.file.name),
            el('td', { style: 'text-align:right' }, e.file.size.toLocaleString()),
        );
        tbody.append(tr);
    }
    summary.textContent = `${importState.entries.length} scene(s) staged, ${importState.entries.reduce((a, e) => a + e.file.size, 0).toLocaleString()} bytes total.`;
    table.hidden = false;
    $('importSubmit').disabled = false;
}

function deriveSceneName(file) {
    // webkitRelativePath is "FolderRoot/SceneName/blob.room" when picked
    // via the folder picker. Use the second-to-last segment.
    const rel = file.webkitRelativePath || '';
    if (rel) {
        const parts = rel.split('/').filter(Boolean);
        if (parts.length >= 2) return parts[parts.length - 2];
    }
    // Fallback for individual-file picker: strip ".room" from name.
    return file.name.replace(/\.room$/i, '');
}

function setImportFiles(fileList) {
    importState.entries = Array.from(fileList)
        .filter(f => f.name.toLowerCase().endsWith('.room'))
        .map(f => ({ file: f, sceneName: deriveSceneName(f) }));
    refreshImportPreview();
}

$('importFolder').addEventListener('change', (ev) => setImportFiles(ev.target.files));
$('importFiles').addEventListener('change', (ev) => setImportFiles(ev.target.files));
$('importOwnerFilter').addEventListener('input', (ev) => renderOwnerDropdown(ev.target.value));

async function loadMirrorRooms() {
    const sel = $('mirrorHtrRoomSelect');
    sel.innerHTML = '<option value="">— loading rooms… —</option>';
    try {
        const rooms = await api('/rooms');
        sel.innerHTML = '';
        sel.append(el('option', { value: '' }, `— pick a room (${rooms.length}) —`));
        for (const r of rooms) {
            const tag = r.IsAGRoom ? 'AG' : (r.IsDormRoom ? 'dorm' : 'orig');
            sel.append(el('option', { value: String(r.Id) },
                `[${tag}] ${r.Name} — id ${r.Id}, ${r.BlobCount} blob${r.BlobCount === 1 ? '' : 's'}`));
        }
    } catch (err) {
        sel.innerHTML = `<option value="">— failed: ${err.message} —</option>`;
    }
}

$('mirrorHtrBtn').addEventListener('click', async () => {
    const roomId = parseInt($('mirrorHtrRoomSelect').value, 10);
    if (!roomId) { toast('Pick a room first', 'error'); return; }
    const status = $('mirrorHtrStatus');
    const btn = $('mirrorHtrBtn');
    btn.disabled = true;
    status.textContent = `Mirroring room ${roomId}…`;
    try {
        const r = await api(`/rooms/${roomId}/mirror-htr`, { method: 'POST' });
        status.textContent =
            `room=${r.roomId}  scannedBlobs=${r.scannedBlobs}  uniqueRefs=${r.uniqueRefs}\n` +
            `alreadyMirrored=${r.alreadyMirrored}  downloaded=${r.downloaded}  roomParseFailures=${r.roomParseFailures}\n` +
            (r.assetDownloadFailures && r.assetDownloadFailures.length
                ? `failures:\n  ${r.assetDownloadFailures.join('\n  ')}`
                : 'no download failures.');
        toast(`Mirrored: refs=${r.uniqueRefs}, new=${r.downloaded}, skipped=${r.alreadyMirrored}`, 'good');
    } catch (err) {
        toast(err.message, 'error');
        status.textContent = `Failed: ${err.message}`;
    } finally {
        btn.disabled = false;
    }
});

$('importRoomForm').addEventListener('submit', async (ev) => {
    ev.preventDefault();
    if (importState.entries.length === 0) {
        toast('Pick a folder or .room files first', 'error');
        return;
    }
    const name = $('importName').value.trim();
    if (!name) { toast('Room name required', 'error'); return; }
    const ownerId = $('importOwner').value;
    if (!ownerId) { toast('Pick an owner account', 'error'); return; }

    const status = $('importStatus');
    const submitBtn = $('importSubmit');
    submitBtn.disabled = true;
    status.textContent = `Uploading ${importState.entries.length} scene(s)…`;

    try {
        const fd = new FormData();
        fd.append('name', name);
        fd.append('creatorPlayerId', ownerId);
        const desc = $('importDesc').value.trim();
        if (desc) fd.append('description', desc);
        const entry = $('importEntry').value.trim();
        if (entry) fd.append('entryScene', entry);
        for (const e of importState.entries) {
            fd.append('files', e.file, e.file.name);
            fd.append('scenePaths', e.sceneName);
        }
        const result = await api('/rooms/import', { method: 'POST', body: fd });
        toast(`Imported "${result.roomName}" (id ${result.roomId}, ${result.sceneCount} scenes)`, 'good');
        const failNote = result.normalizationFailures && result.normalizationFailures.length
            ? ` <span class="error">⚠ ${result.normalizationFailures.length} blob(s) failed normalization (uploaded as-is): ${result.normalizationFailures.map(f => f.scene).join(', ')}</span>`
            : '';
        const htrNote = result.htrMirrorStarted
            ? ' <span class="muted">.htr asset mirror running in background — watch the server log for [htr-mirror] lines.</span>'
            : '';
        status.innerHTML = `<strong>Done.</strong> Room id <code>${result.roomId}</code>, entry <code>${result.entryScene}</code>, ` +
            `${result.sceneCount} scenes wired up (${result.normalizedSceneCount}/${result.sceneCount} round-tripped through 2020 schema).${failNote}${htrNote} ` +
            `Try <code>/goto room/${result.roomName}</code> in-game.`;
        importState.entries = [];
        $('importName').value = '';
        $('importDesc').value = '';
        $('importEntry').value = '';
        $('importFolder').value = '';
        $('importFiles').value = '';
        refreshImportPreview();
    } catch (err) {
        toast(err.message, 'error');
        status.textContent = `Failed: ${err.message}`;
        submitBtn.disabled = false;
    }
});

// ── Helpers ──────────────────────────────────────────────────────────

function renderTable(tableEl, headers, rows, mapRow) {
    const thead = el('thead', {}, el('tr', {}, ...headers.map(h => el('th', {}, h))));
    const tbody = el('tbody', {});
    if (!rows || rows.length === 0) {
        tbody.append(el('tr', {}, el('td', { colSpan: headers.length, class: 'empty' }, 'Nothing here yet.')));
    } else {
        for (const r of rows) {
            const cells = mapRow(r);
            tbody.append(el('tr', {}, ...cells.map((c, i) => {
                const td = el('td', {}, c);
                // Right-align action cells; mono-format ID columns.
                if (typeof c === 'object' && c?.classList?.contains?.('actions')) td.style.textAlign = 'right';
                if (i === 0) td.classList.add('mono');
                return td;
            })));
        }
    }
    tableEl.replaceChildren(thead, tbody);
}

function fmtDate(s) {
    if (!s) return '—';
    const d = new Date(s);
    if (isNaN(d)) return '—';
    return d.toLocaleString();
}

function truncate(s, n) {
    if (!s) return '';
    return s.length > n ? s.slice(0, n) + '…' : s;
}

// ── Store catalog ────────────────────────────────────────────────────

$('refreshStore').addEventListener('click', loadStore);
$('storeFilterStorefront').addEventListener('change', loadStore);
$('storeFilterCategory').addEventListener('change', loadStore);
$('newStoreItemBtn').addEventListener('click', () => openStoreItem(null));

document.querySelectorAll('[data-close-store]').forEach(b =>
    b.addEventListener('click', () => $('storeModal').hidden = true));
$('storeModal').addEventListener('click', (ev) => {
    if (ev.target.id === 'storeModal') $('storeModal').hidden = true;
});

async function loadStore() {
    try {
        const sf = $('storeFilterStorefront').value;
        const cat = $('storeFilterCategory').value;
        const params = new URLSearchParams();
        if (sf) params.set('storefront', sf);
        if (cat) params.set('category', cat);
        const path = '/storeitems' + (params.toString() ? '?' + params : '');
        const rows = await api(path);
        renderTable($('storeTable'),
            ['ID', 'Slug', 'Name', 'Cat', 'Storefront', 'Price', 'Active', 'Actions'],
            rows, r => [
                r.Id,
                r.Slug,
                r.DisplayName,
                r.Category,
                r.Storefront,
                `${r.Price.toLocaleString()} (${r.CurrencyType})`,
                r.IsActive
                    ? el('span', { class: 'badge online' }, 'active')
                    : el('span', { class: 'badge offline' }, 'inactive'),
                el('div', { class: 'actions' },
                    el('button', { on: { click: () => openStoreItem(r) }}, 'edit'),
                    el('button', { class: 'danger', on: { click: () => deleteStoreItem(r) }}, 'delete'),
                ),
            ]);
    } catch (err) { toast(err.message, 'error'); }
}

function openStoreItem(item) {
    const isNew = !item;
    $('storeModalTitle').textContent = isNew
        ? 'New store item'
        : `Edit: ${item.DisplayName} (#${item.Id})`;

    const slug = el('input', { value: item?.Slug || '', placeholder: 'unique slug, e.g. my-cool-hat' });
    if (!isNew) slug.disabled = true;
    const displayName = el('input', { value: item?.DisplayName || '', placeholder: 'shown in store' });
    const description = el('input', { value: item?.Description || '', placeholder: 'item description' });
    const category = el('select', {});
    for (const c of ['head','torso','legs','feet','accessory','hair','face','consumable','roomtemplate','emote']) {
        category.append(el('option', { value: c, selected: (item?.Category || 'accessory') === c }, c));
    }
    const storefront = el('select', {});
    const currentStorefront = item?.Storefront || 'main';
    if (!STORE_STOREFRONTS.includes(currentStorefront)) {
        storefront.append(el('option', { value: currentStorefront, selected: true }, currentStorefront));
    }
    for (const s of STORE_STOREFRONTS) {
        storefront.append(el('option', { value: s, selected: currentStorefront === s }, s));
    }
    const imageName = el('input', { value: item?.ImageName || '', placeholder: 'cdn filename' });
    const currencyType = el('input', { type: 'number', value: item?.CurrencyType ?? 2 });
    const price = el('input', { type: 'number', value: item?.Price ?? 100, min: 0 });
    const isActive = el('input', { type: 'checkbox', checked: item?.IsActive ?? true });
    const isLimitedTime = el('input', { type: 'checkbox', checked: item?.IsLimitedTime ?? false });
    const availableUntil = el('input', { type: 'datetime-local',
        value: item?.AvailableUntil ? new Date(item.AvailableUntil).toISOString().slice(0,16) : '' });

    const checkbox = (lbl, input) => el('label', { class: 'checkbox-label' }, input, lbl);

    $('storeModalBody').replaceChildren(
        el('label', {}, 'Slug', slug),
        el('label', {}, 'Display name', displayName),
        el('label', {}, 'Description', description),
        el('label', {}, 'Category', category),
        el('label', {}, 'Storefront', storefront),
        el('label', {}, 'Image filename (cdn.rec.net/<this>)', imageName),
        el('div', { class: 'row' },
            el('label', { style: 'flex: 1;' }, 'Currency type', currencyType),
            el('label', { style: 'flex: 1;' }, 'Price', price)),
        el('label', {}, 'Available until (optional)', availableUntil),
        el('div', { class: 'row' }, checkbox('Active', isActive), checkbox('Limited-time', isLimitedTime)),
        el('button', {
            class: 'primary',
            on: { click: async () => {
                const body = {
                    Slug: slug.value.trim(),
                    DisplayName: displayName.value.trim(),
                    Description: description.value,
                    Category: category.value,
                    Storefront: storefront.value.trim(),
                    ImageName: imageName.value.trim(),
                    CurrencyType: parseInt(currencyType.value, 10),
                    Price: parseInt(price.value, 10),
                    IsActive: isActive.checked,
                    IsLimitedTime: isLimitedTime.checked,
                    AvailableUntil: availableUntil.value ? new Date(availableUntil.value).toISOString() : null,
                };
                try {
                    if (isNew) {
                        await api('/storeitems', { method: 'POST', body });
                        toast('Item created', 'good');
                    } else {
                        await api(`/storeitems/${item.Id}`, { method: 'POST', body });
                        toast('Item updated', 'good');
                    }
                    $('storeModal').hidden = true;
                    loadStore();
                } catch (err) { toast(err.message, 'error'); }
            }},
        }, isNew ? 'Create' : 'Save'),
    );
    $('storeModal').hidden = false;
}

async function deleteStoreItem(r) {
    if (!confirm(`Delete ${r.Slug}? This is permanent — for soft-disable, edit and uncheck Active.`)) return;
    try {
        await api(`/storeitems/${r.Id}`, { method: 'DELETE' });
        toast('Deleted', 'good');
        loadStore();
    } catch (err) { toast(err.message, 'error'); }
}

// ── Player logs ─────────────────────────────────────────────────────
//
// Per-player request log (Redis-backed ring buffer on the server).
// Reuses the existing /admin/v1/players list to populate the dropdown
// so the operator doesn't need to know account ids by heart, then hits
// /admin/v1/players/{id}/logs?take=N to pull the most-recent entries.

const playerLogsState = {
    players: null,
    selectedId: null,
    autoTimer: null,
};

let playerLogsInited = false;
async function initPlayerLogs() {
    if (!playerLogsInited) {
        playerLogsInited = true;
        $('playerLogsSelect').addEventListener('change', () => {
            playerLogsState.selectedId = parseInt($('playerLogsSelect').value, 10) || null;
            if (playerLogsState.selectedId) loadPlayerLogs();
            else $('playerLogsTable').innerHTML = '';
        });
        $('refreshPlayerLogs').addEventListener('click', loadPlayerLogs);
        $('playerLogsFilter').addEventListener('input', renderPlayerLogsTable);
        $('playerLogsAuto').addEventListener('change', () => {
            if ($('playerLogsAuto').checked) {
                playerLogsState.autoTimer = setInterval(() => {
                    if (playerLogsState.selectedId) loadPlayerLogs(true);
                }, 5000);
            } else {
                clearInterval(playerLogsState.autoTimer);
                playerLogsState.autoTimer = null;
            }
        });
    }
    // Reuse the player list — same endpoint the Import Room dropdown uses.
    try {
        const list = await api('/players?take=200');
        playerLogsState.players = Array.isArray(list) ? list : [];
    } catch (err) {
        playerLogsState.players = [];
        toast(`Couldn't load player list: ${err.message}`, 'error');
    }
    const sel = $('playerLogsSelect');
    const current = sel.value;
    sel.innerHTML = '';
    sel.append(el('option', { value: '' },
        playerLogsState.players.length === 0
            ? '— no accounts in DB —'
            : `— pick an account (${playerLogsState.players.length}) —`));
    for (const p of playerLogsState.players) {
        sel.append(el('option', { value: String(p.Id) }, `${p.Username} (#${p.Id})`));
    }
    if (current && playerLogsState.players.some(p => String(p.Id) === current)) sel.value = current;
}

let playerLogsRecent = [];
async function loadPlayerLogs(silent = false) {
    if (!playerLogsState.selectedId) return;
    const take = Math.max(10, Math.min(500, parseInt($('playerLogsTake').value, 10) || 200));
    try {
        playerLogsRecent = await api(`/players/${playerLogsState.selectedId}/logs?take=${take}`);
        if (!Array.isArray(playerLogsRecent)) playerLogsRecent = [];
        renderPlayerLogsTable();
    } catch (err) {
        if (!silent) toast(err.message, 'error');
    }
}

function renderPlayerLogsTable() {
    const filter = ($('playerLogsFilter').value || '').trim().toLowerCase();
    const rows = filter
        ? playerLogsRecent.filter(e =>
            (e.path || '').toLowerCase().includes(filter) ||
            (e.method || '').toLowerCase().includes(filter) ||
            String(e.status).includes(filter) ||
            (e.host || '').toLowerCase().includes(filter))
        : playerLogsRecent;
    $('playerLogsSummary').textContent = filter
        ? `${rows.length} of ${playerLogsRecent.length} entries match "${filter}"`
        : `${rows.length} entries`;
    renderTable($('playerLogsTable'),
        ['When', 'Method', 'Host', 'Path', 'Status', 'Took', 'Resp'],
        rows,
        e => {
            const statusKind = e.status >= 500 ? 'bad' : e.status >= 400 ? 'warn' : '';
            return [
                fmtDate(e.ts),
                e.method,
                e.host,
                truncate((e.path || '') + (e.query || ''), 80),
                el('span', { class: statusKind }, String(e.status)),
                `${e.elapsedMs}ms`,
                truncate(e.respBody || '', 100),
            ];
        });
}

// ── Boot ─────────────────────────────────────────────────────────────

if (token()) showApp();
