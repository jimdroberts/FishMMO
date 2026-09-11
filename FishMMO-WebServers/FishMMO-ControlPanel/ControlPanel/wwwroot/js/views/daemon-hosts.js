/*
 * Daemon → Hosts and processes. Every machine running a supervising daemon, what that daemon
 * is looking after, and the only three things it can be asked to do.
 *
 * WHAT A BUTTON ON THIS PAGE DOES.
 *
 * It writes a row. The daemon on that host polls for rows addressed to it, claims one, runs
 * it on its own machine, and writes back what happened. Nothing here opens a connection to a
 * host, and no acknowledgement on this page can say a process restarted — only that the
 * command was QUEUED. An operator who believes a restart has already happened, when the row
 * has not been collected, presses the button again; that is the failure this wording exists
 * to prevent, and it is the same reasoning as the server board next door.
 *
 * TWO KINDS OF "DOWN", AND THEY ARE NOT THE SAME FAULT.
 *
 *   - A STALE HOST is the daemon itself gone silent. Nothing on that machine is being
 *     supervised — a process that crashes there will not be restarted — and no command
 *     queued for it will be collected, so it sits until its deadline and is abandoned. That
 *     is a fault on the host, and it is the loudest thing on this page.
 *   - A STOPPED PROCESS on a beating host is one process down while its supervisor is fine.
 *     The daemon can see it, is probably already restarting it, and will collect a command
 *     about it within seconds.
 *
 * Blurring those two sends somebody to the wrong machine, so the page never uses one word
 * for both. Within "not running" there are three more distinctions the daemon itself makes:
 * `Stopped` is down and being worked on, `Exhausted` is down and abandoned by the supervisor
 * — which needs a person — and `StoppedByOperator` is down because somebody meant it, where
 * the supervisor is deliberately not intervening and Start is the obvious action.
 *
 * Host names, application names and daemon versions are written by daemons on other
 * machines. They are not trusted input, so every one of them goes through `ui.esc`.
 */

import { lifecycle, verbBadge } from './daemon-events.js';

/* The page polls at roughly the daemon's own heartbeat rate. Faster shows the same numbers
 * back; slower makes a queued command look ignored, because the claim that proves a daemon
 * picked it up would arrive long after the operator stopped watching for it. */
const REFRESH_MS = 5000;

/* Used only when the payload carries no `staleAfterSeconds`. The server decides the
 * threshold; this is here so a missing field cannot silently turn staleness off, which would
 * leave a dead daemon looking exactly like a live one. */
const FALLBACK_STALE_SECONDS = 45;

/* How many recent commands the strip at the bottom shows. It exists so an operator who has
 * just queued something can watch it be claimed and finished without leaving the page — long
 * enough to cover a burst of commands during a maintenance window, short enough that it does
 * not become a second command log. */
const RECENT_COMMANDS = 8;

/* Status integers from DaemonAppStatus. They are compared as numbers rather than by name
 * because `statusName` is a convenience on the payload and the integer is the contract. */
const HEALTHY = 1;
const STARTING = 2;
const STOPPED = 3;
const EXHAUSTED = 4;
const STOPPED_BY_OPERATOR = 5;

/* Verb integers from DaemonCommandVerb. A closed set on the server; a closed set here. */
const START = 0;
const STOP = 1;
const RESTART = 2;

const VERBS = {
	[START]: {
		label: 'Start',
		gerund: 'start',
		tone: 'primary',
		explain: 'The daemon launches this application from the definition in its own configuration file on that host, and begins health checking it. The panel sends a name and nothing else — no path, no arguments.',
	},
	[STOP]: {
		label: 'Stop',
		gerund: 'stop',
		tone: 'danger',
		explain: 'The daemon stops the process and leaves it stopped: it reports back as stopped by an operator, and the supervisor will not put it back. Anyone connected to it is disconnected when it goes.',
	},
	[RESTART]: {
		label: 'Restart',
		gerund: 'restart',
		tone: 'danger',
		explain: 'The daemon stops the process and launches it again. Anyone connected to it is disconnected; the gap is however long that process takes to come back.',
	},
};

