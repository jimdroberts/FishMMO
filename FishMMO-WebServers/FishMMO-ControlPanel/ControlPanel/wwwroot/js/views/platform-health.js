/*
 * Platform → Health. The panel's own health, and the database every other page reads through.
 *
 * WHAT THIS PAGE IS FOR, AND WHAT IT DELIBERATELY LEAVES OUT.
 *
 * Three screens already answer "is the shard up". The server board owns game server
 * processes, their pulse and their population; daemon hosts owns supervised OS processes and
 * the daemons watching them; Queues owns email queue depth. None of that is repeated here —
 * two screens showing the same number, one poll interval apart, is worse than one screen, and
 * an operator who sees population in two places will eventually believe the stale one.
 *
 * What is left is what nothing else can tell you: whether THIS process is healthy, and
 * whether the database underneath every page is. The last card names the pages that own the
 * rest, and links to them, so "it isn't here" never has to be guessed at.
 *
 * THE FOUR THINGS ON THIS PAGE THAT HAVE NO OTHER SYMPTOM.
 *
 *   - A connection pool near exhaustion. Nothing fails; everything simply waits. It presents
 *     as a panel that has become inexplicably slow, and no other screen measures it.
 *   - A PENDING MIGRATION. The database is a version behind the code, and the first symptom
 *     is a query failing for a column that does not exist — which surfaces as missing player
 *     data, nowhere near the cause.
 *   - AN AUDIT LOG THAT HAS STOPPED. Audit writes never block the action that caused them,
 *     on purpose, so a broken recording path breaks nothing visible. The age of the newest
 *     row is the entire symptom.
 *   - A TWO-FACTOR KEY THAT DID NOT LOAD. Secrets says the row exists; only this process
 *     knows whether it decoded into a usable key. Without this line the symptom is every
 *     authenticator code being rejected, for no stated reason.
 *
 * Everything below is rendered through `ui.esc`. Most of it is authored server-side, but
 * migration identifiers and operation names come out of the database and the assembly, and a
 * page that escapes only what it currently expects to be hostile is one refactor from not.
 */

/* Ten seconds. Faster is pointless — the pool counters move with requests, not with a clock,
 * and each poll opens a real connection to measure the round trip, so polling harder makes
 * the number it reports worse. The server sends `pollSeconds` for the same reason the board
 * sends its pulse rate; this is the fallback if it is missing. */
const REFRESH_MS = 10000;

/* Used only when the payload carries no thresholds. The server decides them; these exist so
 * a missing field cannot silently turn the pool warning off, which would leave an exhausted
 * pool looking exactly like an idle one. */
const FALLBACK_WARN_PERCENT = 70;
const FALLBACK_CRITICAL_PERCENT = 85;

