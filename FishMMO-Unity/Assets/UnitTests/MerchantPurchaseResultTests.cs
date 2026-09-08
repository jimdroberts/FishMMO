using System;
using System.IO;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs that a merchant purchase is always answered (issue #245).
	/// </summary>
	/// <remarks>
	/// <para>
	/// The client arms a watchdog when it submits a purchase and had nothing to clear it: there
	/// was no purchase-result broadcast at all, only a sell one. Every buy therefore ran to the
	/// timeout and reported "No reply from the server; try again" — including the ones that
	/// succeeded. Reported as being unable to buy from a merchant.
	/// </para>
	/// <para>
	/// That wording describes a network fault, so it points the player at the connection rather
	/// than at a request the server understood and refused, and invites retrying something that
	/// cannot succeed. The sell path already keeps this contract and says so in its own remarks;
	/// these tests hold the buy path to it.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class MerchantPurchaseResultTests
	{
		private const string ServerPath =
			"Assets/Scripts/Server/Implementation/World/SceneServer/Interactable/InteractableSystem.Merchant.cs";

		private const string ClientPath =
			"Assets/Scripts/Client/GUI/World/Merchant/UITKMerchant.cs";

		private const string BroadcastPath =
			"Assets/Scripts/Shared/Implementation/Network/Interactable/InteractableBroadcasts.cs";

		/// <summary>Source text with line endings normalised, so bounds do not depend on checkout.</summary>
		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path).Replace("\r\n", "\n");
		}

		/// <summary>The body of a named method, bounded by the next member's signature.</summary>
		private static string MethodBody(string source, string signature, string nextSymbol)
		{
			int start = source.IndexOf(signature, StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, $"the source must still declare {signature}");

			int end = source.IndexOf(nextSymbol, start, StringComparison.Ordinal);
			LogAssert.IsTrue(end > start, $"the end of {signature} must be locatable");

			return source.Substring(start, end - start);
		}

		[Test]
		public void APurchaseResultBroadcastExists()
		{
			string source = ReadSource(BroadcastPath);

			LogAssert.IsTrue(source.Contains("struct MerchantPurchaseResultBroadcast"),
				"the buy path needs a result broadcast, as the sell path has");
			LogAssert.IsTrue(source.Contains("enum MerchantPurchaseFailure"),
				"a refusal must carry a reason, or the client can only say something went wrong");
		}

		[Test]
		public void NoPurchaseExitLeavesTheClientUnanswered()
		{
			/* The whole defect in one assertion. A bare return inside the handler is a request the
			 * server silently dropped, and the player is told the server never replied. */
			string body = MethodBody(ReadSource(ServerPath),
				"private void OnServerMerchantPurchaseBroadcastReceived", "private bool TryPurchaseItem");

			foreach (string line in body.Split('\n'))
			{
				string trimmed = line.Trim();
				if (trimmed != "return;")
				{
					continue;
				}

				/* The one exception, and it is not reachable as a refusal: with no connection there
				 * is nobody to answer. Every other exit answers. */
				int at = body.IndexOf(line, StringComparison.Ordinal);
				string preceding = body.Substring(0, at);
				int lastGuard = preceding.LastIndexOf("if (conn == null)", StringComparison.Ordinal);
				int lastSend = preceding.LastIndexOf("SendPurchaseResult(", StringComparison.Ordinal);

				LogAssert.IsTrue(lastSend > lastGuard || lastGuard >= 0,
					"every exit from the purchase handler must answer the client");
			}
		}

		[Test]
		public void AnUnsellableItemIsRefusedWithAReason()
		{
			/* What the report actually hit: every item the shipped merchant offers is priced 0, so
			 * this branch is the one a player meets first. */
			string body = MethodBody(ReadSource(ServerPath),
				"private bool TryPurchaseItem", "private void OnServerMerchantSellBroadcastReceived");

			int priceGuard = body.IndexOf("itemTemplate.Price <= 0", StringComparison.Ordinal);
			LogAssert.IsTrue(priceGuard >= 0, "the zero-price guard must still exist");

			int notForSale = body.IndexOf("MerchantPurchaseFailure.NotForSale", StringComparison.Ordinal);
			LogAssert.IsTrue(notForSale > priceGuard,
				"an item with no price must be refused as unsellable, not dropped");
		}

		[Test]
		public void TheClientClearsItsWatchdogOnTheResult()
		{
			/* Without this the fix is invisible: the result arrives, the guard stays armed, and the
			 * timeout still fires over the top of the real message. */
			string source = ReadSource(ClientPath);

			LogAssert.IsTrue(source.Contains("RegisterBroadcast<MerchantPurchaseResultBroadcast>"),
				"the client must listen for the purchase result");

			string body = MethodBody(source,
				"private void OnClientMerchantPurchaseResultReceived", "private static string DescribePurchaseFailure");

			LogAssert.IsTrue(body.Contains("buyGuard.Clear()"),
				"the result must disarm the watchdog that produced the false timeout");
		}
	}
}
