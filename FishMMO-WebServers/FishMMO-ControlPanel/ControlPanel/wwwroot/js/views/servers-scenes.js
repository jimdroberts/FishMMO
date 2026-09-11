/*
 * Servers → Scene instances. Every scene row a scene server is holding, read-only.
 *
 * This page has no actions, and that is a decision rather than work left undone. The only
 * action anybody would want here is "retire this instance", and there is no safe way to
 * write it: an instance with players inside it cannot be removed without stranding them,
 * and a row deleted underneath a running scene server is a row it is still serving. The
 * path that drains an instance safely is a scene server shutdown, which warns its players,
 * saves them and lets the instances end — that lives on the board.
 *
 * Scene names come from the game's own scene assets and the server names from the servers
 * themselves, so both go through `ui.esc`.
 */

/* The wire values of SceneStatus. The server sends `statusName` beside the number, and that
 * is preferred where it arrives; this table is what the filter offers and what an
 * unlabelled row falls back to. */
const STATUSES = [
	{ value: 0, label: 'Pending', tone: 'info', pulse: true },
	{ value: 1, label: 'Loading', tone: 'warn', pulse: true },
	{ value: 2, label: 'Ready', tone: 'ok', pulse: false },
	{ value: 3, label: 'Failed', tone: 'danger', pulse: false },
];

export async function render(host, ctx) {
	const { api, ui } = ctx;
	const filters = { status: '', sceneServerId: '', page: 1, pageSize: 25 };

	/* The scene rows carry server ids, not names. The board is the only place those ids have
	 * names, so it is read once to build the lookup and to populate the scene-server filter.
	 * A board that cannot be read costs the names, not the page: the table falls back to
	 * "scene server 4", which is still enough to find it. */
	let sceneServerNames = new Map();
	let worldServerNames = new Map();
	let sceneServerOptions = [];
	try {
		const board = await api.getServerBoard();
		sceneServerNames = new Map((board.sceneServers ?? []).map((s) => [String(s.id), s.name]));
		worldServerNames = new Map((board.worldServers ?? []).map((s) => [String(s.id), s.name]));
		sceneServerOptions = (board.sceneServers ?? []).map((s) => ({ id: s.id, name: s.name }));
	} catch {
		sceneServerNames = new Map();
		worldServerNames = new Map();
		sceneServerOptions = [];
	}

	host.innerHTML = `
		<div class="page-head">
			<div class="page-head-text">
				<h1>Scene instances</h1>
				<p class="page-head-sub">
					Every scene a scene server currently holds — the open world, dungeon and arena
					instances alike. A scene server is not tied to one world: each takes work from a
					single pending queue, which is why a row names both servers.
				</p>
			</div>
			<div class="page-head-actions">
				<button class="btn" data-act="refresh">${ui.icon('refresh')} Refresh</button>
			</div>
		</div>

		<div class="stack-lg">
			${ui.banner('info', 'Retiring an instance is not offered here',
				'There is no safe way to write it. An instance with players inside cannot be removed without stranding them mid-scene, and deleting the row would not tell the scene server holding it to stop. Shutting the scene server down is the path that drains them properly: it warns the players, saves them and stops. That control is on the board.')}

			<section class="card">
				<div class="toolbar">
					<select id="status">
						<option value="">Any status</option>
						${STATUSES.map((s) => `<option value="${s.value}">${ui.esc(s.label)}</option>`).join('')}
					</select>
					<select id="sceneServer">
						<option value="">Any scene server</option>
						${sceneServerOptions.map((s) => `<option value="${ui.esc(s.id)}">${ui.esc(s.name)}</option>`).join('')}
					</select>
					<span class="grow"></span>
					<span class="small muted" id="count"></span>
				</div>
				<div class="card-body flush" id="results">${ui.loading()}</div>
				<footer class="card-foot" id="pager"></footer>
			</section>
		</div>`;

	const results = host.querySelector('#results');
	const pagerHost = host.querySelector('#pager');
	const countHost = host.querySelector('#count');

	async function load() {
		results.innerHTML = ui.loading();
		const page = await api.getScenes(filters);
		const items = page.items ?? [];
		countHost.textContent = `${ui.num(page.totalCount)} instance${page.totalCount === 1 ? '' : 's'}`;

		results.innerHTML = items.length
			? ui.table({
				columns: [
					{
						label: 'Scene',
						cell: (s) => `
							<div class="cell-primary">${ui.esc(s.sceneName)}</div>
							<div class="cell-sub">handle ${ui.esc(s.sceneHandle)}${s.characterId ? ` · opened for character ${ui.esc(s.characterId)}` : ''}</div>`,
					},
					{ label: 'Status', cell: (s) => statusBadge(ui, s) },
					{ label: 'Type', cell: (s) => ui.badge(typeLabel(s)) },
					{ label: 'Players', align: 'right', cell: (s) => `<span class="tnum">${ui.num(s.characterCount)}</span>` },
					{
						label: 'Hosted by',
						cell: (s) => `
							<div>${ui.esc(serverName(sceneServerNames, s.sceneServerId, 'scene server'))}</div>
							<div class="cell-sub">for ${ui.esc(serverName(worldServerNames, s.worldServerId, 'world server'))}</div>`,
					},
					{ label: 'Access', cell: (s) => accessCell(ui, s) },
					{
						label: 'Age',
						align: 'right',
						cell: (s) => `<span class="nowrap" title="${ui.esc(ui.dateTime(s.timeCreatedUtc))}">${ui.ago(s.timeCreatedUtc)}</span>`,
					},
				],
				rows: items,
				/* A failed instance is the row somebody came to this page to find — a scene that
				 * was asked for and never loaded. It gets the alert tint rather than being left
				 * to a badge that scans the same as every other badge in the column. */
				rowAttrs: (s) => (Number(s.status) === 3 ? 'class="is-alert"' : ''),
			})
			: ui.empty('No scene instance',
				filters.status === '' && filters.sceneServerId === ''
					? 'No scene server is holding a scene. That is what an idle shard looks like — scenes are created on demand, and the open world ones appear as soon as a world server has a scene server to load them.'
					: 'Nothing matches those filters. Clear the status, or pick another scene server.',
				'home');

		pagerHost.innerHTML = ui.pager(page.page, page.pageSize, page.totalCount, 'page');
		pagerHost.hidden = !pagerHost.innerHTML.trim();
		pagerHost.querySelectorAll('[data-action="page"]').forEach((b) =>
			b.addEventListener('click', () => {
				filters.page = Number(b.dataset.page);
				load();
			}),
		);
	}

	host.querySelector('#status').addEventListener('change', (e) => {
		filters.status = e.target.value;
		filters.page = 1;
		load();
	});
	host.querySelector('#sceneServer').addEventListener('change', (e) => {
		filters.sceneServerId = e.target.value;
		filters.page = 1;
		load();
	});
	host.querySelector('[data-act="refresh"]').addEventListener('click', () => load());

	await load();
}

