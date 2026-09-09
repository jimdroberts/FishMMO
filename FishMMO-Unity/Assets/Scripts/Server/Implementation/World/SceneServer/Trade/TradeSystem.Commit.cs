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
	///     re-validate everything as if the trade were proposed now, deduct both currency
	///     offers (an escrow — a concurrent spend can only spend what is left, and a refused
	///     write refunds exactly), apply the item exchange in memory all-or-nothing, and hand
	///     back the rows. Credits are NOT applied here: they ride in the written row and land
	///     in memory only once the commit is known, so a refusal never has to claw back money
	///     the player may already have spent.
	///   </description></item>
	///   <item><description>
	///     <see cref="FinishExchange"/> (main thread, after the commit or the rollback): on
	///     success credit both, tell both clients, close as Completed. On refusal undo the
	///     item exchange exactly (every touched slot was locked throughout), refund the
	///     deductions, close as Failed; the inventory system voids anything captured from the
	///     applied state and reconciles both characters.
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

			if (!ValidateCurrencyLeg(first, second, firstPays) || !ValidateCurrencyLeg(second, first, secondPays))
			{
				commit.FailureReason = TradeCloseReason.Failed;
				return null;
			}

			/* The offered slots were the reservation while the table was open. The exchange
			 * needs them unlocked — RemoveItem refuses a locked slot — and the inventory
			 * system re-locks every touched slot the moment the apply returns, before anything
			 * else can run. TradeExchange re-reads every slot before it moves anything. */
			ReleaseOfferLocks(session, first, second);
			commit.OfferLocksReleased = true;

			// Deduct: the escrow. Refunded exactly on any refusal.
			if (firstPays > 0 && !CharacterCurrency.TrySpend(first, currencyTemplate, firstPays))
			{
				commit.FailureReason = TradeCloseReason.Failed;
				return null;
			}
			commit.FirstDeducted = firstPays;

			if (secondPays > 0 && !CharacterCurrency.TrySpend(second, currencyTemplate, secondPays))
			{
				RefundDeductions(session, first, second);
				commit.FailureReason = TradeCloseReason.Failed;
				return null;
			}
			commit.SecondDeducted = secondPays;

			var firstSide = new TradeExchange.Side { Inventory = firstInventory, Offers = session.First.Offers };
			var secondSide = new TradeExchange.Side { Inventory = secondInventory, Offers = session.Second.Offers };

			if (!TradeExchange.TryApply(firstSide, secondSide, out TradeExchange.Failure failure, out TradeExchange.Applied applied))
			{
				RefundDeductions(session, first, second);
				commit.FailureReason = failure == TradeExchange.Failure.NoRoom ? TradeCloseReason.NoRoom : TradeCloseReason.Failed;
				Log.Debug("TradeSystem", $"Session {session.ID}: exchange refused ({failure}).");
				return null;
			}

			commit.Applied = applied;
			commit.FirstSide = firstSide;
			commit.SecondSide = secondSide;

			bool currencyMoved = firstPays > 0 || secondPays > 0;
			int currencyTemplateID = currencyTemplate != null ? currencyTemplate.ID : 0;

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
					CurrencyCredit = secondPays,
					CurrencyTemplateID = currencyTemplateID,
				},
				new ItemExchangeLeg
				{
					Character = second,
					ChangedInventoryItems = secondSide.Changed,
					RemovedInventoryItems = secondSide.Removed,
					TouchedSlots = secondSide.TouchedSlots,
					PersistAttributes = currencyMoved,
					CurrencyPaid = secondPays,
					CurrencyCredit = firstPays,
					CurrencyTemplateID = currencyTemplateID,
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
				// The trade is true. Credit what the written rows already hold.
				if (commit.SecondDeducted > 0 && first != null)
				{
					CharacterCurrency.TryAdd(first, currencyTemplate, commit.SecondDeducted);
				}
				if (commit.FirstDeducted > 0 && second != null)
				{
					CharacterCurrency.TryAdd(second, currencyTemplate, commit.FirstDeducted);
				}

				Log.Debug("TradeSystem", $"Session {session.ID} completed: {session.First.CharacterID} gave {session.First.Offers.Count} item(s) + {commit.FirstDeducted} currency; {session.Second.CharacterID} gave {session.Second.Offers.Count} item(s) + {commit.SecondDeducted} currency.");

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
			RefundDeductions(session, first, second);

			Log.Debug("TradeSystem", $"Session {session.ID}: exchange did not commit ({commit.FailureReason}); memory restored.");
			CloseSession(session, commit.FailureReason, commit.FailureReason);
		}

		/// <summary>Gives back what <see cref="ApplyExchange"/> deducted. Idempotent.</summary>
		private void RefundDeductions(TradeSession session, IPlayerCharacter first, IPlayerCharacter second)
		{
			TradeSession.CommitState commit = session.Commit;
			if (commit == null)
			{
				return;
			}
			if (commit.FirstDeducted > 0 && first != null)
			{
				CharacterCurrency.TryAdd(first, currencyTemplate, commit.FirstDeducted);
			}
			if (commit.SecondDeducted > 0 && second != null)
			{
				CharacterCurrency.TryAdd(second, currencyTemplate, commit.SecondDeducted);
			}
			commit.FirstDeducted = 0;
			commit.SecondDeducted = 0;
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
