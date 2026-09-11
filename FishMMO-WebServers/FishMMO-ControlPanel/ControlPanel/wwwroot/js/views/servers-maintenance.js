/*
 * Servers → Maintenance. Taking a set of servers down together.
 *
 * WHY THIS PAGE EXISTS. Taking a shard down for a patch is a sequence, not a button:
 * lock every server so nobody new joins, let the players already on them finish up or
 * run out of time, then stop. Done by hand across a dozen servers somebody misses one,
 * and the one that gets missed is a world still accepting logins ten seconds before it
 * dies. A window is that sequence written down once.
 *
 * WHAT ACTUALLY MAKES IT HAPPEN — and this is the thing to understand before changing
 * anything here. Starting a window writes two columns on each target's own row:
 * `locked = true` and `shutdown_at_utc = <deadline>`, in one statement, through exactly
 * the same control the server board's buttons use. The deadline is an ABSOLUTE instant.
 * From then on each server counts down to it inside its own process, warns its players
 * as the countdown passes 15m, 10m, 5m, 2m, 1m, 30s and 10s, disconnects them, saves
 * them and stops. None of that needs this page, this browser, or the panel process to be
 * running. An operator can start a twenty minute drain and close their laptop.
 *
 * So this page is a VIEWER, not a driver. The polling below refreshes what the servers'
 * rows say; it is not what advances the maintenance. Closing the tab loses nothing.
 *
 * THE TRAP, repeated everywhere it can be repeated: CANCELLING DOES NOT UNLOCK. The
 * cancel clears `shutdown_at_utc` and nothing else, because scheduling and locking are
 * one statement and un-scheduling is not un-locking. A cancelled window leaves every
 * target closed to new arrivals, and an operator who does not know that has stranded a
 * shard where nobody can log in with the cause nowhere near where they will look. The
 * cancel dialog says it, the acknowledgement says it, the banner afterwards says it, and
 * there is an unlock button on it so the fix is one click rather than a trip to the board
 * for each server.
 *
 * Server names come from the servers themselves, so every one of them goes through
 * `ui.esc` on its way into markup.
 */

/* The servers' own pulse rate. A pulse is the only moment any of these values can change,
 * so polling faster shows the same numbers back and polling slower makes an adopted
 * change look like it was ignored. */
const REFRESH_MS = 5000;

/* The deadline counts down on its own second so it reads like a clock instead of jumping
 * five seconds at a time. Only the countdown text is rewritten. */
const COUNTDOWN_MS = 1000;

/* Mirrors the API's ceiling, so a mistyped drain is refused in the dialog rather than
 * after the operator has typed their reason. */
const MAX_DRAIN_SECONDS = 86400;

const DRAIN_PRESETS = [
	{ label: '5 minutes', seconds: 300 },
	{ label: '15 minutes', seconds: 900 },
	{ label: '30 minutes', seconds: 1800 },
	{ label: '1 hour', seconds: 3600 },
];

export async function render(host, ctx) {
	if (ctx.param) return renderOperation(host, ctx, ctx.param);
	return renderList(host, ctx);
}

/* ── The list ────────────────────────────────────────────────── */

