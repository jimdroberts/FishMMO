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
		event Action<AbilityEvent> OnAddKnownAbilityEvent;

		Dictionary<long, Ability> KnownAbilities { get; }
		HashSet<int> KnownBaseAbilities { get; }
		HashSet<int> KnownAbilityEvents { get; }
		HashSet<int> KnownAbilityOnTickEvents { get; }
		HashSet<int> KnownAbilityOnHitEvents { get; }
		HashSet<int> KnownAbilityOnPreSpawnEvents { get; }
		HashSet<int> KnownAbilityOnSpawnEvents { get; }
		HashSet<int> KnownAbilityOnDestroyEvents { get; }
		bool IsActivating { get; }
		bool AbilityQueued { get; }

		void Interrupt(ICharacter attacker);
		void Activate(long referenceID, KeyCode heldKey);
		void RemoveAbility(int referenceID);
		bool CanManipulate();
		AbilityType GetCurrentAbilityType();
		bool IsCurrentAbilityTypeAerial();
		CharacterAttributeTemplate GetActivationAttributeTemplate(Ability ability);
		float CalculateSpeedReduction(CharacterAttributeTemplate attribute);
		bool KnowsAbility(int abilityID);
		bool LearnBaseAbilities(List<BaseAbilityTemplate> abilityTemplates = null);
		bool KnowsAbilityEvent(int eventID);
		bool LearnAbilityEvents(List<AbilityEvent> abilityEvents = null);
		bool KnowsLearnedAbility(int templateID);
		void LearnAbility(Ability ability, float remainingCooldown = 0.0f);
	}
}