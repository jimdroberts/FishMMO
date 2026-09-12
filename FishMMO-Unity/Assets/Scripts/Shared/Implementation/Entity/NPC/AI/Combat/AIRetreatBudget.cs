using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// The memory a retreating NPC needs to stop retreating: how long it has been running, how many
	/// times it has run during this fight, and whether it has committed to fighting instead.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why memory is the whole point.</b> Fleeing used to be decided from the current tick's
	/// distance and health alone, so the same inputs produced the same answer forever and a pursued
	/// NPC ran until it hit the leash. Every field here exists so a later tick can tell it is in the
	/// same situation as an earlier one and reach a different answer.
	/// </para>
	/// <para>
	/// <b>One per NPC (on <c>AIController</c>), never on the shared state asset.</b> A retreat state
	/// asset is referenced by every NPC that flees, so a seconds-retreating counter stored on it
	/// would be one counter shared by the whole server — one NPC's chase spending another's budget,
	/// and the same mistake makes a template's authored data unreadable as authored data. This
	/// mirrors <see cref="AIKiteBudget"/> exactly, which solves the identical "the NPC never stands
	/// and fights" problem for backing away.
	/// </para>
	/// </remarks>
	public struct AIRetreatBudget
	{
		/// <summary>
		/// Fraction of elapsed time refunded while not retreating.
		/// </summary>
		/// <remarks>
		/// Slower than the kite budget's half. Kiting is a positioning choice made every few seconds
		/// and should come back readily; running for your life is a decision an NPC should have to
		/// earn again, so the budget refills at a rate that makes a long fight with several retreats
		/// progressively harder to flee from — which is what ends the loop.
		/// </remarks>
		public const float REFUND_RATE = 0.25f;

		/// <summary>Seconds of retreating left in the cumulative window.</summary>
		public float Remaining;

		/// <summary>Seconds left of the hold imposed once the cumulative cap was spent.</summary>
		public float RecoveryTimer;

		/// <summary>Seconds left of the hold imposed when the NPC chose to turn and fight.</summary>
		public float ReengageHold;

		/// <summary>Retreats taken during the current fight.</summary>
		/// <remarks>
		/// Deliberately <em>not</em> cleared by <see cref="Reset"/>: it counts episodes within one
		/// fight, and <see cref="Reset"/> runs whenever the budget refills mid-fight. It is cleared
		/// only by <see cref="Clear"/>, which is a genuine end to combat. Clearing it on a lost
		/// target instead would mean it never accumulated at all — every retreat drops the target on
		/// the way out — and the ramp driven by it would be dead code.
		/// </remarks>
		public int ConsecutiveRetreats;

		/// <summary>Seconds until the next re-engagement roll is allowed.</summary>
		public float DecisionTimer;

		/// <summary>
		/// True while the NPC must not flee.
		/// </summary>
		/// <remarks>
		/// <b>Derived, never assigned.</b> Two independent things can refuse a retreat — the
		/// cumulative cap for this fight being spent, and a hold imposed because the NPC chose to
		/// turn and fight — and they overlap and expire on different clocks. A hand-maintained flag
		/// had to be set by both, cleared by whichever expired first, and re-derived on the way out;
		/// reading it off the two timers instead means it cannot disagree with them.
		/// </remarks>
		public bool Exhausted => RecoveryTimer > 0f || ReengageHold > 0f;

		/// <summary>True once <see cref="Reset"/> has primed the budget for a fight.</summary>
		private bool primed;

		/// <summary>The cap the window was primed with, remembered so the refund can clamp to it.</summary>
		private float windowSeconds;

		/// <summary>Primes the budget for a new fight.</summary>
		/// <param name="budgetSeconds">Seconds of retreating allowed before the hold.</param>
		public void Reset(float budgetSeconds)
		{
			windowSeconds = Mathf.Max(0f, budgetSeconds);
			Remaining = windowSeconds;
			RecoveryTimer = 0f;
			primed = true;
		}

		/// <summary>Clears everything, for pooling and for a genuine end to combat.</summary>
		public void Clear()
		{
			Remaining = 0f;
			RecoveryTimer = 0f;
			ReengageHold = 0f;
			DecisionTimer = 0f;
			ConsecutiveRetreats = 0;
			primed = false;
			windowSeconds = 0f;
		}

		/// <summary>
		/// Records that a retreat episode has begun.
		/// </summary>
		/// <param name="checkIntervalSeconds">Seconds before the first re-engagement roll.</param>
		public void NoteRetreatStart(float checkIntervalSeconds)
		{
			ConsecutiveRetreats++;
			DecisionTimer = Mathf.Max(0f, checkIntervalSeconds);
		}

		/// <summary>
		/// Records that the NPC chose to stop running and fight.
		/// </summary>
		/// <remarks>
		/// The hold is what makes the choice stick. Without it the attacking state re-plans on the
		/// very next tick, finds the same health or distance threshold still violated, and hands
		/// straight back to the retreat — which is exactly the loop this whole change is fixing.
		/// </remarks>
		/// <param name="holdSeconds">Seconds during which fleeing is refused.</param>
		public void NoteReengage(float holdSeconds)
		{
			ReengageHold = Mathf.Max(0f, holdSeconds);
		}

		/// <summary>
		/// Advances the budget by one AI tick.
		/// </summary>
		/// <remarks>
		/// Called on every tick regardless of state, because the refund and both holds have to run
		/// while the NPC is <em>not</em> retreating — a budget that only ticked inside the retreat
		/// state could never refill, and both holds would be permanent.
		/// </remarks>
		/// <param name="retreating">True if the NPC spent this tick running away.</param>
		/// <param name="deltaTime">Seconds elapsed since the previous AI tick.</param>
		/// <param name="budgetSeconds">Seconds of retreating allowed per fight. 0 disables the cap.</param>
		/// <param name="recoverySeconds">Seconds of hold once the cap is spent.</param>
		public void Tick(bool retreating, float deltaTime, float budgetSeconds, float recoverySeconds)
		{
			if (deltaTime < 0f)
			{
				deltaTime = 0f;
			}

			if (!primed)
			{
				Reset(budgetSeconds);
			}

			if (DecisionTimer > 0f)
			{
				DecisionTimer -= deltaTime;
			}

			if (ReengageHold > 0f)
			{
				ReengageHold -= deltaTime;
				if (ReengageHold < 0f)
				{
					ReengageHold = 0f;
				}
			}

			/* No cumulative cap configured: the only thing that can refuse a retreat is the hold
			 * imposed by having chosen to fight. An archetype that wants the old unbounded-flee
			 * behaviour sets both this and ReengageChance to zero. */
			if (budgetSeconds <= 0f)
			{
				return;
			}

			if (RecoveryTimer > 0f)
			{
				RecoveryTimer -= deltaTime;
				if (RecoveryTimer <= 0f)
				{
					Reset(budgetSeconds);
				}
				return;
			}

			if (retreating)
			{
				Remaining -= deltaTime;
				if (Remaining <= 0f)
				{
					Remaining = 0f;
					RecoveryTimer = Mathf.Max(0f, recoverySeconds);

					// A zero-second hold would leave nothing refusing the next retreat, so the
					// budget refills immediately and the cap is enforced only by the slow refund.
					if (RecoveryTimer <= 0f)
					{
						Reset(budgetSeconds);
					}
				}
				return;
			}

			Remaining = Mathf.Min(windowSeconds, Remaining + deltaTime * REFUND_RATE);
		}
	}
}
