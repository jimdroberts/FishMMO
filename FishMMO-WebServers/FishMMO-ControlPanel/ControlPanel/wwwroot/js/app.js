/*
 * FishMMO Control Panel — application shell.
 *
 * Owns the session, the theme, the sidebar, the hash router and the step-up
 * prompt. Views are lazily imported and get a small context object; none of
 * them touch the DOM outside the page container they are handed.
 */

import { api } from './api.js';
import * as srp from './srp.js';
import * as ui from './ui.js';
import { ROUTES, NAV_ROUTES, GROUPS, resolve, defaultRouteFor } from './routes.js';
import { renderRegister, renderRegistered } from './register.js';
import { renderResetPassword } from './reset-password.js';

const VIEWS = {
	'dashboard': () => import('./views/dashboard.js'),
	'account': () => import('./views/account.js'),
	'characters': () => import('./views/characters.js'),
	'my-tickets': () => import('./views/my-tickets.js'),
	'my-tickets/': () => import('./views/my-tickets.js'),
	'support/accounts': () => import('./views/support-accounts.js'),
	'support/accounts/': () => import('./views/support-accounts.js'),
	'support/characters': () => import('./views/support-characters.js'),
	'support/characters/': () => import('./views/support-characters.js'),
	'support/chat': () => import('./views/support-chat.js'),
	'support/tickets': () => import('./views/support-tickets.js'),
	'support/tickets/': () => import('./views/support-tickets.js'),
	'support/social': () => import('./views/support-social.js'),
	'support/social/': () => import('./views/support-social.js'),
	'servers/board': () => import('./views/servers-board.js'),
	'servers/maintenance': () => import('./views/servers-maintenance.js'),
	'servers/maintenance/': () => import('./views/servers-maintenance.js'),
	'servers/scenes': () => import('./views/servers-scenes.js'),
	'daemon/hosts': () => import('./views/daemon-hosts.js'),
	'daemon/events': () => import('./views/daemon-events.js'),
	'daemon/logs': () => import('./views/daemon-logs.js'),
	'admin/characters': () => import('./views/admin-characters.js'),
	'admin/characters/': () => import('./views/admin-characters.js'),
	'admin/audit': () => import('./views/admin-audit.js'),
	'platform/health': () => import('./views/platform-health.js'),
	'platform/secrets': () => import('./views/platform-secrets.js'),
	'platform/queues': () => import('./views/platform-queues.js'),
};

const THEME_KEY = 'fishmmo.panel.theme';

const state = {
	session: null,
	route: null,
	cleanup: null,
	// Bumped on every navigation. A view render that finishes after a newer
	// navigation has started must not write its result to the page — otherwise
	// clicking quickly through the sidebar lands you on one page showing another
	// page's content, which on a real network is easy to hit.
	navToken: 0,
};

/* ── Theme ───────────────────────────────────────────────────── */

function readStoredTheme() {
	try {
		return localStorage.getItem(THEME_KEY);
	} catch {
		return null;
	}
}

function applyTheme(theme) {
	if (theme === 'light' || theme === 'dark') {
		document.documentElement.setAttribute('data-theme', theme);
	} else {
		document.documentElement.removeAttribute('data-theme');
	}
	try {
		if (theme) localStorage.setItem(THEME_KEY, theme);
		else localStorage.removeItem(THEME_KEY);
	} catch {
		/* Private windows and blocked site data are fine; the page still works. */
	}
}

function currentTheme() {
	const explicit = document.documentElement.getAttribute('data-theme');
	if (explicit) return explicit;
	return window.matchMedia?.('(prefers-color-scheme: dark)').matches ? 'dark' : 'light';
}

function toggleTheme() {
	applyTheme(currentTheme() === 'dark' ? 'light' : 'dark');
	renderTopbar();
}

applyTheme(readStoredTheme());

/* ── Step-up ─────────────────────────────────────────────────── */

/**
 * Wraps an action that may need a fresh two-factor code. On a 428 the operator
 * is prompted once and the action is retried, so a step-up never loses work.
 */
