using System.Collections.Generic;
using FishMMO.Shared.Core;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Reads a defender's block and deflect buffs and answers the two questions combat asks of
	/// them: how much of this hit is taken off, and does this object get turned away.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>One place, because the two halves must not drift.</b> Negation is applied by
	/// <c>CharacterDamageController.Damage</c> and deflection by <c>AbilityObject.ApplyHit</c> —
	/// different subsystems, different ticks in the frame — but both walk the same buff container,
	/// both need the same "is this coming at my front" test, and both have to spend charges the
	/// same way or a shield ends at a different moment depending on which one used it last.
	/// </para>
	/// <para>
	/// <b>Deterministic order, for free.</b> <c>IBuffController.Buffs</c> is a
	/// <see cref="SortedDictionary{TKey,TValue}"/> keyed by template id, so every peer walks a
	/// defender's shields in the same order without this file having to sort anything. That matters
	/// because two absorb shields spend in sequence, and which one empties first is observable.
	/// </para>
	/// <para>
	/// <b>Live positions, no rewind, deliberately.</b> The facing test asks where the DEFENDER was
	/// looking, and the defender is not the peer whose view is being reconstructed — a rewind
	/// reproduces what the ATTACKER saw. Rewinding the defender's own facing to the attacker's view
	/// would let a player be hit through a shield they had already turned towards the attacker, and
	/// there is nothing to gain: the shield is the defender's decision about the defender's own
	/// character, taken on the tick the server resolves the hit.
	/// </para>
	/// <para>
	/// <b>Mutation is gated by the caller, not here.</b> <see cref="Negate"/> and
	/// <see cref="TryDeflect"/> both take a <c>mutate</c> flag: a peer that does not simulate this
	/// defender's buffs may still want the NUMBER (so the caster's client can predict a blocked hit
	/// honestly) but must never spend a pool it does not own. See
	/// <c>IBuffController.SimulatesBuffEffects</c>.
	/// </para>
	/// </remarks>
	public static class DamageMitigation
	{
		/// <summary>
		/// Scratch list of buffs a mitigation pass emptied, so they can be removed after the walk.
		/// </summary>
		/// <remarks>
		/// Removing inside the loop would mutate the <see cref="SortedDictionary{TKey,TValue}"/>
		/// being enumerated, so the ids are collected first and released afterwards.
		/// </remarks>
		private static readonly List<int> spentBuffs = new List<int>(4);

		/// <summary>True while <see cref="spentBuffs"/> is lent to a pass still running.</summary>
		/// <remarks>
		/// <para>
		/// Removing a buff is not a leaf operation: <c>BuffController.Remove</c> runs the template's
		/// <c>OnRemove</c> and fires the buff-removed triggers, and authored content in those is
		/// free to deal damage — which re-enters <see cref="Negate"/> on some character and would
		/// clear the list the outer pass is still holding.
		/// </para>
		/// <para>
		/// The same shape <c>AbilityApplyAreaAction</c> uses for its hit list, and for the same
		/// reason: the outer call owns the shared list, any nested one allocates its own. The
		/// nested case is rare enough to be worth an allocation and common enough to be worth
		/// surviving.
		/// </para>
		/// </remarks>
		private static bool spentBuffsInUse;

		/// <summary>Borrows the shared spent-buff list, or a private one when it is already lent.</summary>
		private static List<int> BorrowSpentBuffs(out bool ownsShared)
		{
			ownsShared = !spentBuffsInUse;
			if (!ownsShared)
			{
				return new List<int>(2);
			}
			spentBuffsInUse = true;
			spentBuffs.Clear();
			return spentBuffs;
		}

		/// <summary>
		/// Damage left after every qualifying negation buff on <paramref name="defender"/> has taken
		/// its share.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Modes are applied in the order that makes each one mean what it says:
		/// <see cref="DamageNegationMode.Immune"/> first, because an immunity that ran after a
		/// shield would let the shield spend charges on damage that was never going to land;
		/// then <see cref="DamageNegationMode.Reduce"/>, so a percentage is taken off the full hit
		/// rather than off whatever a pool happened to leave; then
		/// <see cref="DamageNegationMode.Absorb"/> pools, which spend what actually remains.
		/// </para>
		/// <para>
		/// A pool that empties is REMOVED, which is the "disappears when the remaining damage
		/// negation amount hits 0" behaviour — and it is removed through
		/// <c>IBuffController.Remove</c> rather than by dropping the entry, so the strip, the FX and
		/// the observer push all hear about it exactly as they would for an expiry.
		/// </para>
		/// </remarks>
		/// <param name="defender">The character taking the hit. Null returns <paramref name="amount"/>.</param>
		/// <param name="attacker">Who is hitting them, for the facing test. Null skips facing-gated buffs.</param>
		/// <param name="amount">Damage remaining after resistances.</param>
		/// <param name="mutate">
		/// True to spend absorb pools and remove emptied buffs. False to answer the question without
		/// touching state — for a peer that does not simulate this defender.
		/// </param>
		/// <returns>The damage that survives, never below zero and never above <paramref name="amount"/>.</returns>
		public static int Negate(ICharacter defender, ICharacter attacker, int amount, bool mutate)
		{
			if (amount <= 0 || defender == null || !defender.TryGet(out IBuffController buffController))
			{
				return amount < 0 ? 0 : amount;
			}

			SortedDictionary<int, Buff> buffs = buffController.Buffs;
			if (buffs == null || buffs.Count == 0)
			{
				return amount;
			}

			Vector3 attackDirection = ResolveAttackDirection(defender, attacker);
			int remaining = amount;

			// Pass 1: immunity. Nothing else needs to run if the hit is gone.
			foreach (KeyValuePair<int, Buff> pair in buffs)
			{
				if (!Qualifies(pair.Value, defender, attackDirection, out DamageNegationBuffTemplate template) ||
					template.Mode != DamageNegationMode.Immune)
				{
					continue;
				}
				return 0;
			}

			// Pass 2: percentage reductions, each taken off the full incoming amount.
			int reduced = 0;
			foreach (KeyValuePair<int, Buff> pair in buffs)
			{
				if (!Qualifies(pair.Value, defender, attackDirection, out DamageNegationBuffTemplate template) ||
					template.Mode != DamageNegationMode.Reduce)
				{
					continue;
				}
				reduced += template.ResolveNegation(amount, pair.Value.RemainingCharges);
			}
			remaining -= reduced;
			if (remaining <= 0)
			{
				return 0;
			}

			// Pass 3: absorb pools, in template order, spending what is actually left.
			List<int> spent = BorrowSpentBuffs(out bool ownsShared);
			try
			{
				foreach (KeyValuePair<int, Buff> pair in buffs)
				{
					if (remaining <= 0)
					{
						break;
					}
					if (!Qualifies(pair.Value, defender, attackDirection, out DamageNegationBuffTemplate template) ||
						template.Mode != DamageNegationMode.Absorb)
					{
						continue;
					}

					int absorbed = template.ResolveNegation(remaining, pair.Value.RemainingCharges);
					if (absorbed <= 0)
					{
						continue;
					}

					remaining -= absorbed;
					if (!mutate)
					{
						continue;
					}

					pair.Value.SpendCharges(absorbed);
					/* The controller cannot see a spend that happened on the Buff instance, and an
					 * unmarked snapshot is served from cache — so the server would never report the
					 * shield it just drained. See IBuffController.MarkBuffStateDirty. */
					buffController.MarkBuffStateDirty();
					if (pair.Value.IsSpent)
					{
						spent.Add(pair.Key);
					}
				}

				/* INSIDE the borrow, not after it. RemoveSpent is the one call in this method that
				 * can re-enter: Remove runs the template's OnRemove and fires the buff-removed
				 * triggers, and authored content there is free to deal damage — which lands back in
				 * Negate on some character. Releasing the borrow first meant that nested pass took
				 * the SHARED list and cleared it while this loop was still walking it, so a second
				 * emptied shield was silently never removed; worse, a nested pass that refilled the
				 * list had its ids removed from THIS defender. */
				RemoveSpent(buffController, spent);
			}
			finally
			{
				if (ownsShared)
				{
					spentBuffsInUse = false;
				}
			}

			return remaining < 0 ? 0 : remaining;
		}

		/// <summary>
		/// True when <paramref name="defender"/> turns away an object arriving along
		/// <paramref name="incomingHeading"/>, and if so what heading it leaves on.
		/// </summary>
		/// <remarks>
		/// The FIRST qualifying buff in template order wins and is the one charged, so two
		/// overlapping guards do not both spend a charge on one projectile.
		/// </remarks>
		/// <param name="defender">The character the object is about to hit.</param>
		/// <param name="incomingHeading">The direction the object is travelling.</param>
		/// <param name="impactNormal">Surface normal the server measured at the impact.</param>
		/// <param name="mutate">True to spend a deflection charge and remove an emptied buff.</param>
		/// <param name="deflectedHeading">The heading to redirect along. Meaningless when this returns false.</param>
		/// <returns>True when the object was deflected and must not be treated as a hit.</returns>
		public static bool TryDeflect(ICharacter defender, Vector3 incomingHeading, Vector3 impactNormal,
			bool mutate, out Vector3 deflectedHeading)
		{
			deflectedHeading = incomingHeading;

			if (defender == null || !defender.TryGet(out IBuffController buffController))
			{
				return false;
			}

			SortedDictionary<int, Buff> buffs = buffController.Buffs;
			if (buffs == null || buffs.Count == 0)
			{
				return false;
			}

			/* Where the object is coming FROM, which is what the guard is held against. The heading
			 * points at the defender, so the arrival direction is its reverse. */
			Vector3 arrivalDirection = -incomingHeading;

			bool deflected = false;
			List<int> spent = BorrowSpentBuffs(out bool ownsShared);
			try
			{
				foreach (KeyValuePair<int, Buff> pair in buffs)
				{
					Buff buff = pair.Value;
					if (buff == null || !(buff.Template is DeflectBuffTemplate template))
					{
						continue;
					}
					if (template.MaxDeflections > 0 && buff.RemainingCharges <= 0)
					{
						continue;
					}
					if (!IsWithinGuard(defender, arrivalDirection, template.DeflectAngleDegrees))
					{
						continue;
					}

					deflectedHeading = DeflectBuffTemplate.ResolveDeflectedHeading(incomingHeading, impactNormal);
					deflected = true;

					if (mutate && template.MaxDeflections > 0)
					{
						buff.SpendCharges(1);
						buffController.MarkBuffStateDirty();
						if (buff.IsSpent)
						{
							spent.Add(pair.Key);
						}
					}

					break;
				}

				// Inside the borrow — see the note in Negate.
				RemoveSpent(buffController, spent);
			}
			finally
			{
				if (ownsShared)
				{
					spentBuffsInUse = false;
				}
			}

			return deflected;
		}

		/// <summary>
		/// True when an ability object striking <paramref name="localImpactPoint"/> met one of
		/// <paramref name="defender"/>'s raised shields rather than the defender.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>Interception, not mitigation.</b> A hit that lands inside a shield volume never reached
		/// the character, so there is no damage to reduce and the buff's
		/// <see cref="DamageNegationMode"/> does not enter into it. The caller stops the object
		/// outright.
		/// </para>
		/// <para>
		/// <b>Local space, and that is what makes it correct.</b> The point comes from
		/// <see cref="AbilitySweepHit.LocalPoint"/> or
		/// <see cref="LagCompensatedQuery.CompensatedHit.LocalPoint"/>, captured inside the rewind
		/// scope and expressed in the defender's own frame; the volume is authored in that same
		/// frame. Neither has to be transformed, so neither can be read out of the wrong world — the
		/// trap that a world-space volume compared against a rewound impact point falls straight
		/// into, since hits are dispatched only after the scope has closed.
		/// </para>
		/// <para>
		/// The FIRST shield in template order that covers the point wins and is the one charged, so
		/// two overlapping shields do not both pay for one projectile.
		/// </para>
		/// </remarks>
		/// <param name="defender">The character the object struck.</param>
		/// <param name="localImpactPoint">Impact point in the defender's local space.</param>
		/// <param name="mutate">True to spend the block cost and remove a shield it empties.</param>
		/// <returns>True when a shield took the hit and the object must not be treated as having landed.</returns>
		public static bool TryBlockAtVolume(ICharacter defender, Vector3 localImpactPoint, bool mutate)
		{
			if (defender == null || !defender.TryGet(out IBuffController buffController))
			{
				return false;
			}

			SortedDictionary<int, Buff> buffs = buffController.Buffs;
			if (buffs == null || buffs.Count == 0)
			{
				return false;
			}

			bool blocked = false;
			List<int> spent = BorrowSpentBuffs(out bool ownsShared);
			try
			{
				foreach (KeyValuePair<int, Buff> pair in buffs)
				{
					Buff buff = pair.Value;
					if (buff == null || !(buff.Template is DamageNegationBuffTemplate template))
					{
						continue;
					}

					ShieldVolume shield = template.Shield;
					if (shield == null || !shield.IsActive || !shield.Contains(localImpactPoint))
					{
						continue;
					}

					/* A shield whose pool cannot cover the block does not stop the object. Otherwise a
					 * barrier at one point of charge would turn away an unlimited number of hits for
					 * free, which is the opposite of what a cost is for. A cost of zero always
					 * passes, which is the channelled case. */
					if (template.VolumeBlockCost > 0 && buff.RemainingCharges < template.VolumeBlockCost)
					{
						continue;
					}

					blocked = true;

					if (mutate && template.VolumeBlockCost > 0)
					{
						buff.SpendCharges(template.VolumeBlockCost);
						buffController.MarkBuffStateDirty();
						if (buff.IsSpent)
						{
							spent.Add(pair.Key);
						}
					}

					break;
				}

				// Inside the borrow — see the note in Negate.
				RemoveSpent(buffController, spent);
			}
			finally
			{
				if (ownsShared)
				{
					spentBuffsInUse = false;
				}
			}

			return blocked;
		}

		/// <summary>
		/// Every shield volume <paramref name="defender"/> currently has raised.
		/// </summary>
		/// <remarks>
		/// For the outward-looking half — <c>ShieldInterceptAction</c> sweeps these for objects in
		/// flight. Appended rather than returned, so the caller owns the list and a per-tick query
		/// allocates nothing.
		/// </remarks>
		/// <param name="defender">The character whose shields are wanted.</param>
		/// <param name="into">Receives the active volumes. Not cleared first.</param>
		public static void CollectShieldVolumes(ICharacter defender, List<ShieldVolume> into)
		{
			if (into == null || defender == null || !defender.TryGet(out IBuffController buffController))
			{
				return;
			}

			SortedDictionary<int, Buff> buffs = buffController.Buffs;
			if (buffs == null)
			{
				return;
			}

			foreach (KeyValuePair<int, Buff> pair in buffs)
			{
				ShieldVolume shield = (pair.Value?.Template as DamageNegationBuffTemplate)?.Shield;
				if (shield != null && shield.IsActive)
				{
					into.Add(shield);
				}
			}
		}

		// ── Helpers ──────────────────────────────────────────────────────────────────

		/// <summary>
		/// True when <paramref name="buff"/> is a negation buff that applies to a hit arriving along
		/// <paramref name="attackDirection"/>.
		/// </summary>
		/// <remarks>
		/// A facing-gated buff with no resolvable attacker direction does NOT apply. That is the
		/// conservative direction: environmental damage, damage-over-time and anything else with no
		/// position behind it is exactly what a shield held in one direction should not stop.
		/// </remarks>
		private static bool Qualifies(Buff buff, ICharacter defender, Vector3 attackDirection,
			out DamageNegationBuffTemplate template)
		{
			template = buff?.Template as DamageNegationBuffTemplate;
			if (template == null)
			{
				return false;
			}
			if (!template.RequiresFacing)
			{
				return true;
			}
			return attackDirection.sqrMagnitude > 1e-8f &&
				IsWithinGuard(defender, attackDirection, template.FacingAngleDegrees);
		}

		/// <summary>
		/// True when <paramref name="direction"/> lies inside a cone of
		/// <paramref name="totalAngleDegrees"/> opening along the defender's forward.
		/// </summary>
		/// <remarks>
		/// Routed through <see cref="TargetOrdering.IsWithinCone"/> so "in front" means the same
		/// thing here as it does to <c>ConeTargetSelector</c> and every facing condition — including
		/// its rule that a zero-length direction is inside no cone at all.
		/// </remarks>
		private static bool IsWithinGuard(ICharacter defender, Vector3 direction, float totalAngleDegrees)
		{
			Transform transform = defender?.Transform;
			if (transform == null)
			{
				return false;
			}
			if (totalAngleDegrees >= 360f)
			{
				return direction.sqrMagnitude > 1e-8f;
			}
			return TargetOrdering.IsWithinCone(Vector3.zero, transform.forward, direction, totalAngleDegrees);
		}

		/// <summary>
		/// The direction a hit from <paramref name="attacker"/> arrives from, or zero when there is
		/// no attacker with a position.
		/// </summary>
		private static Vector3 ResolveAttackDirection(ICharacter defender, ICharacter attacker)
		{
			Transform defenderTransform = defender?.Transform;
			Transform attackerTransform = attacker?.Transform;
			if (defenderTransform == null || attackerTransform == null)
			{
				return Vector3.zero;
			}
			return attackerTransform.position - defenderTransform.position;
		}

		/// <summary>
		/// Removes every buff the walk emptied, after the walk over <c>Buffs</c> has finished —
		/// and while the shared spent list is STILL BORROWED.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Ordered by the ids the walk collected, which is template order, so two shields that empty
		/// on one hit are removed in the same sequence on every peer.
		/// </para>
		/// <para>
		/// The borrow ordering is load-bearing: buff removal fires buff-removed triggers, which can
		/// re-enter <c>Negate</c>. Releasing the shared list before calling this handed the
		/// re-entrant pass the very list this method was iterating — <c>BorrowSpentBuffs</c> clears
		/// it — so a second emptied shield was stranded at zero charges. Every caller now releases
		/// the borrow only AFTER this returns; the re-entrant pass sees the borrow held and
		/// allocates privately.
		/// </para>
		/// </remarks>
		private static void RemoveSpent(IBuffController buffController, List<int> spent)
		{
			if (spent == null || spent.Count == 0)
			{
				return;
			}
			for (int i = 0; i < spent.Count; ++i)
			{
				buffController.Remove(spent[i]);
			}
			spent.Clear();
		}
	}
}
