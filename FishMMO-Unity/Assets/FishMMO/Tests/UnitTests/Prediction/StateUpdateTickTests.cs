using System.Reflection;
using FishNet.Managing.Predicting;
using NUnit.Framework;
using FishMMO.UnitTests.Harness;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The client tick the server stamps on a state update (FishNet, FISHMMO EDIT: PredictionManager.GetStateClientTick).
	/// </summary>
	/// <remarks>
	/// The stamp tells the owner which of its own ticks the state is for; it rolls back to the state and replays every
	/// input after that tick. On a server tick that ran one of the owner's inputs the stamp is that input's tick. The
	/// question is a tick that ran none. FishNet answered with an estimate, one tick later for every such tick, which is
	/// right for an owner that stopped sending (its input is default, and the default data the server ran IS its next
	/// tick) and wrong for one whose input is late: the owner was pulled back a tick of its own movement per blank tick
	/// and snapped forward when the late input ran.
	/// </remarks>
	public class StateUpdateTickTests
	{
		private const uint TickRate = 30;

		private static readonly MethodInfo Resolve = typeof(PredictionManager).GetMethod("GetStateClientTick", BindingFlags.Static | BindingFlags.NonPublic);

		/// <summary>The stamp, the given number of server ticks after input with tick <paramref name="lastRun"/> was run.</summary>
		private static uint Stamp(uint lastRun, uint ticksSince, bool inputExpected)
		{
			Assert.IsNotNull(Resolve, "PredictionManager.GetStateClientTick is gone: the state update's tick has no rule");
			// FishNet's estimate: the last tick run, carried forward by the server ticks since.
			uint estimate = lastRun + ticksSince;
			return (uint)Resolve.Invoke(null, new object[] { lastRun, estimate, ticksSince, inputExpected, TickRate });
		}

		[Test]
		public void ATickThatRanInput_IsStampedWithThatInput_EitherWay()
		{
			Assert.AreEqual(500u, Stamp(500u, 0u, inputExpected: true));
			Assert.AreEqual(500u, Stamp(500u, 0u, inputExpected: false));
		}

		[Test]
		public void LateInput_KeepsTheTickOfTheLastInputRun_ThroughTheWholeGap()
		{
			/* The queue runs dry after input 500 that was not default, for five ticks, and the start delay holds the
			 * input that then arrives for two more. Not one of those states may claim a tick past 500: every one that did
			 * would cost the owner the inputs up to it. */
			for (uint blank = 1; blank <= 7; ++blank)
			{
				Assert.AreEqual(500u, Stamp(500u, blank, inputExpected: true),
					$"blank tick {blank}: the state is still the one after input 500, and must say so");
			}
		}

		[Test]
		public void AnOwnerThatStoppedSending_GetsTheEstimate()
		{
			// After default input the owner goes quiet on purpose; the default data the server runs stands for its next ticks.
			for (uint blank = 1; blank <= 7; ++blank)
			{
				Assert.AreEqual(500u + blank, Stamp(500u, blank, inputExpected: false));
			}
		}

		[Test]
		public void InputLateForLongerThanASecond_FallsBackToTheEstimate()
		{
			// The owner replays every input after the stamp on each state: bounded at a second of them.
			Assert.AreEqual(500u, Stamp(500u, TickRate, inputExpected: true), "a second late is still late");
			Assert.AreEqual(500u + TickRate + 1u, Stamp(500u, TickRate + 1u, inputExpected: true));
		}

		[Test]
		public void TheServer_NotesInputAsItRunsIt_AndStampsStatesByTheRule()
		{
			string manager = ProjectFiles.ReadAllText("Assets/Plugins/FishNet/Runtime/Managing/Prediction/PredictionManager.cs");
			StringAssert.Contains("lastReplicateTick = GetStateClientTick(nc.ReplicateTick.RemoteTick, nc.ReplicateTick.Value(),", manager,
				"SendStateUpdate no longer stamps states through the rule");
			StringAssert.Contains("RedundancyCount > 1 && nc.LastInputReplicateLocalTick >= nc.ReplicateTick.LocalTick", manager,
				"input is only known to be late when the owner would have sent its default input before going quiet");

			string behaviour = ProjectFiles.ReadAllText("Assets/Plugins/FishNet/Runtime/Object/NetworkBehaviour/NetworkBehaviour.Prediction.cs");
			StringAssert.Contains("Owner.LastInputReplicateLocalTick = tm.LocalTick;", behaviour,
				"nothing records that the last replicate run for an owner was input");
		}
	}
}
