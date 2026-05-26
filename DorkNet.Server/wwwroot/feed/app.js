// DorkNet feed — public photo gallery. Reads from same-origin
// /api/photos/v1/* (PhotosController is dual-hosted on api.rec.net and
// feed.rec.net so no CORS dance). Hash routing keeps it a single page:
//   #/        → feed grid
//   #/p/123   → photo detail
//   #/u/456   → uploader profile

const API = '/api/photos/v1';

const $ = (id) => document.getElementById(id);
const el = (tag, attrs, ...children) => {
    const e = document.createElement(tag);
    for (const k in attrs) {
        if (k === 'class') e.className = attrs[k];
        else if (k === 'on') for (const ev in attrs.on) e.addEventListener(ev, attrs.on[ev]);
        else if (k === 'html') e.innerHTML = attrs[k];
        else if (k === 'hidden' && attrs[k]) e.hidden = true;
        else e[k] = attrs[k];
    }
    for (const c of children.flat()) if (c != null) e.append(c?.nodeType ? c : document.createTextNode(String(c)));
    return e;
};

async function api(path) {
    const res = await fetch(API + path);
    if (!res.ok) throw new Error(`${res.status} ${res.statusText}`);
    return res.json();
}

function toast(msg, kind = '') {
    const t = $('toast');
    t.textContent = msg;
    t.className = 'toast ' + kind;
    t.hidden = false;
    clearTimeout(toast._h);
    toast._h = setTimeout(() => t.hidden = true, 3000);
}

function fmtDate(iso) {
    if (!iso) return '';
    const d = new Date(iso);
    if (isNaN(d)) return '';
    const now = new Date();
    const diff = (now - d) / 1000;
    if (diff < 60) return 'just now';
    if (diff < 3600) return `${Math.floor(diff / 60)}m ago`;
    if (diff < 86400) return `${Math.floor(diff / 3600)}h ago`;
    if (diff < 604800) return `${Math.floor(diff / 86400)}d ago`;
    return d.toLocaleDateString();
}

function initials(name) {
    if (!name) return '?';
    const parts = name.split(/[\s_]/).filter(Boolean);
    return (parts[0]?.[0] || '') + (parts[1]?.[0] || '');
}

function showView(name) {
    document.querySelectorAll('.view').forEach(v => v.hidden = v.id !== `${name}-view`);
    document.querySelectorAll('.nav-link').forEach(l => l.classList.toggle('active', l.dataset.route === 'feed' && name === 'feed'));
    window.scrollTo(0, 0);
}

// ── Router ───────────────────────────────────────────────────────────

window.addEventListener('hashchange', route);
window.addEventListener('DOMContentLoaded', route);

function goFeed() { location.hash = '#/'; }

function route() {
    const h = location.hash.slice(1) || '/';
    const photoMatch = h.match(/^\/p\/(\d+)$/);
    const userMatch = h.match(/^\/u\/(\d+)$/);
    if (photoMatch) renderPhoto(parseInt(photoMatch[1], 10));
    else if (userMatch) renderProfile(parseInt(userMatch[1], 10));
    else renderFeed();
}

// ── Feed view ────────────────────────────────────────────────────────

let feedSkip = 0;
const PAGE = 24;

async function renderFeed() {
    showView('feed');
    feedSkip = 0;
    $('grid').replaceChildren(...skeletonCards(6));
    $('empty').hidden = true;
    $('loadMore').hidden = true;
    try {
        const photos = await api(`/feed?take=${PAGE}&skip=0`);
        $('grid').replaceChildren();
        if (photos.length === 0) {
            $('empty').hidden = false;
            return;
        }
        for (const p of photos) $('grid').append(photoCard(p));
        feedSkip = photos.length;
        if (photos.length === PAGE) $('loadMore').hidden = false;
    } catch (err) {
        $('grid').replaceChildren();
        toast(err.message, 'error');
    }
}

$('loadMoreBtn').addEventListener('click', async () => {
    try {
        const photos = await api(`/feed?take=${PAGE}&skip=${feedSkip}`);
        for (const p of photos) $('grid').append(photoCard(p));
        feedSkip += photos.length;
        if (photos.length < PAGE) $('loadMore').hidden = true;
    } catch (err) { toast(err.message, 'error'); }
});

function skeletonCards(n) {
    return Array.from({ length: n }, () => {
        const card = el('div', { class: 'photo-card' });
        const h = 180 + Math.floor(Math.random() * 220);
        card.append(el('div', { class: 'skeleton', style: `height: ${h}px;` }));
        card.append(el('div', { class: 'photo-meta' },
            el('div', { class: 'skeleton', style: 'height: 14px; width: 60%; margin-bottom: 8px;' }),
            el('div', { class: 'skeleton', style: 'height: 12px; width: 90%;' }),
        ));
        return card;
    });
}

