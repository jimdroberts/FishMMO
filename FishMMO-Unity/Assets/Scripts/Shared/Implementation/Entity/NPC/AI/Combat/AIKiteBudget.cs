using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Limits how long an NPC may keep backing away from its target before it has to stand and fight.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A kiting archetype backs away whenever the target is inside its comfort distance. Left
	/// unbounded, and moving at the same run speed as the player, that is a fight nobody can
	/// close: the caster retreats every tick the player advances and a melee character never
	/// lands a hit. The budget drains while the NPC kites; once spent, the planner ignores the
	/// comfort band for <c>RecoverySeconds</c> and the NPC holds its ground, then the budget
	/// refills. Standing still refunds it slowly so a caster that is left alone gets its kite
	/// back.
	/// </para>
	/// <para>
	/// One per NPC (on <c>AIController</c>), never on the shared state asset.
	/// </para>
	/// </remarks>
	public struct AIKiteBudget
	{
		/// <summary>Fraction of elapsed time refunded while not kiting.</summary>
		public const float REFUND_RATE = 0.5f;

		/// <summary>Seconds of kiting left before the budget is spent.</summary>
		public float Remaining;

		/// <summary>Seconds left in the recovery hold, once spent.</summary>
		public float RecoveryTimer;

		/// <summary>True while the budget is spent and the NPC must hold its ground.</summary>
		public bool Exhausted;

		/// <summary>True once <see cref="Reset"/> has primed the budget for a fight.</summary>
		private bool primed;

		/// <summary>Primes the budget for a new fight.</summary>
		/// <param name="budgetSeconds">Seconds of kiting allowed before the hold.</param>
		public void Reset(float budgetSeconds)
		{
			Remaining = Mathf.Max(0f, budgetSeconds);
			RecoveryTimer = 0f;
			Exhausted = false;
			primed = true;
		}

		/// <summary>Clears everything, for pooling.</summary>
		public void Clear()
		{
			Remaining = 0f;
			RecoveryTimer = 0f;
			Exhausted = false;
			primed = false;
		}

		/// <summary>
		/// Advances the budget by one combat update.
		/// </summary>
		/// <param name="kiting">True if the NPC backed away during this update.</param>
		/// <param name="deltaTime">Seconds elapsed since the previous combat update.</param>
		/// <param name="budgetSeconds">Seconds of kiting allowed per window. 0 disables the budget.</param>
		/// <param name="recoverySeconds">Seconds the NPC must hold its ground once spent.</param>
		public void Tick(bool kiting, float deltaTime, float budgetSeconds, float recoverySeconds)
		{
			if (budgetSeconds <= 0f)
			{
				Exhausted = false;
				return;
			}

			if (!primed)
			{
				Reset(budgetSeconds);
			}

			if (deltaTime < 0f)
			{
				deltaTime = 0f;
			}

			if (Exhausted)
			{
				RecoveryTimer -= deltaTime;
				if (RecoveryTimer <= 0f)
				{
					Reset(budgetSeconds);
				}
				return;
			}

			if (kiting)
			{
				Remaining -= deltaTime;
				if (Remaining <= 0f)
				{
					Remaining = 0f;
					Exhausted = true;
					RecoveryTimer = Mathf.Max(0f, recoverySeconds);
				}
				return;
			}

			Remaining = Mathf.Min(budgetSeconds, Remaining + deltaTime * REFUND_RATE);
		}
	}
}
