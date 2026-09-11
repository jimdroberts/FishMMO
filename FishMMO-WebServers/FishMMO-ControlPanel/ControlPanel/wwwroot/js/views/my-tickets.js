/*
 * My tickets — the player's own side of support.
 *
 * This view is deliberately small. It reads two endpoints scoped to the signed-in account
 * and writes one reply; there is no assignment, no status, no priority and no note, because
 * a player has none of those. Nothing here is gated on an access level: the server decides
 * what "mine" means from the session, which is the only place that decision is safe.
 *
 * Internal staff notes are not on this page because the server does not send them. The
 * message loop below skips one if it ever arrives, but that line is a belt to the server's
 * braces — it is not what keeps staff discussion away from the player, and nothing here may
 * be built as though it were.
 *
 * A player writes their own subjects and messages, so every value goes through `ui.esc`.
 */

const CATEGORY_LABELS = ['Bug', 'Player report', 'Help', 'Appeal', 'Other'];
const STATUS_LABELS = ['Open', 'In progress', 'Awaiting player', 'Resolved', 'Closed'];
const STATUS_TONES = ['info', 'accent', 'warn', 'ok', ''];

function labelFor(labels, value, fallback) {
	return labels[value] ?? fallback ?? String(value ?? '');
}

/* Timestamps are UTC. One serialized without a designator would be read as local time and
 * shuffle the thread; `ui` applies the same rule but does not export the parser. */
function utcMs(iso) {
	if (typeof iso !== 'string') return Date.now();
	return Date.parse(/[Zz]$|[+-]\d{2}:?\d{2}$/.test(iso) ? iso : iso + 'Z');
}

export async function render(host, ctx) {
	if (ctx.param) return renderDetail(host, ctx, ctx.param);
	return renderList(host, ctx);
}

/* ── List ────────────────────────────────────────────────────── */

async function renderList(host, ctx) {
	const { api, ui } = ctx;
	const filters = { page: 1, pageSize: 20 };

	host.innerHTML = `
		<div class="page-head">
			<div class="page-head-text">
				<h1>My tickets</h1>
				<p class="page-head-sub">
					Every support ticket you have raised. The team answers them here, so open a ticket to
					read the reply and to write back.
				</p>
			</div>
			<div class="page-head-actions">
				<button class="btn btn-primary" data-act="new">${ui.icon('mail')} Raise a ticket</button>
			</div>
		</div>

		<section class="card">
			<div class="card-body flush" id="results">${ui.loading()}</div>
			<footer class="card-foot" id="pager"></footer>
		</section>`;

	const results = host.querySelector('#results');
	const pagerHost = host.querySelector('#pager');

	/* Most tickets are raised in the game, where the player already is when something goes
	 * wrong. This exists for the times they cannot get in — the client will not start, or they
	 * are on a phone — and for explaining something at length, which a chat line is a poor
	 * place for. */
	host.querySelector('[data-act="new"]').addEventListener('click', async () => {
		const v = await ui.modal({
			title: 'Raise a ticket',
			sub: 'The team answers here, and you will see the reply on this page.',
			confirmLabel: 'Send it',
			body: `
				<div class="field">
					<label for="tk-category">What is this about?</label>
					<select id="tk-category" name="category">
						<option value="2">I need help</option>
						<option value="0">Something is broken</option>
						<option value="1">Report another player</option>
						<option value="4">Something else</option>
					</select>
				</div>
				<div class="field" id="tk-target-field" hidden>
					<label for="tk-target">Which character?</label>
					<input id="tk-target" name="targetCharacterName" autocomplete="off" />
				</div>
				<div class="field">
					<label for="tk-subject">In one line</label>
					<input id="tk-subject" name="subject" maxlength="160" required />
				</div>
				<div class="field">
					<label for="tk-body">What happened?</label>
					<textarea id="tk-body" name="body" rows="6" maxlength="4000" required></textarea>
					<div class="field-hint">The more you say now, the fewer questions come back.</div>
				</div>`,
			onReady: (form) => {
				// The character field only means anything for a report, and an always-visible
				// box asking "which character?" on a bug report just invites a wrong answer.
				const category = form.querySelector('#tk-category');
				const target = form.querySelector('#tk-target-field');
				const sync = () => { target.hidden = category.value !== '1'; };
				category.addEventListener('change', sync);
				sync();
			},
			onSubmit: (values) => {
				if (!String(values.subject).trim() || !String(values.body).trim()) return false;
				if (values.category === '1' && !String(values.targetCharacterName || '').trim()) return false;
				return {
					category: Number(values.category),
					subject: values.subject,
					body: values.body,
					targetCharacterName: values.category === '1' ? values.targetCharacterName : null,
				};
			},
		});
		if (!v) return;
		if (await ctx.attempt(() => api.fileTicket(v), 'Ticket raised')) load();
	});

	async function load() {
		results.innerHTML = ui.loading();
		const page = await api.getMyTickets(filters);

		results.innerHTML = page.items.length
			? ui.table({
				columns: [
					{
						label: 'Subject',
						cell: (t) => `
							<div class="cell-primary" style="max-width:56ch;overflow-wrap:anywhere">${ui.esc(t.subject)}</div>
							<div class="cell-sub">Raised ${ui.ago(t.createdUtc)}</div>`,
					},
					{ label: 'Category', cell: (t) => ui.badge(labelFor(CATEGORY_LABELS, t.category, t.categoryName)) },
					{
						label: 'Status',
						cell: (t) => ui.badge(labelFor(STATUS_LABELS, t.status, t.statusName), STATUS_TONES[t.status] ?? ''),
					},
					{
						label: 'Last activity',
						cell: (t) => `<span class="nowrap" title="${ui.esc(ui.dateTime(t.lastActivityUtc))}">${ui.ago(t.lastActivityUtc)}</span>`,
					},
					{ label: 'Messages', align: 'right', cell: (t) => `<span class="tnum">${ui.num(t.messageCount)}</span>` },
					{
						label: '',
						align: 'right',
						cell: (t) => `<a class="btn btn-sm" href="#/my-tickets/${encodeURIComponent(t.id)}">Open</a>`,
					},
				],
				rows: page.items,
				rowAttrs: (t) => `class="is-clickable" data-id="${ui.esc(t.id)}"`,
			})
			: ui.empty('No tickets', 'You have not raised a support ticket. Raise one here, or use /report, /bug or /helpme in the game.', 'inbox');

		results.querySelectorAll('tr[data-id]').forEach((tr) =>
			tr.addEventListener('click', (e) => {
				if (e.target.closest('a')) return;
				ctx.go(`my-tickets/${encodeURIComponent(tr.dataset.id)}`);
			}),
		);

		pagerHost.innerHTML = ui.pager(page.page, page.pageSize, page.totalCount, 'page');
		pagerHost.hidden = !pagerHost.innerHTML.trim();
		pagerHost.querySelectorAll('[data-action="page"]').forEach((b) =>
			b.addEventListener('click', () => {
				filters.page = Number(b.dataset.page);
				load();
			}),
		);
	}

	await load();
}

