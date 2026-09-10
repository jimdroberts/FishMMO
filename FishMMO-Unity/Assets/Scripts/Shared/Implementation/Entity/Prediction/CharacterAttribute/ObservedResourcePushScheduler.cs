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
	/// <b>Not every resource earns the same schedule.</b> Health drives every observer-facing
	/// readout there is; mana and stamina drive none of them — no nameplate, target frame or party
	/// row reads a peer's mana, and the one genuine peer-side consumer is
	/// <c>HasResourceCondition</c> tested against a victim during a predicted hit. Gated as one OR
	/// across whole units, sprint's 5 stamina a second dirtied the gate on essentially every tick
	/// and turned a change-gated channel into a 2.5-5&#160;Hz stream of the whole resource sheet.
	/// So health (and every maximum) is <see cref="ChangeKind.Primary"/> and keeps the fine
	/// schedule, while mana and stamina are <see cref="ChangeKind.Secondary"/> and move only when
	/// they cross a percentage bucket — the coarsest form anything could act on.
	/// </para>
	/// <para>
	/// Pure state machine with no network dependency so the schedule is unit tested. The owner
	/// of this struct decides what "changed" means (whole units, for a bar) and passes in the
	/// intervals plus, for the confirmation rule, when its next PERIODIC change is due.
	/// </para>
	/// </remarks>
	public struct ObservedResourcePushScheduler
	{
		/// <summary>Ticks after a push before its confirmation re-send. 15 at tick rate 30 is half a second.</summary>
		public const uint ConfirmDelayTicks = 15;

		/// <summary>
		/// Buckets the span between empty and full is divided into for a secondary resource.
		/// </summary>
		/// <remarks>
		/// Ten, so a bucket is a tenth of the bar. Sprint drains stamina at 5 a second, which on a
		/// hundred-point bar crosses a boundary every two seconds rather than every tick, and the
		/// 4/s recovery ramp crosses one every two and a half. Empty and full are buckets of their
		/// own (see <see cref="SecondaryResourceBucket"/>) because "has any at all" and "is
		/// capped" are the two facts a consumer actually branches on.
		/// </remarks>
		public const int SecondaryResourceBuckets = 10;

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

		/// <summary>How much a difference between two resource states is worth.</summary>
		/// <remarks>
		/// The kind selects which rate limit the push is measured against, and nothing else: both
		/// kinds produce the same message. See the type remarks for why the split exists.
		/// </remarks>
		public enum ChangeKind : byte
		{
			/// <summary>Nothing an observer could notice moved.</summary>
			None = 0,
			/// <summary>Only mana or stamina crossed a percentage bucket.</summary>
			Secondary = 1,
			/// <summary>Health moved by a whole unit, or a maximum changed.</summary>
			Primary = 2,
		}

		/// <summary>
		/// The intervals and the periodic-change forecast one <see cref="Evaluate"/> runs against.
		/// </summary>
		/// <remarks>
		/// A value type with no behaviour beyond <see cref="ResolveConfirmTick"/>, so the whole
		/// schedule can be stated in a test without a character, a TimeManager or a regen pulse.
		/// </remarks>
		public readonly struct Schedule
		{
			/// <summary>Minimum ticks between pushes driven by a <see cref="ChangeKind.Primary"/> change.</summary>
			public readonly uint PrimaryInterval;

			/// <summary>Minimum ticks between pushes driven by a <see cref="ChangeKind.Secondary"/> change alone.</summary>
			/// <remarks>
			/// A bucket gate alone is nearly enough, but a value hovering on a boundary — stamina
			/// held at a bucket edge while drain and regeneration alternate — would otherwise
			/// dirty the gate every tick and push at the primary rate. This is the ceiling on that.
			/// </remarks>
			public readonly uint SecondaryInterval;

			/// <summary>Tick the caller's next PERIODIC change is due on, in the same domain as <c>tick</c>.</summary>
			public readonly uint NextPeriodicChangeTick;

			/// <summary>False when the caller has no periodic change to forecast.</summary>
			public readonly bool HasPeriodicChange;

			/// <summary>Builds a schedule. <paramref name="secondaryInterval"/> is floored at <paramref name="primaryInterval"/>.</summary>
			public Schedule(uint primaryInterval, uint secondaryInterval, uint nextPeriodicChangeTick, bool hasPeriodicChange)
			{
				PrimaryInterval = primaryInterval;
				/* A secondary interval below the primary one would be a rate limit that does not
				 * limit: the coarse gate would be free to push more often than the fine one. */
				SecondaryInterval = secondaryInterval < primaryInterval ? primaryInterval : secondaryInterval;
				NextPeriodicChangeTick = nextPeriodicChangeTick;
				HasPeriodicChange = hasPeriodicChange;
			}

			/// <summary>One interval for everything and no periodic forecast — the shape tests use.</summary>
			public static Schedule Simple(uint interval)
			{
				return new Schedule(interval, interval, 0u, false);
			}
		}

		/// <summary>The state most recently sent.</summary>
		public CharacterAttributeResourceState LastPushed;

		/// <summary>False until the first push, which always happens regardless of change.</summary>
		public bool HasPushed;

		/// <summary>Earliest tick a <see cref="ChangeKind.Primary"/> change push may occur on.</summary>
		public uint NextPushTick;

		/// <summary>Earliest tick a <see cref="ChangeKind.Secondary"/>-only change push may occur on.</summary>
		public uint NextSecondaryPushTick;

		/// <summary>True while a confirmation re-send is scheduled for <see cref="ConfirmTick"/>.</summary>
		public bool ConfirmPending;

		/// <summary>Tick the pending confirmation is due on.</summary>
		public uint ConfirmTick;

		/// <summary>
		/// Evaluates one tick against a single interval and no periodic forecast.
		/// </summary>
		/// <remarks>
		/// The shape the schedule had before mana and stamina were split off. Kept because it is
		/// the honest signature for a caller with one resource and no pulse, and because the
		/// confirmation rule it produces is the unconditional one.
		/// </remarks>
		public Decision Evaluate(uint tick, in CharacterAttributeResourceState state, uint pushInterval)
		{
			return Evaluate(tick, state, Schedule.Simple(pushInterval));
		}

		/// <summary>
		/// Evaluates one tick.
		/// </summary>
		/// <param name="tick">The current server tick.</param>
		/// <param name="state">The current resource state, with fields observers do not use zeroed.</param>
		/// <param name="schedule">The intervals and periodic-change forecast in force this tick.</param>
		/// <returns>What, if anything, to send. On <see cref="Decision.Push"/>, <see cref="LastPushed"/> is now <paramref name="state"/>.</returns>
		public Decision Evaluate(uint tick, in CharacterAttributeResourceState state, in Schedule schedule)
		{
			ChangeKind change = HasPushed ? ClassifyChange(LastPushed, state) : ChangeKind.Primary;

			if (change != ChangeKind.None)
			{
				/* Rate limited, against the deadline its own kind owns. A value that changes every
				 * tick still goes out at most once per interval; the confirmation for the previous
				 * push stays scheduled meanwhile, and is replaced when this change finally goes
				 * out. Two deadlines rather than one because a mana bucket crossing must not be
				 * able to hold back the next hit: a push satisfies BOTH (the message carries the
				 * whole sheet's changed fields either way), but only a primary change may be
				 * measured against the primary deadline. */
				uint deadline = change == ChangeKind.Primary ? NextPushTick : NextSecondaryPushTick;
				if (HasPushed && (int)(tick - deadline) < 0)
				{
					return Decision.None;
				}

				LastPushed = state;
				HasPushed = true;
				NextPushTick = tick + schedule.PrimaryInterval;
				NextSecondaryPushTick = tick + schedule.SecondaryInterval;
				ConfirmPending = true;
				ConfirmTick = ResolveConfirmTick(tick, schedule);
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
		/// Drops the outstanding rate limit so the next CHANGE is sent on the tick it happens.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <see cref="NextPushTick"/> is an absolute deadline stamped from the interval in force at
		/// the last push. A caller that widens its interval while a character is idle therefore
		/// leaves a deadline behind that outlives the reason for it — and the event that matters
		/// most (the first hit of a fight) is exactly the one that arrives while it is still
		/// pending. This clears the deadline without pushing anything: nothing is sent unless the
		/// state has actually changed.
		/// </para>
		/// <para>
		/// Deliberately does NOT touch <see cref="LastPushed"/>, <see cref="HasPushed"/> or the
		/// pending confirmation. Clearing those would resend a value the observers already hold and
		/// would drop the loss repair for the previous push.
		/// </para>
		/// <para>
		/// Nor does it touch <see cref="NextSecondaryPushTick"/>. The event this exists for is a
		/// health change; a mana or stamina bucket crossing is not made urgent by combat starting,
		/// and letting it through here would spend the packet combat entry is trying to reserve.
		/// </para>
		/// </remarks>
		/// <param name="tick">The current server tick; the deadline is moved back to it.</param>
		public void AllowImmediatePush(uint tick)
		{
			/* Not zero: Evaluate compares as a SIGNED difference so ticks wrap correctly, and a
			 * literal zero is "very far in the past" only until the tick counter wraps past it.
			 * The current tick is unambiguous — tick - tick is 0, which is not negative, so the
			 * next change passes the gate. */
			NextPushTick = tick;
		}

		/// <summary>
		/// When the confirmation for a push made on <paramref name="pushTick"/> falls due.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <b>A confirmation exists to repair a loss that nothing else will.</b> It repeats the
		/// final value of a burst of changes, on the reliable channel, because that one send has
		/// nothing behind it to supersede it. A burst that is still running needs no repair: the
		/// next push is a few ticks away and carries the same field again.
		/// </para>
		/// <para>
		/// Regeneration made "a burst that stops" untrue. It pulses once a second — thirty ticks,
		/// against a fifteen-tick confirmation delay — so every character below full on any
		/// resource sat in a two-message steady state forever: an unreliable push on the pulse,
		/// then a reliable confirmation fifteen ticks later repeating a value that the next pulse
		/// was about to replace. The confirmation was repairing a value with a successor already
		/// scheduled, which is precisely what it is not for.
		/// </para>
		/// <para>
		/// So a pending periodic change DEFERS the confirmation to one delay past it. Note that
		/// "inside the confirmation window" is not the test: the pulse period is TWICE the delay, so
		/// the pulse never falls inside the window and a window test would have changed nothing at
		/// all. What matters is that a further push is already scheduled, whenever it is due.
		/// </para>
		/// <para>
		/// Deferral rather than cancellation, because the forecast is a schedule and not a promise:
		/// a pulse lands inside a consumption lockout, or on a resource already at its maximum, and
		/// changes nothing. Cancelling on such a pulse would throw away the loss repair for the push
		/// that preceded it and strand observers on whatever they last received — the stale-corpse
		/// failure this whole mechanism exists to remove. Deferred, the confirmation fires the moment
		/// the pulses stop moving the values, and never while they are.
		/// </para>
		/// <para>
		/// The cost is that a burst which ends between two pulses waits for the pulse before it is
		/// repaired: one pulse period plus one delay, rather than one delay. At the authored
		/// one-second regeneration cadence that is at most 45 ticks against 15. A packet lost at the
		/// end of a fight is therefore repaired a second later than it used to be, which is the price
		/// of not sending a reliable repeat every single second of every character's life.
		/// </para>
		/// </remarks>
		/// <param name="pushTick">The tick the push being confirmed went out on.</param>
		/// <param name="schedule">The schedule in force, carrying the periodic-change forecast.</param>
		/// <returns>The tick <see cref="Decision.Confirm"/> becomes due on.</returns>
		public static uint ResolveConfirmTick(uint pushTick, in Schedule schedule)
		{
			uint due = pushTick + ConfirmDelayTicks;

			if (!schedule.HasPeriodicChange)
			{
				return due;
			}

			// Signed differences throughout: the tick counter wraps and these are all distances.
			if ((int)(schedule.NextPeriodicChangeTick - pushTick) < 0)
			{
				// The forecast is already in the past and says nothing about the future.
				return due;
			}

			uint deferred = schedule.NextPeriodicChangeTick + ConfirmDelayTicks;

			/* Never EARLIER than the unconditional schedule. A pulse due on this very tick has
			 * already been folded into the state being pushed, so it must not pull the repair
			 * forward. */
			return (int)(deferred - due) > 0 ? deferred : due;
		}

		/// <summary>
		/// Ticks from <paramref name="from"/> forward to <paramref name="to"/>, floored at zero.
		/// </summary>
		/// <remarks>
		/// <b>A duration, deliberately, not a tick.</b> A caller's periodic schedule is very often
		/// kept in the REPLICATE tick domain — the owning client's own unsynchronised counter — and
		/// comparing that against <c>TimeManager.LocalTick</c> is meaningless. A difference taken
		/// inside one domain is a number of ticks and transfers to any other, so this is how a
		/// replicate-domain pulse is handed to a LocalTick-domain schedule.
		/// </remarks>
		public static uint TicksUntil(uint from, uint to)
		{
			int distance = (int)(to - from);
			return distance <= 0 ? 0u : (uint)distance;
		}

		/// <summary>
		/// How much two resource states differ, as far as an observer is concerned.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Health is compared at whole units because that is what a health bar renders, and every
		/// maximum exactly because it is the denominator that bar is drawn against. Sub-unit
		/// regeneration drift in health would otherwise mark every interval dirty and push
		/// continuously.
		/// </para>
		/// <para>
		/// Mana and stamina are compared by percentage bucket. Nothing on any client renders a
		/// peer's mana or stamina at all; the only peer-side reader is a resource CONDITION
		/// evaluated against a victim, which asks "does it have at least N" — a question a tenth of
		/// the bar answers, and which the server re-answers exactly before anything is allowed to
		/// happen. Compared at whole units these two alone produced a continuous push stream for
		/// any character that was sprinting or recovering.
		/// </para>
		/// </remarks>
		public static ChangeKind ClassifyChange(
			in CharacterAttributeResourceState a, in CharacterAttributeResourceState b)
		{
			if (Mathf.RoundToInt(a.Health) != Mathf.RoundToInt(b.Health) ||
				a.MaxHealth != b.MaxHealth ||
				a.MaxMana != b.MaxMana ||
				a.MaxStamina != b.MaxStamina)
			{
				return ChangeKind.Primary;
			}

			if (SecondaryResourceBucket(a.Mana, a.MaxMana) != SecondaryResourceBucket(b.Mana, b.MaxMana) ||
				SecondaryResourceBucket(a.Stamina, a.MaxStamina) != SecondaryResourceBucket(b.Stamina, b.MaxStamina))
			{
				return ChangeKind.Secondary;
			}

			return ChangeKind.None;
		}

		/// <summary>
		/// True when two resource states differ by enough for an observer to notice.
		/// </summary>
		/// <remarks>
		/// The boolean face of <see cref="ClassifyChange"/>, for callers that only need to know
		/// whether anything is owed rather than which schedule owes it.
		/// </remarks>
		public static bool ResourcesDifferForObservers(
			in CharacterAttributeResourceState a, in CharacterAttributeResourceState b)
		{
			return ClassifyChange(a, b) != ChangeKind.None;
		}

		/// <summary>
		/// Which percentage bucket a secondary resource sits in.
		/// </summary>
		/// <remarks>
		/// Empty is bucket 0 and full is the last bucket, both exactly, with
		/// <see cref="SecondaryResourceBuckets"/> proportional buckets between them. The two ends
		/// are singled out because they are the only two values with a meaning of their own: a
		/// resource at zero cannot pay for anything, and one at its maximum cannot regenerate. A
		/// plain floor would have put 0 and 1 of 100 in the same bucket and 99 and 100 likewise.
		/// </remarks>
		/// <param name="current">The resource's current value.</param>
		/// <param name="max">The resource's maximum; zero or less means the resource is absent.</param>
		/// <returns>A bucket index; only equality between two of these is meaningful.</returns>
		public static int SecondaryResourceBucket(float current, int max)
		{
			if (max <= 0)
			{
				// Absent on this entity. One bucket, so it can never register as a change.
				return 0;
			}
			if (current <= 0.0f)
			{
				return 0;
			}
			if (current >= max)
			{
				return SecondaryResourceBuckets + 1;
			}

			int proportional = (int)(current / max * SecondaryResourceBuckets);
			return 1 + Mathf.Clamp(proportional, 0, SecondaryResourceBuckets - 1);
		}
	}
}
