using FishMMO.Shared;
using NUnit.Framework;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Tests for the currency ledger's persisted numbering.
	/// </summary>
	/// <remarks>
	/// These values are stored as ints in <c>currency_ledger</c> and duplicated as constants in
	/// <c>CurrencyLedgerService</c>, which cannot reference this assembly. Renumbering a member
	/// here would silently reinterpret every stored row — a Returned movement reading back as
	/// Absorbed is a refund counted as revenue — so the values are pinned by test rather than left
	/// to convention.
	/// </remarks>
	[TestFixture]
	public class CurrencyLedgerStateTests
	{
		/// <summary>
		/// Unsettled must be zero, because it is the column default. A row that reaches the table
		/// without an explicit state has to be identifiable as a bug rather than counting as
		/// currency that left the economy.
		/// </summary>
		[Test]
		public void Unsettled_IsZero()
		{
			Assert.AreEqual(0, (int)CurrencyMovementState.Unsettled);
		}

		/// <summary>
		/// The two settled outcomes are the only values <c>RecordAsync</c> accepts, and it
		/// compares against its own copies of these numbers.
		/// </summary>
		[Test]
		public void SettledStates_HaveTheirPersistedValues()
		{
			Assert.AreEqual(1, (int)CurrencyMovementState.Absorbed);
			Assert.AreEqual(2, (int)CurrencyMovementState.Returned);
		}

		/// <summary>
		/// Every state is distinct: two sharing a value would make a refund indistinguishable
		/// from a completed charge.
		/// </summary>
		[Test]
		public void States_AreDistinct()
		{
			Assert.AreNotEqual((int)CurrencyMovementState.Unsettled, (int)CurrencyMovementState.Absorbed);
			Assert.AreNotEqual((int)CurrencyMovementState.Unsettled, (int)CurrencyMovementState.Returned);
			Assert.AreNotEqual((int)CurrencyMovementState.Absorbed, (int)CurrencyMovementState.Returned);
		}

		/// <summary>
		/// A missing reason must be visible rather than masquerading as a real sink.
		/// </summary>
		[Test]
		public void UnknownReason_IsZero()
		{
			Assert.AreEqual(0, (int)CurrencyMovementReason.Unknown);
		}

		/// <summary>
		/// Reasons are stored as ints, so their values are part of the schema.
		/// </summary>
		[TestCase(CurrencyMovementReason.MerchantPurchase, 1)]
		[TestCase(CurrencyMovementReason.AbilityLearn, 2)]
		[TestCase(CurrencyMovementReason.AbilityCraft, 3)]
		[TestCase(CurrencyMovementReason.MailAttachment, 4)]
		[TestCase(CurrencyMovementReason.LandPurchase, 5)]
		[TestCase(CurrencyMovementReason.LandTax, 6)]
		[TestCase(CurrencyMovementReason.PlayerTrade, 7)]
		public void Reasons_HaveTheirPersistedValues(CurrencyMovementReason reason, int expected)
		{
			Assert.AreEqual(expected, (int)reason);
		}
	}
}
