/*
 * Servers → Bandwidth. What every server process sent and received, from the rows each one
 * writes to the database once a minute.
 *
 * WHERE THE NUMBERS COME FROM, AND WHICH ONES ARE MEASURED.
 *
 *   - Game data: the FishNet messages each server handed to its transport and received from
 *     it, counted by the WebTransport socket. MEASURED.
 *   - UDP payload: everything msquic put into and took out of UDP datagrams in that process —
 *     QUIC headers, encryption tags, acknowledgements, retransmissions, padding, and the
 *     handshakes of connections that were refused or never finished. MEASURED. This is the
 *     layer the tables lead with.
 *   - On the wire: the UDP payload plus 28 bytes of IPv4 and UDP header per datagram. ESTIMATED,
 *     because no program can count IP headers; the server computes it as it reads and never
 *     stores it. The sent figure is an upper bound: msquic 2.5.9 over-counts the datagrams of a
 *     burst it sends in several batches (handshakes especially). It is drawn dashed, in its own
 *     colour, and every figure of it carries "≈" and "est." — it is never merged into a
 *     measured number.
 *
 * NO DATA IS NOT ZERO. A server with no rows in a window shows "no data"; a server that ran and
 * moved nothing shows 0. A chart with no points says so rather than drawing a flat line, and a
 * gap between points is drawn as a gap. Every name here was written by a server into its own
 * configuration, so it goes through `ui.esc` (markup) or `textContent` (the tooltip).
 *
 * Units: rates in megabits per second (10^6 bits), totals in gigabytes (10^9 bytes), because
 * hosts bill egress per GB. Some bill per GiB (2^30), which is 7.4% larger; the footnote says so.
 *
 * The page re-reads every minute through `api.auto`, which the audit log does not record; the
 * first load, the Refresh button and every change of range or scope go through `api` and are
 * recorded, as every staff read is.
 */

/* The servers write once a minute, so polling faster shows the same numbers back. */
const REFRESH_MS = 60_000;

const RANGES = [
	{ key: 'hour', label: 'Last hour', peak: 'Peak minute', unit: '1 minute' },
	{ key: 'day', label: 'Last 24 hours', peak: 'Peak 5 minutes', unit: '5 minutes' },
	{ key: 'month', label: 'Last 30 days', peak: 'Peak hour', unit: '1 hour' },
];

const KINDS = ['login', 'world', 'scene'];
const KIND_LABEL = { login: 'Login', world: 'World', scene: 'Scene' };

const APP_NOTE = 'Measured. The game\'s own FishNet messages, as handed to and received from the transport, before any QUIC overhead.';
const PAYLOAD_NOTE = 'Measured by msquic in the server process: QUIC headers, encryption, acknowledgements, retransmissions, padding and the handshakes of refused connections are all included.';
const SENT_WIRE_NOTE = 'Estimated: the measured UDP payload plus 28 bytes of IPv4 and UDP header per datagram. No program can count IP headers, so this is computed, not measured. msquic 2.5.9 over-counts the datagrams of a burst it sends in several batches (handshakes especially), so the sent estimate is an upper bound — a few hundred bytes per handshake.';
const RECV_WIRE_NOTE = 'Estimated: the measured UDP payload plus 28 bytes of IPv4 and UDP header per datagram. The received datagram count is exact, so this is exact up to IP options, which are not used.';

/* Three series per chart, in the panel's categorical order. The estimate has its own hue AND a
 * dash, so it can be told from the payload line it runs just above even where they touch. */
const SERIES = {
	sent: [
		{ key: 'udpSentBps', label: 'UDP payload', measure: 'measured', tone: 'chart-1', area: true, note: PAYLOAD_NOTE },
		{ key: 'wireSentBpsEstimated', label: 'On the wire', measure: 'estimated', tone: 'chart-3', dashed: true, note: SENT_WIRE_NOTE },
		{ key: 'appSentBps', label: 'Game data', measure: 'measured', tone: 'chart-2', note: APP_NOTE },
	],
	recv: [
		{ key: 'udpRecvBps', label: 'UDP payload', measure: 'measured', tone: 'chart-1', area: true, note: PAYLOAD_NOTE },
		{ key: 'wireRecvBpsEstimated', label: 'On the wire', measure: 'estimated', tone: 'chart-3', dashed: true, note: RECV_WIRE_NOTE },
		{ key: 'appRecvBps', label: 'Game data', measure: 'measured', tone: 'chart-2', note: APP_NOTE },
	],
};

