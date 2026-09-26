/*
 * Platform → Queues. The outbound email queue, the outbound SMS queue, and the group finder queue.
 *
 * The SMS card is the email card's twin — same states, same retry — for players who verify by
 * phone. Its phone numbers arrive masked to the last four digits and are masked again here if
 * they ever arrive whole; the message body is never fetched.
 *
 * WHY THIS PAGE EXISTS.
 *
 * Account verification reaches a player through the email queue and through nothing else.
 * A registration writes a row; a login server is supposed to claim it and send it. When
 * none does — the process is down, SMTP is refusing the credentials, the host cannot reach
 * the relay — every new account is created and not one of them can ever be verified. And
 * nothing anywhere raises a hand: no error is logged, because from the database's point of
 * view nothing failed. The website keeps accepting sign-ups. The players who never got
 * their mail do not file tickets about it, they just leave.
 *
 * So this page leads with the one number that can tell that outage from a quiet day. It is
 * not the depth of the queue — a dead queue and a healthy one both read as a handful of
 * rows on a small shard. It is the AGE of the oldest message nothing has picked up. A
 * working deployment drains in seconds; minutes mean nobody is claiming, and that tile
 * turns red and says what it means in words.
 *
 * WHAT IS DELIBERATELY NOT HERE.
 *
 * There is no clear, purge or delete control, for either queue. A queue that can be emptied
 * from a browser is precisely how the outage above becomes invisible: the rows that prove
 * registration is broken are the first thing anybody reaches for when the number looks
 * alarming, and deleting them destroys the evidence AND the mail those accounts are still
 * owed. The only write on this page is a retry, and a retry only ever puts a message back
 * into the queue.
 *
 * Email addresses, account names, character names, subjects and the error text a mail
 * server sent back are all user- or third-party-supplied strings on their way out of the
 * database, so every one of them goes through `ui.esc`.
 */

/* Ten seconds. Faster than the server board's five because there is nothing to gain — a
 * queue age is a clock that the browser could count on its own — and slower than a minute
 * because somebody watching this page is watching it during an incident and wants the
 * pending count to fall while they look at it. */
const REFRESH_MS = 10000;

/* Used only if the payload carries no thresholds. The server decides them; these exist so
 * that a missing field cannot silently turn the alarm off. */
const FALLBACK_WARN_SECONDS = 300;
const FALLBACK_DANGER_SECONDS = 900;

const EMAIL_STATES = [
	{ value: 'pending', label: 'Pending', tone: 'warn', pulse: true },
	{ value: 'claimed', label: 'Claimed', tone: 'info', pulse: true },
	{ value: 'failed', label: 'Failed', tone: 'danger', pulse: false },
	{ value: 'sent', label: 'Sent', tone: 'ok', pulse: false },
];

const FINDER_STATES = [
	{ value: 0, label: 'Waiting', tone: 'warn', pulse: true },
	{ value: 1, label: 'Matched', tone: 'ok', pulse: false },
];

