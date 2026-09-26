using System;
using NUnit.Framework;
using FishMMO.Server.Implementation.World.SceneServer;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The temporary mute for chat flooders (optional change O8).
	/// </summary>
	/// <remarks>
	/// Lines the per-sender rate gate refuses were dropped silently. Past a threshold within a window
	/// the sender is now muted for a few minutes and told why. The mute is held in memory by the chat
	/// system, apart from the stored moderation mute; these pin the rule and where it sits.
	/// </remarks>
	[TestFixture]
	public class ChatFloodMuteTests
	{
		private const string ChatDirectory = "Assets/Scripts/Server/Implementation/World/SceneServer/Chat";

		private const int Threshold = 10;
		private const double Window = 10.0;
		private const double Mute = 180.0;
		private const double T0 = 1000.0;

		private static ChatFloodMute.Outcome Refuse(ref ChatFloodMute.State state, double now) =>
			ChatFloodMute.RecordRefusal(ref state, now, Threshold, Window, Mute);

		// ── The rule ──────────────────────────────────────────────────────────

		[Test]
		public void TheThresholdWithinTheWindowMutes()
		{
			var state = new ChatFloodMute.State();
			for (int i = 0; i < Threshold - 1; ++i)
			{
				LogAssert.AreEqual(ChatFloodMute.Outcome.Counted, Refuse(ref state, T0 + i * 0.5), $"refusal {i + 1} is only counted");
				LogAssert.IsFalse(ChatFloodMute.IsMuted(state, T0 + i * 0.5));
			}

			double tenth = T0 + (Threshold - 1) * 0.5;
			LogAssert.AreEqual(ChatFloodMute.Outcome.Muted, Refuse(ref state, tenth), "the tenth inside ten seconds mutes");
			LogAssert.IsTrue(ChatFloodMute.IsMuted(state, tenth));
			LogAssert.IsTrue(ChatFloodMute.IsMuted(state, tenth + Mute - 0.001), "for the whole term");
			LogAssert.IsFalse(ChatFloodMute.IsMuted(state, tenth + Mute), "and not a moment past it");
			LogAssert.AreEqual(0, state.Refusals, "reaching the threshold empties the window");
		}

		[Test]
		public void RefusalsSpreadWiderThanTheWindowNeverMute()
		{
			var state = new ChatFloodMute.State();
			// One refusal a second and a bit: never ten inside any window opened by a refusal.
			for (int i = 0; i < 100; ++i)
			{
				LogAssert.AreNotEqual(ChatFloodMute.Outcome.Muted, Refuse(ref state, T0 + i * 1.2), $"refusal {i + 1}");
			}
			LogAssert.IsFalse(ChatFloodMute.IsMuted(state, T0 + 120.0), "an occasionally refused sender is never muted");
		}

		[Test]
		public void AWindowClosesAndTheNextRefusalOpensANewOne()
		{
			var state = new ChatFloodMute.State();
			for (int i = 0; i < Threshold - 1; ++i)
			{
				Refuse(ref state, T0);
			}
			LogAssert.AreEqual(ChatFloodMute.Outcome.Counted, Refuse(ref state, T0 + Window + 0.001),
				"nine refusals, then one after the window closed, start over at one");
			LogAssert.AreEqual(1, state.Refusals);
			LogAssert.AreEqual(T0 + Window + 0.001, state.WindowStartSeconds, "the new window opens at that refusal");
		}

		[Test]
		public void FloodingOnWhileMutedPushesTheEndOut()
		{
			var state = new ChatFloodMute.State();
			for (int i = 0; i < Threshold; ++i)
			{
				Refuse(ref state, T0);
			}
			double firstEnd = state.MutedUntilSeconds;
			LogAssert.AreEqual(T0 + Mute, firstEnd);

			double later = T0 + 60.0;
			ChatFloodMute.Outcome last = ChatFloodMute.Outcome.Counted;
			for (int i = 0; i < Threshold; ++i)
			{
				last = Refuse(ref state, later);
			}
			LogAssert.AreEqual(ChatFloodMute.Outcome.Extended, last, "a muted sender reaching it again is extended, not told again");
			LogAssert.AreEqual(later + Mute, state.MutedUntilSeconds, "the mute ends a full term after the flooding stops");
		}

		[Test]
		public void AnExtensionNeverShortensAMute()
		{
			var state = new ChatFloodMute.State { MutedUntilSeconds = T0 + 10_000.0 };
			for (int i = 0; i < Threshold; ++i)
			{
				Refuse(ref state, T0);
			}
			LogAssert.AreEqual(T0 + 10_000.0, state.MutedUntilSeconds, "a longer mute already running is kept");
		}

		[Test]
		public void ZeroSettingsDisableIt()
		{
			var state = new ChatFloodMute.State();
			LogAssert.AreEqual(ChatFloodMute.Outcome.Disabled, ChatFloodMute.RecordRefusal(ref state, T0, 0, Window, Mute), "no threshold");
			LogAssert.AreEqual(ChatFloodMute.Outcome.Disabled, ChatFloodMute.RecordRefusal(ref state, T0, Threshold, 0.0, Mute), "no window");
			LogAssert.AreEqual(ChatFloodMute.Outcome.Disabled, ChatFloodMute.RecordRefusal(ref state, T0, Threshold, Window, 0.0), "no duration");
			LogAssert.AreEqual(0, state.Refusals, "and nothing is recorded");
			for (int i = 0; i < 1000; ++i)
			{
				ChatFloodMute.RecordRefusal(ref state, T0, 1, Window, 0.0);
			}
			LogAssert.IsFalse(ChatFloodMute.IsMuted(state, T0), "a disabled flood mute never mutes");
		}

		[Test]
		public void AStateIsSpentOnceNoMuteRunsAndNoWindowIsOpen()
		{
			var state = new ChatFloodMute.State();
			LogAssert.IsTrue(ChatFloodMute.IsSpent(state, T0, Window), "a fresh state holds nothing");

			Refuse(ref state, T0);
			LogAssert.IsFalse(ChatFloodMute.IsSpent(state, T0 + 1.0, Window), "an open window is kept");
			LogAssert.IsTrue(ChatFloodMute.IsSpent(state, T0 + Window + 1.0, Window), "a closed one is not");

			for (int i = 0; i < Threshold; ++i)
			{
				Refuse(ref state, T0 + 100.0);
			}
			LogAssert.IsFalse(ChatFloodMute.IsSpent(state, T0 + 100.0 + Window + 1.0, Window), "a running mute is kept after its window closes");
			LogAssert.IsTrue(ChatFloodMute.IsSpent(state, T0 + 100.0 + Mute, Window), "and dropped once it ends");
		}

		[Test]
		public void ThePlayerIsToldWhyAndForHowLong()
		{
			string first = ChatFloodMute.DescribeForPlayer(T0 + Mute, T0);
			StringAssert.Contains("too quickly", first);
			StringAssert.Contains("3m", first, "the first line of a three-minute mute says three minutes, rounded up");
			StringAssert.Contains("/helpme", first, "and that commands still work");
			LogAssert.IsTrue(first.Length <= FishMMO.Shared.ChatBroadcast.MaxTextLength, "the client discards a longer line");

			StringAssert.Contains("5s", ChatFloodMute.DescribeForPlayer(T0 + 4.2, T0), "seconds round up too");
			StringAssert.Contains("1s", ChatFloodMute.DescribeForPlayer(T0, T0), "never zero");
		}

		// ── Source pins, each with its control run ────────────────────────────

		[Test]
		public void ARefusalAtTheGateIsCountedBeforeItIsDropped()
		{
			string code = SourceScanPins.ReadCode(ChatDirectory + "/ChatSystem.cs");
			Func<string, string> check = c =>
			{
				string receive = SourceScanPins.Body(c, "private void OnServerChatBroadcastReceived(");
				if (receive == null)
				{
					return "OnServerChatBroadcastReceived is missing";
				}
				string refused = SourceScanPins.Body(receive, "if (!TryChargeChatRate(");
				if (refused == null)
				{
					return "the refusal branch is missing";
				}
				return SourceScanPins.InOrder(refused, "RecordChatRateRefusal(conn, sender);", "return;");
			};
			SourceScanPins.HoldsAndFires("O8 count", code, check,
				SourceScanPins.Replace("RecordChatRateRefusal(conn, sender);", string.Empty),
				"a refusal dropped without being counted");
		}

		[Test]
		public void TheFloodMuteSilencesChatButNotCommands()
		{
			string code = SourceScanPins.ReadCode(ChatDirectory + "/ChatSystem.cs");
			Func<string, string> check = c => SourceScanPins.InOrder(SourceScanPins.Body(c, "private void ProcessNewChatMessage("),
				"ChatHelper.TryParseCommand(", "TryRefuseFloodMuted(conn, sender)", "ChatHelper.TryParseChatCommand(");
			SourceScanPins.HoldsAndFires("O8 order", code, check,
				SourceScanPins.Replace("if (TryRefuseFloodMuted(conn, sender))", "if (false)"),
				"a flood-muted sender chatting on");
		}

		[Test]
		public void TheFloodMuteWritesNoModerationRecord()
		{
			string code = SourceScanPins.ReadCode(ChatDirectory + "/ChatSystem.FloodMute.cs");
			Func<string, string> check = c =>
			{
				if (c.Contains("ChatMutedUntilTicks") || c.Contains("ChatMuteReason"))
				{
					return "the flood mute writes the stored-mute fields the moderation commands own";
				}
				if (c.Contains("Service") || c.Contains("EnqueuePersistence(") || c.Contains("TryEnqueueAsyncWork("))
				{
					return "the flood mute reaches the database";
				}
				return c.Contains("MonotonicClock.NowSeconds") ? null : "the flood mute is no longer timed on the monotonic clock";
			};
			SourceScanPins.HoldsAndFires("O8 in memory", code, check,
				SourceScanPins.InsertBefore("floodMuteStates[sender.ID] = state;", "sender.ChatMutedUntilTicks = long.MaxValue;\n"),
				"a flood mute written as a stored mute");
		}
	}
}
