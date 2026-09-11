/*
 * Administration → Audit log.
 *
 * One table records both halves of the operator surface: what was done through this panel,
 * and what was typed as a command in the game. They arrive in the same shape but read
 * differently — an in-game row carries the command word as its target and the whole typed
 * line as its reason — so the two are told apart on sight rather than flattened together.
 *
 * Every value below is operator- or player-supplied text on its way back out of the
 * database, which is why it all goes through `ui.esc`.
 */

export async function render(host, ctx) {
	const { api, ui } = ctx;
	const filters = { actor: '', action: '', outcome: '', from: '', to: '', page: 1, pageSize: 25 };

	/* The list of actions is a convenience for the filter. If it cannot be fetched the log
	 * itself is still worth showing, so the select degrades to "any action". */
	let actions = [];
	try {
		actions = (await api.getAuditActions()) ?? [];
	} catch {
		actions = [];
	}

	host.innerHTML = `
		<div class="page-head">
			<div class="page-head-text">
				<h1>Audit log</h1>
				<p class="page-head-sub">
					Every privileged action, successful or refused, from this panel and from the commands
					operators type in the game. The table is append-only: rows are added and never edited
					or removed.
				</p>
			</div>
		</div>

		<div class="stack-lg">
			${ui.banner('info', 'Refusals are recorded as well as successes',
				'A log that keeps only what worked cannot show somebody probing at permissions they do not have. Refused rows are tinted, and the reason the server gave sits beside them.')}

			<section class="card">
				<div class="toolbar">
					<input class="search" id="actor" type="search" placeholder="Actor account…" autocomplete="off" />
					<select id="action">
						<option value="">Any action</option>
						${actions.map((a) => `<option value="${ui.esc(a)}">${ui.esc(a)}</option>`).join('')}
					</select>
					<select id="outcome">
						<option value="">Anything</option>
						<option value="succeeded">Succeeded</option>
						<option value="refused">Refused</option>
					</select>
					<input id="from" type="date" style="width:auto" aria-label="From date" />
					<input id="to" type="date" style="width:auto" aria-label="To date" />
					<span class="grow"></span>
					<span class="small muted" id="count"></span>
				</div>
				<div class="card-body flush" id="results">${ui.loading()}</div>
				<footer class="card-foot" id="pager"></footer>
			</section>
		</div>`;

	const results = host.querySelector('#results');
	const pagerHost = host.querySelector('#pager');

	async function load() {
		results.innerHTML = ui.loading();
		const page = await api.getAudit(filters);
		host.querySelector('#count').textContent = `${ui.num(page.totalCount)} entries`;

		results.innerHTML = page.items.length
			? ui.table({
				columns: [
					{
						label: 'When',
						cell: (a) => `<span class="nowrap" title="${ui.esc(ui.dateTime(a.occurredUtc))}">${ui.ago(a.occurredUtc)}</span>`,
					},
					{
						label: 'Actor',
						cell: (a) => `
							<div class="row">
								<span class="avatar avatar-sm ${ui.avatarClass(a.actor)}">${ui.esc(ui.initials(a.actor))}</span>
								<span>
									<span class="cell-primary">${ui.esc(a.actor)}</span>
									<span class="cell-sub" style="display:block">${ui.levelBadge(a.actorAccessLevel)}</span>
								</span>
							</div>`,
					},
					{ label: 'Action', cell: (a) => actionCell(ui, a) },
					{ label: 'Target', cell: (a) => targetCell(ui, a) },
					{ label: 'Outcome', cell: (a) => outcomeCell(ui, a) },
					{ label: 'Source', cell: (a) => sourceCell(ui, a) },
					{ label: '', align: 'right', cell: () => '<button class="btn btn-sm" data-act="expand">Details</button>' },
				],
				rows: page.items,
				rowAttrs: (a, i) => `class="is-clickable${a.succeeded ? '' : ' is-refused'}" data-index="${i}" aria-expanded="false"`,
			})
			: ui.empty('Nothing recorded', 'No entry matches those filters. Widen the dates, or clear the action and outcome.', 'flag');

		wireExpanders(results, ui, page.items);

		pagerHost.innerHTML = ui.pager(page.page, page.pageSize, page.totalCount, 'page');
		pagerHost.hidden = !pagerHost.innerHTML.trim();
		pagerHost.querySelectorAll('[data-action="page"]').forEach((b) =>
			b.addEventListener('click', () => {
				filters.page = Number(b.dataset.page);
				load();
			}),
		);
	}

	let debounce;
	host.querySelector('#actor').addEventListener('input', (e) => {
		clearTimeout(debounce);
		debounce = setTimeout(() => {
			filters.actor = e.target.value.trim();
			filters.page = 1;
			load();
		}, 220);
	});
	for (const [id, key] of [['action', 'action'], ['outcome', 'outcome'], ['from', 'from'], ['to', 'to']]) {
		host.querySelector(`#${id}`).addEventListener('change', (e) => {
			filters[key] = e.target.value;
			filters.page = 1;
			load();
		});
	}

	await load();
}

/* The reason is prose — an operator's justification, or the whole command line a game
 * operator typed — so it sits under the action rather than in a column of its own, where
 * its length would push every other column off the screen. */
