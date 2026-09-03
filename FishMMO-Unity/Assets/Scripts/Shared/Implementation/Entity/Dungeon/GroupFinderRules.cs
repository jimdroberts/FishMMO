using System;

namespace FishMMO.Shared
{
	/// <summary>
	/// The decisions the dungeon group finder makes, as pure functions.
	/// </summary>
	/// <remarks>
	/// Kept free of network, database and Unity state so each rule is a truth table that can be
	/// tested without a server. The scene server's matching pump calls these; the client calls
	/// <see cref="ResolveGroupSize"/> only to decide whether to offer the button, and the server
	/// re-decides everything on its own copy of the same asset.
	/// </remarks>
	public static class GroupFinderRules
	{
		/// <summary>
		/// The smallest group the finder will ever form. A group of one is not a group, and the
		/// solo case is served by the entrance's own "Open New Dungeon".
		/// </summary>
		public const int MinimumGroupSize = 2;

		/// <summary>
		/// How many players the finder gathers for one difficulty before opening a run.
		/// </summary>
		/// <param name="difficulty">The difficulty's ruleset. Null means a dungeon with no template, which the finder does not serve.</param>
		/// <param name="capacity">Resolved instance capacity at this difficulty.</param>
		/// <returns>The group size, or <c>0</c> when Find Group is not available at this difficulty.</returns>
		/// <remarks>
		/// <list type="bullet">
		/// <item>Off when the author turned it off, or when the capacity cannot seat two.</item>
		/// <item>The author's <see cref="DungeonDifficultyDefinition.GroupFinderSize"/> when set, otherwise the capacity: a finder fills groups.</item>
		/// <item>Never below <see cref="MinimumGroupSize"/> or the difficulty's <see cref="DungeonDifficultyDefinition.MinimumPartySize"/>, because the run would refuse a smaller party at the door.</item>
		/// <item>Never above the capacity, because the run could not hold them.</item>
		/// <item>Off when those two bounds cross — a difficulty demanding more players than it can hold is unopenable by anyone, and the finder should not queue people for it.</item>
		/// </list>
		/// </remarks>
		public static int ResolveGroupSize(DungeonDifficultyDefinition difficulty, int capacity)
		{
			if (difficulty == null || !difficulty.GroupFinderEnabled)
			{
				return 0;
			}

			int floor = Math.Max(MinimumGroupSize, difficulty.MinimumPartySize);
			if (capacity < floor)
			{
				return 0;
			}

			int target = difficulty.GroupFinderSize > 0 ? difficulty.GroupFinderSize : capacity;
			if (target < floor)
			{
				target = floor;
			}
			if (target > capacity)
			{
				target = capacity;
			}
			return target;
		}

		/// <summary>
		/// Why a character may not join the queue, decided from what the scene server already
		/// knows about them before it touches the database.
		/// </summary>
		/// <param name="groupSize">Resolved group size for the difficulty; 0 means the finder is off there.</param>
		/// <param name="isInInstance">Whether the character is already inside instanced content.</param>
		/// <param name="inPartyWithOthers">Whether the character shares a party with anybody else.</param>
		/// <returns><see cref="GroupFinderRefusalReason.None"/> when nothing here refuses them.</returns>
		/// <remarks>
		/// Order matters and is deliberate. "Not offered here" is the answer whatever the player's
		/// state, and a player already inside a dungeon should be told that before being told
		/// about their party. Whether they already hold an instance needs a database read and is
		/// decided by the caller afterwards.
		/// </remarks>
		public static GroupFinderRefusalReason ResolveQueueRefusal(int groupSize, bool isInInstance, bool inPartyWithOthers)
		{
			if (groupSize < MinimumGroupSize)
			{
				return GroupFinderRefusalReason.NotAvailable;
			}
			if (isInInstance)
			{
				return GroupFinderRefusalReason.InInstance;
			}
			if (inPartyWithOthers)
			{
				return GroupFinderRefusalReason.InParty;
			}
			return GroupFinderRefusalReason.None;
		}

		/// <summary>
		/// What the pump does with a matched character on each visit.
		/// </summary>
		public enum MatchedTransferAction : byte
		{
			/// <summary>Move them now.</summary>
			Transfer = 0,
			/// <summary>They cannot travel this instant; look again next pump.</summary>
			Wait = 1,
			/// <summary>They have been untransferable too long; take them out of the group and the queue.</summary>
			GiveUp = 2,
		}

		/// <summary>
		/// Decides whether a matched character is moved, left for the next pump, or dropped.
		/// </summary>
		/// <param name="canTransfer">Whether the character may leave the scene right now (not in combat, not dead, not mid-teleport).</param>
		/// <param name="secondsSinceMatch">How long ago the group formed.</param>
		/// <param name="graceSeconds">How long a matched character may stay untransferable before the group goes on without them.</param>
		/// <remarks>
		/// A player matched mid-fight is not punished for the fight: they are moved the moment it
		/// ends. But the rest of their group is already inside, or about to be, and a slot held
		/// forever by someone who never becomes free would be a run that never fills — so the
		/// grace is bounded, and running out of it means the group left without them.
		/// </remarks>
		public static MatchedTransferAction ResolveMatchedTransfer(bool canTransfer, double secondsSinceMatch, double graceSeconds)
		{
			if (canTransfer)
			{
				return MatchedTransferAction.Transfer;
			}
			return secondsSinceMatch >= graceSeconds
				? MatchedTransferAction.GiveUp
				: MatchedTransferAction.Wait;
		}
	}
}
