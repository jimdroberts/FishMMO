/*
 * Servers → Board. Every server process the shard knows about, and the controls that act
 * on them.
 *
 * WHAT THIS PAGE CAN AND CANNOT DO.
 *
 * Every control here writes a column on the server's own database row. Each world and
 * scene server reads its row back on its pulse — five seconds by default — and adopts what
 * it finds. Nothing on this page opens a connection to a process, sends it a signal, or
 * waits for it to answer. That single fact decides most of what follows:
 *
 *   - an acknowledgement can only say the row was WRITTEN. At the moment the toast appears
 *     the server has not acted, and may not for another pulse. Saying "server locked" would
 *     be a claim the panel is in no position to make;
 *   - a server whose pulse has stopped may be a process that is gone, and a row written for
 *     a process that is gone is read by nobody, ever. So a stale server is the loudest thing
 *     on this board, and every dialog repeats the warning before the operator confirms;
 *   - there is no start, stop or restart. A process that is not running polls nothing, so
 *     "start" cannot be expressed as a row at all. The page says so rather than leaving an
 *     operator hunting for buttons that were never there.
 *
 * Server names and addresses are written into the database by the servers themselves, so
 * every one of them goes through `ui.esc` on the way into markup.
 */

/* The board polls at the servers' own pulse rate. A pulse is the only moment any of these
 * values can change, so polling faster shows the same numbers back, and polling slower
 * makes an adopted change look like it was ignored. */
const REFRESH_MS = 5000;

/* A pending shutdown counts down on its own second so it reads like a clock instead of
 * jumping five seconds at a time. Only the countdown text is rewritten; everything else
 * waits for the poll. */
const COUNTDOWN_MS = 1000;

/* Presets for the shutdown dialog, the free field beside them takes anything else. "Now"
 * is zero seconds, and the dialog is explicit that zero still means "when the server next
 * reads its row" rather than "this instant". */
const SHUTDOWN_PRESETS = [
	{ label: 'Now', seconds: 0 },
	{ label: '60 seconds', seconds: 60 },
	{ label: '5 minutes', seconds: 300 },
	{ label: '15 minutes', seconds: 900 },
	{ label: '1 hour', seconds: 3600 },
];

/* Used only when the payload carries no `staleAfterSeconds`. The server decides the
 * threshold; this is here so a missing field cannot silently turn staleness off. */
const FALLBACK_STALE_SECONDS = 60;

/* The ceiling the API enforces on a shutdown delay. It bounds a typo rather than a policy —
 * "shutdown 6000000" should be refused, not lock a world for eleven weeks — and it is
 * repeated here so the refusal happens in the dialog instead of after the operator has
 * typed their reason and pressed confirm. */
const MAX_SHUTDOWN_SECONDS = 86400;