export async function render(host, ctx) {
	const { api, ui } = ctx;

	const emailFilters = { state: '', search: '', page: 1, pageSize: 25 };
	const finderFilters = { status: '', page: 1, pageSize: 25 };
	const smsFilters = { state: '', search: '', page: 1, pageSize: 25 };

	let email = null;
	let finder = null;
	let sms = null;
	let refreshError = null;

	let poll = null;
	/* Registered before anything awaits, and the router keeps only ONE cleanup per view — a
	 * second `onCleanup` call would replace this one and leave the timer firing into a page
	 * the operator has already navigated away from. */
	ctx.onCleanup(() => clearInterval(poll));

	host.innerHTML = `
		<div class="page-head">
			<div class="page-head-text">
				<h1>Queues</h1>
				<p class="page-head-sub">
					Work the shard has accepted but not yet finished: verification emails waiting for a
					login server to send them, text messages waiting for the SMS drain, and players waiting for a group. All three refresh every
					${REFRESH_MS / 1000} seconds.
				</p>
			</div>
			<div class="page-head-actions">
				<button class="btn" data-act="refresh">${ui.icon('refresh')} Refresh</button>
			</div>
		</div>

		<div class="stack-lg">
			<div id="alarm"></div>
			<div class="grid grid-4" id="stats"></div>

			<section class="card">
				<header class="card-head">
					<div class="card-head-text">
						<div class="card-title">Email queue</div>
						<div class="card-sub">Every outbound message, delivered or not. The body is never read by this panel — a verification body contains the verification link, which is a credential.</div>
					</div>
				</header>
				<div class="toolbar">
					<select id="email-state">
						<option value="">Any state</option>
						${EMAIL_STATES.map((s) => `<option value="${ui.esc(s.value)}">${ui.esc(s.label)}</option>`).join('')}
					</select>
					<input class="search" id="email-search" type="search" placeholder="Email address or account…" autocomplete="off" />
					<span class="grow"></span>
					<span class="small muted" id="email-count"></span>
				</div>
				<div class="card-body flush" id="email-results">${ui.loading()}</div>
				<footer class="card-foot" id="email-pager"></footer>
			</section>

			<section class="card">
				<header class="card-head">
					<div class="card-head-text">
						<div class="card-title">SMS queue</div>
						<div class="card-sub">Every outbound text message. Phone numbers are shown to their last four digits only, and the body is never read — a verification text is a one-time code.</div>
					</div>
				</header>
				<div id="sms-alarm"></div>
				<div class="toolbar">
					<select id="sms-state">
						<option value="">Any state</option>
						${EMAIL_STATES.map((s) => `<option value="${ui.esc(s.value)}">${ui.esc(s.label)}</option>`).join('')}
					</select>
					<input class="search" id="sms-search" type="search" placeholder="Account, or a whole phone number…" autocomplete="off" />
					<span class="grow"></span>
					<span class="small muted" id="sms-count"></span>
				</div>
				<div class="card-body flush" id="sms-results">${ui.loading()}</div>
				<footer class="card-foot" id="sms-pager"></footer>
			</section>

			${ui.banner('info', 'There is no control here that empties any queue',
				'That is the point of the page, not an omission. A queue that can be cleared from a browser is how a registration outage becomes invisible: the rows that prove nothing is sending are the first thing somebody deletes when the number looks alarming, and they are also the mail those accounts are still owed. Retry is the only write, and it only ever puts a message back into the queue.')}

			<section class="card">
				<header class="card-head">
					<div class="card-head-text">
						<div class="card-title">Group finder queue</div>
						<div class="card-sub">One table, matched inside a single transaction by a pump running on every scene server. There is no separate matchmaker process, and nothing here can remove a row.</div>
					</div>
				</header>
				<div class="toolbar">
					<select id="finder-status">
						<option value="">Any state</option>
						${FINDER_STATES.map((s) => `<option value="${s.value}">${ui.esc(s.label)}</option>`).join('')}
					</select>
					<span class="grow"></span>
					<span class="small muted" id="finder-count"></span>
				</div>
				<div class="card-body flush" id="finder-results">${ui.loading()}</div>
				<footer class="card-foot" id="finder-pager"></footer>
			</section>
		</div>`;

	const alarmHost = host.querySelector('#alarm');
	const statsHost = host.querySelector('#stats');
	const emailResults = host.querySelector('#email-results');
	const emailPager = host.querySelector('#email-pager');
	const emailCount = host.querySelector('#email-count');
	const finderResults = host.querySelector('#finder-results');
	const finderPager = host.querySelector('#finder-pager');
	const finderCount = host.querySelector('#finder-count');
	const smsAlarm = host.querySelector('#sms-alarm');
	const smsResults = host.querySelector('#sms-results');
	const smsPager = host.querySelector('#sms-pager');
	const smsCount = host.querySelector('#sms-count');

	/* A failed poll keeps what is on screen rather than blanking it. The last good read,
	 * labelled as stale, is more use to somebody mid-incident than an error where the
	 * numbers were. `spinner` is false on the poll for the same reason: replacing a table
	 * with "Loading…" every ten seconds makes it unreadable. */
	/* `source` is `api` for a read somebody asked for — the first load, Refresh, a filter, a
	 * page, the re-read after a retry — and `api.auto` only for the timer's poll: staff reads
	 * are audited, but a background poll is marked and not recorded. */
	async function loadEmail(spinner, source = api) {
		if (spinner) emailResults.innerHTML = ui.loading();
		email = await source.getEmailQueue(emailFilters);
		paintEmail();
		paintSummary();
	}

	async function loadFinder(spinner, source = api) {
		if (spinner) finderResults.innerHTML = ui.loading();
		finder = await source.getGroupFinderQueue(finderFilters);
		paintFinder();
	}

	async function loadSms(spinner, source = api) {
		if (spinner) smsResults.innerHTML = ui.loading();
		sms = await source.getSmsQueue(smsFilters);
		paintSms();
	}

	/* Only ever driven by the timer. */
	async function pollOnce() {
		try {
			await Promise.all([loadEmail(false, api.auto), loadSms(false, api.auto), loadFinder(false, api.auto)]);
			refreshError = null;
		} catch (err) {
			refreshError = err.message || 'The queues could not be refreshed.';
		}
		paintSummary();
	}

	function paintSummary() {
		if (!email) return;

		const counts = email.counts ?? {};
		const warnAt = Number(email.pendingWarnSeconds) > 0 ? Number(email.pendingWarnSeconds) : FALLBACK_WARN_SECONDS;
		const dangerAt = Number(email.pendingDangerSeconds) > 0 ? Number(email.pendingDangerSeconds) : FALLBACK_DANGER_SECONDS;

		/* Null is not zero, and the difference is the whole alarm. Null means nothing is
		 * waiting to be claimed, which is what a working queue looks like. Zero means
		 * something is waiting and has been for no time at all. */
		const oldest = email.oldestPendingAgeSeconds;
		const hasPending = oldest !== null && oldest !== undefined;
		const tone = !hasPending ? 'ok' : oldest >= dangerAt ? 'danger' : oldest >= warnAt ? 'warn' : 'ok';

		alarmHost.innerHTML = `
			${tone === 'danger'
				? ui.banner('danger', 'Nothing is sending the verification emails',
					`The oldest message has been waiting ${ui.duration(oldest)} for a login server to claim it, and a working deployment drains this queue in seconds. While that is true, NOBODY CAN FINISH REGISTERING: the sign-up succeeds, the row is written, and the mail never arrives. Check that a login server is running and that its SMTP credentials still work. Nothing else will report this.`)
				: tone === 'warn'
					? ui.banner('warn', 'The email queue is not draining',
						`The oldest message has been waiting ${ui.duration(oldest)}. A login server normally claims one within seconds, so this is either a restart in progress or the beginning of a registration outage — every account that signs up while it lasts is left unverified.`)
					: counts.failed
						? ui.banner('warn', `${ui.num(counts.failed)} message${counts.failed === 1 ? ' has' : 's have'} failed to send`,
							'The queue is draining, but these were claimed and errored. Nothing in the login server releases a failed claim, so they will not be attempted again on their own — retry puts them back in line. Until one goes out, the account it belongs to cannot finish registering.')
						: ui.banner('ok', 'The email queue is draining',
							'Nothing is sitting unclaimed. This is what a working verification path looks like.')}
			${refreshError ? ui.banner('warn', 'These numbers are not live', `${refreshError} Showing the last good read.`) : ''}`;

		statsHost.innerHTML = `
			${ui.stat({
				label: 'Oldest pending',
				value: hasPending ? ui.duration(oldest) : 'none',
				note: hasPending
					? (tone === 'danger' ? 'registration is broken' : tone === 'warn' ? 'not draining' : 'draining normally')
					: 'nothing is waiting to be claimed',
				noteTone: tone,
			})}
			${ui.stat({
				label: 'Pending',
				value: ui.num(counts.pending ?? 0),
				note: 'queued, waiting for a login server',
				noteTone: (counts.pending ?? 0) > 0 && tone !== 'ok' ? tone : '',
			})}
			${ui.stat({
				label: 'Failed',
				value: ui.num(counts.failed ?? 0),
				note: (counts.failed ?? 0) ? 'will not retry on their own' : 'none errored',
				noteTone: (counts.failed ?? 0) ? 'danger' : 'ok',
			})}
			${ui.stat({
				label: 'In flight',
				value: ui.num(counts.claimed ?? 0),
				note: (counts.claimed ?? 0) ? 'claimed, no outcome reported yet' : 'nothing being delivered',
				noteTone: '',
			})}`;
	}

	function paintEmail() {
		const items = email.items ?? [];
		emailCount.textContent = `${ui.num(email.totalCount)} message${email.totalCount === 1 ? '' : 's'}`;

		emailResults.innerHTML = items.length
			? ui.table({
				columns: [
					{
						label: 'Recipient',
						cell: (e) => `
							<div class="cell-primary">${ui.esc(e.recipientEmail)}</div>
							<div class="cell-sub"><a href="#/support/accounts/${encodeURIComponent(e.recipientUsername ?? '')}">${ui.esc(e.recipientUsername)}</a></div>`,
					},
					/* The kind comes from its own column now, not from reading the subject. A
					 * stalled queue means something different for each: held-up verification
					 * mail stops people finishing registration, held-up resets stop people
					 * getting back in. */
					{ label: 'Kind', cell: (e) => emailKindLabel(ui, e) },
					{ label: 'Subject', cell: (e) => `<span class="small">${ui.esc(e.subject)}</span>` },
					{ label: 'State', cell: (e) => emailStateBadge(ui, e) },
					{
						label: 'Queued',
						align: 'right',
						cell: (e) => queuedCell(ui, e),
					},
					{ label: 'Attempts', align: 'right', cell: (e) => `<span class="tnum">${ui.num(e.attempts ?? 0)}</span>` },
					{ label: 'Last error', cell: (e) => errorCell(ui, e) },
					{
						label: '',
						align: 'right',
						cell: (e) => (e.canRetry ? `<button class="btn btn-sm" data-retry="${ui.esc(e.id)}">Retry</button>` : ''),
					},
				],
				rows: items,
				/* Failed is the row somebody opened this page to find: an account that cannot
				 * finish registering and will not be retried by anything. It gets the alert
				 * tint rather than being left to a badge that scans like every other badge. */
				rowAttrs: (e) => (e.state === 'failed' ? 'class="is-alert"' : ''),
			})
			: ui.empty('No message',
				emailFilters.state === '' && emailFilters.search === ''
					? 'The email queue is empty. That is what a healthy shard looks like between registrations — a message appears here the moment somebody signs up, and disappears from the pending count within seconds.'
					: 'Nothing matches that filter. Clear the state, or widen the search.',
				'mail');

		wireRetryButtons();

		emailPager.innerHTML = ui.pager(email.page, email.pageSize, email.totalCount, 'email-page');
		emailPager.hidden = !emailPager.innerHTML.trim();
		emailPager.querySelectorAll('[data-action="email-page"]').forEach((b) =>
			b.addEventListener('click', () => {
				emailFilters.page = Number(b.dataset.page);
				loadEmail(true).catch(showFailure);
			}),
		);
	}

	/* The SMS twin of paintEmail and the email half of paintSummary, kept inside its own card:
	 * the page's headline tiles stay the email queue's, and this card raises its own alarm only
	 * when there is something to say. The phone is masked by the server; `maskPhone` re-applies
	 * the mask to anything that arrives whole, so a regression there cannot print a number. */
	function paintSms() {
		const items = sms.items ?? [];
		const counts = sms.counts ?? {};
		const warnAt = Number(sms.pendingWarnSeconds) > 0 ? Number(sms.pendingWarnSeconds) : FALLBACK_WARN_SECONDS;
		const dangerAt = Number(sms.pendingDangerSeconds) > 0 ? Number(sms.pendingDangerSeconds) : FALLBACK_DANGER_SECONDS;
		const oldest = sms.oldestPendingAgeSeconds;
		const hasPending = oldest !== null && oldest !== undefined;
		const tone = !hasPending ? 'ok' : oldest >= dangerAt ? 'danger' : oldest >= warnAt ? 'warn' : 'ok';

		smsAlarm.innerHTML = tone === 'danger'
			? ui.banner('danger', 'Nothing is sending the text messages',
				`The oldest message has been waiting ${ui.duration(oldest)} for the SMS drain to claim it. Players who chose to verify by phone cannot finish registering while that lasts. The drain runs in this panel and idles when no SMS sender is configured — in Production that means Sms:Provider is unset. Check the panel log for "SMS queue drain is idle".`)
			: tone === 'warn'
				? ui.banner('warn', 'The SMS queue is not draining',
					`The oldest message has been waiting ${ui.duration(oldest)}. The drain normally claims one within seconds.`)
				: counts.failed
					? ui.banner('warn', `${ui.num(counts.failed)} text message${counts.failed === 1 ? ' has' : 's have'} failed to send`,
						'The drain gave up on these after its attempt limit. Retry releases the claim for one more attempt.')
					: '';

		smsCount.textContent = `${ui.num(sms.totalCount)} message${sms.totalCount === 1 ? '' : 's'} · ${ui.num(counts.pending ?? 0)} pending · ${ui.num(counts.failed ?? 0)} failed`
			+ (hasPending ? ` · oldest pending ${ui.duration(oldest)}` : '');

		smsResults.innerHTML = items.length
			? ui.table({
				columns: [
					{
						label: 'Recipient',
						cell: (s) => `
							<div class="cell-primary tnum">${ui.esc(maskPhone(s.recipientPhone))}</div>
							<div class="cell-sub"><a href="#/support/accounts/${encodeURIComponent(s.recipientUsername ?? '')}">${ui.esc(s.recipientUsername)}</a></div>`,
					},
					{ label: 'Kind', cell: (s) => smsKindLabel(ui, s) },
					{ label: 'State', cell: (s) => emailStateBadge(ui, s) },
					{
						label: 'Queued',
						align: 'right',
						cell: (s) => queuedCell(ui, s),
					},
					{ label: 'Attempts', align: 'right', cell: (s) => `<span class="tnum">${ui.num(s.attempts ?? 0)}</span>` },
					{ label: 'Last error', cell: (s) => errorCell(ui, s) },
					{
						label: '',
						align: 'right',
						cell: (s) => (s.canRetry ? `<button class="btn btn-sm" data-sms-retry="${ui.esc(s.id)}">Retry</button>` : ''),
					},
				],
				rows: items,
				rowAttrs: (s) => (s.state === 'failed' ? 'class="is-alert"' : ''),
			})
			: ui.empty('No message',
				smsFilters.state === '' && smsFilters.search === ''
					? 'The SMS queue is empty. A message appears here when somebody asks for a code by text.'
					: 'Nothing matches that filter. A phone number only matches whole, including its country code.',
				'mail');

		wireSmsRetryButtons();

		smsPager.innerHTML = ui.pager(sms.page, sms.pageSize, sms.totalCount, 'sms-page');
		smsPager.hidden = !smsPager.innerHTML.trim();
		smsPager.querySelectorAll('[data-action="sms-page"]').forEach((b) =>
			b.addEventListener('click', () => {
				smsFilters.page = Number(b.dataset.page);
				loadSms(true).catch(showFailure);
			}),
		);
	}

	function wireSmsRetryButtons() {
		smsResults.querySelectorAll('[data-sms-retry]').forEach((b) =>
			b.addEventListener('click', async () => {
				const id = b.dataset.smsRetry;
				const row = (sms.items ?? []).find((s) => String(s.id) === String(id));

				const confirmed = await ui.modal({
					title: 'Put this text message back in the queue',
					sub: 'The claim on it is released so the SMS drain can pick it up again.',
					confirmLabel: 'Retry',
					body: `
						<p class="small muted">
							This does not send anything. It makes the row eligible to be claimed again,
							so if nothing is claiming the message will sit where it is. A failed message
							has used its attempts, so this grants one more; the attempt count and the
							last error are kept as the evidence.
						</p>
						${row ? `<p class="small muted">Addressed to <strong>${ui.esc(maskPhone(row.recipientPhone))}</strong> for account <strong>${ui.esc(row.recipientUsername)}</strong>.</p>` : ''}
						<div class="field">
							<label for="reason">Reason (recorded in the audit log)</label>
							<textarea id="reason" name="reason" required></textarea>
						</div>`,
					onSubmit: (values) => (String(values.reason ?? '').trim() ? values : false),
				});
				if (!confirmed) return;

				const result = await ctx.attempt(() => api.retrySms(id, confirmed.reason));
				if (!result) return;
				ui.toast('Message re-queued', result.message ?? '', 'ok', 6000);
				loadSms(true).catch(showFailure);
			}),
		);
	}

	function paintFinder() {
		const items = finder.items ?? [];
		const counts = finder.counts ?? {};
		finderCount.textContent = `${ui.num(counts.waiting ?? 0)} waiting · ${ui.num(counts.matched ?? 0)} matched`;

		finderResults.innerHTML = items.length
			? ui.table({
				columns: [
					{
						label: 'Character',
						cell: (q) => `
							<div class="cell-primary">${q.characterName ? ui.esc(q.characterName) : `<span class="faint">character ${ui.esc(q.characterId)}</span>`}</div>
							<div class="cell-sub">${q.accountName
								? `<a href="#/support/accounts/${encodeURIComponent(q.accountName)}">${ui.esc(q.accountName)}</a>`
								: '<span class="faint">no account row</span>'}</div>`,
					},
					{
						label: 'Queued for',
						cell: (q) => `
							<div>${ui.esc(q.sceneName)}</div>
							<div class="cell-sub">${ui.badge(readable(q.sceneTypeName ?? `type ${q.sceneType}`))} difficulty ${ui.esc(q.difficulty)}</div>`,
					},
					{ label: 'State', cell: (q) => finderStateBadge(ui, q) },
					{
						label: 'Waiting',
						align: 'right',
						cell: (q) => `<span class="nowrap" title="${ui.esc(ui.dateTime(q.queuedUtc))}">${ui.duration(q.waitSeconds)}</span>`,
					},
					{ label: 'Heartbeat', align: 'right', cell: (q) => pulseCell(ui, q) },
					{ label: 'Group', cell: (q) => groupCell(ui, q) },
				],
				rows: items,
				/* A stale row is the one worth looking at: the character's scene server stopped
				 * pulsing, so the matcher already excludes them and the sweep will remove them.
				 * A long wait on a stale row is not the matcher failing. */
				rowAttrs: (q) => (q.stale ? 'class="is-alert"' : ''),
			})
			: ui.empty('Nobody is queued',
				finderFilters.status === ''
					? 'No character is waiting for a group or an arena match right now.'
					: 'Nothing matches that filter.',
				'users');

		finderPager.innerHTML = ui.pager(finder.page, finder.pageSize, finder.totalCount, 'finder-page');
		finderPager.hidden = !finderPager.innerHTML.trim();
		finderPager.querySelectorAll('[data-action="finder-page"]').forEach((b) =>
			b.addEventListener('click', () => {
				finderFilters.page = Number(b.dataset.page);
				loadFinder(true).catch(showFailure);
			}),
		);
	}

	function wireRetryButtons() {
		emailResults.querySelectorAll('[data-retry]').forEach((b) =>
			b.addEventListener('click', async () => {
				const id = b.dataset.retry;
				const row = (email.items ?? []).find((e) => String(e.id) === String(id));

				const confirmed = await ui.modal({
					title: 'Put this message back in the queue',
					sub: 'The claim on it is released so a login server can pick it up again.',
					confirmLabel: 'Retry',
					body: `
						<p class="small muted">
							This does not send anything. Delivery belongs to the login server, and all
							this does is make the row eligible to be claimed again — so if nothing is
							claiming, which is what a climbing pending age means, the message will sit
							exactly where it is. The attempt count and the last error are kept, because
							they are the evidence of what went wrong.
						</p>
						${row ? `<p class="small muted">Addressed to <strong>${ui.esc(row.recipientEmail)}</strong> for account <strong>${ui.esc(row.recipientUsername)}</strong>.</p>` : ''}
						<div class="field">
							<label for="reason">Reason (recorded in the audit log)</label>
							<textarea id="reason" name="reason" required></textarea>
						</div>`,
					onSubmit: (values) => (String(values.reason ?? '').trim() ? values : false),
				});
				if (!confirmed) return;

				const result = await ctx.attempt(() => api.retryEmail(id, confirmed.reason));
				if (!result) return;
				ui.toast('Message re-queued', result.message ?? '', 'ok', 6000);
				loadEmail(true).catch(showFailure);
			}),
		);
	}

	/* A read that was asked for and failed — a filter change, a page, a manual refresh. The
	 * spinner it put up must come back down, or the table stays reading "Loading…" forever
	 * over data that is still perfectly good; repainting from the last read and saying so
	 * above it is the honest result. The FIRST reads are deliberately not routed here: they
	 * are allowed to throw so the router renders a failed page rather than an empty one. */
	function showFailure(err) {
		refreshError = err.message || 'That could not be read.';
		if (email) paintEmail();
		if (sms) paintSms();
		if (finder) paintFinder();
		paintSummary();
	}

	host.querySelector('[data-act="refresh"]').addEventListener('click', () => {
		loadEmail(true).catch(showFailure);
		loadSms(true).catch(showFailure);
		loadFinder(true).catch(showFailure);
	});

	host.querySelector('#sms-state').addEventListener('change', (e) => {
		smsFilters.state = e.target.value;
		smsFilters.page = 1;
		loadSms(true).catch(showFailure);
	});

	let smsDebounce;
	host.querySelector('#sms-search').addEventListener('input', (e) => {
		clearTimeout(smsDebounce);
		const value = e.target.value.trim();
		smsDebounce = setTimeout(() => {
			smsFilters.search = value;
			smsFilters.page = 1;
			loadSms(true).catch(showFailure);
		}, 220);
	});

	host.querySelector('#email-state').addEventListener('change', (e) => {
		emailFilters.state = e.target.value;
		emailFilters.page = 1;
		loadEmail(true).catch(showFailure);
	});

	let debounce;
	host.querySelector('#email-search').addEventListener('input', (e) => {
		clearTimeout(debounce);
		const value = e.target.value.trim();
		debounce = setTimeout(() => {
			emailFilters.search = value;
			emailFilters.page = 1;
			loadEmail(true).catch(showFailure);
		}, 220);
	});

	host.querySelector('#finder-status').addEventListener('change', (e) => {
		finderFilters.status = e.target.value;
		finderFilters.page = 1;
		loadFinder(true).catch(showFailure);
	});

	/* Started before the first read so the page heals itself: if the first read throws, the
	 * router shows the failure, and the first poll that succeeds paints over the top of it. */
	poll = setInterval(pollOnce, REFRESH_MS);

	// The first reads are allowed to throw — the router turns that into a visible failure.
	await Promise.all([loadEmail(true), loadSms(true), loadFinder(true)]);
}

