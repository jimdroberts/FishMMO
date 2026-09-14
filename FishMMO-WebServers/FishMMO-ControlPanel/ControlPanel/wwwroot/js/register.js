/*
 * Registration, mirroring the in-game UITKRegister panel field for field — and then the
 * panel's optional additions.
 *
 * The same four required inputs in the same order — account name, email, password, age —
 * validated by the same rules, which the server publishes at api/account/policy and which
 * come from FishMMO.Shared.Authentication, the class the LoginServer validates with too.
 * After them, all optional: a phone number, how to verify (email and/or SMS), a beta code
 * (required while the shard's beta gate is on), and a few details that help support prove
 * who owns an account.
 *
 * The password never leaves this browser. It is turned into an SRP salt and verifier here,
 * exactly as the game client does before it opens its connection, and only those two values
 * are sent.
 *
 * THE DECOY FIELDS. Each form fetched from api/account/register/form carries a signed token
 * and two or three field names chosen at random from names that autofill and form-filling
 * bots put values into. They are rendered as ordinary-looking inputs, moved off-screen (not
 * display:none, which bots skip), hidden from assistive technology, out of the tab order and
 * with autocomplete off, and posted back under their own names. A person never sees or fills
 * them. The server — not this page — decides what a filled one means; this page never
 * inspects them, so there is nothing here for a bot to read about which are traps.
 */

import { api } from './api.js';
import * as srp from './srp.js';
import * as ui from './ui.js';

/** Ages the in-game dropdown offers: index 1 is 13, as in UITKRegister. */
const MIN_AGE = 13;
const MAX_AGE = 100;

/** Six digits: the shape of both the emailed and the texted verification code. */
const VERIFY_CODE_LENGTH = 6;

/**
 * Off-screen, not hidden. `display:none` and `type=hidden` are exactly what a bot filters out
 * before it fills a form; a field that is laid out, has a label and simply sits outside the
 * viewport is filled like any other.
 */
const DECOY_STYLE = 'position:absolute;left:-10000px;top:auto;width:1px;height:1px;overflow:hidden';

/**
 * The server's own rules, fetched once and applied here.
 *
 * Validation happens entirely in this browser on purpose. An endpoint that answered
 * "is this password allowed?" would require sending the password, which is exactly
 * what deriving a verifier locally exists to avoid.
 */
let policy = null;

async function loadPolicy() {
	if (policy) return policy;
	policy = await api.getAccountPolicy();
	return policy;
}

/** Applies one field's rule locally. Returns an error string, or null. */
function checkField(rule, value) {
	if (!value || !rule) return null;
	if (rule.minLength && value.length < rule.minLength) return rule.error;
	if (rule.maxLength && value.length > rule.maxLength) return rule.error;
	if (rule.pattern && !new RegExp(rule.pattern).test(value)) return rule.error;
	return null;
}

const CHANNEL_LABELS = { email: 'email address', sms: 'phone' };

/**
 * Renders the registration card into a host element.
 * @param {HTMLElement} host Where to render.
 * @param {object} options `onCancel` returns to sign-in; `onRegistered` receives the result.
 */
export function renderRegister(host, { onCancel, onRegistered }) {
	host.innerHTML = `
		<div class="signin-card">
			${ui.loading('Loading the registration form…')}
		</div>`;

	Promise.all([loadPolicy(), api.getRegistrationForm()])
		.then(([rules, form]) => drawForm(host, rules, form, { onCancel, onRegistered }))
		.catch((err) => {
			host.innerHTML = `
				<div class="signin-card stack">
					${ui.banner('danger', 'The registration form could not be loaded', err?.message || '')}
					<button class="btn btn-ghost btn-block" type="button" id="reg-cancel">Back to sign in</button>
				</div>`;
			host.querySelector('#reg-cancel').addEventListener('click', onCancel);
		});
}

function decoyMarkup(decoys) {
	/* Each decoy gets an id and name derived from the server's chosen name — escaped, although the
	 * pool is a fixed list of plain identifiers — and a label, so it reads as a field to anything
	 * that reads labels. aria-hidden takes it out of the accessibility tree, tabindex=-1 out of the
	 * keyboard order, and autocomplete=off keeps a real browser's autofill from putting a genuine
	 * value into it and costing a genuine person their registration. */
	return (decoys ?? []).map((d) => `
		<div class="field" style="${DECOY_STYLE}" aria-hidden="true">
			<label for="reg-x-${ui.esc(d.name)}">${ui.esc(d.label)}</label>
			<input id="reg-x-${ui.esc(d.name)}" name="${ui.esc(d.name)}" type="text" value=""
				tabindex="-1" autocomplete="off" />
		</div>`).join('');
}

