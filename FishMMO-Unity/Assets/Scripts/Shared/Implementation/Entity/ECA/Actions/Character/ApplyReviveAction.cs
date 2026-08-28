using System;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Action that offers a resurrect to a dead target character.
	/// Unlike <see cref="ApplyHealAction"/>, this applies to dead characters (CurrentValue == 0).
	/// Used by resurrect/resurrection ability templates.
	/// <para>
	/// For a player with a live connection this sends a <see cref="ResurrectOfferBroadcast"/> and
	/// stops there — the death dialog surfaces its "Accept Resurrect" button and the player
	/// chooses between that and respawning at their bind point. The revive itself happens in
	/// <c>CharacterSystem</c>'s accept handler, which is the only place that both clears
	/// <see cref="CharacterFlags.IsDead"/> and restores health.
	/// </para>
	/// <para>
	/// A target that cannot be asked — an NPC, or a player with no active connection — is
	/// revived outright, so scripted and system resurrects still work.
	/// </para>
	/// </summary>
	[Serializable]
	public class ApplyReviveAction : BaseAction
	{
		/// <summary>
		/// The value provider that determines the amount of health to restore on resurrect.
		/// </summary>
		[Tooltip("The value provider that determines the amount of health to restore on resurrect.")]
		[SerializeReference, SubclassSelector]
		public IIntValueProvider ReviveValue;

		/// <summary>
		/// Resurrects the target character using the computed value.
		/// </summary>
		/// <param name="initiator">The character casting the resurrect.</param>
		/// <param name="eventData">The event data containing the target information.</param>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			/* Server only. State forwarding is off, so an observer never simulates another
			 * character and has nothing to predict here; the outcome reaches every peer through the
			 * authoritative paths (reconcile, observer broadcast). Running it locally as well would
			 * apply the effect twice on the peer that also happens to be the server, and produce a
			 * value on a client that the server never agreed to. */
			if (!EcaAuthority.IsServer(initiator, eventData))
			{
				return;
			}

			if (ReviveValue == null)
			{
				Log.Warning("ApplyReviveAction", "ReviveValue provider is null.");
				return;
			}

			if (!TryResolveTargetOrInitiator(initiator, eventData, out ICharacter target))
			{
				return;
			}

			if (!target.TryGet(out ICharacterDamageController defenderDamageController))
			{
				return;
			}

			int amount = ReviveValue.GetValue(initiator, eventData);

			/* A player is offered the revive; they are not revived by it.
			 *
			 * This used to call Revive first and then send the offer, which made the offer
			 * meaningless and left the target in a contradictory state: health restored, but
			 * CharacterFlags.IsDead still set because only the accept handler clears it. That
			 * character could not be killed again (Kill early-returns on the flag) yet could be
			 * healed (Heal only tests health), and a player who simply ignored the prompt stayed
			 * that way indefinitely.
			 *
			 * The decision belongs to the player: they may prefer to respawn at their bind point
			 * rather than get up where they fell. CharacterSystem's ResurrectAcceptBroadcast
			 * handler performs the actual revive, and it is the only path that both clears the
			 * flag and restores health. */
			bool canPrompt = target is IPlayerCharacter playerCharacter &&
							 playerCharacter.Owner != null &&
							 playerCharacter.Owner.IsValid &&
							 playerCharacter.NetworkObject != null &&
							 playerCharacter.NetworkObject.IsServerStarted;

			if (!canPrompt)
			{
				/* Nothing to prompt: an NPC, or a player with no live connection to answer —
				 * a combat-logout body, for instance. Revive outright so a system or scripted
				 * resurrect still works on targets that cannot be asked. */
				defenderDamageController.Revive(initiator, amount);
				return;
			}

			/* Hand the offer to the server's character system rather than broadcasting from
			 * here. It records what was offered, by whom, and for how much, so the matching
			 * accept can be checked against a real offer instead of being taken on trust. */
			ICharacterDamageController.OnResurrectOffered?.Invoke(initiator, target, amount);
		}
	}
}
