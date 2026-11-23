using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishNet.Connection;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// The character system's part of a death: what the Combat module's combat system, which owns the death,
	/// asks of the world (<see cref="ICharacterDeathHost"/>).
	/// </summary>
	public partial class CharacterSystem
	{
		/// <inheritdoc />
		void ICharacterDeathHost.ApplyInstanceDeathRules(IPlayerCharacter player) => ApplyInstanceDeathRules(player);

		/// <inheritdoc />
		bool ICharacterDeathHost.IsResurrectionAllowed(IPlayerCharacter player) => player != null && IsResurrectionAllowed(player);

		/// <inheritdoc />
		bool ICharacterDeathHost.TryFinalizeCombatLinger(long characterID, string reason)
		{
			if (!lingeringCharacters.ContainsKey(characterID))
			{
				return false;
			}
			FinalizeCombatLinger(characterID, reason);
			return true;
		}

		/// <inheritdoc />
		void ICharacterDeathHost.NotifyPetKilled(NetworkConnection ownerConnection, IPlayerCharacter owner, ICharacter pet)
		{
			OnPetKilled?.Invoke(ownerConnection, owner, pet);
		}

		/// <inheritdoc />
		void ICharacterDeathHost.SendSystemMessage(NetworkConnection connection, string text) => SendSystemMessage(connection, text);

		/// <inheritdoc />
		void ICharacterDeathHost.RespawnAtBindPoint(NetworkConnection conn, IPlayerCharacter player)
		{
			if (conn == null || player == null || player.Motor == null)
			{
				return;
			}

			// The same move a Portal to Bind makes: local in the bind scene, a hand-off from any other.
			SendToBindPoint(conn, player);
		}
	}
}
