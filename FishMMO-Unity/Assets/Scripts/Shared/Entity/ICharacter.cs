using UnityEngine;
#if !UNITY_SERVER
using TMPro;
#endif

namespace FishMMO.Shared
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
	}
}