export async function withStepUp(action) {
	try {
		return await action();
	} catch (err) {
		if (!err.needsStepUp && err.status !== 428) throw err;
		const done = await ui.modal({
			title: 'Confirm it is you',
			sub: 'This action needs a fresh code from your authenticator.',
			confirmLabel: 'Confirm',
			body: `
				<div class="field">
					<label for="su-code">Six-digit code</label>
					<input id="su-code" name="code" inputmode="numeric" autocomplete="one-time-code"
						pattern="[0-9 ]{6,8}" maxlength="8" required placeholder="000000" />
				</div>`,
			onSubmit: async (values) => {
				await api.stepUp(values.code);
				return true;
			},
		});
		if (!done) return null;
		return await action();
	}
}

/** Runs an API call, showing a toast on failure. Returns null if it failed. */
export async function attempt(action, successTitle, successText) {
	try {
		const result = await withStepUp(action);
		if (result !== null && successTitle) ui.toast(successTitle, successText ?? '', 'ok');
		return result;
	} catch (err) {
		ui.toast('That did not work', err.message || 'Unknown error.', 'danger', 6000);
		return null;
	}
}

/* ── Sign-in ─────────────────────────────────────────────────── */

/**
 * The SRP-6a exchange. The password is turned into a proof here and discarded;
 * only the client's public ephemeral and that proof are sent. The server holds
 * a salt and a verifier and never has anything a password could be recovered
 * from, which is the same property the game client's login has.
 *
 * The server's own proof is verified before the session is accepted, so a
 * server that cannot prove it holds the verifier is rejected by the browser
 * rather than trusted because it answered.
 */
async function runSrpSignIn(username, password) {
	const trimmed = String(username).trim();
	const challenge = await api.srpChallenge(trimmed);

	const privateKey = await srp.derivePrivateKey(challenge.salt, trimmed, password);
	const ephemeral = srp.generateEphemeral();
	const clientSession = await srp.deriveSession(
		ephemeral.secret,
		challenge.serverPublicEphemeral,
		challenge.salt,
		trimmed,
		privateKey,
	);

	const result = await api.srpProof(challenge.handle, clientSession.publicEphemeral, clientSession.proof);

	if (result.serverProof) {
		try {
			await srp.verifySession(clientSession.publicEphemeral, clientSession, result.serverProof);
		} catch {
			throw new Error('The server could not prove its identity. Do not enter your password again.');
		}
	}
	return result;
}

