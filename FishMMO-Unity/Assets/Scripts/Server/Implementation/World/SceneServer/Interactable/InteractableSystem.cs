using FishNet.Broadcast;
using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Shared;
using FishMMO.Logging;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Server.Implementation;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Linq;
using System;
using System.Threading.Tasks;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.SceneServer.Interactable
{
	/// <summary>
	/// Validates and processes client interactions with interactable objects including merchants, crafting, and dungeon finders.
	/// </summary>
	[CreateAssetMenu(fileName = "InteractableSystem", menuName = "FishMMO/Server/SceneServer/Interactable System", order = 1)]
	[RequiresDataContainer(typeof(InteractableSystemMainThreadQueueData))]
	[RequiresDataContainer(typeof(AsyncWorkerData))]
	public class InteractableSystem : ServerBehaviour
	{
		public WorldSceneDetailsCache WorldSceneDetailsCache;
		public InteractableHandlerInitializer InteractableHandlerInitializer;
		public int MaxAbilityCount = 25;
		public CharacterAttributeTemplate CurrencyTemplate;

		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error("InteractableSystem", "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (InteractableHandlerInitializer == null)
			{
				Log.Error("InteractableSystem", "InitializeOnce: InteractableHandlerInitializer is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (Server.Database?.ServiceRegistry == null)
			{
				Log.Error("InteractableSystem", "InitializeOnce: Database ServiceRegistry is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (!Server.DataContainerRegistry.TryGet<IInteractableSystemMainThreadQueueData>(out _))
			{
				Log.Error("InteractableSystem", "InitializeOnce: IInteractableSystemMainThreadQueueData not found");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			// Interactable handlers
			InteractableHandlerInitializer.RegisterHandlers(Server);

			// Network broadcasts
			Server.NetworkWrapper.RegisterBroadcast<InteractableBroadcast>(OnServerInteractableBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<MerchantPurchaseBroadcast>(OnServerMerchantPurchaseBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<AbilityCraftBroadcast>(OnServerAbilityCraftBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<DungeonFinderBroadcast>(OnServerDungeonFinderBroadcastReceived, true);

			Log.Debug("InteractableSystem", "Initialized");
			return ServerComponentInitializationStatus.Initialized;
		}

		public override void OnDeinitialize()
		{
			if (Server == null)
			{
				Log.Error("InteractableSystem", "OnDeinitialize: Server is null");
				return;
			}

			// Drain any remaining queued main-thread actions
			DrainMainThreadQueue();

			// Interactable handlers
			ClearAllHandlers();

			// Network broadcasts
			Server.NetworkWrapper.UnregisterBroadcast<InteractableBroadcast>(OnServerInteractableBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<MerchantPurchaseBroadcast>(OnServerMerchantPurchaseBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<AbilityCraftBroadcast>(OnServerAbilityCraftBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<DungeonFinderBroadcast>(OnServerDungeonFinderBroadcastReceived);
		}

		public override void OnLateUpdate(float deltaTime)
		{
			DrainMainThreadQueue();
		}

		private void DrainMainThreadQueue()
		{
			if (Server != null &&
				Server.DataContainerRegistry.TryGet<IInteractableSystemMainThreadQueueData>(out var queueData))
			{
				queueData.Drain();
			}
		}

		private void EnqueueMainThread(Action action)
		{
			if (Server != null &&
				Server.DataContainerRegistry.TryGet<IInteractableSystemMainThreadQueueData>(out var queueData))
			{
				queueData.Enqueue(action);
			}
		}

		private static Dictionary<Type, IInteractableHandler> interactableHandlers = new Dictionary<Type, IInteractableHandler>();

		/// <summary>
		/// Registers an interactable handler for a specific type. The type must implement IInteractable.
		/// </summary>
		/// <typeparam name="T">The type of interactable to register the handler for. Must implement IInteractable.</typeparam>
		/// <param name="handler">The handler instance for the specified interactable type.</param>
		public static void RegisterInteractableHandler<T>(IInteractableHandler handler) where T : IInteractable
		{
			Type type = typeof(T);
			if (!interactableHandlers.ContainsKey(type))
			{
				interactableHandlers.Add(type, handler);
				Log.Debug("InteractableSystem", $"Registered handler for {type.Name}");
			}
			else
			{
				Log.Warning("InteractableSystem", $"Handler for type {type.Name} is already registered. Overwriting existing handler.");
				interactableHandlers[type] = handler; // Overwrite in case you want to update handlers
			}
		}

		/// <summary>
		/// Unregisters the handler for a specific interactable type.
		/// </summary>
		/// <typeparam name="T">The type of interactable to unregister the handler for. Must implement IInteractable.</typeparam>
		/// <returns>True if the handler was found and removed; otherwise, false.</returns>
		public static bool UnregisterInteractableHandler<T>() where T : IInteractable
		{
			Type type = typeof(T);
			if (interactableHandlers.Remove(type))
			{
				Log.Debug("InteractableSystem", $"Unregistered handler for {type.Name}");
				return true;
			}
			else
			{
				Log.Warning("InteractableSystem", $"Attempted to unregister handler for type {type.Name}, but no handler was found.");
				return false;
			}
		}

		/// <summary>
		/// Retrieves the interactable handler for a given type.
		/// </summary>
		public static IInteractableHandler GetInteractableHandler<T>()
		{
			Type type = typeof(T);
			interactableHandlers.TryGetValue(type, out var handler);
			return handler;
		}

		public static void ClearAllHandlers()
		{
			interactableHandlers.Clear();
			Log.Debug("InteractableSystem", "All interactable handlers cleared.");
		}

		/// <summary>
		/// Attempts to add items to a characters inventory controller and broadcasts the update to the client.
		/// DB persistence is fire-and-forget async.
		/// </summary>
		public bool SendNewItemBroadcast(NetworkConnection conn, ICharacter character, IInventoryController inventoryController, Item newItem)
		{
			List<InventorySetItemBroadcast> modifiedItemBroadcasts = new List<InventorySetItemBroadcast>();
			List<CharacterInventoryData> itemsToSave = new List<CharacterInventoryData>();

			// see if we have successfully added the item
			if (inventoryController.TryAddItem(newItem, out List<Item> modifiedItems) &&
				modifiedItems != null &&
				modifiedItems.Count > 0)
			{
				// add slot update requests to our message
				foreach (Item item in modifiedItems)
				{
					// just in case..
					if (item == null)
					{
						continue;
					}

					// collect items for async DB persistence
					item.Version++;
					itemsToSave.Add(new CharacterInventoryData(
						id: item.ID,
						version: item.Version,
						characterID: character.ID,
						templateID: item.Template.ID,
						slot: item.Slot,
						seed: item.IsGenerated ? item.Generator.Seed : 0,
						amount: item.IsStackable ? (uint)item.Stackable.Amount : 1
					));

					// create the new item broadcast
					modifiedItemBroadcasts.Add(new InventorySetItemBroadcast()
					{
						InstanceID = item.ID,
						TemplateID = item.Template.ID,
						Slot = item.Slot,
						Seed = item.IsGenerated ? item.Generator.Seed : 0,
						StackSize = item.IsStackable ? item.Stackable.Amount : 0,
					});
				}
			}

			// tell the client they have new items
			if (modifiedItemBroadcasts.Count > 0)
			{
				Server.NetworkWrapper.Broadcast(conn, new InventorySetMultipleItemsBroadcast()
				{
					Items = modifiedItemBroadcasts,
				}, true, Channel.Reliable);

				// Fire-and-forget: persist inventory changes to DB
				if (itemsToSave.Count > 0)
				{
					EnqueueAsyncWork(() => PersistInventoryItemsAsync(itemsToSave));
				}

				return true;
			}
			return false;
		}

		/// <summary>
		/// Persists inventory items to the database asynchronously.
		/// </summary>
		private async Task PersistInventoryItemsAsync(List<CharacterInventoryData> items)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null)
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<ICharacterInventoryService>(out var inventoryService))
				{
					return;
				}

				await inventoryService.PersistAsync(items);
			}
			catch (Exception ex)
			{
				await Log.Error("InteractableSystem", $"Error persisting inventory items: {ex}");
			}
		}

		/// <summary>
		/// Interactable broadcast received from a character.
		/// </summary>
		private void OnServerInteractableBroadcastReceived(NetworkConnection conn, InteractableBroadcast msg, Channel channel)
		{
			if (conn == null)
			{
				Log.Debug("InteractableSystem", "No connnection");
				return;
			}

			// validate connection character
			if (conn.FirstObject == null)
			{
				Log.Debug("InteractableSystem", "No first object");
				return;
			}
			IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (character == null)
			{
				Log.Debug("InteractableSystem", "No character");
				return;
			}

			// validate scene
			if (!WorldSceneDetailsCache.Scenes.TryGetValue(character.SceneName, out WorldSceneDetails _))
			{
				Log.Debug("InteractableSystem", "Missing Scene:" + character.SceneName);
				return;
			}

			// validate scene object
			if (!ValidateSceneObject(msg.InteractableID, character.GameObject.scene.handle, out ISceneObject sceneObject))
			{
				return;
			}

			IInteractable interactable = sceneObject.GameObject.GetComponent<IInteractable>();
			if (interactable != null &&
				interactable.CanInteract(character))
			{
				Type interactableType = interactable.GetType();

				if (interactableHandlers.TryGetValue(interactableType, out IInteractableHandler handler))
				{
					handler.HandleInteraction(interactable, character, sceneObject, this);
				}
				else
				{
					Log.Warning("InteractableSystem", $"No interaction handler registered for type: {interactableType.Name}");
				}
			}
		}

		public void OnInteractNPC(IPlayerCharacter character, IInteractable interactable)
		{
			if (character == null)
			{
				return;
			}
			if (interactable == null)
			{
				return;
			}

			AIController aiController = interactable.Transform.GetComponent<AIController>();
			if (aiController == null)
			{
				return;
			}

			// Look at the target and transition to idle state
			aiController.LookTarget = character.Transform;
			aiController.TransitionToIdleState();
		}

		private void OnServerMerchantPurchaseBroadcastReceived(NetworkConnection conn, MerchantPurchaseBroadcast msg, Channel channel)
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
				!character.TryGet(out IInventoryController inventoryController))
			{
				return;
			}

			// validate template exists
			MerchantTemplate merchantTemplate = MerchantTemplate.Get<MerchantTemplate>(msg.ID);
			if (merchantTemplate == null)
			{
				return;
			}

			// validate scene
			if (!WorldSceneDetailsCache.Scenes.TryGetValue(character.SceneName, out WorldSceneDetails details))
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
			Merchant merchant = interactable as Merchant;
			if (merchant == null ||
				merchantTemplate.ID != merchant.Template.ID)
			{
				return;
			}

			switch (msg.Type)
			{
				case MerchantTabType.Item:
					BaseItemTemplate itemTemplate = merchantTemplate.Items[msg.Index];
					if (itemTemplate == null)
					{
						return;
					}

					// do we have enough currency to purchase this?
					if (CurrencyTemplate == null)
					{
						Log.Debug("InteractableSystem", "CurrencyTemplate is null.");
						return;
					}
					if (!character.TryGet(out ICharacterAttributeController attributeController) ||
						!attributeController.TryGetAttribute(CurrencyTemplate, out CharacterAttribute currency) ||
						currency.FinalValue < itemTemplate.Price)
					{
						return;
					}

					if (merchantTemplate.Items != null &&
						merchantTemplate.Items.Count >= msg.Index)
					{
						Item newItem = new Item(itemTemplate, 1);
						if (newItem == null)
						{
							return;
						}

						SendNewItemBroadcast(conn, character, inventoryController, newItem);
					}
					break;
				case MerchantTabType.Ability:
					if (merchantTemplate.Abilities != null &&
						merchantTemplate.Abilities.Count >= msg.Index)
					{
						LearnAbilityTemplate(conn, character, merchantTemplate.Abilities[msg.Index]);
					}
					break;
				case MerchantTabType.AbilityEvent:
					if (merchantTemplate.AbilityEvents != null &&
						merchantTemplate.AbilityEvents.Count >= msg.Index)
					{
						LearnAbilityEvent(conn, character, merchantTemplate.AbilityEvents[msg.Index]);
					}
					break;
				default: return;
			}
		}

		private void LearnAbilityGeneric<TTemplate, TBroadcast>(
			NetworkConnection conn,
			IPlayerCharacter character,
			TTemplate template,
			Func<IAbilityController, int, bool> knowsFunc,
			Action<IAbilityController, List<TTemplate>> learnFunc,
			Func<TTemplate, int> idSelector,
			Func<TTemplate, int> priceSelector,
			Func<TTemplate, TBroadcast> broadcastFactory)
			where TTemplate : class
			where TBroadcast : struct, IBroadcast
		{
			if (template == null || character == null || !character.TryGet(out IAbilityController abilityController) || knowsFunc(abilityController, idSelector(template)))
			{
				return;
			}

			if (CurrencyTemplate == null)
			{
				Log.Debug("InteractableSystem", "CurrencyTemplate is null.");
				return;
			}
			if (!character.TryGet(out ICharacterAttributeController attributeController) ||
				!attributeController.TryGetAttribute(CurrencyTemplate, out CharacterAttribute currency) ||
				currency.FinalValue < priceSelector(template))
			{
				Log.Debug("InteractableSystem", "Not enough currency!");
				return;
			}

			// learn the ability or event
			learnFunc(abilityController, new List<TTemplate> { template });

			// remove the price from the characters currency
			currency.AddValue(-priceSelector(template));

			// fire-and-forget: persist the known ability/event to the database
			long charID = character.ID;
			int templateID = idSelector(template);
			EnqueueAsyncWork(() => PersistKnownAbilityAsync(charID, templateID));

			// tell the client about the new ability/event
			Server.NetworkWrapper.Broadcast(conn, broadcastFactory(template), true, Channel.Reliable);
		}

		/// <summary>
		/// Persists a known ability or event to the database asynchronously.
		/// </summary>
		private async Task PersistKnownAbilityAsync(long characterID, int templateID)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null)
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<ICharacterKnownAbilityService>(out var knownAbilityService))
				{
					return;
				}

				await knownAbilityService.PersistAsync(characterID, templateID, 1);
			}
			catch (Exception ex)
			{
				await Log.Error("InteractableSystem", $"Error persisting known ability: {ex}");
			}
		}

		public void LearnAbilityTemplate<T>(NetworkConnection conn, IPlayerCharacter character, T template) where T : BaseAbilityTemplate
		{
			LearnAbilityGeneric<BaseAbilityTemplate, KnownAbilityAddBroadcast>(
				conn,
				character,
				template,
				(abilityController, id) => abilityController.KnowsAbility(id),
				(abilityController, list) => abilityController.LearnBaseAbilities(list.Cast<BaseAbilityTemplate>().ToList()),
				t => t.ID,
				t => t.Price,
				t => new KnownAbilityAddBroadcast { TemplateID = t.ID }
			);
		}

		public void LearnAbilityEvent<T>(NetworkConnection conn, IPlayerCharacter character, T template) where T : AbilityEvent
		{
			LearnAbilityGeneric<AbilityEvent, KnownAbilityEventAddBroadcast>(
				conn,
				character,
				template,
				(abilityController, id) => abilityController.KnowsAbilityEvent(id),
				(abilityController, list) => abilityController.LearnAbilityEvents(list.Cast<AbilityEvent>().ToList()),
				t => t.ID,
				t => t.Price,
				t => new KnownAbilityEventAddBroadcast { TemplateID = t.ID }
			);
		}

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

			// validate main ability exists
			AbilityTemplate mainAbility = AbilityTemplate.Get<AbilityTemplate>(msg.TemplateID);
			if (mainAbility == null)
			{
				return;
			}

			// validate scene
			if (!WorldSceneDetailsCache.Scenes.TryGetValue(character.SceneName, out WorldSceneDetails details))
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
				abilityController.KnownAbilities.Count >= MaxAbilityCount)
			{
				return;
			}

			int price = mainAbility.Price;

			// validate eventIds if there are any...
			if (msg.Events != null)
			{
				bool hasTypeOverride = false;
				HashSet<int> validatedEvents = new HashSet<int>();
				for (int i = 0; i < msg.Events.Count; ++i)
				{
					int id = msg.Events[i];
					if (validatedEvents.Contains(id))
					{
						// duplicate events
						return;
					}
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

					/*if (trigger is AbilityTypeOverrideEventType)
					{
						if (hasTypeOverride)
						{
							// duplicate ability type override
							return;
						}
						hasTypeOverride = true;
					}*/

					price += abilityEvent.Price;
				}
			}

			// do we have enough currency to purchase this?
			if (CurrencyTemplate == null)
			{
				Log.Debug("InteractableSystem", "CurrencyTemplate is null.");
				return;
			}
			if (!character.TryGet(out ICharacterAttributeController attributeController) ||
				!attributeController.TryGetAttribute(CurrencyTemplate, out CharacterAttribute currency) ||
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

		public void OnServerDungeonFinderBroadcastReceived(NetworkConnection conn, DungeonFinderBroadcast msg, Channel channel)
		{
			if (conn == null)
			{
				return;
			}

			// Validate connection character
			if (conn.FirstObject == null)
			{
				return;
			}
			IPlayerCharacter character = conn.FirstObject.GetComponent<IPlayerCharacter>();
			if (character == null)
			{
				return;
			}

			// Validate scene object
			if (!ValidateSceneObject(msg.InteractableID, character.GameObject.scene.handle, out ISceneObject sceneObject))
			{
				return;
			}

			// Validate Dungeon Entrance
			DungeonEntrance dungeonEntrance = sceneObject.GameObject.GetComponent<DungeonEntrance>();
			if (dungeonEntrance == null ||
				!dungeonEntrance.InRange(character.Transform))
			{
				return;
			}

			// Validate scene
			if (!WorldSceneDetailsCache.Scenes.TryGetValue(dungeonEntrance.DungeonName, out WorldSceneDetails details))
			{
				Log.Debug("InteractableSystem", "Missing Scene:" + dungeonEntrance.DungeonName);
				return;
			}

			if (details.RespawnPositions == null || details.RespawnPositions.Count < 1)
			{
				Log.Debug("InteractableSystem", $"Missing Scene: {dungeonEntrance.DungeonName} respawn points.");
				return;
			}

			// Capture main-thread state before going async
			long characterID = character.ID;
			long worldServerID = character.WorldServerID;
			long partyID = 0;
			if (character.TryGet(out IPartyController partyController) && partyController.ID != 0)
			{
				partyID = partyController.ID;
			}
			string dungeonName = dungeonEntrance.DungeonName;
			CharacterRespawnPositionDetails respawnDetails = details.RespawnPositions.Values.ToList().GetRandom();

			// Fire-and-forget: process dungeon instance assignment asynchronously
			EnqueueAsyncWork(() => ProcessDungeonFinderAsync(conn, character, characterID, worldServerID, partyID, dungeonName, respawnDetails));
		}

		/// <summary>
		/// Asynchronously processes dungeon finder logic: checks for existing instances, party instances, or enqueues a new one.
		/// Marshals character state changes and disconnect back to the main thread.
		/// </summary>
		private async Task ProcessDungeonFinderAsync(
			NetworkConnection conn,
			IPlayerCharacter character,
			long characterID,
			long worldServerID,
			long partyID,
			string dungeonName,
			CharacterRespawnPositionDetails respawnDetails)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null)
				{
					return;
				}
				if (!Server.Database.ServiceRegistry.TryGet<ISceneService>(out var sceneService))
				{
					return;
				}

				// Check the status of the characters instance
				var instanceResult = await sceneService.FetchCharacterInstanceAsync(characterID, (FishMMO.Database.Data.Enums.SceneType)(int)SceneType.Group);
				if (!instanceResult.IsSuccess)
				{
					// No existing instance found
					// Check if any party members currently have an instance
					if (partyID > 0 && await CheckCharacterPartyInstanceAsync(partyID))
					{
						// Notify the connection the party member already has an instance.
						return;
					}

					var enqueueResult = await sceneService.EnqueueAsync(
						worldServerID,
						dungeonName,
						(FishMMO.Database.Data.Enums.SceneType)(int)SceneType.Group,
						characterID);

					if (!enqueueResult.IsSuccess)
					{
						await Log.Debug("InteractableSystem", "Failed to enqueue new pending scene load request: " + worldServerID + ":" + dungeonName);
						return;
					}

					long sceneID = enqueueResult.Data;
					EnqueueMainThread(() =>
					{
						character.InstanceID = sceneID;
						character.InstancePosition = respawnDetails.Position;
						character.InstanceRotation = respawnDetails.Rotation;
						character.EnableFlags(CharacterFlags.IsInInstance);
						conn.Disconnect(false);
					});
				}
				else
				{
					long existingInstanceID = instanceResult.Data.ID;
					EnqueueMainThread(() =>
					{
						character.InstanceID = existingInstanceID;
						character.InstancePosition = respawnDetails.Position;
						character.InstanceRotation = respawnDetails.Rotation;
						character.EnableFlags(CharacterFlags.IsInInstance);
						conn.Disconnect(false);
					});
				}
			}
			catch (Exception ex)
			{
				await Log.Error("InteractableSystem", $"Error processing dungeon finder: {ex}");
			}
		}

		/// <summary>
		/// Checks if a characters party members have a valid instance.
		/// </summary>
		private async Task<bool> CheckCharacterPartyInstanceAsync(long partyID)
		{
			if (Server?.Database?.ServiceRegistry == null)
			{
				return false;
			}
			if (!Server.Database.ServiceRegistry.TryGet<ICharacterPartyService>(out var charPartyService) ||
				!Server.Database.ServiceRegistry.TryGet<ISceneService>(out var sceneService))
			{
				return false;
			}

			var membersResult = await charPartyService.FetchManyAsync(partyID);
			if (!membersResult.IsSuccess || membersResult.Data == null || membersResult.Data.Count == 0)
			{
				return false;
			}

			foreach (var member in membersResult.Data)
			{
				var instanceResult = await sceneService.FetchCharacterInstanceAsync(member.CharacterID, (FishMMO.Database.Data.Enums.SceneType)(int)SceneType.Group);
				if (instanceResult.IsSuccess)
				{
					return true;
				}
			}

			return false;
		}

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
			EnqueueAsyncWork(() => PersistAbilityAsync(abilityData));

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

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private bool ValidateSceneObject(long sceneObjectID, int characterSceneHandle, out ISceneObject sceneObject)
		{
			if (!SceneObject.Objects.TryGetValue(sceneObjectID, out sceneObject))
			{
				if (sceneObject == null)
				{
					Log.Debug("InteractableSystem", "Missing SceneObject");
				}
				else
				{
					Log.Debug("InteractableSystem", "Missing ID:" + sceneObjectID);
				}
				return false;
			}
			if (sceneObject.GameObject.scene.handle != characterSceneHandle)
			{
				Log.Debug("InteractableSystem", "Object scene mismatch.");
				return false;
			}
			return true;
		}

		/// <summary>
		/// Enqueues an async work item to the centralized async worker for controlled execution.
		/// </summary>
		private void EnqueueAsyncWork(Func<Task> work, long entityKey = 0, [CallerMemberName] string callerName = null)
		{
			if (Server?.DataContainerRegistry.TryGet<IAsyncWorkerData>(out var asyncWorker) == true)
			{
				if (entityKey != 0)
					asyncWorker.Enqueue(work, entityKey, callerName);
				else
					asyncWorker.Enqueue(work, callerName);
			}
		}
	}
}