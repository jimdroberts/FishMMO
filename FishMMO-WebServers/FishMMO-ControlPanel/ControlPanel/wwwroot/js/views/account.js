/*
 * My account — profile, credentials, two-factor and sessions.
 *
 * Everything here talks to the real backend. Neither the old password nor the new one
 * is ever transmitted: a password change proves the current password with an SRP
 * exchange and sends the new salt and verifier derived in this browser.
 */

import * as srp from '../srp.js';

const TABS = [
	['profile', 'Profile'],
	['security', 'Security'],
	['sessions', 'Sessions'],
];

/** The base32 key an authenticator asks for when it is typed in rather than scanned. */
function setupSecret(otpauthUri) {
	try {
		return new URL(otpauthUri).searchParams.get('secret') ?? otpauthUri;
	} catch {
		return otpauthUri;
	}
}

/**
 * The plain-text form of everything that is shown once.
 *
 * Written out rather than dumped, because the person reading it later will be locked out
 * and in a hurry, and a bare list of codes does not say what they are for.
 */
function recoveryText(username, otpauthUri, codes) {
	const secret = otpauthUri ? setupSecret(otpauthUri) : null;
	return [
		'FishMMO account recovery information',
		`Account: ${username}`,
		`Saved:   ${new Date().toISOString()}`,
		'',
		...(secret ? [`Authenticator key: ${secret}`, `Enrolment URI:     ${otpauthUri}`, ''] : []),
		...((codes ?? []).length ? ['Recovery codes (each one works once):', ...codes.map((c) => `  ${c}`), ''] : []),
		'Keep this somewhere only you can reach. Anyone holding these can sign in as you.',
	].join('\n');
}

