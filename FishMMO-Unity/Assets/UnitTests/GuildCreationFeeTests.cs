using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs for the optional guild creation fee (issue #186).
	/// </summary>
	/// <remarks>
	/// <para>
	/// The fee is a currency attribute charged before the asynchronous create starts, and the
	/// thing that has to hold is that a fee which bought no guild comes back. So the sweep here
	/// is over the create path itself: every way <c>CreateGuildAsync</c> can stop short of a
	/// guild must answer through the one helper that refunds before it replies, and the one
	/// place that does not refund must be the place a guild now exists.
	/// </para>
	/// <para>
	/// The wire values are pinned because both are on the wire: a result code the client maps
	/// to a message, and a ledger reason stored as an integer column.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class GuildCreationFeeTests
	{
		private const string ServerPath =
			"Assets/Scripts/Server/Implementation/World/SceneServer/Guild/GuildSystem.cs";
		private const string ClientPath =
			"Assets/Scripts/Client/GUI/World/Guild/UITKGuild.cs";

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path);
		}

		private static string Between(string source, string start, string end, string what)
		{
			int s = source.IndexOf(start, StringComparison.Ordinal);
			LogAssert.IsTrue(s >= 0, $"{what}: must still contain '{start}'");
			int e = source.IndexOf(end, s, StringComparison.Ordinal);
			LogAssert.IsTrue(e > s, $"{what}: the end marker '{end}' must follow");
			return source.Substring(s, e - s);
		}

		[Test]
		public void TheWireValuesAreAppendedNotInserted()
		{
			LogAssert.AreEqual(21, (int)GuildResultType.InsufficientFunds, "InsufficientFunds is appended after Failed = 20");
			LogAssert.AreEqual(8, (int)CurrencyMovementReason.GuildCreation, "GuildCreation is appended after PlayerTrade = 7");
		}

		[Test]
		public void TheFeeIsTakenBeforeTheAsyncCreateStarts()
		{
			/* Charging on success would leave a window between the check and the charge in which
			 * the same balance can be spent at a merchant, so the guild would exist unpaid. */
			string handler = Between(ReadSource(ServerPath),
				"public void OnServerGuildCreateBroadcastReceived", "#region Creation Fee", "create handler");

			int afford = handler.IndexOf("CharacterCurrency.CanAfford(player, guildCreationFeeCurrency", StringComparison.Ordinal);
			int spend = handler.IndexOf("CharacterCurrency.TrySpend(player, guildCreationFeeCurrency", StringComparison.Ordinal);
			int start = handler.IndexOf("TryEnqueueIngressWork(() => CreateGuildAsync(", StringComparison.Ordinal);

			LogAssert.IsTrue(afford >= 0 && spend >= 0 && start >= 0, "the handler checks, charges, and then starts the create");
			LogAssert.IsTrue(afford < spend && spend < start, "check, then charge, then start — in that order");
			LogAssert.IsTrue(handler.Contains("GuildResultType.InsufficientFunds"), "a player who cannot pay is told so, specifically");
			LogAssert.IsTrue(handler.Contains("RefundCreationFee(characterID, feeCharged);\n\t\t\t\t\tSendServerBusy(conn);") ||
							 handler.Contains("RefundCreationFee(characterID, feeCharged);\r\n\t\t\t\t\tSendServerBusy(conn);"),
				"a create that could not even be queued refunds before reporting busy");
		}

		[Test]
		public void EveryWayTheCreateStopsShortRefunds_AndOnlyAFoundedGuildAbsorbsTheFee()
		{
			string create = Between(ReadSource(ServerPath),
				"private async Task CreateGuildAsync(", "public void OnServerGuildInviteBroadcastReceived", "CreateGuildAsync");

			/* The body before the point the guild exists. Every `return;` in it must be
			 * immediately preceded by the refund-and-reply helper. */
			int founded = create.IndexOf("absorbed: true", StringComparison.Ordinal);
			LogAssert.IsTrue(founded > 0, "the success path records the fee as absorbed");
			string beforeFounded = create.Substring(0, founded);

			MatchCollection returns = Regex.Matches(beforeFounded, @"\n[ \t]*return;");
			LogAssert.IsTrue(returns.Count >= 5, $"the create has several early exits before the guild exists (found {returns.Count})");
			foreach (Match r in returns)
			{
				string preceding = beforeFounded.Substring(Math.Max(0, r.Index - 160), Math.Min(160, r.Index));
				LogAssert.IsTrue(preceding.Contains("FailCreate(conn, characterID, feeCharged,"),
					"each early exit must refund and reply through FailCreate; found one that does not:\n" + preceding);
			}

			LogAssert.IsTrue(create.Contains("if (!guildExists)"), "the catch refunds only when no guild was founded");
			LogAssert.AreEqual(1, Regex.Matches(create, "absorbed: true").Count, "the fee is absorbed in exactly one place");
		}

		[Test]
		public void TheRefundDoesNotDependOnTheConnection()
		{
			/* A player who disconnected while the create was in flight is still owed the money. */
			string fail = Between(ReadSource(ServerPath), "private void FailCreate(", "private void RecordCurrencyMovement(", "FailCreate");

			int refund = fail.IndexOf("RefundCreationFee(characterID, feeCharged);", StringComparison.Ordinal);
			int connCheck = fail.IndexOf("conn.IsActive", StringComparison.Ordinal);
			LogAssert.IsTrue(refund >= 0 && connCheck >= 0 && refund < connCheck, "the refund runs before the connection is consulted");

			string refundBody = Between(ReadSource(ServerPath), "private void RefundCreationFee(", "private void FailCreate(", "RefundCreationFee");
			LogAssert.IsTrue(refundBody.Contains("CharactersByID.TryGetValue(characterID"), "the character is resolved by ID, not by connection");
			LogAssert.IsTrue(refundBody.Contains("absorbed: false"), "a refund is recorded as Returned in the ledger");
		}

		[Test]
		public void TheFeePersistBumpsAndMarksTogether()
		{
			/* A version bump without the pending mark leaves the attribute dirty for the rest of
			 * the session — see CharacterInventorySystem.BuildAttributeDataList. */
			string body = Between(ReadSource(ServerPath), "private bool TryPersistCreationFeeCurrency(", "private async Task PersistCreationFeeCurrencyToDbAsync(", "persist");
			LogAssert.IsTrue(body.Contains("currency.Version++;"), "the version is bumped");
			LogAssert.IsTrue(body.Contains("currency.MarkPersistPending(currency.Version);"), "and the pending mark is stamped with it");
		}

		[Test]
		public void TheClientSaysThePriceAndMapsTheRefusal()
		{
			string client = ReadSource(ClientPath);
			LogAssert.IsTrue(client.Contains("case GuildResultType.InsufficientFunds:"), "the refusal has a message");
			LogAssert.IsTrue(client.Contains("OnReceiveGuildCreationCost +="), "the panel listens for the fee");
			LogAssert.IsTrue(client.Contains("OnReceiveGuildCreationCost -="), "and stops listening when the character goes");

			string create = Between(client, "public void OnButtonCreateGuild()", "public void OnButtonLeaveGuild()", "create button");
			LogAssert.IsTrue(create.Contains("CharacterCurrency.CanAfford(Character, currency, guildController.CreationCost)"),
				"an unaffordable create is refused before a name is asked for");
			LogAssert.IsTrue(create.Contains("Founding a guild costs"), "the prompt states the price");
		}
	}
}
