using System;
using System.Collections.Generic;
using FishMMO.Shared;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// The rules for sending a guild roster as a delta rather than whole.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A scene server keeps the roster it last delivered for each guild it hosts members of, as
	/// full rows (officer notes included), and compares each freshly read roster against it. Which
	/// rows count as changed depends on the AUDIENCE: a recipient who may not read officer notes
	/// holds rows with that column empty, so an officer-note edit is no change at all to them.
	/// </para>
	/// <para>
	/// Pure and static so the rules can be pinned by a test without a server; the delivery itself
	/// is <c>GuildSystem.ApplyGuildSnapshot</c>.
	/// </para>
	/// </remarks>
	internal static class GuildRosterDelta
	{
		/// <summary>
		/// The location label a member carries while not logged in anywhere.
		/// </summary>
		/// <remarks>
		/// The one definition of the label. The disconnect path writes it, the delta rule reads it,
		/// and the client's guild panel compares against the same text.
		/// </remarks>
		internal const string OfflineLocation = "Offline";

		/// <summary>How one audience's copy of a roster change is delivered.</summary>
		internal enum Delivery : byte
		{
			/// <summary>Nothing this audience can see has changed; send nothing.</summary>
			None = 0,
			/// <summary>Send the changed rows and the removals.</summary>
			Delta = 1,
			/// <summary>Send the whole roster.</summary>
			Full = 2,
		}

		/// <summary>
		/// Whether a location label means the member is not logged in.
		/// </summary>
		/// <param name="location">The row's location label.</param>
		/// <returns>True for <see cref="OfflineLocation"/>, compared case-insensitively.</returns>
		internal static bool IsOffline(string location)
		{
			return string.Equals(location, OfflineLocation, StringComparison.OrdinalIgnoreCase);
		}

		/// <summary>
		/// Whether a member's row changed in a way the given audience can see.
		/// </summary>
		/// <param name="previous">The row as last delivered (full projection).</param>
		/// <param name="current">The row as just read (full projection).</param>
		/// <param name="officerAudience">True when the recipients may read officer notes.</param>
		/// <returns>True when the row must be sent to this audience.</returns>
		/// <remarks>
		/// <para>
		/// <b>Last-seen counts only while the member is offline.</b> It is the character's last
		/// save, and an online character saves on a timer, so comparing it unconditionally would
		/// put every online member into every delta — the whole roster again, one row at a time.
		/// The client renders last-seen only for an offline member, and a member going offline
		/// changes their location, which sends the whole row with its current last-seen anyway.
		/// </para>
		/// <para>
		/// The officer note counts only for the audience that receives it. The public copy of the
		/// row carries it empty, so a change to it changes nothing a public recipient holds.
		/// </para>
		/// </remarks>
		internal static bool RowChanged(GuildAddEntry previous, GuildAddEntry current, bool officerAudience)
		{
			if (previous.RankOrder != current.RankOrder ||
				previous.RaceID != current.RaceID ||
				!string.Equals(previous.Location ?? string.Empty, current.Location ?? string.Empty, StringComparison.Ordinal) ||
				!string.Equals(previous.PublicNote ?? string.Empty, current.PublicNote ?? string.Empty, StringComparison.Ordinal))
			{
				return true;
			}

			if (officerAudience &&
				!string.Equals(previous.OfficerNote ?? string.Empty, current.OfficerNote ?? string.Empty, StringComparison.Ordinal))
			{
				return true;
			}

			return IsOffline(current.Location) && previous.LastOnlineUnixSeconds != current.LastOnlineUnixSeconds;
		}

		/// <summary>
		/// Compares a freshly read roster with the one last delivered, for one audience.
		/// </summary>
		/// <param name="previous">The roster as last delivered, keyed by character ID (full projection).</param>
		/// <param name="current">The roster as just read (full projection).</param>
		/// <param name="officerAudience">True when the recipients may read officer notes.</param>
		/// <param name="changedIndices">Receives the indices into <paramref name="current"/> of rows added or changed.</param>
		/// <param name="removedIDs">Receives the character IDs in <paramref name="previous"/> and not in <paramref name="current"/>.</param>
		internal static void Diff(IReadOnlyDictionary<long, GuildAddEntry> previous, IReadOnlyList<GuildAddEntry> current, bool officerAudience, List<int> changedIndices, List<long> removedIDs)
		{
			changedIndices.Clear();
			removedIDs.Clear();

			HashSet<long> present = new HashSet<long>();
			if (current != null)
			{
				for (int i = 0; i < current.Count; ++i)
				{
					GuildAddEntry row = current[i];
					present.Add(row.CharacterID);

					if (previous == null ||
						!previous.TryGetValue(row.CharacterID, out GuildAddEntry before) ||
						RowChanged(before, row, officerAudience))
					{
						changedIndices.Add(i);
					}
				}
			}

			if (previous != null)
			{
				foreach (long characterID in previous.Keys)
				{
					if (!present.Contains(characterID))
					{
						removedIDs.Add(characterID);
					}
				}
			}
		}

		/// <summary>
		/// How to deliver one audience's copy of a roster change.
		/// </summary>
		/// <param name="hasBaseline">Whether the recipients hold this guild's roster, in this audience's projection, from this server.</param>
		/// <param name="changedRows">Rows added or changed for this audience.</param>
		/// <param name="removedRows">Rows removed.</param>
		/// <param name="rosterCount">Rows in the roster as it now stands.</param>
		/// <returns>The delivery.</returns>
		/// <remarks>
		/// A recipient without a baseline has nothing to apply a delta to, so it is sent the whole
		/// roster. And when MORE than half the roster changed the whole roster goes too: a delta
		/// that large costs about as much as the roster and would be applied row by row where one
		/// roster is applied at once.
		/// </remarks>
		internal static Delivery Choose(bool hasBaseline, int changedRows, int removedRows, int rosterCount)
		{
			if (!hasBaseline)
			{
				return Delivery.Full;
			}

			int changes = changedRows + removedRows;
			if (changes < 1)
			{
				return Delivery.None;
			}

			return changes * 2 > rosterCount ? Delivery.Full : Delivery.Delta;
		}

		/// <summary>
		/// Whether two wire ladders say the same thing, rank by rank, in order.
		/// </summary>
		/// <param name="a">One ladder.</param>
		/// <param name="b">The other.</param>
		/// <returns>True when both have the same ranks with the same names and masks.</returns>
		internal static bool SameLadder(GuildRankEntry[] a, GuildRankEntry[] b)
		{
			if (ReferenceEquals(a, b))
			{
				return true;
			}
			if (a == null || b == null || a.Length != b.Length)
			{
				return false;
			}

			for (int i = 0; i < a.Length; ++i)
			{
				if (a[i].RankOrder != b[i].RankOrder ||
					a[i].Permissions != b[i].Permissions ||
					!string.Equals(a[i].Name ?? string.Empty, b[i].Name ?? string.Empty, StringComparison.Ordinal))
				{
					return false;
				}
			}

			return true;
		}
	}
}
