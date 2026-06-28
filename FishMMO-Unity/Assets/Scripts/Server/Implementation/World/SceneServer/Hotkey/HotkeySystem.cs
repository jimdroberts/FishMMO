using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Shared;
using System.Collections.Generic;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared.Core;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Manages player hotkey configurations, allowing clients to set and update hotkey bindings for abilities and items.
	/// </summary>
	[CreateAssetMenu(fileName = "HotkeySystem", menuName = "FishMMO/Server/SceneServer/Hotkey System", order = 1)]
	[RequiresDataContainer(typeof(HotkeySystemRuntimeData))]
	public class HotkeySystem : ServerBehaviour, IHotkeySystem
	{
		/// <summary>
		/// Debounce window in milliseconds for hotkey ingress requests.
		/// </summary>
		[Header("Ingress Protection")]
		[Tooltip("Minimum milliseconds between hotkey requests per connection")]
		[SerializeField] private int ingressDebounceMilliseconds = 75;

		/// <summary>
		/// Maximum hotkey updates accepted in one bulk request.
		/// </summary>
		[Tooltip("Maximum hotkey updates accepted in one bulk request")]
		[SerializeField] private int maxBulkHotkeyUpdates = 64;

		/// <summary>
		/// Interval in seconds between ingress-guard cleanup sweeps.
		/// </summary>
		[Tooltip("Seconds between bounded ingress guard cleanup sweeps")]
		[SerializeField] private float ingressSweepIntervalSeconds = 5.0f;

		/// <summary>
		/// Guard entry time-to-live in seconds.
		/// </summary>
		[Tooltip("Seconds before stale ingress guard entries are removed")]
		[SerializeField] private float ingressEntryTtlSeconds = 30.0f;

		/// <summary>
		/// Maximum stale guard entries removed per cleanup sweep.
		/// </summary>
		[Tooltip("Maximum stale ingress guard entries removed per sweep")]
		[SerializeField] private int ingressSweepMaxRemovals = 128;

		/// <summary>
		/// Operation codes used by hotkey ingress guards.
		/// </summary>
		private enum IngressOperation : byte
		{
			SetSingle = 1,
			SetMultiple = 2,
		}

		/// <summary>
		/// Maximum valid hotkey type byte value.
		/// Mirrors ReferenceButtonType (Client assembly, unreferenceable from Server):
		/// None=0, Inventory=1, Equipment=2, Bank=3, Ability=4.
		/// </summary>
		private const byte MaxHotkeyType = 4;

		/// <summary>
		/// Hotkey type constants mirroring ReferenceButtonType values from the Client assembly.
		/// </summary>
		private const byte HotkeyTypeInventory = 1;
		private const byte HotkeyTypeEquipment = 2;
		private const byte HotkeyTypeAbility = 4;

		/// <summary>
		/// Ensures the character hotkey list exists and is initialized to the configured maximum size.
		/// </summary>
		/// <param name="playerCharacter">Character whose hotkeys should be initialized.</param>
		private static void EnsureHotkeysInitialized(IPlayerCharacter playerCharacter)
		{
			if (playerCharacter.Hotkeys != null)
			{
				return;
			}

			playerCharacter.Hotkeys = new List<HotkeyData>(Constants.Configuration.MaximumPlayerHotkeys);
			for (int i = 0; i < Constants.Configuration.MaximumPlayerHotkeys; ++i)
			{
				playerCharacter.Hotkeys.Add(new HotkeyData()
				{
					Slot = i,
				});
			}
		}

		/// <summary>
		/// Tries to apply a hotkey binding to the character hotkey list.
		/// Validates ownership: the player must actually own the item/equipment/ability
		/// they are attempting to bind to a hotkey slot.
		/// </summary>
		/// <param name="playerCharacter">Character receiving the hotkey binding.</param>
		/// <param name="incomingData">Incoming hotkey data from client message.</param>
		/// <returns>True if the hotkey was applied; otherwise false.</returns>
		private static bool TryApplyHotkey(IPlayerCharacter playerCharacter, HotkeyData incomingData)
		{
			EnsureHotkeysInitialized(playerCharacter);

			// Validate hotkey type is within the defined enum range
			if (incomingData.Type > MaxHotkeyType)
			{
				return false;
			}

			if (incomingData.ReferenceID < -1)
			{
				return false;
			}

			int slot = incomingData.Slot;
			if (slot < 0 || slot >= playerCharacter.Hotkeys.Count)
			{
				return false;
			}

			// Server-authoritative ownership validation: verify the player actually
			// owns the item, equipment, or ability they are attempting to bind.
			// Without this check, a client could bind any ID regardless of ownership.
			int referenceID = (int)incomingData.ReferenceID;
			switch (incomingData.Type)
			{
				case HotkeyTypeInventory:
					if (referenceID >= 0)
					{
						if (!playerCharacter.TryGet(out IInventoryController inventoryController) ||
							!inventoryController.IsValidSlot(referenceID) ||
							!inventoryController.TryGetItem(referenceID, out _))
						{
							return false;
						}
					}
					break;
				case HotkeyTypeEquipment:
					if (referenceID >= 0)
					{
						if (!playerCharacter.TryGet(out IEquipmentController equipmentController) ||
							!equipmentController.IsValidSlot(referenceID) ||
							!equipmentController.TryGetItem(referenceID, out _))
						{
							return false;
						}
					}
					break;
				case HotkeyTypeAbility:
					if (referenceID >= 0)
					{
						if (!playerCharacter.TryGet(out IAbilityController abilityController) ||
							!abilityController.KnowsAbility(referenceID))
						{
							return false;
						}
					}
					break;
			}

			playerCharacter.Hotkeys[slot] = new HotkeyData()
			{
				Type = incomingData.Type,
				Slot = slot,
				ReferenceID = incomingData.ReferenceID,
			};

			return true;
		}

		/// <summary>
		/// Initializes the hotkey system, registering broadcast handlers for hotkey set and hotkey set multiple requests.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error("HotkeySystem", "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (!Server.DataContainerRegistry.TryGet<IHotkeySystemRuntimeData>(out var runtimeData))
			{
				Log.Error("HotkeySystem", "InitializeOnce: IHotkeySystemRuntimeData not found");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			// Network broadcasts
			Server.NetworkWrapper.RegisterBroadcast<HotkeySetBroadcast>(OnServerHotkeySetBroadcastReceived, true);
			Server.NetworkWrapper.RegisterBroadcast<HotkeySetMultipleBroadcast>(OnServerHotkeySetMultipleBroadcastReceived, true);

			ingressDebounceMilliseconds = Mathf.Max(0, ingressDebounceMilliseconds);
			maxBulkHotkeyUpdates = Mathf.Max(1, maxBulkHotkeyUpdates);
			ingressSweepIntervalSeconds = Mathf.Max(0.25f, ingressSweepIntervalSeconds);
			ingressEntryTtlSeconds = Mathf.Max(1.0f, ingressEntryTtlSeconds);
			ingressSweepMaxRemovals = Mathf.Max(1, ingressSweepMaxRemovals);

			Log.Debug("HotkeySystem", "Initialized");
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Cleans up the hotkey system, unregistering broadcast handlers.
		/// </summary>
		public override void OnDeinitialize()
		{
			if (Server == null)
			{
				Log.Error("HotkeySystem", "OnDeinitialize: Server is null");
				return;
			}

			// Network broadcasts
			Server.NetworkWrapper.UnregisterBroadcast<HotkeySetBroadcast>(OnServerHotkeySetBroadcastReceived);
			Server.NetworkWrapper.UnregisterBroadcast<HotkeySetMultipleBroadcast>(OnServerHotkeySetMultipleBroadcastReceived);

			if (Server.DataContainerRegistry.TryGet<IHotkeySystemRuntimeData>(out var runtimeData))
			{
				runtimeData.IngressGuard?.Clear();
			}
		}

		/// <summary>
		/// Drains stale ingress entries with bounded cleanup each frame.
		/// </summary>
		protected override void OnUpdate(float deltaTime)
		{
			if (Server.DataContainerRegistry.TryGet<IHotkeySystemRuntimeData>(out var runtimeData))
			{
				runtimeData.IngressGuard.Sweep(ingressSweepIntervalSeconds, ingressEntryTtlSeconds, ingressSweepMaxRemovals);
			}
		}

		/// <summary>
		/// Attempts to acquire ingress debounce and in-flight guard for a connection operation.
		/// </summary>
		private bool TryBeginIngressGuard(int connectionId, IngressOperation operation, out long guardKey)
		{
			if (!Server.DataContainerRegistry.TryGet<IHotkeySystemRuntimeData>(out var runtimeData))
			{
				guardKey = 0;
				return false;
			}
			return runtimeData.IngressGuard.TryBegin(connectionId, (byte)operation, ingressDebounceMilliseconds, out guardKey);
		}

		/// <summary>
		/// Releases a previously acquired ingress guard key.
		/// </summary>
		private void EndIngressGuard(long guardKey)
		{
			if (Server.DataContainerRegistry.TryGet<IHotkeySystemRuntimeData>(out var runtimeData))
			{
				runtimeData.IngressGuard.End(guardKey);
			}
		}

		/// <summary>
		/// Handles broadcast to set a single hotkey for a player character.
		/// Validates the hotkey list and slot, then updates the hotkey data for the specified slot.
		/// </summary>
		/// <param name="conn">Network connection of the requesting client.</param>
		/// <param name="msg">HotkeySetBroadcast message containing hotkey data.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerHotkeySetBroadcastReceived(NetworkConnection conn, HotkeySetBroadcast msg, Channel channel)
		{
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			IPlayerCharacter playerCharacter = conn.FirstObject.GetComponent<IPlayerCharacter>();

			if (playerCharacter == null || msg.HotkeyData == null || !CharacterStateValidation.CanAct(playerCharacter))
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.SetSingle, out long guardKey))
			{
				return;
			}

			try
			{
				TryApplyHotkey(playerCharacter, msg.HotkeyData);
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		/// <summary>
		/// Handles broadcast to set multiple hotkeys for a player character.
		/// Iterates through each hotkey message, validates the hotkey list and slot, then updates the hotkey data for each slot.
		/// </summary>
		/// <param name="conn">Network connection of the requesting client.</param>
		/// <param name="msg">HotkeySetMultipleBroadcast message containing multiple hotkey data entries.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		public void OnServerHotkeySetMultipleBroadcastReceived(NetworkConnection conn, HotkeySetMultipleBroadcast msg, Channel channel)
		{
			if (conn == null || conn.FirstObject == null)
			{
				return;
			}

			IPlayerCharacter playerCharacter = conn.FirstObject.GetComponent<IPlayerCharacter>();

			if (playerCharacter == null || msg.Hotkeys == null || msg.Hotkeys.Count < 1 || !CharacterStateValidation.CanAct(playerCharacter))
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.SetMultiple, out long guardKey))
			{
				return;
			}

			try
			{
				int applyCount = Mathf.Min(msg.Hotkeys.Count, maxBulkHotkeyUpdates);

				for (int i = 0; i < applyCount; ++i)
				{
					HotkeySetBroadcast subMsg = msg.Hotkeys[i];
					if (subMsg.HotkeyData == null)
					{
						continue;
					}

					TryApplyHotkey(playerCharacter, subMsg.HotkeyData);
				}
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}
	}
}