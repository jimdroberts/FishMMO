/*
 * Daemon → Logs.
 *
 * WHAT THIS PAGE IS, AND WHAT IT IS NOT.
 *
 * It is NOT process output. Nothing a game server printed to stdout or stderr reaches this
 * panel, and deliberately so: server logs carry connection strings, tokens and file paths
 * inside exception messages, and a page that relayed them would turn a read-only operator
 * view into a way of reading secrets out of a machine. An operator arriving here expecting a
 * tail must be told that in the first thing they read, or they will take an empty page to
 * mean the servers are quiet — which is the opposite of what it would mean.
 *
 * What it is: the SUPERVISION HISTORY. The daemon on each host reports what it observes
 * every few seconds, and until now that report only ever overwrote a single row per
 * application — so a server that restart-looped at three in the morning and recovered before
 * anybody looked left no trace anywhere at all. The database now compares each report against
 * the stored one and appends a row when something moved. This page is that record.
 *
 * Its sibling, the command log, is the other half and answers a different question: that page
 * is what OPERATORS asked for through this panel, this page is what the supervisors saw
 * happen on their own. A restart nobody requested appears only here.
 *
 * WHY A RUN OF ROWS IS DRAWN AS ONE BLOCK.
 *
 * The thing an operator comes here hunting is a restart loop, and a loop is not one event —
 * it is eight rows for one application inside four minutes, buried among ordinary transitions
 * from every other host. Rendered as a flat list it reads as noise. So consecutive rows for
 * the same application within a few minutes of each other are drawn as a single banded
 * episode with a headline that says what the run amounted to ("restarted 5 times in 6
 * minutes, then recovered"), with the individual transitions kept inside it.
 *
 * Host names and application names are written by daemons on other machines and are not
 * trusted input, so every one of them goes through `ui.esc`. The sentence describing each
 * transition is composed on the server from two enum values and two integers — no daemon
 * supplies a string that reaches this page.
 */

const PAGE_SIZE = 50;

/* How close two observations for one application must be to count as the same episode. Five
 * minutes: long enough to hold a restart cycle together across the daemon's backoff, short
 * enough that this morning's flap and last night's are not welded into one block. */
const EPISODE_GAP_MS = 5 * 60 * 1000;

/* Status integers from DaemonAppStatus, and the words the hosts page already uses for them.
 * Compared as numbers rather than by name, for the reason that page gives: `statusName` is a
 * convenience on the payload and the integer is the contract.
 *
 * A value this list does not know — which only a daemon newer than this panel can produce —
 * falls back to the name the server sent, so a new status appears in the table whether or not
 * this array has caught up. The only consequence of forgetting to add one is a missing entry
 * in the filter. */
const STATUS_HEALTHY = 1;
const STATUS_EXHAUSTED = 4;

const STATUSES = [
	{ value: 1, label: 'healthy' },
	{ value: 2, label: 'starting' },
	{ value: 3, label: 'not running' },
	{ value: 4, label: 'exhausted' },
	{ value: 5, label: 'stopped by an operator' },
	{ value: 0, label: 'unknown' },
];

const STATUS_LABELS = Object.fromEntries(STATUSES.map((s) => [s.value, s.label]));

/* The windows an operator actually asks for. "Any time" is first because the whole point of
 * the table is the incident nobody saw, which may be older than any default window. */
const WINDOWS = [
	{ value: '', label: 'Any time' },
	{ value: '1', label: 'Last hour' },
	{ value: '24', label: 'Last 24 hours' },
	{ value: '168', label: 'Last 7 days' },
];

