using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using FishMMO.Database.Data;
using FishMMO.Server.Implementation.World.SceneServer;
using FishMMO.Shared;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The party's half of the social update pump (moved from SocialUpdatePumpTests with the party): the
	/// per-recipient buff ledger, the buff signature, and the pins on the party pump, vitals and panel.
	/// </summary>
	[TestFixture]
	public class PartyUpdatePumpTests
	{
		private const string PartySystemPath = "Assets/FishMMO/Modules/Social/Server/PartySystem.cs";
		private const string PartyPanelPath = "Assets/FishMMO/Modules/Social/Client/UITKParty.cs";

		#region ObservedBuffDeliveryLedger

		[Test]
		public void Ledger_SendsToAnAudienceThatHasNotHadTheSet_ThenStops()
		{
			var ledger = new ObservedBuffDeliveryLedger();
			var audience = new List<long> { 1, 2 };

			LogAssert.IsTrue(ledger.NeedsSend(audience, 1, 42), "nobody has been sent anything yet");
			ledger.MarkSent(audience, 1, 42);
			LogAssert.IsFalse(ledger.NeedsSend(audience, 1, 42), "an unchanged set is omitted once everybody holds it");
			LogAssert.IsTrue(ledger.NeedsSend(audience, 1, 43), "a changed set goes out");
		}

		[Test]
		public void Ledger_AJoiner_IsSentAnUnchangedPermanentBuff()
		{
			/* L1: the record was keyed by the member described, so somebody joining the group after
			 * a permanent buff was last sent never received it. The ledger asks about each
			 * recipient, so the joiner's absence from the record sends the array. */
			var ledger = new ObservedBuffDeliveryLedger();
			ledger.MarkSent(new List<long> { 1, 2 }, 1, 42);

			LogAssert.IsTrue(ledger.NeedsSend(new List<long> { 1, 2, 3 }, 1, 42),
				"recipient 3 has never been sent member 1's set");

			ledger.MarkSent(new List<long> { 1, 2, 3 }, 1, 42);
			LogAssert.IsFalse(ledger.NeedsSend(new List<long> { 1, 2, 3 }, 1, 42));
		}

		[Test]
		public void Ledger_ARecipientWhoMissedAChange_IsSentItOnReturn()
		{
			var ledger = new ObservedBuffDeliveryLedger();
			ledger.MarkSent(new List<long> { 1, 2 }, 1, 42);

			// Recipient 2 is in another scene when member 1's buffs change.
			ledger.MarkSent(new List<long> { 1 }, 1, 43);

			LogAssert.IsTrue(ledger.NeedsSend(new List<long> { 1, 2 }, 1, 43),
				"recipient 2 still holds the old set");
		}

		[Test]
		public void Ledger_ForgettingARecipient_ResendsEverythingToThem_AndOnlyThem()
		{
			var ledger = new ObservedBuffDeliveryLedger();
			ledger.MarkSent(new List<long> { 1, 2 }, 1, 42);
			ledger.MarkSent(new List<long> { 1, 2 }, 2, 7);

			ledger.ForgetRecipient(2);

			LogAssert.IsTrue(ledger.NeedsSend(new List<long> { 2 }, 1, 42), "a forgotten client holds nothing");
			LogAssert.IsTrue(ledger.NeedsSend(new List<long> { 2 }, 2, 7));
			LogAssert.IsFalse(ledger.NeedsSend(new List<long> { 1 }, 1, 42), "other recipients are unaffected");
			LogAssert.AreEqual(1, ledger.RecipientCount);

			ledger.ForgetRecipient(99);
			LogAssert.AreEqual(1, ledger.RecipientCount, "forgetting a stranger is a no-op");

			ledger.Clear();
			LogAssert.AreEqual(0, ledger.RecipientCount);
			LogAssert.IsTrue(ledger.NeedsSend(new List<long> { 1 }, 1, 42));
		}

		[Test]
		public void Ledger_AnEmptyAudience_NeverNeedsASend()
		{
			var ledger = new ObservedBuffDeliveryLedger();
			LogAssert.IsFalse(ledger.NeedsSend(new List<long>(), 1, 42));
			LogAssert.IsFalse(ledger.NeedsSend(null, 1, 42));
		}

		#endregion

		#region Buff signature (L1)

		private static ObservedBuffEntry Buff(int template, int stacks, float remaining)
		{
			return new ObservedBuffEntry() { TemplateID = template, Stacks = stacks, RemainingSeconds = remaining, TotalSeconds = 30.0f };
		}

		[Test]
		public void BuffSignature_IgnoresTheCountdown_AndSeesARefresh()
		{
			var set = new List<ObservedBuffEntry>() { Buff(5, 1, 20.0f), Buff(9, 2, 0.0f) };
			var later = new List<ObservedBuffEntry>() { Buff(5, 1, 11.5f), Buff(9, 2, 0.0f) };
			var expiry = new List<long>() { 1000, -1 };

			int signature = PartySystem.ComputeObservedBuffSignature(set, expiry);
			LogAssert.AreEqual(signature, PartySystem.ComputeObservedBuffSignature(later, expiry),
				"a buff counting down is unchanged: the client counts it down itself (L1)");
			LogAssert.AreNotEqual(signature, PartySystem.ComputeObservedBuffSignature(set, new List<long>() { 1010, -1 }),
				"a refresh moves the expiry and is sent");
			LogAssert.AreNotEqual(signature, PartySystem.ComputeObservedBuffSignature(new List<ObservedBuffEntry>() { Buff(5, 2, 20.0f), Buff(9, 2, 0.0f) }, expiry),
				"a stack change is sent");
			LogAssert.AreEqual(0, PartySystem.ComputeObservedBuffSignature(new List<ObservedBuffEntry>(), new List<long>()));
			LogAssert.AreNotEqual(0, signature, "a real set never collides with the empty one");
		}

		[Test]
		public void BuffExpirySecond_IsWholeSecondsOfTheExpiryTick()
		{
			LogAssert.AreEqual(-1L, PartySystem.BuffExpirySecond(0u, 1.0 / 30.0), "a permanent buff has no expiry");
			LogAssert.AreEqual(100L, PartySystem.BuffExpirySecond(3000u, 1.0 / 30.0));
			LogAssert.AreEqual(100L, PartySystem.BuffExpirySecond(3029u, 1.0 / 30.0), "whole seconds");
			LogAssert.AreEqual(101L, PartySystem.BuffExpirySecond(3030u, 1.0 / 30.0));
			LogAssert.AreEqual(143165576L, PartySystem.BuffExpirySecond(4294967295u, 1.0 / 30.0),
				"double precision: a float step would lose whole seconds at large ticks");
			LogAssert.AreEqual(3000L, PartySystem.BuffExpirySecond(3000u, 0.0), "an unknown tick rate hashes the tick itself");
		}

		#endregion

		#region Pump pins

		[Test]
		public void PartyPanel_RebasesItsCountdown_OnlyWhenAnArrayArrives()
		{
			string source = CodeOnly(ReadSource(PartyPanelPath));
			int branch = source.IndexOf("if (entry.BuffsChanged)", StringComparison.Ordinal);
			LogAssert.IsTrue(branch >= 0, "the array branch must still exist");

			string arrayBranch = MethodBody(source.Substring(branch), "if (entry.BuffsChanged)");
			LogAssert.IsTrue(arrayBranch.Contains("model.BuffsReceivedTime = now;"),
				"the countdown's starting point moves with the array (L1)");
			LogAssert.AreEqual(1, Occurrences(source, "model.BuffsReceivedTime = now;"),
				"and nowhere else: stamping it on every payload snaps timed icons back each second");
		}

		[Test]
		public void PartyPump_ReadsRostersAndOnlineMembersInBulk()
		{
			string source = ReadSource(PartySystemPath);
			string pump = CodeOnly(MethodBody(source, "private async Task FetchAndProcessPartyUpdatesAsync("));
			string absent = CodeOnly(MethodBody(source, "private async Task<PartyLeadershipRepair> RepairAbsentLeaderAsync("));
			string audit = CodeOnly(MethodBody(source, "private async Task AuditPartyLeadershipAsync("));

			LogAssert.IsTrue(pump.Contains("charPartyService.FetchManyAsync(pendingIDs)"), "one roster query per pump (M15)");
			LogAssert.IsFalse(pump.Contains("charPartyService.FetchManyAsync(update.PartyID)"), "no per-party roster read in the pump (M15)");
			LogAssert.IsTrue(pump.Contains("FetchOnlineMembersForLeadershipAsync("), "one online query per pump (M15)");
			LogAssert.IsFalse(absent.Contains("FetchOnlineMemberIdsAsync("),
				"the absent-leader repair is handed the online set; it must not query per party (M15)");
			LogAssert.IsTrue(audit.Contains("charPartyService.FetchManyAsync(ids)"), "the audit sweep reads in bulk too");
		}

		[Test]
		public void PartyVitals_AreOneMulticastPerSceneGroup_WithAPerRecipientBuffLedger()
		{
			string body = CodeOnly(MethodBody(ReadSource(PartySystemPath), "private void BroadcastSceneGroupVitals("));

			LogAssert.IsTrue(body.Contains("Server.NetworkWrapper.Broadcast(vitalsRecipients,"), "one multicast per scene group");
			LogAssert.IsFalse(body.Contains("Broadcast(members[i].Owner"), "no per-member unicast of the same payload");
			LogAssert.IsTrue(body.Contains("observedBuffLedger.NeedsSend(vitalsRecipientIDs"),
				"whether an array may be omitted is a question about each recipient (L1)");
		}

		#endregion

		#region Helpers

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(ProjectFiles.Exists(path), $"{relativePath} not found at {path}.");
			return ProjectFiles.ReadAllText(path).Replace("\r\n", "\n");
		}

		/// <summary>The source with comment lines dropped, so a scan sees code and not prose.</summary>
		private static string CodeOnly(string source)
		{
			string[] lines = source.Split('\n');
			StringBuilder code = new StringBuilder(source.Length);

			for (int i = 0; i < lines.Length; ++i)
			{
				string trimmed = lines[i].TrimStart();
				if (trimmed.StartsWith("//", StringComparison.Ordinal) ||
					trimmed.StartsWith("/*", StringComparison.Ordinal) ||
					trimmed.StartsWith("*", StringComparison.Ordinal))
				{
					continue;
				}

				code.Append(lines[i]).Append('\n');
			}

			return code.ToString();
		}

		private static int Occurrences(string source, string needle)
		{
			int count = 0;
			for (int i = source.IndexOf(needle, StringComparison.Ordinal); i >= 0; i = source.IndexOf(needle, i + needle.Length, StringComparison.Ordinal))
			{
				++count;
			}
			return count;
		}

		/// <summary>A method's body, brace-matched from the first brace after its signature.</summary>
		private static string MethodBody(string source, string signature)
		{
			int start = source.IndexOf(signature, StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, $"the source must still declare {signature}");

			int open = source.IndexOf('{', start);
			LogAssert.IsTrue(open > start, $"{signature} must have a body");

			int depth = 0;
			for (int i = open; i < source.Length; ++i)
			{
				if (source[i] == '{')
				{
					++depth;
				}
				else if (source[i] == '}')
				{
					--depth;
					if (depth == 0)
					{
						return source.Substring(open, i - open + 1);
					}
				}
			}

			LogAssert.Fail($"{signature}: unbalanced braces");
			return string.Empty;
		}

		#endregion
	}
}
