using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FishNet.Connection;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Logging;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;

namespace FishMMO.Server.Implementation.World.SceneServer.Interactable
{
	/// <summary>
	/// Commerce on a scene server: buying from and selling to merchants, mail, and looting corpses. Player-to-player
	/// trade is the module's <see cref="TradeSystem"/>.
	/// </summary>
	/// <remarks>
	/// The Commerce module (Phase 3, 2026-11-17). These were partials of <c>InteractableSystem</c>; they are their own
	/// system now, with their own request guard and main-thread queue, so a game without commerce leaves the module out.
	/// Merchants and mailboxes placed in the world stay in the core, and their ECA actions open the windows; a corpse's
	/// loot window opens from <see cref="IInteractableSystem.OnPlayerInteracting"/>.
	/// </remarks>
	[CreateAssetMenu(fileName = "CommerceSystem", menuName = "FishMMO/Server/SceneServer/Commerce System", order = 1)]
	[RequiresDataContainer(typeof(CommerceSystemMainThreadQueueData))]
	[RequiresDataContainer(typeof(CommerceSystemRuntimeData))]
	[RequiresDataContainer(typeof(AsyncWorkerData))]
	public partial class CommerceSystem : ServerBehaviour
	{
		[Header("Main Thread Dispatch")]
		[Tooltip("Most commerce replies drained from the main-thread queue per frame.")]
		[SerializeField] private int maxMainThreadActionsPerFrame = 100;

		[Header("Request Protection")]
		[Tooltip("Minimum milliseconds between two commerce requests from one connection (the shared operation).")]
		[SerializeField] private int interactionDebounceMilliseconds = 1000;
		[Tooltip("Seconds between sweeps of stale request-guard entries.")]
		[SerializeField] private float debounceSweepIntervalSeconds = 5.0f;
		[Tooltip("Seconds before a request-guard entry is stale.")]
		[SerializeField] private float debounceEntryTtlSeconds = 30.0f;
		[Tooltip("Most stale request-guard entries removed per sweep.")]
		[SerializeField] private int debounceSweepMaxRemovals = 128;

		/// <summary>The world scenes, to validate the scene a request is made in (the same asset the interactable system uses).</summary>
		[Header("World")]
		[SerializeField] private WorldSceneDetailsCache worldSceneDetailsCache;

		/// <summary>The currency attribute merchants, postage and corpse coin are paid in.</summary>
		[Header("Economy")]
		[SerializeField] private CharacterAttributeTemplate currencyTemplate;

		/// <summary>Most abilities a character may know; a merchant refuses to teach past it.</summary>
		[SerializeField] [Min(1)] private int maxAbilityCount = 25;

		/// <summary>The interactable system whose accepted interactions open corpse loot.</summary>
		private IInteractableSystem interactableSystem;

