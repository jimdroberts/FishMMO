using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Decides, tick by tick, whether a character's resources go out to its observers.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The observer resource stream is unreliable and change gated: a value is sent when it
	/// changes and then never again. Lose that one packet and every observer holds a stale bar
	/// until the next change — which for a creature that has just died, or has just been topped
	/// up to full, may be never. This schedules a single confirmation re-send of the last pushed
	/// state <see cref="ConfirmDelayTicks"/> later, provided the state is still what was pushed.
	/// One confirmation is pending at most; a new change replaces it rather than stacking.
	/// </para>
	/// <para>
	/// Pure state machine with no network dependency so the schedule is unit tested. The owner
	/// of this struct decides what "changed" means (whole units, for a bar) and passes it in.
	/// </para>
	/// </remarks>
	public struct ObservedResourcePushScheduler
	{
		/// <summary>Ticks after a push before its confirmation re-send. 15 at tick rate 30 is half a second.</summary>
		public const uint ConfirmDelayTicks = 15;

		/// <summary>Why <see cref="Evaluate"/> asked for a send.</summary>
		public enum Decision : byte
		{
			/// <summary>Nothing to send this tick.</summary>
			None = 0,
			/// <summary>The state changed (or nothing was ever sent): push it.</summary>
			Push = 1,
			/// <summary>The last pushed state is unchanged and its confirmation is due: re-send it.</summary>
			Confirm = 2,
		}

		/// <summary>The state most recently sent.</summary>
		public CharacterAttributeResourceState LastPushed;

		/// <summary>False until the first push, which always happens regardless of change.</summary>
		public bool HasPushed;

		/// <summary>Earliest tick the next change push may occur on.</summary>
		public uint NextPushTick;

		/// <summary>True while a confirmation re-send is scheduled for <see cref="ConfirmTick"/>.</summary>
		public bool ConfirmPending;

		/// <summary>Tick the pending confirmation is due on.</summary>
		public uint ConfirmTick;

		/// <summary>
		/// Evaluates one tick.
		/// </summary>
		/// <param name="tick">The current server tick.</param>
		/// <param name="state">The current resource state, with fields observers do not use zeroed.</param>
		/// <param name="pushInterval">Minimum ticks between change pushes.</param>
		/// <returns>What, if anything, to send. On <see cref="Decision.Push"/>, <see cref="LastPushed"/> is now <paramref name="state"/>.</returns>
		public Decision Evaluate(uint tick, in CharacterAttributeResourceState state, uint pushInterval)
		{
			bool changed = !HasPushed || ResourcesDifferForObservers(LastPushed, state);

			if (changed)
			{
				/* Rate limited. A value that changes every tick still goes out at most once per
				 * interval; the confirmation for the previous push stays scheduled meanwhile, and
				 * is replaced when this change finally goes out. */
				if (HasPushed && (int)(tick - NextPushTick) < 0)
				{
					return Decision.None;
				}

				LastPushed = state;
				HasPushed = true;
				NextPushTick = tick + pushInterval;
				ConfirmPending = true;
				ConfirmTick = tick + ConfirmDelayTicks;
				return Decision.Push;
			}

			if (ConfirmPending && (int)(tick - ConfirmTick) >= 0)
			{
				ConfirmPending = false;
				return Decision.Confirm;
			}

			return Decision.None;
		}

		/// <summary>Forgets everything, so the next evaluation pushes unconditionally.</summary>
		public void Reset()
		{
			this = default;
		}

		/// <summary>
		/// True when two resource states differ by enough for an observer to notice.
		/// </summary>
		/// <remarks>
		/// Compared at whole units because that is what a health bar renders. Sub-unit regeneration
		/// drift would otherwise mark every interval dirty and push continuously.
		/// </remarks>
		public static bool ResourcesDifferForObservers(
			in CharacterAttributeResourceState a, in CharacterAttributeResourceState b)
		{
			return Mathf.RoundToInt(a.Health) != Mathf.RoundToInt(b.Health) ||
				   Mathf.RoundToInt(a.Mana) != Mathf.RoundToInt(b.Mana) ||
				   Mathf.RoundToInt(a.Stamina) != Mathf.RoundToInt(b.Stamina) ||
				   a.MaxHealth != b.MaxHealth ||
				   a.MaxMana != b.MaxMana ||
				   a.MaxStamina != b.MaxStamina;
		}
	}
}
