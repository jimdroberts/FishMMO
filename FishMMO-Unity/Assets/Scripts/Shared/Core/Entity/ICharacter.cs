using System.Collections.Generic;
using UnityEngine;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Managing.Predicting;
#if !UNITY_SERVER
using TMPro;
#endif

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// Interface for a character entity in the game world.
	/// Defines core properties, state management, and behaviour registration for all character types.
	/// </summary>
	public interface ICharacter
	{
		/// <summary>
		/// Unique identifier for the character.
		/// </summary>
		long ID { get; set; }
		/// <summary>
		/// The character's display name.
		/// </summary>
		string Name { get; }
		/// <summary>
		/// The transform of the character.
		/// </summary>
		Transform Transform { get; }
		/// <summary>
		/// The GameObject associated with the character.
		/// </summary>
		GameObject GameObject { get; }
		/// <summary>
		/// The collider for the character.
		/// </summary>
		Collider Collider { get; set; }
		/// <summary>
		/// The network connection that owns this character.
		/// </summary>
		NetworkConnection Owner { get; }
		/// <summary>
		/// The network object representing this character in FishNet networking.
		/// </summary>
		NetworkObject NetworkObject { get; }
		/// <summary>
		/// The prediction manager for client-side prediction and reconciliation.
		/// </summary>
		PredictionManager PredictionManager { get; }
		/// <summary>
		/// The set of network connections observing this character.
		/// </summary>
		HashSet<NetworkConnection> Observers { get; }
		/// <summary>
		/// Whether the character is currently teleporting.
		/// </summary>
		bool IsTeleporting { get; }
		/// <summary>
		/// Whether the character is currently spawned in the world.
		/// </summary>
		bool IsSpawned { get; }
		/// <summary>
		/// Bitwise flags representing the character's state.
		/// </summary>
		int Flags { get; set; }
		/// <summary>
		/// Enables the specified flags for the character using bitwise operations.
		/// </summary>
		/// <param name="flags">Flags to enable.</param>
		void EnableFlags(CharacterFlags flags);
		/// <summary>
		/// Disables the specified flags for the character using bitwise operations.
		/// </summary>
		/// <param name="flags">Flags to disable.</param>
		void DisableFlags(CharacterFlags flags);
		/// <summary>
		/// Checks if the specified flags are enabled for the character.
		/// </summary>
		/// <param name="flags">Flags to check.</param>
		/// <returns>True if the flags are enabled; otherwise, false.</returns>
		bool IsFlagged(CharacterFlags flags);

#if !UNITY_SERVER
		/// <summary>
		/// The root transform for the character's mesh/model hierarchy.
		/// </summary>
		Transform MeshRoot { get; }
		/// <summary>
		/// The label displaying the character's name above their model.
		/// </summary>
		TextMeshPro CharacterNameLabel { get; set; }
		/// <summary>
		/// The label displaying the character's guild above their model.
		/// </summary>
		TextMeshPro CharacterGuildLabel { get; set; }
		/// <summary>
		/// Instantiates the character's race model prefab at the specified index and attaches it to the mesh root.
		/// </summary>
		/// <param name="raceTemplate">The race template containing model references.</param>
		/// <param name="modelIndex">The index of the model to instantiate.</param>
		void InstantiateRaceModelFromIndex(RaceTemplate raceTemplate, int modelIndex);
#endif

		/// <summary>
		/// Registers a character behaviour component for this character.
		/// Enables behaviour-based extension and modular logic.
		/// </summary>
		/// <param name="characterBehaviour">The behaviour to register.</param>
		void RegisterCharacterBehaviour(ICharacterBehaviour characterBehaviour);
		/// <summary>
		/// Unregisters a character behaviour component from this character.
		/// </summary>
		/// <param name="characterBehaviour">The behaviour to unregister.</param>
		void UnregisterCharacterBehaviour(ICharacterBehaviour characterBehaviour);
		/// <summary>
		/// Attempts to get a registered character behaviour of type T.
		/// Returns true if found, with the behaviour in the out parameter.
		/// </summary>
		/// <typeparam name="T">The interface type to retrieve.</typeparam>
		/// <param name="control">The behaviour instance if found, otherwise null.</param>
		/// <returns>True if the behaviour is found; otherwise, false.</returns>
		bool TryGet<T>(out T control) where T : class, ICharacterBehaviour;

		// ───── ECA Trigger Lists ─────────────────────────────────────────────

		/// <summary>Triggers invoked when this character deals damage to another. EventData: DamageEventData.</summary>
		List<Trigger> OnDamageTriggers { get; }
		/// <summary>Triggers invoked when this character receives damage from another. EventData: DamageEventData.</summary>
		List<Trigger> OnDamagedTriggers { get; }
		/// <summary>Triggers invoked when this character heals another. EventData: HealEventData.</summary>
		List<Trigger> OnHealTriggers { get; }
		/// <summary>Triggers invoked when this character is healed by another. EventData: HealEventData.</summary>
		List<Trigger> OnHealedTriggers { get; }
		/// <summary>Triggers invoked when this character kills another. EventData with CharacterHitEventData.</summary>
		List<Trigger> OnKillTriggers { get; }
		/// <summary>Triggers invoked when this character is killed by another. EventData with CharacterHitEventData.</summary>
		List<Trigger> OnKilledTriggers { get; }
		/// <summary>Triggers invoked when this character resurrects another. EventData with CharacterHitEventData.</summary>
		List<Trigger> OnResurrectTriggers { get; }
		/// <summary>Triggers invoked when this character is resurrected by another. EventData with CharacterHitEventData.</summary>
		List<Trigger> OnResurrectedTriggers { get; }

		/// <summary>
		/// Executes each trigger in the list with the supplied event data.
		/// Null-safe: no-ops if <paramref name="triggers"/> or <paramref name="eventData"/> is null.
		/// </summary>
		/// <param name="triggers">The list of triggers to execute.</param>
		/// <param name="eventData">The event data to pass to each trigger.</param>
		void Invoke(List<Trigger> triggers, EventData eventData);
	}
}