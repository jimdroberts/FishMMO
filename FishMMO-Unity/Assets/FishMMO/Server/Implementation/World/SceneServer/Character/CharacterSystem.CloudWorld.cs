using FishNet.Connection;
using System.Collections.Generic;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared.Core;
using FishMMO.Shared;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Cloud worlds (<see cref="SceneCloudWorld"/>): whoever sinks into the deck below the islands dies there.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>An ordinary death.</b> A character found below <see cref="SceneCloudWorld.DeathAltitude"/> is killed through
	/// <see cref="ICharacterDamageController.Kill"/> with no killer, which is the whole death path every other death takes:
	/// the dead flag, the death pose for observers, the <c>OnKilled</c> subscribers (this system's own, which sends a
	/// player its death dialog; the pet, arena and aggression systems), the ECA triggers and the cooldown reset. Nobody is
	/// credited, the same as an operator's kill: there is no one to give the standing, quests or achievements to. The
	/// player then respawns from the death dialog as from any death.
	/// </para>
	/// <para>
	/// <b>A walk, not a trigger.</b> One height comparison per character a few times a second, and none at all while no
	/// cloud world is loaded. A trigger volume under the islands would need a collider as wide as the scene in every cloud
	/// world and would fire only on entry, so a body that arrived below the line any other way (a load, a teleport, a
	/// revive) would never be told.
	/// </para>
	/// <para>
	/// <b>The body stays where the deck took it.</b> <c>KCCController</c> holds a character that nobody is steering once it
	/// is below the line, so a corpse does not fall for as long as its player sits at the death dialog (at terminal
	/// velocity it would leave the range network positions are written in within minutes).
	/// </para>
	/// <para>
	/// <b>Nothing is left in the void.</b> An NPC lost to the deck goes straight back to its spawner instead of becoming
	/// a corpse nobody can reach, and a revive of a body the deck holds (a resurrect accepted, an operator's revive) puts
	/// the character on the nearest island ground that will hold it (<see cref="CloudWorldLanding"/>). A bind point that
	/// is open air in a cloud world is passed over the same way, so a respawn cannot drop the character straight back in.
	/// </para>
	/// </remarks>
	public partial class CharacterSystem
	{
		[Tooltip("Seconds between checks for characters that have sunk into a cloud world's deck. Each check is one height comparison per character, and nothing at all while no cloud world is loaded.")]
		[SerializeField][Min(0.05f)] private float cloudWorldFallCheckRate = 0.25f;

		/// <summary>
		/// The characters one check found below the line. Gathered first and killed after, because a death runs
		/// subscribers that change the very collections the check walks (a pet despawned, a linger ended).
		/// </summary>
		private readonly List<ICharacter> lostToTheDeck = new List<ICharacter>();

		/// <summary>Kills every living character below a loaded cloud world's death altitude.</summary>
		private void OnPeriodicCloudWorldFallCheck(float deltaTime)
		{
			if (!SceneCloudWorld.Any)
			{
				return;
			}

			if (!Initialized || Server == null || Server.ServerState != ConnectionState.Started)
			{
				return;
			}

			lostToTheDeck.Clear();

			if (Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> data) &&
				data.ConnectionCharacters != null)
			{
				foreach (IPlayerCharacter player in data.ConnectionCharacters.Values)
				{
					CollectIfLost(player);
				}
			}

			// Combat-logout bodies are in the world with nobody driving them, and are not in ConnectionCharacters.
			foreach (LingeringCharacter lingering in lingeringCharacters.Values)
			{
				CollectIfLost(lingering.Character);
			}

			// NPCs and pets. Players hold no scene object entry; everything else here is not a character.
			foreach (ISceneObject sceneObject in SceneObject.Objects.Values)
			{
				if (sceneObject is ICharacter character && !(sceneObject is IPlayerCharacter))
				{
					CollectIfLost(character);
				}
			}

			for (int i = 0; i < lostToTheDeck.Count; ++i)
			{
				LoseToTheDeck(lostToTheDeck[i]);
			}
			lostToTheDeck.Clear();
		}

		/// <summary>Adds a living, spawned character that is below its cloud world's death altitude.</summary>
		private void CollectIfLost(ICharacter character)
		{
			if (character == null)
			{
				return;
			}

			GameObject gameObject = character.GameObject;
			if (gameObject == null ||
				!gameObject.activeInHierarchy ||
				character.NetworkObject == null ||
				!character.NetworkObject.IsSpawned)
			{
				// A pooled NPC is registered but inactive, and ResetState cleared its dead flag: it is not in the world.
				return;
			}

			if (character.IsFlagged(CharacterFlags.IsDead) || character.IsTeleporting)
			{
				return;
			}

			// A player still being put into the scene is immortal until it lands; its position is not yet its own.
			if (character is IPlayerCharacter && !character.IsFlagged(CharacterFlags.IsLoaded))
			{
				return;
			}

			if (!SceneCloudWorld.IsLost(gameObject.scene, character.Transform.position))
			{
				return;
			}

			lostToTheDeck.Add(character);
		}

		/// <summary>Kills a character the deck has, or fishes it back out when it cannot die.</summary>
		private void LoseToTheDeck(ICharacter character)
		{
			// Re-checked: an earlier death in this pass can have despawned it (a pet whose owner fell with it).
			if (character == null ||
				character.GameObject == null ||
				character.NetworkObject == null ||
				!character.NetworkObject.IsSpawned ||
				character.IsFlagged(CharacterFlags.IsDead))
			{
				return;
			}

			IPlayerCharacter player = character as IPlayerCharacter;

			if (character.TryGet(out ICharacterDamageController damageController))
			{
				damageController.Kill(null);
			}

			if (character.IsFlagged(CharacterFlags.IsDead))
			{
				if (player != null)
				{
					Log.Debug("CharacterSystem", $"{player.CharacterName} fell into the clouds in '{player.CurrentSceneName()}'.");
					SendSystemMessage(player.Owner, "You fell into the clouds.");
				}
				else if (character is NPC npc && !(character is Pet) && npc.IsCorpse)
				{
					/* A corpse down there is one nobody can reach or loot, held for its whole decay. Returned at
					 * once instead, which is also what schedules the spawner's respawn. Whoever knocked it off
					 * forfeits the loot: the deck took it, not them. */
					npc.ReturnToPool();
				}
				return;
			}

			/* It could not die: immortal (an operator's god mode, a quest NPC authored so), or no health to lose. A player
			 * is put back on the nearest ground that holds; otherwise it would fall for ever. An NPC is left to its brain,
			 * whose path back to its navigation mesh is its own business. */
			if (player != null && TryLiftOutOfTheDeck(player))
			{
				SendSystemMessage(player.Owner, "The clouds give you back.");
			}
		}

		/// <summary>
		/// Raised for every revive, whoever performed it: a revived body the deck holds is put on the nearest ground that
		/// holds, before the check can kill it again.
		/// </summary>
		private void CharacterDamageController_OnResurrectedFromTheDeck(ICharacter resurrector, ICharacter resurrected)
		{
			if (resurrected is IPlayerCharacter player &&
				player.GameObject != null &&
				SceneCloudWorld.IsLost(player.GameObject.scene, player.Transform.position))
			{
				TryLiftOutOfTheDeck(player);
			}
		}

		/// <summary>Moves a player to the nearest place in its cloud world that holds. False when there is none.</summary>
		private bool TryLiftOutOfTheDeck(IPlayerCharacter player)
		{
			if (player == null || player.Motor == null || player.GameObject == null ||
				!SceneCloudWorld.TryGet(player.GameObject.scene, out SceneCloudWorld world) ||
				!TryGetSceneDetails(player, out WorldSceneDetails details) ||
				!CloudWorldLanding.TryFind(world, details, player.Transform.position, out RescuePoint landing))
			{
				return false;
			}

			Quaternion rotation = landing.HasRotation ? landing.Rotation : player.Transform.rotation;
			// Through the motor, never the transform: prediction reconciles against the motor.
			player.Motor.SetPositionAndRotationAndVelocity(landing.Position, rotation, Vector3.zero);
			Log.Debug("CharacterSystem", $"{player.CharacterName} was lifted out of the clouds to {landing.Kind} '{landing.Name}'.");
			return true;
		}

		/// <summary>
		/// Where a respawn in the scene the player is standing in puts it: its bind point, unless the scene is a cloud
		/// world and the bind point is open air there, in which case the nearest place that holds.
		/// </summary>
		private void ResolveSameSceneRespawn(IPlayerCharacter player, out Vector3 position, out Quaternion rotation)
		{
			position = player.BindPosition;
			rotation = Quaternion.identity;

			if (player.GameObject == null ||
				!SceneCloudWorld.TryGet(player.GameObject.scene, out SceneCloudWorld world) ||
				CloudWorldLanding.Holds(world, position) ||
				!TryGetSceneDetails(player, out WorldSceneDetails details) ||
				!CloudWorldLanding.TryFind(world, details, position, out RescuePoint landing))
			{
				return;
			}

			Log.Debug("CharacterSystem", $"{player.CharacterName}'s bind point {position} is open air in a cloud world; respawning at {landing.Kind} '{landing.Name}'.");
			position = landing.Position;
			if (landing.HasRotation)
			{
				rotation = landing.Rotation;
			}
		}

		/// <summary>The cached details of the scene the player is physically in.</summary>
		private bool TryGetSceneDetails(IPlayerCharacter player, out WorldSceneDetails details)
		{
			details = null;
			string scene = player.CurrentSceneName();
			return !string.IsNullOrEmpty(scene) &&
				Server != null &&
				Server.BehaviourRegistry.TryGet(out ISceneServerSystem<NetworkConnection> sceneServerSystem) &&
				sceneServerSystem.WorldSceneDetailsCache != null &&
				sceneServerSystem.WorldSceneDetailsCache.Scenes.TryGetValue(scene, out details) &&
				details != null;
		}
	}
}