async function renderList(host, ctx) {
	const { api, ui } = ctx;

	let poll = null;
	let countdown = null;

	/* Registered before anything awaits, and clearing BOTH timers in one function: the
	 * router keeps a single cleanup per view, so a second onCleanup call would replace this
	 * one and leave a timer firing into a page the operator has left. */
	ctx.onCleanup(() => {
		clearInterval(poll);
		clearInterval(countdown);
	});

	let payload = null;
	let board = null;
	let refreshError = null;

	async function read() {
		const [windows, servers] = await Promise.all([api.listMaintenance(), api.getServerBoard()]);
		payload = windows;
		board = servers;
		refreshError = null;
		paint();
	}

	/* A failed poll keeps what is on screen rather than blanking it: the last good read,
	 * labelled as stale, is more use mid-incident than an error page where the numbers were. */
	async function pollOnce() {
		try {
			await read();
		} catch (err) {
			refreshError = err.message || 'The maintenance windows could not be refreshed.';
			if (payload) paint();
		}
	}

	poll = setInterval(pollOnce, REFRESH_MS);
	countdown = setInterval(() => tickCountdowns(host), COUNTDOWN_MS);

	// The first read is allowed to throw — the router turns it into a visible failure.
	await read();

	function paint() {
		const operations = payload.operations ?? [];
		const active = operations.filter((o) => o.active);
		const lockedAfterwards = operations
			.filter((o) => !o.active)
			.reduce((sum, o) => sum + (Number(o.stillLockedCount) || 0), 0);
		const draining = active.reduce((sum, o) => sum + (Number(o.playersRemaining) || 0), 0);

		host.innerHTML = `
			<div class="page-head">
				<div class="page-head-text">
					<h1>Maintenance</h1>
					<p class="page-head-sub">
						One window locks every server you pick, gives the players on them a drain, then stops
						them together. Both parts are columns on each server's own row, so once a window has
						started it runs without this page: the deadline is an absolute time that each server
						counts down to inside its own process. You can close this tab.
					</p>
				</div>
				<div class="page-head-actions">
					<button class="btn" data-act="refresh">${ui.icon('refresh')} Refresh</button>
					<button class="btn btn-primary" data-act="plan">Plan maintenance</button>
				</div>
			</div>

			<div class="stack-lg">
				<div class="grid grid-4">
					${ui.stat({
						label: 'Windows running',
						value: ui.num(active.length),
						note: active.length ? 'Locked, and counting down' : 'Nothing is scheduled to stop',
						noteTone: active.length ? 'warn' : '',
					})}
					${ui.stat({
						label: 'Players still draining',
						value: ui.num(draining),
						note: 'On servers that are due to stop',
					})}
					${ui.stat({
						label: 'Servers left locked',
						value: ui.num(lockedAfterwards),
						noteTone: lockedAfterwards ? 'danger' : 'ok',
						note: lockedAfterwards
							? 'From finished or cancelled windows — unlock them'
							: 'No finished window is holding a lock',
					})}
					${ui.stat({
						label: 'Windows recorded',
						value: ui.num(operations.length),
						note: 'Newest first, below',
					})}
				</div>

				${refreshError
					? ui.banner('warn', 'This page is not refreshing',
						`${refreshError} What is on screen is the last good read. The servers are unaffected — a window that has started does not depend on this panel.`)
					: ''}

				${lockedAfterwards
					? ui.banner('danger', `${lockedAfterwards} server(s) are still locked by a finished window`,
						'Cancelling a window clears the shutdown deadline and nothing else — the lock that scheduling applied stays applied. Open the window to see which servers, and unlock them there.')
					: ''}

				${ui.banner('info', 'What a window writes, and what it does not',
					'It writes locked = true and a shutdown deadline on each target, in one statement, exactly as the board\'s own buttons do. It cannot start a server, and it cannot stop one that is not pulsing — a row written for a process that is not running is read by nobody.')}

				${ui.card({
					title: 'Windows',
					sub: 'Each one is rows in the database, so it survives this panel restarting mid-drain',
					flush: true,
					body: ui.table({
						columns: [
							{
								label: 'Window',
								cell: (o) => `<div class="cell-primary">${ui.esc(o.name)}</div>
									<div class="cell-sub">${ui.esc(o.reason)}</div>`,
							},
							{ label: 'Status', cell: (o) => statusBadge(ui, o.statusName, o.active) },
							{ label: 'Servers', cell: (o) => progressCell(ui, o) },
							{
								label: 'Players left',
								align: 'right',
								cell: (o) => (o.active ? `<span class="tnum">${ui.num(o.playersRemaining)}</span>` : '<span class="faint">—</span>'),
							},
							{ label: 'Stops', cell: (o) => deadlineCell(ui, o) },
							{ label: 'Started by', cell: (o) => `${ui.esc(o.startedBy)}<div class="cell-sub">${ui.esc(ui.ago(o.startedUtc))}</div>` },
							{
								label: '',
								align: 'right',
								cell: (o) => `<a class="btn btn-sm" href="#/servers/maintenance/${encodeURIComponent(o.id)}">Open</a>`,
							},
						],
						rows: operations,
						emptyText: 'No maintenance window has been planned. Planning one locks every server you pick straight away.',
					}),
				})}
			</div>`;

		host.querySelector('[data-act="refresh"]')?.addEventListener('click', () => pollOnce());
		host.querySelector('[data-act="plan"]')?.addEventListener('click', () => askPlan());
	}

	async function askPlan() {
		const worlds = board?.worldServers ?? [];
		const scenes = board?.sceneServers ?? [];
		if (!worlds.length && !scenes.length) {
			ui.toast('Nothing to take down', 'No world or scene server has registered, so there is nothing to lock.', 'warn');
			return;
		}

		const values = await ui.modal({
			title: 'Plan a maintenance window',
			sub: 'Every server you pick is locked the moment you confirm, and scheduled to stop at the end of the drain.',
			confirmLabel: 'Lock and schedule',
			confirmTone: 'danger',
			wide: true,
			body: `
				<div class="field">
					<label for="mw-name">Name</label>
					<input id="mw-name" name="name" maxlength="128" autocomplete="off" placeholder="Patch 0.9.5" />
					<div class="field-hint">What this window is called in the list. Optional.</div>
				</div>

				<div class="field">
					<label>Servers</label>
					<div class="field-hint" style="margin-bottom:var(--sp-2)">
						A world server's deadline reaches the scene servers hosting its scenes as well: they
						read the world's control state and clear its players out on the same deadline. Pick a
						scene server here only when you want that process itself to stop.
					</div>
					${targetGroup(ui, 'World servers', worlds, 'world')}
					${targetGroup(ui, 'Scene servers', scenes, 'scene')}
				</div>

				<div class="field">
					<label for="mw-drain">Drain</label>
					<div class="row" style="flex-wrap:wrap;margin-bottom:var(--sp-2)">
						${DRAIN_PRESETS.map((p) =>
							`<button type="button" class="btn btn-sm" data-preset="${p.seconds}">${ui.esc(p.label)}</button>`).join('')}
					</div>
					<input id="mw-drain" name="drainSeconds" type="number" min="0" max="${MAX_DRAIN_SECONDS}" step="1"
						value="900" required inputmode="numeric" autocomplete="off" />
					<div class="field-hint">
						Seconds. Players are warned as the countdown passes 15m, 10m, 5m, 2m, 1m, 30s and 10s.
						Zero stops everything at its next pulse, with one warning and no drain.
					</div>
				</div>

				<div class="field">
					<label for="mw-reason">Reason (recorded in the audit log)</label>
					<textarea id="mw-reason" name="reason" required placeholder="Deploying build 0.9.5"></textarea>
				</div>

				<div id="mw-preview"></div>`,
			onReady: (form) => wirePlanDialog(ui, form),
			onSubmit: (v, form) => {
				const targets = selectedTargets(form);
				if (!targets.length) {
					flash(form, ui, 'Pick at least one server. A window with no targets writes nothing.');
					return false;
				}
				const drainSeconds = Number(v.drainSeconds);
				if (!Number.isInteger(drainSeconds) || drainSeconds < 0 || drainSeconds > MAX_DRAIN_SECONDS) {
					const input = form.querySelector('#mw-drain');
					input.setCustomValidity(`Give a whole number of seconds between 0 and ${MAX_DRAIN_SECONDS}.`);
					input.reportValidity();
					setTimeout(() => input.setCustomValidity(''), 10);
					return false;
				}
				if (!String(v.reason).trim()) {
					flash(form, ui, 'A reason is required. It goes into the audit log beside your name.');
					return false;
				}
				return { name: v.name, reason: v.reason, drainSeconds, targets };
			},
		});
		if (!values) return;

		const result = await ctx.attempt(
			() => api.startMaintenance({
				name: values.name,
				targets: values.targets,
				drainSeconds: values.drainSeconds,
				reason: values.reason,
			}),
			'Locked and scheduled',
			// What was WRITTEN. At this moment no server has read anything.
			`${values.targets.length} server(s) locked. Each one acts on the deadline once it reads its row.`,
		);
		if (!result) return;

		for (const warning of result.warnings ?? []) {
			ui.toast('Worth knowing', warning, 'warn', 9000);
		}
		ctx.go(`servers/maintenance/${result.operation.id}`);
	}
}