export async function render(host, ctx) {
	const { api, ui } = ctx;
	const filters = { hostName: '', appName: '', status: '', windowHours: '', page: 1, pageSize: PAGE_SIZE };

	/* The host and application filters are a convenience built from the current daemon
	 * reports. If that read fails the history is still worth showing — a daemon that has gone
	 * away entirely is exactly when somebody opens this page — so both selects degrade to
	 * "any", and the history itself is fetched regardless. */
	let hosts = [];
	try {
		hosts = (await api.getDaemonHosts())?.hosts ?? [];
	} catch {
		hosts = [];
	}

	host.innerHTML = `
		<div class="page-head">
			<div class="page-head-text">
				<h1>Logs</h1>
				<p class="page-head-sub">
					What the supervisors saw. Every time a daemon's report differed from what was stored —
					a process that stopped, a relaunch, a restart counter that reset — it is recorded here,
					newest first. Nothing else keeps it: the hosts page shows only what is true now.
				</p>
			</div>
		</div>

		<div class="stack-lg">
			${ui.banner('info', 'This is the supervisor’s record, not process output',
				'No stdout, stderr or log file from a game server reaches this panel, by design: server logs carry connection strings and tokens inside exception messages. What you see here is what the daemon on each host observed about the processes it supervises. An empty page means nothing changed, not that the servers are silent.')}

			<section class="card">
				<div class="toolbar">
					<select id="host-filter" aria-label="Host">
						<option value="">All hosts</option>
						${hosts.map((h) => `<option value="${ui.esc(h.hostName)}">${ui.esc(h.hostName)}</option>`).join('')}
					</select>
					<select id="app-filter" aria-label="Application"></select>
					<select id="status-filter" aria-label="Resulting status">
						<option value="">Any status</option>
						${STATUSES.map((s) => `<option value="${s.value}">${ui.esc(s.label)}</option>`).join('')}
					</select>
					<select id="window-filter" aria-label="Time window">
						${WINDOWS.map((w) => `<option value="${w.value}">${ui.esc(w.label)}</option>`).join('')}
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
	const appSelect = host.querySelector('#app-filter');

	/** Rebuilds the application list for whichever host is selected. */
	function fillApps() {
		const names = appNames(hosts, filters.hostName);
		const previous = filters.appName;
		appSelect.innerHTML = `<option value="">All applications</option>${
			names.map((n) => `<option value="${ui.esc(n)}">${ui.esc(n)}</option>`).join('')}`;
		/* A name that survives the host change keeps its selection; one that does not is
		 * cleared rather than left showing a filter the list no longer offers. */
		if (previous && names.includes(previous)) appSelect.value = previous;
		else filters.appName = '';
	}

	async function load() {
		results.innerHTML = ui.loading();

		const query = {
			hostName: filters.hostName,
			appName: filters.appName,
			status: filters.status,
			page: filters.page,
			pageSize: filters.pageSize,
		};
		if (filters.windowHours) {
			/* An absolute instant in ISO with its zone, not a date the server has to guess at.
			 * A window sent without one would be read in whichever offset the server runs in
			 * and would quietly hide hours of history at one end. */
			query.from = new Date(Date.now() - Number(filters.windowHours) * 3600 * 1000).toISOString();
		}

		let page;
		try {
			page = await api.getDaemonAppEvents(query);
		} catch (err) {
			/* Read-only, so a failed read has nothing to preserve — but it must not read as
			 * "nothing has ever happened", which is the opposite claim and the one an operator
			 * would act on. */
			results.innerHTML = ui.banner('warn', 'The supervision history could not be read', err.message || '');
			pagerHost.hidden = true;
			host.querySelector('#count').textContent = '';
			return;
		}

		const items = page.items ?? [];
		const total = page.totalCount ?? items.length;
		host.querySelector('#count').textContent = `${ui.num(total)} ${total === 1 ? 'observation' : 'observations'}`;

		results.innerHTML = items.length
			? episodes(items).map((e) => episodeHtml(ui, e)).join('')
			: ui.empty('Nothing observed', emptyText(filters), 'terminal');

		wireEpisodes(results);

		pagerHost.innerHTML = ui.pager(page.page ?? 1, page.pageSize ?? PAGE_SIZE, total, 'page');
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
		fillApps();
		load();
	});
	appSelect.addEventListener('change', (e) => {
		filters.appName = e.target.value;
		filters.page = 1;
		load();
	});
	host.querySelector('#status-filter').addEventListener('change', (e) => {
		filters.status = e.target.value;
		filters.page = 1;
		load();
	});
	host.querySelector('#window-filter').addEventListener('change', (e) => {
		filters.windowHours = e.target.value;
		filters.page = 1;
		load();
	});
	host.querySelector('[data-act="refresh"]').addEventListener('click', () => load());

	fillApps();
	await load();
}

/* ── Filter helpers ──────────────────────────────────────────── */

/** The application names to offer: one host's, or every name across all of them. */
function appNames(hosts, hostName) {
	const names = new Set();
	for (const h of hosts) {
		if (hostName && h.hostName !== hostName) continue;
		for (const a of h.apps ?? []) names.add(a.name);
	}
	return [...names].sort();
}

/* An empty page means two very different things, and saying the wrong one sends somebody to
 * the wrong machine. With filters on, the filters are the likely reason; without them, the
 * table genuinely holds nothing — which for a healthy deployment is the correct state, not a
 * missing feed. */
function emptyText(filters) {
	const applied = [
		filters.appName ? `application ${filters.appName}` : '',
		filters.hostName ? `host ${filters.hostName}` : '',
		filters.status ? 'that status' : '',
		filters.windowHours ? 'that time window' : '',
	].filter(Boolean);

	if (applied.length) {
		return `No change was observed for ${applied.join(', ')}. Widen the filters above — the history keeps everything ever observed, including for hosts that no longer exist.`;
	}
	return 'No daemon has reported anything different from what was already stored. That is what a deployment where nothing has stopped, restarted or changed process looks like — this page is a record of changes, so on a quiet system it is empty. It is not a feed of process output, and there is nothing here to switch on.';
}

/* ── Episodes ────────────────────────────────────────────────── */

/**
 * Parses a timestamp from the API, treating one with no zone designator as UTC.
 *
 * The same rule as in `ui.js`, which does not export its parser. Needed here because grouping
 * is arithmetic rather than display: rows are gathered by how far apart they are, and reading
 * them as local time would still group correctly but the SPAN shown in each headline would be
 * wrong the moment two rows straddled a clock change.
 */
function parseUtc(iso) {
	if (typeof iso !== 'string') return new Date(iso);
	return /[Zz]$|[+-]\d{2}:?\d{2}$/.test(iso) ? new Date(iso) : new Date(iso + 'Z');
}

function at(iso) {
	const time = parseUtc(iso).getTime();
	return Number.isFinite(time) ? time : 0;
}

/**
 * Gathers observations of one application into episodes.
 *
 * The rows arrive newest first, so each one is older than the last. A row joins the open
 * episode for its own application when it falls within the gap of that episode's oldest row —
 * a restart loop therefore becomes one block rather than eight rows.
 *
 * Deliberately not "consecutive rows only". Two applications failing at once interleave their
 * observations, and an adjacency rule would shred both runs into single rows at exactly the
 * moment the page has the most to say. Episodes are emitted in the order they open, so the
 * list still runs newest first.
 *
 * Grouping is done over the page in hand, so an episode straddling a page boundary is shown as
 * two. Fixing that would mean fetching rows the operator did not ask to see, and the count in
 * each headline would then disagree with the page it is drawn on.
 */
export function episodes(items) {
	const out = [];
	const open = new Map();

	for (const item of items) {
		// Names are compared as they arrive; the server has already trimmed and clamped them.
		const key = `${item.hostName} ${item.appName}`;
		const episode = open.get(key);

		if (episode && at(episode.oldestUtc) - at(item.observedUtc) <= EPISODE_GAP_MS) {
			episode.items.push(item);
			episode.oldestUtc = item.observedUtc;
			continue;
		}

		const started = {
			hostName: item.hostName,
			appName: item.appName,
			newestUtc: item.observedUtc,
			oldestUtc: item.observedUtc,
			items: [item],
		};
		out.push(started);
		// A gap that big ends the old episode: anything older belongs to this one instead.
		open.set(key, started);
	}
	return out;
}

/** Whether this observation is a process that was not there — or was a different one — before. */
function isRelaunch(event) {
	// A first sighting is not a relaunch: there was nothing to relaunch from.
	if (event.previousStatus == null) return false;
	return event.processId != null && event.processId !== event.previousProcessId;
}

/**
 * What a run of observations amounted to, in one line.
 *
 * This is the page's actual job. "Six rows" is data; "restarted five times in four minutes and
 * then the daemon gave up" is the thing somebody came to find out, and it is the difference
 * between a page that can be skimmed at three in the morning and one that cannot.
 */
function summarise(episode) {
	const { items } = episode;
	const relaunches = items.filter(isRelaunch).length;
	const gaveUp = items.some((e) => e.status === STATUS_EXHAUSTED);
	// Newest first, so the first row is where the application ended up.
	const ended = items[0];
	const spanSeconds = Math.max(0, (at(episode.newestUtc) - at(episode.oldestUtc)) / 1000);

	let text;
	if (relaunches > 1) text = `Restarted ${relaunches} times`;
	else if (relaunches === 1) text = 'Restarted once';
	else text = `${items.length} changes`;

	let tone = 'info';
	if (gaveUp) tone = 'danger';
	else if (relaunches > 1) tone = 'warn';

	const tail = gaveUp
		? ', then the daemon gave up'
		: ended.status === STATUS_HEALTHY
			? ', then recovered'
			: '';

	return { text, tone, tail, spanSeconds, relaunches, gaveUp };
}

function statusTone(status) {
	switch (status) {
		case 1: return 'ok';       // Healthy
		case 2: return 'info';     // Starting
		case 3: return 'warn';     // Stopped
		case 4: return 'danger';   // Exhausted
		case 5: return '';         // Stopped by operator: deliberate, so not a fault colour.
		default: return '';        // Unknown, and anything a newer daemon reports.
	}
}

function episodeHtml(ui, episode) {
	const single = episode.items.length === 1;
	const summary = single ? null : summarise(episode);
	/* A lone observation is toned by where it left the application; a run is toned by what the
	 * run amounted to, which is not the same thing — a loop that ended Healthy is still the
	 * thing worth seeing. */
	const alert = single ? episode.items[0].status === STATUS_EXHAUSTED : summary.tone === 'danger';

	// The headline's contents, so that the same markup can sit in a button or in a plain div.
	const head = `
		<div>
			<span class="cell-primary">${ui.esc(episode.appName)}</span>
			<span class="cell-sub"> on ${ui.esc(episode.hostName)}</span>
			${summary ? ` ${ui.badge(`${summary.text}${summary.tail}`, summary.tone)}` : ''}
		</div>
		<div class="small muted nowrap" title="${ui.esc(ui.dateTime(episode.newestUtc))}">
			${ui.ago(episode.newestUtc)}${summary && summary.spanSeconds > 0
				? ` <span class="faint">over ${ui.duration(summary.spanSeconds)}</span>`
				: ''}
		</div>`;

	const rows = episode.items.map((e) => eventHtml(ui, e)).join('');

	/* A run is collapsible and starts open: the operator who filtered down to one flapping
	 * application wants every transition, and the one scanning all of them wants to fold the
	 * noisy block away. A single observation has nothing to fold, so its headline is not a
	 * button — a control that does nothing is worse than no control. */
	return `
		<div class="episode${alert ? ' is-alert' : ''}">
			${single
				? `<div class="row-between episode-head">${head}</div>`
				: `<button class="row-between episode-head" data-act="fold" aria-expanded="true">${head}</button>`}
			<div class="episode-body">${rows}</div>
		</div>`;
}

function eventHtml(ui, e) {
	const restarts = e.previousRestartAttempts != null && e.restartAttempts !== e.previousRestartAttempts
		? `${e.previousRestartAttempts} → ${e.restartAttempts}`
		: `${e.restartAttempts}`;

	return `
		<div class="row-between episode-event${e.status === STATUS_EXHAUSTED ? ' is-alert' : ''}">
			<div class="episode-event-text">
				${ui.statusBadge(STATUS_LABELS[e.status] ?? e.statusName ?? String(e.status), statusTone(e.status))}
				<span class="small">${ui.esc(e.description || '')}</span>
			</div>
			<div class="small muted nowrap">
				<span class="tnum" title="Restart attempts in the current failure cycle">${ui.esc(restarts)} restarts</span>
				<span class="faint" title="${ui.esc(ui.dateTime(e.observedUtc))}">${ui.ago(e.observedUtc)}</span>
			</div>
		</div>`;
}

/** Folds an episode away. Nothing is fetched or re-rendered; the rows are already there. */
function wireEpisodes(results) {
	for (const button of [...results.querySelectorAll('[data-act="fold"]')]) {
		const body = button.parentElement?.querySelector('.episode-body');
		if (!body) continue;
		button.addEventListener('click', () => {
			body.hidden = !body.hidden;
			button.setAttribute('aria-expanded', String(!body.hidden));
		});
	}
}