function drawForm(host, rules, form, { onCancel, onRegistered }) {
	const verification = rules.verification ?? { email: true, sms: false, autoVerify: false };
	const profileRules = rules.profile ?? {};
	const betaRequired = Boolean(rules.betaRequired);
	const decoys = form?.decoys ?? [];
	const offerChoice = !verification.autoVerify && verification.email && verification.sms;

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

			<form id="register-form" class="stack" novalidate style="position:relative">
				<div class="field">
					<label for="reg-username">Account name</label>
					<input id="reg-username" name="username" autocomplete="username" autocapitalize="none"
						spellcheck="false" required />
					<div class="field-hint" id="hint-username"></div>
				</div>
				<div class="field">
					<label for="reg-email">Email</label>
					<input id="reg-email" name="email" type="email" autocomplete="email" required />
					<div class="field-hint" id="hint-email">Used to verify the account and to reach you about its security.</div>
				</div>
				${decoyMarkup(decoys.slice(0, 1))}
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
					<div class="field-hint" id="hint-age"></div>
				</div>

				<div class="field">
					<label for="reg-phone">Phone number <span class="faint">(optional)</span></label>
					<input id="reg-phone" name="phone" type="tel" autocomplete="tel" placeholder="${ui.esc(profileRules.phoneHint ?? '+44 7700 900123')}" />
					<div class="field-hint" id="hint-phone">With its country code. Needed only to verify by SMS.</div>
				</div>

				${offerChoice ? `
					<div class="field">
						<label>Verify this account by</label>
						<label class="check-row"><input type="checkbox" id="reg-verify-email" name="verifyEmail" checked /> <span>A code sent to my email</span></label>
						<label class="check-row"><input type="checkbox" id="reg-verify-sms" name="verifySms" /> <span>A code sent to my phone by SMS</span></label>
						<div class="field-hint" id="hint-verifySms"></div>
					</div>` : ''}

				<div class="field">
					<label for="reg-beta">Beta code ${betaRequired ? '' : '<span class="faint">(optional)</span>'}</label>
					<input id="reg-beta" name="betaCode" autocomplete="off" autocapitalize="characters" spellcheck="false"
						maxlength="${Number(rules.betaCode?.length ?? 14) + 4}" placeholder="${ui.esc(rules.betaCode?.hint ?? 'XXXX-XXXX-XXXX')}"
						${betaRequired ? 'required aria-required="true"' : ''} />
					<div class="field-hint" id="hint-betaCode">${betaRequired
						? 'This shard is in closed beta: registering needs a beta code.'
						: 'If you were given one.'}</div>
				</div>

				${decoyMarkup(decoys.slice(1))}

				<details>
					<summary class="small">More about you (optional)</summary>
					<p class="xsmall muted">Only support staff can see these, and only to confirm who owns this account if you ever need their help.</p>
					<div class="stack">
						<div class="field">
							<label for="reg-realname">Real name</label>
							<input id="reg-realname" name="realName" autocomplete="name" maxlength="${Number(profileRules.realNameMaxLength ?? 128)}" />
							<div class="field-hint" id="hint-realName"></div>
						</div>
						<div class="field">
							<label for="reg-country">Country or region</label>
							<input id="reg-country" name="country" autocomplete="country-name" maxlength="${Number(profileRules.countryMaxLength ?? 64)}" />
							<div class="field-hint" id="hint-country"></div>
						</div>
						<div class="field">
							<label for="reg-address">Address</label>
							<textarea id="reg-address" name="address" autocomplete="street-address" maxlength="${Number(profileRules.addressMaxLength ?? 512)}"></textarea>
							<div class="field-hint" id="hint-address"></div>
						</div>
						<div class="field">
							<label for="reg-referral">Referred by (account name)</label>
							<input id="reg-referral" name="referralAccount" autocapitalize="none" spellcheck="false" />
							<div class="field-hint" id="hint-referralAccount"></div>
						</div>
					</div>
				</details>

				<button class="btn btn-primary btn-block" type="submit">Create account</button>
				<button class="btn btn-ghost btn-block" type="button" id="reg-cancel">Back to sign in</button>
			</form>

			<div class="signin-footer">
				Your password is turned into a proof in this browser and never sent. The server
				stores a salt and a verifier, exactly as it does for the game client.
			</div>
		</div>`;

	const formEl = host.querySelector('#register-form');
	const errorHost = host.querySelector('#register-error');
	const hintDefaults = new Map();
	host.querySelectorAll('.field-hint[id^="hint-"]').forEach((h) => hintDefaults.set(h.id, h.textContent));

	const clearFieldErrors = () => {
		host.querySelectorAll('.field-hint[id^="hint-"]').forEach((h) => {
			h.textContent = hintDefaults.get(h.id) ?? '';
			h.classList.remove('is-error');
			h.style.color = '';
		});
		host.querySelectorAll('[aria-invalid="true"]').forEach((el) => el.removeAttribute('aria-invalid'));
	};

	const showError = (message, field) => {
		errorHost.innerHTML = ui.banner('danger', message);
		errorHost.style.marginBottom = 'var(--sp-4)';
		// Field names come from the server's own small set; anything else is simply not matched.
		const safe = String(field ?? '').replace(/[^A-Za-z0-9_-]/g, '');
		if (!safe) return;
		const hint = host.querySelector(`#hint-${safe}`);
		if (hint) {
			hint.textContent = message;
			hint.classList.add('is-error');
			hint.style.color = 'var(--danger)';
		}
		const input = safe === 'verifySms' ? host.querySelector('#reg-phone') : formEl.querySelector(`[name="${safe}"]`);
		input?.setAttribute('aria-invalid', 'true');
	};

	const setBusy = (on) => {
		formEl.querySelectorAll('button, input, select, textarea').forEach((el) => {
			// The decoys stay exactly as they are: disabling them would drop them from the form data.
			if (el.closest('[aria-hidden="true"]')) return;
			el.disabled = on;
		});
	};

	host.querySelector('#reg-cancel').addEventListener('click', onCancel);

	let debounce;
	const liveValidate = () => {
		const values = Object.fromEntries(new FormData(formEl).entries());
		host.querySelector('#hint-username').textContent = checkField(rules.username, values.username) ?? '';
		host.querySelector('#hint-password').textContent = checkField(rules.password, values.password) ?? '';
	};
	formEl.addEventListener('input', () => {
		clearTimeout(debounce);
		debounce = setTimeout(liveValidate, 250);
	});

	formEl.addEventListener('submit', async (e) => {
		e.preventDefault();
		errorHost.innerHTML = '';
		clearFieldErrors();
		const values = Object.fromEntries(new FormData(formEl).entries());
		const verifyEmail = offerChoice ? formEl.querySelector('#reg-verify-email').checked : true;
		const verifySms = offerChoice ? formEl.querySelector('#reg-verify-sms').checked : false;
		const trimmed = (name) => String(values[name] ?? '').trim();

		// Local checks first, in the same order the in-game panel applies them.
		if (!values.username) return showError('An account name is required.', 'username');
		if (!values.password) return showError('A password is required.', 'password');
		if (values.password !== values.confirm) return showError('Those two passwords do not match.', 'password');
		if (!values.email) return showError('A valid email address is required to register.', 'email');
		if (!values.age) return showError('You must confirm your age to register.', 'age');
		if (verifySms && !trimmed('phone')) return showError('Verifying by SMS needs a phone number.', 'phone');
		if (betaRequired && !trimmed('betaCode')) return showError('A beta code is required to register.', 'betaCode');

		setBusy(true);
		try {
			const usernameError = checkField(rules.username, values.username);
			if (usernameError) throw Object.assign(new Error(usernameError), { field: 'username' });
			const passwordError = checkField(rules.password, values.password);
			if (passwordError) throw Object.assign(new Error(passwordError), { field: 'password' });
			if (!new RegExp(rules.email.pattern).test(values.email)) throw Object.assign(new Error(rules.email.error), { field: 'email' });

			/* The whole point of the flow: the salt and verifier are computed here, from a
			 * password that never leaves the page. Everything after this line could be read
			 * in full by anyone on the wire without learning it. */
			const salt = srp.generateSalt();
			const privateKey = await srp.derivePrivateKey(salt, values.username, values.password);
			const verifier = await srp.deriveVerifier(privateKey);

			const body = {
				username: values.username,
				salt,
				verifier,
				email: values.email,
				age: Number(values.age),
				phone: trimmed('phone'),
				betaCode: trimmed('betaCode'),
				country: trimmed('country'),
				realName: trimmed('realName'),
				address: trimmed('address'),
				referralAccount: trimmed('referralAccount'),
				verifyEmail,
				verifySms,
				formToken: form?.token ?? '',
			};
			// Posted back as they are, under their own names. The server decides what they mean.
			for (const decoy of decoys) body[decoy.name] = values[decoy.name] ?? '';

			const result = await api.register(body);
			onRegistered({ username: values.username, ...result });
		} catch (err) {
			setBusy(false);
			showError(err.message || 'That account could not be created.', err.field ?? err.payload?.field ?? null);
		}
	});

	host.querySelector('#reg-username')?.focus();
}

