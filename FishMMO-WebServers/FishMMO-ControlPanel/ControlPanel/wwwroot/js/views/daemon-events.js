/*
 * Daemon → Command log.
 *
 * WHAT THIS PAGE IS.
 *
 * Not an event feed, and not a record of what happened on the hosts. It is the list of
 * command ROWS this panel wrote, and what the daemon that collected each one reported back.
 * Every row is one operator pressing Start, Stop or Restart, so each has a life cycle rather
 * than a moment:
 *
 *   waiting  — written, nothing has collected it yet;
 *   claimed  — a daemon took it and is running it on its own machine;
 *   done     — it finished and the daemon said so;
 *   failed   — it finished and the daemon said it did not work;
 *   expired  — no daemon ever collected it, and the row is past its deadline. A restart
 *              carried out an hour late is worse than one that never happened, so an
 *              uncollected command is abandoned rather than run when the daemon comes back.
 *
 * The last two are the reason this page exists. A failed command is the row an operator came
 * here to find, so it is tinted; an expired one looks like nothing happened because nothing
 * did, and saying "pending" forever would be a lie about a command that will never run.
 *
 * Host names, application names and outcomes are written by daemons on other machines. They
 * are not trusted input, so every one of them goes through `ui.esc`.
 */

const PAGE_SIZE = 25;

/* Verb names come from the server as `verbName`. This is the fallback for a payload that
 * carries only the integer, so an unnamed verb reads as a verb rather than as "2". */
const VERB_NAMES = ['Start', 'Stop', 'Restart'];

export async function render(host, ctx) {
	const { api, ui } = ctx;
	const filters = { hostName: '', page: 1, pageSize: PAGE_SIZE };

	/* The host filter is a convenience. If the host list cannot be read the command log is
	 * still worth showing — and a daemon that has gone away entirely is exactly when somebody
	 * is reading this page — so the select degrades to "any host". */
	let hosts = [];
	try {
		hosts = (await api.getDaemonHosts())?.hosts ?? [];
	} catch {
		hosts = [];
	}

	host.innerHTML = `
		<div class="page-head">
			<div class="page-head-text">
				<h1>Command log</h1>
				<p class="page-head-sub">
					Every start, stop and restart this panel has queued, who asked for it, and what the
					daemon that collected it reported back. The panel writes the row; the daemon on the
					host runs it. A row nothing collected before its deadline is abandoned, not run late.
				</p>
			</div>
		</div>

		<div class="stack-lg">
			${ui.banner('info', 'A command is a row, and it has a life cycle',
				'Waiting means it is written and nothing has picked it up. Claimed means a daemon took it and is running it. Done and failed are what that daemon reported afterwards. Expired means no daemon ever collected it — the command did not run, and it never will.')}

			<section class="card">
				<div class="toolbar">
					<select id="host-filter" aria-label="Host">
						<option value="">All hosts</option>
						${hosts.map((h) => `<option value="${ui.esc(h.hostName)}">${ui.esc(h.hostName)}</option>`).join('')}
					</select>
					<span class="grow"></span>
					<span class="small muted" id="count"></span>
					<button class="btn btn-sm" data-act="refresh">${ui.icon('refresh', 13)} Refresh</button>
				</div>
				<div class="card-body flush" id="results">${ui.loading()}</div>
				<footer class="card-foot" id="pager"></footer>
			</section>
		</div>`;

	const results = host.querySelector('#results');
	const pagerHost = host.querySelector('#pager');

	async function load() {
		results.innerHTML = ui.loading();

		let page;
		try {
			page = await api.getDaemonCommands(filters);
		} catch (err) {
			/* The log is read-only, so a failed read has nothing to preserve — but it must not
			 * read as "no commands have ever been queued", which is the opposite claim. */
			results.innerHTML = ui.banner('warn', 'The command log could not be read', err.message || '');
			pagerHost.hidden = true;
			host.querySelector('#count').textContent = '';
			return;
		}

		const items = page.items ?? [];
		host.querySelector('#count').textContent = `${ui.num(page.totalCount ?? items.length)} commands`;

		results.innerHTML = items.length
			? ui.table({
				columns: [
					{
						label: 'When',
						cell: (c) => `<span class="nowrap" title="${ui.esc(ui.dateTime(c.requestedUtc))}">${ui.ago(c.requestedUtc)}</span>`,
					},
					{
						label: 'Target',
						cell: (c) => `
							<div class="cell-primary">${ui.esc(c.appName)}</div>
							<div class="cell-sub">on ${ui.esc(c.hostName)}</div>`,
					},
					{ label: 'Command', cell: (c) => verbBadge(ui, c) },
					{
						label: 'Asked by',
						cell: (c) => (c.requestedBy
							? ui.esc(c.requestedBy)
							: '<span class="faint">not recorded</span>'),
					},
					{ label: 'State', cell: (c) => stateCell(ui, c) },
					{ label: 'Outcome', cell: (c) => outcomeCell(ui, c) },
					{ label: '', align: 'right', cell: () => '<button class="btn btn-sm" data-act="expand">Details</button>' },
				],
				rows: items,
				/* Only a failure is tinted. An expired row matters too and says so in its own
				 * cell, but a page where half the rows are red is a page where the one command
				 * that ran and broke something is no easier to find than the rest. */
				rowAttrs: (c, i) => `class="is-clickable${lifecycle(c).key === 'failed' ? ' is-alert' : ''}" data-index="${i}" aria-expanded="false"`,
			})
			: ui.empty('No command has been queued',
				filters.hostName
					? `Nothing has been started, stopped or restarted on ${filters.hostName} through this panel. Commands queued for other hosts are hidden by the filter above.`
					: 'Nothing has been started, stopped or restarted through this panel. Rows appear here the moment a command is written — before any daemon has collected it — so an empty log means nobody has asked for anything yet.',
				'list');

		wireExpanders(results, ui, items);

		pagerHost.innerHTML = ui.pager(page.page ?? 1, page.pageSize ?? PAGE_SIZE, page.totalCount ?? items.length, 'page');
		pagerHost.hidden = !pagerHost.innerHTML.trim();
		pagerHost.querySelectorAll('[data-action="page"]').forEach((b) =>
			b.addEventListener('click', () => {
				filters.page = Number(b.dataset.page);
				load();
			}),
		);
	}

	host.querySelector('#host-filter').addEventListener('change', (e) => {
		filters.hostName = e.target.value;
		filters.page = 1;
		load();
	});
	host.querySelector('[data-act="refresh"]').addEventListener('click', () => load());

	await load();
}

