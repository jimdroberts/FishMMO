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
		/// Declares that the next disconnect on <paramref name="connection"/> is something the
		/// server is doing on purpose, not a player walking out of a fight.
		/// </summary>
		/// <remarks>
		/// Combat-logout linger exists to stop a player escaping a losing fight by closing the
		/// client, so it triggers on a dropped connection — and the server itself drops
		/// connections for reasons that have nothing to do with the player quitting.
		/// <list type="bullet">
		/// <item><description>
		/// A hand-off to another scene server is implemented as a dropped connection. One that
		/// lingered would leave the body — and its session claim — on the source server while
		/// the client arrives at the destination, which then cannot claim the character and
		/// kicks it, repeatedly, until the linger expires.
		/// </description></item>
		/// <item><description>
		/// An administrative kick is the operator removing the player, so there is no escape to
		/// deny. Lingering there keeps the kicked player's body in the world and holds their
		/// character claim for the length of the linger — which makes a kick the opposite of a
		/// remedy for a character that is stuck.
		/// </description></item>
		/// </list>
		/// The teleport and bind-point-respawn paths avoid this by releasing the character
		/// themselves before disconnecting. Callers that instead rely on the ordinary disconnect
		/// pipeline must announce the intent here first. The marker is consumed by that
		/// disconnect, so it cannot leak onto a later session on a recycled connection id.
		/// </remarks>
		/// <param name="connection">Connection the server is about to disconnect on purpose.</param>
		void SuppressCombatLingerOnDisconnect(TConnection connection);

		/// <summary>
		/// Ends the combat-logout linger held for <paramref name="accountName"/>, if there is one,
		/// saving the body and handing its session claim back.
		/// </summary>
		/// <remarks>
		/// A lingering body has no connection, so it is invisible to everything that acts on a
		/// player by finding their connection — including the administrative kick, which therefore
		/// did nothing at all to a character that had combat-logged. That is the opposite of what a
		/// kick is for: the body stays in the world, still targetable, and goes on holding the
		/// character's session claim, so the operator's remedy for a stuck character is precisely
		/// the case where it has no effect.
		/// <para>
		/// This is the ordinary end of a linger, not a special teardown: the body is persisted and
		/// despawned and the claim released, exactly as when its timer runs out. The character
		/// keeps whatever happened to it while it stood there.
		/// </para>
		/// </remarks>
		/// <param name="accountName">Account whose lingering body should be removed.</param>
		/// <param name="reason">Why the linger is being ended, for diagnostics.</param>
		/// <returns><c>true</c> when a lingering body was found and removed.</returns>
		bool TryEndCombatLingerForAccount(string accountName, string reason);

		/// <summary>
		/// Returns everyone standing in an instance to the open world, so the instance can be
		/// unloaded without stranding them.
		/// </summary>
		/// <remarks>
		/// An instance is normally emptied by its occupants leaving; this is the path for an
		/// instance that ends on the server's terms — a lifetime cap expiring, or a leader closing
		/// it — where the characters inside have not asked to go anywhere.
        /// <para>
		/// Each character takes the ordinary leave-instance route: announced while it still belongs
		/// to the instance so the population is debited correctly, put back at the open-world
		/// position it entered from, saved, released, and disconnected to be re-routed. It is not
		/// gated on combat or death, because the scene is going away regardless and refusing would
		/// leave the character in a scene that is about to be destroyed.
		/// </para>
		/// <para>
		/// Combat-logout bodies in the instance are finalised as well. They have no connection, so
		/// nothing else would notice them — and destroying the scene under one strands that
		/// character's session claim until its lease expires, locking the player out of every scene
		/// server in the meantime.
		/// </para>
		/// </remarks>
		/// <param name="instanceSceneID">Scene row ID of the instance being closed.</param>
		/// <param name="reason">Why it is closing, for diagnostics.</param>
		/// <returns>How many connected characters were moved.</returns>
		int ReturnInstanceOccupantsToWorld(long instanceSceneID, string reason);

		/// <summary>
		/// Moves a character to another channel (another instance of the same scene) by
		/// releasing it here, bound to <paramref name="targetSceneHandle"/>, and dropping the
		/// connection so the client re-routes through the world server.
		/// </summary>
		/// <remarks>
		/// The ordering this performs is the whole point of the method, and it is why callers
		/// must not simply rewrite <c>SceneHandle</c> and disconnect. Scene population is
		/// credited and debited by handle, and the disconnect pipeline debits the handle the
		/// character carries at that moment — so a handle rewritten first debits the
		/// <em>destination</em> instance, which this server may not even host, while the source
		/// instance keeps a resident it no longer has. Neither error self-corrects: the source's
		/// phantom population makes it advertise capacity it does not have, keeps it from ever
		/// being recognised as empty and unloaded, and eventually makes it look full, at which
		/// point players queue for a scene instance that is in fact deserted.
		/// <para>
		/// The character is released promptly (no combat-logout linger) because the destination
		/// scene server has to be able to claim it, exactly as for a teleport.
		/// </para>
		/// </remarks>
		/// <param name="connection">Connection whose character is switching channel.</param>
		/// <param name="targetSceneHandle">Scene row ID of the destination channel.</param>
		/// <returns><c>true</c> when the transfer was started; <c>false</c> if the connection has no character.</returns>
		bool BeginChannelTransfer(TConnection connection, long targetSceneHandle);

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