/* ── Detail ──────────────────────────────────────────────────── */

async function renderDetail(host, ctx, param) {
	const { api, ui } = ctx;
	// The id is whatever is in the address bar: a row number, or nothing worth sending.
	const id = Number(param);
	if (!Number.isInteger(id) || id <= 0) {
		host.innerHTML = ui.empty('That is not a ticket', `Nothing is numbered "${ui.esc(param)}".`, 'search');
		return;
	}

	const ticket = await api.getMyTicket(id);
	const closed = ticket.status === 4;

	host.innerHTML = `
		<div class="page-head">
			<div class="page-head-text">
				<div class="row" style="margin-bottom:var(--sp-2)">
					<a class="small muted" href="#/my-tickets">← My tickets</a>
				</div>
				<h1 style="overflow-wrap:anywhere">${ui.esc(ticket.subject)}</h1>
				<div class="page-head-sub">
					Ticket ${ui.num(ticket.id)} · raised ${ui.esc(ui.dateTime(ticket.createdUtc))}
				</div>
			</div>
			<div class="page-head-actions">
				${ui.badge(labelFor(CATEGORY_LABELS, ticket.category, ticket.categoryName))}
				${ui.badge(labelFor(STATUS_LABELS, ticket.status, ticket.statusName), STATUS_TONES[ticket.status] ?? '')}
			</div>
		</div>

		<div class="stack-lg">
			${closed
				? ui.banner('info', 'This ticket is closed',
					'The team has finished with it. You can still write here, and somebody will pick it up if you do.')
				: ''}

			${ui.card({
				title: 'What you reported',
				sub: ui.dateTime(ticket.createdUtc),
				body: `<div class="msg-body">${ui.esc(ticket.body)}</div>`,
			})}

			${ui.card({
				title: 'Conversation',
				sub: 'Oldest first. Replies from the team appear here.',
				body: conversation(ui, ticket.messages ?? []),
			})}

			${ui.card({
				title: 'Write back',
				body: `
					<div class="field">
						<label for="reply">Your message</label>
						<textarea id="reply" rows="6" placeholder="Anything else the team should know?"></textarea>
					</div>
					<div class="row-between">
						<span class="small muted">The team reads this and answers on this ticket.</span>
						<button class="btn btn-primary" type="button" id="send">Send</button>
					</div>`,
			})}
		</div>`;

	const box = host.querySelector('#reply');
	const send = host.querySelector('#send');

	send.addEventListener('click', async () => {
		const body = box.value.trim();
		if (!body) {
			box.focus();
			return;
		}
		send.disabled = true;
		const sent = await ctx.attempt(() => api.replyToMyTicket(ticket.id, body), 'Message sent', 'The team will reply on this ticket.');
		send.disabled = false;
		// Only clear what was actually delivered: a failed send must not eat what was typed.
		if (sent !== null) {
			box.value = '';
			ctx.refresh();
		}
	});
}

function conversation(ui, messages) {
	if (!messages.length) {
		return ui.empty('No replies yet', 'Nobody has answered this ticket yet. When they do, their reply appears here.', 'chat');
	}
	const ordered = [...messages].sort((a, b) => utcMs(a.createdUtc) - utcMs(b.createdUtc) || (a.id ?? 0) - (b.id ?? 0));
	return `<ol class="thread">${ordered.map((m) => messageBlock(ui, m)).join('')}</ol>`;
}

function messageBlock(ui, m) {
	/* Skipped, never relied upon: the server is what excludes internal notes from this
	 * endpoint. If one turns up here at all, that is a server bug, and swallowing it
	 * quietly is better for the player than rendering staff discussion about them. */
	if (m.internal) return '';

	const staff = Boolean(m.authorIsStaff);
	return `
		<li class="msg ${staff ? 'is-staff' : 'is-player'}">
			<div class="msg-label">${staff ? 'From the support team' : 'From you'}</div>
			<div class="msg-head">
				<span class="msg-author">${staff ? ui.esc(m.authorAccount) : 'You'}</span>
				${staff ? ui.badge('staff', 'accent') : ''}
				<span class="grow"></span>
				<span class="msg-time" title="${ui.esc(ui.dateTime(m.createdUtc))}">${ui.ago(m.createdUtc)}</span>
			</div>
			<div class="msg-body">${ui.esc(m.body)}</div>
		</li>`;
}
