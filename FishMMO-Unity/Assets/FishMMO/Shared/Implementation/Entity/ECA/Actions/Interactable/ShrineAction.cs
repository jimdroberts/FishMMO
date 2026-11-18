using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action that applies healing and/or a buff from a <see cref="IShrine"/>, then sends
	/// <see cref="ShrineBroadcast"/> for client-side VFX/SFX feedback.
	/// Server-only.
	/// </summary>
	[Serializable]
	public class ShrineAction : BaseAction
	{
		/// <summary>
		/// Heals health/mana based on the shrine's template percentages, applies the optional buff,
		/// and broadcasts feedback to the client.
		/// </summary>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			// Server-only. Runtime check, not #if UNITY_SERVER: that define is absent in the
			// editor, where the scene server also runs — see BaseAction.IsServer.
			if (!IsServer(initiator))
			{
				return;
			}

			if (initiator is not IPlayerCharacter player) return;
			if (!eventData.TryGet(out PlayerInteractionEventData data) || data.Interactable == null) return;

			IShrine shrine = data.Interactable as IShrine;
			if (shrine?.Template == null) return;

			ShrineTemplate template = shrine.Template;

			/* A shrine is a restore point between fights unless its template says otherwise.
			 * CharacterStateValidation.CanAct — which every interaction passes through — does not
			 * reject a character in combat, so without this check the shrine was a full heal
			 * available mid-fight. */
			if (!template.UsableInCombat &&
				player.IsFlagged(CharacterFlags.IsInCombat))
			{
				SendShrineResult(player, data.Interactable, template, false, shrine.GetRemainingCooldown(player.ID));
				return;
			}

			/* Spend the cooldown BEFORE anything is applied, and bail out if it is still running.
			 *
			 * Shrine.CanInteract tests the same cooldown, but only the server holds the table, so
			 * a client always believes the shrine is ready and always sends the request. This is
			 * the authoritative gate; CanInteract is the local courtesy that stops a well-behaved
			 * client asking. */
			/* Resolved BEFORE the cooldown is spent. It is the last thing on this path that can
			 * fail, and failing after the consume would burn the player's cooldown on a use that
			 * healed nothing — and return without a reply, so they would not even learn why. */
			if (!player.TryGet(out ICharacterAttributeController attributeController)) return;

			if (!shrine.TryConsumeCooldown(player.ID))
			{
				SendShrineResult(player, data.Interactable, template, false, shrine.GetRemainingCooldown(player.ID));
				return;
			}

			/* Health goes through the damage controller, not straight into the resource.
			 * AddToCurrentValue clamps a number and raises an attribute-changed notification and
			 * nothing else: no OnHealed event, so nothing witnessing the heal reacts to it, and no
			 * check that the character is alive — a shrine would happily restore a corpse's health
			 * while it stayed flagged dead. This is the same routing the buff system's
			 * heal-over-time ticks now use, and for the same reasons. */
			if (template.HealHealth &&
				attributeController.TryGetHealthAttribute(out CharacterResourceAttribute health) &&
				player.TryGet(out ICharacterDamageController damageController))
			{
				int amount = Mathf.RoundToInt(health.FinalValue * template.HealthHealPercent);
				if (amount > 0)
				{
					// The shrine is the source, but there is no character to credit, so no healer.
					damageController.Heal(null, amount);
				}
			}

			// Mana has no damage semantics to borrow; the direct write is correct here.
			if (template.HealMana &&
				attributeController.TryGetManaAttribute(out CharacterResourceAttribute mana))
			{
				mana.AddToCurrentValue(mana.FinalValue * template.ManaHealPercent);
			}

			if (template.Buff != null &&
				player.TryGet(out IBuffController buffController))
			{
				for (int i = 0; i < template.BuffStackCount; i++)
				{
					// Shrine interaction is server-authoritative. ApplyAuthoritative
					// self-corrects to the replicate domain via BuffController's last replicate tick;
					// the tick argument is only used as a fallback pre-first-replication.
					buffController.ApplyAuthoritative(template.Buff, player.GetLocalTick());
				}
			}

			SendShrineResult(player, data.Interactable, template, true, shrine.GetRemainingCooldown(player.ID));

			if (shrine.AchievementTemplate != null &&
				player.TryGet(out IAchievementController achievementController))
			{
				achievementController.Increment(shrine.AchievementTemplate, 1);
			}
		}

		/// <summary>
		/// Sends the one reply every exit from this action owes the player.
		/// </summary>
		/// <param name="player">The interacting player.</param>
		/// <param name="interactable">The shrine that was used.</param>
		/// <param name="template">The shrine's template.</param>
		/// <param name="success">Whether the effects were applied.</param>
		/// <param name="remainingCooldown">Seconds until the shrine is usable again.</param>
		private static void SendShrineResult(
			ICharacter player,
			IInteractable interactable,
			ShrineTemplate template,
			bool success,
			float remainingCooldown)
		{
			SendToOwner(player, new ShrineBroadcast()
			{
				InteractableID = interactable.ID,
				TemplateID = template.ID,
				Success = success,
				RemainingCooldownSeconds = remainingCooldown,
			});
		}
	}
}