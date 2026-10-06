/*
 * Servers → World time. The one world clock, and the controls that set it.
 *
 * WHAT THIS PAGE CAN AND CANNOT DO.
 *
 * World time lives in ONE database row. Every scene server reads it back on its pulse (about
 * every two seconds), and when the row's revision has changed it jumps its players' clocks to
 * it. Every control here writes that row and nothing else, so:
 *
 *   - an acknowledgement says the time was WRITTEN, never that the world has moved. The server
 *     composes it; this page shows it as given;
 *   - if no scene server has ever run against this database there is no row, and nothing to
 *     write. The page says so and offers no control — a write would be refused anyway;
 *   - all clock arithmetic is the database's. The world time a write replaces and the one it
 *     writes are computed in the statement, against the database clock, so neither this
 *     browser's clock nor the panel host's can move the world.
 *
 * The ticking display is a convenience. It starts from world-now as the database computed it
 * when the last read was answered, and advances it at the row's pace by this browser's
 * MONOTONIC clock (performance.now), never its wall clock — a browser set to the wrong date
 * still shows the right world time. It is re-anchored on every read, every few seconds.
 *
 * Every write needs a reason (it is recorded in the audit log with the world time and pace
 * before and after) and a recent step-up; `ctx.attempt` asks for the code when the server
 * answers 428.
 */

/* How often the reading is refreshed. Short enough that a change written in game, or by a
 * second operator, appears here about as soon as the scene servers adopt it. The timer's reads
 * are marked automatic and are not audited; the first load and the Refresh button are. */
const REFRESH_MS = 3000;

/* The display shows milliseconds, so it repaints often. Only one text node is rewritten. */
const TICK_MS = 50;

/* The pace presets the page offers. Free entry beside them takes anything else up to the
 * server's ceiling. */
const PACE_PRESETS = [0.25, 1, 10, 60, 600];

/* One-click steps. Each is a change in the syntax the server parses, so what is recorded is
 * exactly what the button said. */
const STEPS = [
	{ label: '+1 min', change: '+1m' },
	{ label: '+1 h', change: '+1h' },
	{ label: '+1 day', change: '+1d' },
	{ label: '−1 h', change: '-1h' },
];

