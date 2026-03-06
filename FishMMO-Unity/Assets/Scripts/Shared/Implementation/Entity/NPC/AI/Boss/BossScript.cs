using System.Collections.Generic;
using UnityEngine;
using FishMMO.Logging;

namespace FishMMO.Shared
{
	/// <summary>
	/// ScriptableObject that defines a boss encounter's phases and timed mechanics.
	/// Assign to <see cref="AIController.BossScript"/> to make an NPC a scripted boss.
	/// <para>
	/// The <see cref="AIController"/> evaluates the boss script every tick:
	/// <list type="number">
	///   <item>Checks health against phase thresholds.</item>
	///   <item>On phase change, applies overrides (behavior tree, attacking state, ability rotation) and spawns adds.</item>
	///   <item>Ticks all active timed mechanics and force-activates abilities or spawns when timers fire.</item>
	/// </list>
	/// </para>
	/// <para>
	/// <b>Example setup:</b>
	/// <code>
	/// Phases:
	///   [0] HP ≥ 70%  — default behavior
	///   [1] HP &lt; 70%  — spawn 2 adds, switch to Phase2 behavior tree
	///   [2] HP &lt; 40%  — enrage: switch to melee attacking state
	///
	/// Timed Mechanics:
	///   Meteor — every 30s, force-cast AbilityTemplate #5
	///   Summon — every 60s, spawn skeleton prefab (only in phase 1)
	/// </code>
	/// </para>
	/// </summary>
	[CreateAssetMenu(fileName = "New Boss Script", menuName = "FishMMO/Character/NPC/AI/Boss Script")]
	public class BossScript : ScriptableObject
	{
		/// <summary>
		/// Ordered boss phases. Must be sorted from highest health threshold to lowest.
		/// Phase 0 is the opening phase.
		/// </summary>
		[Tooltip("Ordered phases (highest HP threshold first).")]
		public List<BossPhase> Phases = new List<BossPhase>();

		/// <summary>
		/// Timed mechanics that fire at regular intervals during the encounter.
		/// </summary>
		[Tooltip("Timed abilities / spawns that fire at regular intervals.")]
		public List<BossTimedMechanic> TimedMechanics = new List<BossTimedMechanic>();

		/// <summary>
		/// When true, the boss fully heals and resets phases when leashing back home.
		/// </summary>
		[Tooltip("Heal and reset phases when the boss leashes.")]
		public bool ResetOnLeash = true;
	}
}