using System;
using System.Collections.Generic;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>One seat's final line in a match result.</summary>
	public struct ArenaMatchResultMember
	{
		public long CharacterID;
		/// <summary>The character, when still connected to the server that ran the match; else null.</summary>
		public ICharacter Character;
		public int Team;
		public int Kills;
		public int Deaths;
		public int Score;
		/// <summary>1-based placement by score; 0 for a seat that forfeited or never arrived.</summary>
		public int Placement;
		public bool Won;
		public bool Forfeited;
		public int RankDelta;
		public int RatingDelta;
	}

	/// <summary>Everything known about a finished match, for whoever wants to hand out something for it.</summary>
	public struct ArenaMatchResult
	{
		public long MatchID;
		public ArenaTemplate Template;
		public int Format;
		public bool Ranked;
		/// <summary>Winning team, or -1 for a draw.</summary>
		public int WinnerTeam;
		public int[] TeamScores;
		public IReadOnlyList<ArenaMatchResultMember> Members;
	}

	/// <summary>
	/// Server-side hooks for the moments a match produces something: the end of a match, above all.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>This is the reward hook.</b> Rewards themselves are not decided here — currency, items,
	/// titles and achievements are a later system's business — but everything they will need is on
	/// <see cref="ArenaMatchResult"/>, and two ways to react are wired now: this C# event, and the
	/// template-authored trigger lists (<c>WinRewardTriggers</c>, <c>LossRewardTriggers</c>,
	/// <c>DrawRewardTriggers</c>) the coordinator runs on each present character right after raising
	/// it. A reward system subscribes here or authors triggers; the coordinator does not change.
	/// </para>
	/// <para>
	/// Raised on the scene server that hosted the match, on the main thread, after the result is
	/// decided and before the results screen is sent. Subscribers must not throw.
	/// </para>
	/// </remarks>
	public static class ArenaServerEvents
	{
		/// <summary>A match ended with a result. Forfeits and drops are included with <c>Placement</c> 0.</summary>
		public static event Action<ArenaMatchResult> OnMatchEnded;

		/// <summary>A match was cancelled before play. Match id, template, reason.</summary>
		public static event Action<long, ArenaTemplate, string> OnMatchCancelled;

		public static void RaiseMatchEnded(ArenaMatchResult result) => OnMatchEnded?.Invoke(result);
		public static void RaiseMatchCancelled(long matchID, ArenaTemplate template, string reason) => OnMatchCancelled?.Invoke(matchID, template, reason);
	}
}