function renderSignIn(stage = 'credentials', context = {}) {
	const root = document.getElementById('root');
	const step = stage === 'credentials' ? 0 : 1;

	root.innerHTML = `
		<div class="signin">
			<div class="signin-card">
				<div class="signin-brand">
					<span style="color:var(--accent)">${ui.icon('gauge', 26)}</span>
					<div>
						<div style="font-size:var(--fs-lg);font-weight:600;color:var(--text-strong)">FishMMO</div>
						<div class="xsmall muted" style="text-transform:uppercase;letter-spacing:0.07em">Control Panel</div>
					</div>
				</div>
				<div class="signin-title">
					${stage === 'credentials'
						? 'Sign in with your game account.'
						: `Two-factor required for <strong>${ui.esc(context.username ?? '')}</strong>.`}
				</div>

				<div class="signin-steps" aria-hidden="true">
					<span class="signin-step ${step === 0 ? 'is-active' : 'is-done'}"></span>
					<span class="signin-step ${step === 1 ? 'is-active' : ''}"></span>
				</div>

				<div id="signin-error"></div>

				<form id="signin-form" class="stack" novalidate>
					${stage === 'credentials' ? `
						<div class="field">
							<label for="username">Account name</label>
							<input id="username" name="username" autocomplete="username" autocapitalize="none"
								spellcheck="false" required />
						</div>
						<div class="field">
							<label for="password">Password</label>
							<input id="password" name="password" type="password" autocomplete="current-password" required />
						</div>
						<button class="btn btn-primary btn-block" type="submit">Sign in</button>
					` : `
						<div class="field">
							<label for="code">Authenticator code</label>
							<input id="code" name="code" inputmode="numeric" autocomplete="one-time-code"
								maxlength="8" required placeholder="000000" />
						</div>
						<button class="btn btn-primary btn-block" type="submit">Verify</button>
						<button class="btn btn-ghost btn-block" type="button" id="use-recovery">Use a recovery code instead</button>
					`}
				</form>

				${stage === 'credentials' ? `
					<div class="center small" style="margin-top:var(--sp-4)">
						<button class="btn btn-ghost btn-sm" type="button" id="go-register">Create an account</button>
						<button class="btn btn-ghost btn-sm" type="button" id="go-reset">Forgot password?</button>
					</div>` : ''}

				<div class="signin-footer">
					${stage === 'credentials'
						? 'Your password is turned into a proof in this browser and never sent. The server holds a salt and a verifier, exactly as it does for the game client.'
						: 'Enter the six-digit code from your authenticator, or one of your recovery codes.'}
				</div>
			</div>
		</div>`;

	const form = root.querySelector('#signin-form');
	const errorHost = root.querySelector('#signin-error');

	const showError = (message) => {
		errorHost.innerHTML = ui.banner('danger', message);
		errorHost.style.marginBottom = 'var(--sp-4)';
	};

	const submitting = (on) => {
		form.querySelectorAll('button, input').forEach((el) => (el.disabled = on));
	};

	form.addEventListener('submit', async (e) => {
		e.preventDefault();
		errorHost.innerHTML = '';
		const values = Object.fromEntries(new FormData(form).entries());
		submitting(true);
		try {
			const result = stage === 'credentials'
				? await runSrpSignIn(values.username, values.password)
				: context.recovery
					? await api.signInRecoveryCode(values.code)
					: await api.signInTwoFactor(values.code);

			if (result.stage === 'two-factor') {
				renderSignIn('two-factor', { username: result.username });
				return;
			}
			await onSignedIn(result.session);
		} catch (err) {
			submitting(false);
			showError(err.message || 'Sign-in failed.');
		}
	});

	root.querySelector('#use-recovery')?.addEventListener('click', () => {
		renderSignIn('two-factor', { ...context, recovery: true });
		const label = document.querySelector('label[for="code"]');
		if (label) label.textContent = 'Recovery code';
		const input = document.querySelector('#code');
		if (input) {
			input.placeholder = 'XXXX-XXXX';
			input.maxLength = 12;
			input.removeAttribute('inputmode');
		}
	});

	root.querySelector('#go-register')?.addEventListener('click', () => renderRegisterScreen());
	root.querySelector('#go-reset')?.addEventListener('click', () => renderResetScreen());

	document.querySelector('#username, #code')?.focus();
}

/** Shows the registration form, and then what registration produced. */
function renderRegisterScreen() {
	const root = document.getElementById('root');
	root.innerHTML = '<div class="signin"><div id="register-host" style="width:100%;display:flex;justify-content:center"></div></div>';
	const host = root.querySelector('#register-host');

	renderRegister(host, {
		onCancel: () => renderSignIn(),
		onRegistered: (result) => {
			/* The recovery codes and the otpauth URI are in this response and nowhere else —
			 * the server does not keep the plaintext and will not reissue them. Leaving this
			 * screen without saving them means re-enrolling. */
			renderRegistered(host, result, { onDone: () => renderSignIn() });
		},
	});
}

/**
 * Shows the password recovery flow.
 *
 * Reached from the sign-in card, not from a route: somebody who needs it has no session,
 * and a hash route would put a one-time code in the browser's history.
 */
function renderResetScreen() {
	const root = document.getElementById('root');
	root.innerHTML = '<div class="signin"><div id="reset-host" style="width:100%;display:flex;justify-content:center"></div></div>';
	const host = root.querySelector('#reset-host');

	renderResetPassword(host, {
		onCancel: () => renderSignIn(),
		/* Back to the ordinary sign-in, two-factor step included. A reset changes the password
		 * and nothing else, so the authenticator is still required — landing anywhere else
		 * would imply the account was already open. */
		onDone: () => renderSignIn(),
	});
}

async function onSignedIn(session) {
	state.session = session;
	renderShell();
	const wanted = location.hash.slice(2);
	const target = wanted && resolve(wanted) ? wanted : defaultRouteFor(session.accessLevel);
	if (location.hash !== `#/${target}`) location.hash = `#/${target}`;
	else await navigate();
}

