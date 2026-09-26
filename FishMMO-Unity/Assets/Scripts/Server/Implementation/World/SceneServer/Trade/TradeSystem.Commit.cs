using System;
using System.Collections.Generic;
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
	/// <b>The database commit is the point of truth.</b> Nothing about the trade is final —
	/// not the items, not the currency — until the one transaction that carries both
	/// characters' halves has committed, and memory is mutated only while that transaction
	/// holds both characters' row locks. That is what makes a server crash at ANY instant
	/// safe: before the commit, memory is lost and the database rolls back, so the trade
	/// never happened for either side; after the commit, the database holds the whole trade
	/// for both, and the next login loads it. There is no interleaving in which one side's
	/// half is durable and the other's is not, because there is only ever one write and it
	/// is the one that decides. Players will find a way to crash a server; this is built so
	/// that a crash can only lose a trade in flight, never duplicate one.
	/// </para>
	/// <para>
	/// The order of operations, across three hops:
	/// </para>
	/// <list type="number">
	///   <item><description>
	///     <see cref="TryCommit"/> (main thread): the session moves to
	///     <see cref="TradePhase.Committing"/>, so no request from either party is honoured,
	///     and the offered slots stay locked. Nothing is mutated. The exchange is handed to
	///     <see cref="ICharacterInventorySystem.TryRunExchange"/>.
	///   </description></item>
	///   <item><description>
	///     <see cref="ApplyExchange"/> (main thread, both row locks held by the worker):
	///     re-validate everything as if the trade were proposed now, take both currency
	///     payments and give both credits (<see cref="TradeCurrencySettlement"/>), apply the
	///     item exchange in memory all-or-nothing, and hand back the rows. Memory now holds
	///     exactly what the transaction writes, so no other capture of either character can
	///     write a sheet without the trade in it. The credits are HELD: in the balance, out of
	///     reach of any spend, so a refusal still takes back exactly what it gave.
	///   </description></item>
	///   <item><description>
	///     <see cref="FinishExchange"/> (main thread, after the commit or the rollback): on
	///     success release the holds, tell both clients, close as Completed. On refusal undo the
	///     item exchange exactly (every touched slot was locked throughout), take back the
	///     credits and refund the payments, close as Failed; the inventory system voids anything
	///     captured from the applied state and reconciles both characters.
	///   </description></item>
	/// </list>
	/// </remarks>
	public partial class TradeSystem
	{
		/// <summary>Seconds a committing session may wait for its outcome before it is treated as refused.</summary>
		private const double CommitTimeoutSeconds = 60.0;

		private void TryCommit(TradeSession session)
		{
			if (session == null || !session.TryBeginCommit())
			{
				return;
			}

			if (!TryGetResidentCharacter(session.First.CharacterID, out IPlayerCharacter first) ||
				!TryGetResidentCharacter(session.Second.CharacterID, out IPlayerCharacter second))
			{
				CloseSession(session, TradeCloseReason.PartnerLeft, TradeCloseReason.PartnerLeft);
				return;
			}

			if (!Server.BehaviourRegistry.TryGet(out ICharacterInventorySystem inventorySystem))
			{
				Log.Error("TradeSystem", $"Session {session.ID}: ICharacterInventorySystem is unavailable; the trade cannot be persisted and is refused.");
				CloseSession(session, TradeCloseReason.Failed, TradeCloseReason.Failed);
				return;
			}

			session.Commit = new TradeSession.CommitState
			{
				StartedAt = Now,
				FailureReason = TradeCloseReason.Failed,
			};

			if (!inventorySystem.TryRunExchange(first, second,
					() => ApplyExchange(session),
					committed => FinishExchange(session, committed),
					"PlayerTrade"))
			{
				// Refused before anything ran: nothing to undo, the offered locks are still held
				// and CloseSession releases them.
				session.Commit = null;
				CloseSession(session, TradeCloseReason.Failed, TradeCloseReason.Failed);
			}
		}

		/// <summary>
		/// The apply hop. Main thread, both characters' row locks held. Returns the two legs
		/// after mutating memory, or null to abort with nothing changed.
		/// </summary>
		private ItemExchangeLeg[] ApplyExchange(TradeSession session)
		{
			TradeSession.CommitState commit = session.Commit;
			if (commit == null || commit.Finished || session.Phase != TradePhase.Committing)
			{
				return null;
			}

			if (!TryGetResidentCharacter(session.First.CharacterID, out IPlayerCharacter first) ||
				!TryGetResidentCharacter(session.Second.CharacterID, out IPlayerCharacter second))
			{
				commit.FailureReason = TradeCloseReason.PartnerLeft;
				return null;
			}

			if (!CharacterStateValidation.CanAct(first) || !CharacterStateValidation.CanAct(second))
			{
				commit.FailureReason = TradeCloseReason.CannotAct;
				return null;
			}

			if (!TradeRules.CanTradeTogether(first, second, maxTradeDistance))
			{
				commit.FailureReason = TradeCloseReason.OutOfRange;
				return null;
			}

			if (!TryGetInventory(first, out IInventoryController firstInventory) ||
				!TryGetInventory(second, out IInventoryController secondInventory))
			{
				commit.FailureReason = TradeCloseReason.Failed;
				return null;
			}

			long firstPays = session.First.Currency;
			long secondPays = session.Second.Currency;

			CharacterAttribute firstCurrency = null;
			CharacterAttribute secondCurrency = null;
			if (firstPays > 0 || secondPays > 0)
			{
				TryGetCurrency(first, out firstCurrency);
				TryGetCurrency(second, out secondCurrency);
			}

			/* Both payments taken and both credits given, held, before the items move — checked in
			 * full first, so a refusal here has changed nothing. Credits used to reach memory only in
			 * the finish hop, which left a window in which any other capture of the payee's sheet
			 * overwrote the credited row; see TradeCurrencySettlement. */
			if (!TradeCurrencySettlement.TryOpen(firstCurrency, secondCurrency, firstPays, secondPays,
					out TradeCurrencySettlement currency, out TradeCurrencySettlement.Refusal currencyRefusal))
			{
				Log.Debug("TradeSystem", $"Session {session.ID}: currency refused ({currencyRefusal}).");
				commit.FailureReason = TradeCloseReason.Failed;
				return null;
			}
			commit.Currency = currency;

			/* The offered slots were the reservation while the table was open. The exchange
			 * needs them unlocked — RemoveItem refuses a locked slot — and the inventory
			 * system re-locks every touched slot the moment the apply returns, before anything
			 * else can run. TradeExchange re-reads every slot before it moves anything. */
			ReleaseOfferLocks(session, first, second);
			commit.OfferLocksReleased = true;

			var firstSide = new TradeExchange.Side { Inventory = firstInventory, Offers = session.First.Offers };
			var secondSide = new TradeExchange.Side { Inventory = secondInventory, Offers = session.Second.Offers };

			if (!TradeExchange.TryApply(firstSide, secondSide, out TradeExchange.Failure failure, out TradeExchange.Applied applied))
			{
				CloseCurrency(session, committed: false);
				commit.FailureReason = failure == TradeExchange.Failure.NoRoom ? TradeCloseReason.NoRoom : TradeCloseReason.Failed;
				Log.Debug("TradeSystem", $"Session {session.ID}: exchange refused ({failure}).");
				return null;
			}

			commit.Applied = applied;
			commit.FirstSide = firstSide;
			commit.SecondSide = secondSide;

			bool currencyMoved = firstPays > 0 || secondPays > 0;

			// The sheets are written as memory holds them, credits included; see ItemExchangeLeg.PersistAttributes.
			return new[]
			{
				new ItemExchangeLeg
				{
					Character = first,
					ChangedInventoryItems = firstSide.Changed,
					RemovedInventoryItems = firstSide.Removed,
					TouchedSlots = firstSide.TouchedSlots,
					PersistAttributes = currencyMoved,
					CurrencyPaid = firstPays,
				},
				new ItemExchangeLeg
				{
					Character = second,
					ChangedInventoryItems = secondSide.Changed,
					RemovedInventoryItems = secondSide.Removed,
					TouchedSlots = secondSide.TouchedSlots,
					PersistAttributes = currencyMoved,
					CurrencyPaid = secondPays,
				},
			};
		}

		/// <summary>
		/// The finishing hop. Main thread, every touched slot already unlocked.
		/// </summary>
		private void FinishExchange(TradeSession session, bool committed)
		{
			TradeSession.CommitState commit = session.Commit;
			if (commit == null || commit.Finished)
			{
				return;
			}
			commit.Finished = true;

			TryGetResidentCharacter(session.First.CharacterID, out IPlayerCharacter first);
			TryGetResidentCharacter(session.Second.CharacterID, out IPlayerCharacter second);

			if (committed)
			{
				// The trade is true. The credits are already in memory; they stop being held.
				CloseCurrency(session, committed: true);

				Log.Debug("TradeSystem", $"Session {session.ID} completed: {session.First.CharacterID} gave {session.First.Offers.Count} item(s) + {commit.Currency?.First.Paid ?? 0} currency; {session.Second.CharacterID} gave {session.Second.Offers.Count} item(s) + {commit.Currency?.Second.Paid ?? 0} currency.");

				/* The close goes first, on purpose. The owning client holds its own lock on
				 * every slot it offered (mirrored from the last TradeStateBroadcast), and its
				 * inventory handlers apply a set or a remove through SetItemSlot/RemoveItem,
				 * which refuse a locked slot. TradeClosedBroadcast is what releases those
				 * locks, and the channel is reliable and ordered. */
				CloseSession(session, TradeCloseReason.Completed, TradeCloseReason.Completed);

				if (Server.BehaviourRegistry.TryGet(out ICharacterInventorySystem inventorySystem))
				{
					if (first != null && commit.FirstSide != null)
					{
						inventorySystem.NotifyInventorySlots(first, commit.FirstSide.Changed, commit.FirstSide.EmptiedSlots);
					}
					if (second != null && commit.SecondSide != null)
					{
						inventorySystem.NotifyInventorySlots(second, commit.SecondSide.Changed, commit.SecondSide.EmptiedSlots);
					}
				}
				return;
			}

			// Refused or rolled back: put memory back exactly as it was.
			if (commit.Applied != null)
			{
				commit.Applied.Undo();
			}
			CloseCurrency(session, committed: false);

			Log.Debug("TradeSystem", $"Session {session.ID}: exchange did not commit ({commit.FailureReason}); memory restored.");
			CloseSession(session, commit.FailureReason, commit.FailureReason);
		}

		/// <summary>
		/// Closes the currency settlement the apply opened: keeps the credits on a commit, takes them
		/// back and refunds the payments on a refusal. Idempotent.
		/// </summary>
		/// <remarks>
		/// Works on the attributes the apply resolved, not on a fresh lookup by id, so a party that
		/// has since gone into combat-logout linger is still restored; a party whose pooled object has
		/// been reset for somebody else is left alone (the settlement's token no longer matches).
		/// </remarks>
		private void CloseCurrency(TradeSession session, bool committed)
		{
			TradeCurrencySettlement currency = session.Commit?.Currency;
			if (currency == null || currency.Closed)
			{
				return;
			}

			currency.Close(committed, out TradeCurrencySettlement.LegOutcome first, out TradeCurrencySettlement.LegOutcome second);
			ReportCurrencyClose(session, session.First.CharacterID, first);
			ReportCurrencyClose(session, session.Second.CharacterID, second);
		}

		/// <summary>Logs whatever closing one side's currency could not do exactly.</summary>
		private static void ReportCurrencyClose(TradeSession session, long characterID, TradeCurrencySettlement.LegOutcome outcome)
		{
			if (!outcome.Closed)
			{
				Log.Debug("TradeSystem", $"Session {session.ID}: character {characterID}'s currency was no longer this trade's to settle (the character left and its object was reused).");
				return;
			}
			if (outcome.CreditShortfall > 0 || outcome.RefundShortfall > 0)
			{
				// Only reachable through a write that bypasses CharacterCurrency while the trade settled
				// (an operator setting the balance), or a balance pushed to the int ceiling meanwhile.
				Log.Error("TradeSystem",
					$"Session {session.ID}: reversing character {characterID}'s currency was inexact — {outcome.CreditShortfall} of the credit could not be taken back and {outcome.RefundShortfall} of the payment could not be refunded.");
			}
		}

		/// <summary>The character's currency attribute, when the trade has a currency configured and the character has it.</summary>
		private bool TryGetCurrency(IPlayerCharacter character, out CharacterAttribute currency)
		{
			currency = null;
			return currencyTemplate != null &&
				character != null &&
				character.TryGet(out ICharacterAttributeController attributes) &&
				attributes.TryGetAttribute(currencyTemplate, out currency) &&
				currency != null;
		}

		/// <summary>
		/// A committing session whose outcome never arrived. The inventory system posts its
		/// finish with retries, so this only fires when the main thread has been unreachable
		/// for a minute — a server that is already in trouble. Treated as a refusal: memory
		/// is restored, and a commit that did in fact land is undone by the reconcile that
		/// follows, which is consistent in the only direction that matters — nothing is
		/// duplicated.
		/// </summary>
		private void TimeOutCommit(TradeSession session)
		{
			Log.Error("TradeSystem", $"Session {session.ID}: exchange outcome not received within {CommitTimeoutSeconds:0}s; treating as refused.");
			FinishExchange(session, committed: false);
		}

		/// <summary>Unlocks every offered slot on both sides, for the commit path.</summary>
		private void ReleaseOfferLocks(TradeSession session, IPlayerCharacter first, IPlayerCharacter second)
		{
			UnlockOffers(first, session.First);
			UnlockOffers(second, session.Second);
		}
	}
}
