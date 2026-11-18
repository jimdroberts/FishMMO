using System.Collections.Generic;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// What each local guild member's client already holds, as this scene server delivered it.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Two independent records per character. The ROSTER baseline says the client holds this
	/// guild's roster, as this server last delivered it, in one officer-note audience; only such a
	/// client may be sent a <c>GuildRosterDeltaBroadcast</c>. The audience is part of it because a
	/// member promoted into reading officer notes holds rows with that column empty, and a delta
	/// would only ever fill it in for rows that changed. The LADDER baseline says the client holds
	/// a given generation of the guild's rank ladder at a given rank of its own; the rank list is
	/// sent again only when either moves.
	/// </para>
	/// <para>
	/// Forgotten whenever the client may no longer hold what was recorded: on disconnect, when the
	/// character joins or leaves the guild here, when the pump evicts them, and — the ladder alone
	/// — whenever a rank list reaches them by a path that does not record a generation (a single
	/// member's list, the login snapshot), because that list may be older or newer than the one
	/// recorded. The two paths that do record one, the pump's delivery and the publish after a
	/// ladder edit, share <c>GuildSystem.DeliverRankListAudiences</c> and a generation per guild,
	/// so each can tell what the other has already sent. A forgotten record costs one full send; a
	/// wrong one leaves a panel stale until the guild next changes.
	/// </para>
	/// <para>
	/// Plain C#, main-thread only, like the delivery that owns it.
	/// </para>
	/// </remarks>
	internal sealed class GuildRecipientBaselines
	{
		/// <summary>A client's roster baseline: which guild, in which audience.</summary>
		private readonly struct RosterBaseline
		{
			public readonly long GuildID;
			public readonly bool OfficerAudience;

			public RosterBaseline(long guildID, bool officerAudience)
			{
				GuildID = guildID;
				OfficerAudience = officerAudience;
			}
		}

		/// <summary>A client's ladder baseline: which guild, which ladder generation, at which rank.</summary>
		private readonly struct LadderBaseline
		{
			public readonly long GuildID;
			public readonly long Generation;
			public readonly byte RankOrder;

			public LadderBaseline(long guildID, long generation, byte rankOrder)
			{
				GuildID = guildID;
				Generation = generation;
				RankOrder = rankOrder;
			}
		}

		private readonly Dictionary<long, RosterBaseline> rosters = new Dictionary<long, RosterBaseline>();
		private readonly Dictionary<long, LadderBaseline> ladders = new Dictionary<long, LadderBaseline>();

		/// <summary>Characters with a roster baseline. For tests and diagnostics.</summary>
		internal int RosterCount => rosters.Count;

		/// <summary>Characters with a ladder baseline. For tests and diagnostics.</summary>
		internal int LadderCount => ladders.Count;

		/// <summary>
		/// Whether a character's client holds this guild's roster, in this audience, from this server.
		/// </summary>
		internal bool HasRoster(long characterID, long guildID, bool officerAudience)
		{
			return rosters.TryGetValue(characterID, out RosterBaseline baseline) &&
				   baseline.GuildID == guildID &&
				   baseline.OfficerAudience == officerAudience;
		}

		/// <summary>
		/// Records that a character's client was sent this guild's full roster in this audience.
		/// </summary>
		internal void MarkRoster(long characterID, long guildID, bool officerAudience)
		{
			rosters[characterID] = new RosterBaseline(guildID, officerAudience);
		}

		/// <summary>
		/// Whether a character's client holds this ladder generation at this rank of its own.
		/// </summary>
		internal bool HasLadder(long characterID, long guildID, long generation, byte rankOrder)
		{
			return ladders.TryGetValue(characterID, out LadderBaseline baseline) &&
				   baseline.GuildID == guildID &&
				   baseline.Generation == generation &&
				   baseline.RankOrder == rankOrder;
		}

		/// <summary>
		/// Records that a character's client was sent this ladder generation at this rank.
		/// </summary>
		internal void MarkLadder(long characterID, long guildID, long generation, byte rankOrder)
		{
			ladders[characterID] = new LadderBaseline(guildID, generation, rankOrder);
		}

		/// <summary>
		/// Forgets a character's ladder baseline only, so the next delivery re-sends the rank list.
		/// </summary>
		internal void ForgetLadder(long characterID)
		{
			ladders.Remove(characterID);
		}

		/// <summary>
		/// Forgets both of a character's baselines, whatever guild they name.
		/// </summary>
		internal void Forget(long characterID)
		{
			rosters.Remove(characterID);
			ladders.Remove(characterID);
		}

		/// <summary>
		/// Forgets a character's baselines only where they name <paramref name="guildID"/>.
		/// </summary>
		/// <remarks>
		/// For leaving one guild: a character who has already moved to another holds that one's
		/// roster, and forgetting it would cost them a needless full send.
		/// </remarks>
		internal void Forget(long characterID, long guildID)
		{
			if (rosters.TryGetValue(characterID, out RosterBaseline roster) && roster.GuildID == guildID)
			{
				rosters.Remove(characterID);
			}
			if (ladders.TryGetValue(characterID, out LadderBaseline ladder) && ladder.GuildID == guildID)
			{
				ladders.Remove(characterID);
			}
		}

		/// <summary>
		/// Forgets every baseline.
		/// </summary>
		internal void Clear()
		{
			rosters.Clear();
			ladders.Clear();
		}
	}
}
