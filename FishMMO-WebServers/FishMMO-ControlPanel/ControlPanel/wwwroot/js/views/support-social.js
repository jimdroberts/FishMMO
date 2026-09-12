/*
 * Support → Guilds and social. Guilds, their ladders and logs, and live parties.
 *
 * Read-only, and the page says so out loud. Guild and party membership is game state owned
 * by the scene server holding the characters — a player joins, is promoted or is kicked in
 * the world, and the database row follows. There is no write route behind this view and
 * there is not meant to be one, so nothing here offers a control that would quietly lose an
 * operator's change on the next save.
 *
 * Every guild name, rank name, note, application message and character name below was typed
 * by a player, which is why all of it goes through `ui.esc` on the way into markup.
 *
 * The three surfaces — the guild list, one guild, and the party list — are dispatched on
 * `ctx.param` because they are one page: the row an operator clicks and what it opens.
 *
 *   #/support/social                 the guild list
 *   #/support/social/guilds/<id>     one guild
 *   #/support/social/parties         live parties
 */

/* Mirrors GuildPermissions in FishMMO.Shared, low bit first. The backend sends the raw mask
 * rather than names: the enum lives in a Unity assembly the web server cannot reference, so
 * naming the bits there would mean a second copy of it drifting in silence. The decode has
 * to live somewhere, and here it is at least visibly a mirror — an unknown bit is still
 * shown rather than dropped, so a flag added to the game shows up as "bit 14" instead of
 * vanishing. */
const PERMISSION_FLAGS = [
	'Invite', 'Kick', 'Promote', 'Edit MOTD', 'Edit notice', 'Edit ranks', 'Manage bank',
	'Manage applications', 'Disband', 'Edit recruitment', 'View officer notes',
	'Edit officer notes', 'Edit public notes', 'Transfer leadership',
];

/* Mirrors GuildLogEventType in FishMMO.Database.Data. The server sends the number and its
 * own enum name beside it, so an event kind this table has never heard of still renders as
 * whatever the server called it. */
const LOG_LABELS = [
	'Unknown', 'Created', 'Joined', 'Left', 'Kicked', 'Promoted', 'Demoted',
	'Leadership transferred', 'MOTD changed', 'Notice changed', 'Rank edited',
	'Rank created', 'Rank deleted', 'Recruitment changed', 'Application accepted',
	'Application declined', 'Note changed',
];

/** Mirrors PartyRank in FishMMO.Shared. */
const PARTY_RANKS = ['None', 'Member', 'Leader'];

const TABS = [
	['', 'Guilds'],
	['parties', 'Parties'],
];

export async function render(host, ctx) {
	const param = (ctx.param ?? '').replace(/^\/+|\/+$/g, '');

	if (param === 'parties') return renderParties(host, ctx);

	// "guilds/12" from a table row, and a bare "12" from a hand-typed link.
	const guildId = param.startsWith('guilds/') ? param.slice('guilds/'.length) : param;
	if (guildId) return renderGuild(host, ctx, guildId);

	return renderGuilds(host, ctx);
}

/* ── Shared chrome ───────────────────────────────────────────── */

function head(ui, active) {
	return `
		<div class="page-head">
			<div class="page-head-text">
				<h1>Guilds and social</h1>
				<p class="page-head-sub">
					Who is in which guild, who leads it, and who is grouped with whom right now.
				</p>
			</div>
		</div>
		<div class="tabs">
			${/* Links rather than the buttons the other tabbed pages use, because these two tabs
			    are two routes: an operator pasting a link to the party list into a ticket should
			    get the party list. The inline rule suppresses the underline every anchor gets on
			    hover, which a tab should not have. */''}
			${TABS.map(([key, label]) => `
				<a class="tab${active === key ? ' is-active' : ''}" style="text-decoration:none"
				   href="#/support/social${key ? '/' + key : ''}">${ui.esc(label)}</a>`).join('')}
		</div>`;
}

/* Stated on every one of the three surfaces rather than once on the list, because an
 * operator following a link from a ticket lands on the guild or the party page directly and
 * would otherwise never see it. */
function readOnlyBanner(ui, what) {
	return ui.banner(
		'info',
		'Read-only',
		`${what} is changed in the game, not here. Membership, ranks and leadership live in the ` +
		'scene server holding the characters; this page reads the rows that server writes.',
	);
}

