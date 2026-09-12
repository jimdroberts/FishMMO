/*
 * Dashboard — what the panel can actually see.
 *
 * The figures here are counted from the characters table, which is the same set of rows the
 * game servers write, so they are as current as the last persistence pass. Server
 * population, scene instances and host health are not here because the panel cannot read
 * them yet, and the landing page is the worst place in a control panel to show a number
 * that came from nowhere.
 */

export async function render(host, ctx) {
	const { api, ui } = ctx;
	const data = await api.getDashboard();
	const c = data.characters;

	const rows = ui.table({
		columns: [
			{ label: 'Character', cell: (r) => `<div class="cell-primary">${ui.esc(r.name)}</div><div class="cell-sub">race ${r.raceId}</div>` },
			{ label: 'Account', cell: (r) => `<a href="#/support/accounts/${encodeURIComponent(r.account)}">${ui.esc(r.account)}</a>` },
			{ label: 'State', cell: (r) => (r.online ? ui.statusBadge('in world', 'ok', true) : ui.badge('offline')) },
			{ label: 'Last saved', cell: (r) => ui.ago(r.lastSavedUtc) },
		],
		rows: data.recentlySaved,
		emptyText: 'No characters have been saved yet.',
	});

	host.innerHTML = `
		<div class="page-head">
			<div class="page-head-text">
				<h1>Dashboard</h1>
				<p class="page-head-sub">
					Signed in as <strong>${ui.esc(ctx.session.username)}</strong> at
					${ui.esc(ctx.session.levelName)}. These figures read the same database rows the
					game servers write, so they are as current as their last save.
				</p>
			</div>
			<div class="page-head-actions">
				<button class="btn" data-act="refresh">${ui.icon('refresh')} Refresh</button>
			</div>
		</div>

		<div class="stack-lg">
			<div class="grid grid-4">
				${ui.stat({ label: 'In world', value: ui.num(c.online), note: 'Holding a live session lease' })}
				${ui.stat({ label: 'Characters', value: ui.num(c.live), note: 'Not deleted' })}
				${ui.stat({ label: 'Deleted', value: ui.num(c.deleted), note: 'Restorable' })}
				${ui.stat({ label: 'Rows in total', value: ui.num(c.total) })}
			</div>

			${ui.card({
				title: 'Recently saved',
				sub: 'The characters the shard wrote last.',
				flush: true,
				body: rows,
			})}

			${ui.card({
				title: 'Not built yet',
				sub: 'So that nothing here reads as a reassuring green when it is simply absent.',
				body: `<ul class="small muted" style="margin:0;padding-left:var(--sp-5)">
					${data.notWired.map((n) => `<li>${ui.esc(n)}</li>`).join('')}
				</ul>`,
			})}
		</div>`;

	host.querySelector('[data-act="refresh"]').addEventListener('click', () => render(host, ctx));
}
