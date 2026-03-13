using System;
using System.Collections.Generic;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Interface for ability knowledge management — tracking known abilities, base abilities, and ability events.
	/// Separated from IAbilityController to satisfy ISP: consumers that only need to query or modify
	/// known abilities can depend on this smaller contract instead of the full activation interface.
	/// </summary>
	public interface IAbilityKnowledgeController : ICharacterBehaviour
	{
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
		SortedDictionary<long, Ability> KnownAbilities { get; }
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
		/// Returns true if the controller knows the specified base ability.
		/// </summary>
		/// <param name="abilityID">The ability ID.</param>
		bool KnowsAbility(int abilityID);
		/// <summary>
		/// Learns the specified base abilities.
		/// </summary>
		/// <param name="abilityTemplates">The list of base ability templates.</param>
		bool LearnBaseAbilities(List<BaseAbilityTemplate> abilityTemplates = null);
		/// <summary>
		/// Learns a single base ability template without allocating a list.
		/// </summary>
		/// <param name="template">The base ability template to learn.</param>
		bool LearnBaseAbility(BaseAbilityTemplate template);
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
		/// Learns a single ability event without allocating a list.
		/// </summary>
		/// <param name="abilityEvent">The ability event to learn.</param>
		bool LearnAbilityEvent(AbilityEvent abilityEvent);
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
		/// <summary>
		/// Removes an ability by reference ID.
		/// </summary>
		/// <param name="referenceID">The ability reference ID.</param>
		void RemoveAbility(long referenceID);
	}
}