using System;
using System.Collections.Generic;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// An account as an operator needs to see it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>This type must never carry <c>Salt</c>, <c>Verifier</c>, <c>TotpSecret</c>,
	/// <c>VerifyCode</c> or <c>DiscordLinkCode</c>.</b> <see cref="AccountData"/> carries all of
	/// them because the login path is the one caller that needs them: SRP cannot verify a
	/// password without the salt and the verifier, and the TOTP check cannot run without the
	/// secret. An operator view that carried the same fields would put every account's password
	/// material — and every account's second factor — behind nothing but an access-level check,
	/// on a surface that is read by a panel, serialized to JSON, and logged. The whole point of
	/// SRP is that the verifier never leaves the authentication path; a stolen verifier is an
	/// offline dictionary attack against that account with no further access needed, and a
	/// stolen TOTP secret makes the second factor forgeable forever.
	/// </para>
	/// <para>
	/// That is precisely why this is a separate type and not a projection, a subset view, or a
	/// nullable-ed copy of <see cref="AccountData"/>. A projection is one careless
	/// <c>MapEntityToDto</c> edit away from leaking, and the leak would be invisible at the call
	/// site. A type that has no such field cannot leak it, and a future field added to
	/// <see cref="AccountData"/> does not silently appear here.
	/// </para>
	/// <para>
	/// <see cref="TotpEnabled"/> and <see cref="TotpVerifiedAt"/> are safe and are deliberately
	/// kept: an operator answering "why can this person not sign in" needs to know whether a
	/// second factor is in the way, and neither field helps forge a code.
	/// </para>
	/// </remarks>
	public sealed class AccountAdminData
	{
		/// <summary>Account name, as the row stores it. This is the account's primary key.</summary>
		public string Name { get; set; }

		/// <summary>Contact email, or null when the account has none.</summary>
		public string? Email { get; set; }

		/// <summary>
		/// Account access level. Zero is <c>AccessLevel.Banned</c>, so this doubles as the
		/// ban flag — there is no separate banned column.
		/// </summary>
		public byte AccessLevel { get; set; }

		/// <summary>Account holder age, or zero when never supplied.</summary>
		public int Age { get; set; }

		/// <summary>Whether the contact email has been verified.</summary>
		public bool Verified { get; set; }

		/// <summary>Whether TOTP two-factor authentication is enabled.</summary>
		public bool TotpEnabled { get; set; }

		/// <summary>
		/// When TOTP setup was first confirmed, or null when it never was.
		/// </summary>
		/// <remarks>
		/// Null with <see cref="TotpEnabled"/> true means setup was started and never finished,
		/// which is the state an operator is usually looking at when a player reports being
		/// locked out.
		/// </remarks>
		public DateTime? TotpVerifiedAt { get; set; }

		/// <summary>When the account was created (UTC).</summary>
		public DateTime Created { get; set; }

		/// <summary>When the account last logged in (UTC).</summary>
		public DateTime LastLogin { get; set; }

		/// <summary>
		/// How many non-deleted characters the account owns.
		/// </summary>
		/// <remarks>
		/// Soft-deleted characters are excluded because the count exists to answer "is this a
		/// real, played account", and a row an operator already deleted is not evidence of that.
		/// </remarks>
		public int CharacterCount { get; set; }
	}

	/// <summary>
	/// The filter an operator account search applies.
	/// </summary>
	/// <remarks>
	/// An object rather than a parameter list because this grows: every new filter the panel
	/// gains would otherwise be another overload or another positional <c>bool</c> that reads
	/// identically to the one beside it at the call site.
	/// </remarks>
	public sealed class AccountAdminQuery
	{
		/// <summary>
		/// Name or email prefix to match, or null/empty for no text filter.
		/// </summary>
		/// <remarks>
		/// A prefix, not a substring. See the search implementation for why: a leading wildcard
		/// forecloses any use of the indexes on <c>name_lowercase</c> and <c>email</c>, and
		/// guaranteeing a full-table scan is not something a search box gets to do.
		/// </remarks>
		public string? Query { get; set; }

		/// <summary>Exact access level to match, or null for any level.</summary>
		public byte? AccessLevel { get; set; }

		/// <summary>
		/// Whether banned accounts appear in the results. Defaults to true.
		/// </summary>
		/// <remarks>
		/// True by default because the operator searching for an account is most often searching
		/// for one they just banned, or one they are about to unban. Hiding those by default
		/// would make the common case look like the account had vanished.
		/// </remarks>
		public bool IncludeBanned { get; set; } = true;

		/// <summary>1-based page number.</summary>
		public int Page { get; set; } = 1;

		/// <summary>Rows per page. Clamped by the service, whatever is asked for.</summary>
		public int PageSize { get; set; } = 25;
	}

	/// <summary>One page of <see cref="AccountAdminData"/>, with the total the pager needs.</summary>
	public sealed class AccountAdminPage
	{
		/// <summary>The rows on this page.</summary>
		public IReadOnlyList<AccountAdminData> Items { get; set; } = Array.Empty<AccountAdminData>();

		/// <summary>1-based page number.</summary>
		public int Page { get; set; }

		/// <summary>Rows per page.</summary>
		public int PageSize { get; set; }

		/// <summary>Total rows matching the filter, across all pages.</summary>
		public int TotalCount { get; set; }
	}
}