export async function render(host, ctx) {
	const { api, ui } = ctx;

	let poll = null;

	/* Registered before anything awaits, and before the first read can throw: the router keeps
	 * one cleanup per view, so the timer is cancelled on navigation whatever happens below. A
	 * missed cleanup here is a page the operator has left still polling a host list every five
	 * seconds for the rest of the session. */
	ctx.onCleanup(() => clearInterval(poll));

	let payload = null;
	let commands = null;
	// Set when the command strip cannot be read, so its own failure is reported where it is.
	let commandsError = null;
	let readAt = 0;
	let refreshError = null;

	async function read() {
		/* Both reads go out together so the hosts and the commands queued against them describe
		 * the same instant — a command shown as waiting beside a host read five seconds earlier
		 * is a pair of facts that never coexisted. */
		const [hosts, recent] = await Promise.all([api.getDaemonHosts(), readCommands()]);
		payload = hosts;
		commands = recent;
		readAt = Date.now();
		refreshError = null;
		paint();
	}

	/* The command strip is secondary: if that route fails the hosts and their controls are
	 * still worth showing, so it swallows its own failure and says so in its own card rather
	 * than taking the page down with it. */
	async function readCommands() {
		try {
			const page = await api.getDaemonCommands({ page: 1, pageSize: RECENT_COMMANDS });
			commandsError = null;
			return page?.items ?? [];
		} catch (err) {
			commandsError = err.message || 'The command log could not be read.';
			return null;
		}
	}

	/* A failed poll keeps what is on screen rather than blanking it: the last good read,
	 * labelled as not refreshing, is more use mid-incident than an error page where the hosts
	 * were. */
	async function pollOnce() {
		try {
			await read();
		} catch (err) {
			refreshError = err.message || 'The host list could not be refreshed.';
			if (payload) paint();
		}
	}

	/* Started before the first read so the page heals itself: if the first read fails the
	 * router renders that failure, and the first poll that succeeds paints over it. */
	poll = setInterval(pollOnce, REFRESH_MS);

	// The first read is allowed to throw — the router turns it into a visible failure.
	await read();

	function paint() {
		const staleAfter = Number(payload?.staleAfterSeconds) > 0
			? Number(payload.staleAfterSeconds)
			: FALLBACK_STALE_SECONDS;

		const hosts = decorate(payload?.hosts, staleAfter);
		const stale = hosts.filter((h) => h.isStale);
		const apps = hosts.flatMap((h) => h.apps);
		const healthy = apps.filter((a) => Number(a.status) === HEALTHY);
		const starting = apps.filter((a) => Number(a.status) === STARTING);
		const exhausted = apps.filter((a) => Number(a.status) === EXHAUSTED);
		const operatorStopped = apps.filter((a) => Number(a.status) === STOPPED_BY_OPERATOR);
		const down = apps.filter((a) => isDown(a.status));

		host.innerHTML = `
			<div class="page-head">
				<div class="page-head-text">
					<h1>Hosts and processes</h1>
					<p class="page-head-sub">
						Every machine running a supervising daemon, and the processes it watches. Start, stop
						and restart are written here as commands and carried out there: the panel queues a
						row, the daemon collects it on its next poll and runs it on its own host. This page
						refreshes every ${REFRESH_MS / 1000} seconds, which is roughly the daemon's own heartbeat.
					</p>
				</div>
				<div class="page-head-actions">
					<a class="btn" href="#/daemon/events">${ui.icon('list')} Command log</a>
					<button class="btn" data-act="refresh">${ui.icon('refresh')} Refresh</button>
				</div>
			</div>

			<div class="stack-lg">
				<div class="grid grid-4">
					${ui.stat({
						label: 'Hosts reporting',
						value: ui.num(hosts.length - stale.length),
						note: `of ${ui.num(hosts.length)} known to the panel`,
					})}
					${ui.stat({
						label: 'Hosts silent',
						value: ui.num(stale.length),
						noteTone: stale.length ? 'danger' : 'ok',
						note: stale.length
							? `No heartbeat in ${ui.duration(staleAfter)} — nothing there is supervised`
							: 'Every daemon is beating',
					})}
					${ui.stat({
						label: 'Processes healthy',
						value: ui.num(healthy.length),
						note: starting.length
							? `of ${ui.num(apps.length)} supervised · ${ui.num(starting.length)} still starting`
							: `of ${ui.num(apps.length)} supervised`,
					})}
					${ui.stat({
						label: 'Processes down',
						value: ui.num(down.length),
						noteTone: exhausted.length ? 'danger' : down.length ? 'warn' : 'ok',
						note: downNote(ui, down, exhausted, operatorStopped),
					})}
				</div>

				${refreshError
					? ui.banner('warn', 'This page is not refreshing',
						`${refreshError} What you are looking at was read at ${new Date(readAt).toLocaleTimeString()} and is not being updated, so a process that has changed state since will not show it.`)
					: ''}

				${stale.length ? staleBanner(ui, stale, staleAfter) : ''}

				${exhausted.length ? exhaustedBanner(ui, exhausted) : ''}

				${ui.banner('info', 'Commands here are queued, not run',
					'Pressing Start, Stop or Restart writes a row. The daemon on that host collects it on its next poll — normally within about ten seconds — runs it, and reports back. Until it does, nothing has happened to the process. Watch the command through to "done" below rather than pressing again.')}

				${hosts.length
					? hosts.map((h) => hostCard(ui, h)).join('')
					: ui.empty('No daemon has reported',
						'No host has written a heartbeat, so the panel does not know of any machine running a supervising daemon. Either none is running, or none of them can reach the database. Nothing can be started, stopped or restarted from here until one reports: a command may only name a host and an application some daemon has already told the panel about.',
						'activity')}

				${commandsCard(ui, commands, commandsError)}
			</div>`;

		wire();
	}

	function wire() {
		host.querySelector('[data-act="refresh"]')?.addEventListener('click', () => pollOnce());
		for (const button of host.querySelectorAll('[data-verb]')) {
			button.addEventListener('click', () => onCommand(button));
		}
	}

	/* One handler for every control. The button carries the whole target — host, application,
	 * verb, the application's status and how long the daemon has been silent — so the dialog
	 * can warn accurately without going back to a list that may have been repainted by a poll
	 * between the click and the dialog opening. */
	async function onCommand(button) {
		await askCommand({
			host: button.dataset.host,
			app: button.dataset.app,
			verb: Number(button.dataset.verb),
			status: Number(button.dataset.status),
			stale: button.dataset.stale === 'true',
			// An absent age must not read as zero seconds, which would print "silent for 0s".
			ageSeconds: button.dataset.age ? Number(button.dataset.age) : NaN,
		});
	}

	async function askCommand(target) {
		const verb = VERBS[target.verb];
		if (!verb) return;

		const values = await ui.modal({
			title: `${verb.label} ${target.app} on ${target.host}`,
			sub: 'Writes a command row. The daemon on that host collects it on its next poll and carries it out there.',
			confirmLabel: `Queue the ${verb.gerund}`,
			confirmTone: verb.tone,
			body: `
				${target.stale ? staleWarning(ui, target) : ''}
				${ui.banner('info', 'What the daemon does once it collects this', verb.explain)}
				${stateWarning(ui, target)}
				${ui.banner('warn', 'An uncollected command is abandoned, not run late',
					'The row carries a deadline a few minutes out. If no daemon collects it before then it expires and never runs — a restart carried out long after it was asked for, with players back on the server, is worse than one that never happened.')}
				${reasonField()}`,
			onSubmit: (v) => (String(v.reason).trim() ? v : false),
		});
		if (!values) return;

		await ctx.attempt(
			() => api.runDaemonCommand({
				hostName: target.host,
				appName: target.app,
				verb: target.verb,
				reason: values.reason,
			}),
			`${verb.label} queued`,
			queuedText(target, verb),
		);
		await refresh();
	}

	/* Re-reads after a command so the new row appears in the strip at once instead of up to
	 * five seconds later. It swallows its own failure on purpose: the command has already been
	 * acknowledged, and a page that cannot be re-read is the poll's problem to announce. */
	async function refresh() {
		try {
			await read();
		} catch {
			/* left to the poll */
		}
	}
}