function characterLink(ui, id, name, deleted) {
	const label = name ? ui.esc(name) : `<span class="faint">character ${ui.esc(id)}</span>`;
	if (!id) return '<span class="faint">—</span>';
	return `<a href="#/support/characters/${encodeURIComponent(id)}">${label}</a>${
		deleted ? ` ${ui.badge('deleted', 'danger')}` : ''}`;
}

function memberState(ui, m) {
	if (m.deleted) return ui.badge('deleted', 'danger');
	return m.online ? ui.statusBadge('in world', 'ok', true) : ui.badge('offline');
}

/* ── The guild list ──────────────────────────────────────────── */

async function renderGuilds(host, ctx) {
	const { api, ui } = ctx;
	const filters = { query: '', page: 1, pageSize: 25 };

	host.innerHTML = `
		${head(ui, '')}
		<div class="stack-lg">
			${readOnlyBanner(ui, 'Guild membership')}
			<section class="card">
				<div class="toolbar">
					<input class="search" id="q" type="search" placeholder="Guild name starts with…" autocomplete="off" />
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
		const page = await api.searchGuilds(filters);
		host.querySelector('#count').textContent = `${ui.num(page.totalCount)} guilds`;

		results.innerHTML = ui.table({
			columns: [
				{
					label: 'Guild',
					cell: (g) => `
						<div class="row">
							<span class="avatar avatar-sm ${ui.avatarClass(g.name)}">${ui.esc(ui.initials(g.name))}</span>
							<span>
								<span class="cell-primary">${ui.esc(g.name)}</span>
								<span class="cell-sub" style="display:block">${g.tags
									? ui.esc(g.tags)
									: '<span class="faint">no tags</span>'}</span>
							</span>
						</div>`,
				},
				{ label: 'Members', align: 'right', cell: (g) => ui.num(g.memberCount) },
				{ label: 'Ranks', align: 'right', cell: (g) => ui.num(g.rankCount) },
				{
					label: 'Leader',
					/* There is no leader column in the database. A member leads by holding the
					 * top rung of the guild's ladder, and more than one can — so when the answer
					 * is not a single person, the page says so rather than picking one. */
					cell: (g) => {
						if (!g.leaderName) return '<span class="faint">none</span>';
						return `${characterLink(ui, g.leaderCharacterId, g.leaderName, false)}${
							g.leaderIsAmbiguous
								? `<div class="cell-sub">${ui.esc('and others at this rank')}</div>`
								: ''}`;
					},
				},
				{ label: 'Recruiting', cell: (g) => (g.isRecruiting ? ui.badge('recruiting', 'ok') : ui.badge('closed')) },
				{ label: 'Founded', cell: (g) => `<span class="nowrap" title="${ui.esc(ui.dateTime(g.createdUtc))}">${ui.ago(g.createdUtc)}</span>` },
				{
					label: '',
					align: 'right',
					cell: (g) => `<a class="btn btn-sm" href="#/support/social/guilds/${encodeURIComponent(g.id)}">Inspect</a>`,
				},
			],
			rows: page.items,
			emptyText: 'No guild starts with that.',
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

	await load();
}

/* ── One guild ───────────────────────────────────────────────── */

async function renderGuild(host, ctx, id) {
	const { api, ui } = ctx;
	const g = await api.getGuild(id);
	const leaders = (g.members ?? []).filter((m) => m.isLeader);

	host.innerHTML = `
		<div class="page-head">
			<div class="page-head-text">
				<div class="row" style="margin-bottom:var(--sp-2)">
					<a class="small muted" href="#/support/social">← All guilds</a>
				</div>
				<div class="row">
					<span class="avatar avatar-lg ${ui.avatarClass(g.name)}">${ui.esc(ui.initials(g.name))}</span>
					<div>
						<h1>${ui.esc(g.name)}</h1>
						<div class="page-head-sub">
							Guild ${ui.esc(g.id)} · founded ${ui.esc(ui.dateTime(g.createdUtc))}
						</div>
					</div>
				</div>
			</div>
			<div class="page-head-actions">
				${g.isRecruiting ? ui.badge('recruiting', 'ok') : ui.badge('not recruiting')}
			</div>
		</div>

		<div class="stack-lg">
			${readOnlyBanner(ui, 'Guild membership')}

			<div class="grid grid-4">
				${ui.stat({ label: 'Members', value: ui.num(g.memberCount) })}
				${ui.stat({ label: 'Ranks', value: ui.num(g.ranks.length) })}
				${ui.stat({
					label: leaders.length > 1 ? 'Leaders' : 'Leader',
					value: leaders.length ? leaders.map((m) => m.name).join(', ') : 'none',
					note: leaders.length > 1 ? 'More than one member holds the top rank' : '',
					noteTone: leaders.length > 1 ? 'warn' : '',
				})}
				${ui.stat({ label: 'Pending applications', value: ui.num(g.applications.length) })}
			</div>

			${ui.card({
				title: 'What the guild says',
				body: `
					<dl class="dl">
						<dt>Message of the day</dt><dd>${g.messageOfTheDay ? ui.esc(g.messageOfTheDay) : '<span class="faint">empty</span>'}</dd>
						<dt>Notice</dt><dd>${g.notice ? ui.esc(g.notice) : '<span class="faint">empty</span>'}</dd>
						<dt>Recruitment blurb</dt><dd>${g.blurb ? ui.esc(g.blurb) : '<span class="faint">empty</span>'}</dd>
						<dt>Tags</dt><dd>${g.tags ? ui.esc(g.tags) : '<span class="faint">none</span>'}</dd>
						<dt>Row version</dt><dd class="tnum">${ui.num(g.version)}</dd>
					</dl>`,
				foot: 'The notice and the message of the day are written for members. The blurb is what everybody else sees in the recruitment directory.',
			})}

			${ui.card({
				title: 'Roster',
				sub: 'Most senior first.',
				flush: true,
				body: ui.table({
					columns: [
						{
							label: 'Member',
							cell: (m) => `
								<div class="row">
									<span class="avatar avatar-sm ${ui.avatarClass(m.name)}">${ui.esc(ui.initials(m.name))}</span>
									<span class="cell-primary">${characterLink(ui, m.characterId, m.name, m.deleted)}</span>
								</div>`,
						},
						{
							label: 'Rank',
							cell: (m) => `
								<div>${m.rankName
									? ui.esc(m.rankName)
									: `<span class="faint">rank ${ui.esc(m.rank)} — no such rank</span>`}</div>
								${m.isLeader ? `<div class="cell-sub">${ui.badge('leader', 'accent')}</div>` : ''}`,
						},
						{ label: 'State', cell: (m) => memberState(ui, m) },
						{ label: 'Location', cell: (m) => (m.location ? ui.esc(m.location) : '<span class="faint">—</span>') },
						{ label: 'Public note', cell: (m) => (m.publicNote
							? `<span style="max-width:28ch;display:inline-block;overflow-wrap:anywhere">${ui.esc(m.publicNote)}</span>`
							: '<span class="faint">—</span>') },
						{ label: 'Joined', cell: (m) => `<span class="nowrap" title="${ui.esc(ui.dateTime(m.joinedUtc))}">${ui.ago(m.joinedUtc)}</span>` },
					],
					rows: g.members,
					emptyText: 'Nobody is in this guild. The row outlives its last member until somebody disbands it.',
				}),
				foot: 'Officer notes are not shown. They are filtered by rank in the game so that members who may not read them never receive them, and a support agent is not a member of the guild at all.',
			})}

			${ui.card({
				title: 'Rank ladder',
				sub: 'A membership row stores the rank order, not the rank row, so the orders are contiguous by design.',
				flush: true,
				body: ui.table({
					columns: [
						{ label: 'Order', align: 'right', cell: (r) => ui.esc(r.order) },
						{ label: 'Rank', cell: (r) => `<span class="cell-primary">${ui.esc(r.name)}</span>` },
						{ label: 'Members', align: 'right', cell: (r) => ui.num(r.memberCount) },
						{ label: 'Permissions', cell: (r) => permissionBadges(ui, r.permissions) },
					],
					rows: g.ranks,
					emptyText: 'This guild has no rank rows, so nobody in it holds the leader’s seat.',
				}),
			})}

			${g.applications.length ? ui.card({
				title: 'Pending applications',
				flush: true,
				body: ui.table({
					columns: [
						{ label: 'Applicant', cell: (a) => characterLink(ui, a.characterId, a.name, false) },
						{ label: 'Message', cell: (a) => (a.message
							? `<span style="max-width:52ch;display:inline-block;overflow-wrap:anywhere">${ui.esc(a.message)}</span>`
							: '<span class="faint">—</span>') },
						{ label: 'Applied', cell: (a) => `<span class="nowrap" title="${ui.esc(ui.dateTime(a.appliedUtc))}">${ui.ago(a.appliedUtc)}</span>` },
					],
					rows: g.applications,
				}),
			}) : ''}

			<section class="card">
				<header class="card-head">
					<div class="card-head-text">
						<div class="card-title">Activity log</div>
						<div class="card-sub">Append-only, newest first. Entries name characters by ID, so one naming a character since deleted still names them.</div>
					</div>
				</header>
				<div class="card-body flush" id="log">${ui.loading()}</div>
				<footer class="card-foot" id="log-pager"></footer>
			</section>
		</div>`;

	await wireLog(host, ctx, g.id);
}

function permissionBadges(ui, mask) {
	const value = Number(mask) || 0;
	if (!value) return '<span class="faint">none</span>';

	/* Division rather than `&` and `<<`: JavaScript's bitwise operators truncate to 32 bits,
	 * and the column is a bigint precisely so the flags have room to grow past that. A flag at
	 * bit 31 or beyond would decode as the wrong one, or as nothing. */
	const out = [];
	for (let bit = 0; bit < 53; ++bit) {
		if (Math.floor(value / 2 ** bit) % 2 !== 1) continue;
		out.push(ui.badge(PERMISSION_FLAGS[bit] ?? `bit ${bit}`));
	}
	return out.join(' ');
}

async function wireLog(host, ctx, guildId) {
	const { api, ui } = ctx;
	const state = { page: 1, pageSize: 25 };
	const body = host.querySelector('#log');
	const pagerHost = host.querySelector('#log-pager');

	async function load() {
		body.innerHTML = ui.loading();
		const page = await api.getGuildLog(guildId, state);

		body.innerHTML = ui.table({
			columns: [
				{ label: 'When', cell: (l) => `<span class="nowrap" title="${ui.esc(ui.dateTime(l.occurredUtc))}">${ui.ago(l.occurredUtc)}</span>` },
				{ label: 'Event', cell: (l) => `<span class="cell-primary">${ui.esc(LOG_LABELS[l.eventType] ?? l.eventName ?? l.eventType)}</span>` },
				{ label: 'Actor', cell: (l) => (l.actorCharacterId
					? characterLink(ui, l.actorCharacterId, l.actorName, false)
					: '<span class="faint">the guild itself</span>') },
				{ label: 'Target', cell: (l) => (l.targetCharacterId
					? characterLink(ui, l.targetCharacterId, l.targetName, false)
					: '<span class="faint">—</span>') },
				{ label: 'Detail', cell: (l) => (l.detail ? ui.esc(l.detail) : '<span class="faint">—</span>') },
			],
			rows: page.items,
			emptyText: 'Nothing recorded. A guild older than the log itself legitimately has no entries.',
		});

		pagerHost.innerHTML = ui.pager(page.page, page.pageSize, page.totalCount, 'log-page');
		pagerHost.hidden = !pagerHost.innerHTML.trim();
		pagerHost.querySelectorAll('[data-action="log-page"]').forEach((b) =>
			b.addEventListener('click', () => {
				state.page = Number(b.dataset.page);
				load();
			}),
		);
	}

	await load();
}

/* ── Parties ─────────────────────────────────────────────────── */

async function renderParties(host, ctx) {
	const { api, ui } = ctx;
	const filters = { worldServerId: '', page: 1, pageSize: 25 };

	host.innerHTML = `
		${head(ui, 'parties')}
		<div class="stack-lg">
			${readOnlyBanner(ui, 'Party membership')}
			${ui.banner('info', 'A party row is as transient as the group it describes',
				'It is created when people group and deleted when the last member leaves, so this is who is grouped right now and not a history of who was.')}
			<section class="card">
				<div class="toolbar">
					<input class="search" id="world" type="search" inputmode="numeric" placeholder="World server ID…" autocomplete="off" />
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
		const query = { page: filters.page, pageSize: filters.pageSize };
		if (filters.worldServerId) query.worldServerId = filters.worldServerId;

		const page = await api.getParties(query);
		host.querySelector('#count').textContent = `${ui.num(page.totalCount)} parties`;

		results.innerHTML = ui.table({
			columns: [
				{ label: 'Party', cell: (p) => `<span class="cell-primary tnum">${ui.esc(p.id)}</span>` },
				{ label: 'World server', cell: (p) => `<span class="tnum">${ui.esc(p.worldServerId)}</span>` },
				{ label: 'Members', align: 'right', cell: (p) => ui.num(p.memberCount) },
				{
					label: 'Who',
					cell: (p) => (p.members.length
						? p.members.map((m) => characterLink(ui, m.characterId, m.name, m.deleted)).join(', ')
						: '<span class="faint">nobody — the row outlived its last member</span>'),
				},
				{ label: 'Formed', cell: (p) => `<span class="nowrap" title="${ui.esc(ui.dateTime(p.createdUtc))}">${ui.ago(p.createdUtc)}</span>` },
				{ label: '', align: 'right', cell: (p) => (p.members.length
					? '<button class="btn btn-sm" data-act="expand">Roster</button>'
					: '') },
			],
			rows: page.items,
			rowAttrs: (p, i) => `class="${p.members.length ? 'is-clickable' : ''}" data-index="${i}" aria-expanded="false"`,
			emptyText: 'Nobody is grouped right now.',
		});

		wireRosters(results, ui, page.items);

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
	host.querySelector('#world').addEventListener('input', (e) => {
		clearTimeout(debounce);
		debounce = setTimeout(() => {
			// Only digits reach the server: the filter is a bigint column, and a typo should
			// narrow to nothing rather than be sent as a string the API will refuse.
			filters.worldServerId = e.target.value.replace(/\D+/g, '');
			filters.page = 1;
			load();
		}, 220);
	});

	await load();
}

