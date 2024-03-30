using System.Collections.Generic;

namespace FishMMO.Shared
{
	public interface IFactionController : ICharacterBehaviour
	{
		bool IsAggressive { get; set; }
		Dictionary<int, Faction> Factions { get; }

		void SetFaction(int templateID, int value);
		bool TryGetFaction(int templateID, out Faction faction);
		void Add(FactionTemplate template, int amount = 1);
		FactionAllianceLevel GetAllianceLevel(FactionTemplate enemyFaction);
		FactionAllianceLevel GetAllianceLevel(IFactionController otherFactionController);
	}
}