/* ── Handover: shown once ────────────────────────────────────── */

/** The base32 key an authenticator asks for when it is typed in rather than scanned. */
function manualKey(otpauthUri) {
	try {
		return new URL(otpauthUri).searchParams.get('secret') ?? otpauthUri;
	} catch {
		return otpauthUri;
	}
}

function handoverText(username, otpauthUri, codes) {
	const secret = otpauthUri ? manualKey(otpauthUri) : null;
	return [
		'FishMMO account recovery information',
		`Account: ${username}`,
		`Saved:   ${new Date().toISOString()}`,
		'',
		...(secret ? [`Authenticator key: ${secret}`, `Enrolment URI:     ${otpauthUri}`, ''] : []),
		...(codes.length ? ['Recovery codes (each one works once):', ...codes.map((c) => `  ${c}`), ''] : []),
		'Keep this somewhere only you can reach. Anyone holding these can sign in as you.',
	].join('\n');
}

/**
 * The authenticator handover: QR code, manual key, recovery codes, download and copy, and the
 * "I have saved them" checkbox. Used by registration and by a completed two-factor reset alike,
 * because both hand over the same thing exactly once.
 */
export function handoverMarkup({ otpauthUri, recoveryCodes }) {
	const codes = recoveryCodes ?? [];
	const mustAcknowledge = Boolean(otpauthUri || codes.length);
	return `
		${mustAcknowledge ? ui.banner('danger', 'This screen is shown once',
			'The authenticator key and the recovery codes below are not stored anywhere you can read them again. Save them before you continue.') : ''}

		${otpauthUri ? `
			<div class="row" style="align-items:flex-start;gap:var(--sp-4);flex-wrap:wrap">
				<div>${ui.qrCode(otpauthUri, { label: 'Authenticator enrolment code' })}</div>
				<div class="grow" style="min-width:200px">
					<label>Scan this with your authenticator app</label>
					<p class="xsmall muted">Two-factor is required on every FishMMO account, so you will need it to sign in.</p>
					<label style="margin-top:var(--sp-3)">Or enter this key by hand</label>
					<code style="display:block;overflow-wrap:anywhere;padding:var(--sp-2);letter-spacing:.05em">${ui.esc(manualKey(otpauthUri))}</code>
				</div>
			</div>
		` : ui.banner('warn', 'This account has no authenticator',
			'Two-factor could not be set up. Set it up from your account page before you rely on this account.')}

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
		` : ''}`;
}

/** Wires the handover's buttons. `onAcknowledged(checked)` follows the checkbox. */
export function wireHandover(host, { username, otpauthUri, recoveryCodes, onAcknowledged }) {
	const text = handoverText(username, otpauthUri, recoveryCodes ?? []);
	host.querySelector('#copy-codes')?.addEventListener('click', () => ui.copyToClipboard(text));
	host.querySelector('#download-codes')?.addEventListener('click', () =>
		ui.downloadText(`fishmmo-recovery-${username}.txt`, text));
	host.querySelector('#ack-saved')?.addEventListener('change', (e) => onAcknowledged?.(e.target.checked));
	return Boolean(otpauthUri || (recoveryCodes ?? []).length);
}

/* ── Verification ────────────────────────────────────────────── */

/**
 * One code form per outstanding channel, each with its own resend.
 *
 * An account can owe an email code, a phone code, or both, in either order. Every successful
 * verification answers with what is still outstanding, so the forms follow the server rather than
 * guessing: the one just verified goes away, and the account is verified only when the server says
 * nothing remains.
 *
 * @param {HTMLElement} container Where to render.
 * @param {object} options `username`, `pending` (["email","sms"]), `onVerified` when nothing remains.
 */
export function renderVerification(container, { username, pending, onVerified }) {
	let outstanding = [...new Set((pending ?? []).filter((c) => c === 'email' || c === 'sms'))];

	const draw = () => {
		if (!outstanding.length) {
			container.innerHTML = ui.banner('ok', 'Account verified', 'You can sign in now.');
			onVerified?.();
			return;
		}
		container.innerHTML = `
			<div class="stack">
				${ui.banner('info', outstanding.length === 2 ? 'Two codes to enter' : 'Check your ' + CHANNEL_LABELS[outstanding[0]],
					outstanding.length === 2
						? 'A code was sent to your email address and another to your phone. Enter both, in either order, or come back from the sign-in screen later.'
						: `A verification code was sent to your ${CHANNEL_LABELS[outstanding[0]]}. Enter it below, or later from the sign-in screen.`)}
				${outstanding.map((channel) => `
					<form class="stack" data-verify="${channel}">
						<div class="field">
							<label for="verify-code-${channel}">${channel === 'sms' ? 'Code sent to your phone' : 'Code sent to your email'}</label>
							<input id="verify-code-${channel}" name="code" inputmode="numeric" maxlength="${VERIFY_CODE_LENGTH}"
								autocomplete="one-time-code" placeholder="000000" required />
						</div>
						<div data-verify-result></div>
						<div class="row" style="gap:var(--sp-2)">
							<button class="btn btn-primary grow" type="submit">Verify ${channel === 'sms' ? 'phone' : 'email'}</button>
							<button class="btn btn-ghost" type="button" data-resend="${channel}">Send a new code</button>
						</div>
					</form>`).join('')}
			</div>`;

		container.querySelectorAll('form[data-verify]').forEach((formEl) => {
			const channel = formEl.dataset.verify;
			const resultHost = formEl.querySelector('[data-verify-result]');

			formEl.addEventListener('submit', async (e) => {
				e.preventDefault();
				resultHost.innerHTML = '';
				const code = Number(new FormData(formEl).get('code'));
				const button = formEl.querySelector('button[type="submit"]');
				button.disabled = true;
				try {
					const response = channel === 'sms'
						? await api.verifyPhone(username, code)
						: await api.verifyAccount(username, code);
					outstanding = response?.verified
						? []
						: (Array.isArray(response?.outstanding) ? response.outstanding : outstanding.filter((c) => c !== channel));
					draw();
				} catch (err) {
					button.disabled = false;
					resultHost.innerHTML = ui.banner('danger', err.message || 'That verification code is not valid.');
				}
			});

			formEl.querySelector('[data-resend]')?.addEventListener('click', async (e) => {
				e.target.disabled = true;
				try {
					const response = await api.resendVerification(username, channel);
					resultHost.innerHTML = ui.banner('info', response?.message ?? 'If a code is owed, a new one is on its way.');
				} catch (err) {
					resultHost.innerHTML = ui.banner('danger', err.message || 'A new code could not be requested.');
				}
				// Re-enabled after the server's own cooldown would plausibly have passed; the server enforces it regardless.
				setTimeout(() => { e.target.disabled = false; }, 30000);
			});
		});
	};

	draw();
}

/**
 * Shows what registration produced: the authenticator enrolment and the recovery codes, which
 * are displayed once and never again, and the verification prompts.
 *
 * The secret and the codes exist on this screen and nowhere else. The server hands them over in
 * the registration response and keeps only an encrypted secret and hashes, so there is no route
 * that can show them again. Nothing here is written to storage, for the same reason: a copy in
 * localStorage would outlive the screen on a shared machine. So the continue button stays
 * disabled until the person says they have saved them.
 */
export function renderRegistered(host, result, { onDone }) {
	const pending = Array.isArray(result.verificationPending) && result.verificationPending.length
		? result.verificationPending
		: result.requiresVerification ? ['email'] : [];

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
				${result.betaWarning ? ui.banner('warn', 'Beta code not applied', result.betaWarning) : ''}
				${handoverMarkup(result)}
				<div id="verification-host">
					${pending.length ? '' : ui.banner('ok', 'The account is ready', 'You can sign in now.')}
				</div>
				<button class="btn btn-ghost btn-block" type="button" id="reg-done">Continue to sign in</button>
			</div>
		</div>`;

	const done = host.querySelector('#reg-done');
	const mustAcknowledge = wireHandover(host, {
		username: result.username,
		otpauthUri: result.otpauthUri,
		recoveryCodes: result.recoveryCodes,
		onAcknowledged: (checked) => { done.disabled = !checked; },
	});
	done.disabled = mustAcknowledge;
	done.addEventListener('click', onDone);

	if (pending.length) {
		renderVerification(host.querySelector('#verification-host'), { username: result.username, pending });
	}
}