/** The server's word for what this text message is. */
function smsKindLabel(ui, s) {
	const kind = String(s.kind ?? '');
	if (kind === 'verification') return '<span class="small">Verification</span>';
	if (kind === 'notification') return '<span class="small">Notification</span>';
	return ui.badge('Unknown', 'warn');
}

/**
 * A phone number masked to its last four digits: `+447700900123` → `+44•••••0123`.
 *
 * The server already sends it this way (PlatformQueuesController.MaskPhone, the same rule);
 * this is the second line. A value already carrying the bullet passes through untouched, so
 * the mask is never applied twice. The result still goes through `ui.esc` at the call site.
 */
function maskPhone(value) {
	if (value === null || value === undefined || value === '') return '';
	const text = String(value);
	if (text.includes('•')) return text;
	const digits = text.replace(/\D/g, '');
	const prefix = digits.length >= 10 ? digits.slice(0, 2) : '';
	const tail = digits.length > 4 ? digits.slice(-4) : '';
	return `+${prefix}•••••${tail}`;
}

/**
 * The state badge, with the two live states pulsing.
 *
 * Pending and claimed pulse because they are transient by definition: a message that has
 * been pending for ten minutes, or claimed for an hour, is the symptom this page is for,
 * and the moving dot is what makes an operator look at the age beside it.
 */
