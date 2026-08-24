using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Shared;
using FishMMO.Logging;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared.Core;
using FishMMO.Database;
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

		/// <summary>
		/// Initializes the InteractableSystem: validates dependencies, registers all network broadcast handlers,
		/// subscribes to dialogue events, and clamps configuration values.
		/// </summary>
		/// <returns>Initialization status indicating success or failure.</returns>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error("InteractableSystem", "InitializeOnce: Server is null");
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

			if (!Server.DataContainerRegistry.TryGet<IInteractableSystemRuntimeData>(out _))
			{
				Log.Error("InteractableSystem", "InitializeOnce: IInteractableSystemRuntimeData not found");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			// Network broadcasts
			Server.NetworkWrapper.RegisterBroadcast<InteractableBroadcast>(OnServerInteractableBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<MerchantPurchaseBroadcast>(OnServerMerchantPurchaseBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<MerchantSellBroadcast>(OnServerMerchantSellBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<AbilityCraftBroadcast>(OnServerAbilityCraftBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<DungeonFinderBroadcast>(OnServerDungeonFinderBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<DialogueChoiceBroadcast>(OnServerDialogueChoiceBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<MailFetchBroadcast>(OnServerMailFetchBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<MailSendBroadcast>(OnServerMailSendBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<MailDeleteBroadcast>(OnServerMailDeleteBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<ContainerTakeItemBroadcast>(OnServerContainerTakeItemBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<CorpseLootTakeItemBroadcast>(OnServerCorpseLootTakeItemBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<CorpseLootTakeCurrencyBroadcast>(OnServerCorpseLootTakeCurrencyBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<CorpseLootTakeAllBroadcast>(OnServerCorpseLootTakeAllBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<CorpseLootCloseBroadcast>(OnServerCorpseLootCloseBroadcastReceived, true);

			IDialogueInteractable.OnServerDialogueRequested += OnDisplayDialogueActionRequested;

			/* The dialogue choice cache is memory-only and keyed by character ID. Without these
			 * two hooks it has no idea who is still playing, which is what made its capacity
			 * sweep evict a connected player's one-time choices; and nothing ever removed an
			 * entry, so it filled up with characters who had long since logged out. */
			if (Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, UnityEngine.SceneManagement.Scene> dialogueCharacterSystem) &&
				dialogueCharacterSystem != null)
			{
				dialogueCharacterSystem.OnAfterLoadCharacter += CharacterSystem_OnDialogueCharacterLoaded;
				dialogueCharacterSystem.OnDisconnect += CharacterSystem_OnDialogueCharacterDisconnected;
			}
			else
			{
				Log.Warning("InteractableSystem", "InitializeOnce: ICharacterSystem not found; cached dialogue choices will not be released on disconnect.");
			}

			maxMainThreadActionsPerFrame = Mathf.Max(1, maxMainThreadActionsPerFrame);
			interactionDebounceMilliseconds = Mathf.Max(0, interactionDebounceMilliseconds);
			debounceSweepIntervalSeconds = Mathf.Max(0.25f, debounceSweepIntervalSeconds);
			debounceEntryTtlSeconds = Mathf.Max(1.0f, debounceEntryTtlSeconds);
			debounceSweepMaxRemovals = Mathf.Max(1, debounceSweepMaxRemovals);
			// IngressGuard initialized in runtimeData; no additional init required here.

			Log.Debug("InteractableSystem", "Initialized");
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Deinitializes the InteractableSystem: drains queued actions, clears ingress guards, unregisters network
		/// broadcast handlers, unsubscribes from dialogue events, and clears dialogue session state.
		/// </summary>
		public override void OnDeinitialize()
		{
			if (Server == null)
			{
				Log.Error("InteractableSystem", "OnDeinitialize: Server is null");
				return;
			}

			// Drain any remaining queued main-thread actions
			DrainMainThreadQueue(drainAll: true);

			if (Server.DataContainerRegistry.TryGet<IInteractableSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.IngressGuard?.Clear();
			}

			// Network broadcasts
			Server.NetworkWrapper.UnregisterBroadcast<InteractableBroadcast>(OnServerInteractableBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<MerchantPurchaseBroadcast>(OnServerMerchantPurchaseBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<MerchantSellBroadcast>(OnServerMerchantSellBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<AbilityCraftBroadcast>(OnServerAbilityCraftBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<DungeonFinderBroadcast>(OnServerDungeonFinderBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<DialogueChoiceBroadcast>(OnServerDialogueChoiceBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<MailFetchBroadcast>(OnServerMailFetchBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<MailSendBroadcast>(OnServerMailSendBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<MailDeleteBroadcast>(OnServerMailDeleteBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<ContainerTakeItemBroadcast>(OnServerContainerTakeItemBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<CorpseLootTakeItemBroadcast>(OnServerCorpseLootTakeItemBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<CorpseLootTakeCurrencyBroadcast>(OnServerCorpseLootTakeCurrencyBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<CorpseLootTakeAllBroadcast>(OnServerCorpseLootTakeAllBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<CorpseLootCloseBroadcast>(OnServerCorpseLootCloseBroadcastReceived);

			IDialogueInteractable.OnServerDialogueRequested -= OnDisplayDialogueActionRequested;

			if (Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, UnityEngine.SceneManagement.Scene> dialogueCharacterSystem) &&
				dialogueCharacterSystem != null)
			{
				dialogueCharacterSystem.OnAfterLoadCharacter -= CharacterSystem_OnDialogueCharacterLoaded;
				dialogueCharacterSystem.OnDisconnect -= CharacterSystem_OnDialogueCharacterDisconnected;
			}

			// Corpse loot cleanup
			ClearCorpseSubscriptions();

			// Dialogue session cleanup
			activeDialogueSessions.Clear();
			characterDialogueChoices.Clear();
			connectedDialogueCharacters.Clear();
		}

		/// <summary>
		/// Called every frame. Drains a bounded number of queued main-thread actions and sweeps stale debounce entries.
		/// </summary>
		/// <param name="deltaTime">Time elapsed since the last frame.</param>
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

		/// <summary>
		/// Drains the main-thread action queue, processing up to <see cref="maxMainThreadActionsPerFrame"/> items
		/// (or all items if <paramref name="drainAll"/> is true).
		/// </summary>
		/// <param name="drainAll">If true, drains all queued items; otherwise drains up to the per-frame limit.</param>
		private void DrainMainThreadQueue(bool drainAll)
		{
			DrainMainThreadQueue<IInteractableSystemMainThreadQueueData>(maxMainThreadActionsPerFrame, drainAll);
		}

		/// <summary>
		/// Attempts to enqueue an <see cref="Action"/> for execution on the main thread.
		/// </summary>
		/// <param name="action">The action to enqueue.</param>
		/// <returns>True if the action was enqueued; otherwise false.</returns>
		private bool TryEnqueueMainThread(Action action)
		{
			return TryEnqueueMainThread<IInteractableSystemMainThreadQueueData>(action);
		}

		/// <summary>
		/// Attempts to acquire the ingress guard for the specified connection, enforcing the global
		/// interaction debounce cooldown.
		/// </summary>
		/// <param name="connectionId">The client connection ID.</param>
		/// <param name="guardKey">When successful, the guard key to pass to <see cref="EndIngressGuard"/>.</param>
		/// <returns>True if the guard was acquired; false if the cooldown is active.</returns>
		private bool TryBeginIngressGuard(int connectionId, out long guardKey)
		{
			if (!Server.DataContainerRegistry.TryGet<IInteractableSystemRuntimeData>(out var runtimeData))
			{
				guardKey = 0;
				return false;
			}
			return runtimeData.IngressGuard.TryBegin(connectionId, 0, interactionDebounceMilliseconds, out guardKey);
		}

		/// <summary>
		/// Releases the ingress guard identified by the specified key.
		/// </summary>
		/// <param name="guardKey">The guard key returned by <see cref="TryBeginIngressGuard"/>.</param>
		private void EndIngressGuard(long guardKey)
		{
			if (Server.DataContainerRegistry.TryGet<IInteractableSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.IngressGuard.End(guardKey);
			}
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
					Items = modifiedItemBroadcasts.ToArray(),
				}, true, Channel.Reliable);

				// Fire-and-forget: persist inventory changes to DB
				if (itemsToSave.Count > 0)
				{
					if (!TryEnqueueAsyncWork(() => PersistInventoryItemsAsync(itemsToSave), character.ID))
					{
						/* async void local function captures Unity's SynchronizationContext
						 * so the continuation after await runs on the main thread.
						 * ContinueWith would run on the ThreadPool. */
						async void PersistWithFaultHandling()
						{
							try
							{
								await PersistInventoryItemsAsync(itemsToSave);
							}
							catch (Exception ex)
							{
								_ = Log.Error("InteractableSystem", $"Fallback inventory persist failed for CharID={character.ID}: {ex}");
							}
						}
						PersistWithFaultHandling();
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

				DatabaseResult result = await inventoryService.PersistAsync(items);
				if (!result.IsSuccess)
				{
					await Log.Warning("InteractableSystem", $"PersistInventoryItemsAsync DB error ({items.Count} items): {result.ErrorCode} - {result.ErrorMessage}");
				}
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
			if (!CharacterStateValidation.CanAct(character))
				return;

			if (!TryBeginIngressGuard(conn.ClientId, out long guardKey))
			{
				return;
			}

			try
			{

				/* Validate the scene the character is standing in, which is not always SceneName —
				 * see CurrentSceneName. Checking SceneName asked about the wrong scene for every
				 * interaction inside a dungeon, and happened to pass because the open-world scene
				 * is always in the cache, so the check proved nothing about the scene it was
				 * supposed to be validating. */
				string currentScene = character.CurrentSceneName();

				if (worldSceneDetailsCache == null ||
					!worldSceneDetailsCache.Scenes.TryGetValue(currentScene, out WorldSceneDetails _))
				{
					Log.Debug("InteractableSystem", "Missing Scene:" + currentScene);
					return;
				}

				// validate scene object
				if (!ValidateSceneObject(msg.InteractableID, character.GameObject.scene.handle, out ISceneObject sceneObject))
				{
					return;
				}

				IInteractable interactable = ResolveInteractable(sceneObject);
				if (interactable != null &&
					interactable.CanInteract(character))
				{
					/* Corpse looting is handled directly rather than through a trigger, because it
					 * is intrinsic to any NPC that can die: an NPC whose prefab has no interact
					 * triggers configured must still be lootable, and a content author must not be
					 * able to make a creature silently unlootable by forgetting a list entry. The
					 * ECA triggers below still run, so per-NPC extras (achievements, quest
					 * updates, dialogue) compose on top of it. */
					if (interactable is ILootableCorpse corpse)
					{
						OpenCorpseLoot(conn, character, corpse);
					}

					interactable.ExecuteOnInteract(new PlayerInteractionEventData(character, interactable,
					(ch, inv, item) => SendNewItemBroadcast(ch.Owner, ch, inv, item)));
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

		/// <summary>
		/// Resolves the interactable a client's scene object ID actually named.
		/// </summary>
		/// <remarks>
		/// The registered scene object IS the interactable whenever it implements the interface,
		/// and that identity is what the client's ID refers to. Reaching for
		/// <c>GetComponent&lt;IInteractable&gt;()</c> instead asks the GameObject for "an"
		/// interactable and takes whichever component order happens to yield — fine while every
		/// GameObject had exactly one, and wrong the moment one has two. An NPC now does: it is
		/// itself a lootable corpse, and an NPC that is also a merchant or a banker carries that
		/// component too, so a player looting a dead merchant could have their request answered by
		/// the shop. The fallback is kept for interactables that register through some other
		/// component.
		/// </remarks>
		/// <param name="sceneObject">The resolved scene object.</param>
		/// <returns>The interactable, or null.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static IInteractable ResolveInteractable(ISceneObject sceneObject)
		{
			if (sceneObject == null)
			{
				return null;
			}
			if (sceneObject is IInteractable interactable)
			{
				return interactable;
			}
			return sceneObject.GameObject != null ? sceneObject.GameObject.GetComponent<IInteractable>() : null;
		}

		/// <summary>
		/// Validates that a scene object exists and belongs to the expected scene.
		/// </summary>
		/// <param name="sceneObjectID">The ID of the scene object to validate.</param>
		/// <param name="characterSceneHandle">The scene handle of the character for scene matching.</param>
		/// <param name="sceneObject">When successful, the resolved scene object.</param>
		/// <returns>True if the scene object exists and matches the character scene; otherwise false.</returns>
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