using System;
using System.Collections.Generic;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// What a pumped chat row is addressed to, as its handler routes it.
	/// </summary>
	public enum ChatPumpKeyKind : byte
	{
		/// <summary>No recognisable address. Never suppressed; the handler decides.</summary>
		None = 0,
		/// <summary>A world: World, Trade and Discord lines.</summary>
		World,
		/// <summary>A party ID.</summary>
		Party,
		/// <summary>A guild ID.</summary>
		Guild,
		/// <summary>A tell target, by lower-case name.</summary>
		Tell,
	}

	/// <summary>
	/// The relevance key of a pumped chat row: the world, party, guild or tell target it concerns.
	/// </summary>
	/// <remarks>
	/// The same key the pump's SQL filters on (<c>ChatService.FetchPumpAsync</c>): the world column
	/// for World, Trade and Discord, the message's first word for Party and Guild, and for a Tell
	/// the target's address — the quoted name when the row starts with a quote, otherwise the first
	/// word (<c>FishMMO.Shared.ChatTellAddress</c>).
	/// </remarks>
	public readonly struct ChatPumpKey : IEquatable<ChatPumpKey>
	{
		/// <summary>What kind of address this is.</summary>
		public readonly ChatPumpKeyKind Kind;

		/// <summary>The world, party or guild ID. Zero for a tell.</summary>
		public readonly long ID;

		/// <summary>The lower-case tell target. Null for everything else.</summary>
		public readonly string Name;

		private ChatPumpKey(ChatPumpKeyKind kind, long id, string name)
		{
			Kind = kind;
			ID = id;
			Name = name;
		}

		/// <summary>A world's key.</summary>
		public static ChatPumpKey World(long worldServerID) => new ChatPumpKey(ChatPumpKeyKind.World, worldServerID, null);

		/// <summary>A party's key.</summary>
		public static ChatPumpKey Party(long partyID) => new ChatPumpKey(ChatPumpKeyKind.Party, partyID, null);

		/// <summary>A guild's key.</summary>
		public static ChatPumpKey Guild(long guildID) => new ChatPumpKey(ChatPumpKeyKind.Guild, guildID, null);

		/// <summary>A tell target's key. The name is lower-cased here so callers cannot disagree.</summary>
		public static ChatPumpKey Tell(string name) => new ChatPumpKey(ChatPumpKeyKind.Tell, 0, (name ?? string.Empty).ToLowerInvariant());

		/// <summary>
		/// The first space-delimited word of a message, as PostgreSQL's
		/// <c>split_part(message, ' ', 1)</c> takes it: everything before the first space, or the
		/// whole message when there is none.
		/// </summary>
		public static string FirstWord(string message)
		{
			if (string.IsNullOrEmpty(message))
			{
				return string.Empty;
			}
			int space = message.IndexOf(' ');
			return space < 0 ? message : message.Substring(0, space);
		}

		/// <inheritdoc/>
		public bool Equals(ChatPumpKey other)
		{
			return Kind == other.Kind && ID == other.ID && string.Equals(Name, other.Name, StringComparison.Ordinal);
		}

		/// <inheritdoc/>
		public override bool Equals(object obj) => obj is ChatPumpKey other && Equals(other);

		/// <inheritdoc/>
		public override int GetHashCode()
		{
			unchecked
			{
				int hash = (int)Kind * 397;
				hash = (hash ^ ID.GetHashCode()) * 397;
				return hash ^ (Name != null ? StringComparer.Ordinal.GetHashCode(Name) : 0);
			}
		}

		/// <inheritdoc/>
		public override string ToString() => Kind == ChatPumpKeyKind.Tell ? $"Tell:{Name}" : $"{Kind}:{ID}";
	}

	/// <summary>
	/// Where one scene server's cross-server chat pump has got to, and what it has already handled.
	/// Pure bookkeeping on the database clock: no network, no database, main thread only.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why a window and not a cursor.</b> A chat row is stamped by the database clock when its
	/// INSERT runs and becomes visible when its transaction commits. Two writers can commit in the
	/// opposite order to their stamps, so a strict <c>(time, id)</c> cursor that has moved past a
	/// row's stamp before the row committed never sees it: the whisper, party or guild line is lost
	/// on every other scene server (hot-path audit H5). The pump therefore reads from
	/// <see cref="Watermark"/>, held <see cref="CommitWindow"/> behind what it has settled, and
	/// skips by ID what it has already handled (<see cref="SnapshotSeenIds"/>). A row is caught as
	/// long as it commits within the window of its stamp.
	/// </para>
	/// <para>
	/// <b>Why a key has a start.</b> The pump asks only for rows relevant to someone this server
	/// hosts. The window then has a side effect a strict cursor did not: a player who arrives
	/// from another scene server makes their party, guild and name relevant here, and the next read
	/// reaches back a whole window — re-delivering lines the server they left had already shown
	/// them. So a key that becomes relevant is only wanted from the start of the read BEFORE the one
	/// that first asks for it (<see cref="BeginRead"/>). That is exactly the exposure the strict
	/// cursor had: lines stamped between this server's last read and the arrival may reach the
	/// player twice, as they always could; nothing older does.
	/// </para>
	/// <para>
	/// <b>Why a floor.</b> Nothing stamped before this server's first read is ever read. The window
	/// reaches back from each read, and without the floor the second read would replay the window
	/// before the server started pumping.
	/// </para>
	/// </remarks>
	public sealed class ChatPumpCursor
	{
		private readonly Dictionary<long, DateTime> seen = new Dictionary<long, DateTime>();
		private readonly Dictionary<ChatPumpKey, DateTime> keySince = new Dictionary<ChatPumpKey, DateTime>();
		private readonly List<ChatPumpKey> keyScratch = new List<ChatPumpKey>();
		private readonly List<long> idScratch = new List<long>();
		private DateTime? floorUtc;

		/// <summary>
		/// How long after its stamp a row may commit and still be read.
		/// </summary>
		public TimeSpan CommitWindow { get; }

		/// <summary>
		/// The oldest stamp the next read starts from, or null before the first read, which starts at
		/// the database's own "now". Only ever moves forward.
		/// </summary>
		public DateTime? Watermark { get; private set; }

		/// <summary>
		/// The database clock at the start of the last completed read, or null before the first.
		/// </summary>
		public DateTime? LastReadStartedUtc { get; private set; }

		/// <summary>
		/// Rows handled that the next read could still return.
		/// </summary>
		public int SeenCount => seen.Count;

		/// <summary>
		/// Keys currently relevant.
		/// </summary>
		public int KeyCount => keySince.Count;

		/// <summary>
		/// Creates a cursor with the given commit window.
		/// </summary>
		public ChatPumpCursor(TimeSpan commitWindow)
		{
			CommitWindow = commitWindow > TimeSpan.Zero ? commitWindow : TimeSpan.Zero;
		}

		/// <summary>
		/// Records the keys the next read asks for. A key new since the last call is wanted from the
		/// start of the previous read; a key no longer asked for is forgotten.
		/// </summary>
		/// <param name="relevantKeys">Every key this server hosts right now.</param>
		public void BeginRead(ICollection<ChatPumpKey> relevantKeys)
		{
			keyScratch.Clear();
			foreach (KeyValuePair<ChatPumpKey, DateTime> pair in keySince)
			{
				if (relevantKeys == null || !relevantKeys.Contains(pair.Key))
				{
					keyScratch.Add(pair.Key);
				}
			}
			for (int i = 0; i < keyScratch.Count; i++)
			{
				keySince.Remove(keyScratch[i]);
			}
			keyScratch.Clear();

			if (relevantKeys == null)
			{
				return;
			}

			/* Before the first read there is no earlier read to start from; nothing before the
			 * floor is read anyway, so the key is simply wanted from the beginning. */
			DateTime since = LastReadStartedUtc ?? DateTime.MinValue;
			foreach (ChatPumpKey key in relevantKeys)
			{
				if (!keySince.ContainsKey(key))
				{
					keySince[key] = since;
				}
			}
		}

		/// <summary>
		/// The IDs of the rows handled that the next read could return again, for the read to skip.
		/// </summary>
		public long[] SnapshotSeenIds()
		{
			if (seen.Count == 0)
			{
				return Array.Empty<long>();
			}
			var ids = new long[seen.Count];
			seen.Keys.CopyTo(ids, 0);
			return ids;
		}

		/// <summary>
		/// Decides whether one row the read returned is delivered, and records it as handled either way.
		/// </summary>
		/// <param name="id">The row's ID.</param>
		/// <param name="timeCreatedUtc">The row's database-clock stamp.</param>
		/// <param name="key">The row's relevance key.</param>
		/// <returns>
		/// True to deliver. False for a row already handled, or one stamped before its key became
		/// relevant here — which the server the player came from was responsible for.
		/// </returns>
		public bool Admit(long id, DateTime timeCreatedUtc, ChatPumpKey key)
		{
			if (seen.ContainsKey(id))
			{
				return false;
			}
			seen[id] = timeCreatedUtc;

			/* A key the read did not ask for (a row the SQL matched by a rule this parse disagrees
			 * with) is not suppressed: losing a line is worse than the rare duplicate. */
			if (keySince.TryGetValue(key, out DateTime since) && timeCreatedUtc < since)
			{
				return false;
			}
			return true;
		}

		/// <summary>
		/// Settles a completed read: moves the watermark and forgets rows it has passed.
		/// </summary>
		/// <param name="readStartedUtc">The database clock taken before the read's first page.</param>
		/// <param name="drained">True if the read caught up; false if it stopped at its page limit.</param>
		/// <param name="lastRowTimeUtc">The stamp of the last row read, or null if none were.</param>
		public void CompleteRead(DateTime readStartedUtc, bool drained, DateTime? lastRowTimeUtc)
		{
			if (!floorUtc.HasValue)
			{
				floorUtc = readStartedUtc;
			}

			/* Settled up to where the read got: everything committed before the read started when it
			 * drained, only up to its last row when it stopped at its page limit. */
			DateTime settled = readStartedUtc;
			if (!drained && lastRowTimeUtc.HasValue && lastRowTimeUtc.Value < settled)
			{
				settled = lastRowTimeUtc.Value;
			}

			DateTime candidate = settled - CommitWindow;
			if (candidate < floorUtc.Value)
			{
				candidate = floorUtc.Value;
			}
			if (!Watermark.HasValue || candidate > Watermark.Value)
			{
				Watermark = candidate;
			}

			LastReadStartedUtc = readStartedUtc;

			// Rows behind the watermark can never be returned again.
			idScratch.Clear();
			foreach (KeyValuePair<long, DateTime> pair in seen)
			{
				if (pair.Value < Watermark.Value)
				{
					idScratch.Add(pair.Key);
				}
			}
			for (int i = 0; i < idScratch.Count; i++)
			{
				seen.Remove(idScratch[i]);
			}
			idScratch.Clear();
		}

		/// <summary>
		/// Pins the floor at the start of a first read whose rows never reached this cursor.
		/// </summary>
		/// <param name="readStartedUtc">The database clock taken before that read's first page.</param>
		/// <remarks>
		/// The first read has no watermark and starts at the database's "now". If its rows are lost
		/// on the way back (the main-thread queue refused them), nothing recorded where it started,
		/// and the next read would start at a later "now" and skip everything committed in
		/// between. Pinning its start here makes the next read cover that span. Does nothing once a
		/// read has completed.
		/// </remarks>
		public void PinFloor(DateTime readStartedUtc)
		{
			if (floorUtc.HasValue || Watermark.HasValue)
			{
				return;
			}
			floorUtc = readStartedUtc;
			Watermark = readStartedUtc;
		}

		/// <summary>
		/// Forgets everything, for a server that is starting its pump again.
		/// </summary>
		public void Reset()
		{
			seen.Clear();
			keySince.Clear();
			floorUtc = null;
			Watermark = null;
			LastReadStartedUtc = null;
		}
	}
}