function actionCell(ui, a) {
	if (!a.reason) return `<code>${ui.esc(a.action)}</code>`;
	return `
		<div><code>${ui.esc(a.action)}</code></div>
		<div class="cell-sub${a.source === 'game' ? ' mono' : ''}" style="max-width:38ch;overflow-wrap:anywhere">${ui.esc(a.reason)}</div>`;
}

function targetCell(ui, a) {
	/* An in-game row inverts the two fields: the command word is the target id and the
	 * character it was aimed at is the name. Showing them the other way round would read as
	 * a character called "/gm". */
	if (a.source === 'game') {
		return `
			<div class="cell-primary">${a.targetName ? ui.esc(a.targetName) : '<span class="faint">—</span>'}</div>
			<div class="cell-sub"><code>${ui.esc(a.targetId)}</code></div>`;
	}

	const label = a.targetName || a.targetId;
	if (!label) return '<span class="faint">—</span>';

	const href = a.targetType === 'account'
		? `#/support/accounts/${encodeURIComponent(a.targetId)}`
		: a.targetType === 'character'
			? `#/support/characters/${encodeURIComponent(a.targetId)}`
			: null;

	return `
		${href ? `<a href="${href}">${ui.esc(label)}</a>` : `<span class="cell-primary">${ui.esc(label)}</span>`}
		<div class="cell-sub">${a.targetType ? ui.esc(a.targetType) : '<span class="faint">—</span>'}</div>`;
}

function outcomeCell(ui, a) {
	if (a.succeeded) return ui.badge('succeeded', 'ok');
	return `
		${ui.statusBadge('refused', 'danger')}
		${a.outcome ? `<div class="cell-sub" style="max-width:28ch">${ui.esc(a.outcome)}</div>` : ''}`;
}

/* Where the action came from changes what it means: a chat command was typed by somebody
 * already in the world, on a character, with no panel session behind it. */
function sourceCell(ui, a) {
	return a.source === 'game' ? ui.badge('in game', 'accent') : ui.badge('panel', 'info');
}

/**
 * Gives every row a hidden sibling holding its details, and toggles it on click.
 *
 * The detail row is built here rather than inside a cell because it spans the whole table;
 * `ui.table` renders one `<tr>` per row, so the second one is inserted afterwards.
 */
function wireExpanders(results, ui, items) {
	const body = results.querySelector('tbody');
	if (!body) return;

	for (const tr of [...body.querySelectorAll('tr[data-index]')]) {
		const entry = items[Number(tr.dataset.index)];
		const button = tr.querySelector('[data-act="expand"]');

		const detail = document.createElement('tr');
		detail.hidden = true;
		detail.innerHTML = `<td colspan="${tr.children.length}" style="background:var(--surface-sunken)">${detailPanel(ui, entry)}</td>`;
		tr.after(detail);

		tr.addEventListener('click', (e) => {
			// A link in the row is a destination of its own, not a toggle.
			if (e.target.closest('a')) return;
			detail.hidden = !detail.hidden;
			tr.setAttribute('aria-expanded', String(!detail.hidden));
			if (button) button.textContent = detail.hidden ? 'Details' : 'Hide';
		});
	}
}

function detailPanel(ui, a) {
	return `
		<div class="grid grid-2">
			<div>
				<div class="small muted" style="margin-bottom:var(--sp-2)">Details</div>
				${detailsBody(ui, a.details)}
			</div>
			<div>
				<div class="small muted" style="margin-bottom:var(--sp-2)">Provenance</div>
				<dl class="dl">
					<dt>Entry</dt><dd class="tnum">${ui.num(a.id)}</dd>
					<dt>Occurred</dt><dd>${ui.dateTime(a.occurredUtc)}</dd>
					<dt>IP address</dt><dd class="mono">${a.ipAddress ? ui.esc(a.ipAddress) : '<span class="faint">—</span>'}</dd>
					<dt>Session</dt><dd class="tnum">${a.sessionId
						? ui.num(a.sessionId)
						: `<span class="faint">${a.source === 'game' ? 'none — typed in the world' : 'none'}</span>`}</dd>
				</dl>
			</div>
		</div>`;
}

/**
 * Renders the `details` column, which arrives as a JSON *string*.
 *
 * It is written by whatever recorded the action, so it cannot be trusted to parse. A row
 * whose details are malformed still says what the server wrote, and still renders.
 */
function detailsBody(ui, raw) {
	const text = typeof raw === 'string' ? raw.trim() : '';
	if (!text) return '<div class="small faint">Nothing recorded.</div>';

	let parsed;
	try {
		parsed = JSON.parse(text);
	} catch {
		return `<pre class="xsmall">${ui.esc(text)}</pre>`;
	}

	// Only an object turns into key/value rows; anything else reads better verbatim.
	if (!parsed || typeof parsed !== 'object' || Array.isArray(parsed)) {
		return `<pre class="xsmall">${ui.esc(text)}</pre>`;
	}

	const pairs = Object.entries(parsed);
	if (!pairs.length) return '<div class="small faint">Nothing recorded.</div>';

	return `<dl class="dl">${pairs
		.map(([key, value]) => `<dt>${ui.esc(key)}</dt><dd>${ui.esc(readable(value))}</dd>`)
		.join('')}</dl>`;
}

function readable(value) {
	if (value === null || value === undefined) return '—';
	if (typeof value === 'object') return JSON.stringify(value);
	return String(value);
}
