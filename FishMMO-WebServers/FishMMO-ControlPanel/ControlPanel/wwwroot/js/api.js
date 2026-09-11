/*
 * The single seam between the Control Panel's views and its backend.
 *
 * Every method maps to one route, and the bodies are one line each by construction.
 * No view builds a URL or calls fetch: they all talk to the object this module exports,
 * which is what kept the cutover from the design mock to a live backend to this file.
 *
 * There is no mock any more. It was removed once every route it stood in for was built,
 * and it should not come back in this shape. While it existed, the surfaces that act on
 * real accounts, characters, servers and evidence had to be listed in an ALWAYS_REAL set
 * that the mock could not shadow — because the panel's worst failure is not an error, it
 * is a reassuring screen describing something that never happened: a shard shown healthy
 * while it is down, a ban rehearsed against a player who does not exist, an audit row
 * attributing an action nobody took. If a fixture mode is ever wanted again, it belongs
 * behind a build flag that cannot reach a deployment, not a query parameter.
 *
 * A route that no controller serves must not be given a method here. One that answers in
 * development and 404s in production is worse than a missing method, because a page can
 * be written against it and only find out in front of an operator.
 */

const BASE = '/api';

/* ── Real HTTP backend ───────────────────────────────────────── */

async function request(method, path, body) {
	const init = {
		method,
		headers: { Accept: 'application/json' },
		credentials: 'same-origin',
	};
	if (body !== undefined) {
		init.headers['Content-Type'] = 'application/json';
		init.body = JSON.stringify(body);
		/* Cross-site request forgery is defended by the session cookie itself, which is
		 * HttpOnly, Secure and SameSite=Strict — a cross-site POST therefore arrives with no
		 * cookie and is simply unauthenticated. NOTHING STAMPS THIS META TAG TODAY, so the two
		 * lines below send nothing and no server route checks for the header. They are here so
		 * that adding a token is a one-line server change rather than a client change; do not
		 * read their presence as a second defence that is currently running. */
		const csrf = document.querySelector('meta[name="csrf-token"]')?.content;
		if (csrf) init.headers['X-CSRF-Token'] = csrf;
	}

	const response = await fetch(BASE + path, init);
	if (response.status === 204) return null;

	const text = await response.text();
	const payload = text ? JSON.parse(text) : null;

	if (!response.ok) {
		const error = new Error(payload?.detail || payload?.error || response.statusText);
		error.status = response.status;
		error.needsStepUp = response.status === 428;
		throw error;
	}
	return payload;
}

const q = (params) => {
	const search = new URLSearchParams();
	for (const [key, value] of Object.entries(params ?? {})) {
		if (value === '' || value === null || value === undefined) continue;
		/* An array is a parameter the server accepts more than once — the ticket queue asks
		 * for three statuses at a time. Joining them would send one value containing commas,
		 * which is a different question, and the model binder answers it with nothing. */
		if (Array.isArray(value)) for (const one of value) search.append(key, String(one));
		else search.set(key, String(value));
	}
	const s = search.toString();
	return s ? `?${s}` : '';
};

/**
 * The production backend. Every method maps to one route from the design's
 * route table; the bodies are one line each by construction.
 */