/**
 * Gives every party row a hidden sibling holding its roster, and toggles it on click.
 *
 * The roster spans the whole table, and `ui.table` renders one `<tr>` per row, so the
 * detail row is built here and inserted afterwards — the same shape the audit log uses.
 */
function wireRosters(results, ui, items) {
	const body = results.querySelector('tbody');
	if (!body) return;

	for (const tr of [...body.querySelectorAll('tr[data-index]')]) {
		const party = items[Number(tr.dataset.index)];
		if (!party || !party.members.length) continue;

		const button = tr.querySelector('[data-act="expand"]');
		const detail = document.createElement('tr');
		detail.hidden = true;
		detail.innerHTML = `<td colspan="${tr.children.length}" style="background:var(--surface-sunken)">${rosterPanel(ui, party)}</td>`;
		tr.after(detail);

		tr.addEventListener('click', (e) => {
			// A link in the row is a destination of its own, not a toggle.
			if (e.target.closest('a')) return;
			detail.hidden = !detail.hidden;
			tr.setAttribute('aria-expanded', String(!detail.hidden));
			if (button) button.textContent = detail.hidden ? 'Roster' : 'Hide';
		});
	}
}

function rosterPanel(ui, party) {
	return ui.table({
		columns: [
			{ label: 'Member', cell: (m) => characterLink(ui, m.characterId, m.name, m.deleted) },
			{ label: 'Rank', cell: (m) => ui.esc(PARTY_RANKS[m.rank] ?? `rank ${m.rank}`) },
			{ label: 'Scene', cell: (m) => (m.sceneName ? ui.esc(m.sceneName) : '<span class="faint">—</span>') },
			{ label: 'State', cell: (m) => memberState(ui, m) },
			{
				label: 'Health',
				/* Written by the owning scene server for the other members' party frames, so it
				 * is as old as that character's last update — a number for a frame, not a live
				 * vital sign, and it is labelled as such. */
				cell: (m) => ui.meter(Math.round((Number(m.healthPct) || 0) * 100), 100),
			},
			{ label: 'Joined', cell: (m) => `<span class="nowrap" title="${ui.esc(ui.dateTime(m.joinedUtc))}">${ui.ago(m.joinedUtc)}</span>` },
		],
		rows: party.members,
	});
}
