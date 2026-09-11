/*
 * Support → Tickets. The staff queue, and one ticket's thread.
 *
 * A ticket is the one place in this panel where a player writes and an operator answers, so
 * two things govern the whole view.
 *
 * The first is that every word of a ticket — its subject, its body, every message in it —
 * was typed by a player and is aimed straight at a game master's browser. All of it goes
 * through `ui.esc` on the way into markup, and names go through `encodeURIComponent` on the
 * way into a URL.
 *
 * The second is that the thread mixes two kinds of message that must never be confused;
 * `messageBlock` below says why, and what is done about it.
 *
 * Both the queue and the detail live here, dispatched on `ctx.param`, because they are one
 * surface: the row an operator clicks and the ticket it opens.
 */

const CATEGORY_LABELS = ['Bug', 'Player report', 'Help', 'Appeal', 'Other'];
const STATUS_LABELS = ['Open', 'In progress', 'Awaiting player', 'Resolved', 'Closed'];
const STATUS_TONES = ['info', 'accent', 'warn', 'ok', ''];
const PRIORITY_LABELS = ['Low', 'Normal', 'High', 'Urgent'];
const PRIORITY_TONES = ['', '', 'warn', 'danger'];

/* The statuses that mean somebody still has work to do. They are the queue's default
 * filter: a game master opening this page wants the tickets nobody has finished, not the
 * archive of everything ever raised. */
const UNFINISHED = [0, 1, 2];

/* The server refuses Resolved and Closed without a resolution. The dialog holds the same
 * rule so the refusal happens before the operator's typing is thrown away. */
const RESOLUTION_REQUIRED = new Set([3, 4]);

/** A category or status the server sent as a number, named. Falls back to the name it sent. */
function labelFor(labels, value, fallback) {
	return labels[value] ?? fallback ?? String(value ?? '');
}

export async function render(host, ctx) {
	if (ctx.param) return renderDetail(host, ctx, ctx.param);
	return renderQueue(host, ctx);
}

/* ── Queue ───────────────────────────────────────────────────── */

