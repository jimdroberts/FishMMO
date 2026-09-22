using NUnit.Framework;
using FishMMO.Shared.Weather;

namespace FishMMO.UnitTests.Weather
{
	/// <summary>
	/// The closed loop between the prediction replicate's tick and the tick the weather is read at
	/// (N12).
	/// </summary>
	/// <remarks>
	/// <para>
	/// These are the tests the exposure controller was built around, because the failure they guard
	/// against is invisible. The weather timeline is anchored in the SYNCHRONISED tick; a
	/// replicate's tick is the owning client's own counter, which restarts at zero on every connect.
	/// Read the forecast at the wrong one and nothing throws — the client simply predicts a
	/// different hour's weather from the server, every reconcile drags the exposure back, and the
	/// buff flickers on a player's screen with no error anywhere to explain it.
	/// </para>
	/// <para>
	/// The loop closes because FishNet advances <c>ClientReplayTick</c> and <c>ServerReplayTick</c>
	/// together during a reconcile replay. So the formula in
	/// <see cref="WeatherExposureTick.Resolve"/> must return, for the replicate being replayed at a
	/// client tick, precisely the server tick FishNet is replaying it as. That identity is what most
	/// of these check, over the tick ranges a real session produces — including the wrap.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class WeatherExposureTickTests
	{
		/// <summary>
		/// A client's counter starts at zero on connect while a long-lived server is tens of
		/// thousands of ticks in. That gap is the whole problem.
		/// </summary>
		private const uint ClientStateTick = 640u;
		private const uint ServerStateTick = 1_250_000u;

		private static uint Owner(uint inputTick, uint clientState = ClientStateTick, uint serverState = ServerStateTick)
		{
			return WeatherExposureTick.Resolve(false, WeatherExposureTick.Unset, 99u, clientState, serverState, inputTick);
		}

		[Test]
		public void TheFormulaMatchesFishNetsReplayCountersEveryStep()
		{
			// FishNet's replay loop, reproduced: both counters start one past the reconciled pair
			// and advance in lockstep (PredictionManager, the while (ClientReplayTick < localTick)
			// loop). For every step of it the mapping must return the server tick FishNet is using.
			uint clientReplayTick = ClientStateTick + 1;
			uint serverReplayTick = ServerStateTick + 1;

			for (int step = 0; step < 512; step++)
			{
				Assert.That(Owner(clientReplayTick), Is.EqualTo(serverReplayTick),
					$"replay step {step}: the weather tick must be the one FishNet is replaying");
				Assert.That(WeatherExposureTick.ReplayAgrees(ClientStateTick, ServerStateTick, clientReplayTick, serverReplayTick),
					Is.True, $"replay step {step}: the closed-loop check must agree with itself");

				clientReplayTick++;
				serverReplayTick++;
			}
		}

		[Test]
		public void TheOwnerPredictingForwardStaysOnTheServersClock()
		{
			// Live prediction runs AHEAD of the last reconcile, which is the case the replay
			// counters cannot cover: there is no ServerReplayTick to ask.
			for (uint ahead = 1; ahead <= 120; ahead++)
			{
				Assert.That(Owner(ClientStateTick + ahead), Is.EqualTo(ServerStateTick + ahead),
					"a tick predicted forward is that many ticks ahead on the server's clock too");
			}
		}

		[Test]
		public void TheServerReadsItsOwnClockAndNeverTheInputsLabel()
		{
			// The decisive one. On the server input.GetTick() is the OWNER's counter — an arbitrary
			// constant away from the server's — so using it would read a completely different hour's
			// weather. Whatever the input says, the answer is the server's tick.
			const uint serverTick = 1_250_064u;
			foreach (uint inputTick in new[] { 0u, 1u, 640u, 999_999u, uint.MaxValue, WeatherExposureTick.Unset })
			{
				Assert.That(WeatherExposureTick.Resolve(true, serverTick, serverTick, ClientStateTick, ServerStateTick, inputTick),
					Is.EqualTo(serverTick), $"the server must ignore the input's tick ({inputTick})");
			}
		}

		[Test]
		public void BeforeAnyReconcileTheClientUsesItsOwnSynchronisedEstimate()
		{
			// The first seconds after spawn: no reconcile has happened, so there is no pairing to
			// extrapolate from. The client's synchronised tick is the same quantity the server is
			// using, so it is the right fallback — and far better than the raw input tick, which is
			// the client's private counter and would be wrong by the whole connection constant.
			const uint clientSyncTick = 1_249_990u;
			Assert.That(WeatherExposureTick.Resolve(false, WeatherExposureTick.Unset, clientSyncTick,
				WeatherExposureTick.Unset, WeatherExposureTick.Unset, 12u), Is.EqualTo(clientSyncTick));

			// Half a pairing is no pairing.
			Assert.That(WeatherExposureTick.Resolve(false, WeatherExposureTick.Unset, clientSyncTick,
				ClientStateTick, WeatherExposureTick.Unset, 12u), Is.EqualTo(clientSyncTick));
			Assert.That(WeatherExposureTick.Resolve(false, WeatherExposureTick.Unset, clientSyncTick,
				WeatherExposureTick.Unset, ServerStateTick, 12u), Is.EqualTo(clientSyncTick));

			// And a replicate carrying no real input (an empty queue runs the body with a default
			// struct) must not be mapped as though its zero tick were meaningful.
			Assert.That(WeatherExposureTick.Resolve(false, WeatherExposureTick.Unset, clientSyncTick,
				ClientStateTick, ServerStateTick, WeatherExposureTick.Unset), Is.EqualTo(clientSyncTick));
		}

		[Test]
		public void MappingRoundTripsBackToTheTickItCameFrom()
		{
			// The offset is a constant, so the mapping is reversible. If it ever stops round-tripping
			// the two clocks have been mixed up somewhere.
			long offset = (long)ServerStateTick - ClientStateTick;
			for (uint inputTick = ClientStateTick; inputTick < ClientStateTick + 200; inputTick++)
			{
				uint weatherTick = Owner(inputTick);
				Assert.That((long)weatherTick - offset, Is.EqualTo((long)inputTick), "client -> server -> client must be the identity");
			}
		}

		[Test]
		public void AReplayBehindTheReconciledPairMapsBackwardsNotIntoTheFuture()
		{
			// Signed arithmetic: an input tick BEHIND the reconciled one must map behind the server's
			// too. Done unsigned, the subtraction would wrap to about four billion and the weather
			// would be read some four years out.
			Assert.That(Owner(ClientStateTick - 30), Is.EqualTo(ServerStateTick - 30));
			Assert.That(Owner(ClientStateTick - 600), Is.EqualTo(ServerStateTick - 600));
		}

		[Test]
		public void TheMappingSurvivesTheClientsCounterWrapping()
		{
			// A client tick that has wrapped past uint.MaxValue while the server's has not. The
			// difference still has to come out as the small number of ticks it really is.
			const uint wrappedClientState = uint.MaxValue - 4u;
			const uint serverState = 500_000u;

			// Eight ticks later the client's counter has wrapped through zero to 3.
			uint wrappedInput = unchecked(wrappedClientState + 8u);
			Assert.That(wrappedInput, Is.LessThan(wrappedClientState), "this test is only meaningful if the counter really wrapped");
			Assert.That(Owner(wrappedInput, wrappedClientState, serverState), Is.EqualTo(serverState + 8u),
				"eight ticks after the reconcile is eight ticks on the server, wrap or no wrap");
		}

		[Test]
		public void AMappingThatWouldFallBelowZeroIsClampedRatherThanWrapped()
		{
			/* Early in a server's life the pairing can be small enough that a replay behind it would
			 * go negative: here the client is 99 ticks past a reconcile the server made at its tick 5.
			 * Clamping reads the oldest weather there is; wrapping would read the forecast for four
			 * billion ticks hence.
			 *
			 * Note the input tick is 1 and not 0. FishNet's UNSET_TICK is literally zero, so a zero
			 * input tick is indistinguishable from "no input" and takes the fallback above instead —
			 * which is correct, and is why this case has to be built out of a non-zero tick. */
			Assert.That(WeatherExposureTick.Resolve(false, WeatherExposureTick.Unset, 50u, 100u, 5u, 1u), Is.EqualTo(0u));
		}

		[Test]
		public void TheClosedLoopCheckIsQuietWhenThereIsNothingToCheck()
		{
			// Unset counters mean no replay is in progress. That is not a disagreement.
			Assert.That(WeatherExposureTick.ReplayAgrees(WeatherExposureTick.Unset, ServerStateTick, 5u, 6u), Is.True);
			Assert.That(WeatherExposureTick.ReplayAgrees(ClientStateTick, ServerStateTick, WeatherExposureTick.Unset, 6u), Is.True);

			// And it really does fail when the two clocks disagree, or it would be worthless.
			Assert.That(WeatherExposureTick.ReplayAgrees(ClientStateTick, ServerStateTick, ClientStateTick + 1, ServerStateTick + 2), Is.False);
		}
	}
}
