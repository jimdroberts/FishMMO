/*
 * Support → Chat log.
 *
 * The screen a game master opens with a harassment report in front of them: find what was
 * said, by whom, on which channel, in a window around the time the report names.
 *
 * EVERY value rendered here is player-supplied. The message is literally whatever somebody
 * typed into the chat box, and the character and account names arrive denormalized onto the
 * chat row. This is the one view in the panel whose content is written by the person being
 * investigated, so every interpolation below goes through `ui.esc` — an unescaped message
 * would be stored XSS pointed straight at the operator reading the report.
 */

/* The channels as the server's ChatChannel enum numbers them. The value sent to the API is
 * the number, because that is what the column holds; the label is what the operator reads.
 * The API answers with both for the same reason. */
const CHANNELS = [
	{ value: 0, name: 'Say' },
	{ value: 1, name: 'World' },
	{ value: 2, name: 'Region' },
	{ value: 3, name: 'Party' },
	{ value: 4, name: 'Guild' },
	{ value: 5, name: 'Tell' },
	{ value: 6, name: 'Trade' },
	{ value: 7, name: 'System' },
	{ value: 8, name: 'Command' },
	{ value: 9, name: 'Discord' },
];

/* Tone carries the one distinction that changes how a message reads: who could hear it.
 * A slur in World was shouted at everybody; the same words in Tell were aimed at one
 * person. System and Command are not a player talking at all. */
const CHANNEL_TONE = {
	World: 'warn',
	Trade: 'warn',
	Tell: 'accent',
	Party: 'accent',
	Guild: 'ok',
	System: 'info',
	Command: 'info',
	Discord: 'info',
};

export async function render(host, ctx) {
	const { api, ui } = ctx;
	const filters = { characterName: '', accountName: '', channel: '', text: '', from: '', to: '', page: 1, pageSize: 50 };

	host.innerHTML = `
		<div class="page-head">
			<div class="page-head-text">
				<h1>Chat log</h1>
				<p class="page-head-sub">
					What players said, as the scene servers persisted it. This view is read-only: chat is
					the evidence a report is judged on, so nothing here edits or removes a message.
					Newest first, and a message search has to be narrowed by a name or a start date.
				</p>
			</div>
		</div>

		<div class="stack-lg">
			${ui.banner('info', 'Messages are shown exactly as they were sent',
				'Nothing is filtered, corrected or re-wrapped, because the wording is the thing being reported on. Names are the ones in use at the time the message was written, which is not always the name the character carries now.')}

			<section class="card">
				<div class="toolbar">
					<input class="search" id="text" type="search" placeholder="Message contains…" autocomplete="off" />
					<input id="characterName" type="search" placeholder="Character name" autocomplete="off" style="max-width:180px" />
					<input id="accountName" type="search" placeholder="Account name" autocomplete="off" style="max-width:180px" />
					<select id="channel">
						<option value="">Any channel</option>
						${CHANNELS.map((c) => `<option value="${c.value}">${ui.esc(c.name)}</option>`).join('')}
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
	const countHost = host.querySelector('#count');

	async function load() {
		results.innerHTML = ui.loading();

		let page;
		try {
			page = await api.getChatLog(filters);
		} catch (error) {
			/* The server refuses a message search that has nothing to narrow it, and it says in
			 * the refusal which filters would work. Showing that sentence is the entire point:
			 * swapping it for "something went wrong" would leave the operator retrying the same
			 * query, and quietly returning an empty table would read as "they never said it". */
			countHost.textContent = '';
			pagerHost.innerHTML = '';
			pagerHost.hidden = true;
			results.innerHTML = ui.empty(
				error.status === 400 ? 'That search needs narrowing' : 'The chat log could not be read',
				error.message || 'The server refused the search.',
				'search',
			);
			return;
		}

		countHost.textContent = `${ui.num(page.totalCount)} messages`;

		results.innerHTML = page.items.length
			? ui.table({
				columns: [
					{
						label: 'When',
						cell: (m) => `<span class="nowrap tnum" title="${ui.esc(ui.dateTime(m.timeUtc))}">${ui.esc(ui.shortTime(m.timeUtc))}</span>`,
					},
					{ label: 'Character', cell: (m) => characterCell(ui, m) },
					{ label: 'Account', cell: (m) => accountCell(ui, m) },
					{ label: 'Channel', cell: (m) => ui.badge(m.channelName, CHANNEL_TONE[m.channelName] ?? '') },
					{ label: 'Message', cell: (m) => messageCell(ui, m) },
				],
				rows: page.items,
			})
			: ui.empty(
				'No message matches',
				'Nothing in that window matches those filters. Widen the dates, clear the channel, or search for a shorter fragment of the message.',
				'chat',
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

	/* One debounce timer across all three text boxes, so an operator who clears a name and
	 * then types a message fragment produces one search rather than two.
	 *
	 * The filter itself is recorded straight away and only the load is deferred. Setting it
	 * inside the timer instead looks equivalent and is not: the next keystroke in ANY box
	 * cancels the pending timer, so the box typed in first would lose its value entirely and
	 * the search would run with a filter the operator can see on screen but the server was
	 * never told about. */
	let debounce;
	for (const id of ['text', 'characterName', 'accountName']) {
		host.querySelector(`#${id}`).addEventListener('input', (e) => {
			filters[id] = e.target.value.trim();
			filters.page = 1;
			clearTimeout(debounce);
			debounce = setTimeout(load, 220);
		});
	}
	for (const id of ['channel', 'from', 'to']) {
		host.querySelector(`#${id}`).addEventListener('change', (e) => {
			filters[id] = e.target.value;
			filters.page = 1;
			load();
		});
	}

	await load();
}

/* The character may have been deleted since — chat outlives characters by design — so the
 * link can lead to a page that says so. That is better than no link: the id is what ties
 * the message to the rest of the investigation. */
function characterCell(ui, m) {
	const name = m.characterName || '—';
	if (!m.characterId) return `<span class="cell-primary">${ui.esc(name)}</span>`;
	return `
		<a class="cell-primary" href="#/support/characters/${encodeURIComponent(m.characterId)}">${ui.esc(name)}</a>
		<div class="cell-sub tnum">${ui.esc(m.characterId)}</div>`;
}

/* The account is the link that matters when somebody continues the same harassment on a
 * second character. */
function accountCell(ui, m) {
	if (!m.account) return '<span class="faint">—</span>';
	return `<a href="#/support/accounts/${encodeURIComponent(m.account)}">${ui.esc(m.account)}</a>`;
}

/* `overflow-wrap:anywhere` because a message can be four thousand characters with no spaces
 * in it, and a single row must not be able to push every other column off the screen. */
function messageCell(ui, m) {
	return `<div style="max-width:60ch;overflow-wrap:anywhere">${ui.esc(m.message)}</div>`;
}
