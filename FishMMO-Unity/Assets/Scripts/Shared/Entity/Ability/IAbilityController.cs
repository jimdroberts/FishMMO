using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	public interface IAbilityController : ICharacterBehaviour
	{
		event Action<string, float, float> OnUpdate;
		event Action OnInterrupt;
		event Action OnCancel;
		event Func<bool> OnCanManipulate;

		// UI
		event Action OnReset;
		event Action<long, Ability> OnAddAbility;
		event Action<long, BaseAbilityTemplate> OnAddKnownAbility;

		Dictionary<long, Ability> KnownAbilities { get; }
		HashSet<int> KnownBaseAbilities { get; }
		HashSet<int> KnownEvents { get; }
		HashSet<int> KnownSpawnEvents { get; }
		HashSet<int> KnownHitEvents { get; }
		HashSet<int> KnownMoveEvents { get; }
		bool IsActivating { get; }
		bool AbilityQueued { get; }

		void Interrupt(Character attacker);
		void Activate(long referenceID, KeyCode heldKey);
		void RemoveAbility(int referenceID);
		bool CanManipulate();
		bool KnowsAbility(int abilityID);
		bool LearnBaseAbilities(List<BaseAbilityTemplate> abilityTemplates = null);
		void LearnAbility(Ability ability);
	}
}