/** The server's word for what this message is. */
function emailKindLabel(ui, email) {
	const kind = String(email.kind ?? '');
	if (kind === 'verification') return '<span class="small">Verification</span>';
	if (kind === 'password-reset') return '<span class="small">Password reset</span>';
	if (kind === 'security-notice') return '<span class="small">Security notice</span>';
	/* A row written by a newer process, carrying a kind this build has never heard of. Saying
	 * so is better than guessing, and better than leaving the cell blank. */
	return ui.badge('Unknown', 'warn');
}

function emailStateBadge(ui, e) {
	const known = EMAIL_STATES.find((s) => s.value === e.state);
	if (!known) return ui.badge(String(e.state ?? 'unknown'));
	return ui.statusBadge(known.label.toLowerCase(), known.tone, known.pulse);
}

function finderStateBadge(ui, q) {
	const known = FINDER_STATES.find((s) => s.value === Number(q.status));
	const label = q.statusName || known?.label || `status ${q.status}`;
	return ui.statusBadge(readable(label), known?.tone ?? '', known?.pulse ?? false);
}

/* The error is whatever a mail server said, which can be a paragraph. It is truncated by
 * width rather than by character count so the whole thing is still there on hover, and it
 * carries the claiming server's name because "which host failed" is the next question. */
