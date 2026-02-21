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
	[RequiresDataContainer(typeof(InteractableSystemRuntimeData))]
	[RequiresDataContainer(typeof(AsyncWorkerData))]
	public class InteractableSystem : ServerBehaviour
	{
		/// <summary>
		/// Maximum number of queued main-thread actions processed per frame.
		/// This time-slices queue draining to avoid frame spikes.
		/// </summary>
		[Header("Main Thread Dispatch")]
		[Tooltip("Max interactable-system actions drained from main-thread queue per frame")]
		[SerializeField] private int maxMainThreadActionsPerFrame = 100;

		/// <summary>
		/// Minimum milliseconds between merchant requests per character.
		/// </summary>
		[Header("Request Protection")]
		[Tooltip("Minimum milliseconds between merchant purchase requests per character")]
		[SerializeField] private int merchantRequestDebounceMilliseconds = 150;

		/// <summary>
		/// Minimum milliseconds between dungeon finder requests per character.
		/// </summary>
		[Tooltip("Minimum milliseconds between dungeon finder requests per character")]
		[SerializeField] private int dungeonFinderDebounceMilliseconds = 500;

		/// <summary>
		/// Minimum milliseconds between generic interactable requests per character.
		/// </summary>
		[Tooltip("Minimum milliseconds between generic interactable requests per character")]
		[SerializeField] private int interactableRequestDebounceMilliseconds = 100;

		/// <summary>
		/// Minimum milliseconds between ability craft requests per character.
		/// </summary>
		[Tooltip("Minimum milliseconds between ability craft requests per character")]
		[SerializeField] private int abilityCraftDebounceMilliseconds = 150;

		/// <summary>
		/// Interval between debounce-tracker cleanup sweeps.
		/// </summary>
		[Tooltip("Seconds between bounded debounce tracker cleanup sweeps")]
		[SerializeField] private float debounceSweepIntervalSeconds = 5.0f;

		/// <summary>
		/// Time-to-live in seconds for stale debounce entries.
		/// </summary>
		[Tooltip("Seconds before stale debounce entries are removed")]
		[SerializeField] private float debounceEntryTtlSeconds = 30.0f;

		/// <summary>
		/// Maximum stale debounce entries removed per sweep and tracker.
		/// </summary>
		[Tooltip("Maximum stale debounce entries removed per sweep and tracker")]
		[SerializeField] private int debounceSweepMaxRemovals = 128;

		/// <summary>
		/// Cache of world scene details used for scene validation and respawn lookup.
		/// </summary>
		public WorldSceneDetailsCache WorldSceneDetailsCache;
		/// <summary>
		/// Registers and clears interactable handlers for this system.
		/// </summary>
		public InteractableHandlerInitializer InteractableHandlerInitializer;
		/// <summary>
		/// Maximum number of crafted abilities a character may learn.
		/// </summary>
		public int MaxAbilityCount = 25;
		/// <summary>
		/// Maximum number of ability events allowed per craft request.
		/// Defense-in-depth cap to prevent processing oversized payloads.
		/// </summary>
		public int MaxAbilityCraftEvents = 32;
		/// <summary>
		/// Currency attribute required to buy merchant items and abilities.
		/// </summary>
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

			if (!Server.DataContainerRegistry.TryGet<IInteractableSystemRuntimeData>(out var runtimeData))
			{
				Log.Error("InteractableSystem", "InitializeOnce: IInteractableSystemRuntimeData not found");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			// Interactable handlers
			InteractableHandlerInitializer.RegisterHandlers(Server, this);

			// Network broadcasts
			Server.NetworkWrapper.RegisterBroadcast<InteractableBroadcast>(OnServerInteractableBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<MerchantPurchaseBroadcast>(OnServerMerchantPurchaseBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<AbilityCraftBroadcast>(OnServerAbilityCraftBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<DungeonFinderBroadcast>(OnServerDungeonFinderBroadcastReceived, true);

			maxMainThreadActionsPerFrame = Mathf.Max(1, maxMainThreadActionsPerFrame);
			merchantRequestDebounceMilliseconds = Mathf.Max(0, merchantRequestDebounceMilliseconds);
			dungeonFinderDebounceMilliseconds = Mathf.Max(0, dungeonFinderDebounceMilliseconds);
			interactableRequestDebounceMilliseconds = Mathf.Max(0, interactableRequestDebounceMilliseconds);
			abilityCraftDebounceMilliseconds = Mathf.Max(0, abilityCraftDebounceMilliseconds);
			debounceSweepIntervalSeconds = Mathf.Max(0.25f, debounceSweepIntervalSeconds);
			debounceEntryTtlSeconds = Mathf.Max(1.0f, debounceEntryTtlSeconds);
			debounceSweepMaxRemovals = Mathf.Max(1, debounceSweepMaxRemovals);
			runtimeData.NextDebounceSweepUtc = DateTime.UtcNow;

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
			DrainMainThreadQueue(drainAll: true);

			// Interactable handlers
			ClearAllHandlers();
			if (Server.DataContainerRegistry.TryGet<IInteractableSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.InteractableHandlers.Clear();
				runtimeData.InteractableNextAllowedUtcByCharacter.Clear();
				runtimeData.InteractableInFlightByCharacter.Clear();
				runtimeData.NextDebounceSweepUtc = DateTime.UtcNow;
			}

			// Network broadcasts
			Server.NetworkWrapper.UnregisterBroadcast<InteractableBroadcast>(OnServerInteractableBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<MerchantPurchaseBroadcast>(OnServerMerchantPurchaseBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<AbilityCraftBroadcast>(OnServerAbilityCraftBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<DungeonFinderBroadcast>(OnServerDungeonFinderBroadcastReceived);
		}

		public override void OnLateUpdate(float deltaTime)
		{
			DrainMainThreadQueue(drainAll: false);
			SweepDebounceTrackers();
		}

		/// <summary>
		/// Performs bounded cleanup for stale debounce tracker entries.
		/// </summary>
		private void SweepDebounceTrackers()
		{
			if (!Server.DataContainerRegistry.TryGet<IInteractableSystemRuntimeData>(out var runtimeData))
			{
				return;
			}

			DateTime nowUtc = DateTime.UtcNow;
			if (nowUtc < runtimeData.NextDebounceSweepUtc)
			{
				return;
			}

			runtimeData.NextDebounceSweepUtc = nowUtc.AddSeconds(debounceSweepIntervalSeconds);
			DateTime staleBeforeUtc = nowUtc.AddSeconds(-debounceEntryTtlSeconds);

			PruneDebounceTracker(runtimeData.InteractableNextAllowedUtcByCharacter, staleBeforeUtc, debounceSweepMaxRemovals);
		}

		/// <summary>
		/// Removes stale entries from a debounce tracker with bounded removals.
		/// </summary>
		private static void PruneDebounceTracker(System.Collections.Concurrent.ConcurrentDictionary<long, DateTime> tracker, DateTime staleBeforeUtc, int maxRemovals)
		{
			int removed = 0;
			foreach (var kvp in tracker)
			{
				if (removed >= maxRemovals)
				{
					break;
				}

				if (kvp.Value <= staleBeforeUtc && tracker.TryRemove(kvp.Key, out _))
				{
					removed++;
				}
			}
		}

		private void DrainMainThreadQueue(bool drainAll)
		{
			if (Server != null &&
				Server.DataContainerRegistry.TryGet<IInteractableSystemMainThreadQueueData>(out var queueData))
			{
				if (drainAll)
				{
					queueData.Drain();
				}
				else
				{
					queueData.Drain(maxMainThreadActionsPerFrame);
				}
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

		/// <summary>
		/// Returns true when the request is currently rate-limited for the character and false when accepted.
		/// </summary>
		private static bool IsDebounced(System.Collections.Concurrent.ConcurrentDictionary<long, DateTime> tracker, long characterID, int debounceMilliseconds)
		{
			if (debounceMilliseconds <= 0)
			{
				return false;
			}

			DateTime nowUtc = DateTime.UtcNow;
			if (tracker.TryGetValue(characterID, out DateTime nextAllowedUtc) && nowUtc < nextAllowedUtc)
			{
				return true;
			}

			tracker[characterID] = nowUtc.AddMilliseconds(debounceMilliseconds);
			return false;
		}

		/// <summary>
		/// Registers an interactable handler for a specific type. The type must implement IInteractable.
		/// </summary>
		/// <typeparam name="T">The type of interactable to register the handler for. Must implement IInteractable.</typeparam>
		/// <param name="handler">The handler instance for the specified interactable type.</param>
		public void RegisterInteractableHandler<T>(IInteractableHandler handler) where T : IInteractable
		{
			if (!Server.DataContainerRegistry.TryGet<IInteractableSystemRuntimeData>(out var runtimeData))
			{
				return;
			}

			Dictionary<Type, IInteractableHandler> interactableHandlers = runtimeData.InteractableHandlers;
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
		public bool UnregisterInteractableHandler<T>() where T : IInteractable
		{
			if (!Server.DataContainerRegistry.TryGet<IInteractableSystemRuntimeData>(out var runtimeData))
			{
				return false;
			}

			Dictionary<Type, IInteractableHandler> interactableHandlers = runtimeData.InteractableHandlers;
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
		public IInteractableHandler GetInteractableHandler<T>()
		{
			if (!Server.DataContainerRegistry.TryGet<IInteractableSystemRuntimeData>(out var runtimeData))
			{
				return null;
			}

			Dictionary<Type, IInteractableHandler> interactableHandlers = runtimeData.InteractableHandlers;
			Type type = typeof(T);
			interactableHandlers.TryGetValue(type, out var handler);
			return handler;
		}

		/// <summary>
		/// Removes all registered interactable handlers.
		/// </summary>
		public void ClearAllHandlers()
		{
			if (Server.DataContainerRegistry.TryGet<IInteractableSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.InteractableHandlers.Clear();
			}
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
					if (!TryEnqueueAsyncWork(() => PersistInventoryItemsAsync(itemsToSave), character.ID))
					{
						_ = PersistInventoryItemsAsync(itemsToSave);
						Log.Warning("InteractableSystem", $"SendNewItemBroadcast: Async worker rejected inventory persist for CharID={character.ID}; executed fallback persistence path.");
					}
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

			if (!Server.DataContainerRegistry.TryGet<IInteractableSystemRuntimeData>(out var runtimeData))
			{
				return;
			}

			if (IsDebounced(runtimeData.InteractableNextAllowedUtcByCharacter, character.ID, interactableRequestDebounceMilliseconds))
			{
				return;
			}

			if (!runtimeData.InteractableInFlightByCharacter.TryAdd(character.ID, 0))
			{
				return;
			}

			try
			{

				// validate scene
				if (WorldSceneDetailsCache == null ||
					!WorldSceneDetailsCache.Scenes.TryGetValue(character.SceneName, out WorldSceneDetails _))
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

					if (runtimeData.InteractableHandlers.TryGetValue(interactableType, out IInteractableHandler handler))
					{
						handler.HandleInteraction(interactable, character, sceneObject, this);
					}
					else
					{
						Log.Warning("InteractableSystem", $"No interaction handler registered for type: {interactableType.Name}");
					}
				}
			}
			finally
			{
				runtimeData.InteractableInFlightByCharacter.TryRemove(character.ID, out _);
			}
		}

		/// <summary>
		/// Updates an NPC to face the interacting character and enter idle state.
		/// </summary>
		/// <param name="character">Interacting player character.</param>
		/// <param name="interactable">Interacted NPC object.</param>
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

			if (!Server.DataContainerRegistry.TryGet<IInteractableSystemRuntimeData>(out var runtimeData))
			{
				return;
			}

			if (IsDebounced(runtimeData.InteractableNextAllowedUtcByCharacter, character.ID, merchantRequestDebounceMilliseconds))
			{
				return;
			}

			if (!runtimeData.InteractableInFlightByCharacter.TryAdd(character.ID, 0))
			{
				return;
			}

			try
			{

				// validate template exists
				MerchantTemplate merchantTemplate = MerchantTemplate.Get<MerchantTemplate>(msg.ID);
				if (merchantTemplate == null)
				{
					return;
				}

				// validate scene
				if (WorldSceneDetailsCache == null ||
					!WorldSceneDetailsCache.Scenes.TryGetValue(character.SceneName, out _))
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
					if (merchantTemplate.Items == null ||
						msg.Index < 0 ||
						msg.Index >= merchantTemplate.Items.Count)
					{
						return;
					}

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

					Item newItem = new Item(itemTemplate, 1);
					if (newItem == null)
					{
						return;
					}

					if (SendNewItemBroadcast(conn, character, inventoryController, newItem))
					{
						currency.AddValue(-itemTemplate.Price);
					}
					break;
				case MerchantTabType.Ability:
					if (merchantTemplate.Abilities != null &&
						msg.Index >= 0 &&
						msg.Index < merchantTemplate.Abilities.Count)
					{
						LearnAbilityTemplate(conn, character, merchantTemplate.Abilities[msg.Index]);
					}
					break;
				case MerchantTabType.AbilityEvent:
					if (merchantTemplate.AbilityEvents != null &&
						msg.Index >= 0 &&
						msg.Index < merchantTemplate.AbilityEvents.Count)
					{
						LearnAbilityEvent(conn, character, merchantTemplate.AbilityEvents[msg.Index]);
					}
					break;
				default: return;
				}
			}
			finally
			{
				runtimeData.InteractableInFlightByCharacter.TryRemove(character.ID, out _);
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

			long charID = character.ID;
			int templateID = idSelector(template);
			if (!TryEnqueueAsyncWork(() => PersistKnownAbilityAsync(charID, templateID), charID))
			{
				Log.Warning("InteractableSystem", $"LearnAbilityGeneric: Async worker rejected known-ability persist for CharID={charID}, TemplateID={templateID}.");
				return;
			}

			// learn the ability or event
			learnFunc(abilityController, new List<TTemplate> { template });

			// remove the price from the characters currency
			currency.AddValue(-priceSelector(template));

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

		/// <summary>
		/// Learns a base ability template and synchronizes the result to the client.
		/// </summary>
		/// <typeparam name="T">Concrete base ability template type.</typeparam>
		/// <param name="conn">Client connection to notify.</param>
		/// <param name="character">Character learning the ability.</param>
		/// <param name="template">Ability template to learn.</param>
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

		/// <summary>
		/// Learns an ability event template and synchronizes the result to the client.
		/// </summary>
		/// <typeparam name="T">Concrete ability event type.</typeparam>
		/// <param name="conn">Client connection to notify.</param>
		/// <param name="character">Character learning the ability event.</param>
		/// <param name="template">Ability event template to learn.</param>
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

			if (IsDebounced(runtimeData.InteractableNextAllowedUtcByCharacter, character.ID, abilityCraftDebounceMilliseconds))
			{
				return;
			}

			if (!runtimeData.InteractableInFlightByCharacter.TryAdd(character.ID, 0))
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
				if (WorldSceneDetailsCache == null ||
					!WorldSceneDetailsCache.Scenes.TryGetValue(character.SceneName, out _))
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
				// Defense-in-depth: cap event list size to prevent processing oversized payloads.
				if (msg.Events.Count > MaxAbilityCraftEvents)
				{
					return;
				}

				//bool hasTypeOverride = false;
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
			finally
			{
				runtimeData.InteractableInFlightByCharacter.TryRemove(character.ID, out _);
			}
		}

		/// <summary>
		/// Handles a dungeon finder request from a dungeon entrance interactable.
		/// </summary>
		/// <param name="conn">Requesting client connection.</param>
		/// <param name="msg">Dungeon finder payload containing interactable id.</param>
		/// <param name="channel">Transport channel used by FishNet.</param>
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

			if (!Server.DataContainerRegistry.TryGet<IInteractableSystemRuntimeData>(out var runtimeData))
			{
				return;
			}

			if (IsDebounced(runtimeData.InteractableNextAllowedUtcByCharacter, character.ID, dungeonFinderDebounceMilliseconds))
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
			if (WorldSceneDetailsCache == null ||
				!WorldSceneDetailsCache.Scenes.TryGetValue(dungeonEntrance.DungeonName, out WorldSceneDetails details))
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

			if (!runtimeData.InteractableInFlightByCharacter.TryAdd(characterID, 0))
			{
				return;
			}

			// Fire-and-forget: process dungeon instance assignment asynchronously
			if (!TryEnqueueAsyncWork(() => ProcessDungeonFinderAsync(conn, character, characterID, worldServerID, partyID, dungeonName, respawnDetails), characterID))
			{
				runtimeData.InteractableInFlightByCharacter.TryRemove(characterID, out _);
			}
		}

		/// <summary>
		/// Asynchronously processes dungeon finder logic: checks for existing instances, party instances, or enqueues a new one.
		/// Marshals character state changes and disconnect back to the main thread.
		/// </summary>
		/// <param name="conn">Owning connection used for disconnecting after assignment.</param>
		/// <param name="character">Character requesting dungeon finder processing.</param>
		/// <param name="characterID">Unique identifier of the requesting character.</param>
		/// <param name="worldServerID">World server identifier where the request originated.</param>
		/// <param name="partyID">Party identifier if the character is grouped; otherwise 0.</param>
		/// <param name="dungeonName">Target dungeon scene name.</param>
		/// <param name="respawnDetails">Respawn position and rotation to apply on entry.</param>
		/// <returns>A task representing asynchronous dungeon finder processing.</returns>
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
			finally
			{
				if (Server != null &&
					Server.DataContainerRegistry.TryGet<IInteractableSystemRuntimeData>(out var runtimeData))
				{
					runtimeData.InteractableInFlightByCharacter.TryRemove(characterID, out _);
				}
			}
		}

		/// <summary>
		/// Checks if a characters party members have a valid instance.
		/// </summary>
		/// <param name="partyID">Party identifier to check for existing instances.</param>
		/// <returns>True if any member has an existing group instance; otherwise false.</returns>
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
		/// Returns false when the queue is unavailable or rejected.
		/// </summary>
		/// <param name="work">Asynchronous work delegate to enqueue.</param>
		/// <param name="entityKey">Optional entity key for ordered queue partitioning.</param>
		/// <param name="callerName">Caller member name used for diagnostics.</param>
		/// <returns>True if enqueue succeeded; otherwise false.</returns>
		private bool TryEnqueueAsyncWork(Func<Task> work, long entityKey = 0, [CallerMemberName] string callerName = null)
		{
			if (Server?.DataContainerRegistry.TryGet<IAsyncWorkerData>(out var asyncWorker) == true)
			{
				if (entityKey != 0)
				{
					if (asyncWorker.Enqueue(work, entityKey, callerName))
					{
						return true;
					}

					Log.Warning("InteractableSystem", $"{callerName}: Async worker queue rejected work (entityKey={entityKey}).");
					return false;
				}
				else
				{
					if (asyncWorker.Enqueue(work, callerName))
					{
						return true;
					}

					Log.Warning("InteractableSystem", $"{callerName}: Async worker queue rejected work.");
					return false;
				}
			}

			Log.Warning("InteractableSystem", $"{callerName}: IAsyncWorkerData unavailable; work was not enqueued.");
			return false;
		}
	}
}