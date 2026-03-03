using System;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Represents a matrix of faction relationships, where each cell defines the alliance level between two factions.
	/// Used to determine how factions interact (e.g., ally, enemy, neutral).
	/// </summary>
	[Serializable]
	public class FactionMatrix
	{
		/// <summary>
		/// Flat array representing the alliance level between each pair of factions.
		/// Indexed as [x + y * count], where x and y are faction indices.
		/// </summary>
		public FactionAllianceLevel[] Factions;

		/// <summary>
		/// Constructs a new FactionMatrix for the given list of factions, initializing all relationships to Neutral.
		/// </summary>
		/// <param name="Factions">List of all faction templates to include in the matrix.</param>
		public FactionMatrix(List<FactionTemplate> Factions)
		{
			int size = Factions.Count * Factions.Count;

			this.Factions = new FactionAllianceLevel[size];
			for (int i = 0; i < this.Factions.Length; ++i)
			{
				this.Factions[i] = FactionAllianceLevel.Neutral;
			}
		}
	}
}