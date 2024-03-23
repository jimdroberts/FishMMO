using System;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	[Serializable]
	public class FactionMatrix
	{
		public FactionAllianceLevel[] Factions;

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