/* The plot's own coordinate box. The SVG stretches to the plot element (lines keep their
 * width through vector-effect), and every piece of text is HTML beside it, so labels stay
 * crisp and legible at any width instead of scaling with the drawing. */
const VIEW_W = 1000;
const VIEW_H = 200;

/* ── Formatting ──────────────────────────────────────────────── */

function sig(v) {
	if (v === 0) return '0';
	if (v >= 100) return v.toFixed(0);
	if (v >= 10) return v.toFixed(1);
	if (v >= 1) return v.toFixed(2);
	if (v >= 0.01) return v.toFixed(3);
	if (v >= 0.0001) return v.toPrecision(2);
	return '< 0.0001';
}

function known(v) {
	return v !== null && v !== undefined && Number.isFinite(Number(v));
}

/** Bytes per second as megabits per second. Null in, null out: "no data" is the caller's to say. */
export function formatRate(bps) {
	if (!known(bps)) return null;
	return `${sig(Math.max(0, Number(bps)) * 8 / 1e6)} Mbps`;
}

/** Bytes as decimal gigabytes (terabytes past 1000 GB), the unit egress is billed in. */
export function formatTotal(bytes) {
	if (!known(bytes)) return null;
	const gb = Math.max(0, Number(bytes)) / 1e9;
	return gb >= 1000 ? `${sig(gb / 1000)} TB` : `${sig(gb)} GB`;
}

/* ── The view ────────────────────────────────────────────────── */