/* ── Shaping ─────────────────────────────────────────────────── */

/**
 * Adds the derived flags the rest of the view keys off.
 *
 * The server decides what stale means and sends `stale`; the age comparison behind it is only
 * a fallback for a payload that omits the flag. A page that quietly failed to mark a silent
 * daemon is the exact failure this one exists to prevent, so it does not rest on one field
 * arriving.
 */
function decorate(hosts, staleAfter) {
	return (hosts ?? []).map((h) => ({
		...h,
		apps: h.apps ?? [],
		isStale: h.stale === true ||
			(h.stale === undefined && Number.isFinite(h.heartbeatAgeSeconds) && h.heartbeatAgeSeconds > staleAfter),
	}));
}

/* "Down" is every state where the process is not running, deliberate or not — an operator
 * counting processes wants the count of things not serving players. The difference between
 * deliberate and not is made in the badge and in the note under the tile, where there is room
 * to say which. `Unknown` is excluded: the daemon has not said, and guessing is how a page
 * starts reporting faults that are not there. */
function isDown(status) {
	const s = Number(status);
	return s === STOPPED || s === EXHAUSTED || s === STOPPED_BY_OPERATOR;
}

function downNote(ui, down, exhausted, operatorStopped) {
	if (!down.length) return 'Every supervised process is running';
	/* Ordered by how much of a person's attention each one deserves: a process that fell over
	 * first, one the supervisor has abandoned next, and one somebody took down on purpose
	 * last — that one is not a fault at all. */
	const parts = [];
	const crashed = down.length - exhausted.length - operatorStopped.length;
	if (crashed > 0) parts.push(`${ui.num(crashed)} not running`);
	if (exhausted.length) parts.push(`${ui.num(exhausted.length)} the supervisor gave up on`);
	if (operatorStopped.length) parts.push(`${ui.num(operatorStopped.length)} stopped on purpose`);
	return parts.join(' · ');
}