export async function render(host, ctx) {
	const { api, ui } = ctx;

	let poll = null;
	let countdown = null;

	/* Registered before anything awaits, and clearing BOTH timers in one function: the
	 * router keeps a single cleanup per view, so a second `onCleanup` call would replace
	 * this one and leave a timer firing into a page the operator has already left. */
	ctx.onCleanup(() => {
		clearInterval(poll);
		clearInterval(countdown);
	});

	let board = null;
	/* When the board on screen was read. The countdown is anchored to this rather than to
	 * the deadline timestamp, so a clock that disagrees with the server's by a few seconds
	 * cannot make a countdown run backwards. */
	let readAt = 0;
	let refreshError = null;

	async function read() {
		board = await api.getServerBoard();
		readAt = Date.now();
		refreshError = null;
		paint();
	}

	/* A failed poll keeps the board that is on screen rather than blanking it: the last good
	 * read, clearly labelled as stale, is more use to somebody mid-incident than an error
	 * page where the numbers were. */
	async function pollOnce() {
		try {
			await read();
		} catch (err) {
			refreshError = err.message || 'The board could not be refreshed.';
			if (board) paint();
		}
	}

	/* Started before the first read so the page heals itself: if the first read fails the
	 * router renders the failure, and the first poll that succeeds paints the board over
	 * the top of it. */
	poll = setInterval(pollOnce, REFRESH_MS);
	countdown = setInterval(() => tickCountdowns(ui, host), COUNTDOWN_MS);

	// The first read is allowed to throw — the router turns it into a visible failure.
	await read();

	function paint() {
		const staleAfter = Number(board.staleAfterSeconds) > 0
			? Number(board.staleAfterSeconds)
			: FALLBACK_STALE_SECONDS;

		const login = decorate(board.loginServers, staleAfter);
		const worlds = decorate(board.worldServers, staleAfter);
		const scenes = decorate(board.sceneServers, staleAfter);
		const all = [...login, ...worlds, ...scenes];
		const stale = all.filter((s) => s.isStale);
		const players = worlds.reduce((sum, s) => sum + (Number(s.characterCount) || 0), 0);

		host.innerHTML = `
			<div class="page-head">
				<div class="page-head-text">
					<h1>Server board</h1>
					<p class="page-head-sub">
						Every server that has registered itself, and what its row currently says. Lock and
						shutdown are columns on that row: writing one here does not reach into a process,
						it leaves an instruction the server picks up on its next pulse. The board refreshes
						every ${REFRESH_MS / 1000} seconds, which is that same pulse rate.
					</p>
				</div>
				<div class="page-head-actions">
					<button class="btn" data-act="refresh">${ui.icon('refresh')} Refresh</button>
				</div>
			</div>

			<div class="stack-lg">
				<div class="grid grid-4">
					${ui.stat({
						label: 'Servers pulsing',
						value: ui.num(all.length - stale.length),
						note: `of ${ui.num(all.length)} registered`,
					})}
					${ui.stat({
						label: 'Servers stale',
						value: ui.num(stale.length),
						noteTone: stale.length ? 'danger' : 'ok',
						note: stale.length
							? `No pulse in ${ui.duration(staleAfter)}`
							: 'Every registered server is pulsing',
					})}
					${ui.stat({
						label: 'Players online',
						value: ui.num(players),
						note: 'Counted from the world server rows',
					})}
					${ui.stat({
						label: 'Live scene instances',
						value: ui.num(board.sceneInstanceCount ?? 0),
						note: 'Listed under Scene instances',
					})}
				</div>

				${refreshError
					? ui.banner('warn', 'This board is not refreshing',
						`${refreshError} What you are looking at was read at ${new Date(readAt).toLocaleTimeString()} and is not being updated.`)
					: ''}

				${stale.length ? staleBanner(ui, stale, staleAfter) : ''}

				${ui.banner('info', 'There is no start, stop or restart here',
					'Every control on this page is a row that a running server reads back and adopts. A stopped process reads nothing, so starting one cannot be written down — it needs an agent on the host, and that does not exist yet. Until it does, bringing a server back up is done on the host itself.')}

				${all.length ? `
					${ui.card({
						title: 'World servers',
						sub: 'Population, lock state and any scheduled shutdown',
						flush: true,
						body: controlledTable(ui, worlds, 'world', readAt,
							'No world server has registered. Nothing is running, or nothing can reach the database.'),
					})}

					${ui.card({
						title: 'Scene servers',
						sub: 'A locked scene server keeps the instances it holds; the world stops sending it new ones',
						flush: true,
						body: controlledTable(ui, scenes, 'scene', readAt,
							'No scene server has registered, so no scene can be loaded for anybody.'),
					})}

					${ui.card({
						title: 'Login servers',
						sub: 'No controls: the login tier has no lock or shutdown column. It is here so you can see the authentication tier is alive',
						flush: true,
						body: loginTable(ui, login),
					})}`
					: ui.empty('No server has registered',
						'Nothing has written a row to the login, world or scene server tables. A server registers itself as it starts and pulses from then on, so an empty board means nothing is running — or nothing running can reach the database.',
						'server')}
			</div>`;

		wire();
	}

	function wire() {
		host.querySelector('[data-act="refresh"]')?.addEventListener('click', () => pollOnce());
		for (const button of host.querySelectorAll('[data-control]')) {
			button.addEventListener('click', () => onControl(button));
		}
	}

	/* One handler for every control. The button carries the whole target — kind, id, name,
	 * and how long it has been silent — so the dialogs below can warn about a dead server
	 * without going back to look it up in a board that may have been repainted since. */
	async function onControl(button) {
		const target = {
			kind: button.dataset.kind,
			id: button.dataset.id,
			name: button.dataset.name,
			stale: button.dataset.stale === 'true',
			// An absent age must not read as zero seconds, which would print "not pulsed for 0s".
			ageSeconds: button.dataset.age ? Number(button.dataset.age) : NaN,
			overdue: button.dataset.overdue === 'true',
		};

		if (button.dataset.control === 'lock') await askLock(target, true);
		else if (button.dataset.control === 'unlock') await askLock(target, false);
		else if (button.dataset.control === 'shutdown') await askShutdown(target);
		else if (button.dataset.control === 'cancel') await askCancelShutdown(target);
	}

	async function askLock(target, locking) {
		const values = await ui.modal({
			title: locking ? `Lock ${target.name}` : `Unlock ${target.name}`,
			sub: locking
				? 'Writes the locked column on this server\'s row. Nobody is disconnected.'
				: 'Clears the locked column on this server\'s row.',
			confirmLabel: locking ? 'Write the lock' : 'Write the unlock',
			body: `
				${target.stale ? staleWarning(ui, target) : ''}
				${ui.banner('info', 'What the server does once it reads this', lockExplainer(target.kind, locking))}
				${reasonField()}`,
			onSubmit: (v) => (String(v.reason).trim() ? v : false),
		});
		if (!values) return;

		await ctx.attempt(
			() => api.setServerLock(target.kind, target.id, locking, values.reason),
			locking ? 'Lock written' : 'Unlock written',
			target.stale
				? `The row is saved. ${target.name} is not pulsing, so nothing has read it — and nothing will until that process is running again.`
				: `The row is saved. ${target.name} applies it on its next pulse.`,
		);
		await refresh();
	}

	async function askShutdown(target) {
		const values = await ui.modal({
			title: `Schedule a shutdown for ${target.name}`,
			sub: 'Writes a deadline on this server\'s row, and locks the server in the same write.',
			confirmLabel: 'Write the shutdown',
			confirmTone: 'danger',
			body: `
				${target.stale ? staleWarning(ui, target) : ''}
				<div class="field">
					<label for="sv-seconds">Seconds from now</label>
					<div class="row" style="flex-wrap:wrap;margin-bottom:var(--sp-2)">
						${SHUTDOWN_PRESETS.map((p) =>
							`<button type="button" class="btn btn-sm" data-preset="${p.seconds}">${ui.esc(p.label)}</button>`).join('')}
					</div>
					<input id="sv-seconds" name="seconds" type="number" min="0" max="${MAX_SHUTDOWN_SECONDS}" step="1"
						value="900" required inputmode="numeric" autocomplete="off" />
					<div class="field-hint" id="sv-lands"></div>
				</div>
				${ui.banner('warn', 'At the deadline the process exits',
					'The server warns the players it still has as the countdown passes 15m, 10m, 5m, 2m, 1m, 30s and 10s, then disconnects them, saves them and stops. Nothing in this panel can start it again.')}
				${reasonField()}`,
			onReady: (form) => {
				const input = form.querySelector('#sv-seconds');
				const lands = form.querySelector('#sv-lands');
				/* Written as text, not markup: the operator's own typing goes in here on every
				 * keystroke, and a wall-clock preview is not worth an injection seam. */
				const sync = () => {
					const seconds = Number(input.value);
					if (!Number.isFinite(seconds) || seconds < 0 || input.value.trim() === '') {
						lands.textContent = 'Give a whole number of seconds, or pick one above.';
						return;
					}
					if (seconds > MAX_SHUTDOWN_SECONDS) {
						lands.textContent = `The API refuses anything beyond ${ui.duration(MAX_SHUTDOWN_SECONDS)}, so a mistyped delay cannot lock a world for weeks.`;
						return;
					}
					lands.textContent = seconds === 0
						? 'Due immediately: the deadline is written in the past, and the server stops the moment it reads its row — seconds away if it is pulsing, never if it is not.'
						: `Lands at ${new Date(Date.now() + seconds * 1000).toLocaleTimeString()}, ${ui.duration(seconds)} from now. The deadline is an absolute time, so it holds even if the server misses a pulse.`;
				};
				for (const preset of form.querySelectorAll('[data-preset]')) {
					preset.addEventListener('click', () => {
						input.value = preset.dataset.preset;
						sync();
					});
				}
				input.addEventListener('input', sync);
				sync();
			},
			/* The server validates this too. Checking here is not duplication for its own
			 * sake: a refusal arrives after the dialog has taken the operator's reason, and
			 * this way they never lose it. */
			onSubmit: (v, form) => {
				const seconds = Number(v.seconds);
				if (!Number.isInteger(seconds) || seconds < 0 || seconds > MAX_SHUTDOWN_SECONDS) {
					const input = form.querySelector('#sv-seconds');
					input.setCustomValidity(`Give a whole number of seconds between 0 and ${MAX_SHUTDOWN_SECONDS}.`);
					input.reportValidity();
					setTimeout(() => input.setCustomValidity(''), 10);
					return false;
				}
				if (!String(v.reason).trim()) return false;
				return { ...v, seconds };
			},
		});
		if (!values) return;

		const landsAt = new Date(Date.now() + values.seconds * 1000).toLocaleTimeString();
		await ctx.attempt(
			() => api.setServerShutdown(target.kind, target.id, values.seconds, values.reason),
			'Shutdown written',
			shutdownWrittenText(target, values.seconds, landsAt),
		);
		await refresh();
	}

	async function askCancelShutdown(target) {
		const values = await ui.modal({
			title: `Cancel the shutdown of ${target.name}`,
			sub: 'Clears the deadline from this server\'s row. The lock that came with it stays.',
			confirmLabel: 'Write the cancellation',
			body: `
				${target.stale ? staleWarning(ui, target) : ''}
				${target.overdue
					? ui.banner('warn', 'That deadline has already passed',
						'If the server was running, it has already stopped and this changes nothing about that. Clearing the row still matters: a process started again would otherwise read a deadline in the past and stop immediately.')
					: ''}
				${ui.banner('info', 'The server stays locked',
					'Scheduling the shutdown locked the server so it would drain, and cancelling does not undo that. Stopping a shutdown and reopening to players are separate decisions — unlock it when you want it taking players again.')}
				${reasonField()}`,
			onSubmit: (v) => (String(v.reason).trim() ? v : false),
		});
		if (!values) return;

		await ctx.attempt(
			() => api.setServerShutdown(target.kind, target.id, null, values.reason),
			'Cancellation written',
			`The deadline is cleared from the row. ${target.name} stops counting down when it next pulses, and stays locked until you write an unlock.`,
		);
		await refresh();
	}

	/* Re-reads after an action. It swallows its own failure on purpose: the action has
	 * already been reported, and a board that cannot be re-read is the poll's problem to
	 * announce, not something to report twice. */
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
 * Adds the one derived flag the rest of the view keys off.
 *
 * The server decides what stale means and sends `stale`; the age comparison behind it is
 * only a fallback for a payload that omits the flag. A board that quietly failed to mark a
 * dead server is the exact failure this page exists to prevent, so it is not left to one
 * field arriving.
 */
function decorate(servers, staleAfter) {
	return (servers ?? []).map((s) => ({
		...s,
		isStale: s.stale === true ||
			(s.stale === undefined && Number.isFinite(s.pulseAgeSeconds) && s.pulseAgeSeconds > staleAfter),
	}));
}

/* ── Tables ──────────────────────────────────────────────────── */

function loginTable(ui, servers) {
	return ui.table({
		columns: [
			{ label: 'Server', cell: (s) => serverCell(ui, s) },
			/* "Live" rather than "open": a login server has nothing to be open or locked to,
			 * so the only question its row answers is whether it is still there. */
			{ label: 'State', cell: (s) => (s.isStale ? staleBadge(ui, s) : ui.statusBadge('live', 'ok', true)) },
			{
				label: 'Last pulse',
				align: 'right',
				cell: (s) => `<span class="nowrap" title="${ui.esc(ui.dateTime(s.lastPulseUtc))}">${ui.ago(s.lastPulseUtc)}</span>`,
			},
		],
		rows: servers,
		rowAttrs: (s) => (s.isStale ? 'class="is-alert"' : ''),
		emptyText: 'No login server has registered, so nobody can sign in to the game.',
	});
}

function controlledTable(ui, servers, kind, readAt, emptyText) {
	return ui.table({
		columns: [
			{ label: 'Server', cell: (s) => serverCell(ui, s) },
			{ label: 'State', cell: (s) => stateBadge(ui, s) },
			{ label: 'Players', align: 'right', cell: (s) => `<span class="tnum">${ui.num(s.characterCount)}</span>` },
			{ label: 'Shutdown', cell: (s) => shutdownCell(ui, s, readAt) },
			{
				label: 'Last pulse',
				align: 'right',
				cell: (s) => `<span class="nowrap" title="${ui.esc(ui.dateTime(s.lastPulseUtc))}">${ui.ago(s.lastPulseUtc)}</span>`,
			},
			{ label: '', align: 'right', cell: (s) => controlButtons(ui, s, kind) },
		],
		rows: servers,
		// The tint is the second half of making a dead server unmissable; the badge is the first.
		rowAttrs: (s) => (s.isStale ? 'class="is-alert"' : ''),
		emptyText,
	});
}

function serverCell(ui, s) {
	return `
		<div class="cell-primary">${ui.esc(s.name)}</div>
		<div class="cell-sub mono">${ui.esc(s.address)}:${ui.esc(s.port)}</div>`;
}

/**
 * The one badge that says what state a server is in.
 *
 * Stale comes first and is the loudest, because it is the state that invalidates every
 * other one on the row: a server that is not pulsing is not applying its lock, not counting
 * down its shutdown, and not updating the population beside it. Everything below it is only
 * true of a server that is still there.
 */
function stateBadge(ui, s) {
	if (s.isStale) return staleBadge(ui, s);
	if (s.shutdownAtUtc) return ui.statusBadge('shutting down', 'warn', true);
	if (s.locked) return ui.statusBadge('locked', 'info');
	return ui.statusBadge('open', 'ok', true);
}

/* Carries how long the server has been silent rather than the word "stale" alone: "no pulse
 * for 14m" is a fact an operator can act on, and it is the only label on the board that
 * grows more alarming the longer nobody deals with it. */
function staleBadge(ui, s) {
	return ui.statusBadge(
		Number.isFinite(s.pulseAgeSeconds) ? `no pulse for ${ui.duration(s.pulseAgeSeconds)}` : 'no pulse',
		'danger',
	);
}

/**
 * The pending-shutdown countdown.
 *
 * The remaining time is anchored to when the board was read and then ticked locally, so it
 * moves every second instead of every poll. A deadline that has passed says so rather than
 * showing "0s": the row still carries it, which means the server has not acted on it yet —
 * either because it is mid-teardown or because it is not there to act.
 */
function shutdownCell(ui, s, readAt) {
	if (!s.shutdownAtUtc) return '<span class="faint">not scheduled</span>';

	const wall = `<div class="cell-sub">at ${ui.esc(ui.dateTime(s.shutdownAtUtc))}</div>`;
	if (!Number.isFinite(s.secondsUntilShutdown)) {
		return `<div class="strong nowrap">${ui.ago(s.shutdownAtUtc)}</div>${wall}`;
	}

	const deadline = readAt + s.secondsUntilShutdown * 1000;
	return `
		<div class="strong tnum nowrap" data-deadline="${deadline}">${countdownText(ui, s.secondsUntilShutdown)}</div>
		${wall}`;
}

function countdownText(ui, remainingSeconds) {
	return remainingSeconds > 0 ? ui.duration(remainingSeconds) : 'deadline passed';
}

/** Rewrites just the countdowns, once a second, without repainting the board around them. */
function tickCountdowns(ui, host) {
	for (const el of host.querySelectorAll('[data-deadline]')) {
		el.textContent = countdownText(ui, (Number(el.dataset.deadline) - Date.now()) / 1000);
	}
}

/* Controls stay enabled on a stale server rather than being hidden or disabled: a row
 * written now is still adopted if the process comes back, and an operator draining a shard
 * has a real reason to lock a server that has just gone quiet. The dialog is where the
 * warning belongs, because that is where the decision is made. */
function controlButtons(ui, s, kind) {
	const common = `data-kind="${ui.esc(kind)}" data-id="${ui.esc(s.id)}" data-name="${ui.esc(s.name)}"` +
		` data-stale="${s.isStale}" data-age="${ui.esc(s.pulseAgeSeconds ?? '')}"`;
	const overdue = Number.isFinite(s.secondsUntilShutdown) && s.secondsUntilShutdown <= 0;

	return `
		<div class="cell-actions">
			<button class="btn btn-sm" data-control="${s.locked ? 'unlock' : 'lock'}" ${common}>
				${s.locked ? 'Unlock' : 'Lock'}
			</button>
			${s.shutdownAtUtc
				? `<button class="btn btn-sm" data-control="shutdown" ${common}>Reschedule</button>
					<button class="btn btn-sm" data-control="cancel" ${common} data-overdue="${overdue}">Cancel shutdown</button>`
				: `<button class="btn btn-sm btn-danger" data-control="shutdown" ${common}>Schedule shutdown</button>`}
		</div>`;
}

/* ── Dialog copy ─────────────────────────────────────────────── */

function staleBanner(ui, stale, staleAfter) {
	const names = stale.map((s) => s.name).join(', ');
	return ui.banner('danger',
		stale.length === 1
			? '1 server has stopped pulsing'
			: `${stale.length} servers have stopped pulsing`,
		`${names}. Nothing has been heard from ${stale.length === 1 ? 'it' : 'them'} for longer than ${ui.duration(staleAfter)}. Everything else this board shows about ${stale.length === 1 ? 'that row' : 'those rows'} — population, lock, countdown — is whatever was true when it last pulsed, and a control written for a process that is gone is read by nobody.`);
}

function staleWarning(ui, target) {
	return ui.banner('danger',
		Number.isFinite(target.ageSeconds)
			? `${target.name} has not pulsed for ${ui.duration(target.ageSeconds)}`
			: `${target.name} is not pulsing`,
		'This writes a row and nothing more. A server adopts its row on its next pulse, and this one is not pulsing — it may not be running at all, in which case what you write here will never be read. Check the process before you count on this taking effect.');
}

function lockExplainer(kind, locking) {
	if (kind === 'scene') {
		return locking
			? 'A locked scene server keeps the instances and players it already has. The world server stops routing anybody to it and stops giving it new scenes to load, so it empties as its instances finish.'
			: 'The world server starts routing players and new scenes to this scene server again.';
	}
	return locking
		? 'A locked world refuses new logins — accounts above Player are still admitted, so you cannot lock yourself out of your own maintenance. Players already in the world stay where they are; locking drains, it does not evict.'
		: 'The world accepts logins from ordinary players again.';
}

function shutdownWrittenText(target, seconds, landsAt) {
	if (target.stale) {
		return `The deadline and the lock are on the row. ${target.name} is not pulsing, so nothing has read either of them yet.`;
	}
	return seconds === 0
		? `${target.name} is marked due now and locked. It stops when it next reads its row.`
		: `${target.name} is marked to stop at ${landsAt} and locked. It begins warning its players when it next reads its row.`;
}

function reasonField() {
	return `
		<div class="field">
			<label for="sv-reason">Reason (recorded in the audit log)</label>
			<textarea id="sv-reason" name="reason" required placeholder="Why is this being written?"></textarea>
		</div>`;
}