export async function render(host, ctx) {
	const { api, ui } = ctx;

	let poll = null;
	/* Registered before anything awaits: the router keeps one cleanup per view, and a timer
	 * left behind would keep polling into a page the operator has left. */
	ctx.onCleanup(() => clearInterval(poll));

	const query = ctx.route?.query ?? {};
	const state = {
		range: RANGES.some((r) => r.key === query.range) ? query.range : 'hour',
		kind: KINDS.includes(query.kind) ? query.kind : '',
		name: KINDS.includes(query.kind) && query.name ? String(query.name) : '',
		report: null,
		readAt: 0,
		refreshError: null,
	};

	/* Answers are applied only for the newest request, so a slow poll that lands after a range
	 * change cannot paint the old range over the new one. */
	let issued = 0;

	async function read(source = api) {
		const token = ++issued;
		const report = await source.getServerBandwidth({ range: state.range, kind: state.kind, name: state.name });
		if (token !== issued) return;
		state.report = report;
		state.readAt = Date.now();
		state.refreshError = null;
		paint();
	}

	/* A failed read keeps what is on screen, labelled as stale — more use mid-incident than an
	 * error page where the numbers were. */
	async function refresh(source = api) {
		/* A change the operator asked for holds the old frame, dimmed, until the new one lands —
		 * no skeleton, no jump. The minute's poll repaints in place without dimming. */
		if (source === api) host.querySelector('.bw-content')?.classList.add('is-loading');
		try {
			await read(source);
		} catch (err) {
			state.refreshError = err.message || 'Bandwidth could not be read.';
			if (state.report) paint();
		} finally {
			host.querySelector('.bw-content')?.classList.remove('is-loading');
		}
	}

	poll = setInterval(() => refresh(api.auto), REFRESH_MS);

	// The first read is allowed to throw: the router turns it into a visible failure.
	await read();

	function paint() {
		const r = state.report;
		const range = RANGES.find((x) => x.key === r.range) ?? RANGES[0];
		const servers = r.servers ?? [];
		const total = r.total ?? {};
		const scopeLabel = describeScope(state);

		host.innerHTML = `
			<div class="page-head">
				<div class="page-head-text">
					<h1>Bandwidth</h1>
					<p class="page-head-sub">
						What every server process sent and received, from the row each one writes once a minute.
						UDP payload and game data are measured by the servers themselves; "on the wire" adds the
						IP and UDP headers and is an estimate. Egress (sent) is what hosts bill. The page refreshes
						every ${REFRESH_MS / 1000} seconds.
					</p>
				</div>
				<div class="page-head-actions">
					<button class="btn" data-act="refresh">${ui.icon('refresh')} Refresh</button>
				</div>
			</div>

			<div class="bw-filters">
				<div class="tabs bw-range" role="group" aria-label="Range">
					${RANGES.map((x) => `
						<button type="button" class="tab${x.key === range.key ? ' is-active' : ''}" data-range="${x.key}"
							aria-pressed="${x.key === range.key}">${ui.esc(x.label)}</button>`).join('')}
				</div>
				<label class="bw-scope">
					<span class="small muted">Chart</span>
					<select id="bw-scope" aria-label="What the charts show">${scopeOptions(ui, servers, state)}</select>
				</label>
			</div>

			<div class="bw-content stack-lg">
				${state.refreshError
					? ui.banner('warn', 'This page is not refreshing',
						`${state.refreshError} What you are looking at was read at ${new Date(state.readAt).toLocaleTimeString()} and is not being updated.`)
					: ''}
				${r.rollupBehind
					? ui.banner('warn', 'The hourly rollup is behind',
						`The 30-day figures read hour rows up to ${r.latestRolledHourUtc ? ui.dateTime(r.latestRolledHourUtc) : 'none yet'} and minutes after a few hours ago, and the hours in between have not been rolled yet, so 30-day totals are short by them. The Control Panel rolls them every five minutes; if this stays, check its log for "Bandwidth rollup".`)
					: ''}

				${servers.length ? `
					<div class="grid grid-4">
						${ui.stat({
							label: 'Egress now',
							value: formatRate(total.current?.udpSentBps) ?? 'no data',
							note: total.current
								? `≈ ${formatRate(total.current.wireSentBpsEstimated)} on the wire (est.) · ${total.serversReporting} of ${total.servers} reporting`
								: 'No server has reported in the last few minutes',
							noteTone: total.current ? '' : 'warn',
						})}
						${ui.stat({
							label: 'Egress, last 24 hours',
							value: formatTotal(total.lastDay?.udpSentBytes) ?? 'no data',
							note: total.lastDay ? `≈ ${formatTotal(total.lastDay.wireSentBytesEstimated)} on the wire (est.)` : 'No rows in the last 24 hours',
						})}
						${ui.stat({
							label: 'Egress, last 30 days',
							value: formatTotal(total.last30Days?.udpSentBytes) ?? 'no data',
							note: total.last30Days ? `≈ ${formatTotal(total.last30Days.wireSentBytesEstimated)} on the wire (est.)` : 'No rows in 30 days',
						})}
						${ui.stat({
							label: 'Sessions now',
							value: known(total.current?.sessionsActive) ? ui.num(total.current.sessionsActive) : 'no data',
							note: 'WebTransport sessions on the servers reporting now',
						})}
					</div>

					${ui.card({
						title: `Sent (egress) · ${scopeLabel}`,
						sub: `${range.label}, one point per ${range.unit}. Hover or focus the chart for every figure at a moment.`,
						actions: state.kind ? `<button class="btn btn-sm" data-act="clear-scope">Show every server</button>` : '',
						body: chartBlock(ui, 'sent', r, range),
					})}

					${ui.card({
						title: `Received (ingress) · ${scopeLabel}`,
						sub: `${range.label}, one point per ${range.unit}.`,
						body: chartBlock(ui, 'recv', r, range),
					})}

					<details class="bw-details">
						<summary class="small">The charts' figures as a table</summary>
						<div class="bw-details-body"></div>
					</details>

					${ui.card({
						title: 'Tiers',
						sub: 'Each tier\'s servers added together. "Now" adds only the servers that reported in the last few minutes.',
						flush: true,
						body: tierTable(ui, r, range, state),
					})}

					${ui.card({
						title: 'Servers',
						sub: 'Every server with a row in the last 30 days. Choose a row to chart that server alone.',
						flush: true,
						body: serverTable(ui, r, range, state),
					})}

					<p class="small muted">
						Arrows: ↑ sent, ↓ received. Totals are decimal gigabytes (1 GB = 10⁹ bytes); a host that bills
						per GiB (2³⁰ bytes) counts 7.4% fewer units for the same traffic. Rates are megabits per second,
						each server's bytes over the time it was measured, added across servers. "Now" is a server's latest
						minute when that is under ${Math.round((r.currentFreshSeconds ?? 300) / 60)} minutes old. Minutes are kept
						${r.minuteRetentionDays ?? 14} days and hours ${r.hourRetentionMonths ?? 13} months; the 30-day figures run
						from whole hours.
					</p>`
				: ui.empty('No bandwidth has been recorded',
					'Every login, world and scene server writes one row a minute once it is running against a database that has the bandwidth tables. Nothing here means no server has written one in the last 30 days: none is running, none can reach the database, or the migration adding the tables has not been applied.',
					'chart')}
			</div>`;

		wire();
	}

	function wire() {
		host.querySelector('[data-act="refresh"]')?.addEventListener('click', () => refresh(api));
		host.querySelector('[data-act="clear-scope"]')?.addEventListener('click', () => setScope('', ''));
		for (const button of host.querySelectorAll('[data-range]')) {
			button.addEventListener('click', () => {
				if (state.range === button.dataset.range) return;
				state.range = button.dataset.range;
				refresh(api);
			});
		}
		host.querySelector('#bw-scope')?.addEventListener('change', (e) => {
			const [kind, name] = decodeScope(e.target.value);
			setScope(kind, name);
		});
		for (const row of host.querySelectorAll('tr[data-scope-kind]')) {
			const choose = () => {
				const kind = row.dataset.scopeKind;
				const name = row.dataset.scopeName ? decodeURIComponent(row.dataset.scopeName) : '';
				if (kind === state.kind && name === state.name) setScope('', '');
				else setScope(kind, name);
			};
			row.addEventListener('click', choose);
			row.addEventListener('keydown', (e) => {
				if (e.key === 'Enter' || e.key === ' ') {
					e.preventDefault();
					choose();
				}
			});
		}
		for (const chart of host.querySelectorAll('.bw-chart[data-chart]')) {
			wireChart(ui, chart, state.report, SERIES[chart.dataset.chart]);
		}
		const details = host.querySelector('.bw-details');
		details?.addEventListener('toggle', () => {
			/* Built on demand: 720 rows on every minute's repaint would be work for nobody. */
			if (details.open) details.querySelector('.bw-details-body').innerHTML = seriesTable(ui, state.report);
		});
	}

	function setScope(kind, name) {
		state.kind = kind;
		state.name = kind ? name : '';
		refresh(api);
	}
}