/* ── Host card ───────────────────────────────────────────────── */

function hostCard(ui, h) {
	const apps = h.apps;
	const healthy = apps.filter((a) => Number(a.status) === HEALTHY).length;
	const exhausted = apps.filter((a) => Number(a.status) === EXHAUSTED).length;
	const operatorStopped = apps.filter((a) => Number(a.status) === STOPPED_BY_OPERATOR).length;
	const crashed = apps.filter((a) => Number(a.status) === STOPPED).length;

	return ui.card({
		title: h.hostName,
		sub: `${h.osDescription ?? 'unknown system'} · ${ui.num(h.processorCount)} cores · daemon ${h.daemonVersion ?? 'unknown'}`,
		actions: `
			${h.isStale ? heartbeatBadge(ui, h) : ui.statusBadge('heartbeat', 'ok', true)}
			${crashed ? ui.badge(`${crashed} not running`, 'danger') : ''}
			${exhausted ? ui.badge(`${exhausted} exhausted`, 'danger') : ''}
			${operatorStopped ? ui.badge(`${operatorStopped} stopped by an operator`, 'info') : ''}
			<span class="badge">${ui.esc(healthy)}/${ui.esc(apps.length)} healthy</span>`,
		flush: true,
		body: `
			${h.isStale ? `<div style="padding:var(--sp-4) var(--sp-4) 0">${ui.banner('danger',
				'This daemon has stopped reporting',
				`Last heartbeat ${ui.ago(h.lastHeartbeatUtc)}. Nothing on ${h.hostName} is being supervised: a process that fails there will not be restarted, and the states below are whatever was true when the daemon last spoke. A command queued for this host will not be collected — it will sit until its deadline and be abandoned. This is a fault on the host itself, not on any one process.`)}</div>` : ''}
			${appsTable(ui, h)}`,
		foot: `Heartbeat ${ui.ago(h.lastHeartbeatUtc)}${Number.isFinite(h.heartbeatAgeSeconds) ? ` (${ui.duration(h.heartbeatAgeSeconds)} ago)` : ''} · daemon up ${uptime(ui, h.startedUtc)}`,
	});
}

