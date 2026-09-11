using System;
using System.Collections.Generic;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// One persisted chat message as an operator needs to see it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Deliberately not <see cref="ChatData"/>. That type is the game's transfer shape: the scene
	/// servers fetch it to re-broadcast a message, so it is built for delivery and its fields are
	/// the ones delivery needs. This one is built for reading a report, which is a different job —
	/// it keeps both timestamps rather than treating them as interchangeable, because the question
	/// an operator is answering ("when did they say it") and the question the chat pump is
	/// answering ("what is new since my cursor") are not the same question.
	/// </para>
	/// <para>
	/// It carries no <c>Version</c>. The chat table is append-only in practice — nothing updates a
	/// message after it is written — so the concurrency token tells an operator nothing, and a
	/// column on an operator screen that is always 1 trains people to ignore columns.
	/// </para>
	/// <para>
	/// <b>Every string here is player-supplied.</b> <see cref="Message"/> is whatever somebody
	/// typed into the chat box, and <see cref="CharacterName"/> reaches this table denormalized,
	/// without passing back through the character row's validation. Anything rendering these has
	/// to escape them; this is the one screen in an operator panel whose content is written by the
	/// person being investigated.
	/// </para>
	/// </remarks>
	public sealed class ChatAdminData
	{
		/// <summary>Primary key. Also the tie-break that orders messages inside one clock tick.</summary>
		public long ID { get; set; }

		/// <summary>
		/// The character that sent the message.
		/// </summary>
		/// <remarks>
		/// The character row may no longer exist: chat outlives deletion by design, which is the
		/// whole reason the names below are denormalized onto it. A panel linking to the character
		/// has to tolerate the link leading nowhere.
		/// </remarks>
		public long CharacterID { get; set; }

		/// <summary>The sender's character name, as recorded when the message was written.</summary>
		/// <remarks>
		/// A snapshot, not a lookup. If the character has since been renamed this shows the name
		/// they were using at the time, which is what a report about that name needs.
		/// </remarks>
		public string CharacterName { get; set; }

		/// <summary>The sender's account name, as recorded when the message was written.</summary>
		public string AccountName { get; set; }

		/// <summary>The world server the message originated on.</summary>
		public long WorldServerID { get; set; }

		/// <summary>The scene server the message originated on.</summary>
		public long SceneServerID { get; set; }

		/// <summary>The channel, as the raw <c>ChatChannel</c> value.</summary>
		/// <remarks>
		/// Kept as the byte rather than the enum so a row written by a newer server, carrying a
		/// channel this build has no name for, still reads back instead of throwing. Whatever
		/// presents this is responsible for naming it: "channel 5" is not an answer to a report.
		/// </remarks>
		public byte Channel { get; set; }

		/// <summary>The message body, exactly as the player typed it.</summary>
		public string Message { get; set; }

		/// <summary>When the server received the message (UTC).</summary>
		/// <remarks>
		/// This is the legal-audit instant: it is stamped by the scene server that took the
		/// message from the player, so it survives the message sitting in a persistence batch.
		/// </remarks>
		public DateTime ServerReceivedTime { get; set; }

		/// <summary>When the row was written (UTC).</summary>
		/// <remarks>
		/// The row's own clock, and the column every index and every ordering here uses. It can
		/// trail <see cref="ServerReceivedTime"/> by the width of a persistence batch, so the two
		/// are shown separately rather than one standing in for the other.
		/// </remarks>
		public DateTime TimeCreated { get; set; }
	}

	/// <summary>
	/// The filter an operator chat search applies.
	/// </summary>
	/// <remarks>
	/// <para>
	/// An object rather than a parameter list, for the reason <see cref="AccountAdminQuery"/>
	/// gives: eight optional filters as positional arguments is eight chances to transpose two of
	/// them at a call site where both are strings and nothing complains.
	/// </para>
	/// <para>
	/// Every filter is optional and null means "do not narrow on this", with one exception that
	/// the service enforces rather than the type: see <see cref="Text"/>.
	/// </para>
	/// </remarks>
	public sealed class ChatAdminQuery
	{
		/// <summary>Exact character name to match, or null for any sender.</summary>
		/// <remarks>
		/// Exact, not a prefix, and there is no index behind it — see the search implementation.
		/// An operator handling a report has the name in front of them, copied from the report, so
		/// matching it exactly costs them nothing and keeps the scan it forces as narrow as it can
		/// be.
		/// </remarks>
		public string? CharacterName { get; set; }

		/// <summary>Exact account name to match, or null for any account.</summary>
		/// <remarks>
		/// The account filter is what catches somebody using a second character to continue the
		/// same harassment, which is why it is separate from <see cref="CharacterName"/> rather
		/// than one combined "who" box.
		/// </remarks>
		public string? AccountName { get; set; }

		/// <summary>Exact <c>ChatChannel</c> value to match, or null for every channel.</summary>
		/// <remarks>
		/// A byte rather than the enum so an unrecognised channel can still be searched for, and
		/// so a caller parsing a query string does not have to know the enum to pass one through.
		/// </remarks>
		public byte? Channel { get; set; }

		/// <summary>Substring the message must contain, or null for no text filter.</summary>
		/// <remarks>
		/// <para>
		/// A substring, unlike <see cref="AccountAdminQuery.Query"/>, which is a prefix precisely
		/// to stay index-serviceable. There is no prefix version of this filter that answers the
		/// question: a report says "they called me &lt;slur&gt;", and the slur is in the middle of
		/// the sentence. So this one accepts the leading wildcard it needs.
		/// </para>
		/// <para>
		/// What it does not accept is being the ONLY filter. Supplied alone, it is refused with a
		/// validation error rather than silently bounded; see the search implementation for why
		/// refusing beats defaulting.
		/// </para>
		/// </remarks>
		public string? Text { get; set; }

		/// <summary>Inclusive lower bound on <see cref="ChatAdminData.TimeCreated"/>, or null.</summary>
		/// <remarks>
		/// This is the bound that actually narrows a search: it is the one an index on
		/// <c>time_created</c> can start a range scan from. <see cref="ToUtc"/> alone cannot.
		/// </remarks>
		public DateTime? FromUtc { get; set; }

		/// <summary>Exclusive upper bound on <see cref="ChatAdminData.TimeCreated"/>, or null.</summary>
		/// <remarks>
		/// Exclusive so that two adjacent windows tile without a row appearing in both, matching
		/// the convention <c>AdminAuditService.SearchAsync</c> already set.
		/// </remarks>
		public DateTime? ToUtc { get; set; }

		/// <summary>1-based page number.</summary>
		public int Page { get; set; } = 1;

		/// <summary>Rows per page. Clamped by the service, whatever is asked for.</summary>
		public int PageSize { get; set; } = ChatAdminQuery.DefaultPageSize;

		/// <summary>The page size used when a caller asks for none.</summary>
		/// <remarks>
		/// Fifty, rather than the twenty-five an account search uses: a conversation is read in
		/// sequence, and a page that ends mid-exchange makes an operator page back and forth to
		/// reconstruct it.
		/// </remarks>
		public const int DefaultPageSize = 50;

		/// <summary>The largest page the service will return, whatever is asked for.</summary>
		/// <remarks>
		/// The cap exists because the page size is the only part of this query a caller controls
		/// that has no upper bound of its own: every other filter can only narrow the result, while
		/// <c>pageSize=1000000</c> asks the server to materialize the table.
		/// </remarks>
		public const int MaxPageSize = 200;

		/// <summary>
		/// Whether this query narrows the search by something other than the message text.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Deliberately does not count <see cref="ToUtc"/> or <see cref="Channel"/>. An upper time
		/// bound with no lower bound spans the whole history up to that instant, and a channel
		/// filter divides the table by a ten-valued column where the common values hold most of the
		/// rows — neither one turns a scan into anything smaller. Counting them would make the
		/// guard below feel satisfied while changing nothing about what the database does.
		/// </para>
		/// </remarks>
		public bool HasNarrowingFilter =>
			!string.IsNullOrWhiteSpace(CharacterName) ||
			!string.IsNullOrWhiteSpace(AccountName) ||
			FromUtc.HasValue;
	}

	/// <summary>One page of <see cref="ChatAdminData"/>, with the total the pager needs.</summary>
	public sealed class ChatAdminPage
	{
		/// <summary>The rows on this page, newest first.</summary>
		public IReadOnlyList<ChatAdminData> Items { get; set; } = Array.Empty<ChatAdminData>();

		/// <summary>1-based page number.</summary>
		public int Page { get; set; }

		/// <summary>Rows per page.</summary>
		public int PageSize { get; set; }

		/// <summary>Total rows matching the filter, across all pages.</summary>
		public int TotalCount { get; set; }
	}
}