/* ── Scope ───────────────────────────────────────────────────── */

function describeScope(state) {
	if (state.kind && state.name) return `${KIND_LABEL[state.kind]} server ${state.name}`;
	if (state.kind) return `${KIND_LABEL[state.kind]} tier`;
	return 'every server';
}

function encodeScope(kind, name) {
	return kind ? `${kind}|${encodeURIComponent(name ?? '')}` : '';
}

function decodeScope(value) {
	if (!value) return ['', ''];
	const cut = value.indexOf('|');
	const kind = value.slice(0, cut);
	return KINDS.includes(kind) ? [kind, decodeURIComponent(value.slice(cut + 1))] : ['', ''];
}

function scopeOptions(ui, servers, state) {
	const selected = encodeScope(state.kind, state.name);
	const option = (value, label) =>
		`<option value="${ui.esc(value)}"${value === selected ? ' selected' : ''}>${ui.esc(label)}</option>`;
	let out = option('', 'Every server');
	for (const kind of KINDS) {
		const members = servers.filter((s) => s.kind === kind);
		if (!members.length) continue;
		out += `<optgroup label="${ui.esc(KIND_LABEL[kind])}">`;
		out += option(encodeScope(kind, ''), `${KIND_LABEL[kind]} tier (${members.length})`);
		for (const s of members) out += option(encodeScope(kind, s.name), s.name);
		out += '</optgroup>';
	}
	// A scoped server that has since gone quiet stays selectable rather than silently resetting.
	if (state.kind && state.name && !servers.some((s) => s.kind === state.kind && s.name === state.name)) {
		out += option(selected, `${state.name} (no rows in 30 days)`);
	}
	return out;
}

/* ── Tables ──────────────────────────────────────────────────── */

function noData(ui, sub = '') {
	return `<span class="faint">no data</span>${sub ? `<div class="cell-sub">${ui.esc(sub)}</div>` : ''}`;
}

/** A sent figure over a received one, both measured UDP payload. */
function pair(ui, sent, recv, format, subExtra = '') {
	const s = format(sent);
	if (s === null) return noData(ui);
	const r = format(recv);
	return `<div class="tnum" title="Measured UDP payload">↑ ${ui.esc(s)}</div>
		<div class="cell-sub tnum">↓ ${ui.esc(r ?? 'no data')}${subExtra ? ` · ${ui.esc(subExtra)}` : ''}</div>`;
}