function errorCell(ui, e) {
	if (!e.lastError) {
		return e.claimedBy
			? `<span class="cell-sub">held by ${ui.esc(e.claimedBy)}</span>`
			: '<span class="faint">—</span>';
	}
	return `
		<div class="small" style="max-width:34ch;overflow-wrap:anywhere" title="${ui.esc(e.lastError)}">${ui.esc(e.lastError)}</div>
		${e.claimedBy ? `<div class="cell-sub">on ${ui.esc(e.claimedBy)}</div>` : ''}`;
}

/* How long a message has been queued, as the database measured it at the read — the clock
 * that stamped it, and the one the headline age above is taken by. Not `ui.ago(queuedUtc)`,
 * which is this browser's clock minus the database's stamp. The stamp stays in the tooltip. */
function queuedCell(ui, m) {
	const text = Number.isFinite(m.ageSeconds)
		? `${ui.duration(m.ageSeconds)} ago`
		: ui.ago(m.queuedUtc);
	return `<span class="nowrap tnum" title="${ui.esc(ui.dateTime(m.queuedUtc))}">${ui.esc(text)}</span>`;
}

/* A heartbeat age, and whether it has stopped. A stale row is excluded from matching and
 * will be swept away, so the wait beside it is not a queue that is failing to match — it is
 * a scene server that went away with somebody queued on it. */
