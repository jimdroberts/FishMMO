using System;
using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Logging;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// The Combat module's scene-server system (2026-11-20): what a death does and how a player comes back from it. A kill's
	/// consequences (the death flag, the buffs dropped, an instance's lives, a combat-logged body's end, the death dialog,
	/// an NPC's or pet's despawn), respawning at the bind point, resurrect offers and their acceptance, and the dialog for a
	/// player who loads dead. Moved out of the core's character system, which lends it what a death touches that is the
	/// world's through <see cref="ICharacterDeathHost"/>.
	/// </summary>
	[CreateAssetMenu(fileName = "CombatSystem", menuName = "FishMMO/Server/SceneServer/Combat System", order = 1)]
	public class CombatSystem : ServerBehaviour
	{
		private ICharacterDeathHost deathHost;
		private ICharacterSystem<NetworkConnection, Scene> characterSystem;

		/// <inheritdoc />
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error("CombatSystem", "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}
			if (!Server.BehaviourRegistry.TryGet(out deathHost) || deathHost == null ||
				!Server.BehaviourRegistry.TryGet(out characterSystem) || characterSystem == null)
			{
				// The character system lends what a death touches that is the world's; without it no death can be handled.
				Log.Error("CombatSystem", "InitializeOnce: the character system (ICharacterDeathHost) is not registered yet.");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			Server.NetworkWrapper.RegisterBroadcast<RespawnAtBindPointBroadcast>(OnClientRespawnAtBindPointBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<ResurrectAcceptBroadcast>(OnClientResurrectAcceptBroadcastReceived, true);

			ICharacterDamageController.OnKilled += CharacterDamageController_OnKilled;
			ICharacterDamageController.OnResurrectOffered += CharacterDamageController_OnResurrectOffered;
			characterSystem.OnSpawnCharacter += CharacterSystem_OnSpawnCharacter;

			if (Server is IPeriodicUpdateSystem periodicSystem)
			{
				periodicSystem.RegisterPeriodicCallback(30f, OnPeriodicRespawnResurrectSweep);
			}

			Log.Debug("CombatSystem", "Initialized");
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <inheritdoc />
		public override void OnDeinitialize()
		{
			ICharacterDamageController.OnKilled -= CharacterDamageController_OnKilled;
			ICharacterDamageController.OnResurrectOffered -= CharacterDamageController_OnResurrectOffered;
			if (characterSystem != null)
			{
				characterSystem.OnSpawnCharacter -= CharacterSystem_OnSpawnCharacter;
			}
			if (Server != null)
			{
				Server.NetworkWrapper.UnregisterBroadcast<RespawnAtBindPointBroadcast>(OnClientRespawnAtBindPointBroadcastReceived);
				Server.NetworkWrapper.UnregisterBroadcast<ResurrectAcceptBroadcast>(OnClientResurrectAcceptBroadcastReceived);
				if (Server is IPeriodicUpdateSystem periodicSystem)
				{
					periodicSystem.UnregisterPeriodicCallback(OnPeriodicRespawnResurrectSweep);
				}
			}
			pendingResurrectOffers.Clear();
			deathHost = null;
			characterSystem = null;
		}

		/// <summary>
		/// A player who spawns dead (reconnect-while-dead, a scene transfer) is told again, so the death dialog reappears.
		/// </summary>
		private void CharacterSystem_OnSpawnCharacter(NetworkConnection conn, IPlayerCharacter character, Scene scene)
		{
			if (conn != null && conn.IsActive && character != null && character.IsFlagged(CharacterFlags.IsDead))
			{
				Server.NetworkWrapper.Broadcast(conn, new DeathBroadcast(), true, Channel.Reliable);
			}
		}

		/// <summary>
		/// Handles character killed events, processes player and NPC deaths, respawns, and updates state.
		/// </summary>
		/// <param name="killer">Character that performed the kill.</param>
		/// <param name="defender">Character that was killed.</param>
		private void CharacterDamageController_OnKilled(ICharacter killer, ICharacter defender)
		{
			if (defender == null || deathHost == null)
			{
				return;
			}

			IPlayerCharacter playerCharacter = defender as IPlayerCharacter;

			/* Flag the death before running anything else.
			 *
			 * CharacterDamageController.Kill guards re-entry with this exact flag, and now sets
			 * it itself before it dispatches OnKilled, so it is already set when this handler
			 * runs. Setting it again here is a harmless backstop: nothing side-effecting may run
			 * while it is clear, or a second Kill on the same character would sail straight past
			 * the guard and recurse. Buff removal below is exactly such a call: it invokes each
			 * buff's removal effects, which run game logic.
			 *
			 * Do NOT revive or teleport here — the player chooses Respawn or Resurrect. */
			if (playerCharacter != null)
			{
				playerCharacter.EnableFlags(CharacterFlags.IsDead);
			}

			if (defender.TryGet(out ICharacterBuffs buffController))
			{
				buffController.RemoveAll(false);
			}

			// Handle Player deaths
			if (playerCharacter != null)
			{
				/* A difficulty that limits lives spends one here.
				 *
				 * After the death flag is set and the buffs are gone, so the character is in the
				 * state the rest of the death path expects before anything can move it. */
				deathHost.ApplyInstanceDeathRules(playerCharacter);

				//Log.Debug("CharacterSystem", $"PlayerCharacter: {playerCharacter.GameObject.name} Died");

				// A combat-logged body has no owner to notify — it was killed precisely because
				// nobody was driving it. Skip the broadcast and end the linger now: the death is
				// the outcome the body was left exposed for, and persisting it immediately means
				// a crash cannot undo it.
				if (playerCharacter.Owner == null || !playerCharacter.Owner.IsActive)
				{
					if (!deathHost.TryFinalizeCombatLinger(playerCharacter.ID, "killed while logged out"))
					{
						/* Ownerless and not lingering. There is no connection to notify and no
						 * linger to finalise, so the character is flagged dead with nothing
						 * driving it out of that state — the periodic save will persist the
						 * flag and the player will meet the death dialog on their next login.
						 * Not known to be reachable, but silence here would make it invisible
						 * if it ever became so. */
						Log.Warning("CombatSystem",
							$"Character {playerCharacter.ID} was killed with no active owner and no combat-logout linger; " +
							"no death notification was sent. It will be flagged dead on its next load.");
					}
					return;
				}

				// Send the death broadcast to the owning client so the death dialog appears.
				Server.NetworkWrapper.Broadcast(playerCharacter.Owner,
					new DeathBroadcast(), true, FishNet.Transporting.Channel.Reliable);
			}
			else
			{
				// Handle NPC deaths
				NPC npc = defender as NPC;
				if (npc != null)
				{
					// A pet (the Pets module's), seen through the core: it is saved with its owner and fights for it.
					IPersistedPet pet = defender as IPersistedPet;
					if (pet != null)
					{
						//Log.Debug("CharacterSystem", $"Pet: {pet.GameObject.name} Died");

						IPlayerCharacter petOwner = npc.CompanionOwner as IPlayerCharacter;
						if (petOwner != null)
						{
							/* The owner's NetworkObject is checked rather than assumed. A pet is
							 * despawned alongside its owner's character, but the two can land in
							 * the same frame — an owner despawned first leaves PetOwner pointing
							 * at a destroyed Unity object, and dereferencing it throws out of
							 * this handler, skipping the despawn below and every remaining
							 * OnKilled subscriber. */
							NetworkObject ownerObject = petOwner.NetworkObject;
							if (ownerObject != null)
							{
								deathHost.NotifyPetKilled(ownerObject.Owner, petOwner, pet);
							}
						}

						/* Outside the owner check, deliberately. A pet whose owner reference has
						 * already been cleared — or which was never owned by a player — still has
						 * to leave the world. Nested inside, such a pet was left spawned, flagged
						 * dead, with its brain still enabled and nothing anywhere that would ever
						 * collect it: Pet.Despawn skips the corpse timer that reclaims an NPC. */
						npc.Despawn();
					}
					else
					{
						//Log.Debug("CharacterSystem", $"NPC: {npc.GameObject.name} Died");
						npc.Despawn();
					}
				}
			}
		}

		/// <summary>
		/// Handles a dead player asking to respawn at its bind point: revived at full health and moved there. Rate-limited per
		/// connection.
		/// </summary>
		private void OnClientRespawnAtBindPointBroadcastReceived(NetworkConnection conn, RespawnAtBindPointBroadcast msg, FishNet.Transporting.Channel channel)
		{
			if (!TryBeginRespawnResurrectGuard(conn.ClientId, RespawnOperation, out long guardKey))
				return;

			try
			{
				if (deathHost == null || !Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> data))
					return;
				if (!data.ConnectionCharacters.TryGetValue(conn, out IPlayerCharacter player))
					return;

				// Only dead players can respawn at bind point.
				if (!player.IsFlagged(CharacterFlags.IsDead))
					return;

				// Choosing the bind point declines any resurrect that was on the table.
				pendingResurrectOffers.Remove(player.ID);

				player.DisableFlags(CharacterFlags.IsDead);
				if (player.TryGet(out ICharacterDamageController damageController))
					damageController.Revive(null, 999999);

				// In this scene where the position holds, or in another by the ordered transfer (the character system's).
				deathHost.RespawnAtBindPoint(conn, player);
			}
			finally
			{
				EndRespawnResurrectGuard(guardKey);
			}
		}

		/// <summary>
		/// Handles a dead player accepting a resurrect from another player.
		/// Revives at the current position (corpse location), no teleport.
		/// Rate-limited per-connection to prevent spam.
		/// </summary>
		/// <param name="conn">The network connection of the dead player.</param>
		/// <param name="msg">The resurrect-accept broadcast message.</param>
		/// <param name="channel">The channel on which the broadcast was received.</param>
		private void OnClientResurrectAcceptBroadcastReceived(NetworkConnection conn, ResurrectAcceptBroadcast msg, FishNet.Transporting.Channel channel)
		{
			if (!TryBeginRespawnResurrectGuard(conn.ClientId, ResurrectOperation, out long guardKey))
				return;

			try
			{
				if (deathHost == null || !Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> data))
					return;
				if (!data.ConnectionCharacters.TryGetValue(conn, out IPlayerCharacter player))
					return;

				// Only dead players can accept a resurrect.
				if (!player.IsFlagged(CharacterFlags.IsDead))
					return;

				/* Re-checked here, not just at the offer.
				 *
				 * An offer recorded outside a no-resurrection dungeon stands for thirty seconds,
				 * and a character can be moved into one inside that window — so the offer check
				 * alone is a prompt filter, and this is the rule. Where the character is standing
				 * when it redeems is what decides. */
				if (!deathHost.IsResurrectionAllowed(player))
				{
					pendingResurrectOffers.Remove(player.ID);
					deathHost.SendSystemMessage(conn, "The dead cannot be raised in this dungeon.");
					return;
				}

				/* The accept must answer an offer this server actually recorded. Checking only
				 * that the named resurrector existed and shared the scene — as this did — let a
				 * dead player revive themselves at full health whenever anyone else was nearby,
				 * with no resurrect ever cast. */
				if (!pendingResurrectOffers.TryGetValue(player.ID, out PendingResurrectOffer offer))
				{
					return;
				}

				if (MonotonicClock.NowSeconds >= offer.ExpiresAt)
				{
					pendingResurrectOffers.Remove(player.ID);
					return;
				}

				if (msg.ResurrectorID <= 0 || msg.ResurrectorID != offer.ResurrectorID)
				{
					return;
				}

				/* The resurrector must still be present and in the same scene instance when the
				 * offer is accepted.
				 *
				 * Compared by scene handle, not by name. CharactersByID holds every character on
				 * this scene server, across every scene instance it hosts — and scene stacking
				 * means two channels of one open-world scene, or two copies of one dungeon, share
				 * a name. Inside an instance it is worse: SceneName keeps naming the open-world
				 * scene the character will return to, so every character who entered a dungeon
				 * from the same zone compares equal regardless of which instance they are in.
				 * The handle is unambiguous within this process, which is the only place this
				 * check runs. Same rule as BindstoneAction. */
				if (!data.CharactersByID.TryGetValue(offer.ResurrectorID, out IPlayerCharacter resurrector) ||
					resurrector.GameObject == null ||
					player.GameObject == null ||
					resurrector.GameObject.scene.handle != player.GameObject.scene.handle)
				{
					return;
				}

				pendingResurrectOffers.Remove(player.ID);

				player.DisableFlags(CharacterFlags.IsDead);
				if (player.TryGet(out ICharacterDamageController damageController))
				{
					// The offer's amount rather than a blanket full heal, credited to the
					// resurrector so their ECA resurrect triggers fire.
					damageController.Revive(resurrector, offer.Amount);
				}
			}
			finally
			{
				EndRespawnResurrectGuard(guardKey);
			}
		}

		/// <summary>
		/// A resurrect that has been offered to a character and is awaiting their answer.
		/// </summary>
		private sealed class PendingResurrectOffer
		{
			/// <summary>Character that cast the resurrect.</summary>
			public long ResurrectorID;
			/// <summary>Health the offer restores, as configured on the ability that made it.</summary>
			public int Amount;
			/// <summary>When the offer lapses if it is not answered, in <see cref="MonotonicClock"/> seconds.</summary>
			public double ExpiresAt;
		}

		/// <summary>
		/// Outstanding resurrect offers, keyed by the character they were made to. Main-thread only.
		/// </summary>
		/// <remarks>
		/// Accepting a resurrect used to be validated only by checking that the named resurrector
		/// existed and stood in the same scene — never that they had offered anything. A dead player
		/// could therefore revive themselves at full health at will, by naming any other character
		/// in the scene, without a resurrect ever being cast. Recording the offer makes the accept
		/// answerable to something real, and carries the ability's configured heal amount through to
		/// the revive instead of substituting a blanket full heal.
		/// </remarks>
		private readonly Dictionary<long, PendingResurrectOffer> pendingResurrectOffers =
			new Dictionary<long, PendingResurrectOffer>();

		/// <summary>How long an unanswered resurrect offer stands.</summary>
		private static readonly TimeSpan ResurrectOfferLifetime = TimeSpan.FromSeconds(30);

		/// <summary>
		/// Records a resurrect offer and tells the target's client, so its death dialog can present
		/// the accept option.
		/// </summary>
		/// <param name="initiator">Character casting the resurrect.</param>
		/// <param name="target">Character being offered the resurrect.</param>
		/// <param name="amount">Health the offer would restore.</param>
		private void CharacterDamageController_OnResurrectOffered(ICharacter initiator, ICharacter target, int amount)
		{
			if (initiator == null || amount <= 0 || deathHost == null)
			{
				return;
			}

			IPlayerCharacter player = target as IPlayerCharacter;
			if (player == null || player.Owner == null || !player.Owner.IsActive)
			{
				return;
			}

			// Only a dead character has anything to accept.
			if (!player.IsFlagged(CharacterFlags.IsDead))
			{
				return;
			}

			/* Some dungeons forbid resurrection outright, and offering one there would put a
			 * button on a dead player's screen that the accept path is going to refuse. Recording
			 * nothing is also what makes that refusal safe: there is no stale offer to redeem the
			 * moment they leave. */
			if (!deathHost.IsResurrectionAllowed(player))
			{
				deathHost.SendSystemMessage(player.Owner, "The dead cannot be raised in this dungeon.");
				return;
			}

			pendingResurrectOffers[player.ID] = new PendingResurrectOffer
			{
				ResurrectorID = initiator.ID,
				Amount = amount,
				ExpiresAt = MonotonicClock.NowSeconds + ResurrectOfferLifetime.TotalSeconds,
			};

			Server.NetworkWrapper.Broadcast(player.Owner,
				new ResurrectOfferBroadcast { ResurrectorID = initiator.ID }, true, FishNet.Transporting.Channel.Reliable);
		}

		/// <summary>Ingress guard for the respawn and resurrect broadcasts, against rate-limit spam.</summary>
		private readonly IngressGuard respawnResurrectGuard = new IngressGuard();

		private const byte RespawnOperation = 1;
		private const byte ResurrectOperation = 2;
		private const int RespawnResurrectDebounceMs = 2000;

		private bool TryBeginRespawnResurrectGuard(int clientId, byte operation, out long guardKey)
		{
			return respawnResurrectGuard.TryBegin(clientId, operation, RespawnResurrectDebounceMs, out guardKey);
		}

		private void EndRespawnResurrectGuard(long guardKey)
		{
			respawnResurrectGuard.End(guardKey);
		}

		/// <summary>Evicts stale guard entries and unanswered offers, so neither grows for the life of the process.</summary>
		private void OnPeriodicRespawnResurrectSweep(float deltaTime)
		{
			if (Server == null || Server.ServerState != ConnectionState.Started)
				return;

			respawnResurrectGuard.Sweep(30f, 120f, 128);

			SweepExpiredResurrectOffers();
		}

		/// <summary>
		/// Drops resurrect offers that were never answered.
		/// </summary>
		/// <remarks>
		/// The accept path rejects an expired offer on its own, so this is about not retaining one
		/// entry per unanswered resurrect for the life of the process — a player who is offered a
		/// resurrect and respawns instead, or simply logs out, leaves nothing behind to remove it.
		/// </remarks>
		private void SweepExpiredResurrectOffers()
		{
			if (pendingResurrectOffers.Count == 0)
			{
				return;
			}

			double now = MonotonicClock.NowSeconds;
			List<long> expired = null;
			foreach (var kvp in pendingResurrectOffers)
			{
				if (now >= kvp.Value.ExpiresAt)
				{
					(expired ??= new List<long>()).Add(kvp.Key);
				}
			}

			if (expired == null)
			{
				return;
			}

			for (int i = 0; i < expired.Count; ++i)
			{
				pendingResurrectOffers.Remove(expired[i]);
			}
		}
	}
}
