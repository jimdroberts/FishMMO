using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	public interface IAbilityController : ICharacterBehaviour
	{
		event Func<bool> OnCanManipulate;

		event Action<string, float, float> OnUpdate;
		// Invoked when the current ability is Interrupted.
		event Action OnInterrupt;
		// Invoked when the current ability is Cancelled.
		event Action OnCancel;
		// UI
		event Action OnReset;
		event Action<Ability> OnAddAbility;
		event Action<BaseAbilityTemplate> OnAddKnownAbility;

		Dictionary<long, Ability> KnownAbilities { get; }
		HashSet<int> KnownBaseAbilities { get; }
		HashSet<int> KnownEvents { get; }
		HashSet<int> KnownSpawnEvents { get; }
		HashSet<int> KnownHitEvents { get; }
		HashSet<int> KnownMoveEvents { get; }
		bool IsActivating { get; }
		bool AbilityQueued { get; }

		void Interrupt(ICharacter attacker);
		void Activate(long referenceID, KeyCode heldKey);
		void RemoveAbility(int referenceID);
		bool CanManipulate();
		bool KnowsAbility(int abilityID);
		bool LearnBaseAbilities(List<BaseAbilityTemplate> abilityTemplates = null);
		bool KnowsLearnedAbility(int templateID);
		void LearnAbility(Ability ability);
	}
}