/* Carries how long the daemon has been silent rather than the word "stale" alone: "silent for
 * 14m" is a fact an operator can act on, and it is the one label on this page that grows more
 * alarming the longer nobody deals with it. */
function heartbeatBadge(ui, h) {
	return ui.statusBadge(
		Number.isFinite(h.heartbeatAgeSeconds) ? `silent for ${ui.duration(h.heartbeatAgeSeconds)}` : 'no heartbeat',
		'danger',
	);
}

function uptime(ui, startedUtc) {
	if (!startedUtc) return 'unknown';
	const started = new Date(/[Zz]$|[+-]\d{2}:?\d{2}$/.test(startedUtc) ? startedUtc : startedUtc + 'Z').getTime();
	if (!Number.isFinite(started)) return 'unknown';
	return ui.duration((Date.now() - started) / 1000);
}

function appsTable(ui, h) {
	return ui.table({
		columns: [
			{
				label: 'Process',
				cell: (a) => `
					<div class="cell-primary">${ui.esc(a.name)}</div>
					<div class="cell-sub">${a.monitoredPort ? `port ${ui.esc(a.monitoredPort)}` : 'no port watched'}</div>`,
			},
			{ label: 'Status', cell: (a) => statusCell(ui, a) },
			{
				label: 'PID',
				align: 'right',
				cell: (a) => (a.processId
					? `<span class="tnum">${ui.esc(a.processId)}</span>`
					: '<span class="faint">—</span>'),
			},
			{ label: 'Restarts', align: 'right', cell: (a) => restartsCell(ui, a) },
			{
				label: 'Last report',
				align: 'right',
				cell: (a) => `<span class="nowrap" title="${ui.esc(ui.dateTime(a.lastReportedUtc))}">${ui.ago(a.lastReportedUtc)}</span>`,
			},
			{ label: '', align: 'right', cell: (a) => controlButtons(ui, a, h) },
		],
		rows: h.apps,
		/* Tinted for a process that is down and did not mean to be. A process stopped by an
		 * operator is exactly where somebody put it, and tinting it would make a tidy
		 * maintenance window look like an outage. */
		rowAttrs: (a) => (Number(a.status) === STOPPED || Number(a.status) === EXHAUSTED ? 'class="is-alert"' : ''),
		emptyText: `The daemon on ${h.hostName} is reporting, but says it is supervising nothing. Its configuration file on that host lists no applications.`,
	});
}

/**
 * The badge that says what the daemon last reported about one process.
 *
 * The three "not running" states are kept apart deliberately. `Stopped` is a process the
 * supervisor is still dealing with; `Exhausted` is one it has abandoned, which no amount of
 * waiting fixes and which is why it is the only status that gets its own banner at the top of
 * the page; `StoppedByOperator` is somebody's decision being respected, and reads as an
 * ordinary state rather than as a fault.
 */