function wirePair(ui, totals) {
	if (!totals) return noData(ui);
	return `<div class="tnum" title="${ui.esc(SENT_WIRE_NOTE)}">≈ ↑ ${ui.esc(formatTotal(totals.wireSentBytesEstimated))}</div>
		<div class="cell-sub tnum" title="${ui.esc(RECV_WIRE_NOTE)}">≈ ↓ ${ui.esc(formatTotal(totals.wireRecvBytesEstimated))} · est.</div>`;
}

function peakCell(ui, peak) {
	if (!peak) return noData(ui);
	return `<div class="tnum" title="Measured UDP payload sent. On the wire ≈ ${ui.esc(formatRate(peak.wireSentBpsEstimated))} (est.)">↑ ${ui.esc(formatRate(peak.udpSentBps))}</div>
		<div class="cell-sub">${ui.esc(ui.dateTime(peak.bucketUtc))}</div>`;
}

function head(ui, range) {
	const th = (label, title, num = true) =>
		`<th${num ? ' class="num"' : ''}${title ? ` title="${ui.esc(title)}"` : ''}>${ui.esc(label)}</th>`;
	return `<thead><tr>
		${th('Server', '', false)}
		${th('Now', `Measured UDP payload rate of the latest minute. ${PAYLOAD_NOTE}`)}
		${th('Last hour', PAYLOAD_NOTE)}
		${th('Last 24 hours', PAYLOAD_NOTE)}
		${th('Last 30 days', PAYLOAD_NOTE)}
		${th('30 days on the wire (est.)', SENT_WIRE_NOTE)}
		${th(range.peak, `The busiest ${range.unit} of the charted range by measured UDP payload sent.`)}
	</tr></thead>`;
}

function tierTable(ui, r, range, state) {
	const rows = [...(r.tiers ?? []), { ...r.total, kind: null }];
	const body = rows.map((t) => {
		const name = t.kind ? `${KIND_LABEL[t.kind]} tier` : 'Every server';
		const selected = !state.name && (t.kind ?? '') === state.kind;
		return `
			<tr class="is-clickable${selected ? ' is-selected' : ''}" tabindex="0"
				data-scope-kind="${ui.esc(t.kind ?? '')}" data-scope-name="">
				<td><div class="cell-primary">${ui.esc(name)}</div>
					<div class="cell-sub">${ui.num(t.servers)} server${t.servers === 1 ? '' : 's'}, ${ui.num(t.serversReporting)} reporting now</div></td>
				<td class="num">${pair(ui, t.current?.udpSentBps, t.current?.udpRecvBps, formatRate)}</td>
				<td class="num">${pair(ui, t.lastHour?.udpSentBytes, t.lastHour?.udpRecvBytes, formatTotal)}</td>
				<td class="num">${pair(ui, t.lastDay?.udpSentBytes, t.lastDay?.udpRecvBytes, formatTotal)}</td>
				<td class="num">${pair(ui, t.last30Days?.udpSentBytes, t.last30Days?.udpRecvBytes, formatTotal)}</td>
				<td class="num">${wirePair(ui, t.last30Days)}</td>
				<td class="num">${peakCell(ui, t.peak)}</td>
			</tr>`;
	}).join('');
	return `<div class="table-wrap"><table class="data">${head(ui, range)}<tbody>${body}</tbody></table></div>`;
}

