/*
 * Registration, mirroring the in-game UITKRegister panel field for field.
 *
 * The same four inputs in the same order — account name, email, password, age —
 * validated by the same rules, which the server publishes at api/account/policy and
 * which come from FishMMO.Shared.Authentication — the class the LoginServer validates
 * with too, so the two registration paths cannot drift.
 *
 * The password never leaves this browser. It is turned into an SRP salt and verifier
 * here, exactly as the game client does before it opens its connection, and only
 * those two values are sent.
 */

import { api } from './api.js';
import * as srp from './srp.js';
import * as ui from './ui.js';

/** Ages the in-game dropdown offers: index 1 is 13, as in UITKRegister. */
const MIN_AGE = 13;
const MAX_AGE = 100;

/**
 * The server's own rules, fetched once and applied here.
 *
 * Validation happens entirely in this browser on purpose. An endpoint that answered
 * "is this password allowed?" would require sending the password, which is exactly
 * what deriving a verifier locally exists to avoid. The rules come from the same
 * `Authentication` class the LoginServer uses, so nothing can drift.
 */
let policy = null;

async function loadPolicy() {
	if (policy) return policy;
	policy = await api.getAccountPolicy();
	return policy;
}

/** Applies one field's rule locally. Returns an error string, or null. */
function checkField(rule, value) {
	if (!value) return null;
	if (rule.minLength && value.length < rule.minLength) return rule.error;
	if (rule.maxLength && value.length > rule.maxLength) return rule.error;
	if (rule.pattern && !new RegExp(rule.pattern).test(value)) return rule.error;
	return null;
}

/**
 * Renders the registration card into a host element.
 * @param {HTMLElement} host Where to render.
 * @param {object} options `onCancel` returns to sign-in; `onRegistered` receives the result.
 */
