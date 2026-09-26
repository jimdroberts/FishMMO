using System;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// One player's line on the results screen.
	/// </summary>
	public struct ArenaPlacement
	{
		public long CharacterID;
		public int Team;
		public int Kills;
		public int Deaths;
		public int Score;
		/// <summary>1-based place, by score only: team and group are not considered.</summary>
		public int Place;
	}

	/// <summary>Where a team's flag is.</summary>
	public enum ArenaFlagState : byte
	{
		/// <summary>Resting on its stand.</summary>
		Home = 0,
		/// <summary>Being carried by an enemy.</summary>
		Carried = 1,
		/// <summary>Lying where its carrier died or left. Its own team returns it; an enemy picks it up.</summary>
		Dropped = 2,
	}

	/// <summary>What an interaction with a flag stand does.</summary>
	public enum ArenaFlagAction : byte
	{
		/// <summary>Nothing: own stand with nothing to deliver, enemy stand already emptied, or not seated.</summary>
		None = 0,
		/// <summary>Take the enemy's flag from its stand.</summary>
		PickUp = 1,
		/// <summary>Deliver the carried enemy flag at the own stand: a capture.</summary>
		Capture = 2,
		/// <summary>Return a dropped flag of the actor's own team to its stand.</summary>
		Return = 3,
	}

	/// <summary>The outcome of interacting with a control point.</summary>
	public struct ArenaControlPointResult
	{
		/// <summary>Owner after the interaction, or -1 for neutral.</summary>
		public int OwnerTeam;
		/// <summary>Team whose capture is in progress, or -1.</summary>
		public int ProgressTeam;
		/// <summary>Interactions accumulated towards a capture.</summary>
		public int Progress;
		/// <summary>True when this interaction completed a capture.</summary>
		public bool Captured;
	}

	/// <summary>
	/// The decisions an arena match makes, as pure functions: whether a format can be played in a
	/// scene, when a match is over and who won, how players place, what a result does to a rank,
	/// and what an interaction with an objective does.
	/// </summary>
	/// <remarks>
	/// Kept free of network, database and Unity state so each rule is a truth table that can be
	/// tested without a server. The match coordinator calls these; nothing here decides anything
	/// on its own clock.
	/// </remarks>
	public static class ArenaRules
	{
		/// <summary>
		/// Whether a scene can hold a full match at a format.
		/// </summary>
		/// <param name="template">The arena. Null is not playable.</param>
		/// <param name="format">Format index.</param>
		/// <param name="sceneMaxClients">The scene's declared capacity.</param>
		/// <returns>The match size when playable, or 0.</returns>
		public static int ResolveMatchSize(ArenaTemplate template, int format, int sceneMaxClients)
		{
			if (template == null || !template.IsValidFormat(format))
			{
				return 0;
			}

			int size = template.GetMatchSize(format);
			if (sceneMaxClients > 0 && size > sceneMaxClients)
			{
				return 0;
			}
			return size;
		}

		/// <summary>
		/// Whether a pre-made group may queue for a format: it must fit on one team.
		/// </summary>
		public static bool GroupFitsFormat(int groupSize, int teamSize)
		{
			return groupSize >= 1 && teamSize >= 1 && groupSize <= teamSize;
		}

		/// <summary>
		/// Decides whether a live match has ended, and who won.
		/// </summary>
		/// <param name="teamScores">Score per team.</param>
		/// <param name="scoreLimit">Score that ends it at once, or 0 for none.</param>
		/// <param name="timeUp">Whether the clock has run out.</param>
		/// <param name="teamsWithPlayers">How many teams still have at least one player in the match.</param>
		/// <param name="winnerTeam">Winning team index, or -1 for a draw or an undecided match.</param>
		/// <returns>True when the match is over.</returns>
		/// <remarks>
		/// A team reaching the limit wins immediately. Time running out gives the match to the
		/// highest score, or draws it. A match with only one team left standing — everybody else
		/// left or was removed — ends at once in that team's favour, because there is nobody left
		/// to score against.
		/// </remarks>
		public static bool ResolveOutcome(IReadOnlyList<int> teamScores, int scoreLimit, bool timeUp, int teamsWithPlayers, out int winnerTeam)
		{
			winnerTeam = -1;
			if (teamScores == null || teamScores.Count < 2)
			{
				return true;
			}

			int best = -1, bestScore = int.MinValue; bool tie = false;
			for (int t = 0; t < teamScores.Count; ++t)
			{
				if (teamScores[t] > bestScore)
				{
					bestScore = teamScores[t]; best = t; tie = false;
				}
				else if (teamScores[t] == bestScore)
				{
					tie = true;
				}
			}

			if (scoreLimit > 0 && bestScore >= scoreLimit && !tie)
			{
				winnerTeam = best;
				return true;
			}

			if (teamsWithPlayers <= 1)
			{
				// Walkover: the only team left wins whatever the score says, unless nobody is left.
				winnerTeam = teamsWithPlayers == 1 ? -2 : -1;
				return true;
			}

			if (timeUp)
			{
				winnerTeam = tie ? -1 : best;
				return true;
			}

			return false;
		}

		/// <summary>
		/// Orders players for the results screen: score, then kills, then fewer deaths, then a
		/// stable tie-break on character id so two clients agree.
		/// </summary>
		/// <remarks>
		/// By score only. Team and group are deliberately not part of the order: the pedestal is
		/// about who did the most, not who won.
		/// </remarks>
		public static List<ArenaPlacement> ResolvePlacements(IReadOnlyList<ArenaPlacement> members)
		{
			var placed = new List<ArenaPlacement>(members?.Count ?? 0);
			if (members == null)
			{
				return placed;
			}
			placed.AddRange(members);
			placed.Sort((a, b) =>
			{
				int c = b.Score.CompareTo(a.Score);
				if (c != 0) return c;
				c = b.Kills.CompareTo(a.Kills);
				if (c != 0) return c;
				c = a.Deaths.CompareTo(b.Deaths);
				if (c != 0) return c;
				return a.CharacterID.CompareTo(b.CharacterID);
			});
			for (int i = 0; i < placed.Count; ++i)
			{
				ArenaPlacement p = placed[i];
				p.Place = i + 1;
				placed[i] = p;
			}
			return placed;
		}

		/// <summary>
		/// The rank change for one player.
		/// </summary>
		/// <param name="currentRank">Rank before the match.</param>
		/// <param name="team">The player's team.</param>
		/// <param name="winnerTeam">Winning team, or -1 for a draw.</param>
		/// <param name="winPoints">Points for a win.</param>
		/// <param name="lossPoints">Points lost for a loss.</param>
		/// <returns>Signed delta such that <c>currentRank + delta</c> is never below zero.</returns>
		public static int ResolveRankDelta(int currentRank, int team, int winnerTeam, int winPoints, int lossPoints)
		{
			if (winnerTeam < 0)
			{
				return 0;
			}
			if (team == winnerTeam)
			{
				return Math.Max(0, winPoints);
			}
			int loss = Math.Max(0, lossPoints);
			return -Math.Min(loss, Math.Max(0, currentRank));
		}

		/// <summary>
		/// What interacting with a flag stand does for one player.
		/// </summary>
		/// <param name="standTeam">Team whose flag rests on the stand.</param>
		/// <param name="standFlag">Where that team's flag currently is.</param>
		/// <param name="actorTeam">The interacting player's team, or -1 when not seated.</param>
		/// <param name="actorCarriesFlag">Whether the player is carrying an enemy flag.</param>
		/// <remarks>
		/// Classic rules: an enemy takes a flag that is home; the flag's owner delivers a carried
		/// enemy flag at their own stand only while their own flag is home, so a team whose flag is
		/// out cannot score until they get it back. Nobody takes their own flag, and nobody takes a
		/// flag that is already out.
		/// </remarks>
		public static ArenaFlagAction ResolveFlagInteraction(int standTeam, ArenaFlagState standFlag, int actorTeam, bool actorCarriesFlag)
		{
			if (actorTeam < 0)
			{
				return ArenaFlagAction.None;
			}

			if (actorTeam == standTeam)
			{
				return actorCarriesFlag && standFlag == ArenaFlagState.Home ? ArenaFlagAction.Capture : ArenaFlagAction.None;
			}

			return standFlag == ArenaFlagState.Home && !actorCarriesFlag ? ArenaFlagAction.PickUp : ArenaFlagAction.None;
		}

		/// <summary>
		/// What touching a dropped flag lying on the ground does for one player.
		/// </summary>
		/// <param name="flagTeam">Team the flag belongs to.</param>
		/// <param name="actorTeam">The touching player's team, or -1 when not seated.</param>
		/// <param name="actorCarriesFlag">Whether the player is already carrying an enemy flag.</param>
		/// <remarks>
		/// Its own team returns it home with a touch; an enemy not already carrying a flag picks it
		/// up and carries on. Nobody carries two.
		/// </remarks>
		public static ArenaFlagAction ResolveDroppedFlagTouch(int flagTeam, int actorTeam, bool actorCarriesFlag)
		{
			if (actorTeam < 0)
			{
				return ArenaFlagAction.None;
			}
			if (actorTeam == flagTeam)
			{
				return ArenaFlagAction.Return;
			}
			return actorCarriesFlag ? ArenaFlagAction.None : ArenaFlagAction.PickUp;
		}

		/// <summary>
		/// What interacting with a control point does for one player.
		/// </summary>
		/// <param name="ownerTeam">Current owner, or -1 for neutral.</param>
		/// <param name="progressTeam">Team whose capture is in progress, or -1.</param>
		/// <param name="progress">Interactions accumulated by that team.</param>
		/// <param name="actorTeam">The interacting player's team, or -1 when not seated.</param>
		/// <param name="interactionsToCapture">Interactions needed to take the point.</param>
		/// <remarks>
		/// A team that already owns the point gains nothing by touching it. Another team's touch
		/// advances their own progress; a third team's touch, or a touch by a team other than the
		/// one in progress, restarts progress for the toucher's team — contesting is a race, not a
		/// tug of war. Reaching the count flips ownership and clears progress.
		/// </remarks>
		public static ArenaControlPointResult ResolveControlPointInteraction(int ownerTeam, int progressTeam, int progress, int actorTeam, int interactionsToCapture)
		{
			var result = new ArenaControlPointResult { OwnerTeam = ownerTeam, ProgressTeam = progressTeam, Progress = progress, Captured = false };
			if (actorTeam < 0 || actorTeam == ownerTeam)
			{
				return result;
			}

			int needed = Math.Max(1, interactionsToCapture);
			int next = progressTeam == actorTeam ? progress + 1 : 1;
			if (next >= needed)
			{
				result.OwnerTeam = actorTeam;
				result.ProgressTeam = -1;
				result.Progress = 0;
				result.Captured = true;
				return result;
			}

			result.ProgressTeam = actorTeam;
			result.Progress = next;
			return result;
		}

		/// <summary>
		/// Picks the respawn points that belong to a team from a scene's respawn dictionary.
		/// </summary>
		/// <param name="respawnKeys">All respawn point names in the scene.</param>
		/// <param name="prefix">The team's prefix, or null.</param>
		/// <returns>Matching names, or every name when the prefix matches nothing or is null.</returns>
		public static List<string> ResolveTeamSpawnKeys(IEnumerable<string> respawnKeys, string prefix)
		{
			var all = new List<string>();
			var mine = new List<string>();
			if (respawnKeys != null)
			{
				foreach (string key in respawnKeys)
				{
					if (string.IsNullOrEmpty(key))
					{
						continue;
					}
					all.Add(key);
					if (!string.IsNullOrEmpty(prefix) && key.StartsWith(prefix, StringComparison.Ordinal))
					{
						mine.Add(key);
					}
				}
			}
			return mine.Count > 0 ? mine : all;
		}

		/// <summary>
		/// The whole second a phase clock shows when the one-second arena tick samples it.
		/// </summary>
		/// <param name="secondsRemaining">Real seconds until the phase ends; negative once past it.</param>
		/// <returns>The second to announce, never below 0.</returns>
		/// <remarks>
		/// <para>
		/// Rounded to the nearest second, not up. Every timed phase begins inside the tick, so the
		/// tick samples each phase clock at whole seconds give or take a frame. Rounded up, every
		/// sample sat on a step and the frame's jitter picked which side of it the number fell:
		/// one second was announced twice and the next skipped, which silenced that second's
		/// countdown cue, lost a time warning, and could start or end a match a second late.
		/// Rounded to nearest, every step is half a tick away from every sample.
		/// </para>
		/// <para>
		/// Half a second exactly rounds down, and 0 means the phase is over: the tick nearest a
		/// phase's end ends it, not the first tick after it.
		/// </para>
		/// </remarks>
		public static int ResolveTickSecond(double secondsRemaining)
		{
			if (double.IsNaN(secondsRemaining) || secondsRemaining <= 0.5)
			{
				return 0;
			}
			if (secondsRemaining >= int.MaxValue)
			{
				return int.MaxValue;
			}
			return (int)Math.Ceiling(secondsRemaining - 0.5);
		}

		/// <summary>
		/// The countdown seconds a tick must announce: every one passed since the last
		/// announcement, so a late tick, or a hitch that merged several, still announces each.
		/// </summary>
		/// <param name="lastAnnounced">The second announced last, or a negative value when none has been.</param>
		/// <param name="current">The second the clock shows now (see <see cref="ResolveTickSecond"/>).</param>
		/// <param name="highest">The first second to announce; the rest follow it downwards.</param>
		/// <returns>How many seconds to announce, from <paramref name="highest"/> down; 0 when the clock has not moved.</returns>
		/// <remarks>
		/// Each second carries its own designer cues, so a skipped one is a cue that never plays.
		/// Nothing is announced twice: a clock that stood still, or went backwards with the wall
		/// clock, announces nothing.
		/// </remarks>
		public static int ResolveCountdownSteps(int lastAnnounced, int current, out int highest)
		{
			current = Math.Max(0, current);
			if (lastAnnounced < 0)
			{
				highest = current;
				return 1;
			}
			highest = lastAnnounced - 1;
			return highest >= current ? highest - current + 1 : 0;
		}

		/// <summary>
		/// Whether a clock that moved from one second to another passed a threshold on the way.
		/// </summary>
		/// <param name="previousSecond">The second the clock showed at the last check.</param>
		/// <param name="currentSecond">The second it shows now.</param>
		/// <param name="threshold">The second a warning is authored at.</param>
		/// <remarks>
		/// A threshold is passed once the clock is at or below it having been above it. Testing
		/// for the exact second, as the time warnings used to, lost every warning whose second a
		/// late tick stepped over.
		/// </remarks>
		public static bool IsThresholdCrossed(int previousSecond, int currentSecond, int threshold)
		{
			return threshold >= currentSecond && threshold < previousSecond;
		}

		/// <summary>
		/// Adds held time to a control point and converts it to whole points.
		/// </summary>
		/// <param name="heldSeconds">Held time not yet scored.</param>
		/// <param name="elapsedSeconds">Real time held since the last accrual. Negative counts as none.</param>
		/// <param name="secondsPerPoint">Seconds held per point; below 1 counts as 1.</param>
		/// <param name="points">Whole points earned.</param>
		/// <returns>The held time left over, carried to the next accrual.</returns>
		/// <remarks>
		/// Scored on real time, not on ticks. The hold used to gain one second per tick, so a tick
		/// that came late, or a hitch that merged several into one, took that time off the owning
		/// team; and a point taken just before a tick was credited a whole second for a moment's
		/// hold.
		/// </remarks>
		public static double AccrueHold(double heldSeconds, double elapsedSeconds, int secondsPerPoint, out int points)
		{
			int perPoint = Math.Max(1, secondsPerPoint);
			double held = Math.Max(0.0, heldSeconds) + (elapsedSeconds > 0.0 ? elapsedSeconds : 0.0);
			double whole = Math.Floor(held / perPoint);
			points = whole >= int.MaxValue ? int.MaxValue : (int)whole;
			return Math.Max(0.0, held - (double)points * perPoint);
		}

		/// <summary>
		/// Whether a seat's player hears their team's channel: team chat, and anything else sent to
		/// the team rather than to the arena.
		/// </summary>
		/// <param name="present">Standing in the arena's scene now.</param>
		/// <param name="dropped">Dropped for never arriving; no longer part of the match.</param>
		/// <remarks>
		/// The same membership <see cref="ArenaTeamRegistry"/> publishes — every seat not dropped —
		/// narrowed to the players who are actually there to receive it. A player inside their
		/// reconnect grace is on the roster but not in the scene, so not on the channel until they
		/// return.
		/// </remarks>
		public static bool IsOnTeamChannel(bool present, bool dropped)
		{
			return present && !dropped;
		}
	}
}
