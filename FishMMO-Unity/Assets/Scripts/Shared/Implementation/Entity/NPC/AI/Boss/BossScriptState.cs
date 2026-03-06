using System.Collections.Generic;
using UnityEngine;
using FishMMO.Logging;
using FishNet.Managing;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
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
		/// Resets the boss to phase 0 and resets all mechanic timers.
		/// Called on leash or despawn.
		/// </summary>
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

			// Apply behavior tree override.
			if (phase.BehaviorTreeOverride != null)
			{
				controller.BehaviorTree = phase.BehaviorTreeOverride;
			}

			// Apply attacking state override.
			if (phase.AttackingStateOverride != null)
			{
				controller.AttackingState = phase.AttackingStateOverride;
			}

			// Apply ability rotation override.
			if (phase.AbilityRotationOverride != null)
			{
				controller.AbilityRotation = phase.AbilityRotationOverride;
			}

			// Spawn adds.
			SpawnAdds(controller, phase.SpawnOnEnter, phase.SpawnOffsets);

			// Phase announcement.
			if (!string.IsNullOrEmpty(phase.PhaseAnnouncement))
			{
				Log.Info("BossScriptState", $"[BOSS] {controller.gameObject.name}: {phase.PhaseAnnouncement}");
				// TODO: Broadcast announcement to nearby players via chat system.
			}
		}

		/// <summary>
		/// Executes a timed mechanic: force-activates an ability and/or spawns prefabs.
		/// </summary>
		private static void ExecuteMechanic(AIController controller, BossTimedMechanic mechanic)
		{
			// Force-activate the ability.
			if (mechanic.AbilityTemplateID > 0)
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

			// Spawn mechanic prefabs.
			SpawnAdds(controller, mechanic.SpawnPrefabs, mechanic.SpawnOffsets);
		}

		/// <summary>
		/// Spawns a list of NPC prefabs at offsets relative to the boss.
		/// </summary>
		private static void SpawnAdds(AIController controller, List<GameObject> prefabs, List<Vector3> offsets)
		{
			if (prefabs == null || prefabs.Count == 0) return;

			NetworkManager networkManager = controller.NetworkManager;
			if (networkManager == null) return;

			Vector3 bossPos = controller.Character.Transform.position;
			Quaternion bossRot = controller.Character.Transform.rotation;

			for (int i = 0; i < prefabs.Count; i++)
			{
				GameObject prefab = prefabs[i];
				if (prefab == null) continue;

				Vector3 offset = (offsets != null && i < offsets.Count) ? offsets[i] : Vector3.zero;
				Vector3 spawnPos = bossPos + bossRot * offset;

				GameObject instance = Object.Instantiate(prefab, spawnPos, bossRot);
				networkManager.ServerManager.Spawn(instance);
			}
		}

	}
}