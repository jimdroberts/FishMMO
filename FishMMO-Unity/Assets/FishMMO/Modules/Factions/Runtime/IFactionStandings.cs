using System;
using System.Collections.Generic;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// A character's faction standings, as the Factions module keeps them: the rows by template, grouped by alliance, and
	/// the reputation changes. What the rest of the game needs (alliance level, aggression) is the core
	/// <see cref="IFactionController"/>.
	/// </summary>
	public interface IFactionStandings : IFactionController
	{
		/// <summary>Raised when a standing changes. Parameters: character, updated faction.</summary>
		static Action<ICharacter, Faction> OnUpdateFaction;

		/// <summary>All factions and their standing values, keyed by template ID.</summary>
		Dictionary<int, Faction> Factions { get; }

		/// <summary>Allied factions (positive standing), keyed by template ID.</summary>
		Dictionary<int, Faction> Allied { get; }

		/// <summary>Neutral factions (zero standing), keyed by template ID.</summary>
		Dictionary<int, Faction> Neutral { get; }

		/// <summary>Hostile factions (negative standing), keyed by template ID.</summary>
		Dictionary<int, Faction> Hostile { get; }

		/// <summary>Adds an amount to the value of a specific faction.</summary>
		void Add(FactionTemplate template, int amount = 1);
	}
}