/* ── One window ──────────────────────────────────────────────── */

async function renderOperation(host, ctx, id) {
	const { api, ui } = ctx;

	let poll = null;
	let countdown = null;

	ctx.onCleanup(() => {
		clearInterval(poll);
		clearInterval(countdown);
	});

	let operation = null;
	let refreshError = null;

	async function read() {
		operation = await api.getMaintenance(id);
		refreshError = null;
		paint();
	}

	async function pollOnce() {
		// Once every target has finished nothing else can change, so the poll stops rather
		// than asking the same question of the database forever.
		if (operation && !operation.active) {
			clearInterval(poll);
			poll = null;
			return;
		}
		try {
			await read();
		} catch (err) {
			refreshError = err.message || 'This window could not be refreshed.';
			if (operation) paint();
		}
	}

	poll = setInterval(pollOnce, REFRESH_MS);
	countdown = setInterval(() => tickCountdowns(host), COUNTDOWN_MS);

	await read();

	function paint() {
		const targets = operation.targets ?? [];
		const stillLocked = targets.filter((t) => t.stillLocked);

		host.innerHTML = `
			<div class="page-head">
				<div class="page-head-text">
					<div class="row" style="margin-bottom:var(--sp-2)">
						<a class="small muted" href="#/servers/maintenance">← All windows</a>
					</div>
					<h1>${ui.esc(operation.name)}</h1>
					<p class="page-head-sub">
						Planned by ${ui.esc(operation.startedBy)} ${ui.esc(ui.ago(operation.startedUtc))} ·
						${ui.esc(operation.reason)}
					</p>
				</div>
				<div class="page-head-actions">
					${statusBadge(ui, operation.statusName, operation.active)}
					${operation.active ? '<button class="btn btn-danger" data-act="cancel">Cancel the shutdown</button>' : ''}
					${stillLocked.length ? `<button class="btn" data-act="unlock">Unlock ${stillLocked.length} server(s)</button>` : ''}
				</div>
			</div>

			<div class="stack-lg">
				${refreshError ? ui.banner('warn', 'This window is not refreshing', refreshError) : ''}

				${headlineBanner(ui, operation, stillLocked)}

				${ui.card({
					title: 'The plan',
					body: `
						<dl class="dl">
							<dt>Window</dt><dd class="tnum">${ui.esc(String(operation.id))}</dd>
							<dt>Servers</dt><dd>${ui.esc(String(operation.counts.total))} — ${ui.esc(countsSentence(operation.counts))}</dd>
							<dt>Drain</dt><dd>${ui.esc(ui.duration(operation.drainSeconds))}</dd>
							<dt>Locked at</dt><dd>${ui.esc(ui.dateTime(operation.startedUtc))}</dd>
							<dt>Stops at</dt><dd>${ui.esc(ui.dateTime(operation.deadlineUtc))}${deadlineCountdown(ui, operation)}</dd>
							<dt>Finished</dt><dd>${operation.completedUtc ? ui.esc(ui.dateTime(operation.completedUtc)) : '<span class="faint">—</span>'}</dd>
							<dt>Planned by</dt><dd>${ui.esc(operation.startedBy)}</dd>
							<dt>Reason</dt><dd>${ui.esc(operation.reason)}</dd>
							${operation.cancelledUtc ? `
								<dt>Cancelled by</dt><dd>${ui.esc(operation.cancelledBy ?? '')} at ${ui.esc(ui.dateTime(operation.cancelledUtc))}</dd>
								<dt>Cancel reason</dt><dd>${ui.esc(operation.cancelReason ?? '')}</dd>` : ''}
							${operation.outcome ? `<dt>Outcome</dt><dd>${ui.esc(operation.outcome)}</dd>` : ''}
						</dl>`,
				})}

				${ui.card({
					title: 'Servers',
					sub: 'What was written to each one, and what its row says now',
					flush: true,
					body: ui.table({
						columns: [
							{
								label: 'Server',
								cell: (t) => `<div class="cell-primary">${ui.esc(t.serverName)}</div>
									<div class="cell-sub">${ui.esc(t.kind)} server ${ui.esc(String(t.serverId))}</div>`,
							},
							{ label: 'Status', cell: (t) => statusBadge(ui, t.statusName, isLive(t.statusName)) },
							{
								label: 'Players',
								align: 'right',
								cell: (t) => (isLive(t.statusName)
									? `<span class="tnum">${ui.num(t.observedPlayers)}</span>`
									: '<span class="faint">—</span>'),
							},
							{ label: 'Pulse', cell: (t) => pulseCell(ui, t) },
							{ label: 'Written', cell: (t) => writtenCell(ui, t) },
							{
								label: 'What happened',
								cell: (t) => (t.note
									? `<span class="small">${ui.esc(t.note)}</span>`
									: '<span class="faint">—</span>'),
							},
						],
						rows: targets,
						emptyText: 'This window has no servers, which should not be possible.',
					}),
				})}
			</div>`;

		host.querySelector('[data-act="cancel"]')?.addEventListener('click', () => askCancel());
		host.querySelector('[data-act="unlock"]')?.addEventListener('click', () => askUnlock(stillLocked));
	}

	async function askCancel() {
		const targets = operation.targets ?? [];
		const live = targets.filter((t) => isLive(t.statusName));
		const past = Number(operation.secondsUntilDeadline) <= 0;

		const values = await ui.modal({
			title: 'Cancel this maintenance window',
			sub: 'Clears the shutdown deadline on every server that has not finished.',
			confirmLabel: 'Clear the shutdowns',
			confirmTone: 'danger',
			body: `
				${ui.banner('warn', 'This does NOT unlock the servers',
					`Scheduling the shutdown locked them in the same statement, and cancelling only clears the deadline. ${live.length} server(s) will stay closed to new arrivals until you unlock them — there is a button for it on this page afterwards. Leave it undone and nobody can log in, with nothing on the board explaining why.`)}
				${past
					? ui.banner('danger', 'The deadline has already passed',
						'Any server that has read it has already begun stopping, and clearing the column will not bring it back. Cancelling now only affects servers that have not got there yet.')
					: ''}
				<div class="field">
					<label for="mw-cancel-reason">Reason (recorded in the audit log)</label>
					<textarea id="mw-cancel-reason" name="reason" required placeholder="Patch is not ready"></textarea>
				</div>`,
			onSubmit: (v) => (String(v.reason).trim() ? v : false),
		});
		if (!values) return;

		const result = await ctx.attempt(
			() => api.cancelMaintenance(id, values.reason),
			'Shutdowns cleared',
			'The servers are still LOCKED — unlock them below.',
		);
		if (result) await read();
	}

	async function askUnlock(stillLocked) {
		const values = await ui.modal({
			title: `Unlock ${stillLocked.length} server(s)`,
			sub: 'Reopens them to new arrivals. This is the step cancelling does not do for you.',
			confirmLabel: 'Write the unlocks',
			body: `
				${ui.banner('info', 'One write per server',
					'Each one is the same unlock the board writes, and each takes effect when that server next pulses. A server that is not pulsing will not read it.')}
				<ul class="small" style="margin:0 0 var(--sp-3) var(--sp-4)">
					${stillLocked.map((t) => `<li>${ui.esc(t.serverName)} <span class="faint">(${ui.esc(t.kind)} ${ui.esc(String(t.serverId))})</span></li>`).join('')}
				</ul>
				<div class="field">
					<label for="mw-unlock-reason">Reason (recorded in the audit log)</label>
					<textarea id="mw-unlock-reason" name="reason" required placeholder="Maintenance cancelled, reopening"></textarea>
				</div>`,
			onSubmit: (v) => (String(v.reason).trim() ? v : false),
		});
		if (!values) return;

		let unlocked = 0;
		for (const target of stillLocked) {
			/* One call per server rather than one bulk endpoint: the unlock that exists is the
			 * per-server one, and inventing a second path to the same column would be a second
			 * thing to keep in step with it. A failure stops nothing — the remaining servers are
			 * still worth unlocking. */
			const done = await ctx.attempt(
				() => api.setServerLock(target.kind, target.serverId, false, values.reason),
				null,
			);
			if (done) unlocked += 1;
		}

		ui.toast(
			unlocked === stillLocked.length ? 'Unlocks written' : 'Some unlocks did not land',
			`${unlocked} of ${stillLocked.length} written. Each server reopens when it next pulses.`,
			unlocked === stillLocked.length ? 'ok' : 'warn',
			7000,
		);
		await read();
	}
}

