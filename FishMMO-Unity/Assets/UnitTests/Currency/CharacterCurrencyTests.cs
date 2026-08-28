using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishNet.Connection;
using FishNet.Managing.Predicting;
using FishNet.Object;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Tests for <see cref="CharacterCurrency"/>, the shared read/spend/grant path.
	/// </summary>
	/// <remarks>
	/// Currency had no coverage at all before this, while being hand-rolled at nine call sites
	/// across looting, crafting, merchants and mail. These lock down the behaviour those sites
	/// are being migrated onto, including the two mistakes the codebase has already made once:
	/// spending against a buffed total, and deducting without a working persistence path.
	/// </remarks>
	[TestFixture]
	public class CharacterCurrencyTests
	{
		private CharacterAttributeTemplate currencyTemplate;
		private GameObject gameObject;
		private CharacterAttributeController controller;
		private MockCharacter character;

		[SetUp]
		public void SetUp()
		{
			currencyTemplate = ScriptableObject.CreateInstance<CharacterAttributeTemplate>();
			currencyTemplate.name = "CurrencyTestAttribute";
			currencyTemplate.InitialValue = 100;
			currencyTemplate.AddToCache(currencyTemplate.name);

			gameObject = new GameObject("CharacterCurrencyTest");
			controller = gameObject.AddComponent<CharacterAttributeController>();

			character = new MockCharacter(1, controller);
		}

		[TearDown]
		public void TearDown()
		{
			if (currencyTemplate != null)
			{
				currencyTemplate.RemoveFromCache();
				Object.DestroyImmediate(currencyTemplate);
			}
			if (gameObject != null)
			{
				Object.DestroyImmediate(gameObject);
			}
		}

		/// <summary>
		/// Adds the currency attribute to the controller with a starting balance.
		/// </summary>
		private CharacterAttribute GiveCurrency(int startingBalance)
		{
			CharacterAttribute attribute = new CharacterAttribute(controller, currencyTemplate.ID, startingBalance, 0);
			controller.AddAttribute(attribute);
			return attribute;
		}

		[Test]
		public void TryGetBalance_ReturnsFalse_WhenCharacterHasNoCurrencyAttribute()
		{
			Assert.IsFalse(CharacterCurrency.TryGetBalance(character, currencyTemplate, out long balance));
			Assert.AreEqual(0, balance);
		}

		[Test]
		public void TryGetBalance_ReadsTheBaseValue()
		{
			GiveCurrency(250);

			Assert.IsTrue(CharacterCurrency.TryGetBalance(character, currencyTemplate, out long balance));
			Assert.AreEqual(250, balance);
		}

		/// <summary>
		/// The buff trap: a modifier must not be spendable.
		/// </summary>
		/// <remarks>
		/// This is the defect the crafting path shipped with — it tested FinalValue while writing
		/// the base, so any currency-boosting modifier could be spent as though it were money.
		/// </remarks>
		[Test]
		public void CanAfford_IgnoresModifiers_SoABuffCannotBeSpent()
		{
			CharacterAttribute currency = GiveCurrency(50);
			currency.AddModifier(100);

			Assert.Greater(currency.FinalValue, currency.Value, "test needs a modifier in force");
			Assert.IsFalse(CharacterCurrency.CanAfford(character, currencyTemplate, 120),
				"a modifier must not be spendable");
			Assert.IsTrue(CharacterCurrency.CanAfford(character, currencyTemplate, 50));
		}

		[Test]
		public void CanAfford_TreatsNonPositiveAsFree()
		{
			Assert.IsTrue(CharacterCurrency.CanAfford(character, currencyTemplate, 0));
			Assert.IsTrue(CharacterCurrency.CanAfford(character, currencyTemplate, -10));
		}

		[Test]
		public void TryAdd_IncreasesTheBalance()
		{
			CharacterAttribute currency = GiveCurrency(10);

			Assert.IsTrue(CharacterCurrency.TryAdd(character, currencyTemplate, 15));
			Assert.AreEqual(25, currency.Value);
		}

		/// <summary>
		/// A sign error must not quietly take money.
		/// </summary>
		[Test]
		public void TryAdd_RejectsNonPositiveAmounts()
		{
			CharacterAttribute currency = GiveCurrency(10);

			Assert.IsFalse(CharacterCurrency.TryAdd(character, currencyTemplate, -5));
			Assert.AreEqual(10, currency.Value, "a negative grant must not deduct");

			Assert.IsFalse(CharacterCurrency.TryAdd(character, currencyTemplate, 0));
			Assert.AreEqual(10, currency.Value);
		}

		[Test]
		public void TrySpend_DeductsWhenAffordable()
		{
			CharacterAttribute currency = GiveCurrency(100);

			Assert.IsTrue(CharacterCurrency.TrySpend(character, currencyTemplate, 30));
			Assert.AreEqual(70, currency.Value);
		}

		[Test]
		public void TrySpend_RefusesWhenShort_AndLeavesTheBalanceAlone()
		{
			CharacterAttribute currency = GiveCurrency(20);

			Assert.IsFalse(CharacterCurrency.TrySpend(character, currencyTemplate, 21));
			Assert.AreEqual(20, currency.Value);
		}

		/// <summary>
		/// A refused persistence write must not leave the player charged.
		/// </summary>
		[Test]
		public void TrySpend_RefundsWhenPersistenceIsRefused()
		{
			CharacterAttribute currency = GiveCurrency(100);

			Assert.IsFalse(CharacterCurrency.TrySpend(character, currencyTemplate, 40, () => false));
			Assert.AreEqual(100, currency.Value, "a refused write must be refunded in full");
		}

		[Test]
		public void TrySpend_KeepsTheDeduction_WhenPersistenceSucceeds()
		{
			CharacterAttribute currency = GiveCurrency(100);
			bool persisted = false;

			Assert.IsTrue(CharacterCurrency.TrySpend(character, currencyTemplate, 40, () =>
			{
				persisted = true;
				return true;
			}));

			Assert.IsTrue(persisted, "persistence must be invoked");
			Assert.AreEqual(60, currency.Value);
		}

		/// <summary>
		/// Persistence runs after the deduction, because it snapshots the values as they stand.
		/// </summary>
		[Test]
		public void TrySpend_PersistsAfterDeducting()
		{
			CharacterAttribute currency = GiveCurrency(100);
			int balanceSeenByPersist = -1;

			CharacterCurrency.TrySpend(character, currencyTemplate, 25, () =>
			{
				balanceSeenByPersist = currency.Value;
				return true;
			});

			Assert.AreEqual(75, balanceSeenByPersist,
				"persistence must observe the post-deduction balance");
		}

		[Test]
		public void TrySpend_RejectsNonPositiveAmounts()
		{
			CharacterAttribute currency = GiveCurrency(100);

			Assert.IsFalse(CharacterCurrency.TrySpend(character, currencyTemplate, 0));
			Assert.IsFalse(CharacterCurrency.TrySpend(character, currencyTemplate, -50));
			Assert.AreEqual(100, currency.Value, "a negative spend must not grant money");
		}

		[Test]
		public void Operations_AreSafeAgainstNulls()
		{
			Assert.IsFalse(CharacterCurrency.TryGetBalance(null, currencyTemplate, out _));
			Assert.IsFalse(CharacterCurrency.TryGetBalance(character, null, out _));
			Assert.IsFalse(CharacterCurrency.TryAdd(null, currencyTemplate, 10));
			Assert.IsFalse(CharacterCurrency.TrySpend(null, currencyTemplate, 10));
		}

		/// <summary>
		/// Minimal ICharacter that resolves a single attribute controller.
		/// </summary>
		private sealed class MockCharacter : ICharacter
		{
			private readonly ICharacterAttributeController attributeController;

			public MockCharacter(long id, ICharacterAttributeController attributeController)
			{
				ID = id;
				this.attributeController = attributeController;
			}

			public long ID { get; set; }
			public string Name => "MockCharacter";
			public Transform Transform => null;
			public GameObject GameObject => null;
			public Collider Collider { get; set; }
			public NetworkConnection Owner => null;
			public NetworkObject NetworkObject => null;
			public PredictionManager PredictionManager => null;
			public HashSet<NetworkConnection> Observers { get; } = new HashSet<NetworkConnection>();
			public bool IsTeleporting => false;
			public bool IsSpawned => true;
			public int Flags { get; set; }

			public WorldLabel CharacterNameLabel { get; set; }
			public WorldLabel CharacterGuildLabel { get; set; }
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
				control = this.attributeController as T;
				return control != null;
			}

			public void Invoke(List<Trigger> triggers, EventData eventData) { }
		}
	}
}
