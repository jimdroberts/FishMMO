using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Defines a single phase of a boss encounter. A boss transitions to the next phase
	/// when its health drops below <see cref="HealthThreshold"/>. Each phase can override
	/// the boss's behavior tree, attacking state, and spawn adds.
	/// </summary>
	[Serializable]
	public class BossPhase
	{
		/// <summary>
		/// Health percentage (0-1) at which this phase activates. E.g., 0.7 means 70% HP.
		/// Phases should be ordered from highest to lowest threshold.
		/// </summary>
		[Range(0f, 1f)]
		[Tooltip("Health percentage (0-1) at which this phase activates.")]
		public float HealthThreshold = 1.0f;

		/// <summary>
		/// Optional behavior tree override for this phase. If null, the boss keeps its
		/// current tree.
		/// </summary>
		[Tooltip("Optional behavior tree override for this phase.")]
		public AIBehaviorTree BehaviorTreeOverride;

		/// <summary>
		/// Optional attacking state override. Changes the boss's combat style mid-fight.
		/// E.g., Phase 2 switches from melee to caster.
		/// </summary>
		[Tooltip("Optional attacking state override for this phase.")]
		public BaseAIState AttackingStateOverride;

		/// <summary>
		/// Optional ability rotation override for this phase.
		/// </summary>
		[Tooltip("Optional ability rotation override for this phase.")]
		public AIAbilityRotation AbilityRotationOverride;

		/// <summary>
		/// NPC prefabs to spawn when entering this phase (adds / reinforcements).
		/// </summary>
		[Tooltip("NPC prefabs to spawn when this phase starts.")]
		public List<GameObject> SpawnOnEnter = new List<GameObject>();

		/// <summary>
		/// Spawn offset positions relative to the boss for each add.
		/// If fewer offsets than SpawnOnEnter, extras spawn at the boss's position.
		/// </summary>
		[Tooltip("Spawn positions relative to boss for each add.")]
		public List<Vector3> SpawnOffsets = new List<Vector3>();

		/// <summary>
		/// Optional emote / announcement when entering this phase.
		/// </summary>
		[Tooltip("Text announcement when phase begins (e.g., boss yell).")]
		public string PhaseAnnouncement;
	}
}