/* ── Pieces ──────────────────────────────────────────────────── */

/** The banner at the top of one window, which is the page's whole answer in a sentence. */
function headlineBanner(ui, operation, stillLocked) {
	if (operation.statusName === 'Draining') {
		return ui.banner('warn', 'Locked, and draining',
			`Every server is closed to new arrivals and stops at ${ui.dateTime(operation.deadlineUtc)}. ` +
			`${operation.playersRemaining} player(s) are still on them. This happens whether or not anything is watching: the deadline is on the servers' own rows.`);
	}
	if (operation.statusName === 'ShuttingDown') {
		return ui.banner('warn', 'The deadline has passed',
			'Each server stops as it reads its row. Any that are still pulsing a couple of minutes from now have not acted on it, and will be marked failed rather than left looking busy.');
	}
	if (operation.statusName === 'Completed') {
		return ui.banner('ok', 'Every server stopped',
			operation.outcome ?? 'The window finished. Nothing here starts a server again — that is the daemon\'s job, or the host\'s.');
	}
	if (operation.statusName === 'Cancelled') {
		return ui.banner(stillLocked.length ? 'danger' : 'warn',
			stillLocked.length
				? `Cancelled — and ${stillLocked.length} server(s) are STILL LOCKED`
				: 'Cancelled',
			stillLocked.length
				? 'Clearing the shutdown does not lift the lock that scheduling applied. Until these are unlocked nobody can log in, and nothing on the server board says why. Use the unlock button above.'
				: (operation.outcome ?? 'Nothing had been written to any server.'));
	}
	return ui.banner('danger', 'This window did not do what it said',
		operation.outcome ?? 'One or more servers were not taken down. The rows below say which, and why.');
}