function statusCell(ui, app) {
	switch (Number(app.status)) {
		case HEALTHY:
			return ui.statusBadge('healthy', 'ok', true);
		case STARTING:
			return `
				${ui.statusBadge('starting', 'warn', true)}
				<div class="cell-sub">inside its startup grace period</div>`;
		case STOPPED:
			return `
				${ui.statusBadge('not running', 'danger')}
				<div class="cell-sub">the supervisor on this host is fine</div>`;
		case EXHAUSTED:
			return `
				${ui.statusBadge('exhausted', 'danger')}
				<div class="cell-sub">the supervisor gave up restarting it</div>`;
		case STOPPED_BY_OPERATOR:
			return `
				${ui.badge('stopped by an operator', 'info')}
				<div class="cell-sub">deliberate — nothing will restart it</div>`;
		default:
			return `
				${ui.badge('unknown', 'warn')}
				<div class="cell-sub">the daemon has not determined a state</div>`;
	}
}

/* The restart budget, and whether it is spent. A process at its limit is why `Exhausted`
 * happens, so the count is worth reading before it gets there. */
function restartsCell(ui, app) {
	const attempts = Number(app.restartAttempts) || 0;
	const max = Number(app.maxRestartAttempts) || 0;
	const spent = max > 0 && attempts >= max;
	return `<span class="tnum"${spent ? ' style="color:var(--danger)"' : ''}>${ui.esc(attempts)}/${ui.esc(max)}</span>`;
}

/**
 * The three controls, with the ones that make no sense for this state disabled rather than
 * removed.
 *
 * Removing them would be quieter and worse: a Start button that is absent on a healthy
 * process is indistinguishable from a Start button the panel does not have, and an operator
 * looking at an emergency will spend the first minute wondering which. Disabled, with the
 * reason on hover, answers that without offering a command whose only possible outcome is a
 * daemon reporting that it could not do it.
 *
 * On a silent host every control stays enabled. The row is still written, and a daemon that
 * comes back inside the deadline still collects it — the warning belongs in the dialog, which
 * is where the decision is made.
 */
function controlButtons(ui, app, h) {
	const status = Number(app.status);
	const running = status === HEALTHY || status === STARTING;
	const notRunning = isDown(status);
	const common = `data-host="${ui.esc(h.hostName)}" data-app="${ui.esc(app.name)}" data-status="${ui.esc(status)}"` +
		` data-stale="${h.isStale}" data-age="${ui.esc(h.heartbeatAgeSeconds ?? '')}"`;

	/* On a silent host every control is de-emphasised and the whole group carries the reason.
	 * They stay live — a daemon that comes back inside the deadline still collects the row, and
	 * an operator queueing a stop as a machine goes quiet has a real reason to — but a button
	 * that looks as ready as one on a beating host is a button that lies about what it will
	 * achieve. The full warning is in the dialog, where the decision is actually made. */
	const style = h.isStale ? 'btn btn-sm btn-ghost' : 'btn btn-sm';

	const start = running
		? futile(ui, 'Start', `${app.name} is already running. Use Restart to take it down and bring it back.`)
		: `<button class="${style}${notRunning && !h.isStale ? ' btn-primary' : ''}" data-verb="${START}" ${common}>Start</button>`;

	const stop = notRunning
		? futile(ui, 'Stop', `${app.name} is not running, so there is nothing to stop.`)
		: `<button class="${style}" data-verb="${STOP}" ${common}>Stop</button>`;

	const buttons = `
		${start}
		${stop}
		<button class="${style}" data-verb="${RESTART}" ${common}>Restart</button>`;

	return h.isStale
		? `<div class="cell-actions" title="${ui.esc(`${h.hostName}'s daemon is not reporting. A command queued now is written, but nothing is polling for it — it expires unrun unless that daemon comes back first.`)}">${buttons}</div>`
		: `<div class="cell-actions">${buttons}</div>`;
}

/* A disabled button carries no tooltip of its own in several browsers — the pointer events
 * that would show it never reach the element — so the title goes on a wrapper that is not
 * disabled. */
