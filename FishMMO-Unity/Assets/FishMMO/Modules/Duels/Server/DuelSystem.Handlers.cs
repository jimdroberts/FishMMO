using System.Collections.Generic;
using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Server.Implementation;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// The duel system's requests: a challenge, its answer, and yielding.
	/// </summary>
	/// <remarks>
	/// Every refusal is answered (<see cref="DuelRequestResultBroadcast"/>): a challenge that silently went nowhere reads
	/// to the player as a menu entry that does not work. The rules a challenge is sent under are applied again to its
	/// acceptance, because time has passed: the two may have walked apart, entered combat or taken another duel.
	/// </remarks>
	public partial class DuelSystem
	{
		private void OnDuelRequest(NetworkConnection conn, DuelRequestBroadcast msg, Channel channel)
		{
			/* Not gated here: a challenger who cannot act is told so (CannotAct) rather than ignored, and the same rule
			 * is applied to the acceptance that leads to the duel. */
			if (!TryBeginPlayerRequest(conn, out PlayerRequestContext request, PlayerRequestGate.SkipCanAct))
			{
				return;
			}
			if (!TryBeginIngressGuard(conn, IngressOperation.Request, out long guardKey))
			{
				Send(conn, new DuelRequestResultBroadcast { TargetCharacterID = msg.TargetCharacterID, Failure = DuelRequestFailure.Throttled });
				return;
			}
			try
			{
				IPlayerCharacter challenger = request.Character;
				DuelRequestFailure failure = ValidateChallenge(challenger, msg.TargetCharacterID, out IPlayerCharacter target);
				if (failure != DuelRequestFailure.Sent)
				{
					Send(conn, new DuelRequestResultBroadcast { TargetCharacterID = msg.TargetCharacterID, Failure = failure });
					return;
				}

				PendingInvite invite = new PendingInvite
				{
					ChallengerID = challenger.ID,
					TargetID = target.ID,
					ExpiresAt = Now + inviteTtlSeconds,
				};
				invitesByTarget[target.ID] = invite;
				invitesByChallenger[challenger.ID] = invite;

				Send(target, new DuelInviteBroadcast
				{
					ChallengerCharacterID = challenger.ID,
					ChallengerName = challenger.CharacterName,
					ExpiresInSeconds = inviteTtlSeconds,
				});
				Send(conn, new DuelRequestResultBroadcast { TargetCharacterID = target.ID, Failure = DuelRequestFailure.Sent });
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		/// <summary>Everything that must hold for a challenge to be sent.</summary>
		private DuelRequestFailure ValidateChallenge(IPlayerCharacter challenger, long targetID, out IPlayerCharacter target)
		{
			target = null;
			if (!CharacterStateValidation.CanAct(challenger))
			{
				return DuelRequestFailure.CannotAct;
			}
			if (targetID <= 0 || targetID == challenger.ID || !TryGetResidentCharacter(targetID, out target))
			{
				return DuelRequestFailure.TargetUnavailable;
			}
			if (IsBusy(challenger.ID))
			{
				return DuelRequestFailure.SelfBusy;
			}
			if (IsBusy(target.ID))
			{
				return DuelRequestFailure.TargetBusy;
			}
			if (IsOnCooldown(challenger.ID, target.ID))
			{
				return DuelRequestFailure.Throttled;
			}
			return ValidatePair(challenger, target);
		}

		/// <summary>The rules both a challenge and its acceptance answer to, once neither is busy with another duel.</summary>
		private DuelRequestFailure ValidatePair(IPlayerCharacter challenger, IPlayerCharacter target)
		{
			if (!CharacterStateValidation.CanAct(challenger))
			{
				return DuelRequestFailure.CannotAct;
			}
			if (!CharacterStateValidation.CanAct(target))
			{
				return DuelRequestFailure.TargetUnavailable;
			}
			/* Open world only: an instance (a dungeon, an arena match) has its own rules for who fights whom, and a duel
			 * ground in a corridor would rarely fit anyway. */
			if (challenger.IsInInstance() || target.IsInInstance() || ArenaTeamRegistry.IsArena(challenger.GameObject.scene.handle))
			{
				return DuelRequestFailure.NotHere;
			}
			// Not out of a fight already under way: a duel must not be a way to wall oneself off from a monster or a player.
			if (challenger.IsFlagged(CharacterFlags.IsInCombat) || target.IsFlagged(CharacterFlags.IsInCombat))
			{
				return DuelRequestFailure.InCombat;
			}
			if (challenger.Transform == null || target.Transform == null || challenger.Motor == null || target.Motor == null ||
				challenger.GameObject.scene != target.GameObject.scene)
			{
				return DuelRequestFailure.TargetUnavailable;
			}
			if ((challenger.Transform.position - target.Transform.position).sqrMagnitude > challengeRange * challengeRange)
			{
				return DuelRequestFailure.OutOfRange;
			}
			return DuelRequestFailure.Sent;
		}

		private bool IsBusy(long characterID)
		{
			return duelsByCharacter.ContainsKey(characterID) || invitesByChallenger.ContainsKey(characterID) || invitesByTarget.ContainsKey(characterID);
		}

		private void OnDuelInviteResponse(NetworkConnection conn, DuelInviteResponseBroadcast msg, Channel channel)
		{
			/* Not gated: declining is not an action, and a player who cannot act must still be able to say no (and is
			 * refused by the acceptance's own rules if they say yes). */
			if (!TryBeginPlayerRequest(conn, out PlayerRequestContext request, PlayerRequestGate.SkipCanAct))
			{
				return;
			}
			if (!TryBeginIngressGuard(conn, IngressOperation.Respond, out long guardKey))
			{
				return;
			}
			try
			{
				IPlayerCharacter target = request.Character;
				// The answer names its challenge: a prompt left open past one challenge must not accept the next.
				if (!invitesByTarget.TryGetValue(target.ID, out PendingInvite invite) || invite.ChallengerID != msg.ChallengerCharacterID)
				{
					return;
				}
				RemoveInvite(invite);

				bool challengerHere = TryGetResidentCharacter(invite.ChallengerID, out IPlayerCharacter challenger);
				if (!msg.Accept)
				{
					ArmCooldown(invite.ChallengerID, invite.TargetID);
					if (challengerHere)
					{
						Send(challenger, new DuelRequestResultBroadcast { TargetCharacterID = target.ID, Failure = DuelRequestFailure.Declined });
					}
					return;
				}
				if (!challengerHere)
				{
					Send(target, new DuelRequestResultBroadcast { TargetCharacterID = invite.ChallengerID, Failure = DuelRequestFailure.TargetUnavailable });
					return;
				}

				// The challenge itself was taken off the books above, so busy now means another duel or challenge.
				DuelRequestFailure failure = IsBusy(challenger.ID) || IsBusy(target.ID)
					? DuelRequestFailure.TargetBusy
					: ValidatePair(challenger, target);
				DuelArena arena = default;
				UnityEngine.Vector3 firstStart = default;
				UnityEngine.Vector3 secondStart = default;
				if (failure == DuelRequestFailure.Sent && !TryPlaceArena(challenger, target, out arena, out firstStart, out secondStart))
				{
					failure = DuelRequestFailure.NoRoom;
				}
				if (failure != DuelRequestFailure.Sent)
				{
					// Both asked for the duel, so both hear why it is not happening.
					Send(challenger, new DuelRequestResultBroadcast { TargetCharacterID = target.ID, Failure = failure });
					Send(target, new DuelRequestResultBroadcast { TargetCharacterID = challenger.ID, Failure = failure });
					return;
				}
				StartDuel(challenger, target, arena, firstStart, secondStart);
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		private void OnDuelYield(NetworkConnection conn, DuelYieldBroadcast msg, Channel channel)
		{
			/* Not gated: a stunned or rooted duellist may still give up; yielding only ends the duel they are in. */
			if (!TryBeginPlayerRequest(conn, out PlayerRequestContext request, PlayerRequestGate.SkipCanAct))
			{
				return;
			}
			if (!TryBeginIngressGuard(conn, IngressOperation.Yield, out long guardKey))
			{
				return;
			}
			try
			{
				if (duelsByCharacter.TryGetValue(request.Character.ID, out Duel duel))
				{
					EndDuel(duel, request.Character.ID, false, DuelEndReason.Yielded);
				}
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		// ── Challenges ────────────────────────────────────────────────────────────────────

		private void RemoveInvite(PendingInvite invite)
		{
			if (invite == null)
			{
				return;
			}
			if (invitesByTarget.TryGetValue(invite.TargetID, out PendingInvite byTarget) && ReferenceEquals(byTarget, invite))
			{
				invitesByTarget.Remove(invite.TargetID);
			}
			if (invitesByChallenger.TryGetValue(invite.ChallengerID, out PendingInvite byChallenger) && ReferenceEquals(byChallenger, invite))
			{
				invitesByChallenger.Remove(invite.ChallengerID);
			}
		}

		/// <summary>Drops every challenge a character sent or was sent, telling a challenger still here.</summary>
		private void DropInvitesInvolving(long characterID)
		{
			if (invitesByTarget.TryGetValue(characterID, out PendingInvite asTarget))
			{
				RemoveInvite(asTarget);
				if (TryGetResidentCharacter(asTarget.ChallengerID, out IPlayerCharacter challenger))
				{
					Send(challenger, new DuelRequestResultBroadcast { TargetCharacterID = asTarget.TargetID, Failure = DuelRequestFailure.TargetUnavailable });
				}
			}
			if (invitesByChallenger.TryGetValue(characterID, out PendingInvite asChallenger))
			{
				RemoveInvite(asChallenger);
			}
		}

		/// <summary>Lapses challenges nobody answered, telling the challenger, and forgets lapsed cooldowns. Once a second.</summary>
		private void SweepInvites()
		{
			double now = Now;
			if (now < nextInviteSweep)
			{
				return;
			}
			nextInviteSweep = now + 1.0;
			if (invitesByTarget.Count == 0 && inviteCooldowns.Count == 0)
			{
				return;
			}

			List<PendingInvite> expired = null;
			foreach (PendingInvite invite in invitesByTarget.Values)
			{
				if (invite.ExpiresAt <= now)
				{
					(expired ??= new List<PendingInvite>()).Add(invite);
				}
			}
			if (expired != null)
			{
				for (int i = 0; i < expired.Count; ++i)
				{
					PendingInvite invite = expired[i];
					RemoveInvite(invite);
					ArmCooldown(invite.ChallengerID, invite.TargetID);
					if (TryGetResidentCharacter(invite.ChallengerID, out IPlayerCharacter challenger))
					{
						Send(challenger, new DuelRequestResultBroadcast { TargetCharacterID = invite.TargetID, Failure = DuelRequestFailure.Expired });
					}
				}
			}

			if (inviteCooldowns.Count > 0)
			{
				List<(long, long)> lapsed = null;
				foreach (KeyValuePair<(long, long), double> kvp in inviteCooldowns)
				{
					if (kvp.Value <= now)
					{
						(lapsed ??= new List<(long, long)>()).Add(kvp.Key);
					}
				}
				if (lapsed != null)
				{
					for (int i = 0; i < lapsed.Count; ++i)
					{
						inviteCooldowns.Remove(lapsed[i]);
					}
				}
			}
		}

		private void ArmCooldown(long challengerID, long targetID)
		{
			if (perTargetCooldownSeconds > 0f)
			{
				inviteCooldowns[(challengerID, targetID)] = Now + perTargetCooldownSeconds;
			}
		}

		private bool IsOnCooldown(long challengerID, long targetID)
		{
			return inviteCooldowns.TryGetValue((challengerID, targetID), out double until) && until > Now;
		}
	}
}