/**
 * A scene's status, preferring the name the server sent.
 *
 * Pending and Loading pulse because they are transient by definition: a row that has been
 * Loading for ten minutes is a scene server that took the work and never finished it, and
 * the moving dot is what makes an operator look at its age.
 */
function statusBadge(ui, s) {
	const known = STATUSES.find((x) => x.value === Number(s.status));
	const label = s.statusName || known?.label || `status ${s.status}`;
	return ui.statusBadge(readable(label), known?.tone ?? '', known?.pulse ?? false);
}

function typeLabel(s) {
	if (s.typeName) return readable(s.typeName);
	return s.type === null || s.type === undefined ? 'unknown' : `type ${s.type}`;
}

/* The names arrive as the game's enum members — "OpenWorld", "Ready" — and a badge reading
 * "OpenWorld" is the only thing in a column of lower-case badges that shouts. Split the
 * words and lower-case it so the column scans as one. */
function readable(name) {
	return String(name).replace(/([a-z0-9])([A-Z])/g, '$1 $2').toLowerCase();
}

/* A private instance is one a party holds: nobody outside it is routed in. The party id is
 * shown beside it because "private" without saying whose is not actionable. */
function accessCell(ui, s) {
	if (s.isPrivate) {
		return `${ui.badge('private', 'warn')}${s.partyId ? `<div class="cell-sub">party ${ui.esc(s.partyId)}</div>` : ''}`;
	}
	return `${ui.badge('open')}${s.partyId ? `<div class="cell-sub">party ${ui.esc(s.partyId)}</div>` : ''}`;
}

/* An id with no name is still an id worth printing: the name is a convenience from the
 * board, and a scene server that has since deregistered has no name to offer. */
function serverName(names, id, noun) {
	return names.get(String(id)) ?? `${noun} ${id}`;
}
