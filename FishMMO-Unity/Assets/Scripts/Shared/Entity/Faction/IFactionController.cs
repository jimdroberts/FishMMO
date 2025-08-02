using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Interface for faction controllers, providing access and management for all faction relationships and reputation values.
	/// Used to read, modify, and synchronize faction standings for a character.
	/// </summary>
	public interface IFactionController : ICharacterBehaviour
	{
		/// <summary>
		/// Event invoked when a faction is updated (standing changes).
		/// Parameters: character, updated faction.
		/// </summary>
		static Action<ICharacter, Faction> OnUpdateFaction;

		/// <summary>
		/// Gets or sets whether the character is aggressive (treats others as enemies).
		/// </summary>
		bool IsAggressive { get; set; }

		/// <summary>
		/// Dictionary of all factions and their standing values, keyed by template ID.
		/// </summary>
		Dictionary<int, Faction> Factions { get; }

		/// <summary>
		/// Dictionary of allied factions (positive standing), keyed by template ID.
		/// </summary>
		Dictionary<int, Faction> Allied { get; }

		/// <summary>
		/// Dictionary of neutral factions (zero standing), keyed by template ID.
		/// </summary>
		Dictionary<int, Faction> Neutral { get; }

		/// <summary>
		/// Dictionary of hostile factions (negative standing), keyed by template ID.
		/// </summary>
		Dictionary<int, Faction> Hostile { get; }

		/// <summary>
		/// The race template associated with this character, used for initial faction setup.
		/// </summary>
		RaceTemplate RaceTemplate { get; }

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
		/// Adds an amount to the value of a specific faction.
		/// </summary>
		/// <param name="template">Faction template to adjust.</param>
		/// <param name="amount">Amount to add (default 1).</param>
		void Add(FactionTemplate template, int amount = 1);

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
	}
}