async function renderQueue(host, ctx) {
	const { api, ui } = ctx;
	const me = ctx.session.username;
	const filters = {
		status: [...UNFINISHED],
		category: '',
		assignee: 'any',
		assignedTo: '',
		reporter: '',
		target: '',
		subject: '',
		page: 1,
		pageSize: 25,
	};

	host.innerHTML = `
		<div class="page-head">
			<div class="page-head-text">
				<h1>Support tickets</h1>
				<p class="page-head-sub">
					Everything players have reported, newest activity first. The queue opens on the
					tickets nobody has finished — open, in progress, and waiting on the player — because
					those are the ones that still need somebody.
				</p>
			</div>
		</div>

		<section class="card">
			<div class="toolbar">
				<input class="search" id="subject" type="search" placeholder="Subject contains…" autocomplete="off" />
				<select id="status">
					<option value="unfinished" selected>Unfinished</option>
					<option value="any">Any status</option>
					${STATUS_LABELS.map((s, i) => `<option value="${i}">${ui.esc(s)}</option>`).join('')}
				</select>
				<select id="category">
					<option value="">Any category</option>
					${CATEGORY_LABELS.map((c, i) => `<option value="${i}">${ui.esc(c)}</option>`).join('')}
				</select>
				<select id="assignee">
					<option value="any">Anyone</option>
					<option value="mine">Assigned to me</option>
					<option value="unassigned">Unassigned</option>
					<option value="other">A named operator…</option>
				</select>
				<input id="assignedTo" type="search" placeholder="Operator account…" autocomplete="off"
					style="max-width:180px" hidden />
				<input id="reporter" type="search" placeholder="Reporter…" autocomplete="off" style="max-width:150px" />
				<input id="target" type="search" placeholder="Accused…" autocomplete="off" style="max-width:150px" />
				<span class="grow"></span>
				<span class="small muted" id="count"></span>
			</div>
			<div class="card-body flush" id="results">${ui.loading()}</div>
			<footer class="card-foot" id="pager"></footer>
		</section>`;

	const results = host.querySelector('#results');
	const pagerHost = host.querySelector('#pager');
	const assignedToInput = host.querySelector('#assignedTo');

	/* `status` is sent as a repeated parameter, which is why it stays an array here rather
	 * than being joined: "Unfinished" is three statuses, not a fourth one. */
	function query() {
		const opts = {
			category: filters.category,
			reporter: filters.reporter,
			target: filters.target,
			subject: filters.subject,
			page: filters.page,
			pageSize: filters.pageSize,
		};
		if (filters.status.length) opts.status = filters.status;
		if (filters.assignee === 'mine') opts.assignedTo = me;
		else if (filters.assignee === 'unassigned') opts.unassigned = true;
		else if (filters.assignee === 'other' && filters.assignedTo) opts.assignedTo = filters.assignedTo;
		return opts;
	}

	async function load() {
		results.innerHTML = ui.loading();
		const page = await api.getTickets(query());
		host.querySelector('#count').textContent = `${ui.num(page.totalCount)} tickets`;

		results.innerHTML = page.items.length
			? ui.table({
				columns: [
					{ label: 'Priority', cell: (t) => priorityBadge(ui, t.priority) },
					{
						label: 'Subject',
						cell: (t) => `
							<div class="cell-primary" style="max-width:52ch;overflow-wrap:anywhere">${ui.esc(t.subject)}</div>
							<div class="cell-sub">${ui.esc(t.reporterCharacterName || t.reporterAccount)}${
								t.targetCharacterName || t.targetAccount
									? ` · about ${ui.esc(t.targetCharacterName || t.targetAccount)}`
									: ''}</div>`,
					},
					{ label: 'Category', cell: (t) => ui.badge(labelFor(CATEGORY_LABELS, t.category, t.categoryName)) },
					{ label: 'Status', cell: (t) => statusBadge(ui, t) },
					{
						label: 'Assignee',
						cell: (t) => t.assignedTo
							? `<a href="#/support/accounts/${encodeURIComponent(t.assignedTo)}">${ui.esc(t.assignedTo)}</a>`
							: ui.badge('unassigned', 'warn'),
					},
					{
						label: 'Last activity',
						cell: (t) => `<span class="nowrap" title="${ui.esc(ui.dateTime(t.lastActivityUtc))}">${ui.ago(t.lastActivityUtc)}</span>`,
					},
					{ label: 'Messages', align: 'right', cell: (t) => `<span class="tnum">${ui.num(t.messageCount)}</span>` },
					{
						label: '',
						align: 'right',
						cell: (t) => `<a class="btn btn-sm" href="#/support/tickets/${encodeURIComponent(t.id)}">Open</a>`,
					},
				],
				rows: page.items,
				rowAttrs: (t) => `class="is-clickable" data-id="${ui.esc(t.id)}"`,
			})
			: ui.empty('Nothing waiting', 'No ticket matches those filters. Widen the status — the queue hides resolved and closed tickets until you ask for them.', 'inbox');

		// The whole row is the link; the Open button is there for keyboard and middle-click.
		results.querySelectorAll('tr[data-id]').forEach((tr) =>
			tr.addEventListener('click', (e) => {
				if (e.target.closest('a')) return;
				ctx.go(`support/tickets/${encodeURIComponent(tr.dataset.id)}`);
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

	let debounce;
	const debounced = (key) => (e) => {
		clearTimeout(debounce);
		debounce = setTimeout(() => {
			filters[key] = e.target.value.trim();
			filters.page = 1;
			load();
		}, 220);
	};

	host.querySelector('#subject').addEventListener('input', debounced('subject'));
	host.querySelector('#reporter').addEventListener('input', debounced('reporter'));
	host.querySelector('#target').addEventListener('input', debounced('target'));
	assignedToInput.addEventListener('input', debounced('assignedTo'));

	host.querySelector('#status').addEventListener('change', (e) => {
		const v = e.target.value;
		filters.status = v === 'unfinished' ? [...UNFINISHED] : v === 'any' ? [] : [Number(v)];
		filters.page = 1;
		load();
	});
	host.querySelector('#category').addEventListener('change', (e) => {
		filters.category = e.target.value;
		filters.page = 1;
		load();
	});
	host.querySelector('#assignee').addEventListener('change', (e) => {
		filters.assignee = e.target.value;
		assignedToInput.hidden = filters.assignee !== 'other';
		if (assignedToInput.hidden) {
			assignedToInput.value = '';
			filters.assignedTo = '';
		} else {
			assignedToInput.focus();
		}
		filters.page = 1;
		load();
	});

	await load();
}

/* ── Detail ──────────────────────────────────────────────────── */

async function renderDetail(host, ctx, param) {
	const { api, ui } = ctx;
	/* The id comes out of the address bar, so it is whatever somebody typed. It is a row
	 * number or it is nothing; refusing it here keeps a hand-edited hash from being pasted
	 * into a request path. */
	const id = Number(param);
	if (!Number.isInteger(id) || id <= 0) {
		host.innerHTML = ui.empty('That is not a ticket', `Nothing is numbered "${ui.esc(param)}".`, 'search');
		return;
	}

	const ticket = await api.getTicket(id);
	const me = ctx.session.username;
	const mine = ticket.assignedTo && String(ticket.assignedTo).toLowerCase() === String(me).toLowerCase();
	const isReport = ticket.category === 1;
	const hasTarget = Boolean(ticket.targetAccount || ticket.targetCharacterName);
	const closed = ticket.status === 4;

	host.innerHTML = `
		<div class="page-head">
			<div class="page-head-text">
				<div class="row" style="margin-bottom:var(--sp-2)">
					<a class="small muted" href="#/support/tickets">← All tickets</a>
				</div>
				<h1 style="overflow-wrap:anywhere">${ui.esc(ticket.subject)}</h1>
				<div class="page-head-sub">
					Ticket ${ui.num(ticket.id)} · raised ${ui.esc(ui.dateTime(ticket.createdUtc))} by
					<a href="#/support/accounts/${encodeURIComponent(ticket.reporterAccount)}">${ui.esc(ticket.reporterAccount)}</a>
				</div>
			</div>
			<div class="page-head-actions">
				${priorityBadge(ui, ticket.priority)}
				${statusBadge(ui, ticket)}
				${ticket.assignedTo ? ui.badge(`assigned to ${ticket.assignedTo}`, mine ? 'accent' : '') : ui.badge('unassigned', 'warn')}
			</div>
		</div>

		<div class="stack-lg">
			${closed
				? ui.banner('info', 'This ticket is closed',
					'Replies and notes still land on it, and the thread stays exactly as it was. Reopening is a status change like any other.')
				: ''}

			<div class="split">
				<div class="stack">
					${ui.card({
						title: 'The report',
						sub: `${labelFor(CATEGORY_LABELS, ticket.category, ticket.categoryName)} · ${ui.dateTime(ticket.createdUtc)}`,
						body: `<div class="msg-body">${ui.esc(ticket.body)}</div>`,
						foot: ticket.resolution
							? `Resolution: ${ui.esc(ticket.resolution)}`
							: '',
					})}

					${ui.card({
						title: `Conversation (${ui.num(ticket.messages?.length ?? 0)})`,
						sub: 'Oldest first. Internal notes are marked, and the player cannot see them.',
						body: conversation(ui, ticket.messages ?? [], { showInternal: true }),
					})}

					${actionsCard(ui, { ticket, mine, closed })}
				</div>

				<div class="stack">
					${ui.card({
						title: 'Reporter',
						body: `
							<dl class="dl">
								<dt>Account</dt><dd><a href="#/support/accounts/${encodeURIComponent(ticket.reporterAccount)}">${ui.esc(ticket.reporterAccount)}</a></dd>
								<dt>Character</dt><dd>${characterLink(ui, ticket.reporterCharacterName, ticket.reporterCharacterId)}</dd>
								<dt>Scene</dt><dd>${ticket.sceneName ? ui.esc(ticket.sceneName) : '<span class="faint">—</span>'}</dd>
								<dt>Raised</dt><dd>${ui.dateTime(ticket.createdUtc)}</dd>
							</dl>`,
					})}

					${hasTarget
						? ui.card({
							title: isReport ? 'The accused' : 'Subject of the ticket',
							sub: isReport ? 'Named by the reporter. Nothing here is a finding.' : '',
							body: `
								<dl class="dl">
									<dt>Account</dt><dd>${ticket.targetAccount
										? `<a href="#/support/accounts/${encodeURIComponent(ticket.targetAccount)}">${ui.esc(ticket.targetAccount)}</a>`
										: '<span class="faint">—</span>'}</dd>
									<dt>Character</dt><dd>${characterLink(ui, ticket.targetCharacterName, ticket.targetCharacterId)}</dd>
								</dl>
								${isReport ? `
									<div style="margin-top:var(--sp-3)">
										<a class="btn btn-sm btn-block" href="${chatHref(ticket)}">Chat log around the incident</a>
										<div class="field-hint">
											The chat log narrows by name and by date. This link carries
											${ui.esc(ticket.targetCharacterName || ticket.targetAccount || 'the accused')} and the day either
											side of ${ui.esc(ui.dateTime(ticket.createdUtc))}; if it opens unfiltered, those are what to
											type into its toolbar.
										</div>
									</div>` : ''}`,
						})
						: ''}

					${ui.card({
						title: 'State',
						body: `
							<dl class="dl">
								<dt>Status</dt><dd>${statusBadge(ui, ticket)}</dd>
								<dt>Priority</dt><dd>${priorityBadge(ui, ticket.priority)}</dd>
								<dt>Category</dt><dd>${ui.esc(labelFor(CATEGORY_LABELS, ticket.category, ticket.categoryName))}</dd>
								<dt>Assignee</dt><dd>${ticket.assignedTo
									? `<a href="#/support/accounts/${encodeURIComponent(ticket.assignedTo)}">${ui.esc(ticket.assignedTo)}</a>`
									: '<span class="faint">nobody</span>'}</dd>
								<dt>Last activity</dt><dd>${ui.dateTime(ticket.lastActivityUtc)}</dd>
								<dt>Closed</dt><dd>${ticket.closedUtc ? ui.dateTime(ticket.closedUtc) : '<span class="faint">—</span>'}</dd>
								<dt>Closed by</dt><dd>${ticket.closedBy ? ui.esc(ticket.closedBy) : '<span class="faint">—</span>'}</dd>
							</dl>`,
					})}
				</div>
			</div>
		</div>`;

	wireActions(host, ctx, ticket, me);
}

function characterLink(ui, name, id) {
	if (!name && !id) return '<span class="faint">—</span>';
	if (!id) return ui.esc(name);
	return `<a href="#/support/characters/${encodeURIComponent(id)}">${ui.esc(name || id)}</a>`;
}

/**
 * Builds the deep link into the chat log.
 *
 * The chat log narrows by a name and by a date range, so the link carries the accused and
 * the day either side of the report — the window an operator would otherwise type by hand,
 * in the keys and the date format that view's own filters use. The router hands a view
 * whatever follows "?" in the hash; a chat log that does not read it yet still opens on
 * this link, which is why the hint beside it says what to search for.
 */
function chatHref(ticket) {
	const at = utcMs(ticket.createdUtc);
	const day = 86400000;
	const who = ticket.targetCharacterName
		? `characterName=${encodeURIComponent(ticket.targetCharacterName)}`
		: `accountName=${encodeURIComponent(ticket.targetAccount ?? '')}`;
	return `#/support/chat?${who}&from=${isoDay(at - day)}&to=${isoDay(at + day)}`;
}

/** A date in the form the chat log's date inputs hold, which is a day in UTC. */
function isoDay(ms) {
	return new Date(ms).toISOString().slice(0, 10);
}

/* Timestamps from the backend are UTC; one serialized without a designator would otherwise
 * be read as local time and put the incident in the wrong hour. `ui` applies the same rule
 * to everything it formats, but it does not export the parser. */
function utcMs(iso) {
	if (typeof iso !== 'string') return Date.now();
	return Date.parse(/[Zz]$|[+-]\d{2}:?\d{2}$/.test(iso) ? iso : iso + 'Z');
}

function statusBadge(ui, ticket) {
	return ui.badge(labelFor(STATUS_LABELS, ticket.status, ticket.statusName), STATUS_TONES[ticket.status] ?? '');
}

function priorityBadge(ui, priority) {
	const p = Number(priority);
	return ui.badge(labelFor(PRIORITY_LABELS, p, `P${p}`), PRIORITY_TONES[p] ?? '');
}

/* ── Conversation ────────────────────────────────────────────── */

function conversation(ui, messages, { showInternal }) {
	if (!messages.length) {
		return ui.empty('No messages yet', 'Nothing has been said since the ticket was raised.', 'chat');
	}
	/* Oldest first, sorted here rather than trusted: a thread read in the wrong order is a
	 * different conversation, and the reply an operator is answering has to be the last
	 * thing on the page. */
	const ordered = [...messages].sort((a, b) => utcMs(a.createdUtc) - utcMs(b.createdUtc) || (a.id ?? 0) - (b.id ?? 0));
	return `<ol class="thread">${ordered.map((m) => messageBlock(ui, m, showInternal)).join('')}</ol>`;
}

/**
 * One message in the thread.
 *
 * An internal note and a reply to the player are not two shades of the same thing: one the
 * player will read, the other they must never see. A game master glancing at this thread has
 * to know which is which without reading a word and without hovering anything — somebody
 * skimming a heated report at speed is exactly who would mistake one for the other, and the
 * mistake is disclosing staff discussion to the person it is about.
 *
 * So the two differ in every channel at once: background, border, and a spelled-out label in
 * words. Not an icon alone, and not a colour alone — an icon is the first thing a tired eye
 * skips past, and colour is the first thing a colour-blind operator does not have.
 */
function messageBlock(ui, m, showInternal) {
	const internal = Boolean(m.internal);
	// Belt to the server's braces. The server is what keeps internal notes away from
	// players; this never makes a view safe that would otherwise leak one.
	if (internal && !showInternal) return '';

	const kind = internal ? 'is-internal' : m.authorIsStaff ? 'is-staff' : 'is-player';
	const label = internal
		? 'Internal note — the player cannot see this'
		: m.authorIsStaff
			? 'Reply — the player can read this'
			: 'From the player';

	return `
		<li class="msg ${kind}">
			<div class="msg-label">${ui.esc(label)}</div>
			<div class="msg-head">
				<span class="msg-author">${ui.esc(m.authorAccount)}</span>
				${m.authorIsStaff ? ui.badge('staff', 'accent') : ''}
				<span class="grow"></span>
				<span class="msg-time" title="${ui.esc(ui.dateTime(m.createdUtc))}">${ui.ago(m.createdUtc)}</span>
			</div>
			<div class="msg-body">${ui.esc(m.body)}</div>
		</li>`;
}

/* ── Actions ─────────────────────────────────────────────────── */

function actionRow(ui, { act, label, tone = '', title, text, hint = '' }) {
	return `
		<div class="row-between">
			<div>
				<strong>${ui.esc(title)}</strong>
				<div class="small muted">${ui.esc(text)}</div>
				${hint ? `<div class="field-hint">${ui.esc(hint)}</div>` : ''}
			</div>
			<button class="btn btn-sm${tone ? ' btn-' + ui.esc(tone) : ''}" type="button" data-act="${ui.esc(act)}">${ui.esc(label)}</button>
		</div>`;
}

function actionsCard(ui, { ticket, mine, closed }) {
	return ui.card({
		title: 'Actions',
		sub: 'Each one asks for a reason, and the reason is what lands in the audit log.',
		body: `
			<div class="stack">
				${actionRow(ui, {
					act: 'reply',
					label: 'Reply',
					tone: 'primary',
					title: 'Reply to the player',
					text: 'Adds a message the player reads in their own ticket view. This is the only thing here they ever see.',
					hint: closed ? 'This ticket is closed. A reply still lands on it; consider reopening it so the thread is not answered in the archive.' : '',
				})}

				${actionRow(ui, {
					act: 'note',
					label: 'Add note',
					title: 'Add an internal note',
					text: 'Staff-only. It is kept on the ticket and never sent to the player — the server does not return it to them.',
				})}

				${mine
					? actionRow(ui, {
						act: 'unassign',
						label: 'Unassign',
						title: 'Give the ticket back',
						text: 'Returns it to the unassigned queue, where anybody can pick it up.',
					})
					: actionRow(ui, {
						act: 'assign-me',
						label: 'Assign to me',
						tone: 'primary',
						title: 'Take this ticket',
						text: ticket.assignedTo
							? `It is currently with ${ticket.assignedTo}. Taking it moves it to you.`
							: 'Nobody holds this ticket. Taking it says the player is being dealt with.',
					})}

				${actionRow(ui, {
					act: 'assign-other',
					label: 'Assign…',
					title: 'Assign to another operator',
					text: 'Hands the ticket to a named operator account.',
				})}

				${actionRow(ui, {
					act: 'status',
					label: 'Set status',
					title: 'Change the status',
					text: 'Resolved and Closed need a resolution — what was done, and what the player was told.',
				})}

				${actionRow(ui, {
					act: 'priority',
					label: 'Set priority',
					title: 'Change the priority',
					text: 'Orders the queue. Urgent is for something happening to a player right now.',
				})}
			</div>`,
	});
}

function wireActions(host, ctx, ticket, me) {
	const { api, ui } = ctx;
	const on = (act, fn) => host.querySelector(`[data-act="${act}"]`)?.addEventListener('click', fn);

	/* `onSubmit` returning false leaves the dialog open with everything the operator typed
	 * still in it, rather than sending an action nobody can explain later — or, worse,
	 * throwing away a written reply because one field was empty. */
	const reasonField = `
		<div class="field">
			<label for="tk-reason">Reason (recorded in the audit log)</label>
			<textarea id="tk-reason" name="reason" required placeholder="Why is this being done?"></textarea>
		</div>`;

	const askReason = (title, sub, confirmLabel, tone = 'primary') =>
		ui.modal({
			title,
			sub,
			confirmLabel,
			confirmTone: tone,
			body: reasonField,
			onSubmit: (values) => (String(values.reason).trim() ? values : false),
		});

	const composer = (title, sub, confirmLabel, hint) =>
		ui.modal({
			title,
			sub,
			confirmLabel,
			wide: true,
			body: `
				<div class="field">
					<label for="tk-body">Message</label>
					<textarea id="tk-body" name="body" rows="8" required placeholder="What should be said?"></textarea>
					<div class="field-hint">${ui.esc(hint)}</div>
				</div>
				${reasonField}`,
			onSubmit: (values) => (String(values.body).trim() && String(values.reason).trim() ? values : false),
		});

	on('reply', async () => {
		const v = await composer(
			'Reply to the player',
			`Goes to ${ticket.reporterAccount}, who reads it in their own ticket view. Write it as something the player will read, because they will.`,
			'Send reply',
			'The player sees this message. The reason below is for the audit log and stays with staff.',
		);
		if (!v) return;
		if (await ctx.attempt(() => api.replyToTicket(ticket.id, v.body, false, v.reason), 'Reply sent', 'The player can read it in their own ticket view.')) {
			ctx.refresh();
		}
	});

	on('note', async () => {
		const v = await composer(
			'Add an internal note',
			'Staff-only. It is kept with the ticket for whoever picks it up next, and the server never returns it to the player.',
			'Add note',
			'The player never sees this. It is for the next operator to read.',
		);
		if (!v) return;
		if (await ctx.attempt(() => api.replyToTicket(ticket.id, v.body, true, v.reason), 'Note added', 'Visible to staff only.')) {
			ctx.refresh();
		}
	});

	on('assign-me', async () => {
		const v = await askReason(
			'Take this ticket',
			ticket.assignedTo
				? `It moves from ${ticket.assignedTo} to you.`
				: 'It moves from the unassigned queue to you.',
			'Assign to me',
		);
		if (!v) return;
		if (await ctx.attempt(() => api.assignTicket(ticket.id, me, v.reason), 'Assigned to you')) ctx.refresh();
	});

	on('unassign', async () => {
		const v = await askReason(
			'Give the ticket back',
			'It returns to the unassigned queue. Nothing else about it changes — the status, the thread and the priority stay as they are.',
			'Unassign',
		);
		if (!v) return;
		// Null is the unassign: the field is cleared, not set to an operator named "none".
		if (await ctx.attempt(() => api.assignTicket(ticket.id, null, v.reason), 'Unassigned')) ctx.refresh();
	});

	on('assign-other', async () => {
		const v = await ui.modal({
			title: 'Assign to another operator',
			sub: 'The ticket moves to the account you name. The server checks that they may hold one.',
			confirmLabel: 'Assign',
			body: `
				<div class="field">
					<label for="tk-assignee">Operator account</label>
					<input id="tk-assignee" name="assignee" autocomplete="off" spellcheck="false" required
						value="${ui.esc(ticket.assignedTo ?? '')}" />
				</div>
				${reasonField}`,
			onSubmit: (values) => (String(values.assignee).trim() && String(values.reason).trim() ? values : false),
		});
		if (!v) return;
		if (await ctx.attempt(() => api.assignTicket(ticket.id, String(v.assignee).trim(), v.reason), 'Ticket assigned')) {
			ctx.refresh();
		}
	});

	on('status', async () => {
		const v = await ui.modal({
			title: 'Change the status',
			sub: 'Resolved and Closed are the end of the ticket for the player, so both need a resolution saying what was done.',
			confirmLabel: 'Apply',
			body: `
				<div class="field">
					<label for="tk-status">Status</label>
					<select id="tk-status" name="status">
						${STATUS_LABELS.map((s, i) => `<option value="${i}"${ticket.status === i ? ' selected' : ''}>${ui.esc(s)}</option>`).join('')}
					</select>
				</div>
				<div class="field">
					<label for="tk-resolution">Resolution</label>
					<textarea id="tk-resolution" name="resolution" rows="4"
						placeholder="What was done, and what the player was told.">${ui.esc(ticket.resolution ?? '')}</textarea>
					<div class="field-hint" id="tk-resolution-hint"></div>
				</div>
				${reasonField}`,
			onReady: (form) => {
				const select = form.querySelector('#tk-status');
				const hint = form.querySelector('#tk-resolution-hint');
				const sync = () => {
					const required = RESOLUTION_REQUIRED.has(Number(select.value));
					hint.textContent = required
						? 'Required for this status: the server refuses it without one.'
						: 'Optional for this status.';
				};
				select.addEventListener('change', sync);
				sync();
			},
			/* The server enforces the resolution too. Checking it here is not duplication for
			 * its own sake: a refusal from the server arrives after the dialog has taken the
			 * operator's paragraph of typing, and this way they never lose it. */
			onSubmit: (values, form) => {
				if (RESOLUTION_REQUIRED.has(Number(values.status)) && !String(values.resolution).trim()) {
					const box = form.querySelector('#tk-resolution');
					box.setCustomValidity('Resolved and Closed need a resolution.');
					box.reportValidity();
					setTimeout(() => box.setCustomValidity(''), 10);
					return false;
				}
				return String(values.reason).trim() ? values : false;
			},
		});
		if (!v) return;
		const status = Number(v.status);
		const resolution = String(v.resolution ?? '').trim() || null;
		if (await ctx.attempt(() => api.setTicketStatus(ticket.id, status, resolution, v.reason), 'Status changed', `Now ${STATUS_LABELS[status] ?? status}.`)) {
			ctx.refresh();
		}
	});

	on('priority', async () => {
		const v = await ui.modal({
			title: 'Change the priority',
			sub: 'Priority orders the queue and nothing else. It does not notify anybody.',
			confirmLabel: 'Apply',
			body: `
				<div class="field">
					<label for="tk-priority">Priority</label>
					<select id="tk-priority" name="priority">
						${PRIORITY_LABELS.map((p, i) => `<option value="${i}"${ticket.priority === i ? ' selected' : ''}>${ui.esc(p)}</option>`).join('')}
					</select>
				</div>
				${reasonField}`,
			onSubmit: (values) => (String(values.reason).trim() ? values : false),
		});
		if (!v) return;
		const priority = Number(v.priority);
		if (await ctx.attempt(() => api.setTicketPriority(ticket.id, priority, v.reason), 'Priority changed', `Now ${PRIORITY_LABELS[priority] ?? priority}.`)) {
			ctx.refresh();
		}
	});
}
