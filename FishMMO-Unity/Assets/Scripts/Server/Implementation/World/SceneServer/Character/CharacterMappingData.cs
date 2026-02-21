using System.Collections.Generic;
using FishNet.Connection;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Runtime data container for character-to-connection mappings and character lookups.
	/// Manages all character session state separately from CharacterSystem logic.
	/// </summary>
	public class CharacterMappingData : RuntimeDataContainer, ICharacterMappingData<NetworkConnection>
	{
		/// <summary>
		/// Maps character IDs to player character instances.
		/// </summary>
		public Dictionary<long, IPlayerCharacter> CharactersByID { get; private set; }

		/// <summary>
		/// Maps lowercase character names to player character instances for case-insensitive lookups.
		/// </summary>
		public Dictionary<string, IPlayerCharacter> CharactersByLowerCaseName { get; private set; }

		/// <summary>
		/// Maps world server IDs to dictionaries of character IDs and player character instances.
		/// </summary>
		public Dictionary<long, Dictionary<long, IPlayerCharacter>> CharactersByWorld { get; private set; }

		/// <summary>
		/// Maps network connections to player character instances.
		/// </summary>
		public Dictionary<NetworkConnection, IPlayerCharacter> ConnectionCharacters { get; private set; }

		/// <summary>
		/// Maps network connections to player characters waiting for scene load completion.
		/// </summary>
		public Dictionary<NetworkConnection, IPlayerCharacter> WaitingSceneLoadCharacters { get; private set; }

		/// <summary>
		/// Maps character IDs to their session ownership info (token + server ID).
		/// </summary>
		public Dictionary<long, CharacterSessionInfo> SessionTokens { get; private set; }

		/// <summary>
		/// Initializes the character mapping data container.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			CharactersByID = new Dictionary<long, IPlayerCharacter>();
			CharactersByLowerCaseName = new Dictionary<string, IPlayerCharacter>();
			CharactersByWorld = new Dictionary<long, Dictionary<long, IPlayerCharacter>>();
			ConnectionCharacters = new Dictionary<NetworkConnection, IPlayerCharacter>();
			WaitingSceneLoadCharacters = new Dictionary<NetworkConnection, IPlayerCharacter>();
			SessionTokens = new Dictionary<long, CharacterSessionInfo>();
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Clears all character mappings.
		/// </summary>
		public override void Clear()
		{
			CharactersByID?.Clear();
			CharactersByLowerCaseName?.Clear();
			CharactersByWorld?.Clear();
			ConnectionCharacters?.Clear();
			WaitingSceneLoadCharacters?.Clear();
			SessionTokens?.Clear();
		}

		/// <summary>
		/// Deinitializes the character mapping data container.
		/// </summary>
		public override void Deinitialize()
		{
			Clear();
		}
	}
}