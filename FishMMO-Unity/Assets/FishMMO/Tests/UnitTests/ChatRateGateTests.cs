using System;
using System.Collections.Generic;
using System.Reflection;
using NUnit.Framework;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Server.Implementation.World.SceneServer;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The chat rate gate (hot-path audit L2): the per-sender bucket is charged at the door, and the incoming queue's cap
	/// drops rather than kicks. The cross-server pump's tests are the Chat module's (ChatPumpTests).
	/// </summary>
	[TestFixture]
	public class ChatRateGateTests
	{
		private const string ChatDirectory = "Assets/FishMMO/Server/Implementation/World/SceneServer/Chat";
		private static readonly DateTime T0 = new DateTime(2026, 9, 25, 12, 0, 0, DateTimeKind.Utc);

		// ── The rate gate (L2) ────────────────────────────────────────────────

		private static ChatRateGate.State Fresh(long now) => new ChatRateGate.State { IsFull = true, LastRefillTicks = now };

		[Test]
		public void TheBucketAdmitsItsBurstThenRefillsAtItsRate()
		{
			long now = T0.Ticks;
			ChatRateGate.State state = Fresh(now);
			for (int i = 0; i < 5; ++i)
			{
				LogAssert.IsTrue(ChatRateGate.TryCharge(ref state, now, 5, 1.0, 0.0), $"message {i + 1} of the burst of 5");
			}
			LogAssert.IsFalse(ChatRateGate.TryCharge(ref state, now, 5, 1.0, 0.0), "the sixth in the same instant is refused");

			now += TimeSpan.TicksPerSecond * 2;
			LogAssert.IsTrue(ChatRateGate.TryCharge(ref state, now, 5, 1.0, 0.0), "two seconds refill two tokens: one");
			LogAssert.IsTrue(ChatRateGate.TryCharge(ref state, now, 5, 1.0, 0.0), "two");
			LogAssert.IsFalse(ChatRateGate.TryCharge(ref state, now, 5, 1.0, 0.0), "and no third");
		}

		[Test]
		public void ABucketMarkedFullIsFullOnTheFirstMessageEvenInTheSameTick()
		{
			long now = T0.Ticks;
			ChatRateGate.State state = Fresh(now);
			LogAssert.IsTrue(ChatRateGate.TryCharge(ref state, now, 5, 1.0, 0.0), "a new character's first message is not refused against an empty bucket");
			LogAssert.IsFalse(state.IsFull, "the flag is spent");
			LogAssert.IsTrue(Math.Abs(state.Tokens - 4.0) < 1e-9, "five tokens, one spent");
		}

		[Test]
		public void TheMinimumGapRefusesEvenWithTokensLeft()
		{
			long now = T0.Ticks;
			ChatRateGate.State state = Fresh(now);
			LogAssert.IsTrue(ChatRateGate.TryCharge(ref state, now, 5, 1.0, 500.0), "first");
			LogAssert.IsFalse(ChatRateGate.TryCharge(ref state, now + TimeSpan.TicksPerMillisecond * 100, 5, 1.0, 500.0), "100 ms later: inside the gap");
			LogAssert.IsTrue(ChatRateGate.TryCharge(ref state, now + TimeSpan.TicksPerMillisecond * 600, 5, 1.0, 500.0), "600 ms later: past it");
		}

		// ── Source pins, each with its control run ────────────────────────────

		[Test]
		public void TheRateIsChargedBeforeQueueingAndTheQueueCapNeverKicks()
		{
			string code = SourceScanPins.ReadCode(ChatDirectory + "/ChatSystem.cs");
			Func<string, string> check = c =>
			{
				string receive = SourceScanPins.Body(c, "private void OnServerChatBroadcastReceived(");
				string order = SourceScanPins.InOrder(receive, "TryChargeChatRate(", "IncrementIncomingQueueSize()", "IncomingChatQueue.Enqueue(");
				if (order != null)
				{
					return order;
				}
				if (receive.Contains(".Kick("))
				{
					return "the incoming queue kicks a connection again";
				}
				string process = SourceScanPins.Body(c, "private void ProcessNewChatMessage(");
				if (process == null)
				{
					return "ProcessNewChatMessage is missing";
				}
				return process.Contains("TryChargeChatRate(") || process.Contains(".Tokens") ? "the bucket is charged a second time when the queue drains" : null;
			};
			SourceScanPins.HoldsAndFires("L2 enqueue", code, check,
				SourceScanPins.InsertBefore("RecordIncomingOverflow(rateTicks);", "conn.Kick(FishNet.Managing.Server.KickReason.ExploitExcessiveData);"),
				"kicking at the global cap");
		}
	}
}