function serverTable(ui, r, range, state) {
	const body = (r.servers ?? []).map((s) => {
		const selected = state.kind === s.kind && state.name === s.name;
		const seen = s.lastSeenUtc ? `last row ${ui.ago(s.lastSeenUtc)}` : 'no recent row';
		const restarts = s.instances24h > 1 ? ` · ${s.instances24h} processes in 24 h` : '';
		const now = s.current
			? pair(ui, s.current.udpSentBps, s.current.udpRecvBps, formatRate,
				known(s.current.sessionsActive) ? `${ui.num(s.current.sessionsActive)} sessions` : '')
			: noData(ui, s.lastSeenUtc ? `silent since ${ui.ago(s.lastSeenUtc)}` : '');
		return `
			<tr class="is-clickable${selected ? ' is-selected' : ''}${s.current ? '' : ' bw-quiet'}" tabindex="0"
				data-scope-kind="${ui.esc(s.kind)}" data-scope-name="${ui.esc(encodeURIComponent(s.name))}"
				aria-label="Chart ${ui.esc(s.name)}">
				<td><div class="cell-primary">${ui.esc(s.name)}</div>
					<div class="cell-sub">${ui.esc(KIND_LABEL[s.kind] ?? s.kind)} · ${ui.esc(seen)}${ui.esc(restarts)}</div></td>
				<td class="num">${now}</td>
				<td class="num">${pair(ui, s.lastHour?.udpSentBytes, s.lastHour?.udpRecvBytes, formatTotal)}</td>
				<td class="num">${pair(ui, s.lastDay?.udpSentBytes, s.lastDay?.udpRecvBytes, formatTotal)}</td>
				<td class="num">${pair(ui, s.last30Days?.udpSentBytes, s.last30Days?.udpRecvBytes, formatTotal)}</td>
				<td class="num">${wirePair(ui, s.last30Days)}</td>
				<td class="num">${peakCell(ui, s.peak)}</td>
			</tr>`;
	}).join('');
	return `<div class="table-wrap"><table class="data">${head(ui, range)}<tbody>${body}</tbody></table></div>`;
}

/** The chart's points as rows: the accessible twin of both charts. */
function seriesTable(ui, r) {
	const points = r.series ?? [];
	if (!points.length) return ui.empty('No data in this range', 'There are no points to list.', 'chart');
	const cell = (v) => `<td class="num tnum">${ui.esc(formatRate(v) ?? 'no data')}</td>`;
	return `<div class="table-wrap"><table class="data">
		<thead><tr><th>Time</th>
			<th class="num">↑ UDP payload</th><th class="num" title="${ui.esc(SENT_WIRE_NOTE)}">↑ On the wire (est.)</th><th class="num">↑ Game data</th>
			<th class="num">↓ UDP payload</th><th class="num" title="${ui.esc(RECV_WIRE_NOTE)}">↓ On the wire (est.)</th><th class="num">↓ Game data</th>
		</tr></thead>
		<tbody>${points.map((p) => `<tr><td class="nowrap">${ui.esc(ui.dateTime(p.bucketUtc))}</td>
			${cell(p.udpSentBps)}${cell(p.wireSentBpsEstimated)}${cell(p.appSentBps)}
			${cell(p.udpRecvBps)}${cell(p.wireRecvBpsEstimated)}${cell(p.appRecvBps)}</tr>`).join('')}</tbody>
	</table></div>`;
}

/* ── Chart ───────────────────────────────────────────────────── */

/** A 1-2-2.5-5 step giving about four gridlines up to `max` megabits per second. */
export function niceScale(max) {
	if (!(max > 0)) return { top: 1, step: 0.25 };
	const rough = max / 4;
	const power = 10 ** Math.floor(Math.log10(rough));
	const step = [1, 2, 2.5, 5, 10].map((m) => m * power).find((s) => s >= rough);
	return { top: Math.ceil(max / step) * step, step };
}

/**
 * The runs of consecutive points: a new run starts where the next point is more than one and
 * a half buckets after the last, so a missing minute is drawn as a gap and never bridged.
 */
export function runs(points, resolutionSeconds) {
	const out = [];
	let current = [];
	let last = null;
	for (const p of points) {
		const t = Date.parse(p.bucketUtc);
		if (last !== null && t - last > resolutionSeconds * 1500) {
			out.push(current);
			current = [];
		}
		current.push(p);
		last = t;
	}
	if (current.length) out.push(current);
	return out;
}