/* ── Life cycle ──────────────────────────────────────────────── */

/**
 * Parses a timestamp from the API, treating one with no zone designator as UTC.
 *
 * A copy of the same rule in `ui.js`, which does not export its parser. It is needed here
 * because one part of the life cycle is arithmetic rather than display: whether a row's
 * deadline has passed. Parsing that as local time would move the deadline by the viewer's
 * offset and mark live commands expired — or, worse, the other way round.
 */
function parseUtc(iso) {
	if (typeof iso !== 'string') return new Date(iso);
	return /[Zz]$|[+-]\d{2}:?\d{2}$/.test(iso) ? new Date(iso) : new Date(iso + 'Z');
}

function isPast(iso) {
	if (!iso) return false;
	const at = parseUtc(iso).getTime();
	return Number.isFinite(at) && at <= Date.now();
}

/**
 * The one place a command row is turned into a state.
 *
 * Exported because the hosts page shows the same life cycle for the commands an operator
 * has just queued, and two implementations of this would drift into disagreeing about what
 * a row means — which is the only thing either page is for.
 */
export function lifecycle(command) {
	if (command.completedUtc) {
		if (command.succeeded === true) return { key: 'done', label: 'done', tone: 'ok' };
		if (command.succeeded === false) return { key: 'failed', label: 'failed', tone: 'danger' };
		// Finished without a verdict: the daemon wrote a completion and no result.
		return { key: 'finished', label: 'finished', tone: 'warn' };
	}
	// Claimed beats expired: a daemon that took the row is running it, deadline or not.
	if (command.claimedUtc) return { key: 'claimed', label: 'claimed', tone: 'warn', pulse: true };
	if (isPast(command.expiresUtc)) return { key: 'expired', label: 'expired', tone: 'warn' };
	return { key: 'waiting', label: 'waiting', tone: 'info', pulse: true };
}

/** The life-cycle badge on its own, for a table cell that has no room for the sub-line. */
export function lifecycleBadge(ui, command) {
	const state = lifecycle(command);
	return ui.statusBadge(state.label, state.tone, state.pulse === true);
}

export function verbLabel(command) {
	return command.verbName || VERB_NAMES[command.verb] || 'command';
}

export function verbBadge(ui, command) {
	const label = verbLabel(command);
	// Stop and restart interrupt a running process; start cannot take anything away.
	const tone = command.verb === 0 ? 'info' : 'accent';
	return ui.badge(label.toLowerCase(), tone);
}

/* The badge says which state; the line under it says what that means for this row. "Expired"
 * on its own is a word — "expired, never collected" is the fact an operator needs. */
function stateCell(ui, c) {
	const state = lifecycle(c);
	const note = {
		waiting: c.expiresUtc && !isPast(c.expiresUtc)
			? `expires ${ui.ago(c.expiresUtc)}`
			: 'not collected yet',
		claimed: c.claimedUtc ? `running since ${ui.ago(c.claimedUtc)}` : 'running',
		done: c.completedUtc ? ui.ago(c.completedUtc) : '',
		failed: c.completedUtc ? ui.ago(c.completedUtc) : '',
		finished: 'the daemon reported no result',
		expired: 'no daemon collected it — it did not run',
	}[state.key];

	return `
		${ui.statusBadge(state.label, state.tone, state.pulse === true)}
		${note ? `<div class="cell-sub">${ui.esc(note)}</div>` : ''}`;
}