/** Status badge with the tones used across the panel. */
function statusBadge(ui, statusName, pulse) {
	const tone = statusName === 'Completed' ? 'ok'
		: statusName === 'Failed' ? 'danger'
		: statusName === 'Cancelled' ? ''
		: 'warn';
	return ui.statusBadge(String(statusName), tone, Boolean(pulse));
}

/** Whether a target is still in play. */
const isLive = (statusName) => statusName === 'Draining' || statusName === 'ShuttingDown';

/** How far through its servers a window is. */
function progressCell(ui, operation) {
	const c = operation.counts;
	const done = c.completed + c.cancelled + c.failed;
	return `<div class="tnum">${ui.num(done)} of ${ui.num(c.total)} finished</div>
		<div class="cell-sub">${ui.esc(countsSentence(c))}</div>`;
}

function countsSentence(counts) {
	const parts = [];
	if (counts.draining) parts.push(`${counts.draining} draining`);
	if (counts.shuttingDown) parts.push(`${counts.shuttingDown} stopping`);
	if (counts.completed) parts.push(`${counts.completed} stopped`);
	if (counts.cancelled) parts.push(`${counts.cancelled} cancelled`);
	if (counts.failed) parts.push(`${counts.failed} failed`);
	return parts.length ? parts.join(', ') : 'nothing yet';
}