export async function render(host, ctx) {
	const { api, ui } = ctx;

	let poll = null;

	/* Registered before the first read, which is allowed to throw: the router keeps one
	 * cleanup per view, so the timer is cancelled on navigation whatever happens below. A
	 * missed cleanup here is a page the operator has left still opening a database connection
	 * every ten seconds for the rest of the session. */
	ctx.onCleanup(() => clearInterval(poll));

	let data = null;
	let readAt = 0;
	let refreshError = null;

	async function read() {
		data = await api.getPlatformHealth();
		readAt = Date.now();
		refreshError = null;
		paint();
	}

	/* A failed poll keeps what is on screen, clearly labelled as stale. Somebody mid-incident
	 * is better served by the last good read than by an error page where the numbers were —
	 * and on this page in particular, a failed poll is itself a symptom worth seeing beside
	 * the figures that were true a moment ago. */
	async function pollOnce() {
		try {
			await read();
		} catch (err) {
			refreshError = err.message || 'This page could not be refreshed.';
			if (data) paint();
		}
	}

	/* Started before the first read so the page heals itself: if the first read fails the
	 * router renders the failure, and the first poll that succeeds paints over it. */
	poll = setInterval(pollOnce, REFRESH_MS);

	// The first read is allowed to throw — the router turns it into a visible failure.
	await read();

	function paint() {
		const db = data.database ?? {};
		const pool = db.pool ?? {};
		const schema = db.schema ?? {};
		const queries = db.queries ?? {};
		const auditLog = data.auditLog ?? {};
		const panel = data.panel ?? {};

		const seconds = Number(data.pollSeconds) > 0 ? Number(data.pollSeconds) : REFRESH_MS / 1000;
		const alerts = buildAlerts(ui, { db, pool, schema, auditLog, panel, refreshError, readAt });

		host.innerHTML = `
			<div class="page-head">
				<div class="page-head-text">
					<h1>Platform health</h1>
					<p class="page-head-sub">
						This Control Panel process, and the database every page in it reads through. Game
						servers, supervised processes and the email queue are not here — they have their own
						screens, listed at the bottom. Refreshes every ${seconds} seconds.
					</p>
				</div>
				<div class="page-head-actions">
					<button class="btn" data-act="refresh">${ui.icon('refresh')} Refresh</button>
				</div>
			</div>

			<div class="stack-lg">
				${alerts.join('')}

				<div class="grid grid-4">
					${ui.stat({
						label: 'Database',
						value: db.reachable ? 'reachable' : 'unreachable',
						note: db.reachable
							? `${ui.num(db.roundTripMs, 1)} ms round trip, measured now`
							: 'This panel cannot open a connection',
						noteTone: db.reachable ? '' : 'danger',
					})}
					${ui.stat({
						label: 'Pool in use',
						value: ui.num(pool.active),
						unit: `/ ${ui.num(pool.maxSize)}`,
						note: poolNote(ui, pool),
						noteTone: poolTone(pool),
					})}
					${ui.stat({
						label: 'Newest audit entry',
						value: auditLog.readable
							? (auditLog.lastEntryUtc ? ui.ago(auditLog.lastEntryUtc) : 'never')
							: 'unknown',
						note: auditNote(ui, auditLog),
						noteTone: auditLog.readable && auditLog.quiet ? 'warn' : '',
					})}
					${ui.stat({
						label: 'Panel uptime',
						value: panel.uptimeSeconds === null || panel.uptimeSeconds === undefined
							? '—'
							: ui.duration(panel.uptimeSeconds),
						note: `${panel.environment ?? 'unknown'} · ${panel.version ?? 'unknown build'}`,
					})}
				</div>

				<div class="grid grid-2">
					${poolCard(ui, pool)}
					${schemaCard(ui, schema)}
				</div>

				${queriesCard(ui, queries)}

				<div class="grid grid-2">
					${auditCard(ui, auditLog)}
					${processCard(ui, panel)}
				</div>

				${elsewhereCard(ui, data)}
			</div>`;

		host.querySelector('[data-act="refresh"]')?.addEventListener('click', () => pollOnce());
	}
}

/* ── Banners ─────────────────────────────────────────────────── */

/**
 * Everything wrong, loudest first, and one "all clear" when nothing is.
 *
 * Ordered by what an operator should deal with first rather than by which section of the
 * payload it came from: an unreachable database makes every figure below it meaningless, and
 * a pending migration is a live data-loss risk that a busy pool is not.
 */
function buildAlerts(ui, { db, pool, schema, auditLog, panel, refreshError, readAt }) {
	const alerts = [];

	if (refreshError) {
		alerts.push(ui.banner('warn', 'This page is not refreshing',
			`${refreshError} What you are looking at was read at ${new Date(readAt).toLocaleTimeString()} and is not being updated.`));
	}

	if (db.reachable === false) {
		alerts.push(ui.banner('danger', 'The Control Panel cannot reach the database',
			db.unreachableHint || 'No connection could be opened. Every other figure on this page was read before that was known, or not at all.'));
	}

	if (schema.checkRan && schema.upToDate === false) {
		const pending = schema.pendingMigrations ?? [];
		alerts.push(ui.banner('danger',
			pending.length === 1
				? 'A migration has not been applied to this database'
				: `${pending.length} migrations have not been applied to this database`,
			'The schema is behind the code running against it. This does not fail loudly: a query hits a column that is not there, and it surfaces as missing player data somewhere unrelated. Apply them with "dotnet ef database update".'));
	} else if (schema.unavailable && db.reachable !== false) {
		alerts.push(ui.banner('warn', 'The migration check could not run',
			'Whether this database is up to date with the code is unknown — which is not the same as it being fine. The reason is in this host\'s application log.'));
	}

	if (pool.requiresAction || pool.status === 'Critical' || pool.status === 'Unhealthy') {
		alerts.push(ui.banner('danger', 'The connection pool needs attention',
			`${pool.message ?? ''} ${pool.recommendedAction ?? ''}`.trim()));
	} else if (pool.status === 'Warning') {
		alerts.push(ui.banner('warn', 'The connection pool is busy',
			`${pool.message ?? ''} Nothing fails when this pool fills — requests queue for a connection, and the panel simply becomes slow for no visible reason.`.trim()));
	} else if (Number(pool.exhaustionEvents) > 0) {
		alerts.push(ui.banner('warn', 'The pool has been exhausted since this host started',
			`${ui.num(pool.exhaustionEvents)} time(s). It is not exhausted now, but it has been, and requests were waiting for a connection each time.`));
	}

	if (panel.twoFactorKeyLoaded === false) {
		alerts.push(ui.banner('danger', 'The two-factor master key did not load',
			`${panel.twoFactorKeyError || 'The key is unavailable.'} Until it does, no authenticator code can be verified and nobody can enrol. The Secrets page can tell you whether the row exists; this says whether this process could use it.`));
	}

	if (auditLog.readable === false) {
		alerts.push(ui.banner('warn', 'The audit log could not be read',
			'That is not the same as an empty log, and it says nothing about whether rows are still being written.'));
	} else if (auditLog.quiet) {
		alerts.push(ui.banner('warn',
			auditLog.lastEntryUtc ? 'The audit log has gone quiet' : 'The audit log is empty',
			auditLog.lastEntryUtc
				? `Nothing has been recorded for over ${auditLog.windowHours ?? 24} hours. On a quiet shard that is normal. If operators have been working, the recording path is broken — audit writes never block the action that caused them, so nothing else would tell you.`
				: 'No privileged action has ever been recorded. On a new deployment that is expected. If this shard has been in use, the recording path has never worked.'));
	}

	if (panel.autoVerifyAccounts) {
		alerts.push(ui.banner('warn', 'New accounts skip email verification',
			`Registration on this host auto-verifies, so anybody who can reach the sign-up form gets a working account without proving an address. It is honoured only outside Production, and this host reports itself as "${panel.environment ?? 'unknown'}".`));
	}

	if (alerts.length === 0) {
		alerts.push(ui.banner('ok', 'Nothing here needs attention',
			'The database answers, its schema matches the code, the pool is well inside its limits, the audit log is still receiving rows, and the two-factor key is loaded.'));
	}

	return alerts;
}

