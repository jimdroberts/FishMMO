using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Shared;
using FishMMO.Logging;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using System.Collections.Generic;
using System;
using System.Threading.Tasks;

namespace FishMMO.Server.Implementation.World.SceneServer.Interactable
{
	/// <summary>
	/// Ability crafting: validates crafting requests, learns crafted abilities, and persists them to the database.
	/// </summary>
	public partial class InteractableSystem
	{
		/// <summary>
		/// Handles an incoming ability crafting request and validates cost, ownership, and selected events.
		/// </summary>
		/// <param name="conn">Requesting client connection.</param>
		/// <param name="msg">Ability crafting request payload.</param>
		/// <param name="channel">Transport channel used by FishNet.</param>
		public void OnServerAbilityCraftBroadcastReceived(NetworkConnection conn, AbilityCraftBroadcast msg, Channel channel)
		{
			if (conn == null)
			{
				return;
			}

			// validate connection character
			if (conn.FirstObject == null)
			{
				return;
			}
			IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (character == null ||
				!character.TryGet(out IAbilityController abilityController))
			{
				return;
			}

			if (!Server.DataContainerRegistry.TryGet<IInteractableSystemRuntimeData>(out var runtimeData))
			{
				return;
			}


			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.AbilityCraft, out long guardKey))
			{
				return;
			}

			try
			{

				// validate main ability exists
				AbilityTemplate mainAbility = AbilityTemplate.Get<AbilityTemplate>(msg.TemplateID);
				if (mainAbility == null)
				{
					return;
				}

				// validate scene
				if (worldSceneDetailsCache == null ||
					!worldSceneDetailsCache.Scenes.TryGetValue(character.SceneName, out _))
				{
					Log.Debug("InteractableSystem", "Missing Scene:" + character.SceneName);
					return;
				}

				// validate scene object
				if (!ValidateSceneObject(msg.InteractableID, character.GameObject.scene.handle, out ISceneObject sceneObject))
				{
					return;
				}

				// validate interactable
				IInteractable interactable = sceneObject.GameObject.GetComponent<IInteractable>();
				if (interactable == null ||
					!interactable.InRange(character.Transform))
				{
					return;
				}

				// validate the character can learn the ability
				if (!abilityController.KnowsAbility(mainAbility.ID) ||
					abilityController.KnowsLearnedAbility(mainAbility.ID) ||
					abilityController.KnownAbilities.Count >= maxAbilityCount)
				{
					return;
				}

				int price = mainAbility.Price;

				// validate eventIds if there are any...
				if (msg.Events != null)
				{
					// Defense-in-depth: cap event list size to prevent processing oversized payloads.
					if (msg.Events.Count > maxAbilityCraftEvents)
					{
						return;
					}

					HashSet<int> validatedEvents = new HashSet<int>();
					for (int i = 0; i < msg.Events.Count; ++i)
					{
						int id = msg.Events[i];
						if (validatedEvents.Contains(id))
						{
							// duplicate events
							return;
						}
						validatedEvents.Add(id);
						AbilityEvent abilityEvent = AbilityEvent.Get<AbilityEvent>(id);
						if (abilityEvent == null)
						{
							// unknown ability event
							return;
						}

						// validate that the character knows the ability event
						if (!abilityController.KnowsAbilityEvent(abilityEvent.ID))
						{
							return;
						}

						price += abilityEvent.Price;
					}
				}

				// do we have enough currency to purchase this?
				if (currencyTemplate == null)
				{
					Log.Debug("InteractableSystem", "currencyTemplate is null.");
					return;
				}
				if (!character.TryGet(out ICharacterAttributeController attributeController) ||
					!attributeController.TryGetAttribute(currencyTemplate, out CharacterAttribute currency) ||
					currency.FinalValue < price)
				{
					return;
				}

				Ability newAbility = LearnAbility(abilityController, mainAbility, msg.Events);
				if (newAbility != null)
				{
					currency.AddValue(-price);

					AbilityAddBroadcast abilityAddBroadcast = new AbilityAddBroadcast()
					{
						ID = newAbility.ID,
						TemplateID = newAbility.Template.ID,
						Events = msg.Events,
					};

					Server.NetworkWrapper.Broadcast(conn, abilityAddBroadcast, true, Channel.Reliable);
				}
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		/// <summary>
		/// Creates and learns a new crafted ability, then schedules asynchronous persistence.
		/// </summary>
		/// <param name="abilityController">Ability controller receiving the new ability.</param>
		/// <param name="abilityTemplate">Base ability template used for creation.</param>
		/// <param name="abilityEvents">Selected ability event identifiers to attach.</param>
		/// <returns>The created ability instance.</returns>
		public Ability LearnAbility(IAbilityController abilityController, AbilityTemplate abilityTemplate, List<int> abilityEvents)
		{
			Ability newAbility = new Ability(abilityTemplate, abilityEvents);

			// Fire-and-forget: persist the ability to the database
			long charID = abilityController.Character.ID;
			newAbility.Version++;
			var abilityData = new CharacterAbilityData(
				id: newAbility.ID,
				version: newAbility.Version,
				characterID: charID,
				templateID: newAbility.Template.ID,
				abilityEvents: abilityEvents,
				cooldown: 0f
			);
			if (!TryEnqueueAsyncWork(() => PersistAbilityAsync(abilityData), charID))
			{
				Log.Warning("InteractableSystem", $"LearnAbility: Async worker rejected learned-ability persist for CharID={charID}, AbilityID={newAbility.ID}.");
				return null;
			}

			abilityController.LearnAbility(newAbility);

			return newAbility;
		}

		/// <summary>
		/// Persists an ability to the database asynchronously.
		/// </summary>
		private async Task PersistAbilityAsync(CharacterAbilityData abilityData)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null)
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<ICharacterAbilityService>(out var abilityService))
				{
					return;
				}

				await abilityService.PersistAsync(abilityData);
			}
			catch (Exception ex)
			{
				await Log.Error("InteractableSystem", $"Error persisting ability: {ex}");
			}
		}
	}
}