function pulseCell(ui, q) {
	if (q.stale) {
		return `${ui.badge('stale', 'danger')}<div class="cell-sub">${ui.duration(q.pulseAgeSeconds)} ago</div>`;
	}
	return `<span class="nowrap tnum">${ui.duration(q.pulseAgeSeconds)} ago</span>`;
}

/* A matched row names the party and instance it is bound for; a waiting one may still name
 * the pre-made group it queued with. Both are worth printing, because "waiting" for a
 * pre-made group means something different from waiting alone. */
function groupCell(ui, q) {
	const parts = [];
	if (Number(q.groupId)) parts.push(`premade ${ui.esc(q.groupId)}`);
	if (Number(q.partyId)) parts.push(`party ${ui.esc(q.partyId)}`);
	if (Number(q.instanceId)) parts.push(`instance ${ui.esc(q.instanceId)}`);
	if (!parts.length) return '<span class="faint">—</span>';
	return `<span class="cell-sub">${parts.join(' · ')}</span>`;
}

/* Enum members arrive as "OpenWorld", "Waiting" — and a badge reading "OpenWorld" is the
 * only thing in a column of lower-case badges that shouts. */
function readable(name) {
	return String(name).replace(/([a-z0-9])([A-Z])/g, '$1 $2').toLowerCase();
}
