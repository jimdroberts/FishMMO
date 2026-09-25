/*
 * An account's history, as staff weigh a report against it.
 *
 * Shared by the ticket view, beside the accused, and the account page. A report read on its own
 * is how the fifth complaint about one player gets the same warning as the first; this puts the
 * pattern next to the report.
 *
 * Everything here was written by a player or about one — ticket subjects, resolutions, the
 * reasons staff gave — so every value goes through `ui.esc`, and names through
 * `encodeURIComponent` on the way into a link.
 */

const STATUS_LABELS = ['Open', 'In progress', 'Awaiting player', 'Resolved', 'Closed'];

/* The sections the server names in `incomplete` when their read failed, as a sentence can say them. */
const SECTION_NAMES = {
	characters: 'characters',
	pendingTwoFactorReset: 'pending two-factor reset',
	betaCodes: 'beta codes',
	reportsAgainst: 'reports against the account',
	filed: 'tickets it filed',
	moderation: 'staff actions',
};

/**
 * The warning for sections the server could not read, or nothing.
 *
 * Every one of these sections has an ordinary empty answer, so a failed read shown as empty would be
 * indistinguishable from the truth — on the pages staff decide bans and two-factor cancellations on.
 * The server names what it could not read in `incomplete`; this says so above everything else.
 */
export function unreadBanner(ui, incomplete) {
	const missing = (incomplete ?? []).map((key) => SECTION_NAMES[key] ?? key);
	if (!missing.length) return '';
	return ui.banner('warn', 'Parts of this account could not be read',
		`The database did not answer for: ${missing.join(', ')}. Those parts are shown empty because nothing was read, not because there is nothing — reload before acting on them.`);
}

/** True when an instant in the future is still in the future. A null end is "no end". */
function inForce(until) {
	if (until === null || until === undefined) return true;
	return Date.parse(/[Zz]$|[+-]\d{2}:?\d{2}$/.test(until) ? until : until + 'Z') > Date.now();
}

function standing(ui, account, pendingReset) {
	const lines = [];
	if (pendingReset) {
		lines.push(`<dt>Two-factor reset</dt><dd>${ui.badge(pendingReset.isEffective ? 'pending, in effect' : `pending, ${ui.ago(pendingReset.effectiveUtc)}`, 'warn')}
			<div class="cell-sub">requested ${ui.esc(ui.dateTime(pendingReset.requestedUtc))}</div></dd>`);
	}
	if (account.accessLevel === 0) {
		lines.push(`<dt>Ban</dt><dd>${ui.badge(account.bannedUntil ? `until ${ui.dateTime(account.bannedUntil)}` : 'permanent', 'danger')}
			${account.bannedBy ? `<div class="cell-sub">by ${ui.esc(account.bannedBy)}</div>` : ''}
			${account.banReason ? `<div class="cell-sub">${ui.esc(account.banReason)}</div>` : ''}</dd>`);
	}
	if (account.muted && inForce(account.mutedUntil)) {
		lines.push(`<dt>Mute</dt><dd>${ui.badge(account.mutedUntil ? `until ${ui.dateTime(account.mutedUntil)}` : 'no end', 'warn')}
			${account.mutedBy ? `<div class="cell-sub">by ${ui.esc(account.mutedBy)}</div>` : ''}
			${account.muteReason ? `<div class="cell-sub">${ui.esc(account.muteReason)}</div>` : ''}</dd>`);
	}
	return lines.length
		? `<dl class="dl">${lines.join('')}</dl>`
		: '<p class="small muted">Not banned and not muted.</p>';
}

function ticketRows(ui, list, excludeId) {
	const items = (list?.items ?? []).filter((t) => t.id !== excludeId);
	if (!items.length) return '<p class="small muted">None.</p>';
	return `<ul class="stack" style="list-style:none;padding:0;margin:0">${items.map((t) => `
		<li>
			<a href="#/support/tickets/${encodeURIComponent(t.id)}">#${ui.esc(t.id)}</a>
			${ui.badge(STATUS_LABELS[t.status] ?? t.statusName ?? '')}
			<span style="overflow-wrap:anywhere">${ui.esc(t.subject)}</span>
			<div class="cell-sub">${ui.esc(ui.dateTime(t.createdUtc))}${t.reporterAccount ? ` · from ${ui.esc(t.reporterAccount)}` : ''}</div>
			${t.resolution ? `<div class="cell-sub" style="overflow-wrap:anywhere">Resolution: ${ui.esc(t.resolution)}</div>` : ''}
		</li>`).join('')}</ul>`;
}

function moderationRows(ui, rows) {
	if (!rows?.length) return '<p class="small muted">Staff have taken no recorded action on this account or its characters.</p>';
	return `<ul class="stack" style="list-style:none;padding:0;margin:0">${rows.map((e) => `
		<li>
			<span class="nowrap" title="${ui.esc(ui.dateTime(e.occurredUtc))}">${ui.ago(e.occurredUtc)}</span>
			<strong>${ui.esc(e.action)}</strong>
			${e.succeeded ? '' : ui.badge('refused', 'warn')}
			<div class="cell-sub">
				by ${ui.esc(e.actor)} (${ui.esc(e.source ?? '')})${e.targetType === 'character' ? ` · on ${ui.esc(e.targetName || `character ${e.targetId}`)}` : ''}
			</div>
			${e.reason ? `<div class="cell-sub" style="overflow-wrap:anywhere">${ui.esc(e.reason)}</div>` : ''}
			${!e.succeeded && e.outcome ? `<div class="cell-sub" style="overflow-wrap:anywhere">${ui.esc(e.outcome)}</div>` : ''}
		</li>`).join('')}</ul>`;
}

/**
 * The history, as the inside of a card body.
 *
 * @param ui the ui module
 * @param history the response of `api.getAccountHistory`
 * @param excludeTicketId the ticket being read, left out of "reported before" so it does not count itself
 */
export function historyBody(ui, history, excludeTicketId = null) {
	const against = history.reportsAgainst ?? { totalCount: 0, items: [] };
	const filed = history.filed ?? { totalCount: 0, items: [] };
	/* A section the server could not read has no count: it is shown as "—", never as 0. */
	const unread = new Set(history.incomplete ?? []);
	// The server already left the ticket being read out of the total; see SupportAccountsController.History.
	const priorReports = unread.has('reportsAgainst') ? null : (against.totalCount ?? 0);
	const lockedCharacters = (history.characters ?? []).filter((c) => c.locked).length;

	return `
		<div class="stack">
			${unreadBanner(ui, history.incomplete)}
			<div class="grid grid-3">
				${ui.stat({ label: excludeTicketId ? 'Other reports' : 'Reports against', value: ui.num(priorReports) })}
				${ui.stat({ label: 'Tickets filed', value: ui.num(unread.has('filed') ? null : filed.totalCount) })}
				${ui.stat({ label: 'Staff actions', value: ui.num(unread.has('moderation') ? null : (history.moderation?.length ?? 0)), note: lockedCharacters ? `${lockedCharacters} character(s) locked` : '' })}
			</div>
			<div><strong>Standing</strong>${standing(ui, history.account ?? {}, history.pendingTwoFactorReset)}</div>
			<div><strong>Reported ${excludeTicketId ? 'before' : ''}</strong>${ticketRows(ui, against, excludeTicketId)}</div>
			<div><strong>Staff actions</strong>${moderationRows(ui, history.moderation)}</div>
			<div><strong>Filed by this account</strong>${ticketRows(ui, filed, excludeTicketId)}</div>
		</div>`;
}
