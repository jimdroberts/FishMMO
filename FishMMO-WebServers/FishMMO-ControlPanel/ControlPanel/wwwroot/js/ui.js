/*
 * Rendering helpers shared by every view.
 *
 * The panel builds DOM with template strings, so `esc` is the single defence
 * against injecting untrusted text. Chat messages, account names, character
 * names and audit reasons are all player-supplied: every one of them goes
 * through `esc` on its way into markup.
 */

import * as qr from './qr.js';

/* ── Escaping ────────────────────────────────────────────────── */

const ESCAPES = { '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;', "'": '&#39;' };

export function esc(value) {
	if (value === null || value === undefined) return '';
	return String(value).replace(/[&<>"']/g, (c) => ESCAPES[c]);
}

/** Tagged template that escapes every interpolation. Arrays are joined. */
export function html(strings, ...values) {
	let out = strings[0];
	for (let i = 0; i < values.length; i++) {
		const v = values[i];
		out += Array.isArray(v) ? v.join('') : esc(v);
		out += strings[i + 1];
	}
	return out;
}

/** Marks an already-built HTML string as safe to interpolate. */
export function raw(value) {
	// Arrays bypass escaping in `html`, so a one-element array is the marker.
	return [value ?? ''];
}

/* ── Formatting ──────────────────────────────────────────────── */

export function num(value, digits = 0) {
	if (value === null || value === undefined || Number.isNaN(Number(value))) return '—';
	return Number(value).toLocaleString(undefined, {
		minimumFractionDigits: digits,
		maximumFractionDigits: digits,
	});
}

export function bytes(value) {
	if (!value && value !== 0) return '—';
	const units = ['B', 'KB', 'MB', 'GB', 'TB'];
	let v = Number(value);
	let i = 0;
	while (v >= 1024 && i < units.length - 1) {
		v /= 1024;
		i++;
	}
	return `${v.toFixed(v >= 100 || i === 0 ? 0 : 1)} ${units[i]}`;
}

/**
 * Parses a timestamp from the API, treating one with no zone designator as UTC.
 *
 * Every timestamp the backend sends is UTC — the columns are named for it — but a value
 * serialized from a `timestamp without time zone` column arrives as "2026-09-11T02:10:00"
 * with no designator, and JavaScript parses that as LOCAL. Not as an error: every date in
 * the panel silently shifts by the viewer's offset, which is the kind of wrong that reads
 * as a stale row rather than as a bug.
 *
 * The server now stamps the designator, so this is the belt to that pair of braces. It is
 * here rather than in each view because there is no view that wants the other behaviour.
 */
function parseUtc(iso) {
	if (typeof iso !== 'string') return new Date(iso);
	// A designator is a trailing Z, or an offset like +02:00 on the time part.
	return /[Zz]$|[+-]\d{2}:?\d{2}$/.test(iso) ? new Date(iso) : new Date(iso + 'Z');
}

export function ago(iso) {
	if (!iso) return '—';
	const seconds = Math.round((Date.now() - parseUtc(iso).getTime()) / 1000);
	if (seconds < 0) return inFuture(-seconds);
	if (seconds < 10) return 'just now';
	if (seconds < 60) return `${seconds}s ago`;
	if (seconds < 3600) return `${Math.floor(seconds / 60)}m ago`;
	if (seconds < 86400) return `${Math.floor(seconds / 3600)}h ago`;
	if (seconds < 2592000) return `${Math.floor(seconds / 86400)}d ago`;
	return parseUtc(iso).toLocaleDateString();
}

function inFuture(seconds) {
	if (seconds < 60) return `in ${seconds}s`;
	if (seconds < 3600) return `in ${Math.floor(seconds / 60)}m`;
	if (seconds < 86400) return `in ${Math.floor(seconds / 3600)}h`;
	return `in ${Math.floor(seconds / 86400)}d`;
}

export function duration(seconds) {
	if (seconds === null || seconds === undefined) return '—';
	const s = Math.max(0, Math.round(seconds));
	if (s < 60) return `${s}s`;
	const m = Math.floor(s / 60);
	if (m < 60) return `${m}m ${s % 60}s`;
	const h = Math.floor(m / 60);
	if (h < 24) return `${h}h ${m % 60}m`;
	return `${Math.floor(h / 24)}d ${h % 24}h`;
}

export function dateTime(iso) {
	if (!iso) return '—';
	const d = parseUtc(iso);
	return `${d.toLocaleDateString()} ${d.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' })}`;
}

export function shortTime(iso) {
	if (!iso) return '—';
	return parseUtc(iso).toLocaleTimeString([], { hour: '2-digit', minute: '2-digit', second: '2-digit' });
}

export function initials(name) {
	if (!name) return '?';
	return String(name).slice(0, 2);
}

/** Stable avatar tint from a name, so the same account always looks the same. */
export function avatarClass(name) {
	let hash = 0;
	for (let i = 0; i < String(name).length; i++) hash = (hash * 31 + String(name).charCodeAt(i)) | 0;
	const n = Math.abs(hash) % 5;
	return n === 0 ? '' : `avatar-${n + 1}`;
}

export const ACCESS_LEVELS = ['Banned', 'Player', 'GameMaster', 'Admin'];

export function levelName(level) {
	return ACCESS_LEVELS[level] ?? 'Unknown';
}

export function levelBadge(level) {
	const tone = level === 0 ? 'danger' : level === 3 ? 'accent' : level === 2 ? 'info' : '';
	return `<span class="badge${tone ? ' badge-' + tone : ''}">${esc(levelName(level))}</span>`;
}

/* ── Small components ────────────────────────────────────────── */

export function badge(text, tone = '') {
	return `<span class="badge${tone ? ' badge-' + tone : ''}">${esc(text)}</span>`;
}

export function statusBadge(text, tone = '', pulse = false) {
	return `<span class="badge${tone ? ' badge-' + tone : ''}"><span class="dot${pulse ? ' dot-pulse' : ''}"></span>${esc(text)}</span>`;
}

export function stat({ label, value, unit = '', note = '', noteTone = '' }) {
	return `
		<div class="stat">
			<div class="stat-label">${esc(label)}</div>
			<div class="stat-value">${esc(value)}${unit ? `<span class="stat-unit">${esc(unit)}</span>` : ''}</div>
			${note ? `<div class="stat-note${noteTone ? ' is-' + noteTone : ''}">${esc(note)}</div>` : ''}
		</div>`;
}

export function card({ title = '', sub = '', actions = '', body = '', foot = '', flush = false }) {
	return `
		<section class="card">
			${title || actions ? `
				<header class="card-head">
					<div class="card-head-text">
						<div class="card-title">${esc(title)}</div>
						${sub ? `<div class="card-sub">${esc(sub)}</div>` : ''}
					</div>
					${actions ? `<div class="card-head-actions">${actions}</div>` : ''}
				</header>` : ''}
			<div class="card-body${flush ? ' flush' : ''}">${body}</div>
			${foot ? `<footer class="card-foot">${foot}</footer>` : ''}
		</section>`;
}

export function banner(tone, title, text = '') {
	return `
		<div class="banner banner-${esc(tone)}">
			<span class="banner-icon">${icon(tone === 'ok' ? 'check-circle' : tone === 'danger' ? 'alert-octagon' : tone === 'warn' ? 'alert-triangle' : 'info')}</span>
			<div class="banner-body">
				<div class="banner-title">${esc(title)}</div>
				${text ? `<div class="muted small">${esc(text)}</div>` : ''}
			</div>
		</div>`;
}

export function empty(title, text = '', iconName = 'inbox') {
	return `
		<div class="empty">
			<div class="empty-icon">${icon(iconName, 28)}</div>
			<div class="empty-title">${esc(title)}</div>
			${text ? `<div class="small">${esc(text)}</div>` : ''}
		</div>`;
}

export function meter(value, max, tone) {
	const pct = max > 0 ? Math.min(100, (value / max) * 100) : 0;
	const auto = pct >= 90 ? 'danger' : pct >= 70 ? 'warn' : 'ok';
	return `
		<div class="meter-row">
			<div class="meter"><div class="meter-fill is-${esc(tone ?? auto)}" style="width:${pct.toFixed(1)}%"></div></div>
			<div class="meter-value">${pct.toFixed(0)}%</div>
		</div>`;
}

export function table({ columns, rows, emptyText = 'Nothing to show.', rowAttrs = null }) {
	if (!rows.length) return empty('No results', emptyText);
	const head = columns
		.map((c) => `<th${c.align === 'right' ? ' class="num"' : ''}${c.width ? ` style="width:${c.width}"` : ''}>${esc(c.label)}</th>`)
		.join('');
	const body = rows
		.map((row, i) => {
			const attrs = rowAttrs ? rowAttrs(row, i) : '';
			const cells = columns
				.map((c) => `<td${c.align === 'right' ? ' class="num"' : ''}>${c.cell(row, i)}</td>`)
				.join('');
			return `<tr ${attrs}>${cells}</tr>`;
		})
		.join('');
	return `<div class="table-wrap"><table class="data"><thead><tr>${head}</tr></thead><tbody>${body}</tbody></table></div>`;
}

export function pager(page, pageSize, totalCount, action) {
	const pages = Math.max(1, Math.ceil(totalCount / pageSize));
	if (pages <= 1) return '';
	const from = totalCount === 0 ? 0 : (page - 1) * pageSize + 1;
	const to = Math.min(totalCount, page * pageSize);
	return `
		<div class="row-between">
			<span class="small muted tnum">${from}–${to} of ${num(totalCount)}</span>
			<span class="btn-group">
				<button class="btn btn-sm" data-action="${esc(action)}" data-page="${page - 1}" ${page <= 1 ? 'disabled' : ''}>Previous</button>
				<button class="btn btn-sm" data-action="${esc(action)}" data-page="${page + 1}" ${page >= pages ? 'disabled' : ''}>Next</button>
			</span>
		</div>`;
}

/** Inline SVG sparkline. Renders in both themes because it uses currentColor. */
export function sparkline(values, { height = 40, tone = 'chart-1', fill = true } = {}) {
	if (!values || values.length < 2) return '';
	const min = Math.min(...values);
	const max = Math.max(...values);
	const span = max - min || 1;
	const step = 100 / (values.length - 1);
	const points = values.map((v, i) => [i * step, height - ((v - min) / span) * (height - 4) - 2]);
	const line = points.map(([x, y], i) => `${i === 0 ? 'M' : 'L'}${x.toFixed(2)},${y.toFixed(2)}`).join(' ');
	const area = `${line} L100,${height} L0,${height} Z`;
	const id = `sg${Math.random().toString(36).slice(2, 8)}`;
	return `
		<svg class="spark" viewBox="0 0 100 ${height}" preserveAspectRatio="none" role="img" aria-hidden="true" style="color:var(--${esc(tone)})">
			${fill ? `
				<defs>
					<linearGradient id="${id}" x1="0" y1="0" x2="0" y2="1">
						<stop offset="0%" stop-color="currentColor" stop-opacity="0.28" />
						<stop offset="100%" stop-color="currentColor" stop-opacity="0" />
					</linearGradient>
				</defs>
				<path d="${area}" fill="url(#${id})" />` : ''}
			<path d="${line}" fill="none" stroke="currentColor" stroke-width="1.6"
				vector-effect="non-scaling-stroke" stroke-linejoin="round" stroke-linecap="round" />
		</svg>`;
}

/* ── Icons ───────────────────────────────────────────────────── */

const ICONS = {
	gauge: 'M12 3a9 9 0 0 0-9 9 9 9 0 0 0 1.6 5.1h14.8A9 9 0 0 0 21 12a9 9 0 0 0-9-9Z M12 12l4-3',
	user: 'M12 12a4 4 0 1 0 0-8 4 4 0 0 0 0 8Z M4 21a8 8 0 0 1 16 0',
	users: 'M9 12a4 4 0 1 0 0-8 4 4 0 0 0 0 8Z M2 21a7 7 0 0 1 14 0 M17 4.5a4 4 0 0 1 0 7.5 M18 14a7 7 0 0 1 4 7',
	sword: 'M14.5 3.5 21 3l-.5 6.5-9 9-6-6 9-9Z M5 15l-2 2 4 4 2-2',
	shield: 'M12 3l8 3v6c0 5-3.5 8-8 9-4.5-1-8-4-8-9V6l8-3Z',
	chat: 'M21 12a8 8 0 0 1-8 8H7l-4 3v-5.5A8 8 0 0 1 13 4a8 8 0 0 1 8 8Z',
	server: 'M3 5h18v5H3z M3 14h18v5H3z M7 7.5h.01 M7 16.5h.01',
	activity: 'M3 12h4l3 8 4-16 3 8h4',
	terminal: 'M4 5l6 7-6 7 M13 19h7',
	list: 'M8 6h13 M8 12h13 M8 18h13 M3 6h.01 M3 12h.01 M3 18h.01',
	key: 'M15 7a4 4 0 1 1-3.5 6L8 16.5 5.5 14 3 16.5 5 19l1.5-1.5M14 8h.01',
	heart: 'M12 20s-7-4.4-7-9a4 4 0 0 1 7-2.6A4 4 0 0 1 19 11c0 4.6-7 9-7 9Z',
	'alert-triangle': 'M12 4 2.5 20h19L12 4Z M12 10v4 M12 17.5h.01',
	'alert-octagon': 'M8 3h8l5 5v8l-5 5H8l-5-5V8l5-5Z M12 8v4 M12 16h.01',
	'check-circle': 'M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18Z M8.5 12l2.5 2.5 4.5-5',
	info: 'M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18Z M12 11v5 M12 8h.01',
	inbox: 'M3 13h5l1 3h6l1-3h5 M4 13 6 5h12l2 8v6H4v-6Z',
	search: 'M11 18a7 7 0 1 0 0-14 7 7 0 0 0 0 14Z M21 21l-4.5-4.5',
	menu: 'M3 6h18 M3 12h18 M3 18h18',
	sun: 'M12 17a5 5 0 1 0 0-10 5 5 0 0 0 0 10Z M12 2v2 M12 20v2 M2 12h2 M20 12h2 M4.9 4.9l1.4 1.4 M17.7 17.7l1.4 1.4 M4.9 19.1l1.4-1.4 M17.7 6.3l1.4-1.4',
	moon: 'M20 14.5A8.5 8.5 0 0 1 9.5 4a8.5 8.5 0 1 0 10.5 10.5Z',
	logout: 'M14 4h4a2 2 0 0 1 2 2v12a2 2 0 0 1-2 2h-4 M9 16l-4-4 4-4 M5 12h10',
	refresh: 'M20 11a8 8 0 0 0-14-4.5L3 9 M4 13a8 8 0 0 0 14 4.5L21 15 M3 5v4h4 M21 19v-4h-4',
	x: 'M6 6l12 12 M18 6 6 18',
	play: 'M7 4l12 8-12 8V4Z',
	stop: 'M6 6h12v12H6z',
	power: 'M12 3v9 M6.5 6.5a8 8 0 1 0 11 0',
	lock: 'M6 11h12v9H6z M9 11V8a3 3 0 0 1 6 0v3',
	unlock: 'M6 11h12v9H6z M9 11V8a3 3 0 0 1 5.7-1.4',
	clock: 'M12 21a9 9 0 1 0 0-18 9 9 0 0 0 0 18Z M12 7v5l3 2',
	edit: 'M4 20h4L19 9l-4-4L4 16v4Z M14.5 5.5l4 4',
	trash: 'M4 7h16 M9 7V5h6v2 M6 7l1 13h10l1-13',
	download: 'M12 4v11 M8 12l4 4 4-4 M4 20h16',
	copy: 'M9 9h11v11H9z M5 15V4h11',
	database: 'M12 3c4.4 0 8 1.3 8 3s-3.6 3-8 3-8-1.3-8-3 3.6-3 8-3Z M4 6v12c0 1.7 3.6 3 8 3s8-1.3 8-3V6 M4 12c0 1.7 3.6 3 8 3s8-1.3 8-3',
	home: 'M4 11l8-7 8 7 M6 10v10h12V10',
	flag: 'M5 21V4h13l-2.5 4L18 12H5',
	bell: 'M18 15V10a6 6 0 1 0-12 0v5l-2 3h16l-2-3Z M10 21h4',
	mail: 'M3 6h18v12H3z M3 7l9 6 9-6',
};

export function icon(name, size = 16) {
	const path = ICONS[name] ?? ICONS.info;
	const d = path.split(' M').map((p, i) => (i === 0 ? p : 'M' + p));
	return `<svg width="${size}" height="${size}" viewBox="0 0 24 24" fill="none" stroke="currentColor"
		stroke-width="1.7" stroke-linecap="round" stroke-linejoin="round" aria-hidden="true">
		${d.map((p) => `<path d="${p}" />`).join('')}
	</svg>`;
}

/* ── Toasts ──────────────────────────────────────────────────── */

let toastHost = null;

export function toast(title, text = '', tone = 'ok', ms = 4200) {
	if (!toastHost) {
		toastHost = document.createElement('div');
		toastHost.className = 'toasts';
		toastHost.setAttribute('role', 'status');
		toastHost.setAttribute('aria-live', 'polite');
		document.body.appendChild(toastHost);
	}
	const el = document.createElement('div');
	el.className = `toast is-${tone}`;
	el.innerHTML = `
		<span style="color:var(--${tone === 'ok' ? 'ok' : tone === 'danger' ? 'danger' : tone === 'warn' ? 'warn' : 'accent'})">
			${icon(tone === 'ok' ? 'check-circle' : tone === 'danger' ? 'alert-octagon' : 'alert-triangle')}
		</span>
		<div class="toast-body">
			<div class="toast-title">${esc(title)}</div>
			${text ? `<div class="toast-text">${esc(text)}</div>` : ''}
		</div>`;
	toastHost.appendChild(el);
	setTimeout(() => el.remove(), ms);
}

/* ── Modal ───────────────────────────────────────────────────── */

let openModal = null;

/**
 * Opens a modal and resolves with the result of `onSubmit`, or null if the
 * operator dismissed it. `onSubmit` receives the modal's form values.
 */
export function modal({ title, sub = '', body, confirmLabel = 'Confirm', confirmTone = 'primary', wide = false, onSubmit, onReady }) {
	closeModal();
	return new Promise((resolve) => {
		const scrim = document.createElement('div');
		scrim.className = 'modal-scrim';
		scrim.innerHTML = `
			<div class="modal${wide ? ' is-wide' : ''}" role="dialog" aria-modal="true" aria-label="${esc(title)}">
				<header class="modal-head">
					<div>
						<div class="modal-title">${esc(title)}</div>
						${sub ? `<div class="modal-sub">${esc(sub)}</div>` : ''}
					</div>
					<button class="btn btn-ghost btn-icon" data-close aria-label="Close">${icon('x')}</button>
				</header>
				<form class="modal-body">${body}</form>
				<footer class="modal-foot">
					<button class="btn" data-close>Cancel</button>
					<button class="btn btn-${esc(confirmTone)}" data-confirm>${esc(confirmLabel)}</button>
				</footer>
			</div>`;
		document.body.appendChild(scrim);
		openModal = scrim;

		const form = scrim.querySelector('form');
		const confirm = scrim.querySelector('[data-confirm]');

		const finish = (value) => {
			scrim.remove();
			openModal = null;
			document.removeEventListener('keydown', onKey);
			resolve(value);
		};

		const onKey = (e) => {
			if (e.key === 'Escape') finish(null);
		};
		document.addEventListener('keydown', onKey);

		scrim.addEventListener('click', (e) => {
			if (e.target === scrim || e.target.closest('[data-close]')) finish(null);
		});

		const submit = async () => {
			const values = Object.fromEntries(new FormData(form).entries());
			if (!onSubmit) return finish(values);
			confirm.disabled = true;
			const original = confirm.textContent;
			confirm.textContent = 'Working…';
			try {
				const result = await onSubmit(values, form);
				if (result === false) {
					confirm.disabled = false;
					confirm.textContent = original;
					return;
				}
				finish(result ?? values);
			} catch (err) {
				confirm.disabled = false;
				confirm.textContent = original;
				let box = form.querySelector('.modal-error');
				if (!box) {
					box = document.createElement('div');
					box.className = 'modal-error';
					form.prepend(box);
				}
				box.innerHTML = banner('danger', err.message || 'That did not work.');
			}
		};

		confirm.addEventListener('click', submit);
		form.addEventListener('submit', (e) => {
			e.preventDefault();
			submit();
		});

		// Anything the dialog's own markup needs wired up, wired up now that it is in the DOM.
		onReady?.(form);

		const first = form.querySelector('input, select, textarea');
		if (first) setTimeout(() => first.focus(), 30);
		else setTimeout(() => confirm.focus(), 30);
	});
}

export function closeModal() {
	if (openModal) {
		openModal.remove();
		openModal = null;
	}
}

/**
 * The destructive-action gate from CONTROL_PANEL_DESIGN.md §13.4: type the
 * target's name, and give a reason that lands in the audit log.
 */
export function confirmDestructive({ title, sub, targetName, confirmLabel, extraFields = '', onSubmit }) {
	return modal({
		title,
		sub,
		confirmLabel,
		confirmTone: 'danger',
		body: `
			${extraFields}
			<div class="field">
				<label for="cd-name">Type <code>${esc(targetName)}</code> to confirm</label>
				<input id="cd-name" name="confirmName" autocomplete="off" spellcheck="false" required />
			</div>
			<div class="field">
				<label for="cd-reason">Reason (recorded in the audit log)</label>
				<textarea id="cd-reason" name="reason" required placeholder="Why is this being done?"></textarea>
			</div>`,
		onSubmit: async (values, form) => {
			if (values.confirmName !== targetName) {
				const input = form.querySelector('#cd-name');
				input.setCustomValidity('That does not match.');
				input.reportValidity();
				setTimeout(() => input.setCustomValidity(''), 10);
				return false;
			}
			if (!String(values.reason).trim()) return false;
			return onSubmit(values);
		},
	});
}

/* ── Misc DOM ────────────────────────────────────────────────── */

export function loading(text = 'Loading…') {
	return `
		<div class="stack">
			<div class="skeleton" style="width:38%"></div>
			<div class="skeleton" style="width:96%;height:120px"></div>
			<div class="skeleton" style="width:70%"></div>
			<div class="visually-hidden">${esc(text)}</div>
		</div>`;
}

export function copyToClipboard(text) {
	if (navigator.clipboard?.writeText) {
		navigator.clipboard.writeText(text).then(
			() => toast('Copied', '', 'ok', 1800),
			() => toast('Could not copy', 'Your browser refused clipboard access.', 'warn'),
		);
		return;
	}
	const ta = document.createElement('textarea');
	ta.value = text;
	ta.setAttribute('readonly', '');
	ta.style.position = 'fixed';
	ta.style.opacity = '0';
	document.body.appendChild(ta);
	ta.select();
	try {
		document.execCommand('copy');
		toast('Copied', '', 'ok', 1800);
	} catch {
		toast('Could not copy', '', 'warn');
	}
	ta.remove();
}

/**
 * Renders `text` as a real, scannable QR code.
 *
 * This used to be a decorative block pattern, which is worse than nothing: it looks like an
 * enrolment code and enrols nothing. Two-factor is mandatory on every account, so the one
 * moment the secret is shown has to work.
 */
export function qrCode(text, options = {}) {
	try {
		return qr.svg(text, options);
	} catch (err) {
		// Better a visible failure with the key still readable beside it than a picture that
		// cannot be scanned.
		return banner('warn', 'This code could not be drawn', 'Enter the key by hand instead.');
	}
}

/**
 * Offers `text` to the browser as a file download.
 *
 * Recovery codes are shown once. Copy alone is a poor way to keep them — a clipboard is
 * overwritten by the next thing copied — so there is a file too.
 */
export function downloadText(filename, text) {
	const url = URL.createObjectURL(new Blob([text], { type: 'text/plain;charset=utf-8' }));
	const a = document.createElement('a');
	a.href = url;
	a.download = filename;
	document.body.appendChild(a);
	a.click();
	a.remove();
	// Revoke on the next turn: revoking synchronously can beat the download starting.
	setTimeout(() => URL.revokeObjectURL(url), 1000);
}
