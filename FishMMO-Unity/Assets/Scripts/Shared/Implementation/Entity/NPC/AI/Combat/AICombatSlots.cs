using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Hands each attacker its own standing spot in a ring around a shared target, so a pack
	/// surrounds its victim instead of piling onto one point.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why the NavMeshAgent's own avoidance is not enough.</b> Unity's local avoidance is very
	/// good at what it does: two agents crossing paths will slide around each other rather than
	/// interpenetrate. But it is a <em>local</em> solver, and it is only ever given one problem to
	/// solve — "do not overlap right now". It has no opinion about destinations. When five
	/// attackers are all told to path to the same point, avoidance faithfully keeps them from
	/// overlapping while they all continue pushing toward that point, which produces the familiar
	/// shoving, orbiting scrum: agents jitter against each other, get shunted out of attack range,
	/// path back in, and repeat. No amount of tuning agent radius or avoidance quality fixes it,
	/// because the destinations themselves are in conflict.
	/// </para>
	/// <para>
	/// The fix is to stop asking for the same point. Each attacker claims an angular slot around
	/// its target and paths to <em>that</em>, so the destinations are already separated by more
	/// than an agent diameter and avoidance is back to handling the incidental crossings it is
	/// good at.
	/// </para>
	/// <para>
	/// Ring capacity is derived from geometry rather than configured: the number of agents of a
	/// given radius that physically fit on a circle of a given radius. Attackers beyond that
	/// capacity are placed on an outer ring rather than being squeezed in, which is what produces
	/// the natural "front rank and back rank" look when a large pack engages.
	/// </para>
	/// <para>
	/// Server-only state, keyed by character ID. Entries are released on disengage and pruned when
	/// a target's ring empties.
	/// </para>
	/// </remarks>
	public static class AICombatSlots
	{
		/// <summary>
		/// Attackers currently engaging each target, in claim order. Position in the list is the
		/// slot index.
		/// </summary>
		private static readonly Dictionary<long, List<long>> ringsByTarget = new Dictionary<long, List<long>>();

		/// <summary>
		/// Reverse lookup so an attacker can release its slot without knowing its target.
		/// </summary>
		private static readonly Dictionary<long, long> targetByAttacker = new Dictionary<long, long>();

		/// <summary>
		/// Minimum multiple of an agent's diameter between neighbouring slots.
		/// </summary>
		/// <remarks>
		/// Slightly more than touching. At exactly one diameter the agents are in constant contact
		/// and avoidance never settles.
		/// </remarks>
		private const float SLOT_SPACING = 1.35f;

		/// <summary>
		/// Most attackers allowed on a single ring, whatever the geometry suggests.
		/// </summary>
		private const int MAX_RING_CAPACITY = 12;

		/// <summary>
		/// Claims (or refreshes) this attacker's slot in the ring around a target.
		/// </summary>
		/// <param name="targetID">The character being attacked.</param>
		/// <param name="attackerID">The attacking character.</param>
		/// <param name="slot">The attacker's index within its ring.</param>
		/// <param name="ring">Which ring the attacker is on. 0 is the innermost.</param>
		/// <param name="ringCapacity">How many attackers that ring holds.</param>
		/// <param name="combatRadius">Desired distance from the target for the innermost ring.</param>
		/// <param name="agentRadius">The attacker's own agent radius.</param>
		public static void Claim(long targetID, long attackerID, float combatRadius, float agentRadius,
			out int slot, out int ring, out int ringCapacity)
		{
			slot = 0;
			ring = 0;
			ringCapacity = 1;

			if (targetID == 0 || attackerID == 0)
			{
				return;
			}

			// Moved to a different target: give up the old slot so it can be reused.
			if (targetByAttacker.TryGetValue(attackerID, out long previousTarget) && previousTarget != targetID)
			{
				ReleaseInternal(previousTarget, attackerID);
			}

			if (!ringsByTarget.TryGetValue(targetID, out List<long> occupants))
			{
				occupants = new List<long>(8);
				ringsByTarget[targetID] = occupants;
			}

			int index = occupants.IndexOf(attackerID);
			if (index < 0)
			{
				index = occupants.Count;
				occupants.Add(attackerID);
			}

			targetByAttacker[attackerID] = targetID;

			ringCapacity = GetRingCapacity(combatRadius, agentRadius);
			ring = index / ringCapacity;
			slot = index % ringCapacity;
		}

		/// <summary>
		/// Releases an attacker's slot.
		/// </summary>
		/// <param name="attackerID">The attacker giving up its slot.</param>
		public static void Release(long attackerID)
		{
			if (attackerID == 0)
			{
				return;
			}

			if (targetByAttacker.TryGetValue(attackerID, out long targetID))
			{
				ReleaseInternal(targetID, attackerID);
			}
		}

		/// <summary>
		/// Removes every slot around a target. Called when the target dies or despawns.
		/// </summary>
		/// <param name="targetID">The target whose ring should be cleared.</param>
		public static void ReleaseTarget(long targetID)
		{
			if (!ringsByTarget.TryGetValue(targetID, out List<long> occupants))
			{
				return;
			}

			for (int i = 0; i < occupants.Count; ++i)
			{
				targetByAttacker.Remove(occupants[i]);
			}

			ringsByTarget.Remove(targetID);
		}

		/// <summary>
		/// Drops all slot state. Call on scene teardown.
		/// </summary>
		public static void Clear()
		{
			ringsByTarget.Clear();
			targetByAttacker.Clear();
		}

		/// <summary>
		/// Number of attackers currently engaging a target.
		/// </summary>
		/// <param name="targetID">The target to query.</param>
		/// <returns>The attacker count.</returns>
		public static int GetAttackerCount(long targetID)
		{
			return ringsByTarget.TryGetValue(targetID, out List<long> occupants) ? occupants.Count : 0;
		}

		/// <summary>
		/// Computes the world position of a slot around a target.
		/// </summary>
		/// <param name="targetPosition">The target's position.</param>
		/// <param name="slot">Slot index within the ring.</param>
		/// <param name="ring">Ring index. 0 is innermost.</param>
		/// <param name="ringCapacity">Slots on the ring.</param>
		/// <param name="combatRadius">Distance from the target for the innermost ring.</param>
		/// <param name="agentRadius">The attacker's agent radius, used to space outer rings.</param>
		/// <returns>The world position this attacker should stand at.</returns>
		public static Vector3 GetSlotPosition(Vector3 targetPosition, int slot, int ring, int ringCapacity,
			float combatRadius, float agentRadius)
		{
			if (ringCapacity < 1)
			{
				ringCapacity = 1;
			}

			float radius = combatRadius + (ring * agentRadius * 2f * SLOT_SPACING);

			/* Offset alternate rings by half a slot so an outer attacker stands in the gap between
			 * two inner ones rather than directly behind one, which would leave it with no line to
			 * the target and permanently blocked. */
			float halfStep = (ring % 2 == 1) ? (Mathf.PI / ringCapacity) : 0f;
			float angle = ((Mathf.PI * 2f) / ringCapacity) * slot + halfStep;

			return targetPosition + new Vector3(Mathf.Cos(angle) * radius, 0f, Mathf.Sin(angle) * radius);
		}

		/// <summary>
		/// How many agents of a given radius physically fit on a ring of a given radius.
		/// </summary>
		/// <remarks>
		/// Circumference divided by the space one agent needs, floored to at least one so a very
		/// tight melee radius still produces a usable ring rather than a division by zero.
		/// </remarks>
		/// <param name="combatRadius">Ring radius.</param>
		/// <param name="agentRadius">Attacker agent radius.</param>
		/// <returns>Slot count for the ring.</returns>
		public static int GetRingCapacity(float combatRadius, float agentRadius)
		{
			float effectiveAgentRadius = Mathf.Max(agentRadius, 0.1f);
			float effectiveRingRadius = Mathf.Max(combatRadius, effectiveAgentRadius);

			float circumference = 2f * Mathf.PI * effectiveRingRadius;
			float perAgent = effectiveAgentRadius * 2f * SLOT_SPACING;

			int capacity = Mathf.FloorToInt(circumference / perAgent);
			return Mathf.Clamp(capacity, 1, MAX_RING_CAPACITY);
		}

		/// <summary>
		/// Removes one attacker from one target's ring, pruning the ring when it empties.
		/// </summary>
		/// <param name="targetID">The target.</param>
		/// <param name="attackerID">The attacker.</param>
		private static void ReleaseInternal(long targetID, long attackerID)
		{
			targetByAttacker.Remove(attackerID);

			if (!ringsByTarget.TryGetValue(targetID, out List<long> occupants))
			{
				return;
			}

			/* Remove rather than blank the entry. Slots are positional, so removing compacts the
			 * ring and everyone behind shifts inward — which is the behaviour you want when a mob
			 * in the front rank dies: the back rank closes up instead of leaving a hole. */
			occupants.Remove(attackerID);

			if (occupants.Count == 0)
			{
				ringsByTarget.Remove(targetID);
			}
		}
	}
}