		/// <inheritdoc />
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error("CommerceSystem", "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}
			if (Server.Database?.ServiceRegistry == null)
			{
				Log.Error("CommerceSystem", "InitializeOnce: Database ServiceRegistry is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}
			if (!Server.DataContainerRegistry.TryGet<ICommerceSystemMainThreadQueueData>(out _) ||
				!Server.DataContainerRegistry.TryGet<ICommerceSystemRuntimeData>(out _))
			{
				Log.Error("CommerceSystem", "InitializeOnce: its data containers are missing");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}

			Server.NetworkWrapper.RegisterBroadcast<MerchantPurchaseBroadcast>(OnServerMerchantPurchaseBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<MerchantSellBroadcast>(OnServerMerchantSellBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<MailFetchBroadcast>(OnServerMailFetchBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<MailSendBroadcast>(OnServerMailSendBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<MailDeleteBroadcast>(OnServerMailDeleteBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<MailClaimAttachmentBroadcast>(OnServerMailClaimAttachmentBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<CorpseLootTakeItemBroadcast>(OnServerCorpseLootTakeItemBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<CorpseLootTakeCurrencyBroadcast>(OnServerCorpseLootTakeCurrencyBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<CorpseLootTakeAllBroadcast>(OnServerCorpseLootTakeAllBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<CorpseLootCloseBroadcast>(OnServerCorpseLootCloseBroadcastReceived, true);

			if (Server.BehaviourRegistry.TryGet(out interactableSystem) && interactableSystem != null)
			{
				interactableSystem.OnPlayerInteracting += InteractableSystem_OnPlayerInteracting;
			}
			else
			{
				Log.Warning("CommerceSystem", "InitializeOnce: IInteractableSystem not found; corpses cannot be looted.");
			}

			maxMainThreadActionsPerFrame = Mathf.Max(1, maxMainThreadActionsPerFrame);
			interactionDebounceMilliseconds = Mathf.Max(0, interactionDebounceMilliseconds);
			debounceSweepIntervalSeconds = Mathf.Max(0.25f, debounceSweepIntervalSeconds);
			debounceEntryTtlSeconds = Mathf.Max(1.0f, debounceEntryTtlSeconds);
			debounceSweepMaxRemovals = Mathf.Max(1, debounceSweepMaxRemovals);

			Log.Debug("CommerceSystem", "Initialized");
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <inheritdoc />
		public override void OnDeinitialize()
		{
			if (Server == null)
			{
				return;
			}
			DrainMainThreadQueue(drainAll: true);
			if (Server.DataContainerRegistry.TryGet<ICommerceSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.IngressGuard?.Clear();
			}

			Server.NetworkWrapper.UnregisterBroadcast<MerchantPurchaseBroadcast>(OnServerMerchantPurchaseBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<MerchantSellBroadcast>(OnServerMerchantSellBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<MailFetchBroadcast>(OnServerMailFetchBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<MailSendBroadcast>(OnServerMailSendBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<MailDeleteBroadcast>(OnServerMailDeleteBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<MailClaimAttachmentBroadcast>(OnServerMailClaimAttachmentBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<CorpseLootTakeItemBroadcast>(OnServerCorpseLootTakeItemBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<CorpseLootTakeCurrencyBroadcast>(OnServerCorpseLootTakeCurrencyBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<CorpseLootTakeAllBroadcast>(OnServerCorpseLootTakeAllBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<CorpseLootCloseBroadcast>(OnServerCorpseLootCloseBroadcastReceived);

			if (interactableSystem != null)
			{
				interactableSystem.OnPlayerInteracting -= InteractableSystem_OnPlayerInteracting;
				interactableSystem = null;
			}

			ClearCorpseSubscriptions();
		}

		/// <inheritdoc />
		protected override void OnUpdate(float deltaTime)
		{
			DrainMainThreadQueue(drainAll: false);
			if (Server.DataContainerRegistry.TryGet<ICommerceSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.IngressGuard.Sweep(debounceSweepIntervalSeconds, debounceEntryTtlSeconds, debounceSweepMaxRemovals);
			}
		}

		/// <summary>
		/// A corpse's loot window opens on any accepted interaction with it: looting is intrinsic to an NPC that can die,
		/// so no content author can make a creature silently unlootable by forgetting a trigger.
		/// </summary>
		private void InteractableSystem_OnPlayerInteracting(IPlayerCharacter character, IInteractable interactable)
		{
			if (interactable is ILootableCorpse corpse && character?.Owner != null)
			{
				OpenCorpseLoot(character.Owner, character, corpse);
			}
		}

		private void DrainMainThreadQueue(bool drainAll)
		{
			DrainMainThreadQueue<ICommerceSystemMainThreadQueueData>(maxMainThreadActionsPerFrame, drainAll);
		}

		/// <summary>Runs <paramref name="action"/> on the main thread; false when the queue refused it.</summary>
		private bool TryEnqueueMainThread(Action action)
		{
			return TryEnqueueMainThread<ICommerceSystemMainThreadQueueData>(action);
		}

		/// <summary>Acquires the request guard for the shared operation (0) at the system's debounce.</summary>
		private bool TryBeginIngressGuard(int connectionId, out long guardKey)
		{
			return TryBeginIngressGuard(connectionId, 0, interactionDebounceMilliseconds, out guardKey);
		}

		/// <summary>Acquires the request guard for one named operation at its own debounce.</summary>
		private bool TryBeginIngressGuard(int connectionId, byte operation, int debounceMilliseconds, out long guardKey)
		{
			if (!Server.DataContainerRegistry.TryGet<ICommerceSystemRuntimeData>(out var runtimeData))
			{
				guardKey = 0;
				return false;
			}
			return runtimeData.IngressGuard.TryBegin(connectionId, operation, debounceMilliseconds, out guardKey);
		}

		/// <summary>Releases the request guard.</summary>
		private void EndIngressGuard(long guardKey)
		{
			if (Server.DataContainerRegistry.TryGet<ICommerceSystemRuntimeData>(out var runtimeData))
			{
				runtimeData.IngressGuard.End(guardKey);
			}
		}

		/// <summary>
		/// Grants an item through the inventory system's grant funnel (placed, broadcast to the owner, persisted, given its
		/// identity). Refused without an inventory system: an item that never learns its id can never be moved, and the
		/// caller's fallback (back on the corpse, refund the purchase) is the better outcome.
		/// </summary>
		private bool SendNewItemBroadcast(NetworkConnection conn, ICharacter character, IInventoryController inventoryController, Item newItem)
		{
			if (character is not IPlayerCharacter playerCharacter)
			{
				Log.Error("CommerceSystem", "SendNewItemBroadcast: the character is not a player character.");
				return false;
			}
			if (!Server.BehaviourRegistry.TryGet(out ICharacterInventorySystem inventorySystem))
			{
				Log.Error("CommerceSystem", $"SendNewItemBroadcast: ICharacterInventorySystem not found; refusing to grant an item CharID={character.ID} could never persist.");
				return false;
			}
			return inventorySystem.TryGrantItem(playerCharacter, newItem, InventoryType.Inventory);
		}

		/// <summary>Hands inventory rows a sale or a mail attachment changed to the inventory system's journalled batch.</summary>
		private void PersistInventoryChanges(IPlayerCharacter character, IReadOnlyList<Item> changed, IReadOnlyList<RemovedItemRecord> removed)
		{
			if (!Server.BehaviourRegistry.TryGet(out ICharacterInventorySystem inventorySystem))
			{
				Log.Error("CommerceSystem", $"PersistInventoryChanges: ICharacterInventorySystem not found; an inventory change for CharID={character?.ID} was not persisted.");
				return;
			}
			inventorySystem.PersistInventoryChanges(character, changed, removed);
		}

		/// <summary>The interactable a scene object names (the corpse-versus-living rule is <see cref="InteractableResolver"/>'s).</summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private static IInteractable ResolveInteractable(ISceneObject sceneObject)
		{
			return InteractableResolver.Resolve(sceneObject);
		}

		/// <summary>
		/// The scene object with <paramref name="sceneObjectID"/>, when it exists, is live (not pooled) and stands in the
		/// character's scene (the same check the interactable system makes: a pooled object keeps its id).
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		private bool ValidateSceneObject(long sceneObjectID, SceneHandle characterSceneHandle, out ISceneObject sceneObject)
		{
			if (!SceneObject.Objects.TryGetValue(sceneObjectID, out sceneObject))
			{
				Log.Debug("CommerceSystem", $"Missing SceneObject ID:{sceneObjectID}");
				return false;
			}
			GameObject gameObject = sceneObject.GameObject;
			if (gameObject == null || !gameObject.activeInHierarchy)
			{
				Log.Debug("CommerceSystem", $"SceneObject ID:{sceneObjectID} is despawned.");
				sceneObject = null;
				return false;
			}
			if (gameObject.scene.handle != characterSceneHandle)
			{
				Log.Debug("CommerceSystem", "Object scene mismatch.");
				sceneObject = null;
				return false;
			}
			return true;
		}
	}
}
