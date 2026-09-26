using System;
using System.Collections.Generic;

namespace FishMMO.Database.Data
{
	/// <summary>
	/// One read of the cross-server chat pump: where to start, what the reader has already seen,
	/// and which rows are worth sending it at all.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Relevance is decided by the database, not by the reader.</b> Every scene server used to
	/// fetch every other server's World, Trade, Tell, Party and Guild lines in one shared page, so
	/// the page budget was spent on rows the reader then threw away. The arrays below name what
	/// this reader hosts; a row that concerns none of it is never returned, and a busy shard no
	/// longer starves a quiet server's whispers.
	/// </para>
	/// <para>
	/// A row is matched by the same key its handler routes it by: the world column for World, Trade
	/// and Discord, and the first word of the message for Party (party ID), Guild (guild ID) and
	/// Tell (target name, compared case-insensitively). An empty array matches nothing.
	/// </para>
	/// </remarks>
	public sealed class ChatPumpQuery
	{
		/// <summary>
		/// Oldest <c>time_created</c> to read, by the database clock. Null starts at the database's
		/// own "now", for a reader that has not read before.
		/// </summary>
		public DateTime? FromUtc { get; set; }

		/// <summary>
		/// Row IDs the reader has already handled. The read trails behind its own progress to catch
		/// rows committed out of stamp order, so without this every row in that window would come
		/// back on every read.
		/// </summary>
		public long[] ExcludeIds { get; set; } = Array.Empty<long>();

		/// <summary>
		/// The reading scene server. Rows it wrote itself on channels it has already delivered
		/// locally are its own echo and are not returned.
		/// </summary>
		public long SceneServerId { get; set; }

		/// <summary>World server IDs with a character on this scene server (World, Trade, Discord).</summary>
		public long[] WorldServerIds { get; set; } = Array.Empty<long>();

		/// <summary>Party IDs with a member on this scene server.</summary>
		public long[] PartyIds { get; set; } = Array.Empty<long>();

		/// <summary>Guild IDs with a member on this scene server.</summary>
		public long[] GuildIds { get; set; } = Array.Empty<long>();

		/// <summary>Lower-case names of the characters on this scene server, the only possible tell targets here.</summary>
		public string[] TellTargetsLowerCase { get; set; } = Array.Empty<string>();

		/// <summary>Rows per page.</summary>
		public int PageSize { get; set; } = 100;

		/// <summary>
		/// Most pages read in one call. Reading continues while a page comes back full, so this is
		/// what bounds one call's work when the reader has fallen behind.
		/// </summary>
		public int MaxPages { get; set; } = 10;
	}

	/// <summary>
	/// One read of the chat table by a reader that takes every game row: the Discord relay.
	/// </summary>
	/// <remarks>
	/// The same window as <see cref="ChatPumpQuery"/> (see <see cref="ChatReadWindow"/>), without
	/// the relevance filter or the echo rule: the relay scans every row for account-link codes and
	/// decides for itself which channels it republishes. Rows bridged in from Discord are never
	/// returned, so the relay cannot echo its own traffic.
	/// </remarks>
	public sealed class ChatRelayQuery
	{
		/// <summary>
		/// Oldest <c>time_created</c> to read, by the database clock. Null starts at the database's
		/// own "now", for a reader that has not read before — so a reader that starts does not
		/// replay history.
		/// </summary>
		public DateTime? FromUtc { get; set; }

		/// <summary>
		/// Row IDs the reader has already handled. See <see cref="ChatPumpQuery.ExcludeIds"/>.
		/// </summary>
		public long[] ExcludeIds { get; set; } = Array.Empty<long>();

		/// <summary>Rows per page.</summary>
		public int PageSize { get; set; } = 100;

		/// <summary>
		/// Most pages read in one call. Reading continues while a page comes back full, so this is
		/// what bounds one call's work when the reader has fallen behind.
		/// </summary>
		public int MaxPages { get; set; } = 10;
	}

	/// <summary>
	/// The result of one <see cref="ChatPumpQuery"/> or <see cref="ChatRelayQuery"/>.
	/// </summary>
	public sealed class ChatPumpPage
	{
		/// <summary>The rows read, in <c>(time_created, id)</c> order.</summary>
		public List<ChatData> Messages { get; set; } = new List<ChatData>();

		/// <summary>
		/// The database clock (UTC) taken before the first page was read. Every row committed before
		/// this instant, at or after the query's start and relevant to it, is either in
		/// <see cref="Messages"/> or excluded as already seen — when <see cref="Drained"/> is true.
		/// </summary>
		public DateTime ReadStartedUtc { get; set; }

		/// <summary>
		/// True when the last page came back short: the reader has caught up. False when the read
		/// stopped at <see cref="ChatPumpQuery.MaxPages"/> with rows still waiting.
		/// </summary>
		public bool Drained { get; set; }
	}
}