/** The deadline, with a live countdown while the window is running. */
function deadlineCell(ui, operation) {
	if (!operation.active) {
		return `<span class="faint">${ui.esc(ui.dateTime(operation.deadlineUtc))}</span>`;
	}
	return `<div>${ui.esc(ui.dateTime(operation.deadlineUtc))}</div>
		<div class="cell-sub">${countdownSpan(operation.deadlineUtc)}</div>`;
}

function deadlineCountdown(ui, operation) {
	return operation.active ? ` <span class="faint">·</span> ${countdownSpan(operation.deadlineUtc)}` : '';
}

/* The countdown element. Its text is rewritten every second by tickCountdowns and never
 * contains anything but digits and units, so nothing operator-supplied passes through it. */
function countdownSpan(deadlineUtc) {
	const at = Date.parse(deadlineUtc);
	return Number.isFinite(at) ? `<span class="tnum" data-countdown="${at}"></span>` : '';
}

function tickCountdowns(host) {
	const now = Date.now();
	for (const element of host.querySelectorAll('[data-countdown]')) {
		const at = Number(element.dataset.countdown);
		if (!Number.isFinite(at)) continue;
		const seconds = Math.round((at - now) / 1000);
		element.textContent = seconds > 0 ? `in ${clock(seconds)}` : `${clock(-seconds)} ago`;
	}
}

