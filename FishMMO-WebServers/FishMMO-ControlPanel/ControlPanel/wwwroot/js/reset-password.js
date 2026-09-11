/*
 * Account recovery: setting a new password from a code emailed to the address on the
 * account.
 *
 * ── What this recovers, and what it deliberately does not ───────────────────
 *
 * There are two ways to be locked out, and they have different answers.
 *
 *   Lost password       → this page. A code goes to the address on the account.
 *   Lost authenticator  → one of the eight recovery codes issued at registration,
 *                         entered at the two-factor step of an ordinary sign-in.
 *
 * They compose rather than replace each other, and that is the point. **Completing a
 * reset here does not satisfy two-factor.** After setting a new password you still have
 * to pass the authenticator, or spend a recovery code, to sign in. If an emailed code
 * could also clear two-factor, then anyone who reached the mailbox would own the account
 * outright and the second factor would be protecting nothing.
 *
 * Somebody who has lost the password AND all eight recovery codes cannot be recovered
 * from this screen by design, and no amount of clicking should suggest otherwise. That
 * is a support ticket, where a human decides.
 *
 * ── The password still never leaves the browser ─────────────────────────────
 *
 * Exactly as in registration and in an ordinary password change: a fresh SRP salt is
 * generated here, the verifier is derived here, and only those two values are sent. The
 * server has no route that accepts a password, so a reset cannot introduce one.
 *
 * Deriving a verifier needs the account name, because the name is an input to the key
 * derivation — so the code is exchanged for its account name first. Holding a valid code
 * already proves control of the mailbox, which is a stronger claim than knowing the name.
 */

import { api } from './api.js';
import * as srp from './srp.js';
import * as ui from './ui.js';

/** The server's own rules, so the new password is held to the same bar as registration. */
let policy = null;

async function loadPolicy() {
	if (policy) return policy;
	policy = await api.getAccountPolicy();
	return policy;
}

/** Applies one field's rule locally. Returns an error string, or null. */
function checkField(rule, value) {
	if (!rule || !value) return null;
	if (rule.minLength && value.length < rule.minLength) return rule.error;
	if (rule.maxLength && value.length > rule.maxLength) return rule.error;
	if (rule.pattern && !new RegExp(rule.pattern).test(value)) return rule.error;
	return null;
}

function card(icon, title, bodyHtml, footer) {
	return `
		<div class="signin-card">
			<div class="signin-brand">
				<span style="color:var(--accent)">${ui.icon(icon, 26)}</span>
				<div>
					<div style="font-size:var(--fs-lg);font-weight:600;color:var(--text-strong)">${ui.esc(title)}</div>
					<div class="xsmall muted" style="text-transform:uppercase;letter-spacing:0.07em">FishMMO</div>
				</div>
			</div>
			<div id="reset-error"></div>
			${bodyHtml}
			<div class="signin-footer">${footer}</div>
		</div>`;
}

/**
 * Renders the recovery flow into a host element.
 * @param {HTMLElement} host Where to render.
 * @param {object} options `onCancel` returns to sign-in; `onDone` is called after success.
 */
export function renderResetPassword(host, { onCancel, onDone }) {
	renderRequest(host, { onCancel, onDone });
}

/* ── Step one: ask for the code ──────────────────────────────────────────── */

function renderRequest(host, ctx) {
	host.innerHTML = card('mail', 'Reset your password', `
		<p class="small muted" style="margin:0 0 var(--sp-4)">
			Enter the email address on the account. If it is registered, a one-time code will be
			sent to it.
		</p>
		<form id="reset-form" class="stack" novalidate>
			<div class="field">
				<label for="reset-email">Email address</label>
				<input id="reset-email" name="email" type="email" autocomplete="email"
					autocapitalize="none" spellcheck="false" required />
			</div>
			<button class="btn btn-primary btn-block" type="submit">Send the code</button>
			<button class="btn btn-ghost btn-block" type="button" id="reset-have-code">I already have a code</button>
			<button class="btn btn-ghost btn-block" type="button" id="reset-cancel">Back to sign in</button>
		</form>`,
		`Lost your authenticator instead? Sign in as usual and choose <strong>Use a recovery
		 code</strong> at the two-factor step. Lost both? Open a support ticket — nothing on
		 this page can recover an account without one of them.`);

	const form = host.querySelector('#reset-form');
	const errorHost = host.querySelector('#reset-error');
	const showError = (message) => {
		errorHost.innerHTML = ui.banner('danger', message);
		errorHost.style.marginBottom = 'var(--sp-4)';
	};
	const setBusy = (on) => form.querySelectorAll('button, input').forEach((el) => (el.disabled = on));

	host.querySelector('#reset-cancel').addEventListener('click', ctx.onCancel);
	host.querySelector('#reset-have-code').addEventListener('click', () => renderRedeem(host, ctx, null));

	form.addEventListener('submit', async (e) => {
		e.preventDefault();
		errorHost.innerHTML = '';
		const email = new FormData(form).get('email')?.trim();
		if (!email) return showError('An email address is required.');

		setBusy(true);
		try {
			/* The server answers the same way whether or not that address is registered, so
			 * there is nothing here to report back beyond "we have taken the request". Saying
			 * "no such account" would turn this box into a membership oracle. */
			const result = await api.requestPasswordReset(email);
			renderRedeem(host, ctx, result?.devCode ?? null);
		} catch (err) {
			setBusy(false);
			showError(err.message || 'That request could not be sent.');
		}
	});

	host.querySelector('#reset-email').focus();
}

