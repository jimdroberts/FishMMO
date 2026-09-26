using System.Collections.Generic;
using UnityEngine;
using FishMMO.Logging;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting;
using UnityEngine.SceneManagement;
using FishMMO.Shared.Core;
using FishMMO.Shared;
using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer.AI
{
	/// <summary>
	/// Per-NPC runtime state for a <see cref="BossScript"/>. Tracks the current phase index,
	/// timed mechanic timers, and handles phase transitions and mechanic activations.
	/// <para>
	/// This is a plain C# class (not a ScriptableObject) because it holds mutable state
	/// that differs per NPC instance — the same <see cref="BossScript"/> asset may be
	/// shared across multiple boss spawns.
	/// </para>
	/// </summary>
	public class BossScriptState
	{
		/// <summary>
		/// The asset defining phases and mechanics.
		/// </summary>
		public BossScript Script { get; private set; }

		/// <summary>
		/// Current phase index into <see cref="BossScript.Phases"/>.
		/// </summary>
		public int CurrentPhaseIndex { get; private set; }

		/// <summary>
		/// Returns the current <see cref="BossPhase"/>, or null if no phases are configured.
		/// </summary>
		public BossPhase CurrentPhase
		{
			get
			{
				if (Script == null || Script.Phases == null || CurrentPhaseIndex >= Script.Phases.Count)
					return null;
				return Script.Phases[CurrentPhaseIndex];
			}
		}

		/// <summary>
		/// Per-mechanic countdown timers. Index matches <see cref="BossScript.TimedMechanics"/>.
		/// </summary>
		private float[] mechanicTimers;

		/// <summary>
		/// True if this state has been initialized.
		/// </summary>
		public bool Initialized { get; private set; }

		/// <summary>
		/// The adds this boss called in that are still alive, in the order they arrived.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Its own list rather than the boss's pack. The pack is often the boss's spawner's, with
		/// NPCs in it that were never the boss's to call in or send away, so "every member but the
		/// boss" is not "the boss's adds". Only what <see cref="SpawnAdds"/> spawned is entered here.
		/// </para>
		/// <para>
		/// <b>Membership is the living adds, kept from both ends</b>, as a pack's is. An add holds
		/// its boss (<see cref="AIController.Summoner"/>) and leaves this list on death, on despawn
		/// and pool reset, and when it is destroyed with its scene
		/// (<see cref="AIController.LeaveSummoner"/>). A brain is pooled and reissued, so a
		/// reference kept past any of those would, on the next leash, despawn whatever NPC the pool
		/// had since made of it — a spawner's, or another boss's.
		/// </para>
		/// </remarks>
		private readonly List<AIController> liveAdds = new List<AIController>();

		/// <summary>How many of this boss's adds are alive.</summary>
		public int LiveAddCount => liveAdds.Count;

		public BossScriptState(BossScript script)
		{
			Script = script;
			CurrentPhaseIndex = 0;
			Initialized = false;

			if (script != null && script.TimedMechanics != null)
			{
				mechanicTimers = new float[script.TimedMechanics.Count];
				for (int i = 0; i < mechanicTimers.Length; i++)
				{
					mechanicTimers[i] = script.TimedMechanics[i].Interval;
				}
			}
			else
			{
				mechanicTimers = System.Array.Empty<float>();
			}

			Initialized = true;
		}

		/// <summary>
		/// Whether one more add may be called in, with <paramref name="liveAdds"/> of the boss's
		/// adds already alive.
		/// </summary>
		/// <remarks>
		/// A phase's adds always arrive: a phase is entered at most once per pull, so they are
		/// bounded by what was authored. A timed mechanic's arrive only below the cap, since it fires
		/// for as long as the fight lasts. See <see cref="BossScript.MaxLiveAdds"/>.
		/// </remarks>
		/// <param name="phaseAdd">True for a phase's <see cref="BossPhase.SpawnOnEnter"/>, false for a timed mechanic's.</param>
		/// <param name="liveAdds">The boss's adds alive now, including any called in earlier in the same call.</param>
		/// <param name="maxLiveAdds">The boss script's cap; anything below one is treated as one.</param>
		public static bool MayCallInAdd(bool phaseAdd, int liveAdds, int maxLiveAdds)
		{
			return phaseAdd || liveAdds < Mathf.Max(1, maxLiveAdds);
		}

		/// <summary>
		/// Records <paramref name="add"/> as one of this boss's live adds.
		/// </summary>
		/// <remarks>
		/// An add is one boss's at a time; one already held by another is taken from it first.
		/// </remarks>
		internal void AdoptAdd(AIController add)
		{
			if (add == null || ReferenceEquals(add.Summoner, this))
			{
				return;
			}

			add.LeaveSummoner();
			add.Summoner = this;
			liveAdds.Add(add);
		}

		/// <summary>
		/// Drops <paramref name="add"/> from this boss's live adds. Called by the add as it leaves:
		/// see <see cref="AIController.LeaveSummoner"/>.
		/// </summary>
		internal void ForgetAdd(AIController add)
		{
			liveAdds.Remove(add);
		}

		/// <summary>
		/// Sends every live add this boss called in back to the pool, for a leash reset.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A reset boss is back at full health in its first phase, and phase adds only arrive on a
		/// phase transition — so adds left standing met the next pull beside a boss that would call
		/// the same adds in again, and a boss pulled and dropped a few times filled its room with
		/// them. What a leash reset promises is the encounter as it was before the pull.
		/// </para>
		/// <para>
		/// Only this list is touched, never the boss's pack: members a spawner put there are not
		/// the boss's adds and stay. A dead add left the list when it died, so its corpse keeps its
		/// loot and decays as any other. Each add is taken off the list and unlinked before its
		/// despawn, whose pool reset would otherwise come back here mid-walk; and each despawn is
		/// isolated, so one that throws does not leave the rest standing.
		/// </para>
		/// </remarks>
		/// <param name="networkManager">The network manager the boss and its adds are spawned under.</param>
		/// <returns>How many adds were despawned.</returns>
		internal int DismissAdds(NetworkManager networkManager)
		{
			int dismissed = 0;
			for (int i = liveAdds.Count - 1; i >= 0; --i)
			{
				AIController add = liveAdds[i];
				liveAdds.RemoveAt(i);

				// Destroyed with its scene: nothing is left to despawn.
				if (add == null)
				{
					continue;
				}
				add.Summoner = null;

				try
				{
					if (PersistentPool.Despawn(networkManager, add.NetworkObject))
					{
						++dismissed;
					}
				}
				catch (System.Exception ex)
				{
					Log.Error("BossScriptState", $"Could not despawn add {add.name} on a leash reset: {ex}");
				}
			}
			return dismissed;
		}

		/// <summary>
		/// Stops counting any add as this boss's, leaving every one of them where it is.
		/// </summary>
		/// <remarks>
		/// For a boss that dies, despawns, is destroyed or is given another script. Its adds fight
		/// on exactly as they did before this list existed; they are simply no longer the boss's to
		/// send away, and no later occupant of the boss's pooled brain inherits them.
		/// </remarks>
		internal void ReleaseAdds()
		{
			for (int i = 0; i < liveAdds.Count; ++i)
			{
				AIController add = liveAdds[i];
				if (!ReferenceEquals(add, null) && ReferenceEquals(add.Summoner, this))
				{
					add.Summoner = null;
				}
			}
			liveAdds.Clear();
		}

		/// <summary>
		/// Resets the boss to phase 0 and resets all mechanic timers.
		/// Called on leash or despawn.
		/// </summary>
		/// <remarks>
		/// The adds are not touched here: a leash dismisses them (<see cref="DismissAdds"/>) and a
		/// despawn releases them (<see cref="ReleaseAdds"/>), and <see cref="AIController"/> says
		/// which.
		/// </remarks>
		public void Reset()
		{
			CurrentPhaseIndex = 0;
			if (Script != null && Script.TimedMechanics != null)
			{
				for (int i = 0; i < mechanicTimers.Length; i++)
				{
					mechanicTimers[i] = Script.TimedMechanics[i].Interval;
				}
			}
		}

		/// <summary>
		/// Evaluates phase transitions based on current HP. Returns true if a phase change occurred.
		/// Call once per AI tick.
		/// </summary>
		/// <param name="controller">The boss NPC's AI controller.</param>
		/// <returns>True if a phase transition happened this tick.</returns>
		public bool EvaluatePhases(AIController controller)
		{
			if (Script == null || Script.Phases == null || Script.Phases.Count == 0)
				return false;

			if (!controller.Character.TryGet(out ICharacterDamageController dmg))
				return false;

			float hpPercent = dmg.ResourceInstance != null && dmg.ResourceInstance.FinalValue > 0
				? dmg.ResourceInstance.CurrentValue / dmg.ResourceInstance.FinalValue
				: 1f;

			// Check if we should advance to a later phase.
			// Phases are ordered highest threshold → lowest, so scan forward.
			int newPhase = CurrentPhaseIndex;
			for (int i = CurrentPhaseIndex + 1; i < Script.Phases.Count; i++)
			{
				if (hpPercent <= Script.Phases[i].HealthThreshold)
				{
					newPhase = i;
				}
				else
				{
					break;
				}
			}

			if (newPhase != CurrentPhaseIndex)
			{
				TransitionToPhase(controller, newPhase);
				return true;
			}

			return false;
		}

		/// <summary>
		/// Ticks all active timed mechanics for the current phase.
		/// Call once per AI tick with the delta time.
		/// </summary>
		/// <param name="controller">The boss NPC's AI controller.</param>
		/// <param name="deltaTime">Time since last tick.</param>
		public void TickMechanics(AIController controller, float deltaTime)
		{
			if (Script == null || Script.TimedMechanics == null) return;

			for (int i = 0; i < Script.TimedMechanics.Count; i++)
			{
				BossTimedMechanic mechanic = Script.TimedMechanics[i];
				if (mechanic == null) continue;

				// Check if this mechanic is active in the current phase.
				if (mechanic.ActivePhases != null && mechanic.ActivePhases.Count > 0 &&
					!mechanic.ActivePhases.Contains(CurrentPhaseIndex))
				{
					continue;
				}

				mechanicTimers[i] -= deltaTime;
				if (mechanicTimers[i] <= 0f)
				{
					mechanicTimers[i] = mechanic.Interval;
					ExecuteMechanic(controller, mechanic);
				}
			}
		}

		/// <summary>
		/// Transitions to a new phase, applying overrides and spawning adds.
		/// </summary>
		private void TransitionToPhase(AIController controller, int phaseIndex)
		{
			int oldPhase = CurrentPhaseIndex;
			CurrentPhaseIndex = phaseIndex;
			BossPhase phase = CurrentPhase;

			if (phase == null) return;

			Log.Debug("BossScriptState", $"Boss {controller.gameObject.name} transitioning from Phase {oldPhase} to Phase {phaseIndex} (HP threshold {phase.HealthThreshold:P0})");

			/* Phase overrides sit in front of the archetype's slots on the controller rather than
			 * overwriting them: the archetype is a shared asset, and the controller drops the
			 * overrides again when the script resets or the instance is pooled. A slot the phase
			 * leaves null keeps whatever an earlier phase installed. */
			controller.SetPhaseOverrides(phase.AttackingStateOverride, phase.BehaviorTreeOverride, phase.AbilityRotationOverride);

			// Spawn adds. A phase's always arrive; see MayCallInAdd.
			SpawnAdds(controller, phase.SpawnOnEnter, phase.SpawnOffsets, phaseAdds: true);

			// Phase announcement.
			if (!string.IsNullOrEmpty(phase.PhaseAnnouncement))
			{
				Log.Info("BossScriptState", $"[BOSS] {controller.gameObject.name}: {phase.PhaseAnnouncement}");

				/* One write for the whole observer set, not one per observer. The set overload
				 * serialises the message once and reuses the ArraySegment for every recipient;
				 * the per-connection overload it replaced rebuilt the same bytes for each of a
				 * raid's worth of observers, on the tick a phase change already costs the most. */
				controller.ServerManager.Broadcast(controller.NetworkObject, new ChatBroadcast()
				{
					Channel = ChatChannel.System,
					Text = phase.PhaseAnnouncement,
				}, true, Channel.Reliable);
			}
		}

		/// <summary>
		/// Executes a timed mechanic: force-activates an ability and/or spawns prefabs.
		/// </summary>
		private void ExecuteMechanic(AIController controller, BossTimedMechanic mechanic)
		{
			/* Force-activate the ability. != 0, not > 0: template ids are signed hashes, so a
			 * mechanic naming an ability whose id happens to be negative never fired. */
			if (mechanic.AbilityTemplateID != 0)
			{
				if (controller.Character.TryGet(out IAbilityController abilityController))
				{
					Ability ability = AIUtility.FindAbilityByTemplate(abilityController, mechanic.AbilityTemplateID);
					if (ability != null)
					{
						bool held = abilityController.RequiresHeld(ability.ID);
						abilityController.Activate(ability.ID, held);
					}
				}
			}

			// Spawn mechanic prefabs, up to the boss's cap of live adds.
			SpawnAdds(controller, mechanic.SpawnPrefabs, mechanic.SpawnOffsets, phaseAdds: false);
		}

		/// <summary>
		/// Calls in a boss's adds at offsets around it: drawn from the pool, into the boss's own
		/// scene instance, with their brains prepared where they stand, and joined to the boss's pack.
		/// </summary>
		/// <remarks>
		/// <para>
		/// It used to <c>Object.Instantiate</c> each prefab and spawn it with no scene. The adds
		/// landed in whichever scene was active rather than the boss's instance — outside its
		/// physics scene, its NavMesh and its scene's observers, on a scene server that stacks
		/// instances of one dungeon — and every add was a fresh instantiation the pool never
		/// supplied, although its corpse was then pooled like any other. Now they come out of the
		/// pool and go into the boss's scene as a spawner's NPCs do, and an interrupted spawn goes
		/// back into the pool.
		/// </para>
		/// <para>
		/// <b>Adds are the boss's pack.</b> They join the boss's pack as DPS — the pack its spawner
		/// put it in, or a new one the boss founds with itself as a member of no role, so its own
		/// targeting is untouched — and, when they arrive mid-fight, are alerted onto the boss's
		/// target at once rather than standing about until something hits them.
		/// </para>
		/// <para>
		/// <b>Adds are the boss's to send away, and there is a ceiling on them.</b> Each one is
		/// recorded as the boss's own (<see cref="AdoptAdd"/>), so a leash reset despawns exactly
		/// these and nothing else in the pack. A timed mechanic calls one in only while the boss has
		/// fewer than <see cref="BossScript.MaxLiveAdds"/> alive (<see cref="MayCallInAdd"/>); a
		/// phase's adds always arrive.
		/// </para>
		/// </remarks>
		/// <param name="phaseAdds">True for a phase's adds, which the cap does not hold back.</param>
		private void SpawnAdds(AIController controller, List<GameObject> prefabs, List<Vector3> offsets, bool phaseAdds)
		{
			if (prefabs == null || prefabs.Count == 0) return;

			NetworkManager networkManager = controller.NetworkManager;
			if (networkManager == null || controller.Character == null) return;

			Scene scene = controller.Character.GameObject.scene;
			if (!scene.IsValid() || !scene.isLoaded) return;

			Vector3 bossPos = controller.Character.Transform.position;
			Quaternion bossRot = controller.Character.Transform.rotation;

			AIBrainHost.TryGet(networkManager, out AIBrainHost host);
			NPCGroup pack = null;

			int maxLiveAdds = Script != null ? Script.MaxLiveAdds : 1;

			for (int i = 0; i < prefabs.Count; i++)
			{
				GameObject prefab = prefabs[i];
				if (prefab == null) continue;

				// Counted per add, so a mechanic naming several tops the boss up to its cap.
				if (!MayCallInAdd(phaseAdds, liveAdds.Count, maxLiveAdds)) break;

				NetworkObject prefabObject = prefab.GetComponent<NetworkObject>();
				if (prefabObject == null)
				{
					Log.Warning("BossScriptState", $"Boss {controller.gameObject.name} cannot call in add '{prefab.name}': it has no NetworkObject.");
					continue;
				}

				Vector3 offset = (offsets != null && i < offsets.Count) ? offsets[i] : Vector3.zero;
				Vector3 spawnPos = bossPos + bossRot * offset;

				AIController add = SpawnAdd(networkManager, host, prefabObject, scene, spawnPos, bossRot);
				if (add == null) continue;

				AdoptAdd(add);

				if (pack == null)
				{
					pack = ResolveAddPack(controller);
				}
				pack.AddMember(add, NPCGroupRole.DPS);
			}

			// Adds called in mid-fight join it at once, against whoever the boss is fighting.
			if (pack != null && controller.IsInCombatState)
			{
				ICharacter enemy = controller.TargetCharacter;
				if (enemy != null)
				{
					pack.AlertGroup(enemy);
				}
			}
		}

		/// <summary>
		/// Spawns one add from the pool into <paramref name="scene"/>, its brain prepared before the
		/// network spawn, or puts it back in the pool if anything interrupts that.
		/// </summary>
		/// <returns>The add's brain, or null when nothing was spawned or it has none.</returns>
		private static AIController SpawnAdd(NetworkManager networkManager, AIBrainHost host, NetworkObject prefab, Scene scene, Vector3 position, Quaternion rotation)
		{
			NetworkObject instance = networkManager.GetPooledInstantiated(prefab, position, rotation, true);
			if (instance == null)
			{
				Log.Warning("BossScriptState", $"The pool produced no instance of add '{prefab.name}'; is it among the network manager's spawnable prefabs?");
				return null;
			}

			NPC npc = instance.GetComponent<NPC>();
			AIController brain = null;
			bool completed = false;
			try
			{
				UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(instance.gameObject, scene);

				/* Prepared after the scene move and before the spawn, as a spawner's NPC is: the
				 * agent is warped onto the boss's NavMesh at the add's own spot, which becomes its
				 * home. Without a host the spawn hook has nothing to prepare it with either. */
				if (npc != null && host != null)
				{
					brain = host.Prepare(npc, position);
				}

				networkManager.ServerManager.Spawn(instance, null, scene);
				completed = true;
			}
			finally
			{
				if (!completed)
				{
					RollBackAdd(networkManager, instance);
				}
			}
			return brain;
		}

		/// <summary>
		/// Puts an add whose spawn was interrupted back into the pool, out of the world scenes.
		/// Runs while an exception is on its way out, so it must not throw one of its own.
		/// </summary>
		private static void RollBackAdd(NetworkManager networkManager, NetworkObject instance)
		{
			try
			{
				if (instance.IsSpawned)
				{
					networkManager.ServerManager.Despawn(instance, DespawnType.Pool);
				}
				else
				{
					// Never spawned, so no despawn will reset the brain prepared for it.
					AIBrainHost.RetireBrainOf(networkManager, instance);
					networkManager.StorePooledInstantiated(instance, true);
				}
				PersistentPool.Keep(networkManager, instance);
			}
			catch (System.Exception ex)
			{
				Log.Error("BossScriptState", $"Could not roll back an interrupted add spawn of {instance.name}: {ex}");
			}
		}

		/// <summary>
		/// The pack a boss's adds join: the boss's own, or a new one with the boss in it.
		/// </summary>
		/// <remarks>
		/// The boss joins a pack it founds with no role, so its targeting stays its personality's;
		/// its adds focus whoever the pack is alerted onto.
		/// </remarks>
		private static NPCGroup ResolveAddPack(AIController boss)
		{
			if (boss.Group != null && !boss.Group.Released)
			{
				return boss.Group;
			}

			NPCGroup pack = new NPCGroup(PackTactic.None, true, NPCGroup.DEFAULT_ORBIT_RADIUS, NPCGroup.DEFAULT_KITE_ROTATION_SPEED);
			pack.AddMember(boss, NPCGroupRole.None);
			return pack;
		}

	}
}