export function renderRegister(host, { onCancel, onRegistered }) {
	host.innerHTML = `
		<div class="signin-card">
			<div class="signin-brand">
				<span style="color:var(--accent)">${ui.icon('user', 26)}</span>
				<div>
					<div style="font-size:var(--fs-lg);font-weight:600;color:var(--text-strong)">Create an account</div>
					<div class="xsmall muted" style="text-transform:uppercase;letter-spacing:0.07em">FishMMO</div>
				</div>
			</div>
			<div class="signin-title">
				The same account you will use in the game client.
			</div>

			<div id="register-error"></div>

			<form id="register-form" class="stack" novalidate>
				<div class="field">
					<label for="reg-username">Account name</label>
					<input id="reg-username" name="username" autocomplete="username" autocapitalize="none"
						spellcheck="false" required />
					<div class="field-hint" id="hint-username"></div>
				</div>
				<div class="field">
					<label for="reg-email">Email</label>
					<input id="reg-email" name="email" type="email" autocomplete="email" required />
					<div class="field-hint">Used once, to verify the account.</div>
				</div>
				<div class="field">
					<label for="reg-password">Password</label>
					<input id="reg-password" name="password" type="password" autocomplete="new-password" required />
					<div class="field-hint" id="hint-password"></div>
				</div>
				<div class="field">
					<label for="reg-confirm">Confirm password</label>
					<input id="reg-confirm" name="confirm" type="password" autocomplete="new-password" required />
				</div>
				<div class="field">
					<label for="reg-age">Age</label>
					<select id="reg-age" name="age" required>
						<option value="">Confirm your age…</option>
						${Array.from({ length: MAX_AGE - MIN_AGE + 1 }, (_, i) => MIN_AGE + i)
							.map((a) => `<option value="${a}">${a}${a === MAX_AGE ? '+' : ''}</option>`).join('')}
					</select>
				</div>
				<button class="btn btn-primary btn-block" type="submit">Create account</button>
				<button class="btn btn-ghost btn-block" type="button" id="reg-cancel">Back to sign in</button>
			</form>

			<div class="signin-footer">
				Your password is turned into a proof in this browser and never sent. The server
				stores a salt and a verifier, exactly as it does for the game client.
			</div>
		</div>`;

	const form = host.querySelector('#register-form');
	const errorHost = host.querySelector('#register-error');

	const showError = (message) => {
		errorHost.innerHTML = ui.banner('danger', message);
		errorHost.style.marginBottom = 'var(--sp-4)';
	};

	const setBusy = (on) => {
		form.querySelectorAll('button, input, select').forEach((el) => (el.disabled = on));
	};

	host.querySelector('#reg-cancel').addEventListener('click', onCancel);

	// Show the rules as the operator types, entirely locally.
	loadPolicy().catch(() => { /* the submit path re-checks and the server validates anyway */ });

	let debounce;
	const liveValidate = () => {
		if (!policy) return;
		const values = Object.fromEntries(new FormData(form).entries());
		host.querySelector('#hint-username').textContent = checkField(policy.username, values.username) ?? '';
		host.querySelector('#hint-password').textContent = checkField(policy.password, values.password) ?? '';
	};
	form.addEventListener('input', () => {
		clearTimeout(debounce);
		debounce = setTimeout(liveValidate, 250);
	});

	form.addEventListener('submit', async (e) => {
		e.preventDefault();
		errorHost.innerHTML = '';
		const values = Object.fromEntries(new FormData(form).entries());

		// Local checks first, in the same order the in-game panel applies them.
		if (!values.username) return showError('An account name is required.');
		if (!values.password) return showError('A password is required.');
		if (values.password !== values.confirm) return showError('Those two passwords do not match.');
		if (!values.email) return showError('A valid email address is required to register.');
		if (!values.age) return showError('You must confirm your age to register.');

		setBusy(true);
		try {
			const rules = await loadPolicy();
			const usernameError = checkField(rules.username, values.username);
			if (usernameError) throw new Error(usernameError);
			const passwordError = checkField(rules.password, values.password);
			if (passwordError) throw new Error(passwordError);
			if (!new RegExp(rules.email.pattern).test(values.email)) throw new Error(rules.email.error);

			/* The whole point of the flow: the salt and verifier are computed here, from a
			 * password that never leaves the page. Everything after this line could be read
			 * in full by anyone on the wire without learning it. */
			const salt = srp.generateSalt();
			const privateKey = await srp.derivePrivateKey(salt, values.username, values.password);
			const verifier = await srp.deriveVerifier(privateKey);

			const result = await api.register({
				username: values.username,
				salt,
				verifier,
				email: values.email,
				age: Number(values.age),
			});

			onRegistered({ username: values.username, ...result });
		} catch (err) {
			setBusy(false);
			showError(err.message || 'That account could not be created.');
		}
	});

	host.querySelector('#reg-username')?.focus();
}

/**
 * Shows what registration produced: the authenticator enrolment and the recovery
 * codes, which are displayed once and never again, and the verification prompt.
 *
 * The secret and the codes exist on this screen and nowhere else. The server hands them
 * over in the registration response and keeps only an encrypted secret and PBKDF2 hashes,
 * so there is no route that can show them again — losing them means re-enrolling. Nothing
 * here is written to storage, which is the same reason: a copy in localStorage would
 * outlive the screen on a shared machine.
 *
 * So the continue button stays disabled until the person says they have saved them. That
 * is not a legal formality; it is the last chance anyone gets.
 */
