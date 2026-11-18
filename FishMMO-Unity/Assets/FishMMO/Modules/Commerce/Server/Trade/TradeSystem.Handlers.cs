using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Logging;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// The broadcast handlers: invitation, offers, currency, acceptance, cancellation.
	/// </summary>
	/// <remarks>
	/// Every handler follows the same shape: ingress guard, resolve and validate the sender
	/// through <see cref="CharacterStateValidation.CanAct(IPlayerCharacter)"/>, find the session
	/// they are a party to, ask <see cref="TradeSession"/> whether the change is allowed, apply
	/// the side effect (a slot lock, an unlock), then send both parties the new table. A
	/// refused change answers the sender alone with a <see cref="TradeRefusedBroadcast"/> AND
	/// the current table, so a client that acted on a stale view is corrected and told why
	/// rather than left waiting.
	/// </remarks>
	public partial class TradeSystem
	{
		// ── Invitation ──────────────────────────────────────────────────────────────────────

		private void OnServerTradeRequestReceived(NetworkConnection conn, TradeRequestBroadcast msg, Channel channel)
		{
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			IPlayerCharacter requester = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (requester == null)
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.Request, out long guardKey))
			{
				Send(conn, new TradeRequestResultBroadcast { TargetCharacterID = msg.TargetCharacterID, Failure = TradeRequestFailure.Throttled });
				return;
			}

			try
			{
				TradeRequestFailure failure = ValidateRequest(requester, msg.TargetCharacterID, out IPlayerCharacter target);
				if (failure != TradeRequestFailure.None)
				{
					Send(conn, new TradeRequestResultBroadcast { TargetCharacterID = msg.TargetCharacterID, Failure = failure });
					return;
				}

				var invite = new PendingInvite
				{
					RequesterID = requester.ID,
					TargetID = target.ID,
					ExpiresAt = Now + inviteTtlSeconds,
				};
				invitesByTarget[target.ID] = invite;
				invitesByRequester[requester.ID] = invite;

				Send(target, new TradeInviteBroadcast
				{
					RequesterCharacterID = requester.ID,
					RequesterName = requester.CharacterName,
					ExpiresInSeconds = inviteTtlSeconds,
				});
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		/// <summary>
		/// Everything that must be true for an invitation to be sent. The same rules are
		/// applied again when the target answers, because time has passed.
		/// </summary>
		/// <remarks>
		/// ONE TRADE PER CHARACTER. A character in a session, or with an invitation out or
		/// pending, is busy; both sides are checked, so nobody can be in two conversations at
		/// once from either end.
		/// </remarks>
		private TradeRequestFailure ValidateRequest(IPlayerCharacter requester, long targetID, out IPlayerCharacter target)
		{
			target = null;

			if (!CharacterStateValidation.CanAct(requester))
			{
				return TradeRequestFailure.CannotAct;
			}

			if (targetID <= 0 || targetID == requester.ID || !TryGetResidentCharacter(targetID, out target))
			{
				return TradeRequestFailure.TargetUnavailable;
			}

			if (sessionsByCharacter.ContainsKey(requester.ID) || invitesByRequester.ContainsKey(requester.ID) || invitesByTarget.ContainsKey(requester.ID))
			{
				return TradeRequestFailure.SelfBusy;
			}

			if (sessionsByCharacter.ContainsKey(target.ID) || invitesByTarget.ContainsKey(target.ID) || invitesByRequester.ContainsKey(target.ID))
			{
				return TradeRequestFailure.TargetBusy;
			}

			if (IsInviteOnCooldown(requester.ID, target.ID))
			{
				return TradeRequestFailure.Throttled;
			}

			if (!CharacterStateValidation.CanAct(target))
			{
				return TradeRequestFailure.TargetUnavailable;
			}

			if (!TradeRules.CanTradeTogether(requester, target, maxTradeDistance))
			{
				return TradeRequestFailure.OutOfRange;
			}

			return TradeRequestFailure.None;
		}

		private void OnServerTradeRequestResponseReceived(NetworkConnection conn, TradeRequestResponseBroadcast msg, Channel channel)
		{
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			IPlayerCharacter target = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (target == null)
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.Respond, out long guardKey))
			{
				return;
			}

			try
			{
				/* The answer names the invitation it answers. A dialog left open past one
				 * invitation's expiry must not accept the next person's; a mismatch is ignored
				 * rather than treated as an answer to whatever is pending now. */
				if (!invitesByTarget.TryGetValue(target.ID, out PendingInvite invite) ||
					invite.RequesterID != msg.RequesterCharacterID)
				{
					return;
				}

				RemoveInvite(invite);

				bool requesterPresent = TryGetResidentCharacter(invite.RequesterID, out IPlayerCharacter requester);

				if (!msg.Accept)
				{
					ArmInviteCooldown(invite.RequesterID, invite.TargetID);
					if (requesterPresent)
					{
						Send(requester, new TradeRequestResultBroadcast { TargetCharacterID = target.ID, Failure = TradeRequestFailure.Declined });
					}
					return;
				}

				// Re-validated in full: the players may have walked apart, died, or opened
				// another trade while the prompt was up.
				if (!requesterPresent)
				{
					Send(target, new TradeClosedBroadcast { Reason = TradeCloseReason.PartnerLeft });
					return;
				}

				TradeRequestFailure failure = ValidateAcceptance(requester, target);
				if (failure != TradeRequestFailure.None)
				{
					Send(requester, new TradeRequestResultBroadcast { TargetCharacterID = target.ID, Failure = failure });
					// The target asked for the trade too; a silent no-show would look like a bug.
					Send(target, new TradeClosedBroadcast { Reason = failure == TradeRequestFailure.OutOfRange ? TradeCloseReason.OutOfRange : TradeCloseReason.Failed });
					return;
				}

				OpenSession(requester, target);
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		/// <summary>The request rules, minus the pending-invitation checks that no longer apply.</summary>
		private TradeRequestFailure ValidateAcceptance(IPlayerCharacter requester, IPlayerCharacter target)
		{
			if (!CharacterStateValidation.CanAct(requester))
			{
				return TradeRequestFailure.CannotAct;
			}
			if (!CharacterStateValidation.CanAct(target))
			{
				return TradeRequestFailure.TargetUnavailable;
			}
			if (sessionsByCharacter.ContainsKey(requester.ID))
			{
				return TradeRequestFailure.SelfBusy;
			}
			if (sessionsByCharacter.ContainsKey(target.ID))
			{
				return TradeRequestFailure.TargetBusy;
			}
			if (!TradeRules.CanTradeTogether(requester, target, maxTradeDistance))
			{
				return TradeRequestFailure.OutOfRange;
			}
			return TradeRequestFailure.None;
		}

		// ── The table ───────────────────────────────────────────────────────────────────────

		/// <summary>
		/// Resolves the sender and the open session they are a party to.
		/// </summary>
		/// <returns>
		/// <see cref="TradeRefusalReason.None"/> with both out values set, or the reason the
		/// request cannot be considered. A sender with no open session at all gets nothing back:
		/// there is no window on their screen to tell.
		/// </returns>
		private TradeRefusalReason TryResolveOpenSession(NetworkConnection conn, out IPlayerCharacter character, out TradeSession session)
		{
			character = null;
			session = null;

			if (conn == null || conn.FirstObject == null)
			{
				return TradeRefusalReason.NotOpen;
			}

			character = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (character == null)
			{
				return TradeRefusalReason.NotOpen;
			}

			if (!sessionsByCharacter.TryGetValue(character.ID, out session) || session.Phase != TradePhase.Open)
			{
				return TradeRefusalReason.NotOpen;
			}

			if (!CharacterStateValidation.CanAct(character))
			{
				return TradeRefusalReason.CannotAct;
			}

			return TradeRefusalReason.None;
		}

		/// <summary>
		/// Answers a refused change: why, and the table as it actually stands.
		/// </summary>
		private void Refuse(IPlayerCharacter character, TradeSession session, TradeRefusalReason reason)
		{
			if (character == null)
			{
				return;
			}
			Send(character, new TradeRefusedBroadcast { Reason = reason });
			if (session != null)
			{
				Send(character, session.BuildStateFor(character.ID));
			}
		}

		private static TradeRefusalReason ToReason(TradeOfferRefusal refusal)
		{
			switch (refusal)
			{
				case TradeOfferRefusal.NotOpen: return TradeRefusalReason.NotOpen;
				case TradeOfferRefusal.AlreadyOffered: return TradeRefusalReason.AlreadyOffered;
				case TradeOfferRefusal.TableFull: return TradeRefusalReason.TableFull;
				case TradeOfferRefusal.NotOffered: return TradeRefusalReason.NotOffered;
				case TradeOfferRefusal.NegativeCurrency: return TradeRefusalReason.InsufficientCurrency;
				case TradeOfferRefusal.StaleVersion: return TradeRefusalReason.StaleVersion;
				case TradeOfferRefusal.TableLocked: return TradeRefusalReason.TableLocked;
				case TradeOfferRefusal.NotLocked: return TradeRefusalReason.NotConfirmed;
				case TradeOfferRefusal.NotAParty: return TradeRefusalReason.NotOpen;
				default: return TradeRefusalReason.None;
			}
		}

		private void OnServerTradeOfferItemReceived(NetworkConnection conn, TradeOfferItemBroadcast msg, Channel channel)
		{
			TradeRefusalReason gate = TryResolveOpenSession(conn, out IPlayerCharacter character, out TradeSession session);
			if (gate != TradeRefusalReason.None)
			{
				Refuse(character, session, gate);
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.Offer, out long guardKey))
			{
				return;
			}

			try
			{
				if (!TryGetInventory(character, out IInventoryController inventory) ||
					!inventory.CanManipulate() ||
					!inventory.IsValidSlot(msg.Slot) ||
					!inventory.TryGetItem(msg.Slot, out Item item) ||
					item == null ||
					item.Template == null)
				{
					Refuse(character, session, TradeRefusalReason.InvalidSlot);
					return;
				}

				if (inventory.IsSlotLocked(msg.Slot))
				{
					// Already on the table (its own lock), or reserved by something else.
					Refuse(character, session, session.PartyOf(character.ID).IndexOfSlot(msg.Slot) >= 0
						? TradeRefusalReason.AlreadyOffered
						: TradeRefusalReason.ItemLocked);
					return;
				}

				/* An item the database has not written yet has no identity to re-check at
				 * completion and no row to move. Its slot is locked by the grant path until the
				 * identity lands, so this is unreachable in practice; refused here so the rule
				 * does not depend on that. */
				if (item.ID <= 0)
				{
					Refuse(character, session, TradeRefusalReason.ItemNotReady);
					return;
				}

				if (!TradeRules.TryResolveOfferAmount(item, msg.Amount, out uint amount))
				{
					Refuse(character, session, TradeRefusalReason.BadAmount);
					return;
				}

				var offer = new TradeOffer(
					msg.Slot,
					item.ID,
					item.Template.ID,
					item.IsGenerated ? item.Generator.Seed : 0,
					amount);

				TradeOfferRefusal refusal = session.TryAddOffer(character.ID, offer);
				if (refusal != TradeOfferRefusal.None)
				{
					Refuse(character, session, ToReason(refusal));
					return;
				}

				// Reserved: nothing can move, split, merge, sell, mail, equip or consume it now.
				inventory.LockSlot(msg.Slot);
				BroadcastState(session);
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		private void OnServerTradeWithdrawItemReceived(NetworkConnection conn, TradeWithdrawItemBroadcast msg, Channel channel)
		{
			TradeRefusalReason gate = TryResolveOpenSession(conn, out IPlayerCharacter character, out TradeSession session);
			if (gate != TradeRefusalReason.None)
			{
				Refuse(character, session, gate);
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.Withdraw, out long guardKey))
			{
				return;
			}

			try
			{
				TradeOfferRefusal refusal = session.TryRemoveOffer(character.ID, msg.Slot, out TradeOffer removed);
				if (refusal != TradeOfferRefusal.None)
				{
					Refuse(character, session, ToReason(refusal));
					return;
				}

				if (TryGetInventory(character, out IInventoryController inventory))
				{
					inventory.UnlockSlot(removed.Slot);
				}
				BroadcastState(session);
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		private void OnServerTradeSetCurrencyReceived(NetworkConnection conn, TradeSetCurrencyBroadcast msg, Channel channel)
		{
			TradeRefusalReason gate = TryResolveOpenSession(conn, out IPlayerCharacter character, out TradeSession session);
			if (gate != TradeRefusalReason.None)
			{
				Refuse(character, session, gate);
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.Currency, out long guardKey))
			{
				return;
			}

			try
			{
				if (msg.Amount < 0)
				{
					Refuse(character, session, TradeRefusalReason.InsufficientCurrency);
					return;
				}

				/* The balance is checked here so the table never shows money the player does
				 * not have, and checked again at completion because it may have been spent in
				 * between. The BASE value, never FinalValue — see CharacterCurrency. */
				if (msg.Amount > 0)
				{
					if (currencyTemplate == null)
					{
						Refuse(character, session, TradeRefusalReason.NoCurrency);
						return;
					}
					if (!CharacterCurrency.TryGetBalance(character, currencyTemplate, out long balance) || balance < msg.Amount)
					{
						Refuse(character, session, TradeRefusalReason.InsufficientCurrency);
						return;
					}
				}

				TradeOfferRefusal refusal = session.TrySetCurrency(character.ID, msg.Amount);
				if (refusal != TradeOfferRefusal.None)
				{
					Refuse(character, session, ToReason(refusal));
					return;
				}

				BroadcastState(session);
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		private void OnServerTradeConfirmReceived(NetworkConnection conn, TradeConfirmBroadcast msg, Channel channel)
		{
			TradeRefusalReason gate = TryResolveOpenSession(conn, out IPlayerCharacter character, out TradeSession session);
			if (gate != TradeRefusalReason.None)
			{
				Refuse(character, session, gate);
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.Confirm, out long guardKey))
			{
				return;
			}

			try
			{
				if (msg.Confirm)
				{
					/* Room is checked when an offer is declared final, so a player who cannot
					 * hold what they are about to be given hears it while the table is still
					 * theirs to trim. It is checked again at the accept and enforced for real
					 * by the exchange, which is all-or-nothing on its own. */
					TradeRefusalReason room = CheckRoom(session, character);
					if (room != TradeRefusalReason.None)
					{
						Refuse(character, session, room);
						return;
					}
				}

				TradeOfferRefusal refusal = session.TryConfirm(character.ID, msg.Confirm, msg.Version);
				if (refusal != TradeOfferRefusal.None)
				{
					Refuse(character, session, ToReason(refusal));
					return;
				}

				BroadcastState(session);
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		private void OnServerTradeAcceptReceived(NetworkConnection conn, TradeAcceptBroadcast msg, Channel channel)
		{
			TradeRefusalReason gate = TryResolveOpenSession(conn, out IPlayerCharacter character, out TradeSession session);
			if (gate != TradeRefusalReason.None)
			{
				Refuse(character, session, gate);
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.Accept, out long guardKey))
			{
				return;
			}

			try
			{
				if (msg.Accept)
				{
					/* Checked again here, not only at the confirmation: the partner may have
					 * spent the interval filling their own bag, and a refusal now still leaves
					 * the trade recoverable by revoking. The exchange remains all-or-nothing on
					 * its own; this is feedback, not the decision. */
					TradeRefusalReason room = CheckRoom(session, character);
					if (room != TradeRefusalReason.None)
					{
						Refuse(character, session, room);
						return;
					}
				}

				TradeOfferRefusal refusal = session.TryAccept(character.ID, msg.Accept, msg.Version);
				if (refusal != TradeOfferRefusal.None)
				{
					// A stale accept is answered with the current table, not silence: the
					// client's version is behind and this is what brings it forward.
					Refuse(character, session, ToReason(refusal));
					return;
				}

				if (session.BothAccepted)
				{
					// Both parties get the "both accepted" table before the outcome, so a
					// completion that takes a frame does not look like a hang.
					BroadcastState(session);
					TryCommit(session);
					return;
				}

				BroadcastState(session);
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		/// <summary>
		/// Whether both bags can take what the other side is offering, from the point of view
		/// of <paramref name="character"/>: their own shortfall is <see cref="TradeRefusalReason.NoRoom"/>,
		/// the partner's is <see cref="TradeRefusalReason.PartnerNoRoom"/>.
		/// </summary>
		private TradeRefusalReason CheckRoom(TradeSession session, IPlayerCharacter character)
		{
			TradeSession.Party own = session.PartyOf(character.ID);
			TradeSession.Party partner = session.PartnerOf(character.ID);
			if (own == null || partner == null)
			{
				return TradeRefusalReason.NotOpen;
			}

			if (TryGetInventory(character, out IInventoryController ownInventory) &&
				!TradeRules.HasRoomFor(ownInventory, partner.ToEntries(), own.ToEntries(), out _, out _))
			{
				return TradeRefusalReason.NoRoom;
			}

			if (TryGetResidentCharacter(partner.CharacterID, out IPlayerCharacter partnerCharacter) &&
				TryGetInventory(partnerCharacter, out IInventoryController partnerInventory) &&
				!TradeRules.HasRoomFor(partnerInventory, own.ToEntries(), partner.ToEntries(), out _, out _))
			{
				return TradeRefusalReason.PartnerNoRoom;
			}

			return TradeRefusalReason.None;
		}

		private void OnServerTradeCancelReceived(NetworkConnection conn, TradeCancelBroadcast msg, Channel channel)
		{
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (character == null)
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.Cancel, out long guardKey))
			{
				return;
			}

			try
			{
				/* Deliberately NOT gated on CanAct. A dead or stunned player must still be able
				 * to close their own trade window; the session would close on the next state
				 * tick anyway, and refusing the request only delays that. */
				if (sessionsByCharacter.TryGetValue(character.ID, out TradeSession session))
				{
					if (session.Phase == TradePhase.Open)
					{
						CloseSessionFor(session, character.ID, TradeCloseReason.Cancelled, TradeCloseReason.PartnerCancelled);
					}
					// A committing session cannot be cancelled: the exchange is already decided.
					return;
				}

				// No session: a cancel from a requester withdraws their outstanding invitation.
				if (invitesByRequester.TryGetValue(character.ID, out PendingInvite invite))
				{
					RemoveInvite(invite);
					if (TryGetResidentCharacter(invite.TargetID, out IPlayerCharacter target))
					{
						// The prompt on the target's screen names an invitation that no longer
						// exists; an answer to it would be ignored, so tell them to drop it.
						Send(target, new TradeClosedBroadcast { Reason = TradeCloseReason.PartnerCancelled });
					}
				}
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}
	}
}
