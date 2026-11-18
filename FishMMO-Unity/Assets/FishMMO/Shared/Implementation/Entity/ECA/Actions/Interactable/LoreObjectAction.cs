using System;
using System.Collections.Generic;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA action for lore object interactions. Sends a <see cref="LoreObjectBroadcast"/>
	/// to display the UILore window, grants abilities and ability events inline (idempotent),
	/// invokes <see cref="PlayerInteractionEventData.OnGrantItem"/> once per item grant so that
	/// <c>InteractableSystem</c> can persist each item to the database, and increments
	/// the achievement counter. Server-only.
	/// </summary>
	[Serializable]
	public class LoreObjectAction : BaseAction
	{
		/// <summary>
		/// Executes the lore object interaction: sends the lore broadcast, grants abilities and
		/// ability events, grants items, and increments the achievement counter.
		/// Server-only.
		/// </summary>
		/// <param name="initiator">The character interacting with the lore object.</param>
		/// <param name="eventData">The event data containing the interaction context.</param>
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			// Server-only. Runtime check, not #if UNITY_SERVER: that define is absent in the
			// editor, where the scene server also runs — see BaseAction.IsServer.
			if (!IsServer(initiator))
			{
				return;
			}

			if (!eventData.TryGet(out PlayerInteractionEventData data) || data.Interactable == null) return;

			ILoreObject loreObject = data.Interactable as ILoreObject;
			if (loreObject?.Template == null) return;

			LoreObjectTemplate template = loreObject.Template;

			// Show the lore window on the client
			SendToOwner(initiator, new LoreObjectBroadcast()
			{
				InteractableID = data.Interactable.ID,
				TemplateID = template.ID,
			});

			// Grant abilities (idempotent — already-known are skipped)
			if (template.GrantAbilities != null && template.GrantAbilities.Count > 0)
			{
				if (initiator.TryGet(out IAbilityController abilityController))
				{
					List<BaseAbilityTemplate> toLearn = new List<BaseAbilityTemplate>();
					for (int i = 0; i < template.GrantAbilities.Count; i++)
					{
						BaseAbilityTemplate ability = template.GrantAbilities[i];
						if (ability != null && !abilityController.KnowsAbility(ability.ID))
						{
							toLearn.Add(ability);
						}
					}

					if (toLearn.Count > 0)
					{
						abilityController.LearnBaseAbilities(toLearn);
						for (int i = 0; i < toLearn.Count; i++)
						{
							SendToOwner(initiator,
								new KnownAbilityAddBroadcast() { TemplateID = toLearn[i].ID });
						}
					}
				}
			}

			// Grant ability events (idempotent — already-known are skipped)
			if (template.GrantAbilityEvents != null && template.GrantAbilityEvents.Count > 0)
			{
				if (initiator.TryGet(out IAbilityController abilityController))
				{
					List<AbilityEvent> toLearn = new List<AbilityEvent>();
					for (int i = 0; i < template.GrantAbilityEvents.Count; i++)
					{
						AbilityEvent abilityEvent = template.GrantAbilityEvents[i];
						if (abilityEvent != null && !abilityController.KnowsAbilityEvent(abilityEvent.ID))
						{
							toLearn.Add(abilityEvent);
						}
					}

					if (toLearn.Count > 0)
					{
						abilityController.LearnAbilityEvents(toLearn);
						for (int i = 0; i < toLearn.Count; i++)
						{
							SendToOwner(initiator,
								new KnownAbilityEventAddBroadcast() { TemplateID = toLearn[i].ID });
						}
					}
				}
			}

			/* Grant items — one time per character, unlike everything above it.
			 *
			 * The ability and event grants are naturally idempotent: the controller is asked what
			 * the character already knows and the already-known are skipped. Items have no such
			 * memory, so without this the whole list was handed out again on every read. The claim
			 * is taken before any item is created so a refusal costs nothing, and it is only taken
			 * when there is something to grant — a lore object with no items must not consume a
			 * claim that a later content edit would then have to work around. */
			if (template.GrantItems != null && template.GrantItems.Count > 0 &&
				loreObject.TryConsumeItemGrant(initiator.ID))
			{
				if (initiator.TryGet(out IInventoryController inventoryController))
				{
					for (int i = 0; i < template.GrantItems.Count; i++)
					{
						BaseItemTemplate itemTemplate = template.GrantItems[i];
						if (itemTemplate != null)
						{
							Item newItem = new Item(itemTemplate, 1);
							data.OnGrantItem?.Invoke(initiator, inventoryController, newItem);
						}
					}
				}
			}

			// Achievement
			if (loreObject.AchievementTemplate != null &&
				initiator.TryGet(out IAchievementController achievementController))
			{
				achievementController.Increment(loreObject.AchievementTemplate, 1);
			}
		}
	}
}