function photoCard(p) {
    const card = el('a', {
        class: 'photo-card',
        href: `#/p/${p.Id}`,
    });
    const img = el('img', {
        class: 'photo-img',
        src: p.ImageUrl,
        alt: p.Caption || `Photo by ${p.UploaderDisplayName}`,
        loading: 'lazy',
    });
    img.addEventListener('error', () => {
        img.style.display = 'none';
    });
    card.append(img);
    card.append(el('div', { class: 'photo-meta' },
        el('div', { class: 'uploader' },
            el('div', { class: 'uploader-avatar' }, initials(p.UploaderDisplayName)),
            el('span', {}, p.UploaderDisplayName),
        ),
        p.Caption ? el('div', { class: 'caption' }, p.Caption) : null,
        el('div', { class: 'photo-foot' },
            el('span', { class: 'cheers' }, `♥ ${p.CheerCount.toLocaleString()}`),
            p.RoomName ? el('span', { class: 'room' }, `^${p.RoomName}`) : el('span', { class: 'time' }, fmtDate(p.CreatedAt)),
        ),
    ));
    return card;
}

// ── Photo detail view ────────────────────────────────────────────────

async function renderPhoto(id) {
    showView('photo');
    $('photoDetail').replaceChildren(el('div', { class: 'skeleton', style: 'height: 480px;' }));
    try {
        const p = await api(`/${id}`);
        const tagged = (p.TaggedPlayerIds || []).slice(0, 8);
        $('photoDetail').replaceChildren(el('div', { class: 'photo-detail' },
            el('div', { class: 'image-wrap' },
                el('img', { src: p.ImageUrl, alt: p.Caption || `Photo #${p.Id}` })),
            el('div', { class: 'info' },
                el('h2', {}, `Photo #${p.Id}`),
                el('div', { class: 'caption-full' }, p.Caption || ''),
                el('div', { class: 'meta-row' },
                    el('span', { class: 'k' }, 'By'),
                    el('a', { class: 'uploader-link', href: `#/u/${p.UploaderPlayerId}` },
                        el('div', { class: 'uploader-avatar' }, initials(p.UploaderDisplayName)),
                        el('span', {}, p.UploaderDisplayName),
                    ),
                ),
                p.RoomName
                    ? el('div', { class: 'meta-row' },
                        el('span', { class: 'k' }, 'In'),
                        el('span', { class: 'room-pill' }, `^${p.RoomName}`))
                    : null,
                el('div', { class: 'meta-row' },
                    el('span', { class: 'k' }, 'Posted'),
                    el('span', {}, fmtDate(p.CreatedAt) + ' (' + new Date(p.CreatedAt).toLocaleString() + ')'),
                ),
                tagged.length
                    ? el('div', { class: 'meta-row' },
                        el('span', { class: 'k' }, 'Tagged'),
                        el('span', {}, tagged.length + ' player' + (tagged.length === 1 ? '' : 's')))
                    : null,
                el('div', { class: 'stats' },
                    el('div', { class: 'stat-pill cheers' },
                        el('div', { class: 'v' }, p.CheerCount.toLocaleString()),
                        el('div', { class: 'l' }, 'cheers')),
                    el('div', { class: 'stat-pill' },
                        el('div', { class: 'v' }, p.ViewCount.toLocaleString()),
                        el('div', { class: 'l' }, 'views')),
                ),
            ),
        ));
    } catch (err) {
        $('photoDetail').replaceChildren(el('div', { class: 'empty' },
            el('p', {}, 'Photo not found.')));
    }
}

$('backBtn').addEventListener('click', goFeed);

// ── Profile view ─────────────────────────────────────────────────────

async function renderProfile(playerId) {
    showView('profile');
    $('profileDetail').replaceChildren(el('div', { class: 'skeleton', style: 'height: 100px;' }));
    try {
        const photos = await api(`/by/${playerId}?take=60`);
        const uploaderName = photos[0]?.UploaderDisplayName || `Player ${playerId}`;
        const uploaderUsername = photos[0]?.UploaderUsername || '';
        $('profileDetail').replaceChildren(
            el('div', { class: 'profile-header' },
                el('div', { class: 'avatar' }, initials(uploaderName)),
                el('div', {},
                    el('h2', {}, uploaderName),
                    el('div', { class: 'username' },
                        uploaderUsername ? `@${uploaderUsername} · ` : '',
                        `${photos.length} photo${photos.length === 1 ? '' : 's'}`),
                ),
            ),
            (() => {
                const grid = el('div', { class: 'grid' });
                if (photos.length === 0)
                    grid.append(el('div', { class: 'empty' },
                        el('p', {}, 'No photos posted yet.')));
                else
                    for (const p of photos) grid.append(photoCard(p));
                return grid;
            })(),
        );
    } catch (err) {
        $('profileDetail').replaceChildren(el('div', { class: 'empty' },
            el('p', {}, 'Profile not found.')));
    }
}
