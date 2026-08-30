using System.Collections.Generic;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Abstract base class for all AI states. Defines common parameters and logic for state transitions, leash checks, enemy detection, and line of sight.
	/// </summary>
	public abstract class BaseAIState : CachedScriptableObject<BaseAIState>, ICachedObject
	{
		[SerializeField]
		private float updateRate = 1.0f;

		/// <summary>
		/// The rate (in seconds) at which the AI will check if it should leash and return home. If 0 or below, leashing is disabled.
		/// </summary>
		[Tooltip("The rate at which the AI will check if it should leash and return home. If 0 or below it will never leash.")]
		public float LeashUpdateRate = 5.0f;

		/// <summary>
		/// Minimum distance at which the AI will forget the current target and return home.
		/// </summary>
		public float MinLeashRange = 50.0f;

		/// <summary>
		/// Maximum distance at which the AI will forget the current target and teleport home.
		/// </summary>
		public float MaxLeashRange = 100.0f;

		/// <summary>
		/// The radius within which the AI will detect enemies.
		/// </summary>
		public float DetectionRadius = 10;

		/// <summary>
		/// Bodies already accepted by the current <see cref="SweepForEnemies"/> pass.
		/// </summary>
		/// <remarks>
		/// Static and reused: the sweep is fully consumed inside one synchronous call, and AI runs
		/// on the server's single thread. States are <c>ScriptableObject</c> assets shared by every
		/// NPC that references them, so a per-instance field here would be shared anyway — cleared
		/// on entry is what makes it safe either way.
		/// </remarks>
		private static readonly List<GameObject> sweepKeys = new List<GameObject>(16);

		/// <summary>
		/// The layers considered as enemies for detection.
		/// </summary>
		public LayerMask EnemyLayers;

		/// <summary>
		/// The layers that block line of sight checks.
		/// </summary>
		public LayerMask LineOfSightBlockingLayers;

		[Header("Combat Continuity")]
		/// <summary>
		/// True when this state is a <em>combat sub-state</em>: something the NPC steps into
		/// while still fighting, such as orbiting, flanking or kiting.
		/// </summary>
		/// <remarks>
		/// <see cref="BaseAttackingState.Exit"/> normally clears the combat target and interrupts
		/// the cast, which is correct when the NPC disengages. Transitioning into a positioning
		/// state is not a disengage, and clearing the target on the way in left the sub-state with
		/// nothing to orbit — it immediately bailed to idle. Set this on orbit / flank / strafe /
		/// retreat assets so the target survives the transition.
		/// </remarks>
		[Tooltip("This state continues the current fight (orbit/flank/kite). Keeps the combat target when entered from an attacking state.")]
		public bool KeepsCombatTarget;

		/// <summary>
		/// Returns the update rate for this state (in seconds).
		/// </summary>
		public virtual float GetUpdateRate() { return updateRate; }

		/// <summary>
		/// Returns the update rate for this state, given the NPC that is running it.
		/// </summary>
		/// <remarks>
		/// States that randomise their tick interval need the owning NPC's seeded RNG to stay
		/// deterministic. <see cref="IdleState"/> and <see cref="WanderState"/> previously reached
		/// for <see cref="DeterministicRNG.Shared"/> here, which made their timing depend on how
		/// many other NPCs had drawn from the shared stream first.
		/// </remarks>
		/// <param name="controller">The AI controller running this state.</param>
		/// <returns>Update rate in seconds.</returns>
		public virtual float GetUpdateRate(AIController controller) { return GetUpdateRate(); }

		/// <summary>
		/// Returns a value between <paramref name="minimum"/> and <paramref name="maximum"/> using
		/// the NPC's seeded RNG, or <paramref name="minimum"/> when the range is degenerate.
		/// </summary>
		/// <param name="controller">The AI controller running this state.</param>
		/// <param name="minimum">Lower bound.</param>
		/// <param name="maximum">Upper bound. Values at or below the lower bound return the lower bound.</param>
		/// <returns>The randomised rate.</returns>
		protected static float RandomizeRate(AIController controller, float minimum, float maximum)
		{
			if (maximum <= minimum)
			{
				return minimum;
			}
			DeterministicRNG rng = controller != null ? controller.NpcRNG : null;
			return (rng ?? DeterministicRNG.Shared).Range(minimum, maximum);
		}

		/// <summary>
		/// Called when the state is entered. Implement state-specific logic here.
		/// </summary>
		/// <param name="controller">The AI controller managing this NPC.</param>
		public abstract void Enter(AIController controller);

		/// <summary>
		/// Called when the state is exited. Implement cleanup logic here.
		/// </summary>
		/// <param name="controller">The AI controller managing this NPC.</param>
		public abstract void Exit(AIController controller);

		/// <summary>
		/// Called every frame while in this state. Implement state update logic here.
		/// </summary>
		/// <param name="controller">The AI controller managing this NPC.</param>
		/// <param name="deltaTime">Time since last update.</param>
		public abstract void UpdateState(AIController controller, float deltaTime);

		/// <summary>
		/// Returns the NPC to its attacking state when it still has a live target, or to idle
		/// when it does not.
		/// </summary>
		/// <remarks>
		/// Combat sub-states (orbit, flank) used to hard-code a transition to idle when their
		/// manoeuvre completed, which dropped the NPC out of a fight it was still winning.
		/// </remarks>
		/// <param name="controller">The AI controller managing this NPC.</param>
		protected static void ReturnToCombatOrIdle(AIController controller)
		{
			if (controller.Target != null && controller.AttackingState != null)
			{
				controller.ChangeState(controller.AttackingState);
				return;
			}
			controller.TransitionToIdleState();
		}

		/// <summary>
		/// Checks if the AI has line of sight to the target. Uses raycasting to determine if any objects block the view.
		/// </summary>
		/// <param name="controller">The AI controller managing this NPC.</param>
		/// <param name="target">The target character to check line of sight to.</param>
		/// <returns>True if line of sight exists, false otherwise.</returns>
		public virtual bool HasLineOfSight(AIController controller, ICharacter target)
		{
			if (target == null || controller.Character == null)
			{
				return false;
			}

			// Use the eye transform for ray origin if available, otherwise use character position.
			Vector3 rayOrigin = controller.EyeTransform.position;

			// Use the center of the target's collider if available for more accurate targeting.
			Vector3 targetPoint = target.Transform.position;
			if (target != null && target.Collider != null)
			{
				targetPoint = target.Collider.bounds.center;
			}

			Vector3 direction = (targetPoint - rayOrigin).normalized;
			float distance = Vector3.Distance(rayOrigin, targetPoint);

			RaycastHit hit;
			// Offset the ray origin slightly to avoid self-intersection with the AI's own collider.
			float colliderRadius = 0.1f;

			// Raycast to check if anything blocks line of sight before reaching the target.
			if (controller.PhysicsScene.Raycast(rayOrigin + direction * colliderRadius, direction, out hit, distance - colliderRadius, LineOfSightBlockingLayers))
			{
				// If the raycast hit something, check if it was the target itself.
				// If it hit something else before the target, line of sight is blocked.
				return hit.transform == target.Transform || hit.collider == target?.Collider;
			}
			return true;
		}

		/// <summary>
		/// Sweeps for nearby enemies using physics overlap checks and faction logic.
		/// Populates the provided list with detected enemies. Returns true if any enemies are detected with line of sight.
		/// </summary>
		/// <param name="controller">The AI controller managing this NPC.</param>
		/// <param name="detectedEnemies">Reusable list to populate with detected enemy characters. Cleared before use.</param>
		/// <returns>True if any enemies are detected, false otherwise.</returns>
		public virtual bool SweepForEnemies(AIController controller, List<ICharacter> detectedEnemies)
		{
			detectedEnemies.Clear();

			if (controller.Character == null ||
				controller.AttackingState == null ||
				!controller.Character.TryGet(out IFactionController ourFactionController) ||
				controller.Observers.Count < 1)
			{
				return false;
			}

			// Only sweep for enemies if alive.
			if (controller.Character.TryGet(out ICharacterDamageController damageController) &&
				!damageController.IsAlive)
			{
				return false;
			}

			/* Re-queried until the buffer stops coming back full. A non-allocating overlap returns
			 * at most buffer.Length results and says nothing about how many it discarded, and the
			 * broadphase chose which ones — so an NPC in a fight bigger than its buffer aggroed an
			 * arbitrary subset of its attackers and simply never noticed the rest, differently on
			 * each run. */
			int overlapCount;
			while (true)
			{
				overlapCount = controller.PhysicsScene.OverlapSphere(
						controller.Character.Transform.position,
						DetectionRadius,
						controller.SweepHits,
						EnemyLayers,
						QueryTriggerInteraction.Ignore);

				if (!TargetOrdering.TryGrowQueryBuffer(ref controller.SweepHits, overlapCount))
				{
					break;
				}
			}

			/* Resolved through TargetOrdering, not with a bare GetComponent on the collider.
			 *
			 * The collider a query returns is frequently not the body anything cares about: a
			 * character is free to hang its hitbox off a child transform, and GetComponent on that
			 * child finds no ICharacter — so such a character was INVISIBLE to every NPC's enemy
			 * sweep, while remaining perfectly able to attack them. The same walk also collapses a
			 * character rigged with several colliders, which would otherwise be added to
			 * detectedEnemies once per collider and weighted that many times by whatever picks a
			 * target from the list. Same resolver the hit-resolving paths use. */
			sweepKeys.Clear();
			for (int i = 0; i < overlapCount && i < controller.SweepHits.Length; ++i)
			{
				Collider hitCollider = controller.SweepHits[i];
				if (hitCollider == null)
				{
					continue;
				}

				GameObject key = TargetOrdering.ResolveHitKey(hitCollider, out ICharacter def);
				// Ignore ourselves, and anything that is not a character.
				if (key == null || def == null || def == controller.Character)
				{
					continue;
				}

				// One body, one entry, however many colliders it owns.
				if (TargetOrdering.ContainsBody(sweepKeys, key))
				{
					continue;
				}
				sweepKeys.Add(key);

				// Check faction alliance and only add enemies.
				if (def.TryGet(out IFactionController defenderFactionController) &&
					defenderFactionController.GetAllianceLevel(ourFactionController) == FactionAllianceLevel.Enemy)
				{
					bool lineOfSight = HasLineOfSight(controller, def);

#if UNITY_EDITOR
					Log.Debug("BaseAIState", $"{controller.gameObject.name} Enemy Detected: {def.GameObject.name} | Line of Sight: {lineOfSight}");
#endif

					if (lineOfSight)
					{
						detectedEnemies.Add(def);
					}
				}
			}
			return detectedEnemies.Count > 0;
		}
	}
}