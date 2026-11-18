using System;
using System.Reflection;
using NUnit.Framework;
using FishMMO.Shared;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Closes the loop on "you hit what you saw": drives BOTH halves of the lag-compensation
	/// derivation from production code across a spread of round-trip times and asserts that the
	/// position the server rewinds to is the position the shooter's client was rendering.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>What makes this different from <see cref="CombatAccuracyLatencyTests"/> and
	/// <see cref="LagCompensationTests"/>.</b> Those two measure one half each: the first models the
	/// error an UNCOMPENSATED shot would carry, the second exercises the ring buffer's resolution in
	/// isolation. Neither composes the client's <c>ResolveViewOffset</c> with the server's
	/// <c>ResolveAnchor</c>, so neither could catch the failure mode that matters most — the two
	/// halves drifting apart, which is silent at runtime because a mis-compensated hit still looks
	/// like a hit.
	/// </para>
	/// <para>
	/// <b>The derivation being pinned.</b> Write everything in fractional server ticks. Let the
	/// owner produce an input at server time <c>S</c>. Three delays separate that instant from the
	/// tick the server RUNS the input on, and three separate it from the tick the owner was LOOKING
	/// at:
	/// </para>
	/// <list type="bullet">
	/// <item><description>The state the owner renders left the server one way trip ago, and
	/// NetworkTransform renders it <c>SpectatorInterpolationTicks</c> behind even that, so the owner
	/// is looking at server tick <c>R = S - oneWay - interp</c>.</description></item>
	/// <item><description>The input crosses the network (another one way trip) and then waits
	/// <c>StateInterpolation</c> ticks in FishNet's replicate queue, so the replicate body runs at
	/// server tick <c>A = S + oneWay + queue</c>.</description></item>
	/// <item><description>The offset the server subtracts is the client's claim (full round trip
	/// plus interpolation) plus the queue depth: <c>A - (2·oneWay + interp + queue)</c>.</description></item>
	/// </list>
	/// <para>
	/// Substituting <c>A</c>: <c>S + oneWay + queue - 2·oneWay - interp - queue = S - oneWay -
	/// interp = R</c>. Every latency term cancels exactly, which is the whole claim of the
	/// subsystem — and the reason it must be pinned is that it only cancels while the client's half
	/// carries the FULL round trip and the server's half adds the queue depth. Either mistake
	/// leaves a residue proportional to ping, which no fixed-latency test would show.
	/// </para>
	/// <para>
	/// <b>What is production and what is the model.</b> The offset arithmetic
	/// (<see cref="LagCompensationTick.ResolveViewOffset"/>), the anchor arithmetic
	/// (<c>LagCompensationTick.ResolveAnchor</c>), the rewind target's sub-tick decomposition
	/// (<see cref="RewindTarget.GetBounds"/>) and the ring buffer's interpolating resolve
	/// (<see cref="CharacterPositionHistory.TryResolve(RewindTarget, out CharacterPositionHistory.Snapshot)"/>)
	/// are all production. What the test supplies is the peer's motion and the latency — the two
	/// things a live session would supply.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class LagCompensationClosedLoopTests
	{
		/// <summary>Server tick rate on every shipped scene.</summary>
		private const int TickRate = 30;

		/// <summary>Seconds per tick.</summary>
		private const double TickDelta = 1.0 / TickRate;

		/// <summary>
		/// <c>PredictionManager.StateInterpolation</c> as authored: how many ticks an arrived input
		/// waits in the replicate queue before the body consumes it.
		/// </summary>
		private const uint QueueTicks = 2;

		/// <summary><c>KCCController.MaxAirMoveSpeed</c> — the fastest a peer moves under its own power.</summary>
		private const float PeerSpeed = 6f;

		/// <summary>Server tick the recorded history starts at. Arbitrary, and deliberately not zero.</summary>
		private const uint FirstTick = 10_000;

		/// <summary>
		/// A realistic latency spread in ONE-WAY milliseconds, including connections the subsystem
		/// is expected to fail gracefully on rather than only ones it handles.
		/// </summary>
		/// <remarks>
		/// Fractional values on purpose: a round trip that lands on a whole tick exercises none of
		/// the sub-tick byte, which is precisely the machinery a capsule-width hit decision depends
		/// on. 8.333 ms is exactly a quarter tick at 30 Hz.
		/// </remarks>
		private static readonly (string Name, double OneWayMs)[] Latencies =
		{
			("lan-5ms", 5.0),
			("same-city-15ms", 15.0),
			("quarter-tick-8.33ms", 1000.0 / TickRate / 4.0),
			("national-30ms", 30.0),
			("cross-country-45ms", 45.0),
			("transatlantic-75ms", 75.0),
			("intercontinental-110ms", 110.0),
			("bad-150ms", 150.0),
		};

		private GameObject go;
		private CharacterPositionHistory history;
		private MethodInfo allocate;
		private MethodInfo record;

		[SetUp]
		public void CreateHistory()
		{
			go = new GameObject("ClosedLoopHistory");
			history = go.AddComponent<CharacterPositionHistory>();

			Type t = typeof(CharacterPositionHistory);
			allocate = t.GetMethod("AllocateBuffer", BindingFlags.Instance | BindingFlags.NonPublic);
			record = t.GetMethod("Record", BindingFlags.Instance | BindingFlags.NonPublic);

			LogAssert.IsNotNull(allocate, "AllocateBuffer must exist; the ring is allocated through it.");
			LogAssert.IsNotNull(record, "Record must exist; the ring is written through it.");
		}

		[TearDown]
		public void DestroyHistory()
		{
			if (go != null)
			{
				UnityEngine.Object.DestroyImmediate(go);
			}
		}

		// ── harness ────────────────────────────────────────────────────────────

		private void Allocate(int ticks) => allocate.Invoke(history, new object[] { ticks });

		private void Record(uint tick, Vector3 position) =>
			record.Invoke(history, new object[] { tick, position, Quaternion.identity });

		/// <summary>
		/// The peer's true world position at any fractional server tick. Continuous, so the test's
		/// ground truth does not inherit the sampling the subsystem under test performs.
		/// </summary>
		private delegate Vector3 Path(double fractionalTick);

		/// <summary>A straight run at <see cref="PeerSpeed"/>. Linear, so sampling loses nothing.</summary>
		private static Vector3 StraightPath(double fractionalTick)
		{
			double seconds = (fractionalTick - FirstTick) * TickDelta;
			return new Vector3(0f, 0f, (float)(PeerSpeed * seconds));
		}

		/// <summary>
		/// A hard circular strafe at <see cref="PeerSpeed"/> on a 3&#160;m radius — about as sharply
		/// as a player can turn while sprinting. Curved, so the ring's linear interpolation between
		/// 33&#160;ms samples costs a measurable chord error.
		/// </summary>
		private static Vector3 CirclePath(double fractionalTick)
		{
			const double radius = 3.0;
			double seconds = (fractionalTick - FirstTick) * TickDelta;
			double theta = PeerSpeed * seconds / radius;
			return new Vector3((float)(radius * Math.Sin(theta)), 0f, (float)(radius * Math.Cos(theta)));
		}

		/// <summary>Fills the ring with <paramref name="ticks"/> samples of <paramref name="path"/>.</summary>
		private void RecordPath(Path path, int ticks)
		{
			Allocate(ticks);
			for (int i = 0; i < ticks; ++i)
			{
				uint tick = FirstTick + (uint)i;
				Record(tick, path(tick));
			}
		}

		/// <summary>
		/// Runs one shot end to end and reports what the server resolved against what the owner saw.
		/// </summary>
		/// <remarks>
		/// <para>
		/// <paramref name="anchorTick"/> is the server tick the replicate body runs on. Everything
		/// else is derived from it, so the test never has to invent a "now" — which is the point:
		/// the owner's <c>S</c> and the tick it was rendering are consequences of the anchor and the
		/// latency, exactly as they are at runtime.
		/// </para>
		/// <para>
		/// The claim is produced by production's <c>ResolveViewOffset</c> from the round trip alone,
		/// so the byte quantisation the wire imposes is included rather than modelled away.
		/// </para>
		/// </remarks>
		private void Shoot(
			double oneWayMs, uint anchorTick, Path path,
			out double renderedTick, out RewindTarget target, out bool resolved,
			out Vector3 serverResolvedPosition, out Vector3 ownerRenderedPosition)
		{
			double oneWayTicks = oneWayMs / 1000.0 / TickDelta;

			// The client half: full round trip plus its interpolation buffer, as bytes on the wire.
			LagCompensationTick.ResolveViewOffset(
				oneWayMs * 2.0, TickDelta, out byte claimedTicks, out byte claimedFraction);

			// The server half: cap the claim, add the queue depth the client cannot see.
			resolved = LagCompensationTick.ResolveAnchor(
				anchorTick, claimedTicks, claimedFraction, QueueTicks, out target);

			/* Ground truth, in fractional server ticks and independent of everything above.
			 * The input was produced at S = anchor - oneWay - queue, and at that instant the owner
			 * was rendering the server's state from oneWay + interp ticks earlier. */
			double producedAtTick = anchorTick - oneWayTicks - QueueTicks;
			renderedTick = producedAtTick - oneWayTicks - LagCompensationTick.SpectatorInterpolationTicks;

			ownerRenderedPosition = path(renderedTick);
			serverResolvedPosition = Vector3.zero;
			if (resolved && history.TryResolve(target, out CharacterPositionHistory.Snapshot snapshot))
			{
				serverResolvedPosition = snapshot.Position;
			}
		}

		/// <summary>
		/// The worst error the sub-tick byte alone can produce, in metres, at a given peer speed.
		/// </summary>
		/// <remarks>
		/// One count of the fraction byte is 1/256 of a tick. Two of them can be lost — one to the
		/// floor/round in <c>ResolveViewOffset</c>, one to the interpolation alpha — so the bound is
		/// stated as two counts rather than one, and it is still well under a millimetre.
		/// </remarks>
		private static float QuantizationBoundMetres(float speed) =>
			(float)(speed * TickDelta * 2.0 / 256.0);

		// ── the loop ───────────────────────────────────────────────────────────

		/// <summary>
		/// Across every latency, the tick the server rewinds to IS the tick the owner was rendering,
		/// to within the sub-tick byte.
		/// </summary>
		/// <remarks>
		/// This is the arithmetic half, asserted before the positional half so a failure says which
		/// of the two broke. A residue that grows with latency means one of the terms in
		/// <c>ResolveViewOffset</c> or <c>ResolveAnchor</c> stopped cancelling — half a round trip
		/// instead of a full one, or a missing queue term.
		/// </remarks>
		[Test]
		public void RewindTick_MatchesTheTickTheOwnerRendered_AtEveryLatency()
		{
			RecordPath(StraightPath, 64);

			foreach ((string name, double oneWayMs) in Latencies)
			{
				uint anchorTick = FirstTick + 48;
				Shoot(oneWayMs, anchorTick, StraightPath,
					out double renderedTick, out RewindTarget target, out bool resolved,
					out _, out _);

				LogAssert.IsTrue(resolved,
					$"[{name}] The server must compensate a shot from a client that reported latency.");

				double error = Math.Abs(target.AsFractionalTick - renderedTick);
				LogAssert.IsTrue(error <= 1.0 / 256.0 + 1e-9,
					$"[{name}] The rewind target and the owner's rendered tick must agree to within the " +
					$"sub-tick byte. Rewound to {target.AsFractionalTick:F5}, owner rendered " +
					$"{renderedTick:F5}, error {error:F6} ticks — the latency terms are not cancelling.");
			}
		}

		/// <summary>
		/// The residue does not GROW with latency. A subsystem that compensated half the round trip
		/// would still pass a fixed-latency test; it cannot pass this one.
		/// </summary>
		[Test]
		public void RewindError_DoesNotScaleWithLatency()
		{
			RecordPath(StraightPath, 64);

			double worst = 0.0;
			foreach ((string name, double oneWayMs) in Latencies)
			{
				Shoot(oneWayMs, FirstTick + 48, StraightPath,
					out double renderedTick, out RewindTarget target, out bool resolved, out _, out _);
				LogAssert.IsTrue(resolved, $"[{name}] must resolve.");
				worst = Math.Max(worst, Math.Abs(target.AsFractionalTick - renderedTick));
			}

			/* One sub-tick count, not a fraction of a tick that happens to be small at low ping.
			 * Compensating half the round trip would put the worst case at 150 ms / 33.3 ms = 4.5
			 * ticks — three orders of magnitude above this bound. */
			LogAssert.IsTrue(worst <= 1.0 / 256.0 + 1e-9,
				$"The worst rewind error across the whole latency spread was {worst:F6} ticks. " +
				"It must be bounded by the sub-tick byte and independent of ping.");
		}

		/// <summary>
		/// The POSITION the server resolves is the position the owner was rendering — sub-millimetre
		/// on a straight run, at every latency.
		/// </summary>
		/// <remarks>
		/// A straight run at constant speed is exactly representable by the ring's linear
		/// interpolation, so anything above the quantisation bound here is a real defect rather than
		/// sampling loss. That is what makes this the strict case; the turning case below is the
		/// tolerant one.
		/// </remarks>
		[Test]
		public void ResolvedPosition_MatchesWhatTheOwnerSaw_StraightRun()
		{
			RecordPath(StraightPath, 64);
			float bound = QuantizationBoundMetres(PeerSpeed);

			foreach ((string name, double oneWayMs) in Latencies)
			{
				Shoot(oneWayMs, FirstTick + 48, StraightPath,
					out _, out _, out bool resolved,
					out Vector3 server, out Vector3 owner);

				LogAssert.IsTrue(resolved, $"[{name}] must resolve.");

				float error = Vector3.Distance(server, owner);
				LogAssert.IsTrue(error <= bound,
					$"[{name}] The server resolved the target at {server:F4} while the owner was " +
					$"rendering it at {owner:F4} — {error * 1000f:F3} mm apart, against a " +
					$"{bound * 1000f:F3} mm quantisation bound. A shot the owner saw connect would " +
					"resolve against a different world.");
			}
		}

		/// <summary>
		/// A hard turn costs only the chord error of 33&#160;ms sampling — centimetres, not the tens
		/// of centimetres an uncompensated shot carries.
		/// </summary>
		/// <remarks>
		/// <para>
		/// This is the honest ceiling on the subsystem: the ring stores one pose per tick, so a
		/// curving peer is reconstructed by a chord across the sampling interval whatever the
		/// latency. The bound is derived rather than tuned — a circle of radius <c>r</c> sampled
		/// every <c>d</c> seconds has a maximum sagitta of <c>r(1 - cos(ω·d/2))</c> — and asserted
		/// with a small allowance for the sub-tick byte on top.
		/// </para>
		/// <para>
		/// It is asserted as an UPPER bound and separately as being far below a capsule radius, so a
		/// regression that widened the sampling interval (or dropped the sub-tick interpolation and
		/// snapped to whole ticks) would fail here rather than degrade quietly.
		/// </para>
		/// </remarks>
		[Test]
		public void ResolvedPosition_TracksAHardTurn_WithinTheSamplingChord()
		{
			RecordPath(CirclePath, 64);

			const double radius = 3.0;
			double omega = PeerSpeed / radius;
			double sagitta = radius * (1.0 - Math.Cos(omega * TickDelta / 2.0));
			float bound = (float)sagitta + QuantizationBoundMetres(PeerSpeed);

			/* The capsule the answer has to land inside. If the chord error ever approached this the
			 * ring would need a finer sampling rate, and the assert below is what would say so. */
			const float capsuleRadius = 0.3f;
			LogAssert.IsTrue(bound < capsuleRadius * 0.5f,
				$"The sampling chord error ({bound * 1000f:F2} mm) must stay well inside a " +
				$"{capsuleRadius * 100f:F0} cm capsule, or a turning target cannot be hit reliably " +
				"however good the tick arithmetic is.");

			foreach ((string name, double oneWayMs) in Latencies)
			{
				Shoot(oneWayMs, FirstTick + 48, CirclePath,
					out _, out _, out bool resolved,
					out Vector3 server, out Vector3 owner);

				LogAssert.IsTrue(resolved, $"[{name}] must resolve.");

				float error = Vector3.Distance(server, owner);
				LogAssert.IsTrue(error <= bound,
					$"[{name}] A turning target resolved {error * 1000f:F2} mm from where the owner " +
					$"rendered it, above the {bound * 1000f:F2} mm sampling-chord bound. The rewind " +
					"is no longer reconstructing the arc the owner watched.");
			}
		}

		/// <summary>
		/// What an UNCOMPENSATED server would have got wrong, measured on the same paths, so the
		/// bounds above are anchored to the error they exist to remove.
		/// </summary>
		/// <remarks>
		/// Without this the strict asserts prove only that two pieces of arithmetic agree; they do
		/// not say the arithmetic is worth having. At 150&#160;ms one way the uncompensated gap is
		/// most of two metres — six capsule widths — against a sub-millimetre compensated one.
		/// </remarks>
		[Test]
		public void CompensationIsWorthOrdersOfMagnitude_AgainstResolvingLive()
		{
			RecordPath(StraightPath, 64);
			uint anchorTick = FirstTick + 48;

			foreach ((string name, double oneWayMs) in Latencies)
			{
				Shoot(oneWayMs, anchorTick, StraightPath,
					out _, out _, out bool resolved,
					out Vector3 server, out Vector3 owner);
				LogAssert.IsTrue(resolved, $"[{name}] must resolve.");

				float compensated = Vector3.Distance(server, owner);
				// "Live" is the peer's position at the tick the body runs — no rewind at all.
				float uncompensated = Vector3.Distance(StraightPath(anchorTick), owner);

				LogAssert.IsTrue(uncompensated > compensated * 100f,
					$"[{name}] compensated error {compensated * 1000f:F3} mm vs uncompensated " +
					$"{uncompensated * 1000f:F1} mm. The rewind must be buying at least two orders " +
					"of magnitude, or something has quietly stopped rewinding.");
			}
		}

		/// <summary>
		/// A latency beyond the recorded window degrades to the oldest sample and says so through a
		/// bounded error — it never silently resolves against the live position.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The ring holds <c>maximumRewindMilliseconds</c> of history (500&#160;ms authored, 15
		/// ticks at 30&#160;Hz), while <see cref="LagCompensationTick.MaximumCompensationTicks"/>
		/// caps a CLAIM at 30. The two are deliberately different: the cap stops an inflated claim
		/// reaching for a window that does not exist, and
		/// <see cref="CharacterPositionHistory.TryResolve(uint, out CharacterPositionHistory.Snapshot)"/>
		/// clamps what is left to the oldest sample.
		/// </para>
		/// <para>
		/// The property asserted is that the clamp engages — the answer is the oldest RECORDED pose,
		/// not the present one. An implementation that quietly fell through to the newest sample
		/// would put the shot a full window ahead of the owner's view, which is the failure this
		/// whole subsystem exists to prevent and is invisible in play.
		/// </para>
		/// </remarks>
		[Test]
		public void BeyondTheRecordedWindow_ClampsToTheOldestSample_NeverToThePresent()
		{
			// 15 ticks is the authored 500 ms window at 30 Hz.
			const int windowTicks = 15;
			RecordPath(StraightPath, windowTicks);

			uint anchorTick = FirstTick + windowTicks - 1;
			// 400 ms one way: an 800 ms round trip, well past the recorded window.
			Shoot(400.0, anchorTick, StraightPath,
				out _, out RewindTarget target, out bool resolved,
				out Vector3 server, out Vector3 owner);

			LogAssert.IsTrue(resolved,
				"An honest high-latency client must still be compensated as far as the window allows.");

			Vector3 oldest = StraightPath(FirstTick);
			Vector3 present = StraightPath(anchorTick);

			LogAssert.IsTrue(Vector3.Distance(server, oldest) < 1e-3f,
				$"Past the window the resolve must clamp to the oldest recorded pose {oldest:F3}; " +
				$"it returned {server:F3}. Rewind target was tick {target.AsFractionalTick:F3}.");

			LogAssert.IsTrue(Vector3.Distance(server, present) > 1f,
				"The clamped answer must not be the present position — falling through to live is " +
				"the silent failure this test exists to catch.");

			/* And the residual error is bounded by the window rather than by the ping: the owner
			 * saw further back than anything recorded, so the gap is what the window could not
			 * cover, and it stops growing once the claim exceeds it. */
			float residual = Vector3.Distance(server, owner);
			float windowMetres = (float)(PeerSpeed * windowTicks * TickDelta);
			LogAssert.IsTrue(residual <= windowMetres,
				$"The residual at 800 ms RTT was {residual:F3} m; it cannot exceed the {windowMetres:F3} m " +
				"the recorded window itself spans.");
		}

		/// <summary>
		/// A tick-domain error is still REFUSED rather than clamped, at every latency.
		/// </summary>
		/// <remarks>
		/// The clamp above and this refusal are the two halves of one rule and they must not merge:
		/// a claim a little past the window is honest latency and gets what the window holds, while
		/// a tick hundreds of thousands out is a target built from the owning client's replicate
		/// counter instead of the server's — the exact bug <c>LagCompensationTick</c> exists to
		/// prevent. Clamping that would hand back a real-looking pose for a tick nobody recorded.
		/// </remarks>
		[Test]
		public void ATickDomainError_IsRefused_NotClamped()
		{
			RecordPath(StraightPath, 32);

			/* A replicate-domain tick: the owning client's own counter, which starts when ITS
			 * process did and is unrelated to the server's. Three minutes of client uptime against
			 * a server that has been up longer is the shape of the bug — thousands of ticks adrift,
			 * against a 60-tick honest-latency threshold. */
			var wrongDomain = new RewindTarget(FirstTick - 5_000u);
			LogAssert.IsFalse(history.TryResolve(wrongDomain, out _),
				"A tick from the wrong domain must be refused outright. Clamping it would turn a " +
				"dead rewind into a silently wrong one.");

			// While a claim just past the window, which is honest latency, is served.
			var justPast = new RewindTarget(FirstTick - 5u);
			LogAssert.IsTrue(history.TryResolve(justPast, out _),
				"A claim a little past the window is honest high latency and must still be served.");
		}

		/// <summary>
		/// The claim cap does not eat the queue term. Capping the SUM would compensate a
		/// high-latency client short by the queue depth, whatever the deployment.
		/// </summary>
		/// <remarks>
		/// Ordering-sensitive and invisible at ordinary ping, because the cap only binds above
		/// <see cref="LagCompensationTick.MaximumCompensationTicks"/>. A deployment that raised
		/// <c>StateInterpolation</c> would silently lose exactly that many ticks of compensation for
		/// its worst-connected players.
		/// </remarks>
		[Test]
		public void TheClaimCapAppliesToTheClaimAlone_NotToTheQueueTerm()
		{
			uint anchor = 100_000;
			byte overCap = (byte)(LagCompensationTick.MaximumCompensationTicks + 40);

			LogAssert.IsTrue(
				LagCompensationTick.ResolveAnchor(anchor, overCap, 0, QueueTicks, out RewindTarget target),
				"An over-cap claim must still resolve; it simply buys no more than the cap.");

			uint expected = anchor - (LagCompensationTick.MaximumCompensationTicks + QueueTicks);
			LogAssert.AreEqual(expected, target.Tick,
				"The cap must bind the client's claim and the queue depth must be added afterwards. " +
				$"Expected tick {expected}, got {target.Tick} — capping the sum would lose " +
				$"{QueueTicks} ticks of compensation for every high-latency player.");
		}

		/// <summary>
		/// A shot with no view offset at all resolves against live positions, and a shot with only a
		/// sub-tick offset still compensates.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The first half pins the boundary a server-driven character sits on: an NPC brain aims at
		/// live positions and must not be rewound.
		/// </para>
		/// <para>
		/// The second half is the one that matters for precision. The whole-tick byte alone
		/// quantises the rewind to a 33&#160;ms boundary, which at 6&#160;m/s is 20&#160;cm — most
		/// of a capsule. A build that dropped the fraction byte would still pass every whole-tick
		/// test and lose a third of a capsule width on every shot.
		/// </para>
		/// </remarks>
		[Test]
		public void SubTickOffsetAlone_StillCompensates()
		{
			LogAssert.IsFalse(
				LagCompensationTick.ResolveAnchor(100_000, 0, 0, 0, out _),
				"Nothing to compensate must decline rather than rewind to the present tick.");

			LogAssert.IsTrue(
				LagCompensationTick.ResolveAnchor(100_000, 0, 128, 0, out RewindTarget half),
				"Half a tick of measured view offset is still a view offset.");
			LogAssert.IsTrue(Math.Abs(half.SubTickFraction - 0.5f) < 1e-6f,
				$"The fraction byte must survive into the rewind target; got {half.SubTickFraction}.");

			RecordPath(StraightPath, 32);
			LogAssert.IsTrue(history.TryResolve(new RewindTarget(FirstTick + 20, 0.5f),
				out CharacterPositionHistory.Snapshot snapshot),
				"A sub-tick target must resolve.");

			Vector3 expected = StraightPath(FirstTick + 20 - 0.5);
			LogAssert.IsTrue(Vector3.Distance(snapshot.Position, expected) < 1e-3f,
				$"A half-tick rewind must land halfway between two samples: expected {expected:F4}, " +
				$"got {snapshot.Position:F4}. Snapping to the whole tick costs " +
				$"{PeerSpeed * TickDelta * 100.0:F0} cm at running speed.");
		}

		/// <summary>
		/// The client half is monotonic and total: every round trip a byte can describe produces an
		/// offset, and a larger round trip never produces a smaller one.
		/// </summary>
		/// <remarks>
		/// A cheap property, and the one that would catch a future edit that reintroduced rounding
		/// to whole ticks or let the fraction wrap. Swept finely enough to cross many byte
		/// boundaries rather than sampled at a handful of round numbers.
		/// </remarks>
		[Test]
		public void ViewOffset_IsMonotonicInRoundTrip_AcrossTheWholeByteRange()
		{
			/* Swept past saturation: 255 ticks at 30 Hz is an 8.4 second round trip, so the sweep has
			 * to reach ten seconds to prove the ceiling holds rather than wraps. */
			double previous = -1.0;
			for (double rttMs = 0.0; rttMs <= 10_000.0; rttMs += 0.7)
			{
				LagCompensationTick.ResolveViewOffset(rttMs, TickDelta, out byte whole, out byte fraction);
				double offset = whole + (fraction / 256.0);

				LogAssert.IsTrue(offset >= previous - 1e-9,
					$"View offset must never decrease as the round trip grows: {rttMs:F1} ms gave " +
					$"{offset:F5} ticks after {previous:F5}.");
				previous = offset;
			}

			LogAssert.IsTrue(previous >= byte.MaxValue,
				"A round trip past what the byte can describe must saturate rather than wrap.");
		}
	}
}
