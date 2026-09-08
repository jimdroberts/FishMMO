using System;
using FishMMO.Logging;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Completion: the moment both parties have accepted the same version of the table.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The order of operations is the whole design, so it is stated here in full:
	/// </para>
	/// <list type="number">
	///   <item><description>
	///     Move the session to <see cref="TradePhase.Committing"/>. From here no request from
	///     either party is honoured; the outcome is decided by this method alone.
	///   </description></item>
	///   <item><description>
	///     RE-VALIDATE EVERYTHING as if the trade were being proposed now: both present, both
	///     able to act, same scene, in range, every offered slot still holds the item and
	///     quantity that was offered, both currency balances cover their offers, and neither
	///     receiving balance would overflow. Nothing has been touched yet, so any refusal is
	///     simply a close.
	///   </description></item>
	///   <item><description>
	///     Deduct both currency offers. This is the one mutation that precedes the item
	///     exchange, and it is refunded if the exchange refuses — it goes first because a
	///     deduction can only fail for reasons step 2 already ruled out, whereas the exchange
	///     can still refuse for room, and a refund is a trivially exact undo.
	///   </description></item>
	///   <item><description>
	///     Apply the item exchange in memory, all or nothing (<see cref="TradeExchange"/>).
	///     On refusal: refund, close with <see cref="TradeCloseReason.NoRoom"/>, and both bags
	///     are exactly as they were.
	///   </description></item>
	///   <item><description>
	///     Credit both currency offers to their recipients. Cannot fail: the recipient's
	///     attribute was read in step 2 and the sum was overflow-checked.
	///   </description></item>
	///   <item><description>
	///     Hand both characters' rows, both attribute sheets and the ledger rows to
	///     <see cref="ICharacterInventorySystem.TryPersistExchange"/> as ONE unit of work.
	///     Memory is authoritative from step 5 onward; the database converges to it, and if
	///     the transaction rolls back both characters are reconciled from memory. It cannot
	///     land half a trade, because there is only one commit.
	///   </description></item>
	///   <item><description>
	///     Tell both clients which of their slots changed, and close the session as
	///     <see cref="TradeCloseReason.Completed"/>.
	///   </description></item>
	/// </list>
	/// </remarks>
	public partial class TradeSystem
	{
		private void TryCommit(TradeSession session)
		{
			if (session == null || !session.TryBeginCommit())
			{
				return;
			}

			// ── 2. Re-validate ──────────────────────────────────────────────────────────────

			bool firstPresent = TryGetResidentCharacter(session.First.CharacterID, out IPlayerCharacter first);
			bool secondPresent = TryGetResidentCharacter(session.Second.CharacterID, out IPlayerCharacter second);
			if (!firstPresent || !secondPresent)
			{
				// The committing phase never held locks it did not release; CloseSession
				// releases them for a session that was open, and this one no longer is, so
				// release explicitly before closing.
				ReleaseOfferLocks(session, first, second);
				CloseSession(session, TradeCloseReason.PartnerLeft, TradeCloseReason.PartnerLeft);
				return;
			}

			if (!CharacterStateValidation.CanAct(first) || !CharacterStateValidation.CanAct(second))
			{
				ReleaseOfferLocks(session, first, second);
				CloseSession(session, TradeCloseReason.CannotAct, TradeCloseReason.CannotAct);
				return;
			}

			if (!TradeRules.CanTradeTogether(first, second, maxTradeDistance))
			{
				ReleaseOfferLocks(session, first, second);
				CloseSession(session, TradeCloseReason.OutOfRange, TradeCloseReason.OutOfRange);
				return;
			}

			if (!TryGetInventory(first, out IInventoryController firstInventory) ||
				!TryGetInventory(second, out IInventoryController secondInventory))
			{
				ReleaseOfferLocks(session, first, second);
				CloseSession(session, TradeCloseReason.Failed, TradeCloseReason.Failed);
				return;
			}

			long firstPays = session.First.Currency;
			long secondPays = session.Second.Currency;

			if (!ValidateCurrencyLeg(first, second, firstPays) || !ValidateCurrencyLeg(second, first, secondPays))
			{
				ReleaseOfferLocks(session, first, second);
				CloseSession(session, TradeCloseReason.Failed, TradeCloseReason.Failed);
				return;
			}

			if (!Server.BehaviourRegistry.TryGet(out ICharacterInventorySystem inventorySystem))
			{
				Log.Error("TradeSystem", $"Session {session.ID}: ICharacterInventorySystem is unavailable; the trade cannot be persisted and is refused.");
				ReleaseOfferLocks(session, first, second);
				CloseSession(session, TradeCloseReason.Failed, TradeCloseReason.Failed);
				return;
			}

			/* The locks were the reservation while the table was open. The exchange itself
			 * needs the slots unlocked — RemoveItem refuses a locked slot — and the session is
			 * committing, so no request can reach these slots between here and the end of this
			 * method. TradeExchange re-reads every slot before it moves anything. */
			ReleaseOfferLocks(session, first, second);

			// ── 3. Deduct currency ──────────────────────────────────────────────────────────

			if (firstPays > 0 && !CharacterCurrency.TrySpend(first, currencyTemplate, firstPays))
			{
				CloseSession(session, TradeCloseReason.Failed, TradeCloseReason.Failed);
				return;
			}
			if (secondPays > 0 && !CharacterCurrency.TrySpend(second, currencyTemplate, secondPays))
			{
				if (firstPays > 0)
				{
					CharacterCurrency.TryAdd(first, currencyTemplate, firstPays);
				}
				CloseSession(session, TradeCloseReason.Failed, TradeCloseReason.Failed);
				return;
			}

			// ── 4. Exchange items ───────────────────────────────────────────────────────────

			var firstSide = new TradeExchange.Side { Inventory = firstInventory, Offers = session.First.Offers };
			var secondSide = new TradeExchange.Side { Inventory = secondInventory, Offers = session.Second.Offers };

			if (!TradeExchange.TryApply(firstSide, secondSide, out TradeExchange.Failure failure))
			{
				if (firstPays > 0)
				{
					CharacterCurrency.TryAdd(first, currencyTemplate, firstPays);
				}
				if (secondPays > 0)
				{
					CharacterCurrency.TryAdd(second, currencyTemplate, secondPays);
				}

				TradeCloseReason reason = failure == TradeExchange.Failure.NoRoom ? TradeCloseReason.NoRoom : TradeCloseReason.Failed;
				Log.Debug("TradeSystem", $"Session {session.ID}: exchange refused ({failure}).");
				CloseSession(session, reason, reason);
				return;
			}

			// ── 5. Credit currency ──────────────────────────────────────────────────────────

			if (firstPays > 0)
			{
				CharacterCurrency.TryAdd(second, currencyTemplate, firstPays);
			}
			if (secondPays > 0)
			{
				CharacterCurrency.TryAdd(first, currencyTemplate, secondPays);
			}

			// ── 6. One commit ───────────────────────────────────────────────────────────────

			bool currencyMoved = firstPays > 0 || secondPays > 0;

			var firstLeg = new ItemExchangeLeg
			{
				Character = first,
				ChangedInventoryItems = firstSide.Changed,
				RemovedInventoryItems = firstSide.Removed,
				PersistAttributes = currencyMoved,
				CurrencyPaid = firstPays,
			};
			var secondLeg = new ItemExchangeLeg
			{
				Character = second,
				ChangedInventoryItems = secondSide.Changed,
				RemovedInventoryItems = secondSide.Removed,
				PersistAttributes = currencyMoved,
				CurrencyPaid = secondPays,
			};

			if (!inventorySystem.TryPersistExchange(firstLeg, secondLeg, "PlayerTrade"))
			{
				// Not dropped: the write is running on the fallback path. Worth a line because
				// sustained fallback means the persistence queue is saturated.
				Log.Warning("TradeSystem", $"Session {session.ID}: persistence queue full; the exchange write ran on the fallback path.");
			}

			// ── 7. Close, THEN tell both clients ────────────────────────────────────────────

			/* The close goes first, on purpose. The owning client holds its own lock on every
			 * slot it offered (mirrored from the last TradeStateBroadcast), and its inventory
			 * handlers apply a set or a remove through SetItemSlot/RemoveItem, which refuse a
			 * locked slot. Sent the other way round, the "slot 3 is now empty" message would
			 * arrive while slot 3 was still locked on the client, be refused, and the player
			 * would keep seeing an item they had just given away. TradeClosedBroadcast is
			 * what releases those locks, and the channel is reliable and ordered. */
			Log.Debug("TradeSystem", $"Session {session.ID} completed: {first.ID} gave {session.First.Offers.Count} item(s) + {firstPays} currency; {second.ID} gave {session.Second.Offers.Count} item(s) + {secondPays} currency.");

			CloseSession(session, TradeCloseReason.Completed, TradeCloseReason.Completed);

			inventorySystem.NotifyInventorySlots(first, firstSide.Changed, firstSide.EmptiedSlots);
			inventorySystem.NotifyInventorySlots(second, secondSide.Changed, secondSide.EmptiedSlots);
		}

		/// <summary>
		/// True when <paramref name="payer"/> can pay <paramref name="amount"/> and
		/// <paramref name="payee"/> can receive it without overflowing.
		/// </summary>
		/// <remarks>
		/// Currency is an <c>int</c>-valued attribute. A balance near the ceiling receiving a
		/// large offer would wrap negative inside <c>AddValue</c>, which is a far worse outcome
		/// than refusing the trade.
		/// </remarks>
		private bool ValidateCurrencyLeg(IPlayerCharacter payer, IPlayerCharacter payee, long amount)
		{
			if (amount <= 0)
			{
				return true;
			}

			if (currencyTemplate == null)
			{
				return false;
			}

			if (!CharacterCurrency.TryGetBalance(payer, currencyTemplate, out long payerBalance) || payerBalance < amount)
			{
				return false;
			}

			if (!CharacterCurrency.TryGetBalance(payee, currencyTemplate, out long payeeBalance))
			{
				return false;
			}

			return payeeBalance + amount <= int.MaxValue;
		}

		/// <summary>Unlocks every offered slot on both sides, for the commit path.</summary>
		private void ReleaseOfferLocks(TradeSession session, IPlayerCharacter first, IPlayerCharacter second)
		{
			UnlockOffers(first, session.First);
			UnlockOffers(second, session.Second);
		}
	}
}
