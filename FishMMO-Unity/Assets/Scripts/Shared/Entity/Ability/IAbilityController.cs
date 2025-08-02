using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Interface for ability controllers, managing ability activation, events, and known abilities.
	/// </summary>
	public interface IAbilityController : ICharacterBehaviour
	{
		/// <summary>
		/// Event triggered to check if manipulation is allowed.
		/// </summary>
		event Func<bool> OnCanManipulate;

		/// <summary>
		/// Event triggered to update ability UI or state.
		/// </summary>
		event Action<string, float, float> OnUpdate;
		/// <summary>
		/// Event triggered when the current ability is interrupted.
		/// </summary>
		event Action OnInterrupt;
		/// <summary>
		/// Event triggered when the current ability is cancelled.
		/// </summary>
		event Action OnCancel;
		/// <summary>
		/// Event triggered to reset the UI.
		/// </summary>
		event Action OnReset;
		/// <summary>
		/// Event triggered when a new ability is added.
		/// </summary>
		event Action<Ability> OnAddAbility;
		/// <summary>
		/// Event triggered when a new base ability is added.
		/// </summary>
		event Action<BaseAbilityTemplate> OnAddKnownAbility;
		/// <summary>
		/// Event triggered when a new ability event is added.
		/// </summary>
		event Action<AbilityEvent> OnAddKnownAbilityEvent;

		/// <summary>
		/// Dictionary of known abilities by ID.
		/// </summary>
		Dictionary<long, Ability> KnownAbilities { get; }
		/// <summary>
		/// Set of known base ability IDs.
		/// </summary>
		HashSet<int> KnownBaseAbilities { get; }
		/// <summary>
		/// Set of known ability event IDs.
		/// </summary>
		HashSet<int> KnownAbilityEvents { get; }
		/// <summary>
		/// Set of known OnTick event IDs.
		/// </summary>
		HashSet<int> KnownAbilityOnTickEvents { get; }
		/// <summary>
		/// Set of known OnHit event IDs.
		/// </summary>
		HashSet<int> KnownAbilityOnHitEvents { get; }
		/// <summary>
		/// Set of known OnPreSpawn event IDs.
		/// </summary>
		HashSet<int> KnownAbilityOnPreSpawnEvents { get; }
		/// <summary>
		/// Set of known OnSpawn event IDs.
		/// </summary>
		HashSet<int> KnownAbilityOnSpawnEvents { get; }
		/// <summary>
		/// Set of known OnDestroy event IDs.
		/// </summary>
		HashSet<int> KnownAbilityOnDestroyEvents { get; }
		/// <summary>
		/// True if an ability is currently activating.
		/// </summary>
		bool IsActivating { get; }
		/// <summary>
		/// True if an ability is queued for activation.
		/// </summary>
		bool AbilityQueued { get; }

		/// <summary>
		/// Interrupts the current ability, optionally specifying the attacker.
		/// </summary>
		/// <param name="attacker">The character causing the interruption.</param>
		void Interrupt(ICharacter attacker);
		/// <summary>
		/// Activates an ability by reference ID and held key.
		/// </summary>
		/// <param name="referenceID">The ability reference ID.</param>
		/// <param name="heldKey">The key held for activation.</param>
		void Activate(long referenceID, KeyCode heldKey);
		/// <summary>
		/// Removes an ability by reference ID.
		/// </summary>
		/// <param name="referenceID">The ability reference ID.</param>
		void RemoveAbility(int referenceID);
		/// <summary>
		/// Returns true if manipulation is allowed.
		/// </summary>
		bool CanManipulate();
		/// <summary>
		/// Gets the current ability type.
		/// </summary>
		AbilityType GetCurrentAbilityType();
		/// <summary>
		/// Returns true if the current ability type is aerial.
		/// </summary>
		bool IsCurrentAbilityTypeAerial();
		/// <summary>
		/// Gets the activation attribute template for the given ability.
		/// </summary>
		/// <param name="ability">The ability to check.</param>
		CharacterAttributeTemplate GetActivationAttributeTemplate(Ability ability);
		/// <summary>
		/// Calculates the speed reduction for the given attribute.
		/// </summary>
		/// <param name="attribute">The attribute to check.</param>
		float CalculateSpeedReduction(CharacterAttributeTemplate attribute);
		/// <summary>
		/// Returns true if the controller knows the specified ability.
		/// </summary>
		/// <param name="abilityID">The ability ID.</param>
		bool KnowsAbility(int abilityID);
		/// <summary>
		/// Learns the specified base abilities.
		/// </summary>
		/// <param name="abilityTemplates">The list of base ability templates.</param>
		bool LearnBaseAbilities(List<BaseAbilityTemplate> abilityTemplates = null);
		/// <summary>
		/// Returns true if the controller knows the specified ability event.
		/// </summary>
		/// <param name="eventID">The event ID.</param>
		bool KnowsAbilityEvent(int eventID);
		/// <summary>
		/// Learns the specified ability events.
		/// </summary>
		/// <param name="abilityEvents">The list of ability events.</param>
		bool LearnAbilityEvents(List<AbilityEvent> abilityEvents = null);
		/// <summary>
		/// Returns true if the controller knows the specified learned ability.
		/// </summary>
		/// <param name="templateID">The template ID.</param>
		bool KnowsLearnedAbility(int templateID);
		/// <summary>
		/// Learns the specified ability, with optional cooldown.
		/// </summary>
		/// <param name="ability">The ability to learn.</param>
		/// <param name="remainingCooldown">The remaining cooldown time.</param>
		void LearnAbility(Ability ability, float remainingCooldown = 0.0f);
	}
}