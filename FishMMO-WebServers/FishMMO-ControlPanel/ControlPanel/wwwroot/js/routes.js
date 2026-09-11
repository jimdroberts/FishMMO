/*
 * The navigation map. One entry per view, ordered as it appears in the sidebar.
 *
 * `level` is the minimum AccessLevel that may see and reach the route, matching
 * the policies in CONTROL_PANEL_DESIGN.md §6.2. Hiding a link is a courtesy;
 * the router refuses the route as well, and the real API refuses it again.
 *
 * `built: false` marks a route whose backend does not exist yet. It stays in this table
 * because it is the plan of record, but it is kept out of the sidebar: a link that leads to
 * "this page could not be loaded" teaches an operator to ignore errors, which is the last
 * habit a control panel should teach.
 */

export const GROUPS = [
	{ label: null, level: 1 },
	{ label: 'Support', level: 2 },
	{ label: 'Servers', level: 3 },
	{ label: 'Daemon', level: 3 },
	{ label: 'Administration', level: 3 },
	{ label: 'Platform', level: 3 },
];

export const ROUTES = [
	{ path: 'dashboard', group: null, label: 'Dashboard', icon: 'gauge', level: 1, title: 'Dashboard', built: true },
	{ path: 'account', group: null, label: 'My account', icon: 'user', level: 1, title: 'My account', built: true },
	{ path: 'characters', group: null, label: 'My characters', icon: 'sword', level: 1, title: 'My characters', built: true },
	{ path: 'my-tickets', group: null, label: 'My tickets', icon: 'mail', level: 1, title: 'My tickets', built: true },

	{ path: 'support/tickets', group: 'Support', label: 'Tickets', icon: 'inbox', level: 2, title: 'Support tickets', crumb: 'Support', built: true },
	{ path: 'support/accounts', group: 'Support', label: 'Accounts', icon: 'users', level: 2, title: 'Accounts', crumb: 'Support', built: true },
	{ path: 'support/characters', group: 'Support', label: 'Characters', icon: 'sword', level: 2, title: 'Characters', crumb: 'Support', built: true },
	{ path: 'support/chat', group: 'Support', label: 'Chat log', icon: 'chat', level: 2, title: 'Chat log', crumb: 'Support', built: true },
	{ path: 'support/social', group: 'Support', label: 'Guilds & social', icon: 'shield', level: 2, title: 'Guilds and social', crumb: 'Support', built: true },

	{ path: 'servers/board', group: 'Servers', label: 'Board', icon: 'server', level: 3, title: 'Server board', crumb: 'Servers', built: true },
	{ path: 'servers/maintenance', group: 'Servers', label: 'Maintenance', icon: 'clock', level: 3, title: 'Maintenance', crumb: 'Servers', built: true },
	{ path: 'servers/scenes', group: 'Servers', label: 'Scene instances', icon: 'home', level: 3, title: 'Scene instances', crumb: 'Servers', built: true },

	{ path: 'daemon/hosts', group: 'Daemon', label: 'Hosts & processes', icon: 'activity', level: 3, title: 'Hosts and processes', crumb: 'Daemon', built: true },
	/* "Command log", not "Events": every row on that page is a command this panel wrote and
	 * what the daemon that collected it did with it. Calling it an event feed would promise
	 * crashes, port-check failures and restarts the daemon decided on by itself, none of which
	 * are recorded anywhere the panel can read. */
	{ path: 'daemon/events', group: 'Daemon', label: 'Command log', icon: 'list', level: 3, title: 'Command log', crumb: 'Daemon', built: true },
	/* "Logs" is the supervisor's own history — every status change, relaunch and restart-counter
	 * reset the daemon observed — and deliberately NOT process output. Server logs carry
	 * connection strings and tokens inside exception messages, so shipping them to a browser
	 * would turn a read-only page into an exfiltration path, and it would give a compromised
	 * daemon a way to put attacker-chosen text on an operator's screen. Every row here is
	 * composed server-side from two enum values and a few integers; the daemon supplies no
	 * strings at all. It answers the question a log page is actually opened for: what happened
	 * to this process overnight, which nothing else records. */
	{ path: 'daemon/logs', group: 'Daemon', label: 'Logs', icon: 'terminal', level: 3, title: 'Logs', crumb: 'Daemon', built: true },

	{ path: 'admin/characters', group: 'Administration', label: 'Character editor', icon: 'edit', level: 3, title: 'Character editor', crumb: 'Administration', built: true },
	{ path: 'admin/audit', group: 'Administration', label: 'Audit log', icon: 'flag', level: 3, title: 'Audit log', crumb: 'Administration', built: true },

	{ path: 'platform/health', group: 'Platform', label: 'Health', icon: 'heart', level: 3, title: 'Platform health', crumb: 'Platform', built: true },
	{ path: 'platform/secrets', group: 'Platform', label: 'Secrets', icon: 'key', level: 3, title: 'Secrets', crumb: 'Platform', built: true },
	{ path: 'platform/queues', group: 'Platform', label: 'Queues', icon: 'mail', level: 3, title: 'Queues', crumb: 'Platform', built: true },
];

/** The sidebar shows only what the backend can actually serve. */
export const NAV_ROUTES = ROUTES.filter((r) => r.built);

/** Routes reachable without appearing in the sidebar. */
export const DETAIL_ROUTES = [
	{ prefix: 'my-tickets/', level: 1, title: 'My ticket' },
	{ prefix: 'support/tickets/', level: 2, title: 'Ticket', crumb: 'Support' },
	{ prefix: 'support/social/', level: 2, title: 'Guilds and social', crumb: 'Support' },
	{ prefix: 'support/accounts/', level: 2, title: 'Account', crumb: 'Support' },
	{ prefix: 'support/characters/', level: 2, title: 'Character', crumb: 'Support' },
	{ prefix: 'admin/characters/', level: 3, title: 'Character editor', crumb: 'Administration' },
	{ prefix: 'servers/maintenance/', level: 3, title: 'Maintenance operation', crumb: 'Servers' },
	/* No `daemon/logs/` here. The supervision history is a filtered list, not a per-process
	 * drill-down, and a detail route carries no `built` flag — so one would pass the router's
	 * "not built yet" check and land on a view that never expected a parameter. */
];

export function resolve(path) {
	/* A link may carry state after a "?" — a ticket sends the chat log the character to look
	 * at and the days either side of the report. It still addresses the same view, so the
	 * query is split off before the path is matched and handed to the view as `query`;
	 * matching the whole string would route a link carrying state to "that page does not
	 * exist", which is the one answer that teaches an operator the link is broken. */
	const cut = path.indexOf('?');
	const bare = cut === -1 ? path : path.slice(0, cut);
	const query = cut === -1 ? {} : Object.fromEntries(new URLSearchParams(path.slice(cut + 1)));

	const exact = ROUTES.find((r) => r.path === bare);
	if (exact) return { ...exact, param: null, query };
	const detail = DETAIL_ROUTES.find((r) => bare.startsWith(r.prefix));
	if (detail) {
		return {
			path: detail.prefix,
			level: detail.level,
			title: detail.title,
			crumb: detail.crumb,
			param: decodeURIComponent(bare.slice(detail.prefix.length)),
			query,
		};
	}
	return null;
}

export function defaultRouteFor(level) {
	return NAV_ROUTES.find((r) => r.level <= level)?.path ?? 'dashboard';
}