function futile(ui, label, why) {
	return `<span title="${ui.esc(why)}"><button class="btn btn-sm" disabled>${ui.esc(label)}</button></span>`;
}

/* ── Recent commands ─────────────────────────────────────────── */

/**
 * The life cycle of what has just been asked for, on the page where it was asked.
 *
 * This is the half of the screen that stops a command being queued twice. The acknowledgement
 * can only say a row was written, so something has to show that row being claimed and
 * finished; without it the honest wording is just an absence of feedback, and an operator
 * fills an absence of feedback by pressing the button again.
 */
function commandsCard(ui, commands, error) {
	if (error) {
		return ui.card({
			title: 'Recent commands',
			sub: 'What has been queued from this panel, and how far each one has got',
			body: ui.banner('warn', 'The command log could not be read',
				`${error} Commands can still be queued — this is the view of them that is missing, not the queue.`),
		});
	}

	const items = commands ?? [];
	const waiting = items.filter((c) => lifecycle(c).key === 'waiting').length;
	const claimed = items.filter((c) => lifecycle(c).key === 'claimed').length;

	return ui.card({
		title: 'Recent commands',
		sub: 'What has been queued from this panel, and how far each one has got',
		actions: `
			${waiting ? ui.statusBadge(`${waiting} waiting`, 'info', true) : ''}
			${claimed ? ui.statusBadge(`${claimed} being run`, 'warn', true) : ''}
			<a class="btn btn-sm" href="#/daemon/events">Full command log</a>`,
		flush: true,
		body: ui.table({
			columns: [
				{
					label: 'Queued',
					cell: (c) => `<span class="nowrap" title="${ui.esc(ui.dateTime(c.requestedUtc))}">${ui.ago(c.requestedUtc)}</span>`,
				},
				{
					label: 'Target',
					cell: (c) => `
						<div class="cell-primary">${ui.esc(c.appName)}</div>
						<div class="cell-sub">on ${ui.esc(c.hostName)}</div>`,
				},
				{ label: 'Command', cell: (c) => verbBadge(ui, c) },
				{ label: 'Asked by', cell: (c) => (c.requestedBy ? ui.esc(c.requestedBy) : '<span class="faint">—</span>') },
				{ label: 'Life cycle', cell: (c) => progressCell(ui, c) },
			],
			rows: items,
			rowAttrs: (c) => (lifecycle(c).key === 'failed' ? 'class="is-alert"' : ''),
			emptyText: 'Nothing has been queued from this panel. A command appears here the moment it is written, before any daemon has collected it, and stays until it is done or abandoned.',
		}),
		foot: 'Waiting means written and not yet collected. Claimed means a daemon took it and is running it on its host. Expired means no daemon ever collected it, and it will not run.',
	});
}

/* The three steps as one line, so "has it been picked up yet" is answered by shape before it
 * is read. The step that has been reached is the badge; the rest are faint. */
function progressCell(ui, c) {
	const state = lifecycle(c);
	const step = (label, reached) =>
		(reached ? `<span class="small strong">${ui.esc(label)}</span>` : `<span class="small faint">${ui.esc(label)}</span>`);

	if (state.key === 'expired') {
		return `
			${ui.statusBadge('expired', 'warn')}
			<div class="cell-sub">no daemon collected it — it did not run</div>`;
	}

	const claimed = Boolean(c.claimedUtc);
	const finished = Boolean(c.completedUtc);

	return `
		${ui.statusBadge(state.label, state.tone, state.pulse === true)}
		<div class="cell-sub">
			${step('waiting', true)} → ${step('claimed', claimed)} → ${step('finished', finished)}
		</div>
		${finished && c.outcome ? `<div class="cell-sub" style="max-width:32ch;overflow-wrap:anywhere">${ui.esc(c.outcome)}</div>` : ''}`;
}

/* ── Banners and dialog copy ─────────────────────────────────── */

