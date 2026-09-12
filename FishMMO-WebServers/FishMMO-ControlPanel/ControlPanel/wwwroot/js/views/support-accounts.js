/*
 * Support → Accounts. Search, inspect, and moderate an account.
 *
 * An account's access level IS its ban state: level 0 is banned, and there is no second
 * flag that could disagree with it. That is why the detail page reads the level to decide
 * whether it offers Ban or Unban, rather than tracking a status of its own.
 *
 * Every action here writes an audit row, and the reason the operator types is the only
 * part of that row a human wrote — the rest is machine-generated and explains nothing six
 * months later. So no action proceeds without one, and the dialogs say what will actually
 * happen rather than asking "are you sure?".
 *
 * Account names and email addresses are supplied by whoever registered them, so every one
 * of them goes through `ui.esc` on its way into markup and `encodeURIComponent` on its way
 * into a URL.
 */

export async function render(host, ctx) {
	if (ctx.param) return renderDetail(host, ctx, ctx.param);
	return renderList(host, ctx);
}

/* ── List ────────────────────────────────────────────────────── */

async function renderList(host, ctx) {
	const { api, ui } = ctx;
	const filters = { query: '', accessLevel: '', includeBanned: false, page: 1, pageSize: 25 };

	host.innerHTML = `
		<div class="page-head">
			<div class="page-head-text">
				<h1>Accounts</h1>
				<p class="page-head-sub">
					Search by account name or email. Lookups match the case-insensitive name column, so
					casing never changes the result. Banned accounts are left out until you ask for them.
				</p>
			</div>
		</div>

		<section class="card">
			<div class="toolbar">
				<input class="search" id="q" type="search" placeholder="Account name or email…" autocomplete="off" />
				<select id="level">
					<option value="">Any access level</option>
					<option value="0">Banned</option>
					<option value="1">Player</option>
					<option value="2">Game Master</option>
					<option value="3">Admin</option>
				</select>
				<label class="check-row" style="margin:0">
					<input type="checkbox" id="banned" />
					<span>Include banned</span>
				</label>
				<span class="grow"></span>
				<span class="small muted" id="count"></span>
			</div>
			<div class="card-body flush" id="results">${ui.loading()}</div>
			<footer class="card-foot" id="pager"></footer>
		</section>`;

	const results = host.querySelector('#results');
	const pagerHost = host.querySelector('#pager');
	const countHost = host.querySelector('#count');

	async function load() {
		results.innerHTML = ui.loading();
		const page = await api.searchAccounts(filters);
		countHost.textContent = `${ui.num(page.totalCount)} matching`;

		results.innerHTML = page.items.length
			? ui.table({
				columns: [
					{
						label: 'Account',
						cell: (a) => `
							<div class="row">
								<span class="avatar avatar-sm ${ui.avatarClass(a.name)}">${ui.esc(ui.initials(a.name))}</span>
								<span>
									<span class="cell-primary">${ui.esc(a.name)}</span>
									<span class="cell-sub" style="display:block">${a.verified ? 'verified' : 'unverified'} · two-factor ${a.totpEnabled ? 'on' : 'off'}</span>
								</span>
							</div>`,
					},
					{ label: 'Email', cell: (a) => `<span class="cell-sub">${ui.esc(a.email)}</span>` },
					{ label: 'Access level', cell: (a) => ui.levelBadge(a.accessLevel) },
					{ label: 'Characters', align: 'right', cell: (a) => `<span class="tnum">${ui.num(a.characterCount)}</span>` },
					{
						label: 'Last sign-in',
						cell: (a) => `<span class="nowrap" title="${ui.esc(ui.dateTime(a.lastLogin))}">${ui.ago(a.lastLogin)}</span>`,
					},
					{
						label: '',
						align: 'right',
						cell: (a) => `<a class="btn btn-sm" href="#/support/accounts/${encodeURIComponent(a.name)}">Inspect</a>`,
					},
				],
				rows: page.items,
			})
			: ui.empty('No account matches', 'Nothing matches that search. Clear the access level, or tick "include banned" — a banned account is hidden by default.', 'search');

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
	host.querySelector('#level').addEventListener('change', (e) => {
		filters.accessLevel = e.target.value;
		filters.page = 1;
		load();
	});
	host.querySelector('#banned').addEventListener('change', (e) => {
		filters.includeBanned = e.target.checked;
		filters.page = 1;
		load();
	});

	await load();
}

/* ── Detail ──────────────────────────────────────────────────── */

async function renderDetail(host, ctx, name) {
	const { api, ui } = ctx;
	const account = await api.getAccount(name);
	const isAdmin = ctx.session.accessLevel >= 3;
	/* Compared case-insensitively because the name column is: "Bob" and "bob" are one
	 * account, and an operator must not be able to reach their own by changing the casing
	 * in the address bar. */
	const isSelf = String(ctx.session.username).toLowerCase() === String(account.name).toLowerCase();
	const banned = account.accessLevel === 0;
	const characters = account.characters ?? [];
	const online = characters.filter((c) => c.online).length;
	const selfHint = 'This is your own account. The server refuses moderation against yourself, so the panel does not pretend to offer it.';

	host.innerHTML = `
		<div class="page-head">
			<div class="page-head-text">
				<div class="row" style="margin-bottom:var(--sp-2)">
					<a class="small muted" href="#/support/accounts">← All accounts</a>
				</div>
				<div class="row">
					<span class="avatar avatar-lg ${ui.avatarClass(account.name)}">${ui.esc(ui.initials(account.name))}</span>
					<div>
						<h1>${ui.esc(account.name)}</h1>
						<div class="page-head-sub">${ui.esc(account.email)}</div>
					</div>
				</div>
			</div>
			<div class="page-head-actions">
				${ui.levelBadge(account.accessLevel)}
				${online ? ui.statusBadge(`${online} in world`, 'ok', true) : ui.badge('nobody online')}
			</div>
		</div>

		<div class="stack-lg">
			${banned
				? ui.banner('danger', 'This account is banned',
					'Level 0 is the ban. It can authenticate nowhere — game client or panel — until somebody lifts it. Unbanning restores Player level and nothing else.')
				: ''}
			${isSelf ? ui.banner('info', 'This is your own account', selfHint) : ''}

			<div class="split">
				<div class="stack">
					${ui.card({
						title: `Characters (${ui.num(characters.length)})`,
						sub: 'Every character on this account, including deleted ones.',
						flush: true,
						body: characters.length
							? ui.table({
								columns: [
									{
										label: 'Character',
										cell: (c) => `
											<div class="row">
												<span class="avatar avatar-sm ${ui.avatarClass(c.name)}">${ui.esc(ui.initials(c.name))}</span>
												<span>
													<span class="cell-primary">${ui.esc(c.name)}</span>
													<span class="cell-sub" style="display:block">race ${ui.num(c.raceId)}</span>
												</span>
											</div>`,
									},
									{ label: 'State', cell: (c) => c.deleted
										? ui.badge('deleted', 'danger')
										: c.online ? ui.statusBadge('in world', 'ok', true) : ui.badge('offline') },
									{ label: 'Access level', cell: (c) => ui.levelBadge(c.accessLevel) },
									{ label: 'Last saved', cell: (c) => `<span class="nowrap">${ui.ago(c.lastSavedUtc)}</span>` },
									{
										label: '',
										align: 'right',
										cell: (c) => `
											<div class="cell-actions">
												<a class="btn btn-sm" href="#/support/characters/${encodeURIComponent(c.id)}">Inspect</a>
												${isAdmin ? `<a class="btn btn-sm" href="#/admin/characters/${encodeURIComponent(c.id)}">Edit</a>` : ''}
											</div>`,
									},
								],
								rows: characters,
							})
							: ui.empty('No characters', 'This account has never created one, or every one it had is gone.', 'sword'),
					})}

					${actionsCard(ui, { account, banned, isSelf, online, selfHint })}

					${isAdmin ? accessLevelCard(ui, { account, isSelf, selfHint }) : ''}
				</div>

				<div class="stack">
					${ui.card({
						title: 'Identity',
						body: `
							<dl class="dl">
								<dt>Account</dt><dd>${ui.esc(account.name)}</dd>
								<dt>Email</dt><dd>${ui.esc(account.email)}</dd>
								<dt>Verified</dt><dd>${account.verified ? ui.badge('yes', 'ok') : ui.badge('no', 'warn')}</dd>
								<dt>Age</dt><dd class="tnum">${account.age === null || account.age === undefined ? '<span class="faint">—</span>' : ui.num(account.age)}</dd>
								<dt>Access level</dt><dd>${ui.levelBadge(account.accessLevel)}</dd>
								<dt>Two-factor</dt><dd>${account.totpEnabled ? ui.badge('enrolled', 'ok') : ui.badge('not enrolled', 'warn')}</dd>
								<dt>Enrolled</dt><dd>${account.totpVerifiedAt ? ui.dateTime(account.totpVerifiedAt) : '<span class="faint">—</span>'}</dd>
								<dt>Created</dt><dd>${ui.dateTime(account.created)}</dd>
								<dt>Last sign-in</dt><dd>${ui.dateTime(account.lastLogin)}</dd>
								<dt>Characters</dt><dd class="tnum">${ui.num(account.characterCount ?? characters.length)}</dd>
							</dl>`,
						foot: 'The SRP salt and verifier are never returned to this page, and neither is the authenticator secret.',
					})}
				</div>
			</div>
		</div>`;

	wireActions(host, ctx, account);
}

/**
 * One row of the actions card.
 *
 * The consequences of these four differ from one another in ways that matter — one is
 * recoverable by signing in again, one removes a security control — so each says what it
 * does beside its button, rather than hiding it in a tooltip nobody hovers.
 */
function actionRow(ui, { act, label, tone = '', title, text, disabled = false, hint = '' }) {
	return `
		<div class="row-between">
			<div>
				<strong>${ui.esc(title)}</strong>
				<div class="small muted">${ui.esc(text)}</div>
				${hint ? `<div class="field-hint">${ui.esc(hint)}</div>` : ''}
			</div>
			<button class="btn btn-sm${tone ? ' btn-' + ui.esc(tone) : ''}" type="button"
				data-act="${ui.esc(act)}" ${disabled ? 'disabled' : ''}>${ui.esc(label)}</button>
		</div>`;
}

function actionsCard(ui, { account, banned, isSelf, online, selfHint }) {
	return ui.card({
		title: 'Actions',
		sub: 'Each one asks for a reason, and the reason is what lands in the audit log.',
		body: `
			<div class="stack">
				${actionRow(ui, {
					act: 'kick',
					label: 'Kick',
					title: 'Kick from the game',
					text: 'Writes a kick request the game servers act on. Every character on this account is disconnected; the account can sign straight back in.',
					disabled: isSelf || !online,
					hint: isSelf ? selfHint : !online ? 'Nobody on this account is in the world, so there is nothing to disconnect.' : '',
				})}

				${banned
					? actionRow(ui, {
						act: 'unban',
						label: 'Unban',
						tone: 'primary',
						title: 'Lift the ban',
						text: 'Restores Player level, and only that. The tokens and panel sessions the ban revoked stay revoked — the player signs in again.',
						disabled: isSelf,
						hint: isSelf ? selfHint : '',
					})
					: actionRow(ui, {
						act: 'ban',
						label: 'Ban',
						tone: 'danger',
						title: 'Ban the account',
						text: 'Sets the level to Banned, revokes every game token, revokes every panel session, and writes a kick request — in one transaction, all of it or none of it.',
						disabled: isSelf,
						hint: isSelf ? selfHint : '',
					})}

				${actionRow(ui, {
					act: 'revoke',
					label: 'Revoke',
					title: 'Revoke tokens',
					text: 'Forces re-authentication everywhere: every game client and every panel session on this account has to sign in again. The account itself is untouched.',
					disabled: isSelf,
					hint: isSelf ? selfHint : '',
				})}

				${actionRow(ui, {
					act: 'reset2fa',
					label: 'Reset',
					tone: 'danger',
					title: 'Reset two-factor',
					text: account.totpEnabled
						? 'Clears the authenticator secret AND every recovery code. The player must enrol again from scratch on their next sign-in.'
						: 'Nothing is enrolled on this account right now. Resetting clears any half-finished enrolment and its recovery codes.',
					disabled: isSelf,
					hint: isSelf
						? selfHint
						: 'This one removes a security control rather than restoring one: until the player enrols again, their password is all that stands in front of the account. Confirm the request came from them.',
				})}
			</div>`,
		foot: 'Everything except Kick needs a fresh code from your authenticator before it is applied.',
	});
}

/**
 * Access level is admin-only because the rules the server enforces are about the actor's
 * own level, and an operator who cannot satisfy them should not be shown the control.
 */
function accessLevelCard(ui, { account, isSelf, selfHint }) {
	return ui.card({
		title: 'Access level',
		sub: 'Promotion and demotion. The server checks these rules again and refuses anything that breaks them.',
		body: `
			<div class="field">
				<label for="lvl">New access level</label>
				<select id="lvl" ${isSelf ? 'disabled' : ''}>
					${[0, 1, 2, 3].map((l) => `<option value="${l}"${account.accessLevel === l ? ' selected' : ''}>${ui.esc(ui.levelName(l))}</option>`).join('')}
				</select>
				<div class="field-hint">
					You cannot change your own level, you cannot act on an account at or above your own,
					and you cannot grant a level at or above your own. Setting Banned here bans the
					account but revokes nothing — use Ban for that.
				</div>
				${isSelf ? `<div class="field-hint">${ui.esc(selfHint)}</div>` : ''}
			</div>
			<div>
				<button class="btn btn-primary" type="button" data-act="level" ${isSelf ? 'disabled' : ''}>Apply</button>
			</div>`,
	});
}

function wireActions(host, ctx, account) {
	const { api, ui } = ctx;
	const on = (act, fn) => host.querySelector(`[data-act="${act}"]`)?.addEventListener('click', fn);

	/* The reason is mandatory: `onSubmit` returning false leaves the dialog open with the
	 * operator's typing intact, rather than sending an action nobody can explain later. */
	const askReason = (title, sub, confirmLabel, tone = 'primary') =>
		ui.modal({
			title,
			sub,
			confirmLabel,
			confirmTone: tone,
			body: `
				<div class="field">
					<label for="reason">Reason (recorded in the audit log)</label>
					<textarea id="reason" name="reason" required placeholder="Why is this being done?"></textarea>
				</div>`,
			onSubmit: (values) => (String(values.reason).trim() ? values : false),
		});

	on('kick', async () => {
		const v = await askReason(
			'Kick from the game',
			`Disconnects every character on "${account.name}". Nothing is revoked and nothing is barred: the account can sign back in immediately.`,
			'Kick',
		);
		if (!v) return;
		if (await ctx.attempt(() => api.kickAccount(account.name, v.reason), 'Kick request written', 'The game servers act on it on their next poll.')) {
			ctx.refresh();
		}
	});

	on('ban', async () => {
		const done = await ui.confirmDestructive({
			title: 'Ban this account',
			sub: 'Sets the access level to Banned, revokes every game token, revokes every panel session, and writes a kick request. It is one transaction: all of it lands, or none of it does.',
			targetName: account.name,
			confirmLabel: 'Ban account',
			extraFields: ui.banner('danger', 'The player is locked out everywhere',
				'A banned account authenticates nowhere — game client or panel. Unbanning later restores Player level only; the tokens and sessions revoked here are not given back.'),
			onSubmit: (values) => ctx.withStepUp(() => api.banAccount(account.name, values.reason)),
		});
		if (done) {
			ui.toast('Account banned', done.message ?? '', 'ok');
			ctx.refresh();
		}
	});

	on('unban', async () => {
		const v = await askReason(
			'Lift the ban',
			`"${account.name}" returns to Player level. The tokens and panel sessions the ban revoked are NOT restored — the player signs in again.`,
			'Unban',
		);
		if (!v) return;
		if (await ctx.attempt(() => api.unbanAccount(account.name, v.reason), 'Ban lifted', 'The account is at Player level and must sign in again.')) {
			ctx.refresh();
		}
	});

	on('revoke', async () => {
		const v = await askReason(
			'Revoke tokens',
			'Forces re-authentication everywhere: every game client and every panel session on this account signs in again. The account keeps its level and its two-factor enrolment.',
			'Revoke tokens',
		);
		if (!v) return;
		await ctx.attempt(() => api.revokeTokens(account.name, v.reason), 'Tokens revoked', 'Everything signed in on this account has to authenticate again.');
	});

	on('reset2fa', async () => {
		const done = await ui.confirmDestructive({
			title: 'Reset two-factor',
			sub: 'Clears the authenticator secret and every recovery code. The player must enrol again from scratch the next time they sign in.',
			targetName: account.name,
			confirmLabel: 'Reset two-factor',
			extraFields: ui.banner('danger', 'This removes a security control, it does not restore one',
				'Until the player enrols again, their password is the only thing in front of this account — so somebody who already has that password is exactly who benefits from this being done. Confirm out of band that the request came from the account holder.'),
			onSubmit: (values) => ctx.withStepUp(() => api.resetTwoFactor(account.name, values.reason)),
		});
		if (done) {
			ui.toast('Two-factor reset', done.message ?? 'The player must enrol again on their next sign-in.', 'ok');
			ctx.refresh();
		}
	});

	on('level', async () => {
		const level = Number(host.querySelector('#lvl').value);
		if (level === account.accessLevel) {
			ui.toast('Nothing to change', `"${account.name}" is already ${ui.levelName(level)}.`, 'warn');
			return;
		}
		const v = await askReason(
			'Change access level',
			`"${account.name}" becomes ${ui.levelName(level)}, from ${ui.levelName(account.accessLevel)}.`,
			'Apply',
			level === 0 ? 'danger' : 'primary',
		);
		if (!v) return;
		if (await ctx.attempt(() => api.setAccessLevel(account.name, level, v.reason), 'Access level changed')) ctx.refresh();
	});
}