/* ── Shell ───────────────────────────────────────────────────── */

function renderShell() {
	const root = document.getElementById('root');
	root.innerHTML = `
		<div class="shell" id="shell">
			<nav class="nav" id="nav" aria-label="Sections"></nav>
			<div class="main">
				<header class="topbar" id="topbar"></header>
				<main class="page" id="page" tabindex="-1">
					<div class="page-inner" id="page-inner"></div>
				</main>
			</div>
		</div>`;

	renderNav();
	renderTopbar();

	document.getElementById('shell').addEventListener('click', (e) => {
		if (e.target.classList.contains('nav-scrim')) closeNav();
	});
}

function closeNav() {
	document.getElementById('shell')?.classList.remove('nav-open');
	document.querySelector('.nav-scrim')?.remove();
}

function openNav() {
	const shell = document.getElementById('shell');
	shell.classList.add('nav-open');
	if (!document.querySelector('.nav-scrim')) {
		const scrim = document.createElement('div');
		scrim.className = 'nav-scrim';
		shell.appendChild(scrim);
	}
}

function renderNav() {
	const nav = document.getElementById('nav');
	const level = state.session.accessLevel;
	const current = state.route?.path ?? '';

	const groups = GROUPS.filter((g) => g.level <= level)
		.map((group) => {
			// NAV_ROUTES, not ROUTES: a section whose backend does not exist yet is not offered.
			const links = NAV_ROUTES.filter((r) => r.group === group.label && r.level <= level);
			if (!links.length) return '';
			return `
				<div class="nav-group">
					${group.label ? `<div class="nav-group-label">${ui.esc(group.label)}</div>` : ''}
					${links.map((r) => `
						<a class="nav-link${current === r.path || current.startsWith(r.path + '/') ? ' is-active' : ''}"
							href="#/${r.path}" data-nav>
							<span class="nav-link-icon">${ui.icon(r.icon)}</span>
							<span class="nav-link-label">${ui.esc(r.label)}</span>
						</a>`).join('')}
				</div>`;
		})
		.join('');

	nav.innerHTML = `
		<div class="nav-head">
			<span style="color:var(--accent)">${ui.icon('gauge', 22)}</span>
			<div class="nav-head-text">
				<div class="nav-head-title">FishMMO</div>
				<div class="nav-head-sub">Control Panel</div>
			</div>
		</div>
		<div class="nav-scroll">${groups}</div>
		<div class="nav-foot">
			v0.1.0
		</div>`;

	nav.querySelectorAll('[data-nav]').forEach((a) => a.addEventListener('click', closeNav));
}

function renderTopbar() {
	const bar = document.getElementById('topbar');
	if (!bar) return;
	const route = state.route;
	const theme = currentTheme();

	bar.innerHTML = `
		<button class="btn btn-icon topbar-menu" id="menu-toggle" aria-label="Open navigation">${ui.icon('menu')}</button>
		<div class="crumbs">
			${route?.crumb ? `<span class="crumb-parent">${ui.esc(route.crumb)}</span><span class="crumb-sep">/</span>` : ''}
			<span class="crumbs-current">${ui.esc(route?.title ?? 'Control Panel')}</span>
		</div>
		<div class="topbar-spacer"></div>
		<div class="topbar-actions">
			<button class="btn btn-ghost btn-icon" id="theme-toggle"
				aria-label="Switch to ${theme === 'dark' ? 'light' : 'dark'} theme"
				title="Switch to ${theme === 'dark' ? 'light' : 'dark'} theme">
				${ui.icon(theme === 'dark' ? 'sun' : 'moon')}
			</button>
			<div class="identity">
				<span class="avatar avatar-sm ${ui.avatarClass(state.session.username)}">${ui.esc(ui.initials(state.session.username))}</span>
				<span class="identity-text">
					<span class="identity-name">${ui.esc(state.session.username)}</span>
					<span class="identity-level">${ui.esc(state.session.levelName)}</span>
				</span>
			</div>
			<button class="btn btn-ghost btn-icon" id="sign-out" aria-label="Sign out" title="Sign out">${ui.icon('logout')}</button>
		</div>`;

	bar.querySelector('#menu-toggle').addEventListener('click', openNav);
	bar.querySelector('#theme-toggle').addEventListener('click', toggleTheme);
	bar.querySelector('#sign-out').addEventListener('click', async () => {
		await api.signOut();
		state.session = null;
		state.route = null;
		if (state.cleanup) state.cleanup();
		state.cleanup = null;
		location.hash = '';
		renderSignIn();
	});
}

