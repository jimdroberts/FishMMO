using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// A character's standing toward others, as the rest of the game asks it: alliance level and its colour, the
	/// aggression toggle and its lock, setting a standing by template id. Core: implemented by the Factions module's
	/// <c>FactionController</c> (a character part); without the module a character has none and every caller treats that
	/// as "no relation" (the harm rule allows, AI sees no enemy but the aggressive).
	/// </summary>
	/// <remarks>The standings themselves (the <c>Faction</c> rows and templates) are the module's: <c>IFactionStandings</c>.</remarks>
	public interface IFactionController : ICharacterBehaviour
	{
		/// <summary>
		/// Gets or sets whether the character is aggressive (treats others as enemies).
		/// </summary>
		bool IsAggressive { get; }

		/// <summary>
		/// A player's aggression toggle, as the server last set it: while on, they may harm neutral and allied targets, and
		/// anyone may harm them. Off by default; always off for a character that has no toggle.
		/// </summary>
		bool AggressionEnabled => false;

		/// <summary>Seconds before the aggression toggle may be turned off again (a hit made or taken restarts it).</summary>
		float AggressionLockRemaining => 0f;

		/// <summary>
		/// Server: sets the aggression toggle when the rules allow it. False, with the seconds left, when turning it off is
		/// still locked.
		/// </summary>
		bool TrySetAggression(bool enabled, out float lockedSeconds)
		{
			lockedSeconds = 0f;
			return false;
		}

		/// <summary>
		/// The race template associated with this character, used for initial faction setup.
		/// </summary>
		RaceTemplate RaceTemplate { get; }

		/// <summary>
		/// Triggers invoked when a faction standing changes for this character.
		/// </summary>
		List<Trigger> OnFactionChangeTriggers { get; }

		/// <summary>
		/// Copies all faction data from another faction controller.
		/// </summary>
		/// <param name="factionController">The source faction controller to copy from.</param>
		void CopyFrom(IFactionController factionController);

		/// <summary>
		/// Sets the value for a specific faction by template ID.
		/// Optionally skips event notification.
		/// </summary>
		/// <param name="templateID">Template ID of the faction.</param>
		/// <param name="value">New value to set.</param>
		/// <param name="skipEvent">If true, skips event notification.</param>
		void SetFaction(int templateID, int value, bool skipEvent = false);

		/// <summary>
		/// Adjusts faction standings based on another controller's allied/hostile groups and specified percentages.
		/// </summary>
		/// <param name="defenderFactionController">The other faction controller to compare against.</param>
		/// <param name="alliedPercentToSubtract">Percentage of allied standing to subtract.</param>
		/// <param name="hostilePercentToAdd">Percentage of hostile standing to add.</param>
		void AdjustFaction(IFactionController defenderFactionController, float alliedPercentToSubtract, float hostilePercentToAdd);

		/// <summary>
		/// Gets the alliance level (Ally, Neutral, Enemy) between this character and another faction controller.
		/// </summary>
		/// <param name="otherFactionController">The other faction controller to compare against.</param>
		/// <returns>Alliance level (Ally, Neutral, Enemy).</returns>
		FactionAllianceLevel GetAllianceLevel(IFactionController otherFactionController);

		/// <summary>
		/// Gets the color representing the alliance level between this character and another faction controller.
		/// </summary>
		/// <param name="otherFactionController">The other faction controller to compare against.</param>
		/// <returns>Color representing the alliance level.</returns>
		Color GetAllianceLevelColor(IFactionController otherFactionController);

		/// <summary>
		/// The standing this character's client shows for another: <see cref="GetAllianceLevel"/>, except that a player
		/// whose aggression is on reads as an enemy to everyone but their companions (party, guild), as
		/// HarmRule treats them. What the nameplate's colour and mark both follow.
		/// </summary>
		FactionAllianceLevel GetDisplayedAllianceLevel(IFactionController otherFactionController) => GetAllianceLevel(otherFactionController);

		/// <summary>
		/// The standing between the two as it is, without the aggression toggle: <see cref="GetAllianceLevel"/> reads any pair
		/// where either side's aggression is on as Enemy (so hostile effects reach neutrals), and that widening is for HOSTILE
		/// reach only. Beneficial targeting (a heal or cleanse that reaches neutral players) reads this, so turning aggression
		/// on does not stop a player healing the neutral player beside them.
		/// </summary>
		FactionAllianceLevel GetStandingAllianceLevel(IFactionController otherFactionController) => GetAllianceLevel(otherFactionController);
	}
}