/* ── Step two: redeem the code and set a new password ────────────────────── */

function renderRedeem(host, ctx, devCode) {
	host.innerHTML = card('key', 'Enter your code', `
		<p class="small muted" style="margin:0 0 var(--sp-4)">
			If that address is registered, a code is on its way. It is good for one use and
			expires shortly, so paste it here and choose a new password.
		</p>
		${devCode ? ui.banner('warn', 'Development build: the code is shown below.',
			'No mail was sent. This never happens in production.') : ''}
		<form id="reset-form" class="stack" novalidate>
			<div class="field">
				<label for="reset-code">Reset code</label>
				<input id="reset-code" name="code" autocapitalize="none" spellcheck="false"
					autocomplete="one-time-code" required value="${ui.esc(devCode ?? '')}" />
			</div>
			<div class="field">
				<label for="reset-password">New password</label>
				<input id="reset-password" name="password" type="password"
					autocomplete="new-password" required />
				<div class="field-hint" id="reset-hint-password"></div>
			</div>
			<div class="field">
				<label for="reset-confirm">Confirm new password</label>
				<input id="reset-confirm" name="confirm" type="password"
					autocomplete="new-password" required />
			</div>
			<button class="btn btn-primary btn-block" type="submit">Set the new password</button>
			<button class="btn btn-ghost btn-block" type="button" id="reset-restart">Send another code</button>
			<button class="btn btn-ghost btn-block" type="button" id="reset-cancel">Back to sign in</button>
		</form>`,
		`Your new password is turned into a verifier in this browser and never sent. Setting it
		 signs out every other browser and game client on the account.`);

	const form = host.querySelector('#reset-form');
	const errorHost = host.querySelector('#reset-error');
	const showError = (message) => {
		errorHost.innerHTML = ui.banner('danger', message);
		errorHost.style.marginBottom = 'var(--sp-4)';
	};
	const setBusy = (on) => form.querySelectorAll('button, input').forEach((el) => (el.disabled = on));

	host.querySelector('#reset-cancel').addEventListener('click', ctx.onCancel);
	host.querySelector('#reset-restart').addEventListener('click', () => renderRequest(host, ctx));

	loadPolicy().catch(() => { /* submit re-checks, and the server validates regardless */ });
	let debounce;
	form.addEventListener('input', () => {
		clearTimeout(debounce);
		debounce = setTimeout(() => {
			if (!policy) return;
			const values = Object.fromEntries(new FormData(form).entries());
			host.querySelector('#reset-hint-password').textContent =
				checkField(policy.password, values.password) ?? '';
		}, 250);
	});

	form.addEventListener('submit', async (e) => {
		e.preventDefault();
		errorHost.innerHTML = '';
		const values = Object.fromEntries(new FormData(form).entries());
		const code = values.code?.trim();

		if (!code) return showError('The code from the email is required.');
		if (!values.password) return showError('A new password is required.');
		if (values.password !== values.confirm) return showError('Those two passwords do not match.');

		setBusy(true);
		try {
			const rules = await loadPolicy();
			const passwordError = checkField(rules.password, values.password);
			if (passwordError) throw new Error(passwordError);

			/* The account name is an input to the SRP key derivation, so it has to be known
			 * before the verifier can be computed. The code is what proves the right to ask. */
			const { username } = await api.lookupPasswordReset(code);
			if (!username) throw new Error('That code is not valid.');

			const salt = srp.generateSalt();
			const privateKey = await srp.derivePrivateKey(salt, username, values.password);
			const verifier = await srp.deriveVerifier(privateKey);

			await api.completePasswordReset(code, salt, verifier);
			renderDone(host, ctx, username);
		} catch (err) {
			setBusy(false);
			showError(err.message || 'That password could not be changed.');
		}
	});

	host.querySelector(devCode ? '#reset-password' : '#reset-code').focus();
}

/* ── Step three: say plainly that two-factor still applies ───────────────── */

function renderDone(host, ctx, username) {
	host.innerHTML = card('check-circle', 'Password changed', `
		${ui.banner('ok', `The password for ${username} has been changed.`)}
		<p class="small" style="margin:var(--sp-4) 0 0">
			Every other browser session and game client on this account has been signed out, and
			the code you used has been spent.
		</p>
		<p class="small" style="margin:var(--sp-3) 0 0">
			<strong>You will still need your authenticator to sign in.</strong> A reset changes
			the password and nothing else — your two-factor enrolment and your recovery codes are
			untouched. If the authenticator is gone too, use one of the eight recovery codes from
			registration at the two-factor step.
		</p>
		<button class="btn btn-primary btn-block" type="button" id="reset-signin"
			style="margin-top:var(--sp-5)">Sign in</button>`,
		`If you did not ask for this reset, change your password again from a device you trust
		 and open a support ticket.`);

	host.querySelector('#reset-signin').addEventListener('click', ctx.onDone ?? ctx.onCancel);
}
