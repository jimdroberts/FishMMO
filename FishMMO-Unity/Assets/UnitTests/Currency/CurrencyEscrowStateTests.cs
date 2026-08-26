using FishMMO.Shared;
using NUnit.Framework;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Tests for the currency escrow state and reason contracts.
	/// </summary>
	/// <remarks>
	/// The numeric values are persisted in <c>currency_escrow</c> and duplicated as constants in
	/// <c>CurrencyEscrowService</c>, which cannot reference this assembly. Renumbering an enum
	/// member here would silently reinterpret every stored row — a Held hold reading back as
	/// Absorbed is money that is never returned — so the values are pinned by test rather than
	/// left to convention.
	/// </remarks>
	[TestFixture]
	public class CurrencyEscrowStateTests
	{
		/// <summary>
		/// Held must be zero: the reconciliation index is filtered on <c>state = 0</c>, and the
		/// column defaults to 0 so an insert that omits state is Held rather than settled.
		/// </summary>
		[Test]
		public void Held_IsZero()
		{
			Assert.AreEqual(0, (int)CurrencyEscrowState.Held);
		}

		[Test]
		public void SettledStates_HaveTheirPersistedValues()
		{
			Assert.AreEqual(1, (int)CurrencyEscrowState.Absorbed);
			Assert.AreEqual(2, (int)CurrencyEscrowState.Returned);
		}

		/// <summary>
		/// A missing reason must be visible rather than masquerading as a real one.
		/// </summary>
		[Test]
		public void UnknownReason_IsZero()
		{
			Assert.AreEqual(0, (int)CurrencyEscrowReason.Unknown);
		}

		/// <summary>
		/// Reasons are stored as ints, so their values are part of the schema.
		/// </summary>
		[TestCase(CurrencyEscrowReason.MerchantPurchase, 1)]
		[TestCase(CurrencyEscrowReason.AbilityLearn, 2)]
		[TestCase(CurrencyEscrowReason.AbilityCraft, 3)]
		[TestCase(CurrencyEscrowReason.MailAttachment, 4)]
		[TestCase(CurrencyEscrowReason.LandPurchase, 5)]
		[TestCase(CurrencyEscrowReason.LandTax, 6)]
		[TestCase(CurrencyEscrowReason.PlayerTrade, 7)]
		public void Reasons_HaveTheirPersistedValues(CurrencyEscrowReason reason, int expected)
		{
			Assert.AreEqual(expected, (int)reason);
		}

		/// <summary>
		/// Every state is distinct: two states sharing a value would make a settled hold
		/// indistinguishable from an outstanding one.
		/// </summary>
		[Test]
		public void States_AreDistinct()
		{
			Assert.AreNotEqual((int)CurrencyEscrowState.Held, (int)CurrencyEscrowState.Absorbed);
			Assert.AreNotEqual((int)CurrencyEscrowState.Held, (int)CurrencyEscrowState.Returned);
			Assert.AreNotEqual((int)CurrencyEscrowState.Absorbed, (int)CurrencyEscrowState.Returned);
		}
	}
}
