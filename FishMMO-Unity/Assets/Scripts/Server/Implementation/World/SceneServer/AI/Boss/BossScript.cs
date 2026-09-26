using System.Collections.Generic;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishMMO.Server.Core.World.SceneServer;

namespace FishMMO.Server.Implementation.World.SceneServer.AI
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

		/// <summary>
		/// The most of this boss's adds that may be alive at once before its timed mechanics stop
		/// calling in more.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A timed mechanic fires every <see cref="BossTimedMechanic.Interval"/> for as long as the
		/// fight lasts, so a party that tanks the boss and ignores its adds used to meet one more
		/// every interval without end: a 30-second summon left forty NPCs in the instance after
		/// twenty minutes, each on the AI tick and each a spawn every observer is sent. A mechanic
		/// that fires with the boss at its cap calls in nothing, and tops the adds back up to the
		/// cap as they die.
		/// </para>
		/// <para>
		/// A phase's <see cref="BossPhase.SpawnOnEnter"/> adds always arrive: a phase is entered
		/// at most once per pull, so they are bounded by what was authored, and a phase that
		/// silently came up short of the adds it names would be a different encounter. They count
		/// towards the cap all the same, so the timed mechanics hold off while they live.
		/// </para>
		/// <para>
		/// Twelve because it is the most attackers <c>AICombatSlots</c> seats on one ring around a
		/// target, which makes it about the largest crowd a timed summon can mean: past it, adds
		/// stand on an outer ring waiting for a place. No shipped boss script exists yet (every
		/// catalogue entry's is empty), so no authored encounter changes; one written against the
		/// old behaviour reaches the cap only if its adds are left alive for twelve intervals.
		/// </para>
		/// </remarks>
		[Tooltip("Most of this boss's adds alive at once before timed mechanics stop calling more. Phase adds always arrive, and count towards it.")]
		[Min(1)]
		public int MaxLiveAdds = 12;
	}
}