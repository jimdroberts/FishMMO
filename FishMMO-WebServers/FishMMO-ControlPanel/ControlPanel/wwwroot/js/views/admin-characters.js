/*
 * Administration → Character editor.
 *
 * The most dangerous page in the panel. A live character's row is not
 * authoritative: its state lives in the owning scene server's memory and is
 * written over the row on the next persistence pass, and the persistence layer's
 * version contract means an out-of-band write can be silently discarded. So the
 * editor refuses any character holding a live session lease, and offers "kick
 * and wait" instead.
 */

export async function render(host, ctx) {
	if (ctx.param) return renderEditor(host, ctx, ctx.param);
	return renderPicker(host, ctx);
}

async function renderPicker(host, ctx) {
	const { api, ui } = ctx;
	const filters = { query: '', online: '', includeDeleted: true, page: 1, pageSize: 15 };

	host.innerHTML = `
		<div class="page-head">
			<div class="page-head-text">
				<h1>Character editor</h1>
				<p class="page-head-sub">
					Load a character and change its stored state. Only characters that are offline and not
					holding a session lease can be edited — everything else is read-only until it is.
				</p>
			</div>
		</div>

		<div class="stack-lg">
			${ui.banner('warn', 'Editing a live character would be silently discarded',
				'While a character is in the world the owning scene server holds it in memory. Attribute and ability versions only move on save, so a row written from here can be overwritten, or can clear a dirty flag that should have stayed set.')}

			<section class="card">
				<div class="toolbar">
					<input class="search" id="q" type="search" placeholder="Character or account name…" autocomplete="off" />
					<select id="online">
						<option value="">Anywhere</option>
						<option value="offline">Editable (offline)</option>
						<option value="online">In world</option>
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

	async function load() {
		results.innerHTML = ui.loading();
		const page = await api.searchCharacters(filters);
		host.querySelector('#count').textContent = `${ui.num(page.totalCount)} matching`;

		results.innerHTML = ui.table({
			columns: [
				{ label: 'Character', cell: (c) => `<div class="cell-primary">${ui.esc(c.name)}</div><div class="cell-sub">race ${c.raceId}</div>` },
				{ label: 'Account', cell: (c) => `<a href="#/support/accounts/${encodeURIComponent(c.account)}">${ui.esc(c.account)}</a>` },
				{ label: 'Editable', cell: (c) => c.deleted
					? ui.badge('deleted', 'danger')
					: c.online ? ui.badge('held by a server', 'warn') : ui.badge('yes', 'ok') },
				{ label: 'Last saved', cell: (c) => ui.ago(c.lastSavedUtc) },
				{ label: '', align: 'right', cell: (c) => `<a class="btn btn-sm btn-primary" href="#/admin/characters/${c.id}">Open</a>` },
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

	await load();
}

/* Attributes and currency are not here. Both would need their own sub-entity services and
 * their own version contract, and a tab that shows numbers the backend never sent is worse
 * than no tab. */
const TABS = [
	['position', 'Position'],
	['identity', 'Identity'],
	['record', 'Record'],
];

async function renderEditor(host, ctx, id) {
	const { api, ui } = ctx;
	let tab = 'position';
	let waiting = null;

	ctx.onCleanup(() => clearInterval(waiting));

	async function draw() {
		const c = await api.getCharacter(id);
		const lock = c.editLock;

		host.innerHTML = `
			<div class="page-head">
				<div class="page-head-text">
					<div class="row" style="margin-bottom:var(--sp-2)">
						<a class="small muted" href="#/admin/characters">← Pick another character</a>
					</div>
					<div class="row">
						<span class="avatar avatar-lg ${ui.avatarClass(c.name)}">${ui.esc(ui.initials(c.name))}</span>
						<div>
							<h1>${ui.esc(c.name)}</h1>
							<div class="page-head-sub">
								race ${c.raceId} ·
								<a href="#/support/accounts/${encodeURIComponent(c.account)}">${ui.esc(c.account)}</a> ·
								row version <span class="tnum">${ui.num(c.version)}</span>
							</div>
						</div>
					</div>
				</div>
				<div class="page-head-actions">
					<a class="btn" href="#/support/characters/${c.id}">Read-only view</a>
					${lock.editable ? ui.statusBadge('editable', 'ok') : ui.statusBadge('locked', 'warn')}
				</div>
			</div>

			<div class="stack-lg">
				${lock.editable
					? ui.banner('ok', 'Offline and safe to edit', 'No server holds a lease on this character, so a write here is authoritative.')
					: lockBanner(ui, c, lock)}

				<div>
					<div class="tabs">
						${TABS.map(([key, label]) => `
							<button class="tab${tab === key ? ' is-active' : ''}" data-tab="${key}">${ui.esc(label)}</button>`).join('')}
					</div>
					<div id="tab-body"></div>
				</div>
			</div>`;

		host.querySelectorAll('[data-tab]').forEach((b) =>
			b.addEventListener('click', () => {
				tab = b.dataset.tab;
				draw();
			}),
		);

		host.querySelector('[data-act="kick-wait"]')?.addEventListener('click', async () => {
			const v = await ui.modal({
				title: 'Kick and wait',
				sub: `Writes a kick request for "${c.account}", then waits for the session lease to lapse. The character becomes editable once its owning server has released it.`,
				confirmLabel: 'Kick and wait',
				confirmTone: 'danger',
				body: `
					<div class="field">
						<label for="reason">Reason (recorded in the audit log)</label>
						<textarea id="reason" name="reason" required></textarea>
					</div>`,
				onSubmit: (values) => (String(values.reason).trim() ? values : false),
			});
			if (!v) return;
			if (await ctx.attempt(() => api.kickAccount(c.account, v.reason), 'Kick request written', 'Waiting for the lease to lapse.')) {
				clearInterval(waiting);
				waiting = setInterval(async () => {
					const state = await api.getCharacterEditLock(id);
					if (state.editable) {
						clearInterval(waiting);
						ui.toast('The character is free', 'Its lease has lapsed and it can be edited.', 'ok');
						draw();
					}
				}, 2000);
			}
		});

		const body = host.querySelector('#tab-body');
		if (tab === 'position') body.innerHTML = positionTab(ui, c, lock);
		else if (tab === 'identity') body.innerHTML = identityTab(ui, c, lock);
		else body.innerHTML = recordTab(ui, c);

		wireTab(body, ctx, c, lock, draw, tab);
	}

	await draw();
}

function lockBanner(ui, c, lock) {
	if (lock.reason === 'deleted') {
		return ui.banner('danger', 'This character is deleted', 'Restore it from the Record tab before editing. It can still be renamed.');
	}
	return `
		<div class="banner banner-warn">
			<span class="banner-icon">${ui.icon('alert-triangle')}</span>
			<div class="banner-body">
				<div class="banner-title">Held by a live session — editing is refused</div>
				<div class="muted small">${ui.esc(lock.message)}</div>
				<div class="muted small" style="margin-top:var(--sp-1)">
					Owner: ${ui.esc(lock.ownerServerName ?? `server ${lock.ownerServerId}`)} ·
					lease expires ${ui.ago(lock.leaseExpiresUtc)}
				</div>
				<div style="margin-top:var(--sp-3)">
					<button class="btn btn-danger btn-sm" data-act="kick-wait">Kick and wait</button>
				</div>
			</div>
		</div>`;
}

function fieldset(ui, lock, inner, submitLabel = 'Save changes') {
	return `
		<form class="stack" ${lock.editable ? '' : 'data-locked="1"'}>
			<fieldset style="border:0;padding:0;margin:0" ${lock.editable ? '' : 'disabled'}>
				${inner}
			</fieldset>
			<div><button class="btn btn-primary" type="submit" ${lock.editable ? '' : 'disabled'}>${ui.esc(submitLabel)}</button></div>
		</form>`;
}

function positionTab(ui, c, lock) {
	return ui.card({
		title: 'Position and scene',
		sub: 'The lowest-risk edit, and the one operators reach for most: unsticking a character that fell out of the world.',
		body: fieldset(ui, lock, `
			<div class="field-row">
				<div class="field"><label for="x">X</label><input id="x" name="x" type="number" step="0.01" value="${c.x}" /></div>
				<div class="field"><label for="y">Y</label><input id="y" name="y" type="number" step="0.01" value="${c.y}" /></div>
				<div class="field"><label for="z">Z</label><input id="z" name="z" type="number" step="0.01" value="${c.z}" /></div>
			</div>
			<div class="field-row">
				<div class="field">
					<label for="sceneName">Scene</label>
					<input id="sceneName" name="sceneName" value="${ui.esc(c.sceneName)}" />
				</div>
				<div class="field">
					<label for="bindScene">Bind scene</label>
					<input id="bindScene" name="bindScene" value="${ui.esc(c.bindScene)}" />
					<div class="field-hint">Where the character respawns and where a teleport home lands.</div>
				</div>
			</div>`),
		foot: 'The write is one transaction that re-reads the lease and bumps the row version, so a racing server write loses the concurrency check rather than interleaving.',
	});
}

/**
 * Renaming is allowed whenever nothing holds a live lease — including on a deleted
 * character, because moving the deleted row off a contested name is how an operator
 * clears the way for a restore.
 */
function renamable(lock) {
	return lock.editable || lock.reason === 'deleted';
}

function identityTab(ui, c, lock) {
	return ui.card({
		title: 'Identity',
		body: fieldset(ui, lock, `
			<div class="field-row">
				<div class="field">
					<label for="accessLevel">Character access level</label>
					<select id="accessLevel" name="accessLevel">
						${[0, 1, 2, 3].map((l) => `<option value="${l}"${c.accessLevel === l ? ' selected' : ''}>${ui.levelName(l)}</option>`).join('')}
					</select>
					<div class="field-hint">Separate from the account's level; it gates the in-game commands this character may use.</div>
				</div>
			</div>
			`),
		foot: `
			<div class="row-between">
				<div>
					<strong>${ui.esc(c.name)}</strong>
					<div class="small muted">
						The name carries a unique index, so renaming is its own action: a collision
						has to be reported rather than silently lost.
					</div>
				</div>
				<button class="btn" type="button" data-act="rename" ${renamable(lock) ? '' : 'disabled'}>Rename…</button>
			</div>`,
	});
}

/**
 * The character's stored record, plus Restore when it is deleted.
 *
 * There is no delete here, and no create. Destroying a character is in-game only:
 * deletion spans fourteen sub-entity tables inside one unit of work in the login
 * server's character-select system, and a second implementation of that is how a shard
 * loses data. Restore is on the other side of that line — it clears two columns on a row
 * that is still there.
 */
function recordTab(ui, c) {
	return `
		<div class="grid grid-2">
			${ui.card({
				title: 'Record',
				body: `
					<dl class="dl">
						<dt>Character id</dt><dd class="tnum">${c.id}</dd>
						<dt>Account</dt><dd><a href="#/support/accounts/${encodeURIComponent(c.account)}">${ui.esc(c.account)}</a></dd>
						<dt>Row version</dt><dd class="tnum">${ui.num(c.version)}</dd>
						<dt>Created</dt><dd>${ui.dateTime(c.createdUtc)}</dd>
						<dt>Last saved</dt><dd>${ui.dateTime(c.lastSavedUtc)}</dd>
						<dt>Deleted</dt><dd>${c.deleted ? ui.dateTime(c.timeDeletedUtc) : '<span class="faint">no</span>'}</dd>
						${c.deleted ? `<dt>Stored as</dt><dd><code class="xsmall">${ui.esc(c.storedName)}</code></dd>` : ''}
					</dl>`,
			})}
			${c.deleted
				? ui.card({
					title: 'Restore',
					sub: 'Brings back a soft-deleted character.',
					body: `
						${ui.banner('danger', 'This character is deleted', `Removed ${ui.ago(c.timeDeletedUtc)}.`)}
						<p class="small muted" style="margin-top:var(--sp-3)">
							Deleting renamed the row so that the name could be taken by somebody else.
							Restoring puts <strong>${ui.esc(c.name)}</strong> back, and is refused if
							that name is now in use.
						</p>
						<p class="small muted" style="margin-top:var(--sp-3)">
							A character deleted without the keep-data switch has already lost its
							sub-entity rows, so what comes back is the character record, not
							everything it owned.
						</p>
						<div style="margin-top:var(--sp-4)">
							<button class="btn btn-primary" data-act="restore">Restore character</button>
						</div>`,
				})
				: ui.card({
					title: 'Deletion is the game\'s to decide',
					body: `
						<p class="small muted">
							Characters are created and deleted through the login server, which owns that
							whole transaction across fourteen sub-entity tables. The panel deliberately
							has no second copy of it, so a character that needs removing is removed in
							the game client.
						</p>
						<p class="small muted" style="margin-top:var(--sp-3)">
							Restoring one that was already deleted, and renaming one that exists, are
							both available here — neither destroys anything.
						</p>`,
				})}
		</div>`;
}

function wireTab(body, ctx, c, lock, redraw, tab) {
	const { api, ui } = ctx;
	const form = body.querySelector('form');

	const askReason = (title, sub, confirmLabel, tone = 'primary') =>
		ui.modal({
			title,
			sub,
			confirmLabel,
			confirmTone: tone,
			body: `
				<div class="field">
					<label for="reason">Reason (recorded in the audit log)</label>
					<textarea id="reason" name="reason" required></textarea>
				</div>`,
			onSubmit: (values) => (String(values.reason).trim() ? values : false),
		});

	if (form) {
		form.addEventListener('submit', async (e) => {
			e.preventDefault();
			const values = Object.fromEntries(new FormData(form).entries());
			const v = await askReason('Save changes', `Writing to "${c.name}".`, 'Save');
			if (!v) return;

			/* Only the fields this tab actually carries are sent. The service treats a
			 * missing field as "leave alone", so a position save cannot blank an access level. */
			const patch = {};
			for (const key of ['x', 'y', 'z', 'accessLevel']) {
				if (values[key] !== undefined) patch[key] = Number(values[key]);
			}
			for (const key of ['sceneName', 'bindScene']) {
				if (values[key] !== undefined) patch[key] = values[key];
			}
			if (await ctx.attempt(() => api.patchCharacter(c.id, patch, v.reason), 'Character saved')) redraw();
		});
	}

	body.querySelector('[data-act="rename"]')?.addEventListener('click', async () => {
		/* The call is made from inside the modal so that the one failure the operator can
		 * act on — the name is taken — appears next to the field they must change, instead
		 * of closing the dialog and making them start over. */
		const rule = (await api.getAccountPolicy()).characterName;
		const done = await ui.modal({
			title: `Rename "${c.name}"`,
			sub: 'Names are unique, case-insensitively. A collision is refused, not silently applied.',
			confirmLabel: 'Rename',
			body: `
				<div class="field">
					<label for="newName">New name</label>
					<input id="newName" name="newName" required autocomplete="off"
						minlength="${rule.minLength}" maxlength="${rule.maxLength}" />
					<div class="field-hint">${ui.esc(rule.error)}</div>
				</div>
				<div class="field">
					<label for="reason">Reason (recorded in the audit log)</label>
					<textarea id="reason" name="reason" required></textarea>
				</div>`,
			onSubmit: async (values) => {
				const name = String(values.newName).trim();
				if (!String(values.reason).trim()) return false;
				// The same rule the server applies, so the obvious mistakes never need a round trip.
				if (!new RegExp(rule.pattern).test(name) || name.length < rule.minLength || name.length > rule.maxLength) {
					throw new Error(rule.error);
				}
				if (name === c.name) throw new Error('That is already the name.');
				await ctx.withStepUp(() => api.renameCharacter(c.id, name, values.reason));
				return true;
			},
		});
		if (done) {
			ui.toast('Character renamed', '', 'ok');
			redraw();
		}
	});

	body.querySelector('[data-act="restore"]')?.addEventListener('click', async () => {
		const done = await ui.modal({
			title: `Restore "${c.name}"`,
			sub: 'Clears the deletion. Refused if the name has been taken since.',
			confirmLabel: 'Restore character',
			body: `
				<div class="field">
					<label for="reason">Reason (recorded in the audit log)</label>
					<textarea id="reason" name="reason" required></textarea>
				</div>`,
			onSubmit: async (values) => {
				if (!String(values.reason).trim()) return false;
				await ctx.withStepUp(() => api.restoreCharacter(c.id, values.reason));
				return true;
			},
		});
		if (done) {
			ui.toast('Character restored', 'It is offline and editable.', 'ok');
			redraw();
		}
	});
}
