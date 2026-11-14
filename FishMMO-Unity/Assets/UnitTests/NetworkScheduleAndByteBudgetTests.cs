using System.Collections.Generic;
using System.IO;
using NUnit.Framework;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The bandwidth work of 2026-11-13: sending every Nth tick while simulating every tick (NetworkSendSchedule, with
	/// FishNet tick markers so batching cannot shift a message's tick), and each viewer's byte budget for observed characters
	/// (ObserverStreamingPolicy.FitByteBudget).
	/// </summary>
	public class NetworkScheduleAndByteBudgetTests
	{
		private int savedBudget, savedCost;
		private float savedIdle, savedHysteresis;

		[SetUp]
		public void SetUp()
		{
			savedBudget = ObserverStreamingPolicy.ByteBudget;
			savedCost = ObserverStreamingPolicy.UpdateCostBytes;
			savedIdle = ObserverStreamingPolicy.IdleCostShare;
			savedHysteresis = ObserverStreamingPolicy.VisibilityBudgetHysteresis;
			ObserverStreamingPolicy.UpdateCostBytes = 10;
			ObserverStreamingPolicy.IdleCostShare = 0.1f;
			ObserverStreamingPolicy.VisibilityBudgetHysteresis = 0.25f;
		}

		[TearDown]
		public void TearDown()
		{
			ObserverStreamingPolicy.ByteBudget = savedBudget;
			ObserverStreamingPolicy.UpdateCostBytes = savedCost;
			ObserverStreamingPolicy.IdleCostShare = savedIdle;
			ObserverStreamingPolicy.VisibilityBudgetHysteresis = savedHysteresis;
		}

		private static ObserverStreamingPolicy.BudgetSlot Slot(byte desired = 1, bool pinned = false, bool engaged = false, bool moving = true, bool visible = false)
		{
			return new ObserverStreamingPolicy.BudgetSlot { DesiredInterval = desired, Pinned = pinned, Engaged = engaged, Moving = moving, CurrentlyVisible = visible };
		}

		// At 30 ticks/s and 10 B an update: a moving character costs 300 B/s at interval 1, 150 B/s at interval 2.

		[Test]
		public void VisibilityComesBeforeFidelity()
		{
			var slots = new List<ObserverStreamingPolicy.BudgetSlot> { Slot(), Slot(), Slot(), Slot() };
			ObserverStreamingPolicy.FitByteBudget(slots, 600, 30, 2);
			for (int i = 0; i < 4; ++i)
			{
				LogAssert.IsTrue(slots[i].Admitted, $"all four fit at the slowest interval (4 × 150 = 600), slot {i}");
				LogAssert.AreEqual((byte)2, slots[i].Interval, $"none can be upgraded without dropping another, slot {i}");
			}
		}

		[Test]
		public void LeftoverBudgetUpgradesInPriorityOrder()
		{
			var slots = new List<ObserverStreamingPolicy.BudgetSlot> { Slot(), Slot(), Slot() };
			ObserverStreamingPolicy.FitByteBudget(slots, 600, 30, 2);
			LogAssert.AreEqual((byte)1, slots[0].Interval, "450 at the slowest leaves 150: the most relevant goes to full rate");
			LogAssert.AreEqual((byte)2, slots[1].Interval);
			LogAssert.AreEqual((byte)2, slots[2].Interval);
		}

		[Test]
		public void TheLeastRelevantAreLeftOutWhenTheBudgetRunsOut()
		{
			var slots = new List<ObserverStreamingPolicy.BudgetSlot> { Slot(), Slot(), Slot() };
			ObserverStreamingPolicy.FitByteBudget(slots, 300, 30, 2);
			LogAssert.IsTrue(slots[0].Admitted && slots[1].Admitted, "two fit at 150 each");
			LogAssert.IsFalse(slots[2].Admitted, "the third does not");
		}

		[Test]
		public void PinnedAreNeverLeftOut()
		{
			var slots = new List<ObserverStreamingPolicy.BudgetSlot> { Slot(), Slot(pinned: true) };
			ObserverStreamingPolicy.FitByteBudget(slots, 150, 30, 2);
			LogAssert.IsTrue(slots[1].Admitted, "a pin is admitted even with the budget spent");
		}

		[Test]
		public void EngagedAreNeverSlowedOrLeftOut_AndAreChargedFirst()
		{
			// The engaged one is listed last, yet it is charged first, at full rate.
			var slots = new List<ObserverStreamingPolicy.BudgetSlot> { Slot(), Slot(), Slot(engaged: true) };
			ObserverStreamingPolicy.FitByteBudget(slots, 450, 30, 2);
			LogAssert.IsTrue(slots[2].Admitted, "engaged is admitted");
			LogAssert.AreEqual((byte)1, slots[2].Interval, "engaged keeps full rate: lag compensation rewinds to what the viewer saw");
			LogAssert.IsTrue(slots[0].Admitted, "300 + 150 = 450: one more fits");
			LogAssert.IsFalse(slots[1].Admitted, "and the next does not");
		}

		[Test]
		public void AStillCharacterCostsOnlyItsIdleShare()
		{
			var slots = new List<ObserverStreamingPolicy.BudgetSlot>();
			for (int i = 0; i < 10; ++i)
			{
				slots.Add(Slot(moving: false));
			}
			float spent = ObserverStreamingPolicy.FitByteBudget(slots, 150, 30, 2);
			for (int i = 0; i < 10; ++i)
			{
				LogAssert.IsTrue(slots[i].Admitted, $"ten still characters at 15 B/s each fit in 150, slot {i}");
			}
			LogAssert.IsTrue(spent <= 150f, $"and the plan stays within the budget ({spent})");
		}

		[Test]
		public void ACharacterAlreadyVisibleKeepsItsPlaceWithinTheHysteresis()
		{
			var slots = new List<ObserverStreamingPolicy.BudgetSlot> { Slot(), Slot(), Slot(visible: true) };
			ObserverStreamingPolicy.FitByteBudget(slots, 360, 30, 2);
			LogAssert.IsTrue(slots[2].Admitted, "450 is within 360 × 1.25: a visible one at the edge stays");
			var fresh = new List<ObserverStreamingPolicy.BudgetSlot> { Slot(), Slot(), Slot(visible: false) };
			ObserverStreamingPolicy.FitByteBudget(fresh, 360, 30, 2);
			LogAssert.IsFalse(fresh[2].Admitted, "but one not yet visible is not admitted past the budget");
		}

		[Test]
		public void TheScheduleNeverExceedsTheTestedInterval()
		{
			LogAssert.AreEqual((byte)1, NetworkSendSchedule.Clamp(0));
			LogAssert.AreEqual((byte)2, NetworkSendSchedule.Clamp(2));
			LogAssert.AreEqual(NetworkSendSchedule.MaximumInterval, NetworkSendSchedule.Clamp(9), "clamped to what the interpolation buffer was sized for");
		}

		[Test]
		public void TheReplicateQueueDeepensWithTheClientInterval()
		{
			LogAssert.AreEqual((byte)2, NetworkSendSchedule.StateInterpolationFor(2, 1), "every-tick sends keep the authored depth");
			LogAssert.AreEqual((byte)3, NetworkSendSchedule.StateInterpolationFor(2, 2),
				"inputs arriving in pairs need one more tick of queue, or a late pair empties it and default input runs");
		}

		private static string Read(string relative) =>
			ProjectFiles.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Assets/Plugins/FishNet/Runtime", relative));

		/// <summary>
		/// A bundle carries one tick in its header and the receiver reads every message in it against that tick, so a bundle
		/// spanning two ticks would read the second tick's inputs one tick early. The tick marker keeps each message on its
		/// own tick; these pins keep the four halves of it together.
		/// </summary>
		[Test]
		public void BatchedBundlesMarkEachTickChange()
		{
			string buffer = Read("Connection/Buffer.cs");
			LogAssert.IsTrue(buffer.Contains("ushort marker = (ushort)PacketId.TickMarker;") && buffer.Contains("else if (markTicks && tick != Tick)"),
				"the writer marks a later tick inside a buffer");
			LogAssert.IsTrue(buffer.Contains("GetSendInterval(_asServer) > 1"),
				"but only on a side whose interval is above 1, so an interval of 1 is FishNet's own wire format");
			LogAssert.IsTrue(buffer.Contains("segment.Count + markerLength <= ba.Remaining"), "and leaves room for the marker");
			LogAssert.IsTrue(Read("Managing/Server/ServerManager.cs").Contains("if (packetId == PacketId.TickMarker)"), "the server reads it");
			LogAssert.IsTrue(Read("Managing/Client/ClientManager.cs").Contains("if (packetId == PacketId.TickMarker)"), "the client reads it");
			LogAssert.IsTrue(Read("Managing/Timing/TimeManager.cs").Contains("transportManager.ShouldFlushOutgoing(asServer: true, LocalTick)"),
				"the tick flushes per side by its interval");
		}
	}
}