function chartBlock(ui, which, r, range) {
	const points = r.series ?? [];
	const series = SERIES[which];
	const legend = `<div class="bw-legend">${series.map((s) => `
		<span class="bw-key" title="${ui.esc(s.note)}">
			<span class="bw-key-line${s.dashed ? ' is-dashed' : ''}" style="color:var(--${s.tone})"></span>
			${ui.esc(s.label)} <span class="badge${s.measure === 'estimated' ? ' badge-warn' : ''}">${ui.esc(s.measure)}</span>
		</span>`).join('')}</div>`;

	if (!points.length) {
		return `${legend}${ui.empty('No data in this range',
			'Nothing in this scope wrote a row in the charted range, so there is nothing to draw — not a flat zero.', 'chart')}`;
	}

	const t0 = Date.parse(r.rangeStartUtc);
	const t1 = Math.max(Date.parse(r.generatedUtc), t0 + 1);
	const x = (p) => ((Date.parse(p.bucketUtc) - t0) / (t1 - t0)) * VIEW_W;
	let max = 0;
	for (const p of points) for (const s of series) if (known(p[s.key])) max = Math.max(max, p[s.key] * 8 / 1e6);
	const { top, step } = niceScale(max);
	const y = (bps) => VIEW_H - (Math.max(0, bps) * 8 / 1e6 / top) * VIEW_H;

	const segments = runs(points, r.resolutionSeconds);
	const paths = series.map((s) => {
		let line = '';
		let area = '';
		for (const run of segments) {
			const drawn = run.filter((p) => known(p[s.key]));
			if (!drawn.length) continue;
			/* A lone point would be an invisible zero-length line; a short tick keeps it visible. */
			const coords = drawn.length === 1
				? [[x(drawn[0]) - 2, y(drawn[0][s.key])], [x(drawn[0]) + 2, y(drawn[0][s.key])]]
				: drawn.map((p) => [x(p), y(p[s.key])]);
			const d = coords.map(([px, py], i) => `${i ? 'L' : 'M'}${px.toFixed(1)},${py.toFixed(1)}`).join(' ');
			line += `${d} `;
			if (s.area) area += `${d} L${coords[coords.length - 1][0].toFixed(1)},${VIEW_H} L${coords[0][0].toFixed(1)},${VIEW_H} Z `;
		}
		return `
			${area ? `<path d="${area}" fill="currentColor" fill-opacity="0.1" stroke="none" style="color:var(--${s.tone})" />` : ''}
			<path d="${line}" fill="none" stroke="currentColor" stroke-width="2" vector-effect="non-scaling-stroke"
				stroke-linejoin="round" stroke-linecap="round"${s.dashed ? ' stroke-dasharray="6 4"' : ''}
				style="color:var(--${s.tone})" />`;
	}).join('');

	const ticks = [];
	for (let v = 0; v <= top + step / 2; v += step) ticks.push(v);
	const grid = ticks.map((v) => {
		const py = VIEW_H - (v / top) * VIEW_H;
		return `<line x1="0" x2="${VIEW_W}" y1="${py.toFixed(1)}" y2="${py.toFixed(1)}" class="bw-grid" vector-effect="non-scaling-stroke" />`;
	}).join('');
	const yLabels = ticks.map((v) =>
		`<span class="bw-ytick" style="top:${(100 - (v / top) * 100).toFixed(2)}%">${ui.esc(sig(v))}</span>`).join('');

	const xLabels = [0, 0.25, 0.5, 0.75, 1].map((f) => {
		const at = new Date(t0 + f * (t1 - t0));
		const text = r.range === 'month'
			? at.toLocaleDateString([], { month: 'short', day: 'numeric' })
			: at.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' });
		return `<span class="bw-xtick${f === 0 ? ' is-first' : f === 1 ? ' is-last' : ''}" style="left:${(f * 100).toFixed(1)}%">${ui.esc(text)}</span>`;
	}).join('');

	return `${legend}
		<div class="bw-chart" data-chart="${which}">
			<div class="bw-yaxis" aria-hidden="true"><span class="bw-yunit">Mbps</span>${yLabels}</div>
			<div class="bw-plot" tabindex="0" role="img"
				aria-label="${ui.esc(`${which === 'sent' ? 'Sent' : 'Received'} rate, ${points.length} points; the table below the charts lists them. Use the arrow keys to step through.`)}">
				<svg class="bw-svg" viewBox="0 0 ${VIEW_W} ${VIEW_H}" preserveAspectRatio="none" aria-hidden="true">${grid}${paths}</svg>
				<div class="bw-crosshair"></div>
				<div class="bw-tip" role="status" aria-live="polite"></div>
			</div>
			<div class="bw-xaxis" aria-hidden="true">${xLabels}</div>
		</div>`;
}