function outcomeCell(ui, c) {
	if (c.outcome) {
		return `<div class="cell-sub" style="max-width:34ch;overflow-wrap:anywhere">${ui.esc(c.outcome)}</div>`;
	}
	const state = lifecycle(c);
	if (state.key === 'waiting' || state.key === 'claimed') return '<span class="faint">not yet</span>';
	if (state.key === 'expired') return '<span class="faint">never ran</span>';
	return '<span class="faint">—</span>';
}

/* ── Details ─────────────────────────────────────────────────── */

/**
 * Gives every row a hidden sibling holding its details, and toggles it on click.
 *
 * Built after the table rather than inside a cell because the detail spans the whole width;
 * `ui.table` renders one `<tr>` per row, so the second is inserted here.
 */
function wireExpanders(results, ui, items) {
	const body = results.querySelector('tbody');
	if (!body) return;

	for (const tr of [...body.querySelectorAll('tr[data-index]')]) {
		const command = items[Number(tr.dataset.index)];
		const button = tr.querySelector('[data-act="expand"]');

		const detail = document.createElement('tr');
		detail.hidden = true;
		detail.innerHTML = `<td colspan="${tr.children.length}" style="background:var(--surface-sunken)">${detailPanel(ui, command)}</td>`;
		tr.after(detail);

		tr.addEventListener('click', () => {
			detail.hidden = !detail.hidden;
			tr.setAttribute('aria-expanded', String(!detail.hidden));
			if (button) button.textContent = detail.hidden ? 'Details' : 'Hide';
		});
	}
}

function detailPanel(ui, c) {
	return `
		<div class="grid grid-2">
			<div>
				<div class="small muted" style="margin-bottom:var(--sp-2)">Why it was queued</div>
				<div class="small" style="overflow-wrap:anywhere">${c.reason
					? ui.esc(c.reason)
					: '<span class="faint">No reason recorded. The API requires one, so a row without it predates that rule or was written directly.</span>'}</div>
				<div class="small muted" style="margin:var(--sp-4) 0 var(--sp-2)">What happened</div>
				<div class="small">${ui.esc(storyOf(c))}</div>
			</div>
			<div>
				<div class="small muted" style="margin-bottom:var(--sp-2)">Timing</div>
				<dl class="dl">
					<dt>Command</dt><dd class="tnum">${ui.num(c.id)}</dd>
					<dt>Queued</dt><dd>${ui.dateTime(c.requestedUtc)}</dd>
					<dt>Asked by</dt><dd>${c.requestedBy ? ui.esc(c.requestedBy) : '<span class="faint">—</span>'}</dd>
					<dt>Deadline</dt><dd>${c.expiresUtc ? ui.dateTime(c.expiresUtc) : '<span class="faint">none</span>'}</dd>
					<dt>Claimed</dt><dd>${c.claimedUtc
						? `${ui.dateTime(c.claimedUtc)} <span class="faint">(waited ${ui.duration(gap(c.requestedUtc, c.claimedUtc))})</span>`
						: '<span class="faint">never</span>'}</dd>
					<dt>Claimed by</dt><dd class="mono">${c.claimedBy ? ui.esc(c.claimedBy) : '<span class="faint">—</span>'}</dd>
					<dt>Completed</dt><dd>${c.completedUtc
						? `${ui.dateTime(c.completedUtc)}${c.claimedUtc ? ` <span class="faint">(ran ${ui.duration(gap(c.claimedUtc, c.completedUtc))})</span>` : ''}`
						: '<span class="faint">never</span>'}</dd>
				</dl>
			</div>
		</div>`;
}

/** Seconds between two timestamps, or null if either is missing or unparseable. */
function gap(fromIso, toIso) {
	if (!fromIso || !toIso) return null;
	const seconds = (parseUtc(toIso).getTime() - parseUtc(fromIso).getTime()) / 1000;
	return Number.isFinite(seconds) ? Math.max(0, seconds) : null;
}

/* One sentence saying what the row means, because the columns above it are facts and this
 * is the reading of them. An operator deciding whether to queue the command again needs the
 * reading, not the fields. */
function storyOf(c) {
	const state = lifecycle(c);
	const verb = verbLabel(c).toLowerCase();
	switch (state.key) {
		case 'waiting':
			return `Written and waiting. No daemon has collected this ${verb} yet; ${c.hostName}'s daemon takes it on its next poll, and if it does not before the deadline above the row is abandoned.`;
		case 'claimed':
			return `${c.hostName}'s daemon has this ${verb} and is carrying it out. It has not reported a result yet — a daemon that stops before it does leaves the row here, claimed and unfinished.`;
		case 'done':
			return `The daemon on ${c.hostName} ran this ${verb} and reported that it worked.`;
		case 'failed':
			return `The daemon on ${c.hostName} ran this ${verb} and reported that it did not work. The process is in whatever state that left it, which is not necessarily the state it was in before.`;
		case 'finished':
			return `The daemon on ${c.hostName} closed this ${verb} without saying whether it worked. Check the process itself rather than assuming either way.`;
		default:
			return `Nothing ever collected this ${verb}, and the deadline passed. It did not run and it will not: ${c.hostName}'s daemon was not there to take it, or was not running long enough to. Queue it again once that daemon is reporting.`;
	}
}
