using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Shared;
using FishMMO.Logging;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared.Core;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
	public partial class InteractableSystem : ServerBehaviour, IInteractableSystem
	{
		/// <summary>
		/// Maximum number of queued main-thread actions processed per frame.
		/// This time-slices queue draining to avoid frame spikes.
		/// </summary>
		[Header("Main Thread Dispatch")]
		[Tooltip("Max interactable-system actions drained from main-thread queue per frame")]
		[SerializeField] private int maxMainThreadActionsPerFrame = 100;

		/// <summary>
		/// Global interaction cooldown in milliseconds. After any interaction,
		/// the player cannot perform another interaction of any type until this cooldown expires.
		/// All interaction types share a single per-connection IngressGuard key.
		/// </summary>
		[Header("Request Protection")]
		[Tooltip("Global interaction cooldown in milliseconds. Players can only interact with one thing at a time.")]
		[SerializeField] private int interactionDebounceMilliseconds = 1000;

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
		/// Injected via Unity's [SerializeField] as a ScriptableObject asset reference,
		/// which is the standard Unity pattern for editor-assigned asset dependencies.
		/// </summary>
		[SerializeField] private WorldSceneDetailsCache worldSceneDetailsCache;
		/// <summary>
		/// Registers and clears interactable handlers for this system.
		/// </summary>
		[SerializeField] private InteractableHandlerInitializer interactableHandlerInitializer;
		/// <summary>
		/// Maximum number of crafted abilities a character may learn.
		/// </summary>
		[SerializeField] [Min(1)] private int maxAbilityCount = 25;
		/// <summary>
		/// Maximum number of ability events allowed per craft request.
		/// Defense-in-depth cap to prevent processing oversized payloads.
		/// </summary>
		[SerializeField] [Min(1)] private int maxAbilityCraftEvents = 32;
		/// <summary>
		/// Currency attribute required to buy merchant items and abilities.
		/// </summary>
		[SerializeField] private CharacterAttributeTemplate currencyTemplate;

		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error("InteractableSystem", "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (interactableHandlerInitializer == null)
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
			interactableHandlerInitializer.RegisterHandlers(Server, this);

			// Network broadcasts
			Server.NetworkWrapper.RegisterBroadcast<InteractableBroadcast>(OnServerInteractableBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<MerchantPurchaseBroadcast>(OnServerMerchantPurchaseBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<AbilityCraftBroadcast>(OnServerAbilityCraftBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<DungeonFinderBroadcast>(OnServerDungeonFinderBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<DialogueChoiceBroadcast>(OnServerDialogueChoiceBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<MailFetchBroadcast>(OnServerMailFetchBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<MailSendBroadcast>(OnServerMailSendBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<MailDeleteBroadcast>(OnServerMailDeleteBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<ContainerTakeItemBroadcast>(OnServerContainerTakeItemBroadcastReceived, true);

			IDialogueInteractable.OnServerDialogueRequested += OnDisplayDialogueActionRequested;

			maxMainThreadActionsPerFrame = Mathf.Max(1, maxMainThreadActionsPerFrame);
			interactionDebounceMilliseconds = Mathf.Max(0, interactionDebounceMilliseconds);
			debounceSweepIntervalSeconds = Mathf.Max(0.25f, debounceSweepIntervalSeconds);
			debounceEntryTtlSeconds = Mathf.Max(1.0f, debounceEntryTtlSeconds);
			debounceSweepMaxRemovals = Mathf.Max(1, debounceSweepMaxRemovals);
			// IngressGuard initialized in runtimeData; no additional init required here.

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
				runtimeData.IngressGuard?.Clear();
			}

			// Network broadcasts
			Server.NetworkWrapper.UnregisterBroadcast<InteractableBroadcast>(OnServerInteractableBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<MerchantPurchaseBroadcast>(OnServerMerchantPurchaseBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<AbilityCraftBroadcast>(OnServerAbilityCraftBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<DungeonFinderBroadcast>(OnServerDungeonFinderBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<DialogueChoiceBroadcast>(OnServerDialogueChoiceBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<MailFetchBroadcast>(OnServerMailFetchBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<MailSendBroadcast>(OnServerMailSendBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<MailDeleteBroadcast>(OnServerMailDeleteBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<ContainerTakeItemBroadcast>(OnServerContainerTakeItemBroadcastReceived);

			IDialogueInteractable.OnServerDialogueRequested -= OnDisplayDialogueActionRequested;

			// Dialogue session cleanup
			activeDialogueSessions.Clear();
			characterDialogueChoices.Clear();
		}

		protected override void OnUpdate(float deltaTime)
		{
			DrainMainThreadQueue(drainAll: false);
			SweepDebounceTrackers();
		}

		/// <summary>
		/// Performs bounded cleanup via the runtime IngressGuard sweep.
		/// </summary>
		private void SweepDebounceTrackers()
		{
			if (Server.DataContainerRegistry.TryGet<IInteractableSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.IngressGuard.Sweep(debounceSweepIntervalSeconds, debounceEntryTtlSeconds, debounceSweepMaxRemovals);
			}
		}

		private void DrainMainThreadQueue(bool drainAll)
		{
			DrainMainThreadQueue<IInteractableSystemMainThreadQueueData>(maxMainThreadActionsPerFrame, drainAll);
		}

		private bool TryEnqueueMainThread(Action action)
		{
			return TryEnqueueMainThread<IInteractableSystemMainThreadQueueData>(action);
		}

		private bool TryBeginIngressGuard(int connectionId, out long guardKey)
		{
			if (!Server.DataContainerRegistry.TryGet<IInteractableSystemRuntimeData>(out var runtimeData))
			{
				guardKey = 0;
				return false;
			}
			return runtimeData.IngressGuard.TryBegin(connectionId, 0, interactionDebounceMilliseconds, out guardKey);
		}

		private void EndIngressGuard(long guardKey)
		{
			if (Server.DataContainerRegistry.TryGet<IInteractableSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.IngressGuard.End(guardKey);
			}
		}

		/// <summary>
		/// Registers an interactable handler for a specific interactable type (non-generic overload).
		/// Used by reflection-based auto-discovery in InteractableHandlerInitializer.
		/// </summary>
		/// <param name="interactableType">The interactable type to register the handler for. Must implement IInteractable.</param>
		/// <param name="handler">The handler instance for the specified interactable type.</param>
		public void RegisterInteractableHandler(Type interactableType, IInteractableHandler handler)
		{
			if (interactableType == null || handler == null)
			{
				return;
			}

			if (!typeof(IInteractable).IsAssignableFrom(interactableType))
			{
				Log.Warning("InteractableSystem", $"Type {interactableType.Name} does not implement IInteractable. Skipping registration.");
				return;
			}

			if (!Server.DataContainerRegistry.TryGet<IInteractableSystemRuntimeData>(out var runtimeData))
			{
				return;
			}

			Dictionary<Type, IInteractableHandler> interactableHandlers = runtimeData.InteractableHandlers;
			if (!interactableHandlers.ContainsKey(interactableType))
			{
				interactableHandlers.Add(interactableType, handler);
				Log.Debug("InteractableSystem", $"Registered handler for {interactableType.Name}");
			}
			else
			{
				Log.Warning("InteractableSystem", $"Handler for type {interactableType.Name} is already registered. Overwriting existing handler.");
				interactableHandlers[interactableType] = handler;
			}
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
		public IInteractableHandler GetInteractableHandler<T>() where T : IInteractable
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
		public bool SendNewItemBroadcast<T>(T conn, ICharacter character, IInventoryController inventoryController, Item newItem)
		{
			if (conn is not NetworkConnection networkConn)
			{
				Log.Error("InteractableSystem", "Invalid connection type passed to SendNewItemBroadcast");
				return false;
			}

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
				Server.NetworkWrapper.Broadcast(networkConn, new InventorySetMultipleItemsBroadcast()
				{
					Items = modifiedItemBroadcasts,
				}, true, Channel.Reliable);

				// Fire-and-forget: persist inventory changes to DB
				if (itemsToSave.Count > 0)
				{
					if (!TryEnqueueAsyncWork(() => PersistInventoryItemsAsync(itemsToSave), character.ID))
					{
						_ = PersistInventoryItemsAsync(itemsToSave).ContinueWith(
							t => Log.Error("InteractableSystem", $"Fallback inventory persist failed for CharID={character.ID}: {t.Exception}"),
							TaskContinuationOptions.OnlyOnFaulted);
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

			if (!TryBeginIngressGuard(conn.ClientId, out long guardKey))
			{
				return;
			}

			try
			{

				// validate scene
				if (worldSceneDetailsCache == null ||
					!worldSceneDetailsCache.Scenes.TryGetValue(character.SceneName, out WorldSceneDetails _))
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
				EndIngressGuard(guardKey);
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

		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private bool ValidateSceneObject(long sceneObjectID, int characterSceneHandle, out ISceneObject sceneObject)
		{
			if (!SceneObject.Objects.TryGetValue(sceneObjectID, out sceneObject))
			{
				Log.Debug("InteractableSystem", $"Missing SceneObject ID:{sceneObjectID}");
				return false;
			}
			if (sceneObject.GameObject.scene.handle != characterSceneHandle)
			{
				Log.Debug("InteractableSystem", "Object scene mismatch.");
				return false;
			}
			return true;
		}

		// Uses ServerBehaviour.TryEnqueueAsyncWork
	}
}