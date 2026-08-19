using System;
using FishMMO.Shared.Core;

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
		/// Number of characters whose bodies remain in the world after a combat logout.
		/// </summary>
		/// <remarks>
		/// These have no connection, so they are absent from the connection maps population
		/// counts are normally derived from — but they are still resident and still hold a
		/// session claim, so anything reporting load or deciding whether a scene is empty has to
		/// account for them.
		/// </remarks>
		int LingeringCharacterCount { get; }

		/// <summary>
		/// Declares that the next disconnect on <paramref name="connection"/> is a deliberate
		/// hand-off to another scene server, not a player leaving.
		/// </summary>
		/// <remarks>
		/// Combat-logout linger exists to stop a player escaping a fight by closing the client,
		/// so it applies to dropped connections — and a transfer is implemented as a dropped
		/// connection. A transfer that lingered would leave the body (and its session claim) on
		/// the source server while the client arrives at the destination, which then cannot
		/// claim the character and kicks it, repeatedly, until the linger expires.
		/// <para>
		/// The teleport and bind-point-respawn paths avoid this by releasing the character
		/// themselves before disconnecting. Callers that instead rely on the ordinary disconnect
		/// pipeline — a channel switch does — must announce the intent here first. The marker is
		/// consumed by that disconnect, so it cannot leak onto a later session on a recycled
		/// connection id.
		/// </para>
		/// </remarks>
		/// <param name="connection">Connection about to be disconnected for a transfer.</param>
		void BeginDeliberateTransfer(TConnection connection);

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
	}
}