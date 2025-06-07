using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	public interface IFactionController : ICharacterBehaviour
	{
		static Action<ICharacter, Faction> OnUpdateFaction;

		bool IsAggressive { get; set; }
		Dictionary<int, Faction> Factions { get; }
		Dictionary<int, Faction> Allied { get;}
		Dictionary<int, Faction> Neutral { get; }
		Dictionary<int, Faction> Hostile { get; }
		RaceTemplate RaceTemplate { get; }

		void CopyFrom(IFactionController factionController);
		void SetFaction(int templateID, int value, bool skipEvent = false);
		void AdjustFaction(IFactionController defenderFactionController, float alliedPercentToSubtract, float hostilePercentToAdd);
		void Add(FactionTemplate template, int amount = 1);
		FactionAllianceLevel GetAllianceLevel(IFactionController otherFactionController);
		Color GetAllianceLevelColor(IFactionController otherFactionController);
	}
}