export async function render(host, ctx) {
	const { api, ui } = ctx;

	let poll = null;
	let ticker = null;
	ctx.onCleanup(() => {
		clearInterval(poll);
		clearInterval(ticker);
	});

	let clock = null;
	/* performance.now() when `clock` was answered — the anchor the display ticks from. */
	let anchoredAt = 0;
	let refreshError = null;
	let busy = false;
	/* Which layout is on screen: null before the first paint, then 'missing' or 'clock'. The
	 * whole page is painted only when this changes, so a poll never wipes a half-typed value
	 * or reason. */
	let painted = null;

	function adopt(reading) {
		clock = reading;
		anchoredAt = performance.now();
		refreshError = null;
		paintOrUpdate();
	}

	async function read(source = api) {
		adopt(await source.getWorldTime());
	}

	async function pollOnce(source = api) {
		try {
			await read(source);
		} catch (err) {
			refreshError = err.message || 'The world clock could not be refreshed.';
			if (clock) paintOrUpdate();
		}
	}

	poll = setInterval(() => pollOnce(api.auto), REFRESH_MS);
	ticker = setInterval(tick, TICK_MS);

	// The first read may throw; the router renders the failure and the poll heals the page.
	await read();

	/* ── Painting ─────────────────────────────────────────────── */

	function paintOrUpdate() {
		const layout = clock.exists ? 'clock' : 'missing';
		if (layout !== painted) {
			painted = layout;
			host.innerHTML = layout === 'clock' ? clockPage() : missingPage();
			wire();
		}
		update();
	}

	function head() {
		return `
			<div class="page-head">
				<div class="page-head-text">
					<h1>World time</h1>
					<p class="page-head-sub">
						One clock for the whole world. It lives in a single database row that every scene server
						reads on its pulse and adopts as an instant jump. Writing it here does not reach into any
						process: it leaves the time for the scene servers to pick up within a few seconds.
					</p>
				</div>
				<div class="page-head-actions">
					<button class="btn" data-act="refresh">${ui.icon('refresh')} Refresh</button>
				</div>
			</div>`;
	}

	function missingPage() {
		return `
			${head()}
			<div class="stack-lg">
				<div data-slot="refresh-error"></div>
				${ui.banner('warn', 'No scene server has created the world clock yet',
					'The first scene server to start against this database creates the row from its calendar, which is the only place the calendar epoch is known. Until then there is no world time to show and nothing to write, so this page offers no controls. Start a scene server and this page fills in on its own.')}
				${ui.empty('No world clock row', 'Nothing has been written to world_clock_control.', 'clock')}
			</div>`;
	}

	function clockPage() {
		return `
			${head()}
			<div class="stack-lg">
				<div data-slot="refresh-error"></div>

				${ui.card({
					title: 'World time now',
					sub: `Written ${clock.format ?? 'yyyy-MM-dd HH:mm:ss.fff'}: the calendar epoch plus world milliseconds, not anybody's local time`,
					body: `
						<div class="stat-value mono tnum" data-field="ticking" style="font-size:clamp(1.4rem, 5vw, 2.4rem);overflow-wrap:anywhere">—</div>
						<div class="row" style="flex-wrap:wrap;margin-top:var(--sp-3)">
							<span data-field="pace-badge"></span>
							<span class="small muted" data-field="ticking-note"></span>
						</div>`,
				})}

				<div class="grid grid-4">
					<div class="stat"><div class="stat-label">Pace</div><div class="stat-value" data-field="pace">—</div><div class="stat-note" data-field="resume-pace"></div></div>
					<div class="stat"><div class="stat-label">Revision</div><div class="stat-value tnum" data-field="revision">—</div><div class="stat-note">Scene servers jump on a new revision</div></div>
					<div class="stat"><div class="stat-label">Last changed by</div><div class="stat-value" style="font-size:var(--fs-lg)" data-field="updated-by">—</div><div class="stat-note" data-field="updated-at"></div></div>
					<div class="stat"><div class="stat-label">Calendar epoch</div><div class="stat-value mono" style="font-size:var(--fs-lg);overflow-wrap:anywhere" data-field="epoch">—</div><div class="stat-note">World time zero</div></div>
				</div>

				${ui.card({
					title: 'Change world time',
					sub: 'Each control writes the row once. Every change is recorded in the audit log with the world time and pace before and after',
					body: `
						<div class="stack">
							<div class="field">
								<label for="wt-reason">Reason (recorded in the audit log with every change below)</label>
								<textarea id="wt-reason" name="reason" required placeholder="Why is world time being changed?"></textarea>
							</div>

							<form class="field" data-form="set" novalidate>
								<label for="wt-value">Set to a world timestamp, or move by a change</label>
								<div class="row" style="flex-wrap:wrap">
									<input id="wt-value" class="grow mono" name="value" autocomplete="off" spellcheck="false" maxlength="64"
										placeholder="1203-04-05 06:07:08.250   or   +1h30m" style="min-width:min(100%, 16rem)" />
									<button type="submit" class="btn btn-primary" data-write>Write</button>
								</div>
								<div class="field-hint">
									A timestamp keeps the pace and sets the time to the millisecond. A value starting with + or −
									is a change from world-now: units d, h, m, s, ms, largest first — <span class="mono">+1h30m</span>,
									<span class="mono">-90s</span>, <span class="mono">+2d3h4m5.25s</span>; a bare number is seconds.
								</div>
							</form>

							<div class="field">
								<label>Step</label>
								<div class="row" style="flex-wrap:wrap">
									${STEPS.map((s) => `<button type="button" class="btn" data-step="${ui.esc(s.change)}" data-write>${ui.esc(s.label)}</button>`).join('')}
								</div>
							</div>

							<div class="field">
								<label>Hold or resume</label>
								<div class="row" style="flex-wrap:wrap">
									<button type="button" class="btn" data-act="hold" data-write>${ui.icon('stop')} Hold</button>
									<button type="button" class="btn" data-act="resume" data-write>${ui.icon('play')} Resume</button>
								</div>
								<div class="field-hint" data-field="hold-hint"></div>
							</div>

							<form class="field" data-form="pace" novalidate>
								<label for="wt-rate">Pace (world seconds per real second)</label>
								<div class="row" style="flex-wrap:wrap;margin-bottom:var(--sp-2)">
									${PACE_PRESETS.map((r) => `<button type="button" class="btn btn-sm" data-pace="${r}" data-write>${ui.esc(paceLabel(r))}</button>`).join('')}
								</div>
								<div class="row" style="flex-wrap:wrap">
									<input id="wt-rate" name="rate" type="number" min="0" max="${ui.esc(clock.maxRate ?? 100000)}" step="any"
										inputmode="decimal" autocomplete="off" placeholder="e.g. 24" style="max-width:10rem" />
									<button type="submit" class="btn" data-write>Set pace</button>
								</div>
								<div class="field-hint">
									Changes the pace from this instant without moving world time. 0 holds. The pace that is not 0
									becomes the pace a resume returns to.
								</div>
							</form>
						</div>`,
				})}
			</div>`;
	}

	/* Rewrites the facts in place. Called on every read; never touches an input. */
	function update() {
		const slot = host.querySelector('[data-slot="refresh-error"]');
		if (slot) {
			slot.innerHTML = refreshError
				? ui.banner('warn', 'This page is not refreshing',
					`${refreshError} The clock below is ticking from the last good read and may no longer match the row.`)
				: '';
		}
		if (!clock.exists) return;

		setText('pace', clock.pace ?? paceLabel(clock.rate));
		setText('resume-pace', clock.held ? `Resume returns to ${clock.resumePace ?? paceLabel(clock.resumeRate)}` : `Resume pace ${clock.resumePace ?? paceLabel(clock.resumeRate)}`);
		setText('revision', ui.num(clock.revision));
		setText('updated-by', clock.updatedBy ?? 'seeded by a scene server');
		setText('updated-at', clock.updatedAtUtc ? `${ui.dateTime(clock.updatedAtUtc)} (${ui.ago(clock.updatedAtUtc)})` : 'never changed by anyone');
		setText('epoch', clock.epochTimestamp ?? String(clock.epochUnixSeconds));
		setText('ticking-note', clock.held
			? 'Held: the clock is not moving.'
			: `Advancing ${clock.rate === 1 ? 'in real time' : `at ${paceLabel(clock.rate)}`} from the last read.`);
		const badge = host.querySelector('[data-field="pace-badge"]');
		if (badge) {
			badge.innerHTML = clock.held
				? ui.statusBadge('held', 'warn')
				: ui.statusBadge(clock.rate === 1 ? 'real time' : paceLabel(clock.rate), clock.rate === 1 ? 'ok' : 'info', true);
		}
		const hold = host.querySelector('[data-act="hold"]');
		const resume = host.querySelector('[data-act="resume"]');
		if (hold) hold.disabled = busy || clock.held;
		if (resume) resume.disabled = busy || !clock.held;
		setText('hold-hint', clock.held
			? `Held. Resume continues from the held time at ${clock.resumePace ?? paceLabel(clock.resumeRate)}.`
			: 'Hold stops world time where it is; resume continues from there at the pace it had.');
		tick();
	}

	function setText(field, text) {
		const el = host.querySelector(`[data-field="${field}"]`);
		if (el) el.textContent = text;
	}

	function tick() {
		if (!clock?.exists) return;
		const el = host.querySelector('[data-field="ticking"]');
		if (!el) return;
		const elapsed = Math.max(0, performance.now() - anchoredAt);
		const worldMs = clock.worldMsNow + Math.floor((clock.rate || 0) * elapsed);
		el.textContent = formatWorld(clock.epochUnixSeconds, worldMs) ?? clock.worldTimestamp ?? '—';
	}

	/* ── Writes ───────────────────────────────────────────────── */

	function wire() {
		host.querySelector('[data-act="refresh"]')?.addEventListener('click', () => pollOnce());
		if (painted !== 'clock') return;

		host.querySelector('[data-form="set"]').addEventListener('submit', (e) => {
			e.preventDefault();
			const input = host.querySelector('#wt-value');
			const value = input.value.trim();
			if (!value) {
				invalid(input, 'Give a world timestamp or a change such as +1h30m.');
				return;
			}
			write((reason) => api.setWorldTime(value, reason), () => {
				input.value = '';
			});
		});

		for (const button of host.querySelectorAll('[data-step]')) {
			button.addEventListener('click', () => write((reason) => api.shiftWorldTime(button.dataset.step, reason)));
		}

		host.querySelector('[data-act="hold"]').addEventListener('click', () => write((reason) => api.holdWorldTime(reason)));
		host.querySelector('[data-act="resume"]').addEventListener('click', () => write((reason) => api.resumeWorldTime(reason)));

		for (const button of host.querySelectorAll('[data-pace]')) {
			button.addEventListener('click', () => write((reason) => api.setWorldTimePace(Number(button.dataset.pace), reason)));
		}

		host.querySelector('[data-form="pace"]').addEventListener('submit', (e) => {
			e.preventDefault();
			const input = host.querySelector('#wt-rate');
			const rate = Number(input.value);
			const max = Number(clock.maxRate ?? 100000);
			if (input.value.trim() === '' || !Number.isFinite(rate) || rate < 0 || rate > max) {
				invalid(input, `Give a pace between 0 and ${max}.`);
				return;
			}
			write((reason) => api.setWorldTimePace(rate, reason), () => {
				input.value = '';
			});
		});
	}

	/* One path for every write: the shared reason, the step-up, the server's own sentence as
	 * the acknowledgement, and the reading it returned adopted at once. */
	async function write(call, onWritten) {
		if (busy) return;
		const reasonBox = host.querySelector('#wt-reason');
		const reason = reasonBox.value.trim();
		if (!reason) {
			invalid(reasonBox, 'Give a reason first. It is recorded with the change.');
			return;
		}

		setBusy(true);
		try {
			// No success title: `attempt` would toast it before we have the server's own words.
			const result = await ctx.attempt(() => call(reason), null);
			if (result) {
				ui.toast('Written', result.message ?? 'World time written. Every scene server adopts it within a few seconds.', 'ok', 7000);
				if (result.clock) adopt(result.clock);
				onWritten?.();
			}
		} finally {
			setBusy(false);
		}
	}

	function setBusy(value) {
		busy = value;
		for (const el of host.querySelectorAll('[data-write]')) el.disabled = value;
		if (clock) update();
	}

	function invalid(input, message) {
		input.setCustomValidity(message);
		input.reportValidity();
		input.focus();
		setTimeout(() => input.setCustomValidity(''), 10);
	}
}

/* ── Formatting ──────────────────────────────────────────────── */

/* "held", "real time", "60×". Mirrors the server's own wording. */
function paceLabel(rate) {
	const r = Number(rate);
	if (!(r > 0)) return 'held';
	if (r === 1) return 'real time';
	return `${Number(r.toFixed(4))}×`;
}

/**
 * The world timestamp for the ticking display: the epoch plus world milliseconds, in UTC-style
 * calendar arithmetic, formatted exactly as the server formats it (yyyy-MM-dd HH:mm:ss.fff).
 * Null when the instant is outside the years 1–9999, which the server would not write either.
 */
export function formatWorld(epochUnixSeconds, worldMs) {
	const ms = Number(epochUnixSeconds) * 1000 + Number(worldMs);
	if (!Number.isFinite(ms)) return null;
	const d = new Date(ms);
	const year = d.getUTCFullYear();
	if (Number.isNaN(year) || year < 1 || year > 9999) return null;
	const p = (n, w = 2) => String(n).padStart(w, '0');
	return `${p(year, 4)}-${p(d.getUTCMonth() + 1)}-${p(d.getUTCDate())} ` +
		`${p(d.getUTCHours())}:${p(d.getUTCMinutes())}:${p(d.getUTCSeconds())}.${p(d.getUTCMilliseconds(), 3)}`;
}
