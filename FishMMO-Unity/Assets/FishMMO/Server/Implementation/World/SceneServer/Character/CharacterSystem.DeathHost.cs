using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishNet.Connection;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// The character system's part of a death (2026-11-20): what the Combat module's combat system, which owns the death,
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

			if ((player.IsInInstance() && player.InstanceSceneName != player.BindScene) ||
				(!player.IsInInstance() && player.SceneName != player.BindScene))
			{
				// Respawning at a bind point in another scene is a scene-server transfer, so it
				// has to follow the same order as the teleporter path.
				//
				// Previously this only mutated the character and disconnected, leaving the
				// save and session release to the connection-stopped handler. By then SceneName
				// had already been overwritten with BindScene, so every OnDisconnect subscriber
				// — party, guild, and the scene server's own per-scene bookkeeping — was told
				// the player left the scene they were respawning *into* rather than the one
				// they died in.
				bool leavingInstance = player.IsInInstance();

				DispatchCharacterEvent(OnDisconnect, conn, player, nameof(OnDisconnect));

				player.SceneName = player.BindScene;
				player.Motor.SetPositionAndRotationAndVelocity(player.BindPosition, player.Motor.Transform.rotation, Vector3.zero);
				player.DisableFlags(CharacterFlags.IsInInstance);
				player.DisableFlags(CharacterFlags.IsLoaded);

				if (leavingInstance)
				{
					/* Clear the instance bookkeeping, exactly as the teleport and
					 * leave-instance paths do. Dropping only the flag left InstanceID on the
					 * character, and BuildCharacterData persists it unconditionally — so a
					 * player who died in a dungeon and respawned at their bind point kept a
					 * row pointing at that dungeon's scene instance. Nothing reads it while
					 * the flag is clear, which is exactly what makes it dangerous: it is a
					 * live instance reference sitting on a character that is demonstrably not
					 * in one, waiting for anything that sets the flag without also setting
					 * the id.
					 *
					 * The transform is the bind point now, so the open-world position the save
					 * writes must be that too. */
					player.InstanceID = 0;
					player.InstanceSceneName = null;
					player.InstanceSceneHandle = 0;
					player.LastWorldPosition = player.BindPosition;
					player.LastWorldRotation = player.Motor.Transform.rotation;
				}

				// Save and release the session before dropping the connection so the
				// destination scene server can claim the character.
				RemoveCharacterConnectionMapping(conn, skipOnDisconnect: true);

				conn.Disconnect(false);
			}
			else
			{
				// The bind point, unless this is a cloud world and the bind point is open air in it.
				ResolveSameSceneRespawn(player, out Vector3 respawnPosition, out Quaternion respawnRotation);
				player.Motor.SetPositionAndRotationAndVelocity(respawnPosition, respawnRotation, Vector3.zero);
			}
		}
	}
}
