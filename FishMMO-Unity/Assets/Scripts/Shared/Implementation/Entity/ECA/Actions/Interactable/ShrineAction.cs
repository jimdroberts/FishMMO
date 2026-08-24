using System;
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

			if (!player.TryGet(out ICharacterAttributeController attributeController)) return;

			if (template.HealHealth &&
				attributeController.TryGetHealthAttribute(out CharacterResourceAttribute health))
			{
				health.AddToCurrentValue(health.FinalValue * template.HealthHealPercent);
			}

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

			initiator.NetworkObject.Broadcast(new ShrineBroadcast()
			{
				InteractableID = data.Interactable.ID,
				TemplateID = template.ID,
			});

			if (shrine.AchievementTemplate != null &&
				player.TryGet(out IAchievementController achievementController))
			{
				achievementController.Increment(shrine.AchievementTemplate, 1);
			}
		}
	}
}