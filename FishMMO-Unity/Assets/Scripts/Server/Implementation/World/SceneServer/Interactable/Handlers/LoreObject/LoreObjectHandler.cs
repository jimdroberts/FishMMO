using FishMMO.Shared;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared.Core;
using FishNet.Transporting;
using FishNet.Connection;
using System.Collections.Generic;

namespace FishMMO.Server.Implementation.World.SceneServer.Interactable
{
	/// <summary>
	/// Handles lore object interactions. Sends a <see cref="LoreObjectBroadcast"/> to the client
	/// to display the UILore window, and optionally grants abilities, ability events, and items.
	/// Ability/event grants are idempotent; already-known entries are skipped.
	/// </summary>
	[HandlesInteractable(typeof(LoreObject))]
	public class LoreObjectHandler : IInteractableHandler
	{
		private readonly IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> server;

		public LoreObjectHandler(IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> server)
		{
			this.server = server;
		}

		public void HandleInteraction(IInteractable interactable, IPlayerCharacter character, ISceneObject sceneObject, IInteractableSystem interactableSystem)
		{
			ILoreObject loreObject = interactable as ILoreObject;
			if (loreObject == null || loreObject.Template == null)
			{
				return;
			}

			LoreObjectTemplate template = loreObject.Template;

			// Broadcast lore text to the client
			server.NetworkWrapper.Broadcast(character.Owner, new LoreObjectBroadcast()
			{
				InteractableID = sceneObject.ID,
				TemplateID = template.ID,
			}, true, Channel.Reliable);

			// Grant abilities (idempotent — already-known are skipped)
			if (template.GrantAbilities != null && template.GrantAbilities.Count > 0)
			{
				if (character.TryGet(out IAbilityController abilityController))
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
							server.NetworkWrapper.Broadcast(character.Owner, new KnownAbilityAddBroadcast()
							{
								TemplateID = toLearn[i].ID,
							}, true, Channel.Reliable);
						}
					}
				}
			}

			// Grant ability events (idempotent — already-known are skipped)
			if (template.GrantAbilityEvents != null && template.GrantAbilityEvents.Count > 0)
			{
				if (character.TryGet(out IAbilityController abilityController))
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
							server.NetworkWrapper.Broadcast(character.Owner, new KnownAbilityEventAddBroadcast()
							{
								TemplateID = toLearn[i].ID,
							}, true, Channel.Reliable);
						}
					}
				}
			}

			// Grant items
			if (template.GrantItems != null && template.GrantItems.Count > 0)
			{
				if (character.TryGet(out IInventoryController inventoryController))
				{
					for (int i = 0; i < template.GrantItems.Count; i++)
					{
						BaseItemTemplate itemTemplate = template.GrantItems[i];
						if (itemTemplate != null)
						{
							Item newItem = new Item(itemTemplate, 1);
							interactableSystem.SendNewItemBroadcast(character.Owner, character, inventoryController, newItem);
						}
					}
				}
			}

			// Increment achievement
			if (loreObject.AchievementTemplate != null &&
				character.TryGet(out IAchievementController achievementController))
			{
				achievementController.Increment(loreObject.AchievementTemplate, 1);
			}
		}
	}
}