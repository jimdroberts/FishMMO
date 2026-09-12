/*
 * Support → Characters. Search and inspect any character, read-only.
 *
 * Everything shown here comes from the characters table itself. Sub-entity data —
 * attributes, items, guild membership — lives in its own tables behind its own services,
 * which the panel does not read yet; rather than show invented rows, this page shows what
 * the backend actually sends. Race is an ID: templates are Unity assets, not database rows.
 */

export async function render(host, ctx) {
	if (ctx.param) return renderDetail(host, ctx, ctx.param);
	return renderList(host, ctx);
}

async function renderList(host, ctx) {
	const { api, ui } = ctx;
	const filters = { query: '', online: '', includeDeleted: false, page: 1, pageSize: 15 };

	host.innerHTML = `
		<div class="page-head">
			<div class="page-head-text">
				<h1>Characters</h1>
				<p class="page-head-sub">
					Search across every account. Presence comes from the character's session lease, so a
					character shown in world genuinely has a scene server holding it.
				</p>
			</div>
		</div>
		<section class="card">
			<div class="toolbar">
				<input class="search" id="q" type="search" placeholder="Character or account name…" autocomplete="off" />
				<select id="online">
					<option value="">Anywhere</option>
					<option value="online">In world</option>
					<option value="offline">Offline</option>
				</select>
				<label class="check-row" style="margin:0">
					<input type="checkbox" id="deleted" />
					<span>Include deleted</span>
				</label>
				<span class="grow"></span>
				<span class="small muted" id="count"></span>
			</div>
			<div class="card-body flush" id="results">${ui.loading()}</div>
			<footer class="card-foot" id="pager"></footer>
		</section>`;

	const results = host.querySelector('#results');
	const pagerHost = host.querySelector('#pager');
	const isAdmin = ctx.session.accessLevel >= 3;

	async function load() {
		results.innerHTML = ui.loading();
		const page = await api.searchCharacters(filters);
		host.querySelector('#count').textContent = `${ui.num(page.totalCount)} matching`;

		results.innerHTML = ui.table({
			columns: [
				{
					label: 'Character',
					cell: (c) => `
						<div class="row">
							<span class="avatar avatar-sm ${ui.avatarClass(c.name)}">${ui.esc(ui.initials(c.name))}</span>
							<span>
								<span class="cell-primary">${ui.esc(c.name)}</span>
								<span class="cell-sub" style="display:block">race ${c.raceId}</span>
							</span>
						</div>`,
				},
				{ label: 'Account', cell: (c) => `<a href="#/support/accounts/${encodeURIComponent(c.account)}">${ui.esc(c.account)}</a>` },
				{ label: 'State', cell: (c) => c.deleted
					? ui.badge('deleted', 'danger')
					: c.online ? ui.statusBadge('in world', 'ok', true) : ui.badge('offline') },
				{ label: 'Access level', cell: (c) => ui.levelBadge(c.accessLevel) },
				{ label: 'Last saved', cell: (c) => ui.ago(c.lastSavedUtc) },
				{
					label: '',
					align: 'right',
					cell: (c) => `
						<div class="cell-actions">
							<a class="btn btn-sm" href="#/support/characters/${c.id}">Inspect</a>
							${isAdmin ? `<a class="btn btn-sm" href="#/admin/characters/${c.id}">Edit</a>` : ''}
						</div>`,
				},
			],
			rows: page.items,
			emptyText: 'No character matches that search.',
		});

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
	host.querySelector('#q').addEventListener('input', (e) => {
		clearTimeout(debounce);
		debounce = setTimeout(() => {
			filters.query = e.target.value.trim();
			filters.page = 1;
			load();
		}, 220);
	});
	host.querySelector('#online').addEventListener('change', (e) => {
		filters.online = e.target.value;
		filters.page = 1;
		load();
	});
	host.querySelector('#deleted').addEventListener('change', (e) => {
		filters.includeDeleted = e.target.checked;
		filters.page = 1;
		load();
	});

	await load();
}

async function renderDetail(host, ctx, id) {
	const { api, ui } = ctx;
	const c = await api.getCharacter(id);
	const isAdmin = ctx.session.accessLevel >= 3;

	host.innerHTML = `
		<div class="page-head">
			<div class="page-head-text">
				<div class="row" style="margin-bottom:var(--sp-2)">
					<a class="small muted" href="#/support/characters">← All characters</a>
				</div>
				<div class="row">
					<span class="avatar avatar-lg ${ui.avatarClass(c.name)}">${ui.esc(ui.initials(c.name))}</span>
					<div>
						<h1>${ui.esc(c.name)}</h1>
						<div class="page-head-sub">
							race ${c.raceId} ·
							<a href="#/support/accounts/${encodeURIComponent(c.account)}">${ui.esc(c.account)}</a>
						</div>
					</div>
				</div>
			</div>
			<div class="page-head-actions">
				${c.online ? ui.statusBadge('In world', 'ok', true) : ui.badge('Offline')}
				${isAdmin ? `<a class="btn btn-primary" href="#/admin/characters/${c.id}">Open in the editor</a>` : ''}
			</div>
		</div>

		<div class="stack-lg">
			${c.deleted ? ui.banner('danger', 'This character is deleted', `Removed ${ui.ago(c.timeDeletedUtc)}. An operator can restore it from the editor.`) : ''}
			${!c.editLock.editable && !c.deleted
				? ui.banner('warn', 'Held by a live session', c.editLock.message)
				: ''}

			<div class="grid grid-3">
				${ui.stat({ label: 'Access level', value: ui.levelName(c.accessLevel) })}
				${ui.stat({ label: 'Row version', value: ui.num(c.version), note: 'Bumps on every save' })}
				${ui.stat({ label: 'Last saved', value: ui.ago(c.lastSavedUtc) })}
			</div>

			<div class="split">
				<div class="stack">
					${ui.card({
						title: 'Attributes, items and guild are not loaded',
						body: `
							<p class="small muted">
								Each of those lives in its own table behind its own service, with its own
								version contract, and the panel does not read them yet. This page shows the
								character row, which is what the operator actions here work on.
							</p>`,
					})}
				</div>

				<div class="stack">
					${ui.card({
						title: 'Identity',
						body: `
							<dl class="dl">
								<dt>Character id</dt><dd class="tnum">${c.id}</dd>
								<dt>Account</dt><dd><a href="#/support/accounts/${encodeURIComponent(c.account)}">${ui.esc(c.account)}</a></dd>
								<dt>Access level</dt><dd>${ui.levelBadge(c.accessLevel)}</dd>
								<dt>Race</dt><dd class="tnum">${c.raceId}</dd>
								<dt>Selected</dt><dd>${c.selected ? 'yes' : '<span class="faint">no</span>'}</dd>
								<dt>Created</dt><dd>${ui.dateTime(c.createdUtc)}</dd>
							</dl>`,
					})}

					${ui.card({
						title: 'Position',
						body: `
							<dl class="dl">
								<dt>Scene</dt><dd>${ui.esc(c.sceneName)}</dd>
								<dt>World server</dt><dd class="tnum">${c.worldServerId || '<span class="faint">—</span>'}</dd>
								<dt>Coordinates</dt><dd class="tnum">${c.x.toFixed(1)}, ${c.y.toFixed(1)}, ${c.z.toFixed(1)}</dd>
								<dt>Bind scene</dt><dd>${ui.esc(c.bindScene)}</dd>
							</dl>`,
					})}

					${ui.card({
						title: 'Session',
						body: `
							<dl class="dl">
								<dt>State</dt><dd>${c.sessionState === 1 ? 'Online' : 'Offline'}</dd>
								<dt>Owner server</dt><dd>${c.sessionOwnerServerId || '<span class="faint">none</span>'}</dd>
								<dt>Lease expires</dt><dd>${c.sessionOwnerServerId ? ui.ago(c.sessionLeaseExpiresUtc) : '<span class="faint">—</span>'}</dd>
								<dt>Editable</dt><dd>${c.editLock.editable ? ui.badge('yes', 'ok') : ui.badge('no', 'warn')}</dd>
							</dl>`,
						foot: 'A live lease means the owning scene server holds this character in memory and will overwrite the row on its next save.',
					})}
				</div>
			</div>
		</div>`;
}
