using System;
using FishMMO.Shared;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// The currency half of a trade's commit: both payments taken and both credits given in memory
	/// when the apply runs, with the credits held until the database answers.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Pure attribute arithmetic: no network, no persistence, no character lookups — the caller
	/// resolves the two currency attributes and this does the rest, so the rules can be tested
	/// without a server.
	/// </para>
	/// <para>
	/// <b>Why the credit is applied at the apply and not after the commit.</b> The exchange writes
	/// each side's attribute sheet in its transaction. The payee's row used to be written as "memory
	/// plus the credit", with memory credited only in the finish hop after the commit. Every capture
	/// of that attribute in between — the periodic save, another item batch's full sheet (which
	/// queues on the same row lock and lands straight after the commit), a merchant's write — carried
	/// a newer version and no credit, and overwrote the credited row. The items had moved and the
	/// seller's coin was missing from the database until the next save wrote memory again; a crash in
	/// that interval lost it. Applying the credit with the payment, symmetrically, makes memory equal
	/// to what the transaction writes, so no capture can lack it.
	/// </para>
	/// <para>
	/// <b>Exact undo is kept by holding the credit.</b> It was deferred so that a refused write would
	/// never have to claw back money already spent. <see cref="CharacterAttribute.CreditHeld"/> puts
	/// it in the balance but out of <see cref="CharacterCurrency.TrySpend"/>'s reach, so a refusal
	/// takes back exactly what was credited and refunds exactly what was paid.
	/// </para>
	/// <para>
	/// <b>While settling, the trade is the attribute's only writer.</b> Both attributes are marked
	/// (<see cref="CharacterAttribute.IsSettling"/>) and the character's own saves leave them for the
	/// pass after the outcome: a periodic save that captured the moved values could otherwise land
	/// them while the transaction was still undecided, and a refusal would leave the database showing
	/// half a trade. On a refusal the reversal marks the attribute dirty again, and the next save
	/// writes the restored balance.
	/// </para>
	/// </remarks>
	public sealed class TradeCurrencySettlement
	{
		/// <summary>Why a settlement could not be opened. Nothing was changed.</summary>
		public enum Refusal : byte
		{
			None = 0,
			/// <summary>A side that pays or is paid has no currency attribute.</summary>
			NoCurrency,
			/// <summary>An amount is negative or larger than a balance can hold.</summary>
			InvalidAmount,
			/// <summary>A payer's spendable balance does not cover their payment.</summary>
			CannotPay,
			/// <summary>A payee's balance would pass <see cref="int.MaxValue"/>.</summary>
			WouldOverflow,
			/// <summary>A currency attribute already has a settlement open.</summary>
			AlreadySettling,
		}

		/// <summary>One side's part: what it paid and what it was credited, and the token that closes it.</summary>
		public sealed class Leg
		{
			/// <summary>The side's currency attribute.</summary>
			public CharacterAttribute Currency;
			/// <summary>The settlement token on <see cref="Currency"/>.</summary>
			public long Token;
			/// <summary>Taken from this side at the apply; refunded on a refusal.</summary>
			public int Paid;
			/// <summary>Credited to this side at the apply and held; taken back on a refusal.</summary>
			public int Received;
		}

		/// <summary>What closing one leg did, for the log.</summary>
		public struct LegOutcome
		{
			/// <summary>False when the attribute no longer carried this settlement, so nothing was touched.</summary>
			public bool Closed;
			/// <summary>Credit that could not be taken back because a direct write had lowered the balance under it.</summary>
			public int CreditShortfall;
			/// <summary>Refund that could not be paid because the balance would have overflowed.</summary>
			public int RefundShortfall;
		}

		/// <summary>The first side.</summary>
		public Leg First { get; private set; }

		/// <summary>The second side.</summary>
		public Leg Second { get; private set; }

		/// <summary>True once <see cref="Close"/> has run.</summary>
		public bool Closed { get; private set; }

		private TradeCurrencySettlement()
		{
		}

		/// <summary>
		/// Validates both payments, then takes them and gives both credits, held. Main thread only.
		/// </summary>
		/// <remarks>
		/// Everything is checked before anything moves, so a refusal changes nothing. A trade in which
		/// no currency moves needs no settlement and gets none: <paramref name="settlement"/> is null
		/// and the result is true.
		/// </remarks>
		/// <param name="firstCurrency">The first side's currency attribute; may be null only when no currency moves.</param>
		/// <param name="secondCurrency">The second side's currency attribute; may be null only when no currency moves.</param>
		/// <param name="firstPays">What the first side pays the second.</param>
		/// <param name="secondPays">What the second side pays the first.</param>
		/// <param name="settlement">The open settlement, or null when none was needed or it was refused.</param>
		/// <param name="refusal">Why it was refused.</param>
		/// <returns>True when the currency half is in place (or there is none).</returns>
		public static bool TryOpen(
			CharacterAttribute firstCurrency,
			CharacterAttribute secondCurrency,
			long firstPays,
			long secondPays,
			out TradeCurrencySettlement settlement,
			out Refusal refusal)
		{
			settlement = null;
			refusal = CheckOpen(
				firstCurrency != null, firstCurrency?.Value ?? 0, firstCurrency?.HeldValue ?? 0, firstCurrency?.IsSettling ?? false,
				secondCurrency != null, secondCurrency?.Value ?? 0, secondCurrency?.HeldValue ?? 0, secondCurrency?.IsSettling ?? false,
				firstPays, secondPays);
			if (refusal != Refusal.None)
			{
				return false;
			}

			if (firstPays == 0 && secondPays == 0)
			{
				return true;
			}

			var opened = new TradeCurrencySettlement
			{
				First = new Leg { Currency = firstCurrency, Token = firstCurrency.BeginSettlement(), Paid = (int)firstPays, Received = (int)secondPays },
				Second = new Leg { Currency = secondCurrency, Token = secondCurrency.BeginSettlement(), Paid = (int)secondPays, Received = (int)firstPays },
			};

			// Take, then give — the order TradeExchange uses for items, and the one the overflow check
			// assumed: a side that both pays and is paid never holds both amounts at once.
			if (opened.First.Paid > 0)
			{
				firstCurrency.AddValue(-opened.First.Paid);
			}
			if (opened.Second.Paid > 0)
			{
				secondCurrency.AddValue(-opened.Second.Paid);
			}
			/* CheckOpen has already proven both credits fit, so neither of these can refuse. If one
			 * ever did, the trade would commit a payment with no matching credit — so a refusal here
			 * is treated as the overflow it would be: everything taken so far is put back exactly and
			 * the trade is refused, rather than trusting the check above to stay in step. */
			bool credited =
				(opened.First.Received == 0 || firstCurrency.CreditHeld(opened.First.Token, opened.First.Received)) &&
				(opened.Second.Received == 0 || secondCurrency.CreditHeld(opened.Second.Token, opened.Second.Received));
			if (!credited)
			{
				opened.Close(false, out _, out _);
				refusal = Refusal.WouldOverflow;
				return false;
			}

			settlement = opened;
			return true;
		}

		/// <summary>
		/// The whole admission rule, over plain numbers. Pure.
		/// </summary>
		/// <remarks>
		/// A payer must be able to cover the payment from what is spendable — a credit another
		/// settlement holds is not theirs yet. A payee must be able to hold the credit in an
		/// <c>int</c>: checked against the balance before the payee's own payment is taken, which is
		/// conservative toward refusing, exactly as the trade's accept-time check always was.
		/// </remarks>
		public static Refusal CheckOpen(
			bool firstHasCurrency, int firstValue, int firstHeld, bool firstSettling,
			bool secondHasCurrency, int secondValue, int secondHeld, bool secondSettling,
			long firstPays, long secondPays)
		{
			if (firstPays < 0 || secondPays < 0 || firstPays > int.MaxValue || secondPays > int.MaxValue)
			{
				return Refusal.InvalidAmount;
			}
			if (firstPays == 0 && secondPays == 0)
			{
				return Refusal.None;
			}
			if (!firstHasCurrency || !secondHasCurrency)
			{
				return Refusal.NoCurrency;
			}
			if (firstSettling || secondSettling)
			{
				return Refusal.AlreadySettling;
			}
			if (CharacterCurrency.Spendable(firstValue, firstHeld) < firstPays ||
				CharacterCurrency.Spendable(secondValue, secondHeld) < secondPays)
			{
				return Refusal.CannotPay;
			}
			if ((long)secondValue + firstPays > int.MaxValue || (long)firstValue + secondPays > int.MaxValue)
			{
				return Refusal.WouldOverflow;
			}
			return Refusal.None;
		}

		/// <summary>
		/// Closes both legs with the transaction's outcome. Main thread only. Idempotent.
		/// </summary>
		/// <remarks>
		/// Committed: the credits stay and stop being held. Refused: each credit is taken back and
		/// each payment refunded. A leg whose attribute no longer carries this settlement's token —
		/// the character left and its pooled object was reset for somebody else — is not touched at
		/// all; the departing character's own save left the settling attribute to this transaction,
		/// so the database already says what the outcome says.
		/// </remarks>
		/// <param name="committed">Whether the transaction committed.</param>
		/// <param name="first">What closing the first leg did.</param>
		/// <param name="second">What closing the second leg did.</param>
		public void Close(bool committed, out LegOutcome first, out LegOutcome second)
		{
			first = default;
			second = default;
			if (Closed)
			{
				return;
			}
			Closed = true;
			first = CloseLeg(First, committed);
			second = CloseLeg(Second, committed);
		}

		private static LegOutcome CloseLeg(Leg leg, bool committed)
		{
			var outcome = new LegOutcome();
			if (leg?.Currency == null || !leg.Currency.EndSettlement(leg.Token, committed, out outcome.CreditShortfall))
			{
				return outcome;
			}
			outcome.Closed = true;

			if (!committed && leg.Paid > 0)
			{
				long room = (long)int.MaxValue - leg.Currency.Value;
				int refund = (int)Math.Min(leg.Paid, Math.Max(0, room));
				outcome.RefundShortfall = leg.Paid - refund;
				if (refund > 0)
				{
					leg.Currency.AddValue(refund);
				}
			}
			return outcome;
		}
	}
}