export function renderRegistered(host, result, { onDone }) {
	const codes = result.recoveryCodes ?? [];
	// The manual-entry key is what an authenticator asks for, not the whole URI.
	const secret = result.otpauthUri ? new URL(result.otpauthUri).searchParams.get('secret') : null;
	const mustAcknowledge = Boolean(result.otpauthUri || codes.length);
	const plainText = [
		'FishMMO account recovery information',
		`Account: ${result.username}`,
		`Saved:   ${new Date().toISOString()}`,
		'',
		secret ? `Authenticator key: ${secret}` : '',
		result.otpauthUri ? `Enrolment URI:     ${result.otpauthUri}` : '',
		codes.length ? '' : null,
		codes.length ? 'Recovery codes (each one works once):' : '',
		...codes.map((c) => `  ${c}`),
		'',
		'Keep this somewhere only you can reach. Anyone holding these can sign in as you.',
	].filter((line) => line !== null).join('\n');

	host.innerHTML = `
		<div class="signin-card" style="max-width:560px">
			<div class="signin-brand">
				<span style="color:var(--ok)">${ui.icon('check-circle', 26)}</span>
				<div>
					<div style="font-size:var(--fs-lg);font-weight:600;color:var(--text-strong)">Account created</div>
					<div class="xsmall muted">${ui.esc(result.username)}</div>
				</div>
			</div>

			<div class="stack" style="margin-top:var(--sp-4)">
				${mustAcknowledge ? ui.banner('danger', 'This screen is shown once',
					'The authenticator key and the recovery codes below are not stored anywhere you can read them again. Save them before you continue.') : ''}

				${result.otpauthUri ? `
					<div class="row" style="align-items:flex-start;gap:var(--sp-4);flex-wrap:wrap">
						<div>${ui.qrCode(result.otpauthUri, { label: 'Authenticator enrolment code' })}</div>
						<div class="grow" style="min-width:200px">
							<label>Scan this with your authenticator app</label>
							<p class="xsmall muted">Two-factor is required on every FishMMO account, so you will need it to sign in.</p>
							<label style="margin-top:var(--sp-3)">Or enter this key by hand</label>
							<code style="display:block;overflow-wrap:anywhere;padding:var(--sp-2);letter-spacing:.05em">${ui.esc(secret ?? result.otpauthUri)}</code>
						</div>
					</div>
				` : ui.banner('warn', 'This account has no authenticator',
					'Two-factor could not be set up during registration. Set it up from your account page before you rely on this account.')}

				${codes.length ? `
					<div>
						<label>Recovery codes</label>
						<p class="xsmall muted">
							Each one signs you in a single time if you lose your authenticator. Anyone
							holding them can sign in as you, so keep them where only you can reach.
						</p>
						<div style="display:grid;grid-template-columns:repeat(auto-fill,minmax(150px,1fr));gap:var(--sp-2);margin-top:var(--sp-2)">
							${codes.map((c) => `<code class="center">${ui.esc(c)}</code>`).join('')}
						</div>
					</div>
				` : ''}

				${mustAcknowledge ? `
					<div class="row" style="gap:var(--sp-2)">
						<button class="btn grow" type="button" id="download-codes">${ui.icon('download')} Download</button>
						<button class="btn grow" type="button" id="copy-codes">${ui.icon('copy')} Copy</button>
					</div>
					<label class="check-row">
						<input type="checkbox" id="ack-saved" />
						<span>I have saved my authenticator key and recovery codes somewhere safe.</span>
					</label>
				` : ''}

				${result.requiresVerification ? `
					${ui.banner('info', 'Check your email',
						'A verification code has been sent. Enter it below, or later from the sign-in screen.')}
					<form id="verify-form" class="stack">
						<div class="field">
							<label for="verify-code">Verification code</label>
							<input id="verify-code" name="code" inputmode="numeric" maxlength="6"
								placeholder="000000" required />
						</div>
						<div id="verify-error"></div>
						<button class="btn btn-primary btn-block" type="submit">Verify account</button>
					</form>
				` : ui.banner('ok', 'The account is ready', 'You can sign in now.')}

				<button class="btn btn-ghost btn-block" type="button" id="reg-done" ${mustAcknowledge ? 'disabled' : ''}>Continue to sign in</button>
			</div>
		</div>`;

	host.querySelector('#copy-codes')?.addEventListener('click', () => ui.copyToClipboard(plainText));
	host.querySelector('#download-codes')?.addEventListener('click', () =>
		ui.downloadText(`fishmmo-recovery-${result.username}.txt`, plainText));

	const done = host.querySelector('#reg-done');
	host.querySelector('#ack-saved')?.addEventListener('change', (e) => {
		done.disabled = !e.target.checked;
	});
	done.addEventListener('click', onDone);

	host.querySelector('#verify-form')?.addEventListener('submit', async (e) => {
		e.preventDefault();
		const code = new FormData(e.target).get('code');
		const errorHost = host.querySelector('#verify-error');
		errorHost.innerHTML = '';
		try {
			await api.verifyAccount(result.username, Number(code));
			errorHost.innerHTML = ui.banner('ok', 'Account verified', 'You can sign in now.');
			e.target.querySelector('button').disabled = true;
		} catch (err) {
			errorHost.innerHTML = ui.banner('danger', err.message || 'That verification code is not valid.');
		}
	});
}