/* ── Cards ───────────────────────────────────────────────────── */

function poolCard(ui, pool) {
	const max = Number(pool.maxSize) || 0;
	const active = Number(pool.active) || 0;
	const leaked = Number(pool.openContexts) || 0;

	return ui.card({
		title: 'Connection pool',
		sub: 'Connections this panel process holds against PostgreSQL',
		body: `
			${ui.meter(active, max, poolTone(pool) || 'ok')}
			<dl class="dl" style="margin-top:var(--sp-4)">
				<dt>In use</dt><dd class="tnum">${ui.num(active)}</dd>
				<dt>Maximum</dt><dd class="tnum">${ui.num(max)}</dd>
				<dt>Peak since start</dt><dd class="tnum">${ui.num(pool.peakActive)}</dd>
				<dt>Opened</dt><dd class="tnum">${ui.num(pool.opened)}</dd>
				<dt>Closed</dt><dd class="tnum">${ui.num(pool.closed)}</dd>
				<dt>Open contexts</dt><dd class="tnum">${ui.num(leaked)}</dd>
				<dt>Connection errors</dt><dd class="tnum${Number(pool.connectionErrors) > 0 ? ' is-alert' : ''}">${ui.num(pool.connectionErrors)}</dd>
				<dt>Exhaustion events</dt><dd class="tnum${Number(pool.exhaustionEvents) > 0 ? ' is-alert' : ''}">${ui.num(pool.exhaustionEvents)}</dd>
			</dl>
			<p class="small muted" style="margin-top:var(--sp-4)">
				${ui.esc(pool.message ?? '')}
			</p>
			<p class="small muted" style="margin-top:var(--sp-3)">
				“Open contexts” is what this factory has handed out and not seen disposed. It moves with
				requests in flight, not with connections, so a figure well above “in use” on an idle panel
				is the shape a leak makes — and a leak is what eventually exhausts the pool.
			</p>`,
		foot: `Warning at ${ui.num(pool.warnAtPercent ?? FALLBACK_WARN_PERCENT)}%, critical at ${ui.num(pool.criticalAtPercent ?? FALLBACK_CRITICAL_PERCENT)}%. ${ui.esc(pool.scope ?? '')}`,
	});
}

