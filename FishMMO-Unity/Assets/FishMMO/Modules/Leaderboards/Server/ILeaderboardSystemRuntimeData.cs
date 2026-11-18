using System;
using FishMMO.Database.Data;
using FishMMO.Server.Core.Collections;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Runtime state for the leaderboard system: the request guard and the two read caches.
	/// </summary>
	/// <remarks>
	/// The caches are this server's memory of what the database said, shared by every player on
	/// it. Each scene server keeps its own; they agree to within one cache lifetime because each
	/// re-reads the same rows when its copy expires.
	/// </remarks>
	public interface ILeaderboardSystemRuntimeData : IRuntimeDataContainer
	{
		/// <summary>Per-connection debounce and in-flight tracking for board requests.</summary>
		IngressGuard IngressGuard { get; }

		/// <summary>Pages of boards, keyed by board and position.</summary>
		SingleFlightCache<LeaderboardPageKey, LeaderboardPageData> Pages { get; }

		/// <summary>Individual characters' standings, for players not on the page they are viewing.</summary>
		SingleFlightCache<LeaderboardStandingKey, LeaderboardStandingData> Standings { get; }
	}

	/// <summary>One page of one board: the unit the page cache is keyed by.</summary>
	public readonly struct LeaderboardPageKey : IEquatable<LeaderboardPageKey>
	{
		public readonly LeaderboardQuery Query;
		/// <summary>Zero-based position of the page's first row.</summary>
		public readonly int Offset;
		/// <summary>Rows on the page.</summary>
		public readonly int Limit;

		public LeaderboardPageKey(LeaderboardQuery query, int offset, int limit)
		{
			Query = query;
			Offset = offset;
			Limit = limit;
		}

		public bool Equals(LeaderboardPageKey other) => Query.Equals(other.Query) && Offset == other.Offset && Limit == other.Limit;
		public override bool Equals(object obj) => obj is LeaderboardPageKey other && Equals(other);
		public override int GetHashCode()
		{
			unchecked
			{
				return (Query.GetHashCode() * 397 ^ Offset) * 397 ^ Limit;
			}
		}
	}

	/// <summary>One character on one board: the unit the standing cache is keyed by.</summary>
	public readonly struct LeaderboardStandingKey : IEquatable<LeaderboardStandingKey>
	{
		public readonly LeaderboardQuery Query;
		public readonly long CharacterID;

		public LeaderboardStandingKey(LeaderboardQuery query, long characterID)
		{
			Query = query;
			CharacterID = characterID;
		}

		public bool Equals(LeaderboardStandingKey other) => Query.Equals(other.Query) && CharacterID == other.CharacterID;
		public override bool Equals(object obj) => obj is LeaderboardStandingKey other && Equals(other);
		public override int GetHashCode()
		{
			unchecked
			{
				return Query.GetHashCode() * 397 ^ CharacterID.GetHashCode();
			}
		}
	}
}
