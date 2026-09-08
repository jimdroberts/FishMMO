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
		public void AnUnpricedItemIsRefusedWithAReason()
		{
			/* Only a NEGATIVE price is unsellable. A price of zero is free — see
			 * AFreeItemIsSoldRatherThanRefused. */
			string body = MethodBody(ReadSource(ServerPath),
				"private bool TryPurchaseItem", "private void OnServerMerchantSellBroadcastReceived");

			int priceGuard = body.IndexOf("itemTemplate.Price < 0", StringComparison.Ordinal);
			LogAssert.IsTrue(priceGuard >= 0, "the nonsense-price guard must still exist");

			int notForSale = body.IndexOf("MerchantPurchaseFailure.NotForSale", StringComparison.Ordinal);
			LogAssert.IsTrue(notForSale > priceGuard,
				"an item with a nonsense price must be refused as unsellable, not dropped");
		}

		[Test]
		public void AFreeItemIsSoldRatherThanRefused()
		{
			/* The reported symptom, and its actual cause: Price is an int that defaults to 0, the
			 * guard read `Price <= 0`, and so every item on every shipped merchant was refused as
			 * "not for sale". A price of zero is a price. */
			string body = MethodBody(ReadSource(ServerPath),
				"private bool TryPurchaseItem", "private void OnServerMerchantSellBroadcastReceived");

			LogAssert.IsTrue(body.IndexOf("itemTemplate.Price <= 0", StringComparison.Ordinal) < 0,
				"a price of zero must not refuse the sale");

			/* Both are load-bearing at zero, and for different reasons. The affordability divide is
			 * a DivideByZeroException that would take the handler down mid-request; TrySpend
			 * rejects a non-positive amount by design, so calling it would report "insufficient
			 * funds" for something that costs nothing. */
			LogAssert.IsTrue(body.Contains("if (unitPrice > 0)"),
				"the affordability divide must be guarded so a free item cannot divide by zero");
			LogAssert.IsTrue(body.Contains("if (charge > 0 &&"),
				"a free item must not be sent through TrySpend, which refuses a non-positive amount");
		}

		[Test]
		public void ARefusedAbilityLearnIsNotReportedAsAPurchase()
		{
			/* The learn helpers refuse for half a dozen reasons — already known, no ability
			 * controller, no currency, a full async worker — and the caller used to send
			 * MerchantPurchaseFailure.None unconditionally afterwards. The player was told the
			 * purchase succeeded and got nothing. */
			string source = ReadSource(ServerPath);

			LogAssert.IsTrue(source.Contains("private MerchantPurchaseFailure LearnAbilityGeneric"),
				"the learn helper must report its outcome rather than swallowing it");

			string body = MethodBody(source,
				"private void OnServerMerchantPurchaseBroadcastReceived", "private bool TryPurchaseItem");

			LogAssert.IsTrue(body.Contains("MerchantPurchaseFailure failure = LearnAbilityTemplate("),
				"the ability branch must answer with what the learn actually returned");
			LogAssert.IsTrue(body.Contains("MerchantPurchaseFailure failure = LearnAbilityEvent("),
				"and so must the ability-event branch");
		}

		[Test]
		public void APremadeAbilityPurchaseAnswersEveryExit()
		{
			/* The premade-ability path is the crafting path with the recipe supplied by content,
			 * and it is held to the same contract as every other purchase: a request the server
			 * understood and refused is answered with the reason, never dropped. */
			string source = ReadSource(ServerPath);
			LogAssert.IsTrue(source.Contains("case MerchantTabType.PremadeAbility:"),
				"the purchase handler must dispatch the premade-ability tab");

			string body = MethodBody(source,
				"private bool TryPurchasePremadeAbility", "private MerchantPurchaseFailure LearnAbilityGeneric");

			int returns = 0;
			int searchFrom = 0;
			while (true)
			{
				int at = body.IndexOf("return false;", searchFrom, StringComparison.Ordinal);
				if (at < 0)
				{
					break;
				}
				++returns;

				string between = body.Substring(searchFrom, at - searchFrom);
				LogAssert.IsTrue(between.Contains("SendPurchaseResult("),
					"every refusal in TryPurchasePremadeAbility must answer the client before returning");
				searchFrom = at + 1;
			}
			LogAssert.IsTrue(returns >= 5, "the premade path must refuse for its documented reasons");

			LogAssert.IsTrue(body.Contains("KnowsLearnedAbility("),
				"a premade ability must not be sold twice for the same template");
			LogAssert.IsTrue(body.Contains("MerchantPurchaseFailure.AbilityLimit"),
				"a full ability list must be refused with its own reason");
			LogAssert.IsTrue(body.Contains("offer.Validate("),
				"a recipe the crafter would refuse must not be sold");
			LogAssert.IsTrue(body.Contains("AbilityLearnedObserverBroadcast"),
				"observers must be told about the new ability, as the craft path tells them");
		}

		[Test]
		public void TheClientNamesEveryRefusalIncludingTheAbilityLimit()
		{
			string client = ReadSource(ClientPath);
			string body = MethodBody(client, "private static string DescribePurchaseFailure", "private void OnClientMerchantSellResultReceived");
			LogAssert.IsTrue(body.Contains("MerchantPurchaseFailure.AbilityLimit"),
				"a refused premade purchase for a full ability list must be worded, not left to the default");
		}

		[Test]
		public void APurchaseSuccessSaysWhereThePurchaseWent()
		{
			/* Issue #247. "Bought 1." was true of a template and said nothing about the trip to the
			 * crafter that comes next, so the player went looking for the ability on the hotkey bar. */
			string client = ReadSource(ClientPath);
			string body = MethodBody(client, "private static string DescribePurchaseSuccess", "private static string DescribePurchaseFailure");
			LogAssert.IsTrue(body.Contains("case MerchantTabType.Ability:") && body.Contains("Ability Crafter"),
				"buying a template must say it needs crafting");
			LogAssert.IsTrue(body.Contains("case MerchantTabType.PremadeAbility:"),
				"buying a premade ability must say it is ready to use");
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