function schemaCard(ui, schema) {
	const pending = schema.pendingMigrations ?? [];

	const body = schema.checkRan
		? (pending.length
			? `
				<p class="small">These migrations exist in the code and have not been run against this database:</p>
				<ul class="small mono" style="margin:var(--sp-3) 0 0;padding-left:1.2em">
					${pending.map((m) => `<li>${ui.esc(m)}</li>`).join('')}
				</ul>
				<p class="small muted" style="margin-top:var(--sp-4)">
					Apply them with <code>dotnet ef database update</code> against this deployment.
				</p>`
			: '<p class="small">Every migration the code knows about has been applied to this database.</p>')
		: `<p class="small">${ui.esc(schema.note ?? 'The check did not run.')}</p>`;

	return ui.card({
		title: 'Database schema',
		sub: schema.checkRan
			? (pending.length ? `${pending.length} migration${pending.length > 1 ? 's' : ''} pending` : 'Up to date')
			: 'Not determined',
		body,
		/* The narrowness is stated every time rather than only when something is wrong. "Up to
		 * date" here means "nothing pending", which is a weaker claim than "the schema is
		 * right", and an operator who reads the strong version off this card will rule out the
		 * exact fault it cannot see. */
		foot: schema.checkRan
			? ui.esc(schema.note ?? '')
			: 'The reason is in this host’s application log; it is not sent to the browser because it is arbitrary text from the database driver.',
	});
}

function queriesCard(ui, queries) {
	if (!queries.trackingEnabled) {
		return ui.card({
			title: 'Query performance',
			sub: 'Not being tracked',
			body: `
				<p class="small muted">
					Query tracking is off on this host, so there is nothing to report — not “no slow
					queries”. Turn it on in the Npgsql performance configuration if you are chasing
					something; the tracking level is <code>${ui.esc(queries.trackingLevel ?? 'None')}</code>.
				</p>`,
		});
	}

	const slowest = queries.slowest ?? [];

	return ui.card({
		title: 'Query performance',
		sub: `${ui.num(queries.operationsTracked)} operations tracked, ${ui.num(queries.totalExecutions)} executions since this host started`,
		flush: true,
		body: ui.table({
			columns: [
				{
					label: 'Operation',
					cell: (o) => `<div class="cell-primary mono">${ui.esc(o.operation)}</div>`,
				},
				{ label: 'Runs', align: 'right', cell: (o) => `<span class="tnum">${ui.num(o.executions)}</span>` },
				{
					label: 'Failed',
					align: 'right',
					cell: (o) => (Number(o.failed) > 0
						? `<span class="tnum is-alert">${ui.num(o.failed)}</span>`
						: '<span class="faint">—</span>'),
				},
				{
					label: 'Slow',
					align: 'right',
					cell: (o) => (Number(o.slow) > 0
						? `<span class="tnum">${ui.num(o.slow)}</span>`
						: '<span class="faint">—</span>'),
				},
				{ label: 'Average', align: 'right', cell: (o) => `<span class="tnum nowrap">${ui.num(o.averageMs, 1)} ms</span>` },
				{ label: '95th', align: 'right', cell: (o) => `<span class="tnum nowrap">${ui.num(o.p95Ms, 1)} ms</span>` },
				{ label: 'Worst', align: 'right', cell: (o) => `<span class="tnum nowrap">${ui.num(o.maxMs, 1)} ms</span>` },
			],
			rows: slowest,
			emptyText: 'No database operation has run through this host yet.',
		}),
		/* Both halves matter. "Since this host started" stops "no slow queries" being read as a
		 * clean bill of health two minutes after a restart, and "by average" explains why the
		 * worst single time is not what ordered the list — one cold start after a deployment
		 * gives almost every operation a five-second maximum. */
		foot: `Slowest by average, not by worst case. Counters are this process’s and reset when it restarts; a query counts as slow past ${ui.num(queries.slowQueryThresholdMs)} ms. Tracking level ${ui.esc(queries.trackingLevel ?? '')}.`,
	});
}

function auditCard(ui, auditLog) {
	const body = auditLog.readable
		? `
			<dl class="dl">
				<dt>Newest entry</dt>
				<dd>${auditLog.lastEntryUtc
					? `<span title="${ui.esc(ui.dateTime(auditLog.lastEntryUtc))}">${ui.ago(auditLog.lastEntryUtc)}</span>`
					: '<span class="faint">never</span>'}</dd>
				<dt>Its action</dt>
				<dd>${auditLog.lastEntryAction ? `<code>${ui.esc(auditLog.lastEntryAction)}</code>` : '<span class="faint">—</span>'}</dd>
				<dt>Last ${ui.esc(auditLog.windowHours ?? 24)}h</dt>
				<dd class="tnum">${auditLog.entriesRecently === null || auditLog.entriesRecently === undefined
					? '<span class="faint">not counted</span>'
					: ui.num(auditLog.entriesRecently)}</dd>
				<dt>All time</dt><dd class="tnum">${ui.num(auditLog.totalEntries)}</dd>
			</dl>`
		: `<p class="small muted">${ui.esc(auditLog.note ?? 'The audit log could not be read.')}</p>`;

	return ui.card({
		title: 'Audit log',
		sub: 'Whether it is still receiving rows — not what is in it',
		body,
		/* Counts and one timestamp, never rows. Repeating an actor name or a reason here would
		 * be a second, worse audit view, and the real one is one click away. */
		foot: auditLog.readable
			? `${ui.esc(auditLog.note ?? '')} The rows themselves are on the audit log page.`
			: '',
	});
}

