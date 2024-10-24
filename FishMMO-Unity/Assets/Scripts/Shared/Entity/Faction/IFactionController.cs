using System;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	public interface IFactionController : ICharacterBehaviour
	{
		static Action<Faction, int> OnAddFaction;

		bool IsAggressive { get; set; }
		Dictionary<int, Faction> Factions { get; }
		Dictionary<int, Faction> Allied { get;}
		Dictionary<int, Faction> Neutral { get; }
		Dictionary<int, Faction> Hostile { get; }
		FactionTemplate Template{ get; }

		void SetFaction(int templateID, int value);
		void Add(IFactionController defenderFactionController);
		void Add(FactionTemplate template, int amount = 1);
		FactionAllianceLevel GetAllianceLevel(IFactionController otherFactionController);
	}
}