/** Crosshair, dots and one tooltip listing every series at the nearest point. Pointer and keyboard. */
function wireChart(ui, chart, r, series) {
	const plot = chart.querySelector('.bw-plot');
	const cross = chart.querySelector('.bw-crosshair');
	const tip = chart.querySelector('.bw-tip');
	const points = r?.series ?? [];
	if (!plot || !points.length) return;

	const t0 = Date.parse(r.rangeStartUtc);
	const t1 = Math.max(Date.parse(r.generatedUtc), t0 + 1);
	const fractions = points.map((p) => (Date.parse(p.bucketUtc) - t0) / (t1 - t0));
	let top = 0;
	for (const p of points) for (const s of series) if (known(p[s.key])) top = Math.max(top, p[s.key] * 8 / 1e6);
	top = niceScale(top).top;

	const dots = series.map((s) => {
		const dot = document.createElement('span');
		dot.className = 'bw-dot';
		dot.style.color = `var(--${s.tone})`;
		plot.appendChild(dot);
		return dot;
	});
	const range = RANGES.find((x) => x.key === r.range) ?? RANGES[0];
	let index = -1;

	function show(i) {
		index = Math.max(0, Math.min(points.length - 1, i));
		const p = points[index];
		const f = Math.max(0, Math.min(1, fractions[index]));
		cross.style.display = 'block';
		cross.style.left = `${(f * 100).toFixed(2)}%`;
		series.forEach((s, k) => {
			const v = p[s.key];
			if (!known(v)) {
				dots[k].style.display = 'none';
				return;
			}
			dots[k].style.display = 'block';
			dots[k].style.left = `${(f * 100).toFixed(2)}%`;
			dots[k].style.top = `${(100 - (Math.max(0, v) * 8 / 1e6 / top) * 100).toFixed(2)}%`;
		});

		/* Built with textContent: nothing here is markup, and the habit costs nothing. */
		tip.replaceChildren();
		const time = document.createElement('div');
		time.className = 'bw-tip-time';
		time.textContent = `${ui.dateTime(p.bucketUtc)} · ${range.unit}`;
		tip.appendChild(time);
		for (const s of series) {
			const row = document.createElement('div');
			row.className = 'bw-tip-row';
			const key = document.createElement('span');
			key.className = `bw-key-line${s.dashed ? ' is-dashed' : ''}`;
			key.style.color = `var(--${s.tone})`;
			const value = document.createElement('span');
			value.className = 'bw-tip-value';
			value.textContent = `${s.measure === 'estimated' ? '≈ ' : ''}${formatRate(p[s.key]) ?? 'no data'}`;
			const name = document.createElement('span');
			name.className = 'bw-tip-name';
			name.textContent = `${s.label}${s.measure === 'estimated' ? ' (est.)' : ''}`;
			row.append(key, value, name);
			tip.appendChild(row);
		}
		tip.style.display = 'block';
		if (f < 0.6) {
			tip.style.left = `calc(${(f * 100).toFixed(2)}% + 12px)`;
			tip.style.right = '';
		} else {
			tip.style.left = '';
			tip.style.right = `calc(${(100 - f * 100).toFixed(2)}% + 12px)`;
		}
	}

	function hide() {
		index = -1;
		cross.style.display = 'none';
		tip.style.display = 'none';
		for (const dot of dots) dot.style.display = 'none';
	}

	/* The nearest point to a fraction of the width: the reader aims at a time, not at a line. */
	function nearest(f) {
		let lo = 0;
		let hi = fractions.length - 1;
		while (lo < hi) {
			const mid = (lo + hi) >> 1;
			if (fractions[mid] < f) lo = mid + 1;
			else hi = mid;
		}
		if (lo > 0 && Math.abs(fractions[lo - 1] - f) <= Math.abs(fractions[lo] - f)) lo--;
		return lo;
	}

	plot.addEventListener('pointermove', (e) => {
		const box = plot.getBoundingClientRect();
		if (box.width > 0) show(nearest((e.clientX - box.left) / box.width));
	});
	plot.addEventListener('pointerleave', () => {
		if (document.activeElement !== plot) hide();
	});
	plot.addEventListener('focus', () => show(index < 0 ? points.length - 1 : index));
	plot.addEventListener('blur', hide);
	plot.addEventListener('keydown', (e) => {
		const moves = { ArrowLeft: -1, ArrowRight: 1, Home: -Infinity, End: Infinity };
		if (!(e.key in moves)) return;
		e.preventDefault();
		const step = moves[e.key];
		show(Number.isFinite(step) ? (index < 0 ? points.length - 1 : index) + step : step < 0 ? 0 : points.length - 1);
	});
}