function staleBanner(ui, stale, staleAfter) {
	const names = stale.map((h) => h.hostName).join(', ');
	const one = stale.length === 1;
	return ui.banner('danger',
		one ? '1 daemon has stopped reporting' : `${stale.length} daemons have stopped reporting`,
		`${names}. Nothing has been heard from ${one ? 'it' : 'them'} for longer than ${ui.duration(staleAfter)}. ` +
		`No process on ${one ? 'that machine' : 'those machines'} is being supervised — a crash there will not be restarted — and every process state shown for ${one ? 'it' : 'them'} below is whatever was true when the daemon last spoke. ` +
		'A command queued for a silent daemon is never collected and expires unrun, so this is fixed on the host, not from this page.');
}

function exhaustedBanner(ui, exhausted) {
	const names = exhausted.map((a) => a.name).join(', ');
	const one = exhausted.length === 1;
	return ui.banner('warn',
		one ? '1 process has exhausted its restarts' : `${exhausted.length} processes have exhausted their restarts`,
		`${names}. The supervisor tried, ran out of attempts and stopped trying, so ${one ? 'this one is' : 'these are'} down and staying down with nothing working on ${one ? 'it' : 'them'}. ` +
		'Waiting will not change it. Find out why it kept failing before queueing a start, or it will exhaust its budget again.');
}

function staleWarning(ui, target) {
	return ui.banner('danger',
		Number.isFinite(target.ageSeconds)
			? `${target.host} has not reported for ${ui.duration(target.ageSeconds)}`
			: `${target.host} is not reporting`,
		'This writes a row and nothing more. A daemon collects its commands by polling, and this one is not polling — it may not be running at all. If it does not come back before the deadline on the row, the command expires without ever being run, and nothing will have happened.');
}

/* The state-specific half of the dialog: what this particular command means for a process in
 * this particular state. It is where `Exhausted` and `StoppedByOperator` stop being badges
 * and start being different decisions. */
function stateWarning(ui, target) {
	if (target.status === EXHAUSTED && target.verb !== STOP) {
		return ui.banner('warn', 'The supervisor already gave up on this one',
			'It restarted this process until its budget ran out and then stopped trying. Starting it again does not address whatever kept killing it — if that is still true, this buys one more crash. Look at why it failed first.');
	}
	if (target.status === STOPPED_BY_OPERATOR && target.verb !== STOP) {
		return ui.banner('info', 'Somebody stopped this deliberately',
			'This process is down because an operator asked for it, and the supervisor has been leaving it alone. Starting it undoes that decision, and the supervisor resumes keeping it up — check that whatever it was taken down for is finished.');
	}
	if (target.status === STOPPED && target.verb === RESTART) {
		return ui.banner('info', 'This process is not running',
			'A restart on something already down is a start with extra steps. It is accepted, but Start says what you mean.');
	}
	if (target.status === STARTING && target.verb !== START) {
		return ui.banner('warn', 'This process is still starting',
			'It is inside its startup grace period and has not been judged healthy or unhealthy yet. Interrupting it now means you will not find out which it would have been.');
	}
	return '';
}

/* The acknowledgement. It says the row was WRITTEN, and where to watch what becomes of it —
 * never that the process restarted, which is a claim the panel is in no position to make. */
function queuedText(target, verb) {
	if (target.stale) {
		return `The row is written. ${target.host}'s daemon is not reporting, so nothing has collected it — and if nothing does before the deadline, it expires without running.`;
	}
	return `${verb.label} queued for ${target.app} on ${target.host}. The daemon picks it up on its next poll, normally within about ten seconds. Watch it reach "done" under Recent commands below.`;
}

function reasonField() {
	return `
		<div class="field">
			<label for="dm-reason">Reason (recorded in the audit log)</label>
			<textarea id="dm-reason" name="reason" required placeholder="Why is this being queued?"></textarea>
		</div>`;
}