/* ── Router ──────────────────────────────────────────────────── */

async function navigate() {
	if (!state.session) return;

	const token = ++state.navToken;
	const path = location.hash.slice(2) || defaultRouteFor(state.session.accessLevel);
	const route = resolve(path);

	if (!route) {
		showNotFound(path);
		return;
	}
	if (route.level > state.session.accessLevel) {
		showForbidden(route);
		return;
	}

	if (state.cleanup) {
		state.cleanup();
		state.cleanup = null;
	}

	state.route = route;
	renderNav();
	renderTopbar();

	const attached = document.getElementById('page-inner');
	attached.innerHTML = ui.loading();
	document.getElementById('page').scrollTop = 0;

	// A route in the plan whose backend is not written yet says so, plainly. It would
	// otherwise reach its view, call an endpoint that answers 404, and land on "this page
	// could not be loaded" — which reads as a fault rather than as work not done.
	if (route.built === false) {
		showNotBuilt(route);
		return;
	}

	const load = VIEWS[route.path];
	if (!load) {
		showNotFound(path);
		return;
	}

	// The view renders into a detached container. It is swapped in only if this
	// navigation is still the current one, so a slow render cannot clobber a
	// newer page. Views keep the same element reference afterwards, so their own
	// redraws and polling continue to work once it is attached.
	const host = document.createElement('div');
	host.className = 'page-inner';

	try {
		const module = await load();
		if (token !== state.navToken) return;
		const context = {
			api,
			ui,
			session: state.session,
			param: route.param,
			route,
			go: (to) => {
				location.hash = `#/${to}`;
			},
			attempt,
			withStepUp,
			refresh: () => navigate(),
			onCleanup: (fn) => {
				state.cleanup = fn;
			},
		};
		await module.render(host, context);
		if (token !== state.navToken) return;
		host.id = 'page-inner';
		attached.replaceWith(host);
	} catch (err) {
		if (token !== state.navToken) return;
		console.error(err);
		host.innerHTML = ui.banner('danger', 'This page could not be loaded.', err.message || '');
		host.id = 'page-inner';
		attached.replaceWith(host);
	}
}

function showNotBuilt(route) {
	const host = document.querySelector('#page-inner');
	if (!host) return;
	host.innerHTML = `
		<div class="page-head">
			<div class="page-head-text">
				<h1>${ui.esc(route.title ?? 'Not built yet')}</h1>
				<p class="page-head-sub">This part of the control panel has not been built yet.</p>
			</div>
		</div>
		${ui.empty('Nothing to show here yet',
			'The page exists in the plan, but the backend behind it is not written, so there is nothing real to display. It is kept out of the sidebar until there is.',
			'clock')}`;
}

function showNotFound(path) {
	state.route = { title: 'Not found', crumb: null, path: '' };
	renderNav();
	renderTopbar();
	document.getElementById('page-inner').innerHTML = ui.empty(
		'That page does not exist',
		`Nothing is routed at "${path}".`,
		'search',
	);
}

function showForbidden(route) {
	state.route = { title: 'Not permitted', crumb: null, path: '' };
	renderNav();
	renderTopbar();
	document.getElementById('page-inner').innerHTML = ui.banner(
		'warn',
		'Your access level does not reach this page',
		`"${route.title}" needs ${ui.levelName(route.level)}. You are signed in as ${state.session.levelName}.`,
	);
}

/* ── Boot ────────────────────────────────────────────────────── */

window.addEventListener('hashchange', navigate);

(async function boot() {
	const existing = await api.getSession();
	if (existing) {
		await onSignedIn(existing);
	} else {
		renderSignIn();
	}
	document.getElementById('boot')?.remove();
})();
