/*
 * Dashboard — what the panel can actually see.
 *
 * The figures here are counted from the characters table, which is the same set of rows the
 * game servers write, so they are as current as the last persistence pass. Server
 * population, scene instances and host health are not here because the panel cannot read
 * them yet, and the landing page is the worst place in a control panel to show a number
 * that came from nowhere.
 */

/* The figures below are staff reads (the Support policy). A player's sign-in lands here too, the
 * first route their level reaches, so a player gets their own home, built from the reads scoped
 * to their account, rather than a refused request shown as a broken page (2026-11-21). */
const SUPPORT_LEVEL = 2;
const OPEN_TICKET_STATUSES = new Set([0, 1, 2]);

export async function render(host, ctx) {
	if ((ctx.session.accessLevel ?? 0) < SUPPORT_LEVEL) return renderPlayer(host, ctx);
	return renderStaff(host, ctx);
}

async function renderPlayer(host, ctx) {
	const { api, ui } = ctx;
	// Each read stands alone: a ticket service that is down must not hide the characters.
	const [chars, tickets] = await Promise.allSettled([
		api.getMyCharacters(),
		api.getMyTickets({ page: 1, pageSize: 50 }),
	]);
	const characters = chars.status === 'fulfilled' ? chars.value ?? [] : null;
	const ticketPage = tickets.status === 'fulfilled' ? tickets.value : null;
	const openTickets = ticketPage ? ticketPage.items.filter((t) => OPEN_TICKET_STATUSES.has(t.status)).length : null;
	const awaiting = ticketPage ? ticketPage.items.filter((t) => t.status === 2).length : 0;
	const dash = '—';

	const characterList = characters === null
		? ui.banner('warn', 'Your characters could not be read just now', 'Try Refresh in a moment.')
		: characters.length
			? `<ul class="stack" style="list-style:none;margin:0;padding:0">${characters.map((c) => `
				<li class="row-between">
					<div class="row" style="gap:var(--sp-3)">
						<span class="avatar ${ui.avatarClass(c.name)}">${ui.esc(ui.initials(c.name))}</span>
						<div>
							<div class="cell-primary">${ui.esc(c.name)}</div>
							<div class="cell-sub">${ui.esc(c.sceneName || 'Unknown location')}</div>
						</div>
					</div>
					${c.online ? ui.statusBadge('In world', 'ok', true) : ui.badge('Offline')}
				</li>`).join('')}</ul>`
			: ui.empty('No characters yet', 'Create one from the game client.', 'sword');

	host.innerHTML = `
		<div class="page-head">
			<div class="page-head-text">
				<h1>Welcome, ${ui.esc(ctx.session.username)}</h1>
				<p class="page-head-sub">
					Your account at a glance. Manage your sign-in and security under My account, and reach
					the team through My tickets.
				</p>
			</div>
			<div class="page-head-actions">
				<button class="btn" data-act="refresh">${ui.icon('refresh')} Refresh</button>
			</div>
		</div>

		<div class="stack-lg">
			<div class="grid grid-3">
				${ui.stat({ label: 'Characters', value: characters === null ? dash : ui.num(characters.length) })}
				${ui.stat({ label: 'In world', value: characters === null ? dash : ui.num(characters.filter((c) => c.online).length) })}
				${ui.stat({
					label: 'Open tickets',
					value: openTickets === null ? dash : ui.num(openTickets),
					note: awaiting ? `${awaiting} waiting on your reply` : '',
					noteTone: awaiting ? 'warn' : '',
				})}
			</div>

			${ui.card({
				title: 'Your characters',
				sub: 'As the shard last saved them.',
				actions: `<a class="btn btn-sm" href="#/characters">View all</a>`,
				body: characterList,
			})}

			${ui.card({
				title: 'Need a hand?',
				sub: 'Report a bug, a player, or ask for help.',
				actions: `<a class="btn btn-sm" href="#/my-tickets">My tickets</a>`,
				body: `<p class="small muted" style="margin:0">The team answers tickets here. Open one to read a reply or write back.</p>`,
			})}
		</div>`;

	host.querySelector('[data-act="refresh"]').addEventListener('click', () => render(host, ctx));
}

async function renderStaff(host, ctx) {
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

			${!data.notWired?.length ? '' : ui.card({
				title: 'Not built yet',
				sub: 'So that nothing here reads as a reassuring green when it is simply absent.',
				body: `<ul class="small muted" style="margin:0;padding-left:var(--sp-5)">
					${data.notWired.map((n) => `<li>${ui.esc(n)}</li>`).join('')}
				</ul>`,
			})}
		</div>`;

	host.querySelector('[data-act="refresh"]').addEventListener('click', () => render(host, ctx));
}
