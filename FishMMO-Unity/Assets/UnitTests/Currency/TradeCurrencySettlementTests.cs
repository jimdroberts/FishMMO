using System;
using System.IO;
using System.Reflection;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Server.Implementation.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs for the currency half of a trade's commit: credits applied at the apply and held,
	/// exact reversal on a refusal, and the save path leaving a settling attribute to the trade.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The defect: the payee's currency row was written by the exchange as "memory plus the credit"
	/// while memory received the credit only after the commit, so any capture of that attribute in
	/// the window — a periodic save, another batch's full sheet — carried a newer version without it
	/// and overwrote the credited row. Items moved, the seller's coin was missing from the database
	/// until the next save, and a crash in that interval lost it.
	/// </para>
	/// <para>
	/// The invariants pinned here: after the apply, memory equals what the transaction writes (so no
	/// capture can lack the credit); nothing can spend a held credit (so a refusal takes back exactly
	/// what it gave); a refusal restores both balances exactly; and a late close can never touch an
	/// attribute that has been reset for another occupant of the pooled object.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class TradeCurrencySettlementTests
	{
		private const string SavingPath = "Assets/Scripts/Server/Implementation/World/SceneServer/Character/CharacterSystem.Saving.cs";
		private const string CommitPath = "Assets/Scripts/Server/Implementation/World/SceneServer/Trade/TradeSystem.Commit.cs";
		private const string InventoryPath = "Assets/Scripts/Server/Implementation/World/SceneServer/CharacterInventory/CharacterInventorySystem.cs";

		private GameObject gameObject;
		private CharacterAttributeController controller;
		private CharacterAttributeTemplate template;

		[SetUp]
		public void SetUp()
		{
			template = ScriptableObject.CreateInstance<CharacterAttributeTemplate>();
			template.name = "TradeSettlementCurrency";
			template.AddToCache(template.name);
			gameObject = new GameObject("TradeCurrencySettlementTests");
			controller = gameObject.AddComponent<CharacterAttributeController>();
		}

		[TearDown]
		public void TearDown()
		{
			if (template != null)
			{
				template.RemoveFromCache();
				Object.DestroyImmediate(template);
			}
			if (gameObject != null)
			{
				Object.DestroyImmediate(gameObject);
			}
		}

		/// <summary>A currency attribute with a starting balance. Two of these stand for the two parties.</summary>
		private CharacterAttribute Currency(int balance)
		{
			return new CharacterAttribute(controller, template.ID, balance, 0);
		}

		#region The admission rule

		[Test]
		public void CheckOpen_NoCurrencyMoving_NeedsNothing()
		{
			LogAssert.AreEqual(TradeCurrencySettlement.Refusal.None,
				TradeCurrencySettlement.CheckOpen(false, 0, 0, false, false, 0, 0, false, 0, 0),
				"an items-only trade needs no currency attribute at all");
		}

		[Test]
		public void CheckOpen_RefusesWhatCannotBeSettledExactly()
		{
			LogAssert.AreEqual(TradeCurrencySettlement.Refusal.InvalidAmount,
				TradeCurrencySettlement.CheckOpen(true, 100, 0, false, true, 100, 0, false, -1, 0), "a negative payment");
			LogAssert.AreEqual(TradeCurrencySettlement.Refusal.InvalidAmount,
				TradeCurrencySettlement.CheckOpen(true, 100, 0, false, true, 100, 0, false, (long)int.MaxValue + 1, 0), "a payment no balance can hold");
			LogAssert.AreEqual(TradeCurrencySettlement.Refusal.NoCurrency,
				TradeCurrencySettlement.CheckOpen(true, 100, 0, false, false, 0, 0, false, 10, 0), "a payee with nowhere to put it");
			LogAssert.AreEqual(TradeCurrencySettlement.Refusal.CannotPay,
				TradeCurrencySettlement.CheckOpen(true, 100, 0, false, true, 0, 0, false, 101, 0), "a payer short of the amount");
			LogAssert.AreEqual(TradeCurrencySettlement.Refusal.CannotPay,
				TradeCurrencySettlement.CheckOpen(true, 100, 60, false, true, 0, 0, false, 50, 0),
				"a payer who could only pay out of a credit another settlement is holding");
			LogAssert.AreEqual(TradeCurrencySettlement.Refusal.WouldOverflow,
				TradeCurrencySettlement.CheckOpen(true, 100, 0, false, true, int.MaxValue - 5, 0, false, 10, 0), "a payee at the int ceiling");
			LogAssert.AreEqual(TradeCurrencySettlement.Refusal.AlreadySettling,
				TradeCurrencySettlement.CheckOpen(true, 100, 0, true, true, 100, 0, false, 10, 0), "an attribute another transaction is settling");
			LogAssert.AreEqual(TradeCurrencySettlement.Refusal.None,
				TradeCurrencySettlement.CheckOpen(true, 100, 0, false, true, 100, 0, false, 100, 100), "each side can pay everything it holds");
		}

		[Test]
		public void TryOpen_ARefusal_ChangesNothing()
		{
			CharacterAttribute first = Currency(10);
			CharacterAttribute second = Currency(10);

			LogAssert.IsFalse(TradeCurrencySettlement.TryOpen(first, second, 11, 0, out TradeCurrencySettlement settlement, out var refusal),
				"the first side cannot pay 11");
			LogAssert.AreEqual(TradeCurrencySettlement.Refusal.CannotPay, refusal, "and says why");
			LogAssert.IsNull(settlement, "no settlement is handed back");
			LogAssert.AreEqual(10, first.Value, "the payer is untouched");
			LogAssert.AreEqual(10, second.Value, "the payee is untouched");
			LogAssert.IsFalse(first.IsSettling || second.IsSettling, "and nothing was left settling");
		}

		[Test]
		public void TryOpen_AnItemsOnlyTrade_OpensNothing()
		{
			LogAssert.IsTrue(TradeCurrencySettlement.TryOpen(null, null, 0, 0, out TradeCurrencySettlement settlement, out _),
				"no currency moving is not a refusal");
			LogAssert.IsNull(settlement, "and there is nothing to close afterwards");
		}

		#endregion

		#region Apply: memory equals what the transaction writes

		[Test]
		public void TryOpen_CreditsThePayeeAtOnce_SoEveryCaptureCarriesIt()
		{
			CharacterAttribute buyer = Currency(500);
			CharacterAttribute seller = Currency(40);

			LogAssert.IsTrue(TradeCurrencySettlement.TryOpen(buyer, seller, 300, 0, out TradeCurrencySettlement settlement, out _), "opens");

			/* The defect was a credit that reached memory only after the commit. Every capture reads
			 * Value, so the credit has to be IN Value from the apply onwards. */
			LogAssert.AreEqual(200, buyer.Value, "the payment is taken");
			LogAssert.AreEqual(340, seller.Value, "the credit is already in the value any save would capture");
			LogAssert.AreEqual(300, seller.HeldValue, "and it is held");
			LogAssert.AreEqual(0, buyer.HeldValue, "nothing is held for the side that only paid");
			LogAssert.IsTrue(buyer.IsSettling && seller.IsSettling, "both attributes belong to the trade until it answers");
			LogAssert.IsFalse(settlement.Closed, "and the settlement is open");
		}

		[Test]
		public void TryOpen_BothSidesPaying_NetsTheirBalances()
		{
			CharacterAttribute first = Currency(100);
			CharacterAttribute second = Currency(100);

			LogAssert.IsTrue(TradeCurrencySettlement.TryOpen(first, second, 30, 70, out _, out _), "opens");
			LogAssert.AreEqual(140, first.Value, "first: -30 +70");
			LogAssert.AreEqual(60, second.Value, "second: -70 +30");
			LogAssert.AreEqual(70, first.HeldValue, "first holds what it received");
			LogAssert.AreEqual(30, second.HeldValue, "second holds what it received");
		}

		[Test]
		public void AHeldCredit_CannotBeSpent_ButTheRestOfTheBalanceCan()
		{
			CharacterAttribute buyer = Currency(500);
			CharacterAttribute seller = Currency(40);
			TradeCurrencySettlement.TryOpen(buyer, seller, 300, 0, out _, out _);

			var sellerCharacter = new MockCharacter(controller, seller);
			LogAssert.IsFalse(CharacterCurrency.TrySpend(sellerCharacter, template, 41),
				"the seller's own 40 is spendable, the held 300 is not");
			LogAssert.AreEqual(340, seller.Value, "a refused spend changes nothing");
			LogAssert.IsTrue(CharacterCurrency.TrySpend(sellerCharacter, template, 40), "the seller's own money is still theirs");
			LogAssert.AreEqual(300, seller.Value, "leaving exactly the held credit");
			LogAssert.IsTrue(CharacterCurrency.CanAfford(sellerCharacter, template, 300),
				"CanAfford reports the balance; TrySpend is where the hold is enforced");
		}

		#endregion

		#region Outcome

		[Test]
		public void Close_Committed_KeepsTheCreditAndReleasesTheHold()
		{
			CharacterAttribute buyer = Currency(500);
			CharacterAttribute seller = Currency(40);
			TradeCurrencySettlement.TryOpen(buyer, seller, 300, 0, out TradeCurrencySettlement settlement, out _);

			settlement.Close(true, out var first, out var second);

			LogAssert.IsTrue(first.Closed && second.Closed, "both legs were this settlement's to close");
			LogAssert.AreEqual(200, buyer.Value, "the payment stays paid");
			LogAssert.AreEqual(340, seller.Value, "the credit stays credited");
			LogAssert.AreEqual(0, seller.HeldValue, "and is no longer held");
			LogAssert.IsFalse(buyer.IsSettling || seller.IsSettling, "the saves may write them again");
			LogAssert.IsTrue(CharacterCurrency.TrySpend(new MockCharacter(controller, seller), template, 340), "the seller can spend all of it now");
		}

		[Test]
		public void Close_Refused_RestoresBothBalancesExactly()
		{
			CharacterAttribute first = Currency(100);
			CharacterAttribute second = Currency(250);
			TradeCurrencySettlement.TryOpen(first, second, 30, 70, out TradeCurrencySettlement settlement, out _);

			settlement.Close(false, out var a, out var b);

			LogAssert.AreEqual(100, first.Value, "first is back where it started");
			LogAssert.AreEqual(250, second.Value, "second is back where it started");
			LogAssert.AreEqual(0, a.CreditShortfall + a.RefundShortfall + b.CreditShortfall + b.RefundShortfall, "exactly");
			LogAssert.IsFalse(first.IsSettling || second.IsSettling, "and neither is settling");
			LogAssert.IsTrue(first.PersistenceDirty && second.PersistenceDirty, "the restored balances are marked for the next save");
		}

		[Test]
		public void Close_Refused_KeepsWhatWasEarnedMeanwhile_AndTakesBackOnlyTheCredit()
		{
			CharacterAttribute buyer = Currency(500);
			CharacterAttribute seller = Currency(40);
			TradeCurrencySettlement.TryOpen(buyer, seller, 300, 0, out TradeCurrencySettlement settlement, out _);

			// Loot lands on the seller while the trade is settling; a spend of the seller's own money too.
			var sellerCharacter = new MockCharacter(controller, seller);
			CharacterCurrency.TryAdd(sellerCharacter, template, 25);
			CharacterCurrency.TrySpend(sellerCharacter, template, 60);

			settlement.Close(false, out _, out var sellerLeg);

			LogAssert.AreEqual(40 + 25 - 60, seller.Value, "the seller keeps what happened outside the trade, and nothing of it");
			LogAssert.AreEqual(0, sellerLeg.CreditShortfall, "the held credit was all still there");
			LogAssert.AreEqual(500, buyer.Value, "the buyer is refunded in full");
		}

		[Test]
		public void Close_IsIdempotent()
		{
			CharacterAttribute first = Currency(100);
			CharacterAttribute second = Currency(100);
			TradeCurrencySettlement.TryOpen(first, second, 10, 0, out TradeCurrencySettlement settlement, out _);

			settlement.Close(false, out _, out _);
			settlement.Close(false, out var again, out _);
			settlement.Close(true, out _, out _);

			LogAssert.IsFalse(again.Closed, "a second close does nothing");
			LogAssert.AreEqual(100, first.Value, "the refund was paid once");
			LogAssert.AreEqual(100, second.Value, "the credit was taken back once");
		}

		[Test]
		public void Close_NeverTouchesAnAttributeResetForAnotherOccupant()
		{
			CharacterAttribute first = Currency(100);
			CharacterAttribute second = Currency(100);
			TradeCurrencySettlement.TryOpen(first, second, 10, 0, out TradeCurrencySettlement settlement, out _);

			/* The second party logged out; its pooled object was reset and handed to somebody else, who
			 * loaded a balance of 7. The trade's late refusal must not take its credit out of them. */
			second.ResetPersistenceState();
			second.SetValue(7);

			settlement.Close(false, out var firstLeg, out var secondLeg);

			LogAssert.IsTrue(firstLeg.Closed, "the party still here is restored");
			LogAssert.AreEqual(100, first.Value, "exactly");
			LogAssert.IsFalse(secondLeg.Closed, "the reset attribute no longer carries the settlement");
			LogAssert.AreEqual(7, second.Value, "and the new occupant's balance is untouched");
		}

		[Test]
		public void EndSettlement_ReportsAShortfall_WhenADirectWriteLoweredTheBalanceUnderTheHold()
		{
			CharacterAttribute seller = Currency(0);
			long token = seller.BeginSettlement();
			seller.CreditHeld(token, 300);

			seller.SetValue(120); // an operator's setgold bypasses CharacterCurrency

			LogAssert.IsTrue(seller.EndSettlement(token, false, out int shortfall), "the token is still the open one");
			LogAssert.AreEqual(0, seller.Value, "what was there is taken back, and the balance never goes negative");
			LogAssert.AreEqual(180, shortfall, "and the rest is reported");
		}

		[Test]
		public void BeginSettlement_RefusesASecondOpenSettlement()
		{
			CharacterAttribute currency = Currency(10);
			long token = currency.BeginSettlement();

			LogAssert.IsTrue(token != 0, "the first opens");
			LogAssert.AreEqual(0L, currency.BeginSettlement(), "a second cannot open over it");
			LogAssert.IsFalse(currency.CreditHeld(token + 1, 5), "and a stranger's token credits nothing");
			LogAssert.AreEqual(10, currency.Value, "so the value is unchanged");
		}

		[Test]
		public void Spendable_IsTheValueLessTheHold_NeverNegative()
		{
			LogAssert.AreEqual(70L, CharacterCurrency.Spendable(100, 30), "value less hold");
			LogAssert.AreEqual(100L, CharacterCurrency.Spendable(100, 0), "no hold");
			LogAssert.AreEqual(0L, CharacterCurrency.Spendable(20, 30), "a hold larger than the value leaves nothing, not a debt");
			LogAssert.AreEqual(100L, CharacterCurrency.Spendable(100, -5), "a negative hold is no hold");
		}

		#endregion

		#region Source pins

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path).Replace("\r\n", "\n");
		}

		/// <summary>The source with comment lines removed, so prose about a construct does not satisfy a scan for it.</summary>
		private static string CodeOnly(string source)
		{
			var kept = new System.Text.StringBuilder(source.Length);
			foreach (string line in source.Split('\n'))
			{
				string t = line.TrimStart();
				if (t.StartsWith("//") || t.StartsWith("/*") || t.StartsWith("*"))
				{
					continue;
				}
				kept.Append(line).Append('\n');
			}
			return kept.ToString();
		}

		private static string MethodBody(string source, string signature, string nextSymbol)
		{
			int start = source.IndexOf(signature, StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, $"the source must still declare {signature}");
			int end = source.IndexOf(nextSymbol, start + signature.Length, StringComparison.Ordinal);
			LogAssert.IsTrue(end > start, $"the end of {signature} must be locatable");
			return source.Substring(start, end - start);
		}

		[Test]
		public void TheCharactersOwnSaves_LeaveASettlingAttributeToTheTrade()
		{
			string body = CodeOnly(MethodBody(ReadSource(SavingPath),
				"private void AppendAttributeData(IPlayerCharacter character, List<CharacterAttributeData> attributes)",
				"private void AppendAbilityData("));

			LogAssert.IsTrue(body.Contains("if (attr.IsSettling)"),
				"a plain attribute a trade is settling is left dirty for the pass after the outcome");
			LogAssert.IsTrue(body.Contains("!resAttr.PersistenceDirty || resAttr.IsSettling"),
				"and so is a resource attribute");
		}

		[Test]
		public void TheExchangeWritesTheSheetAsMemoryHoldsIt()
		{
			string capture = CodeOnly(MethodBody(ReadSource(InventoryPath),
				"private ItemWriteBatch CaptureExchangeLeg(ExchangeRun run, ItemExchangeLeg leg)",
				"private async Task FinishExchangeAsync("));
			LogAssert.IsTrue(capture.Contains("batch.AddAttributeWrites(BuildAttributeDataList(character));"),
				"the exchange leg writes the attribute sheet straight from memory");
			LogAssert.IsFalse(CodeOnly(ReadSource(InventoryPath)).Contains("AttributesMustApplyWhole"),
				"no batch carries a value memory does not hold, so none needs to insist on landing whole");

			LogAssert.IsNull(typeof(ItemExchangeLeg).GetField("CurrencyCredit", BindingFlags.Public | BindingFlags.Instance),
				"an exchange leg no longer carries a credit to fold into its row");
		}

		[Test]
		public void TheTradeCreditsAtTheApply_AndNeverAtTheFinish()
		{
			string source = ReadSource(CommitPath);
			string apply = CodeOnly(MethodBody(source, "private ItemExchangeLeg[] ApplyExchange(TradeSession session)", "private void FinishExchange("));
			string finish = CodeOnly(MethodBody(source, "private void FinishExchange(TradeSession session, bool committed)", "private void CloseCurrency("));

			int open = apply.IndexOf("TradeCurrencySettlement.TryOpen(", StringComparison.Ordinal);
			int items = apply.IndexOf("TradeExchange.TryApply(", StringComparison.Ordinal);
			LogAssert.IsTrue(open >= 0 && items > open, "the currency is settled in memory at the apply, before the items move");
			LogAssert.IsFalse(finish.Contains("CharacterCurrency.TryAdd("),
				"the finish hop credits nobody: a credit arriving after the commit is the window that lost the seller's coin");
			LogAssert.IsTrue(finish.Contains("CloseCurrency(session, committed: true);") && finish.Contains("CloseCurrency(session, committed: false);"),
				"the finish hop only closes the settlement, either way");
		}

		#endregion

		/// <summary>An ICharacter whose attribute controller resolves one currency attribute.</summary>
		private sealed class MockCharacter : ICharacter
		{
			private readonly CharacterAttributeController attributes;

			public MockCharacter(CharacterAttributeController attributes, CharacterAttribute currency)
			{
				this.attributes = attributes;
				// The controller resolves by template; make this the attribute it hands back.
				attributes.Attributes.Remove(currency.Template.ID);
				attributes.AddAttribute(currency);
			}

			public long ID { get; set; } = 1;
			public string Name => "MockCharacter";
			public Transform Transform => null;
			public GameObject GameObject => null;
			public Collider Collider { get; set; }
			public FishNet.Connection.NetworkConnection Owner => null;
			public FishNet.Object.NetworkObject NetworkObject => null;
			public FishNet.Managing.Predicting.PredictionManager PredictionManager => null;
			public System.Collections.Generic.HashSet<FishNet.Connection.NetworkConnection> Observers { get; } = new System.Collections.Generic.HashSet<FishNet.Connection.NetworkConnection>();
			public bool IsTeleporting => false;
			public bool IsSpawned => true;
			public int Flags { get; set; }
			public Nameplate CharacterNameplate { get; set; }
			public Transform MeshRoot => null;

#if !UNITY_SERVER
			public void InstantiateRaceModelFromIndex(RaceTemplate raceTemplate, int modelIndex) { }
			public void InstantiateRaceModelFromIndex(RaceTemplate raceTemplate, int modelIndex, CharacterGender gender) { }
#endif

			public void EnableFlags(CharacterFlags flags) => Flags |= (int)flags;
			public void DisableFlags(CharacterFlags flags) => Flags &= ~(int)flags;
			public bool IsFlagged(CharacterFlags flags) => (Flags & (int)flags) != 0;
			public void RegisterCharacterBehaviour(ICharacterBehaviour characterBehaviour) { }
			public void UnregisterCharacterBehaviour(ICharacterBehaviour characterBehaviour) { }

			public bool TryGet<T>(out T control) where T : class, ICharacterBehaviour
			{
				control = attributes as T;
				return control != null;
			}

			public void Invoke(System.Collections.Generic.List<Trigger> triggers, EventData eventData) { }
		}
	}
}