function clock(seconds) {
	const s = Math.max(0, Math.floor(seconds));
	const h = Math.floor(s / 3600);
	const m = Math.floor((s % 3600) / 60);
	const sec = s % 60;
	const pad = (n) => String(n).padStart(2, '0');
	return h > 0 ? `${h}:${pad(m)}:${pad(sec)}` : `${m}:${pad(sec)}`;
}

/** Whether the server is still talking, which decides whether any of this reached it. */
function pulseCell(ui, target) {
	if (!target.observedRegistered) {
		return '<span class="faint">registration gone</span>';
	}
	if (target.observedLastPulseUtc == null) {
		return '<span class="faint">—</span>';
	}
	const text = ui.esc(ui.ago(target.observedLastPulseUtc));
	return target.observedStale
		? `${text}<div class="cell-sub"><span class="badge badge-danger">not pulsing</span></div>`
		: text;
}

/** What this window actually wrote to the server, and when. */
function writtenCell(ui, target) {
	if (!target.shutdownWrittenUtc) {
		return '<span class="badge badge-danger">nothing written</span>';
	}
	const lines = [`<div class="small">locked + deadline ${ui.esc(ui.shortTime(target.shutdownWrittenUtc))}</div>`];
	if (target.shutdownClearedUtc) {
		lines.push(`<div class="cell-sub">deadline cleared ${ui.esc(ui.shortTime(target.shutdownClearedUtc))}${target.stillLocked ? ' — still locked' : ''}</div>`);
	} else if (!target.pulsingAtStart) {
		lines.push('<div class="cell-sub">was not pulsing when this was written</div>');
	}
	return lines.join('');
}

/* ── The plan dialog ─────────────────────────────────────────── */

/** One tier's worth of checkboxes, with everything that should give an operator pause. */
function targetGroup(ui, title, servers, kind) {
	if (!servers.length) {
		return `<div class="small muted" style="margin-bottom:var(--sp-2)">No ${ui.esc(title.toLowerCase())} are registered.</div>`;
	}
	return `
		<div class="small" style="margin:var(--sp-2) 0 var(--sp-1);font-weight:600">${ui.esc(title)}</div>
		<div class="stack-sm" style="margin-bottom:var(--sp-2)">
			${servers.map((s) => `
				<label class="row" style="gap:var(--sp-2);align-items:flex-start">
					<input type="checkbox" name="target" value="${ui.esc(kind)}:${ui.esc(String(s.id))}"
						data-players="${Number(s.characterCount) || 0}" />
					<span>
						<span>${ui.esc(s.name)}</span>
						<span class="faint small"> · ${ui.num(Number(s.characterCount) || 0)} player(s)</span>
						${s.stale ? '<span class="badge badge-danger">not pulsing</span>' : ''}
						${s.locked ? '<span class="badge">already locked</span>' : ''}
						${s.shutdownAtUtc ? `<span class="badge badge-warn">shutdown already set for ${ui.esc(ui.shortTime(s.shutdownAtUtc))}</span>` : ''}
						${s.stale ? '<div class="cell-sub">This server is not pulsing. The rows will be written and nothing will read them.</div>' : ''}
						${s.shutdownAtUtc ? '<div class="cell-sub">Including it here overwrites that deadline with this window\'s.</div>' : ''}
					</span>
				</label>`).join('')}
		</div>`;
}

