using FishMMO.Shared.Core;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// What a held guard (a <see cref="AbilityType.Block"/> ability, <see cref="AbilityTemplate.Guard"/>) does to a hit:
	/// takes its share off a blow from in front and charges for it, or breaks.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Read off the cast, not a buff.</b> The guard is up exactly while the defender's Block cast is
	/// (<see cref="AbilityController.TryGetActiveGuard"/>), and the cast is reconciled state, so there is no buff to apply
	/// on the hold and remove on the release — nothing for a replay to apply twice or a missed release to leave behind.
	/// </para>
	/// <para>
	/// <b>In front, by position.</b> The attacker's position against where the defender faces, through the same cone test
	/// every other "in front" uses (<see cref="TargetOrdering.IsWithinCone"/>), live and unrewound for the reason
	/// <c>BuffDamageMitigation</c> gives: the guard is the defender's own decision. No attacker, no block.
	/// </para>
	/// <para>
	/// <b>The cost, and the break.</b> Each blocked hit spends <see cref="AbilityGuard.ResolveCost"/> of the guard's resource.
	/// When the pool cannot pay, the hit lands in full and the guard breaks: the cast is interrupted (the server's
	/// interrupt, carried to the owner by the reconcile) and its cooldown starts. Spending and breaking happen only where
	/// <paramref name="mutate"/> says this peer owns the defender's state; anywhere else the number is answered and nothing
	/// is touched.
	/// </para>
	/// </remarks>
	public static class HeldGuardMitigation
	{
		/// <summary>Installs this as the core's guard hook (<see cref="DamageMitigation.GuardNegator"/>).</summary>
		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
#if UNITY_EDITOR
		[UnityEditor.InitializeOnLoadMethod]
#endif
		private static void Install()
		{
			DamageMitigation.GuardNegator = Guard;
		}

		/// <summary>The damage left after <paramref name="defender"/>'s held guard, if any, has taken its share.</summary>
		public static int Guard(ICharacter defender, ICharacter attacker, int amount, bool mutate)
		{
			if (amount <= 0 || defender == null || attacker == null || ReferenceEquals(defender, attacker) ||
				!defender.TryGet(out IAbilityController abilities) || !(abilities is AbilityController controller) ||
				!controller.TryGetActiveGuard(out AbilityGuard guard))
			{
				return amount;
			}

			Transform defenderTransform = defender.Transform;
			Transform attackerTransform = attacker.Transform;
			if (defenderTransform == null || attackerTransform == null ||
				!IsInFront(defenderTransform.forward, attackerTransform.position - defenderTransform.position, guard.ArcDegrees))
			{
				return amount;
			}

			int blocked = AbilityGuard.ResolveBlocked(amount, guard.ReducePercent);
			if (blocked <= 0)
			{
				return amount;
			}

			int cost = guard.ResolveCost(blocked);
			if (guard.CostResource != null && cost > 0)
			{
				if (!defender.TryGet(out ICharacterAttributeController attributes) ||
					!attributes.TryGetResourceAttribute(guard.CostResource, out CharacterResourceAttribute pool))
				{
					return amount;
				}
				if (pool.CurrentValue < cost)
				{
					// Too tired to hold it: the blow lands in full and knocks the guard down.
					if (mutate)
					{
						controller.Interrupt(attacker);
					}
					return amount;
				}
				if (mutate)
				{
					pool.Consume(cost);
				}
			}
			return amount - blocked;
		}

		/// <summary>
		/// True when a blow arriving from <paramref name="toAttacker"/> (defender to attacker) is inside a guard of
		/// <paramref name="arcDegrees"/> held along <paramref name="forward"/>. Flat: height does not matter.
		/// </summary>
		public static bool IsInFront(Vector3 forward, Vector3 toAttacker, float arcDegrees)
		{
			forward.y = 0f;
			toAttacker.y = 0f;
			if (toAttacker.sqrMagnitude < 1e-8f || forward.sqrMagnitude < 1e-8f)
			{
				return false;
			}
			if (arcDegrees >= 360f)
			{
				return true;
			}
			return TargetOrdering.IsWithinCone(Vector3.zero, forward.normalized, toAttacker, arcDegrees);
		}
	}
}
