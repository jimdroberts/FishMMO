using System.IO;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.UnitTests.Harness;
using FishNet.Serializing;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Pins the cost model of the observer resource channel, message by message.
	/// </summary>
	/// <remarks>
	/// Every one of these is a regression for something that was measured going out on the wire and
	/// then thrown away by the receiver: values no client renders driving the rate limit, a loss
	/// repair firing on every regeneration pulse, unchanged maxima re-sent five times a second, a
	/// health change delivered twice, and a party row that overwrote an exact number with a
	/// quantised copy of itself.
	/// </remarks>
	[TestFixture]
	public class ObserverResourceChannelTests
	{
		private const string ControllerPath =
			"Assets/Scripts/Shared/Implementation/Entity/Prediction/CharacterAttribute/CharacterAttributeController.cs";

		private const string DamagePath =
			"Assets/Scripts/Shared/Implementation/Entity/Prediction/CharacterAttribute/CharacterDamageController.cs";

		private const string BroadcastPath =
			"Assets/Scripts/Shared/Implementation/Network/Character/Prediction/PredictionObserverBroadcasts.cs";

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path).Replace("\r\n", "\n");
		}

		/// <summary>A resource sheet with every value nameable, in the shape the sender pushes.</summary>
		private static CharacterAttributeResourceState State(
			float health = 100.0f, int maxHealth = 100,
			float mana = 50.0f, int maxMana = 50,
			float stamina = 100.0f, int maxStamina = 100)
		{
			return new CharacterAttributeResourceState()
			{
				Health = health,
				MaxHealth = maxHealth,
				Mana = mana,
				MaxMana = maxMana,
				Stamina = stamina,
				MaxStamina = maxStamina,
				NextRegenTick = 0u,
			};
		}

		// ── DEFECT 1: stamina and mana must not drive the push ──────────────

		/// <summary>
		/// A sprinting character must not dirty the change gate on every tick.
		/// </summary>
		/// <remarks>
		/// Sprint costs 5 stamina a second (<c>Constants.SprintStaminaCost</c>), so at tick rate 30
		/// the rounded stamina moves about five times a second. Gated as one OR across all six
		/// values at whole units, that marked the gate dirty on essentially every tick and the
		/// scheduler emitted a push on every rate-limit boundary — 2.5&#160;Hz idle, 5&#160;Hz in
		/// combat — carrying the whole sheet to every observer, for a value no client renders.
		/// </remarks>
		[Test]
		public void SprintingStaminaDoesNotDirtyTheChangeGate()
		{
			// Already sprinting, so away from the exactly-full bucket.
			CharacterAttributeResourceState before = State(stamina: 95.0f, maxStamina: 100);

			// One more second of sprinting: five whole units gone, and still inside the same tenth.
			CharacterAttributeResourceState afterOneSecond = State(stamina: 90.0f, maxStamina: 100);

			Assert.AreEqual(ObservedResourcePushScheduler.ChangeKind.None,
				ObservedResourcePushScheduler.ClassifyChange(before, afterOneSecond),
				"Five whole units of stamina inside one bucket must not be a change: nothing draws it. " +
				"At whole units this was thirty dirty ticks a second.");

			// Two seconds: across the boundary, and now it is worth one coarse push.
			CharacterAttributeResourceState afterTwoSeconds = State(stamina: 89.0f, maxStamina: 100);

			Assert.AreEqual(ObservedResourcePushScheduler.ChangeKind.Secondary,
				ObservedResourcePushScheduler.ClassifyChange(before, afterTwoSeconds),
				"Crossing a bucket is the coarsest thing a consumer could act on, so it is what is sent.");
		}

		/// <summary>Health keeps the whole-unit gate it always had; a maximum is health-grade too.</summary>
		[Test]
		public void HealthAndMaximaKeepTheFineSchedule()
		{
			Assert.AreEqual(ObservedResourcePushScheduler.ChangeKind.Primary,
				ObservedResourcePushScheduler.ClassifyChange(State(health: 100.0f), State(health: 99.0f)),
				"One point of health is a visible bar movement and must reach observers immediately.");

			Assert.AreEqual(ObservedResourcePushScheduler.ChangeKind.None,
				ObservedResourcePushScheduler.ClassifyChange(State(health: 100.0f), State(health: 100.4f)),
				"Sub-unit drift is invisible; the whole-unit gate on health is unchanged by the split.");

			Assert.AreEqual(ObservedResourcePushScheduler.ChangeKind.Primary,
				ObservedResourcePushScheduler.ClassifyChange(State(maxMana: 50), State(maxMana: 75)),
				"A maximum is the denominator the bar is drawn against, whichever resource it belongs to.");
		}

		/// <summary>Empty and full are their own buckets, because they are the two facts with meaning.</summary>
		[Test]
		public void EmptyAndFullAreBucketsOfTheirOwn()
		{
			int empty = ObservedResourcePushScheduler.SecondaryResourceBucket(0.0f, 100);
			int almostEmpty = ObservedResourcePushScheduler.SecondaryResourceBucket(1.0f, 100);
			int almostFull = ObservedResourcePushScheduler.SecondaryResourceBucket(99.0f, 100);
			int full = ObservedResourcePushScheduler.SecondaryResourceBucket(100.0f, 100);

			Assert.AreNotEqual(empty, almostEmpty,
				"Spending the last point of a resource changes whether it can pay for anything at all.");
			Assert.AreNotEqual(almostFull, full,
				"Reaching the cap is what stops regeneration; a consumer that cannot see it cannot act on it.");

			Assert.AreEqual(0, ObservedResourcePushScheduler.SecondaryResourceBucket(37.0f, 0),
				"A resource this entity does not have has one bucket, so it can never register a change.");
		}

		/// <summary>
		/// A mana or stamina push must not be able to hold back the next hit.
		/// </summary>
		/// <remarks>
		/// The coarse interval is a ceiling on how often the secondary values may cause a push BY
		/// THEMSELVES. Measured against one shared deadline it would also have delayed the health
		/// push that follows it, which is the one thing this channel exists to deliver promptly.
		/// </remarks>
		[Test]
		public void ASecondaryPushDoesNotDelayTheNextHealthPush()
		{
			const uint Primary = 6;
			const uint Secondary = 30;
			ObservedResourcePushScheduler scheduler = default;
			ObservedResourcePushScheduler.Schedule schedule =
				new ObservedResourcePushScheduler.Schedule(Primary, Secondary, 0u, false);

			Assert.AreEqual(ObservedResourcePushScheduler.Decision.Push,
				scheduler.Evaluate(0, State(), schedule), "First push is unconditional.");

			// A bucket crossing six ticks later is refused: the coarse deadline is thirty out.
			Assert.AreEqual(ObservedResourcePushScheduler.Decision.None,
				scheduler.Evaluate(6, State(stamina: 85.0f), schedule),
				"Mana and stamina may not push at the health rate; that is the whole point of the split.");

			// At thirty it goes.
			Assert.AreEqual(ObservedResourcePushScheduler.Decision.Push,
				scheduler.Evaluate(30, State(stamina: 85.0f), schedule),
				"The coarse interval has elapsed, so the accumulated bucket crossing goes out.");

			// A second crossing must wait out the coarse interval again.
			Assert.AreEqual(ObservedResourcePushScheduler.Decision.None,
				scheduler.Evaluate(36, State(stamina: 75.0f), schedule),
				"Six ticks later is inside the thirty-tick secondary interval.");

			// But a hit on the same tick must not be.
			Assert.AreEqual(ObservedResourcePushScheduler.Decision.Push,
				scheduler.Evaluate(36, State(stamina: 75.0f, health: 40.0f), schedule),
				"Health is measured against its OWN deadline, which elapsed at tick 36. One shared " +
				"deadline would have let a stamina bucket hold back the first hit of a fight.");
		}

		// ── DEFECT 2: the loss repair must not fire on every regen pulse ────

		/// <summary>
		/// Regeneration must not turn a one-off loss repair into a permanent second channel.
		/// </summary>
		/// <remarks>
		/// Regen pulses every 30 ticks; the confirmation delay is 15. Every character below full on
		/// any resource therefore sat in a two-message steady state forever — an unreliable push on
		/// the pulse and a RELIABLE confirmation halfway to the next one, repeating a value the next
		/// pulse was about to replace. The confirmation exists to repair a burst that STOPS.
		/// </remarks>
		[Test]
		public void RegenerationPulsesNeverPayForAConfirmation()
		{
			const uint Interval = 6;
			const uint PulsePeriod = 30;

			ObservedResourcePushScheduler scheduler = default;
			float health = 50.0f;
			uint nextPulse = 0u;

			int confirmations = 0;
			int pushes = 0;

			for (uint tick = 0; tick < 300; ++tick)
			{
				if (tick == nextPulse)
				{
					// Four health a second, delivered on the pulse.
					health = Mathf.Min(100.0f, health + 4.0f);
					nextPulse = tick + PulsePeriod;
				}

				ObservedResourcePushScheduler.Schedule schedule =
					new ObservedResourcePushScheduler.Schedule(Interval, 30u, nextPulse, true);

				switch (scheduler.Evaluate(tick, State(health: health), schedule))
				{
					case ObservedResourcePushScheduler.Decision.Push: ++pushes; break;
					case ObservedResourcePushScheduler.Decision.Confirm: ++confirmations; break;
				}
			}

			/* Ten seconds of regeneration from half health. Thirteen pulses move the value (50 to
			 * 100 at four a pulse), and every one of them is a push; nothing else may be sent. */
			Assert.AreEqual(0, confirmations,
				"A pulse is always scheduled behind the push, so nothing needs repairing yet — " +
				"this used to be one RELIABLE packet per second per observer, indefinitely.");
			Assert.AreEqual(10, pushes,
				"Exactly one push per pulse that moved a value: ten pulses in three hundred ticks.");
		}

		/// <summary>
		/// When the pulses stop moving anything, the repair must still happen.
		/// </summary>
		/// <remarks>
		/// This is why the forecast DEFERS the confirmation rather than cancelling it. A pulse is a
		/// schedule, not a promise: it lands inside a consumption lockout, or on a resource already
		/// at its maximum, and changes nothing. Cancelled, the loss repair for the preceding push
		/// would be gone and observers would be stranded on whatever they last received — which for
		/// a killing blow is a corpse drawn as alive, the exact failure the mechanism exists for.
		/// </remarks>
		[Test]
		public void APulseThatChangesNothingStillGetsItsConfirmation()
		{
			const uint Pulse = 30;
			ObservedResourcePushScheduler scheduler = default;
			ObservedResourcePushScheduler.Schedule schedule =
				new ObservedResourcePushScheduler.Schedule(6u, 30u, Pulse, true);

			// A killing blow at tick 0, with a pulse forecast at 30 that will do nothing: the dead
			// do not regenerate.
			CharacterAttributeResourceState dead = State(health: 0.0f);
			Assert.AreEqual(ObservedResourcePushScheduler.Decision.Push, scheduler.Evaluate(0, dead, schedule));

			for (uint tick = 1; tick < Pulse + ObservedResourcePushScheduler.ConfirmDelayTicks; ++tick)
			{
				Assert.AreEqual(ObservedResourcePushScheduler.Decision.None, scheduler.Evaluate(tick, dead, schedule),
					$"Nothing may be sent on tick {tick}: the confirmation is deferred behind the forecast pulse.");
			}

			Assert.AreEqual(ObservedResourcePushScheduler.Decision.Confirm,
				scheduler.Evaluate(Pulse + ObservedResourcePushScheduler.ConfirmDelayTicks, dead, schedule),
				"One delay past the pulse that changed nothing, the repair fires after all.");
			Assert.AreEqual(0.0f, scheduler.LastPushed.Health, "And it repeats the value that was last sent.");
		}

		/// <summary>The confirmation rule, stated directly as a truth table.</summary>
		[Test]
		public void ConfirmTickTruthTable()
		{
			const uint Delay = ObservedResourcePushScheduler.ConfirmDelayTicks;

			// No forecast at all: the unconditional schedule, unchanged.
			Assert.AreEqual(100u + Delay,
				ObservedResourcePushScheduler.ResolveConfirmTick(100u, ObservedResourcePushScheduler.Schedule.Simple(6u)),
				"With nothing forecast the confirmation is due one delay after the push.");

			/* Any pending pulse defers, not only one inside the window. The pulse period is TWICE
			 * the delay, so a window test would never have fired and the two-message steady state
			 * would have survived the fix. */
			Assert.AreEqual(130u + Delay,
				ObservedResourcePushScheduler.ResolveConfirmTick(100u,
					new ObservedResourcePushScheduler.Schedule(6u, 30u, 130u, true)),
				"A pulse thirty ticks out still defers: there is a push already scheduled behind this one.");

			Assert.AreEqual(110u + Delay,
				ObservedResourcePushScheduler.ResolveConfirmTick(100u,
					new ObservedResourcePushScheduler.Schedule(6u, 30u, 110u, true)),
				"And one inside the window defers rather than being cancelled by it.");

			// A pulse due on this very tick is already folded into what is being pushed.
			Assert.AreEqual(100u + Delay,
				ObservedResourcePushScheduler.ResolveConfirmTick(100u,
					new ObservedResourcePushScheduler.Schedule(6u, 30u, 100u, true)),
				"The confirmation may be deferred, never pulled forward.");

			// Forecast already in the past: it says nothing about the future.
			Assert.AreEqual(100u + Delay,
				ObservedResourcePushScheduler.ResolveConfirmTick(100u,
					new ObservedResourcePushScheduler.Schedule(6u, 30u, 90u, true)),
				"A stale forecast must not defer anything.");
		}

		/// <summary>
		/// The forecast crosses a tick-domain boundary as a DURATION, never as a tick.
		/// </summary>
		/// <remarks>
		/// The regeneration schedule is stamped from a replicate's tick, which on the server is the
		/// owning client's own unsynchronised counter. Handing that number to a schedule measured in
		/// <c>TimeManager.LocalTick</c> would be meaningless; the distance between two ticks of one
		/// domain is a number of ticks and transfers to any other.
		/// </remarks>
		[Test]
		public void ThePeriodicForecastTravelsAsADuration()
		{
			Assert.AreEqual(30u, ObservedResourcePushScheduler.TicksUntil(1000u, 1030u),
				"A forward distance is the number of ticks to wait.");
			Assert.AreEqual(0u, ObservedResourcePushScheduler.TicksUntil(1030u, 1000u),
				"A schedule already in the past is due now, not in four billion ticks.");
			Assert.AreEqual(5u, ObservedResourcePushScheduler.TicksUntil(uint.MaxValue - 2u, 2u),
				"And it is wrap-safe, because the tick counter wraps.");

			string source = ReadSource(ControllerPath);
			LogAssert.IsTrue(
				source.Contains("ObservedResourcePushScheduler.TicksUntil(lastProcessedRegenTick, nextRegenTick)"),
				"the forecast must be built from two ticks of the REPLICATE domain, not from LocalTick");
		}

		// ── DEFECT 3: the changed-field mask ───────────────────────────────

		/// <summary>Only fields that actually moved get a bit.</summary>
		[Test]
		public void MaskNamesOnlyTheFieldsThatMoved()
		{
			CharacterResourcesBroadcast previous = Message(health: 800, maxHealth: 1000, mana: 300, maxMana: 400, stamina: 200, maxStamina: 200);
			CharacterResourcesBroadcast next = Message(health: 740, maxHealth: 1000, mana: 300, maxMana: 400, stamina: 200, maxStamina: 200);

			byte mask = CharacterResourcesMask.ChangedFields(previous, next);

			Assert.AreEqual(CharacterResourcesMask.Health, mask,
				"A hit moves health and nothing else; the other five fields are values the observer " +
				"has held since the spawn payload.");
			Assert.IsFalse(CharacterResourcesMask.IncludesMaximum(mask),
				"And no maximum moved, so this push stays on the unreliable channel.");
		}

		/// <summary>
		/// A masked push is smaller than the sheet it replaces, and the reader consumes exactly it.
		/// </summary>
		/// <remarks>
		/// The three maxima were roughly two fifths of every push and change on an equip, a buff or
		/// a level — so during a fight they are pure duplication of what the spawn payload already
		/// delivered. This is the same redundancy the reconcile path solved for the same struct.
		/// </remarks>
		[Test]
		public void AMaskedPushCarriesOnlyWhatItNames()
		{
			CharacterResourcesBroadcast full = Message(health: 740, maxHealth: 1000, mana: 300, maxMana: 400, stamina: 200, maxStamina: 200);
			full.Mask = CharacterResourcesMask.All;

			CharacterResourcesBroadcast healthOnly = full;
			healthOnly.Mask = CharacterResourcesMask.Health;

			int fullBytes = Bytes(full);
			int maskedBytes = Bytes(healthOnly);

			Assert.Less(maskedBytes, fullBytes,
				"A push that names one field must not cost what a push naming six costs.");

			// And the round trip is exact for what it carries, and untouched for what it does not.
			Writer writer = new Writer();
			writer.WriteCharacterResourcesBroadcast(healthOnly);
			Reader reader = new Reader(writer.GetArraySegment(), null);
			CharacterResourcesBroadcast read = reader.ReadCharacterResourcesBroadcast();

			Assert.AreEqual(0, reader.Remaining, "The reader must consume exactly what was written.");
			Assert.AreEqual(CharacterResourcesMask.Health, read.Mask);
			Assert.AreEqual(healthOnly.Sequence, read.Sequence, "The staleness window travels on every message.");
			Assert.AreEqual(healthOnly.CharacterObjectID, read.CharacterObjectID);
			Assert.AreEqual(740, read.Health, "A named field arrives as an ABSOLUTE, not as a difference.");
			Assert.AreEqual(0, read.MaxHealth,
				"An unnamed field arrives as zero and must never be applied — zero is deliberately " +
				"not a usable maximum, so a receiver that ignores the mask fails loudly.");
			Assert.AreEqual(0, read.Mana);
			Assert.AreEqual(0, read.MaxStamina);
		}

		/// <summary>
		/// The loss-safety contract of a masked update on an unreliable channel.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Two properties together are what make the omission safe. Every field on the wire is an
		/// ABSOLUTE, so a receiver that missed the previous push still applies what arrives exactly
		/// right — there is no delta chain to corrupt, which is the difference from the reconcile
		/// serializer this borrows its shape from. What a loss can cost is an omitted field staying
		/// where it was, and that is bounded by a send that names everything.
		/// </para>
		/// <para>
		/// The confirmation is that send: all six bits, on the reliable channel. The maxima get a
		/// second guarantee on top, because they are the fields with no successor a few ticks behind
		/// — the push that changes one is reliable and the following pushes repeat it.
		/// </para>
		/// </remarks>
		[Test]
		public void EveryFieldIsRepairableAfterAnyLoss()
		{
			Assert.AreEqual(
				CharacterResourcesMask.Health | CharacterResourcesMask.MaxHealth |
				CharacterResourcesMask.Mana | CharacterResourcesMask.MaxMana |
				CharacterResourcesMask.Stamina | CharacterResourcesMask.MaxStamina,
				CharacterResourcesMask.All,
				"A confirmation must be self-sufficient, so 'All' has to mean all six.");

			Assert.IsTrue(CharacterResourcesMask.IncludesMaximum(CharacterResourcesMask.All));
			Assert.IsTrue(CharacterResourcesMask.IncludesMaximum(CharacterResourcesMask.MaxMana));
			Assert.IsFalse(CharacterResourcesMask.IncludesMaximum(
				CharacterResourcesMask.Health | CharacterResourcesMask.Mana | CharacterResourcesMask.Stamina));

			string source = ReadSource(ControllerPath);
			LogAssert.IsTrue(source.Contains("changed = CharacterResourcesMask.All;"),
				"the confirmation must set every bit, or it cannot repair a loss it knows nothing about");
			LogAssert.IsTrue(source.Contains("bool reliable = isConfirm || maximumChanged;"),
				"a push that changes a maximum has no successor behind it and must be delivered");
			LogAssert.IsTrue(source.Contains("reliable ? Channel.Reliable : Channel.Unreliable"),
				"and the change stream itself stays unreliable");
			LogAssert.IsTrue(source.Contains("observedResourceMaximaResends = ObservedResourceMaximaResendPushes;"),
				"the maxima are repeated on the following pushes so a discarded retransmission cannot strand them");
		}

		/// <summary>The sequence window is still what protects a masked update from a reorder.</summary>
		/// <remarks>
		/// A reordered older push carries older absolutes, so applying it would regress a bar. The
		/// existing wrapping window drops it, and that is still the right answer with a mask —
		/// which is why the maxima are repeated rather than sent once and trusted.
		/// </remarks>
		[Test]
		public void AnOlderMaskedPushIsStillDiscarded()
		{
			LogAssert.IsTrue(CharacterAttributeController.IsNewerObservedSequence(9, 8),
				"the next push is newer");
			LogAssert.IsFalse(CharacterAttributeController.IsNewerObservedSequence(8, 9),
				"one that overtook it on the way is not, mask or no mask");
			LogAssert.IsTrue(CharacterAttributeController.IsNewerObservedSequence(0, ushort.MaxValue),
				"and the window wraps");
		}

		// ── DEFECT 4: a health change must not be delivered twice ──────────

		/// <summary>
		/// A combat report reconstructs the health the resource push will later confirm.
		/// </summary>
		/// <remarks>
		/// The reported amount IS the health delta — <c>Damage</c> reports the number it passed to
		/// <c>ResourceInstance.Consume</c> — and a heal clamps server-side to the maximum the
		/// observer already holds. So the observer can move the bar on the tick the report lands
		/// instead of waiting up to a full push interval for the next absolute.
		/// </remarks>
		[Test]
		public void ACombatReportReconstructsTheObservedHealth()
		{
			Assert.AreEqual(740.0f,
				CharacterAttributeController.ResolveObservedHealthAfterDelta(800.0f, 1000, 60, restoresHealth: false),
				"Damage subtracts the post-mitigation amount the server actually applied.");

			Assert.AreEqual(1000.0f,
				CharacterAttributeController.ResolveObservedHealthAfterDelta(980.0f, 1000, 60, restoresHealth: true),
				"A heal clamps to the maximum, exactly as the server's own clamp does.");

			Assert.AreEqual(0.0f,
				CharacterAttributeController.ResolveObservedHealthAfterDelta(20.0f, 1000, 60, restoresHealth: false),
				"And health cannot go below zero, so a killing blow lands on zero rather than past it.");

			Assert.AreEqual(800.0f,
				CharacterAttributeController.ResolveObservedHealthAfterDelta(800.0f, 1000, 0, restoresHealth: false),
				"A zero amount is not an event.");
		}

		/// <summary>
		/// The delta and the push cannot compound, because the push carries an absolute.
		/// </summary>
		/// <remarks>
		/// This is what makes the fast path safe against a duplicated report: the resource push
		/// overwrites rather than adds, so the two channels can disagree for at most one push
		/// interval and the authoritative one always wins.
		/// </remarks>
		[Test]
		public void TheResourcePushOverwritesAnyNumberOfAppliedDeltas()
		{
			float observed = 800.0f;
			observed = CharacterAttributeController.ResolveObservedHealthAfterDelta(observed, 1000, 60, false);
			observed = CharacterAttributeController.ResolveObservedHealthAfterDelta(observed, 1000, 60, false);
			Assert.AreEqual(680.0f, observed, "Two reports, two deltas.");

			// The push then states the server's number outright.
			CharacterResourcesBroadcast push = Message(health: 740, maxHealth: 1000, mana: 0, maxMana: 0, stamina: 0, maxStamina: 0);
			push.Mask = CharacterResourcesMask.Health;
			Assert.AreEqual(740, push.Health,
				"An absolute is idempotent: applying it after one delta, two deltas or none gives the same value.");

			string source = ReadSource(DamagePath);
			LogAssert.IsTrue(source.Contains("attributeController.ApplyObservedHealthDelta(msg.Amount,"),
				"the report must move the bar at the receive seam, not only draw a number");
			LogAssert.IsTrue(source.Contains("if (!targetNob.IsOwner)"),
				"and never for the owner, whose own reconcile is authoritative every tick");
		}

		// ── DEFECT 5: your own party row ───────────────────────────────────

		/// <summary>The exact local value outranks the quantised copy of itself.</summary>
		/// <remarks>
		/// The scene server includes the recipient's own row so the miss counter that greys a member
		/// out stays intact — absence is the signal — but the three fractions on that row are a
		/// byte-quantised copy of a number the panel has just derived exactly from the local
		/// reconciled controller.
		/// </remarks>
		[Test]
		public void YourOwnPartyRowKeepsItsExactValues()
		{
			Assert.IsTrue(UITKParty.ShouldApplyBroadcastVitals(hasLocalVitals: false),
				"Another member, or your own row before the controller resolves: the payload is the only source.");
			Assert.IsFalse(UITKParty.ShouldApplyBroadcastVitals(hasLocalVitals: true),
				"Once the exact fraction has been read locally, the quantised copy may not overwrite it.");
		}

		// ── DEFECTS 6 and 7: pooling and per-observer serialisation ────────

		/// <summary>
		/// Every observer-push baseline is cleared when a pooled object changes occupant.
		/// </summary>
		/// <remarks>
		/// The combat-state edge detector chooses the push interval and decides whether the rate
		/// limit is cleared on the first hit of a fight. Left behind, a pooled shell's next occupant
		/// inherits the previous character's combat state and never sees the transition. It was
		/// benign only because the scheduler's own reset forces an unconditional first push — which
		/// is exactly the kind of accident the rest of that block exists not to rely on.
		/// </remarks>
		[Test]
		public void PoolingClearsEveryObserverPushBaseline()
		{
			string source = ReadSource(ControllerPath);
			int reset = source.IndexOf("public override void ResetState(bool asServer)", System.StringComparison.Ordinal);
			LogAssert.IsTrue(reset > 0, "ResetState must exist to be audited.");
			// Bounded at the next member's doc comment, so a later assignment cannot pass this for it.
			int end = source.IndexOf("\n\t\t/// <summary>", reset, System.StringComparison.Ordinal);
			LogAssert.IsTrue(end > reset, "ResetState must be followed by another documented member.");
			string body = source.Substring(reset, end - reset);

			foreach (string field in new[]
			{
				"resourcePushScheduler.Reset();",
				"lastObservedResourceCombatState = null;",
				"observedResourceDamageControllerResolved = false;",
				"lastSentObservedResources = default;",
				"hasSentObservedResources = false;",
				"observedResourceMaximaResends = 0;",
				"observedResourceSequence = 0;",
				"lastObservedResourceSequence = 0;",
				"lastPushedAttributes = null;",
			})
			{
				LogAssert.IsTrue(body.Contains(field),
					$"ResetState claims completeness for the observer push baselines; {field} is one of them");
			}
		}

		/// <summary>
		/// One combat message is serialised once per channel, not once per observer.
		/// </summary>
		/// <remarks>
		/// The channel is the only thing that differs between recipients — reliable to the source's
		/// owner, unreliable to everyone else — so a per-connection loop paid a full re-serialisation
		/// of an identical payload for every observer of the victim, on the hottest path combat has.
		/// </remarks>
		[Test]
		public void CombatEventsAreSerialisedOncePerChannel()
		{
			string source = ReadSource(DamagePath);

			LogAssert.IsFalse(source.Contains("ServerManager.Broadcast(conn, message"),
				"the per-connection send is what re-serialised the message for every observer");
			LogAssert.IsTrue(source.Contains("Broadcast(combatEventReliableRecipients, message, true, Channel.Reliable)"),
				"the source's owner still gets the reliable copy its rejection signal depends on");
			LogAssert.IsTrue(source.Contains("Broadcast(combatEventUnreliableRecipients, message, true, Channel.Unreliable)"),
				"and every other observer gets one serialisation between them");
			LogAssert.IsTrue(source.Contains("private static readonly HashSet<FishNet.Connection.NetworkConnection> combatEventReliableRecipients"),
				"the recipient sets are scratch, not allocated per event");
		}

		/// <summary>
		/// The <c>Deflected</c> remark must not claim the heading can be re-derived.
		/// </summary>
		/// <remarks>
		/// It said one bit was all it took because the new heading is a pure function of the incoming
		/// one and the normal — directly above <c>PackedDeflectHeading</c>, which exists precisely
		/// because that re-derivation is NOT safe: reflecting twice about the same normal returns the
		/// original vector, so the caster's own client would turn the object straight back.
		/// </remarks>
		[Test]
		public void TheDeflectRemarkMatchesTheWireFormat()
		{
			string source = ReadSource(BroadcastPath);

			LogAssert.IsFalse(
				source.Contains("One bit is all it takes, because the new"),
				"the comment claimed the receiver could re-derive a heading the message deliberately carries");
			LogAssert.IsTrue(source.Contains("public uint PackedDeflectHeading;"),
				"and the absolute heading is still what actually travels");
		}

		// ── helpers ────────────────────────────────────────────────────────

		private static CharacterResourcesBroadcast Message(
			int health, int maxHealth, int mana, int maxMana, int stamina, int maxStamina)
		{
			return new CharacterResourcesBroadcast()
			{
				Sequence = 42,
				CharacterObjectID = 1234,
				Health = health,
				MaxHealth = maxHealth,
				Mana = mana,
				MaxMana = maxMana,
				Stamina = stamina,
				MaxStamina = maxStamina,
			};
		}

		private static int Bytes(CharacterResourcesBroadcast message)
		{
			Writer writer = new Writer();
			writer.WriteCharacterResourcesBroadcast(message);
			return writer.GetArraySegment().Count;
		}
	}
}