function processCard(ui, panel) {
	return ui.card({
		title: 'This panel process',
		sub: 'Facts about the running host that no other page reports',
		body: `
			<dl class="dl">
				<dt>Build</dt><dd class="mono">${ui.esc(panel.version ?? 'unknown')}</dd>
				<dt>Environment</dt><dd>${ui.esc(panel.environment ?? 'unknown')}</dd>
				<dt>Started</dt>
				<dd>${panel.startedUtc ? ui.esc(ui.dateTime(panel.startedUtc)) : '<span class="faint">not available</span>'}</dd>
				<dt>Uptime</dt>
				<dd>${panel.uptimeSeconds === null || panel.uptimeSeconds === undefined
					? '<span class="faint">not available</span>'
					: ui.esc(ui.duration(panel.uptimeSeconds))}</dd>
				<dt>Registration</dt>
				<dd>${panel.registrationEnabled
					? ui.badge('open', 'info')
					: ui.badge('closed', '')}</dd>
				<dt>Email verification</dt>
				<dd>${panel.autoVerifyAccounts
					? ui.badge('SKIPPED', 'warn')
					: ui.badge('required', 'ok')}</dd>
				<dt>Two-factor key</dt>
				<dd>${panel.twoFactorKeyLoaded
					? ui.statusBadge('loaded', 'ok')
					: ui.badge('NOT LOADED', 'danger')}</dd>
			</dl>
			${panel.twoFactorKeyLoaded === false && panel.twoFactorKeyError
				? `<p class="small muted" style="margin-top:var(--sp-4)">${ui.esc(panel.twoFactorKeyError)}</p>`
				: ''}`,
		foot: 'Whether a supervisor is watching this process, and whether it is up, is on the daemon hosts page. Which build it is running, and whether it could load what it needs, is only here.',
	});
}

/**
 * What this page does not show, where it lives, and the one figure nobody can produce yet.
 *
 * This is not filler. The whole risk of a page called "Platform health" is an operator
 * reading it as the health of the platform, concluding from a green board here that the shard
 * is fine, and never opening the screen that would have told them otherwise.
 */
function elsewhereCard(ui, data) {
	const elsewhere = data.ownedElsewhere ?? [];
	const notWired = data.notWired ?? [];

	return ui.card({
		title: 'Not shown here, on purpose',
		sub: 'A green page above does not mean the shard is healthy — only that this panel and its database are',
		body: `
			<dl class="dl">
				${elsewhere.map((e) => `
					<dt>${e.route
						? `<a href="#/${ui.esc(e.route)}">${ui.esc(e.where)}</a>`
						: ui.esc(e.where)}</dt>
					<dd>${ui.esc(e.what)}</dd>`).join('')}
			</dl>
			${notWired.length ? `
				<p class="small muted" style="margin-top:var(--sp-5)">
					<strong>Wanted here and not built:</strong>
				</p>
				<dl class="dl" style="margin-top:var(--sp-2)">
					${notWired.map((n) => `
						<dt>${ui.esc(n.what)}</dt>
						<dd class="small muted">${ui.esc(n.why)}</dd>`).join('')}
				</dl>` : ''}`,
	});
}

/* ── Shaping ─────────────────────────────────────────────────── */

/**
 * The pool's tone.
 *
 * Taken from the server's own assessment rather than recomputed from the percentage: the
 * status it sends also weighs exhaustion events and the connection error rate, so a pool at
 * 20% that has been exhausted twice is not green, and a page that derived its colour from
 * utilisation alone would paint it so.
 */
function poolTone(pool) {
	switch (pool.status) {
		case 'Unhealthy':
		case 'Critical':
			return 'danger';
		case 'Warning':
			return 'warn';
		case 'Healthy':
			return 'ok';
		default:
			return '';
	}
}

function poolNote(ui, pool) {
	if (pool.utilizationPercent === null || pool.utilizationPercent === undefined) {
		return 'Utilisation cannot be calculated';
	}
	return `${ui.num(pool.utilizationPercent, 1)}% of this process’s pool`;
}

function auditNote(ui, auditLog) {
	if (!auditLog.readable) return 'The log could not be read';
	if (!auditLog.lastEntryUtc) return 'Nothing has ever been recorded';
	return `${ui.num(auditLog.entriesRecently)} in the last ${auditLog.windowHours ?? 24}h`;
}