export async function render(host, ctx) {
	const { api, ui } = ctx;
	let tab = 'profile';
	let account = await api.getMyAccount();

	async function draw() {
		host.innerHTML = `
			<div class="page-head">
				<div class="page-head-text">
					<h1>My account</h1>
					<p class="page-head-sub">
						Self-service for the account you are signed in with. This is the same account
						the game client authenticates, and changes here apply to both.
					</p>
				</div>
			</div>
			<div class="tabs">
				${TABS.map(([key, label]) => `
					<button class="tab${tab === key ? ' is-active' : ''}" data-tab="${key}">${ui.esc(label)}</button>`).join('')}
			</div>
			<div id="tab-body">${ui.loading()}</div>`;

		host.querySelectorAll('[data-tab]').forEach((b) =>
			b.addEventListener('click', () => {
				tab = b.dataset.tab;
				draw();
			}),
		);

		const body = host.querySelector('#tab-body');
		if (tab === 'profile') await drawProfile(body);
		else if (tab === 'security') await drawSecurity(body);
		else await drawSessions(body);
	}

	async function reload() {
		account = await api.getMyAccount();
		await draw();
	}

	/* ── Profile ─────────────────────────────────────────────── */

	async function drawProfile(body) {
		body.innerHTML = `
			<div class="split">
				<div class="stack">
					${ui.card({
						title: 'Details',
						body: `
							<dl class="dl">
								<dt>Account name</dt><dd>${ui.esc(account.name)}</dd>
								<dt>Access level</dt><dd>${ui.levelBadge(account.accessLevel)}</dd>
								<dt>Email</dt><dd>${ui.esc(account.email ?? '—')} ${account.verified ? ui.badge('verified', 'ok') : ui.badge('unverified', 'warn')}</dd>
								<dt>Age</dt><dd>${account.age || '—'}</dd>
								<dt>Two-factor</dt><dd>${account.totpEnabled ? ui.badge('enabled', 'ok') : ui.badge('not set up', 'warn')}</dd>
								<dt>Enrolled</dt><dd>${account.totpVerifiedAtUtc ? ui.dateTime(account.totpVerifiedAtUtc) : '<span class="faint">—</span>'}</dd>
								<dt>Last sign-in</dt><dd>${account.lastLoginUtc ? ui.dateTime(account.lastLoginUtc) : '<span class="faint">—</span>'}</dd>
							</dl>`,
					})}
					${ui.card({
						title: 'Email address',
						sub: 'Changing it marks the account unverified and sends a new code to the new address.',
						body: `
							<form id="email-form" class="stack">
								<div class="field">
									<label for="email">Email</label>
									<input id="email" name="email" type="email" value="${ui.esc(account.email ?? '')}" required />
								</div>
								<div id="email-result"></div>
								<div><button class="btn btn-primary" type="submit">Change email</button></div>
							</form>`,
					})}
				</div>
				<div class="stack">
					${ui.card({
						title: 'Access level',
						body: `
							<p class="small muted">
								Your level decides which pages exist for you. It is set by an operator, never
								self-service.
							</p>
							<ul class="timeline" style="margin-top:var(--sp-3)">
								<li class="${account.accessLevel >= 1 ? 'is-ok' : ''}"><div class="timeline-title">Player</div><div class="timeline-meta">Own account and characters</div></li>
								<li class="${account.accessLevel >= 2 ? 'is-ok' : ''}"><div class="timeline-title">Game Master</div><div class="timeline-meta">Support, moderation, chat log</div></li>
								<li class="${account.accessLevel >= 3 ? 'is-ok' : ''}"><div class="timeline-title">Admin</div><div class="timeline-meta">Servers, supervisor, editing, platform</div></li>
							</ul>`,
					})}
				</div>
			</div>`;

		body.querySelector('#email-form').addEventListener('submit', async (e) => {
			e.preventDefault();
			const email = new FormData(e.target).get('email');
			const resultHost = body.querySelector('#email-result');
			resultHost.innerHTML = '';
			try {
				const result = await api.changeMyEmail(email);
				resultHost.innerHTML = ui.banner('ok', result.message ?? 'Email changed.');
				await reload();
			} catch (err) {
				resultHost.innerHTML = ui.banner('danger', err.message || 'That email could not be saved.');
			}
		});
	}

	/* ── Security ────────────────────────────────────────────── */

	async function drawSecurity(body) {
		const mustKeepTwoFactor = account.accessLevel >= 2;

		body.innerHTML = `
			<div class="split">
				<div class="stack">
					${ui.card({
						title: 'Password',
						sub: 'Signs out every other browser and every game client.',
						body: `
							<form id="pw-form" class="stack">
								<div class="field">
									<label for="current">Current password</label>
									<input id="current" name="current" type="password" autocomplete="current-password" required />
								</div>
								<div class="field-row">
									<div class="field">
										<label for="next">New password</label>
										<input id="next" name="next" type="password" autocomplete="new-password" required />
										<div class="field-hint" id="pw-hint"></div>
									</div>
									<div class="field">
										<label for="confirm">Confirm new password</label>
										<input id="confirm" name="confirm" type="password" autocomplete="new-password" required />
									</div>
								</div>
								<div id="pw-result"></div>
								<div><button class="btn btn-primary" type="submit">Change password</button></div>
							</form>`,
						foot: 'Neither password leaves this browser. The current one is proved with an SRP exchange, and the new one becomes a salt and a verifier before anything is sent.',
					})}
				</div>
				<div class="stack">
					${ui.card({
						title: 'Two-factor authentication',
						body: account.totpEnabled
							? `
								${ui.banner('ok', 'Two-factor is enabled',
									account.totpVerifiedAtUtc ? `Confirmed ${ui.ago(account.totpVerifiedAtUtc)}.` : '')}
								<div class="row" style="margin-top:var(--sp-4);flex-wrap:wrap">
									<button class="btn" data-act="codes">New recovery codes</button>
									<button class="btn" data-act="reenrol">Re-enrol authenticator</button>
									${mustKeepTwoFactor
										? ''
										: '<button class="btn btn-danger" data-act="disable">Disable two-factor</button>'}
								</div>
								${mustKeepTwoFactor
									? `<p class="small muted" style="margin-top:var(--sp-3)">
										Two-factor is required at ${ui.esc(ui.levelName(account.accessLevel))} and cannot be turned off.
									</p>`
									: ''}`
							: `
								${ui.banner(mustKeepTwoFactor ? 'danger' : 'warn',
									mustKeepTwoFactor
										? 'Two-factor is required at your access level'
										: 'Two-factor is not set up',
									mustKeepTwoFactor
										? 'Until it is enrolled you can reach only your own account pages.'
										: 'Strongly recommended, and required if you are ever promoted.')}
								<div style="margin-top:var(--sp-4)">
									<button class="btn btn-primary" data-act="setup">Set up two-factor</button>
								</div>`,
					})}
				</div>
			</div>`;

		// Show the server's password rules as the user types, entirely locally.
		let policy = null;
		try {
			policy = await api.getAccountPolicy();
		} catch {
			/* the server validates on submit regardless */
		}
		const hint = body.querySelector('#pw-hint');
		body.querySelector('#next').addEventListener('input', (e) => {
			if (!policy) return;
			const v = e.target.value;
			const bad = v && (v.length < policy.password.minLength || v.length > policy.password.maxLength ||
				!new RegExp(policy.password.pattern).test(v));
			hint.textContent = bad ? policy.password.error : '';
		});

		body.querySelector('#pw-form').addEventListener('submit', async (e) => {
			e.preventDefault();
			const v = Object.fromEntries(new FormData(e.target).entries());
			const resultHost = body.querySelector('#pw-result');
			resultHost.innerHTML = '';

			if (v.next !== v.confirm) {
				resultHost.innerHTML = ui.banner('warn', 'Those two new passwords do not match.');
				return;
			}
			if (policy) {
				const bad = v.next.length < policy.password.minLength || v.next.length > policy.password.maxLength ||
					!new RegExp(policy.password.pattern).test(v.next);
				if (bad) {
					resultHost.innerHTML = ui.banner('warn', policy.password.error);
					return;
				}
			}

			const buttons = e.target.querySelectorAll('button, input');
			buttons.forEach((el) => (el.disabled = true));
			try {
				/* Prove the CURRENT password with a real SRP exchange, then derive a brand-new
				 * salt and verifier for the new one. The server sees a proof and two derived
				 * values; it never sees either password. */
				const challenge = await api.srpChallenge(account.name);
				const currentKey = await srp.derivePrivateKey(challenge.salt, account.name, v.current);
				const ephemeral = srp.generateEphemeral();
				const session = await srp.deriveSession(
					ephemeral.secret, challenge.serverPublicEphemeral, challenge.salt, account.name, currentKey);

				const newSalt = srp.generateSalt();
				const newKey = await srp.derivePrivateKey(newSalt, account.name, v.next);
				const newVerifier = await srp.deriveVerifier(newKey);

				const result = await api.changeMyPassword({
					handle: challenge.handle,
					clientPublicEphemeral: session.publicEphemeral,
					clientProof: session.proof,
					newSalt,
					newVerifier,
				});

				e.target.reset();
				buttons.forEach((el) => (el.disabled = false));
				resultHost.innerHTML = ui.banner('ok', result.message ?? 'Password changed.',
					result.revokedSessions ? `${result.revokedSessions} other session(s) signed out.` : '');

				if (result.signedOut) {
					ui.toast('Sign in again', 'Your session could not be renewed.', 'warn');
				}
			} catch (err) {
				buttons.forEach((el) => (el.disabled = false));
				resultHost.innerHTML = ui.banner('danger', err.message || 'That password could not be changed.');
			}
		});

		body.querySelector('[data-act="setup"]')?.addEventListener('click', () => runEnrolment('Set up two-factor'));
		body.querySelector('[data-act="reenrol"]')?.addEventListener('click', () => runEnrolment('Re-enrol your authenticator'));
		body.querySelector('[data-act="codes"]')?.addEventListener('click', regenerateCodes);

		body.querySelector('[data-act="disable"]')?.addEventListener('click', async () => {
			const done = await ui.confirmDestructive({
				title: 'Disable two-factor',
				sub: 'Your recovery codes are invalidated at the same time.',
				targetName: account.name,
				confirmLabel: 'Disable',
				onSubmit: () => ctx.withStepUp(() => api.disableTwoFactor()),
			});
			if (done) {
				ui.toast('Two-factor disabled', '', 'warn');
				await reload();
			}
		});
	}

	/**
	 * Enrolment: generate a secret, show it once with fresh recovery codes, and enable
	 * only after a code from the new authenticator verifies.
	 */
	async function runEnrolment(title) {
		let setup;
		try {
			setup = await api.beginTwoFactorSetup();
		} catch (err) {
			ui.toast('Could not start enrolment', err.message || '', 'danger');
			return;
		}

		const done = await ui.modal({
			title,
			sub: 'Scan the code with your authenticator, then confirm with the six digits it shows.',
			confirmLabel: 'Confirm and enable',
			wide: true,
			body: `
				<div class="split" style="grid-template-columns:auto 1fr">
					<div>${ui.qrCode(setup.otpauthUri, { label: 'Authenticator enrolment code' })}</div>
					<div class="stack">
						<div>
							<label>Or enter this key by hand</label>
							<code style="display:block;overflow-wrap:anywhere;padding:var(--sp-2);letter-spacing:.05em">${ui.esc(setupSecret(setup.otpauthUri))}</code>
						</div>
						<div class="field">
							<label for="tf-code">Code from your authenticator</label>
							<input id="tf-code" name="code" inputmode="numeric" maxlength="8" required placeholder="000000" />
						</div>
					</div>
				</div>
				<div style="margin-top:var(--sp-4)">
					${ui.banner('warn', 'Save your recovery codes now', 'They are shown once and are not retrievable. Each one signs you in a single time if you lose your authenticator.')}
					<div style="display:grid;grid-template-columns:repeat(auto-fill,minmax(150px,1fr));gap:var(--sp-2);margin-top:var(--sp-3)">
						${(setup.recoveryCodes ?? []).map((c) => `<code class="center">${ui.esc(c)}</code>`).join('')}
					</div>
					<div class="row" style="gap:var(--sp-2);margin-top:var(--sp-3)">
						<button class="btn btn-sm" type="button" data-act="download-setup">${ui.icon('download')} Download</button>
						<button class="btn btn-sm" type="button" data-act="copy-setup">${ui.icon('copy')} Copy</button>
					</div>
				</div>`,
			onSubmit: async (values) => {
				await api.confirmTwoFactorSetup(values.code);
				return true;
			},
			// The buttons live inside the dialog, so they are wired once it exists.
			onReady: (form) => {
				const text = recoveryText(ctx.session.username, setup.otpauthUri, setup.recoveryCodes);
				form.querySelector('[data-act="copy-setup"]')?.addEventListener('click', () => ui.copyToClipboard(text));
				form.querySelector('[data-act="download-setup"]')?.addEventListener('click', () =>
					ui.downloadText(`fishmmo-recovery-${ctx.session.username}.txt`, text));
			},
		});

		if (done) {
			ui.toast('Two-factor enabled', '', 'ok');
			await reload();
		} else {
			// Abandoning after the secret was stored leaves the account with a secret it has
			// not confirmed. That is harmless — two-factor is still off — but say so.
			ui.toast('Enrolment not completed', 'Two-factor is unchanged.', 'warn');
		}
	}

	async function regenerateCodes() {
		const result = await ctx.attempt(
			() => api.regenerateRecoveryCodes(), 'New recovery codes issued', 'The old ones no longer work.');
		if (!result) return;
		const codes = result.recoveryCodes ?? [];
		await ui.modal({
			title: 'Recovery codes',
			sub: 'Shown once and not retrievable. Keep them where only you can reach.',
			confirmLabel: 'Done',
			body: `
				<div style="display:grid;grid-template-columns:repeat(auto-fill,minmax(150px,1fr));gap:var(--sp-2)">
					${codes.map((c) => `<code class="center">${ui.esc(c)}</code>`).join('')}
				</div>
				<div class="row" style="gap:var(--sp-2);margin-top:var(--sp-3)">
					<button class="btn btn-sm" type="button" data-act="download-codes">${ui.icon('download')} Download</button>
					<button class="btn btn-sm" type="button" data-act="copy-codes">${ui.icon('copy')} Copy</button>
				</div>`,
			onReady: (form) => {
				const text = recoveryText(ctx.session.username, null, codes);
				form.querySelector('[data-act="copy-codes"]')?.addEventListener('click', () => ui.copyToClipboard(text));
				form.querySelector('[data-act="download-codes"]')?.addEventListener('click', () =>
					ui.downloadText(`fishmmo-recovery-${ctx.session.username}.txt`, text));
			},
		});
	}

	/* ── Sessions ────────────────────────────────────────────── */

	async function drawSessions(body) {
		const sessions = await api.getMySessions();

		body.innerHTML = ui.card({
			title: 'Panel sessions',
			sub: 'Browsers that are signed in to this control panel.',
			flush: true,
			body: ui.table({
				columns: [
					{
						label: 'Browser',
						cell: (s) => `
							<div class="cell-primary">${ui.esc(s.userAgent || 'Unknown')}</div>
							<div class="cell-sub">${ui.esc(s.ipAddress || '—')}</div>`,
					},
					{ label: 'Started', cell: (s) => ui.ago(s.createdUtc) },
					{ label: 'Last seen', cell: (s) => ui.ago(s.lastSeenUtc) },
					{ label: 'Expires', cell: (s) => ui.ago(s.expiresUtc) },
					{
						label: '',
						align: 'right',
						cell: (s) => s.current
							? ui.badge('this browser', 'accent')
							: `<button class="btn btn-sm" data-revoke="${s.id}">Revoke</button>`,
					},
				],
				rows: sessions,
				emptyText: 'No other sessions.',
			}),
			foot: 'Changing your password signs out every session here and every game client.',
		});

		body.querySelectorAll('[data-revoke]').forEach((b) =>
			b.addEventListener('click', async () => {
				const done = await ctx.attempt(() => api.revokeMySession(b.dataset.revoke), 'Session revoked');
				if (done) draw();
			}),
		);
	}

	await draw();
}