function selectedTargets(form) {
	return Array.from(form.querySelectorAll('input[name="target"]:checked')).map((input) => {
		const [kind, serverId] = String(input.value).split(':');
		return { kind, serverId: Number(serverId) };
	});
}

/**
 * The preview, and it is the point of the dialog: what will be written, to how many
 * servers, affecting how many players, and at what wall-clock time. Built with
 * textContent rather than markup — the operator's own typing reaches it on every
 * keystroke, and a preview is not worth an injection seam.
 */
function wirePlanDialog(ui, form) {
	const drain = form.querySelector('#mw-drain');
	const preview = form.querySelector('#mw-preview');

	const sync = () => {
		const targets = selectedTargets(form);
		const players = Array.from(form.querySelectorAll('input[name="target"]:checked'))
			.reduce((sum, input) => sum + (Number(input.dataset.players) || 0), 0);
		const seconds = Number(drain.value);

		const box = document.createElement('div');
		box.className = 'banner banner-info';
		const body = document.createElement('div');
		body.className = 'banner-body';

		const title = document.createElement('div');
		title.className = 'banner-title';
		const text = document.createElement('div');
		text.className = 'muted small';

		if (!targets.length) {
			title.textContent = 'Nothing selected';
			text.textContent = 'Pick the servers this window covers. Each one is locked the moment you confirm.';
		} else if (!Number.isFinite(seconds) || seconds < 0 || drain.value.trim() === '') {
			title.textContent = `${targets.length} server(s) selected`;
			text.textContent = 'Give a whole number of seconds for the drain, or pick one above.';
		} else if (seconds > MAX_DRAIN_SECONDS) {
			title.textContent = 'That drain is too long';
			text.textContent = `The API refuses anything beyond ${ui.duration(MAX_DRAIN_SECONDS)}, so a mistyped drain cannot lock a shard for weeks.`;
		} else {
			const landsAt = new Date(Date.now() + seconds * 1000);
			title.textContent = seconds === 0
				? `${targets.length} server(s) lock and stop at their next pulse`
				: `${targets.length} server(s) lock now, and stop at ${landsAt.toLocaleTimeString()}`;
			text.textContent = seconds === 0
				? `${players} player(s) are on them and get one warning, not a drain. The deadline is written in the past, so each server acts the instant it reads its row.`
				: `${players} player(s) are on them now and have ${ui.duration(seconds)} to finish up, warned as the countdown passes 15m, 10m, 5m, 2m, 1m, 30s and 10s. `
					+ 'Cancelling later clears the deadline but leaves every one of them locked.';
		}

		body.append(title, text);
		box.append(body);
		preview.replaceChildren(box);
	};

	for (const preset of form.querySelectorAll('[data-preset]')) {
		preset.addEventListener('click', () => {
			drain.value = preset.dataset.preset;
			sync();
		});
	}
	drain.addEventListener('input', sync);
	for (const box of form.querySelectorAll('input[name="target"]')) {
		box.addEventListener('change', sync);
	}
	sync();
}

/** A refusal the dialog makes itself, shown where the server's own refusals appear. */
function flash(form, ui, message) {
	let box = form.querySelector('.modal-error');
	if (!box) {
		box = document.createElement('div');
		box.className = 'modal-error';
		form.prepend(box);
	}
	box.innerHTML = ui.banner('danger', message);
}
