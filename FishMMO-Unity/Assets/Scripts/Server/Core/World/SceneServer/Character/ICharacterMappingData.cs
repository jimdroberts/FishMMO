using System.Collections.Generic;
using UnityEngine.SceneManagement;
using FishMMO.Shared;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Runtime data container for character-to-connection mappings and character lookups.
	/// Provides read-only access to all character mapping collections.
	/// </summary>
	public interface ICharacterMappingData<TConnection> : IRuntimeDataContainer
	{
		/// <summary>
		/// Maps character IDs to player character instances.
		/// </summary>
		Dictionary<long, FishMMO.Shared.IPlayerCharacter> CharactersByID { get; }

		/// <summary>
		/// Maps lowercase character names to player character instances for case-insensitive lookups.
		/// </summary>
		Dictionary<string, FishMMO.Shared.IPlayerCharacter> CharactersByLowerCaseName { get; }

		/// <summary>
		/// Maps world server IDs to dictionaries of character IDs and player character instances.
		/// </summary>
		Dictionary<long, Dictionary<long, FishMMO.Shared.IPlayerCharacter>> CharactersByWorld { get; }

		/// <summary>
		/// Maps network connections to player character instances.
		/// </summary>
		Dictionary<TConnection, FishMMO.Shared.IPlayerCharacter> ConnectionCharacters { get; }

		/// <summary>
		/// Maps network connections to player characters waiting for scene load completion.
		/// </summary>
		Dictionary<TConnection, FishMMO.Shared.IPlayerCharacter> WaitingSceneLoadCharacters { get; }
	}
}