export const api = {
	// auth — the password is never sent; see js/srp.js and the flow in app.js
	srpChallenge: (username) => request('POST', '/auth/srp/challenge', { username }),
	// The handle ties message two to the exchange message one opened.
	srpProof: (handle, clientPublicEphemeral, clientProof) =>
		request('POST', '/auth/srp/proof', { handle, clientPublicEphemeral, clientProof }),
	// One endpoint takes both a TOTP code and a recovery code; the server tells them
	// apart by shape, using the same helper that generates them.
	signInTwoFactor: (code) => request('POST', '/auth/2fa/verify', { code }),
	signInRecoveryCode: (code) => request('POST', '/auth/2fa/verify', { code }),
	stepUp: (code) => request('POST', '/auth/step-up', { code }),
	getSession: () => request('GET', '/auth/session'),
	signOut: () => request('POST', '/auth/logout'),

	// registration
	register: (body) => request('POST', '/account/register', body),
	verifyAccount: (username, code) => request('POST', '/account/verify', { username, code }),
	// Takes nothing: the browser applies the rules locally so the password it is
	// checking never has to be transmitted.
	getAccountPolicy: () => request('GET', '/account/policy'),
	/* Account recovery.
	 *
	 * Three calls because the browser cannot derive an SRP verifier without the account name,
	 * and the name is an input to the key derivation — so the code is exchanged for it first.
	 * Holding a valid code already proves control of the mailbox, which is a stronger claim
	 * than knowing the name.
	 *
	 * The code goes in the BODY of every one of these, never in a URL. A one-time credential
	 * in a path or query string ends up in access logs, proxy logs and browser history.
	 *
	 * The request call always succeeds, whether or not the address is registered. There is
	 * nothing to branch on and nothing to report: an endpoint that said "no such account"
	 * would be a membership oracle for every address anyone cared to try. */
	requestPasswordReset: (email) => request('POST', '/account/password-reset/request', { email }),
	lookupPasswordReset: (code) => request('POST', '/account/password-reset/lookup', { code }),
	completePasswordReset: (code, salt, verifier) =>
		request('POST', '/account/password-reset/complete', { code, salt, verifier }),

	// own account
	getMyAccount: () => request('GET', '/account'),
	getMySessions: () => request('GET', '/account/sessions'),
	revokeMySession: (id) => request('DELETE', `/account/sessions/${id}`),
	// Carries an SRP proof of the CURRENT password and the NEW salt and verifier.
	// Neither password is ever transmitted; see js/views/account.js.
	changeMyPassword: (body) => request('POST', '/account/password', body),
	changeMyEmail: (email) => request('POST', '/account/email', { email }),
	beginTwoFactorSetup: () => request('POST', '/account/2fa/setup'),
	confirmTwoFactorSetup: (code) => request('POST', '/account/2fa/confirm', { code }),
	disableTwoFactor: () => request('DELETE', '/account/2fa'),
	regenerateRecoveryCodes: () => request('POST', '/account/2fa/recovery-codes'),
	getMyCharacters: () => request('GET', '/characters'),
	getMyCharacter: (id) => request('GET', `/characters/${id}`),

	// support
	searchAccounts: (opts) => request('GET', `/support/accounts${q(opts)}`),
	getAccount: (name) => request('GET', `/support/accounts/${encodeURIComponent(name)}`),
	searchCharacters: (opts) => request('GET', `/support/characters${q(opts)}`),
	getCharacter: (id) => request('GET', `/support/characters/${id}`),
	/* Kick is the only one of these that needs no step-up: it disconnects a session and
	 * takes nothing away. The rest change what an account may do, so the server answers
	 * them with a 428 until the operator has proved who they are again. */
	kickAccount: (name, reason) => request('POST', `/support/accounts/${encodeURIComponent(name)}/kick`, { reason }),
	banAccount: (name, reason) => request('POST', `/support/accounts/${encodeURIComponent(name)}/ban`, { reason }),
	unbanAccount: (name, reason) => request('POST', `/support/accounts/${encodeURIComponent(name)}/unban`, { reason }),
	revokeTokens: (name, reason) => request('POST', `/support/accounts/${encodeURIComponent(name)}/revoke-tokens`, { reason }),
	resetTwoFactor: (name, reason) => request('POST', `/support/accounts/${encodeURIComponent(name)}/reset-2fa`, { reason }),
	getChatLog: (opts) => request('GET', `/support/chat${q(opts)}`),
	/* Support tickets. `status` is repeated rather than joined when the queue asks for more
	 * than one; see the query builder above. This detail route returns the internal staff
	 * notes on a ticket, which is why the player's own routes below are separate ones. */
	getTickets: (opts) => request('GET', `/support/tickets${q(opts)}`),
	getTicket: (id) => request('GET', `/support/tickets/${id}`),
	// A null assignee is the unassign: the column is cleared, not set to an operator called nobody.
	assignTicket: (id, assignee, reason) => request('POST', `/support/tickets/${id}/assign`, { assignee, reason }),
	replyToTicket: (id, body, internal, reason) => request('POST', `/support/tickets/${id}/reply`, { body, internal, reason }),
	setTicketStatus: (id, status, resolution, reason) => request('POST', `/support/tickets/${id}/status`, { status, resolution, reason }),
	setTicketPriority: (id, priority, reason) => request('POST', `/support/tickets/${id}/priority`, { priority, reason }),
	/* The player's own tickets. These are scoped by the session rather than by a parameter,
	 * and the server strips internal notes from what they return — which is why the player's
	 * view calls these and never the operator routes above. */
	fileTicket: (body) => request('POST', '/support/tickets/mine', body),
	getMyTickets: (opts) => request('GET', `/support/tickets/mine${q(opts)}`),
	getMyTicket: (id) => request('GET', `/support/tickets/mine/${id}`),
	replyToMyTicket: (id, body) => request('POST', `/support/tickets/mine/${id}/reply`, { body }),
	/* Guilds and parties.
	 *
	 * A guild's leader is every member at or above the ladder's top rung, which is the game's
	 * own rule and not "whoever ranks highest" — a guild can have several, or none at all. The
	 * server says which, and says when it is ambiguous, so this surface never has to guess.
	 *
	 * Officer notes are not carried. They are permission-filtered in game so that members who
	 * may not read them never receive them, and a support agent is not a member. */
	searchGuilds: (opts) => request('GET', `/support/social/guilds${q(opts)}`),
	getGuild: (id) => request('GET', `/support/social/guilds/${id}`),
	getGuildLog: (id, opts) => request('GET', `/support/social/guilds/${id}/log${q(opts)}`),
	getParties: (opts) => request('GET', `/support/social/parties${q(opts)}`),
	/* No getArenaMatches and no getPlots. Both pointed at routes no controller serves — the
	 * failure the header warns about. Arena history and housing plots have no operator
	 * surface yet; when they get one, the controller comes first. */

	// dashboard
	getDashboard: () => request('GET', '/dashboard'),

	/* servers
	 *
	 * The whole board is one call: the three tiers are read together so the stale threshold
	 * and the counts on screen all describe the same instant. `kind` below is "world" or
	 * "scene" — the login tier has no lock or shutdown column, so it has no controls.
	 *
	 * There is no start, stop or restart here and there is no route for one. Those cannot be
	 * a database row, because a process that is not running never reads it.
	 *
	 * The control routes answer with a body, and must keep doing so: `attempt` in app.js reads
	 * a null result as "nothing happened" — that is how it swallows a dismissed step-up — so a
	 * 204 here would write the row and then show the operator no acknowledgement at all. */
	getServerBoard: () => request('GET', '/servers'),
	getScenes: (opts) => request('GET', `/servers/scenes${q(opts)}`),
	/* No retireScene. Deleting a scene row does not tell the scene server holding it to stop,
	 * and anyone inside the instance would be left there; draining it means shutting the
	 * scene server down. See the note on the Scene instances page. */
	setServerLock: (kind, id, locked, reason) =>
		request('POST', `/servers/${kind}/${id}/${locked ? 'lock' : 'unlock'}`, { reason }),
	// A null `seconds` is the cancellation: the deadline column is cleared, not set to zero,
	// which would mean "stop immediately".
	setServerShutdown: (kind, id, seconds, reason) =>
		seconds === null
			? request('DELETE', `/servers/${kind}/${id}/shutdown`, { reason })
			: request('POST', `/servers/${kind}/${id}/shutdown`, { seconds, reason }),
	/* Maintenance windows.
	 *
	 * Starting one is a single write per target: the server's own row gets a lock and an
	 * absolute deadline, and from then on the server counts down inside its own process and
	 * warns its own players. Nothing here has to stay open for the shutdown to happen.
	 *
	 * Cancelling clears the deadline and deliberately leaves the lock, because a server that
	 * already began stopping must not quietly reopen. The response names the servers left
	 * locked; the page unlocks them one audited write at a time through setServerLock. */
	startMaintenance: (body) => request('POST', '/servers/maintenance', body),
	getMaintenance: (id) => request('GET', `/servers/maintenance/${id}`),
	listMaintenance: (opts) => request('GET', `/servers/maintenance${q(opts)}`),
	cancelMaintenance: (id, reason) => request('DELETE', `/servers/maintenance/${id}`, { reason }),

	/* daemon
	 *
	 * Four routes. `getDaemonEvents` and `setDesiredState` used to sit here against routes that
	 * were never built; a seam method pointing at nothing is worse than a missing one, because
	 * a view can be written against it and only find out in production. The fourth is the
	 * supervision history below, which does have a backend.
	 *
	 * A command is QUEUED, not executed: the POST writes a row and the daemon on that host
	 * collects it on its next poll. It answers with a body — `attempt` in app.js reads a null
	 * result as "nothing happened", which is how it swallows a dismissed step-up — so a 204
	 * here would write the row and then show the operator no acknowledgement at all.
	 *
	 * The body carries a verb from a closed enumeration and the NAME of an application some
	 * daemon already reported supervising. No path, no arguments, no shell: the daemon looks
	 * that name up in its own configuration file on disk and refuses anything else. That is
	 * what stops this becoming remote execution, so nothing free-form is ever added here. */
	getDaemonHosts: () => request('GET', '/daemon/hosts'),
	getDaemonCommands: (opts) => request('GET', `/daemon/commands${q(opts)}`),
	/* The supervisor's own record of what it observed: every status change, relaunch and
	 * restart-counter reset, one row per transition. Deliberately NOT process output — logs
	 * carry connection strings and tokens inside exception messages, and relaying them into a
	 * browser would turn a read-only view into an exfiltration path. */
	getDaemonAppEvents: (opts) => request('GET', `/daemon/app-events${q(opts)}`),
	runDaemonCommand: ({ hostName, appName, verb, reason }) =>
		request('POST', '/daemon/commands', { hostName, appName, verb, reason }),

	// admin
	getCharacterEditLock: (id) => request('GET', `/admin/characters/${id}/edit-lock`),
	patchCharacter: (id, patch, reason) => request('PATCH', `/admin/characters/${id}`, { ...patch, reason }),
	/* No attribute or currency routes: those need their own sub-entity services and their
	 * own version contract, and the panel does not pretend to have them yet. */
	/* Restore and rename exist; delete and create do not. The line is destruction, not
	 * mutation: restore un-deletes a row that is still there and rename changes one field,
	 * while deletion tears down fourteen sub-entity tables and stays in the game. */
	restoreCharacter: (id, reason) => request('POST', `/admin/characters/${id}/restore`, { reason }),
	renameCharacter: (id, newName, reason) => request('POST', `/admin/characters/${id}/rename`, { newName, reason }),
	setAccessLevel: (name, level, reason) => request('POST', `/admin/accounts/${encodeURIComponent(name)}/access-level`, { level, reason }),
	getAudit: (opts) => request('GET', `/admin/audit${q(opts)}`),
	getAuditActions: () => request('GET', '/admin/audit/actions'),

	// platform
	getPlatformHealth: () => request('GET', '/platform/health'),
	/* An inventory, never a value, and there is no rotate route: rotating the two-factor
	 * master key without re-encrypting every enrolled secret would lock every player out
	 * irreversibly, so that machinery stays in the installer. */
	getSecrets: () => request('GET', '/platform/secrets'),
	/* The queues.
	 *
	 * The email queue has no state column: a row's state is which of sent_at, claimed_at and
	 * last_error are set, and the server decides it. `state` here is the server's word for it
	 * — pending, claimed, failed or sent — not a column being filtered on.
	 *
	 * A retry is a real send to a real address, so it is a step-up write and it is audited. */
	getEmailQueue: (opts) => request('GET', `/platform/queues/email${q(opts)}`),
	retryEmail: (id, reason) => request('POST', `/platform/queues/email/${id}/retry`, { reason }),
	getGroupFinderQueue: (opts) => request('GET', `/platform/queues/group-finder${q(opts)}`),
};
