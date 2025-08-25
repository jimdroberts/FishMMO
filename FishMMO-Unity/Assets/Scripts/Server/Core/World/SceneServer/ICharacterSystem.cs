using System;
using System.Collections.Generic;
using FishMMO.Shared;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// Engine-agnostic public API for character management on a scene server.
	/// Exposes events and read-only mappings other systems use to query and react
	/// to character lifecycle and state changes.
	/// </summary>
	/// <typeparam name="TConnection">An engine-agnostic connection handle type used by the implementation (for example a network connection or player session object).</typeparam>
	/// <typeparam name="TScene">The scene object type used by the engine (kept generic to avoid engine-specific types leaking into the public API).</typeparam>
	/// <remarks>
	/// Implementations should document any threading and lifetime guarantees for
	/// event invocation and the provided read-only collections. Event subscribers
	/// will typically cast <typeparamref name="TConnection"/> and
	/// <typeparamref name="TScene"/> to concrete engine types where required.
	/// </remarks>
	public interface ICharacterSystem<TConnection, TScene> : IServerBehaviour
	{
		/// <summary>
		/// Raised immediately before a character load is initiated for the given
		/// connection. The long parameter is the persistent character id requested
		/// by the client.
		/// </summary>
		event Action<TConnection, long> OnBeforeLoadCharacter;

		/// <summary>
		/// Raised after a character has been loaded from persistence and fully
		/// populated with server-side state. Handlers receive the connection and
		/// the loaded <see cref="IPlayerCharacter"/> instance.
		/// </summary>
		event Action<TConnection, IPlayerCharacter> OnAfterLoadCharacter;

		/// <summary>
		/// Raised when a character is associated with a connection (player has
		/// fully connected or reconnected). Subscribers receive the connection and
		/// the associated <see cref="IPlayerCharacter"/>.
		/// </summary>
		event Action<TConnection, IPlayerCharacter> OnConnect;

		/// <summary>
		/// Raised when a character is removed from a connection or the
		/// connection is disconnected. Handlers should use this to persist or
		/// clean up per-connection state.
		/// </summary>
		event Action<TConnection, IPlayerCharacter> OnDisconnect;

		/// <summary>
		/// Raised after a character is spawned into a scene. The third parameter
		/// is the engine-specific scene object representing the spawned entity
		/// (typed as <typeparamref name="TScene"/>).
		/// </summary>
		event Action<TConnection, IPlayerCharacter, TScene> OnSpawnCharacter;

		/// <summary>
		/// Raised when a character is despawned from the active scene. This
		/// occurs when a player leaves a scene, transfers, or their connection
		/// is removed.
		/// </summary>
		event Action<TConnection, IPlayerCharacter> OnDespawnCharacter;

		/// <summary>
		/// Raised when a pet owned by the character is killed. Subscribers may
		/// use this to update pet-related state, notify the client, or trigger
		/// persistence.
		/// </summary>
		event Action<TConnection, IPlayerCharacter> OnPetKilled;

		/// <summary>
		/// Read-only mapping of persistent character id -> loaded
		/// <see cref="IPlayerCharacter"/> instance for fast lookup.
		/// </summary>
		Dictionary<long, IPlayerCharacter> CharactersByID { get; }

		/// <summary>
		/// Read-only mapping of lowercase character name -> player character.
		/// Useful for resolving chat, whispers, or invite targets without a
		/// database round-trip. Keys are stored in lowercase for case-insensitive lookup.
		/// </summary>
		Dictionary<string, IPlayerCharacter> CharactersByLowerCaseName { get; }

		/// <summary>
		/// Mapping of world server id -> (character id -> player character).
		/// This view allows runtime systems to find characters grouped by the
		/// world server that owns them.
		/// </summary>
		public Dictionary<long, Dictionary<long, IPlayerCharacter>> CharactersByWorld { get; }

		/// <summary>
		/// Read-only mapping of connection -> currently associated
		/// <see cref="IPlayerCharacter"/>. The connection type is the
		/// generic <typeparamref name="TConnection"/>, allowing engine-specific
		/// connection objects to be used by implementations.
		/// </summary>
		Dictionary<TConnection, IPlayerCharacter> ConnectionCharacters { get; }

		/// <summary>
		/// Mapping of connections that are currently waiting for scene load to
		/// their player characters. Characters in this collection are loaded but
		/// not yet spawned into the active scene.
		/// </summary>
		Dictionary<TConnection, IPlayerCharacter> WaitingSceneLoadCharacters { get; }
	}
}