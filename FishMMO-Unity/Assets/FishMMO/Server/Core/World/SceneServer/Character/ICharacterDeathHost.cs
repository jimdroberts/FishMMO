using FishMMO.Shared.Core;
using FishNet.Connection;

namespace FishMMO.Server.Core.World.SceneServer
{
	/// <summary>
	/// What a death touches that is the world's, lent by the character system to the Combat module's combat system
	///: an instance's lives and its resurrection ban, the combat-logout linger, the pets' owners' notice, the
	/// move to a bind point (possibly in another scene, which is a scene-server transfer), and the system line.
	/// </summary>
	/// <remarks>
	/// The module owns the death itself: what a kill does to the victim, the death dialog's messages, respawning and
	/// resurrecting. Everything here is main-thread only.
	/// </remarks>
	public interface ICharacterDeathHost : IServerBehaviour
	{
		/// <summary>Spends one of the player's lives in an instance whose difficulty limits them, removing them on the last.</summary>
		void ApplyInstanceDeathRules(IPlayerCharacter player);

		/// <summary>Whether the player may be resurrected where it stands (a difficulty may forbid it in its dungeon).</summary>
		bool IsResurrectionAllowed(IPlayerCharacter player);

		/// <summary>
		/// Ends the combat-logout linger of the character, if it has one, with <paramref name="reason"/> in the log.
		/// False when the character is not lingering.
		/// </summary>
		bool TryFinalizeCombatLinger(long characterID, string reason);

		/// <summary>Raises the character system's pet-killed event for the pet's owner (the pet system listens).</summary>
		void NotifyPetKilled(NetworkConnection ownerConnection, IPlayerCharacter owner, ICharacter pet);

		/// <summary>
		/// Moves a revived player to its bind point: in this scene, where the position holds (a cloud world's open-air bind
		/// point resolves to the nearest place that does); in another scene, by the ordered transfer every scene change uses
		/// (announce the departure, rebind, save, release, drop the connection).
		/// </summary>
		void RespawnAtBindPoint(NetworkConnection connection, IPlayerCharacter player);

		/// <summary>Sends one system-channel line to a connection, if it is still there.</summary>
		void SendSystemMessage(NetworkConnection connection, string text);
	}
}
