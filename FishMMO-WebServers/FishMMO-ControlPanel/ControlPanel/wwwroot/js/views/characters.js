/*
 * My characters — read-only.
 *
 * Creating and deleting a character are IN-GAME ONLY, by decision. Deletion spans
 * fourteen sub-entity tables inside a single unit of work in the login server's
 * character-select system, and a second implementation of that is how a shard loses
 * data. This view lists and inspects; it never changes whether a character exists.
 */

export async function render(host, ctx) {
	const { api, ui } = ctx;
	const characters = await api.getMyCharacters();

	const cards = characters.map((c) => `
		<section class="card">
			<div class="card-body">
				<div class="row" style="align-items:flex-start;gap:var(--sp-3)">
					<span class="avatar avatar-lg ${ui.avatarClass(c.name)}">${ui.esc(ui.initials(c.name))}</span>
					<div class="grow">
						<div class="row-between">
							<div>
								<div class="card-title">${ui.esc(c.name)}</div>
								<div class="card-sub">${ui.esc(c.sceneName || 'Unknown location')}</div>
							</div>
							${c.online ? ui.statusBadge('In world', 'ok', true) : ui.badge('Offline')}
						</div>
						<dl class="dl" style="margin-top:var(--sp-4)">
							<dt>Location</dt><dd>${ui.esc(c.sceneName || '—')}</dd>
							<dt>Bind point</dt><dd>${ui.esc(c.bindScene || '—')}</dd>
							<dt>Coordinates</dt><dd class="tnum">${c.x?.toFixed?.(1) ?? '—'}, ${c.y?.toFixed?.(1) ?? '—'}, ${c.z?.toFixed?.(1) ?? '—'}</dd>
							<dt>Access level</dt><dd>${ui.levelBadge(c.accessLevel)}</dd>
							<dt>Created</dt><dd>${ui.dateTime(c.createdUtc)}</dd>
							<dt>Last saved</dt><dd>${ui.ago(c.lastSavedUtc)}</dd>
						</dl>
					</div>
				</div>
			</div>
			${c.selected ? '<footer class="card-foot"><span class="small">Selected on the character screen</span></footer>' : ''}
		</section>`).join('');

	host.innerHTML = `
		<div class="page-head">
			<div class="page-head-text">
				<h1>My characters</h1>
				<p class="page-head-sub">
					Characters on <strong>${ui.esc(ctx.session.username)}</strong>, as the shard has them
					stored. A character that is in the world is shown as its owning server last saved it.
				</p>
			</div>
		</div>

		<div class="stack-lg">
			${ui.banner('info', 'Characters are created and deleted in the game',
				'Both go through the login server, which owns the whole of that transaction. This page shows what you have; it does not add or remove anything.')}

			${characters.length
				? `<div class="grid grid-2">${cards}</div>`
				: ui.empty('No characters yet', 'Create one from the game client.', 'sword')}
		</div>`;
}
