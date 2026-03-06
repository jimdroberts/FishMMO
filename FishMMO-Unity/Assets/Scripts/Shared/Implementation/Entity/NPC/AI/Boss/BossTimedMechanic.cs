using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Defines a timed mechanic that fires at regular intervals during a boss encounter.
	/// E.g., "Every 30 seconds, cast Meteor."
	/// </summary>
	[Serializable]
	public class BossTimedMechanic
	{
		/// <summary>
		/// Seconds between activations. First activation happens after this delay.
		/// </summary>
		[Tooltip("Seconds between activations.")]
		public float Interval = 30f;

		/// <summary>
		/// The ability template ID to force-activate when the timer fires.
		/// The boss must know this ability (listed in NPC.Abilities).
		/// </summary>
		[Tooltip("Ability template ID to activate.")]
		public int AbilityTemplateID;

		/// <summary>
		/// Optional: NPC prefabs to spawn each time the mechanic fires.
		/// </summary>
		[Tooltip("NPC prefabs to spawn each activation.")]
		public List<GameObject> SpawnPrefabs = new List<GameObject>();

		/// <summary>
		/// Spawn offsets relative to the boss for each prefab.
		/// </summary>
		[Tooltip("Spawn positions relative to boss.")]
		public List<Vector3> SpawnOffsets = new List<Vector3>();

		/// <summary>
		/// Optional phases during which this mechanic is active.
		/// Empty means active in all phases.
		/// </summary>
		[Tooltip("Phase indices (0-based) during which this mechanic fires. Empty = all phases.")]
		public List<int> ActivePhases = new List<int>();
	}
}