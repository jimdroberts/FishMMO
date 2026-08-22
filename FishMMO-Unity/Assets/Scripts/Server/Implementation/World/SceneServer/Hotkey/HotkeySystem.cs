using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Shared;
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UnityEngine.SceneManagement;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
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
		/// Seconds between flushes of pending hotkey writes to the database.
		/// </summary>
		/// <remarks>
		/// Hotkey changes are coalesced per character rather than written per request. Dragging an
		/// ability along the bar produces a dozen accepted requests in a couple of seconds and only
		/// the last one can be right, so batching is both cheaper and more accurate. A crash inside
		/// the window loses at most this many seconds of re-binding, which a player repeats in
		/// moments; the disconnect path flushes immediately so a normal logout loses nothing.
		/// </remarks>
		[Header("Persistence")]
		[Tooltip("Seconds between flushes of pending hotkey writes to the database")]
		[SerializeField] private float persistFlushIntervalSeconds = 5.0f;

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
		private const byte HotkeyTypeBank = 3;
		private const byte HotkeyTypeAbility = 4;

		/// <summary>
		/// Reusable drain buffer for the persistence flush. Main-thread only.
		/// </summary>
		private readonly List<KeyValuePair<long, HotkeyData[]>> flushBuffer = new List<KeyValuePair<long, HotkeyData[]>>();

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
					ReferenceID = HotkeyData.UnsetReferenceID,
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

			/* A clear is always legal and carries no reference to validate. Both spellings of
			 * "empty" are accepted because the client sends Type 0 with the unset sentinel and the
			 * stored bar uses the sentinel with a stale type. */
			bool isClear = incomingData.Type == 0 ||
				incomingData.ReferenceID == HotkeyData.UnsetReferenceID;

			if (isClear)
			{
				playerCharacter.Hotkeys[slot] = new HotkeyData()
				{
					Type = 0,
					Slot = slot,
					ReferenceID = HotkeyData.UnsetReferenceID,
				};
				return true;
			}

			/* Server-authoritative ownership validation: verify the player actually owns the
			 * item, equipment, or ability they are attempting to bind. Shared with the
			 * connect-time prune so the two can never disagree about what a valid binding is —
			 * they did before, and in a way that mattered:
			 *
			 *  - Bank (type 3) was inside the enum range and had NO case, so it fell out of the
			 *    switch with no ownership check whatsoever.
			 *  - Ability bindings were validated with KnowsAbility(referenceID), which tests
			 *    KnownBaseAbilities — i.e. TEMPLATE ids — against what the client actually sends,
			 *    which is the crafted ability's INSTANCE id. It was also narrowed to int first,
			 *    truncating a 64-bit instance id. Validated against KnownAbilities by the full
			 *    long now, which is the collection the bar itself resolves against. */
			if (!IsHotkeyReferenceValid(playerCharacter, incomingData))
			{
				return false;
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

			/* Hotkeys were NEVER persisted. ICharacterHotkeyService.PersistAsync — both overloads —
			 * had no caller anywhere in the repository; the load path reads the rows and the save
			 * path never wrote them, so every bar was whatever the last successful write had left
			 * behind, which for most characters is nothing. Flushed periodically and on disconnect
			 * from here rather than from CharacterSystem.Saving, because this system already owns
			 * the runtime bar and the save path belongs to another area of the audit. */
			if (Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> characterSystem) &&
				characterSystem != null)
			{
				characterSystem.OnConnect += CharacterSystem_OnConnect;
				characterSystem.OnDisconnect += CharacterSystem_OnDisconnect;
			}
			else
			{
				Log.Warning("HotkeySystem", "InitializeOnce: ICharacterSystem not found; hotkeys will only be flushed periodically.");
			}

			persistFlushIntervalSeconds = Mathf.Max(1.0f, persistFlushIntervalSeconds);
			if (Server is IPeriodicUpdateSystem periodicSystem)
			{
				periodicSystem.RegisterPeriodicCallback(persistFlushIntervalSeconds, OnPeriodicPersistFlush);
			}

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

			if (Server.BehaviourRegistry.TryGet(out ICharacterSystem<NetworkConnection, Scene> characterSystem))
			{
				characterSystem.OnConnect -= CharacterSystem_OnConnect;
				characterSystem.OnDisconnect -= CharacterSystem_OnDisconnect;
			}

			if (Server is IPeriodicUpdateSystem periodicSystem)
			{
				periodicSystem.UnregisterPeriodicCallback(OnPeriodicPersistFlush);
			}

			// Last chance to get anything still staged onto disk before the system goes away.
			FlushPendingHotkeyWrites();

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

			if (playerCharacter == null || !CharacterStateValidation.CanAct(playerCharacter))
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.SetSingle, out long guardKey))
			{
				/* A refusal is still an answer. The client applies a binding locally the instant
				 * the player drops an icon on a slot, so returning in silence leaves the two sides
				 * permanently disagreeing: the bar shows a binding the server never took, and it is
				 * simply gone at the next login. This is the COMMON case, not an edge one —
				 * clearing a slot with right-click and setting it with left-click sends two
				 * requests a few milliseconds apart, comfortably inside the 75ms window, so the one
				 * the debounce drops is the set. Echoing the authoritative slot repaints the bar
				 * with whatever the server actually holds, which is the correct answer either way. */
				AcknowledgeHotkey(conn, playerCharacter, msg.HotkeyData.Slot);
				return;
			}

			try
			{
				if (TryApplyHotkey(playerCharacter, msg.HotkeyData))
				{
					StageHotkeyPersist(playerCharacter);
					AcknowledgeHotkey(conn, playerCharacter, msg.HotkeyData.Slot);
				}
				else
				{
					/* Refusal has to be told to the client, not swallowed. The client applies a
					 * binding locally the moment the player drops an icon on a slot and had no ack
					 * of any kind, so a rejected bind left the bar showing something the server
					 * does not believe in until the next relog. Echoing the AUTHORITATIVE slot —
					 * whatever the server actually holds — repaints it correctly either way. */
					AcknowledgeHotkey(conn, playerCharacter, msg.HotkeyData.Slot);
				}
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

			if (playerCharacter == null || msg.Hotkeys == null || msg.Hotkeys.Length < 1 || !CharacterStateValidation.CanAct(playerCharacter))
			{
				return;
			}

			if (!TryBeginIngressGuard(conn.ClientId, IngressOperation.SetMultiple, out long guardKey))
			{
				// Same reasoning as the single-slot handler: a refused request must still leave the
				// client holding the server's version of the bar rather than its own optimistic one.
				AcknowledgeAllHotkeys(conn, playerCharacter);
				return;
			}

			try
			{
				int applyCount = Mathf.Min(msg.Hotkeys.Length, maxBulkHotkeyUpdates);

				bool anyApplied = false;
				for (int i = 0; i < applyCount; ++i)
				{
					HotkeySetBroadcast subMsg = msg.Hotkeys[i];

					anyApplied |= TryApplyHotkey(playerCharacter, subMsg.HotkeyData);
				}

				if (anyApplied)
				{
					StageHotkeyPersist(playerCharacter);
				}

				// One authoritative echo of the whole bar, whether or not every entry was accepted.
				AcknowledgeAllHotkeys(conn, playerCharacter);
			}
			finally
			{
				EndIngressGuard(guardKey);
			}
		}

		#region Acknowledgement

		/// <summary>
		/// Echoes the server's authoritative value for one hotkey slot back to the requester.
		/// </summary>
		/// <param name="conn">The requesting connection.</param>
		/// <param name="playerCharacter">The requesting character.</param>
		/// <param name="slot">The slot to echo.</param>
		private void AcknowledgeHotkey(NetworkConnection conn, IPlayerCharacter playerCharacter, int slot)
		{
			if (conn == null ||
				playerCharacter?.Hotkeys == null ||
				slot < 0 ||
				slot >= playerCharacter.Hotkeys.Count)
			{
				return;
			}

			Server.NetworkWrapper.Broadcast(conn, new HotkeySetBroadcast()
			{
				HotkeyData = playerCharacter.Hotkeys[slot],
			}, true, Channel.Reliable);
		}

		/// <summary>
		/// Echoes the server's authoritative value for every hotkey slot back to the requester.
		/// </summary>
		/// <param name="conn">The requesting connection.</param>
		/// <param name="playerCharacter">The requesting character.</param>
		private void AcknowledgeAllHotkeys(NetworkConnection conn, IPlayerCharacter playerCharacter)
		{
			if (conn == null || playerCharacter?.Hotkeys == null || playerCharacter.Hotkeys.Count == 0)
			{
				return;
			}

			HotkeySetBroadcast[] payload = new HotkeySetBroadcast[playerCharacter.Hotkeys.Count];
			for (int i = 0; i < playerCharacter.Hotkeys.Count; ++i)
			{
				payload[i] = new HotkeySetBroadcast() { HotkeyData = playerCharacter.Hotkeys[i] };
			}

			Server.NetworkWrapper.Broadcast(conn, new HotkeySetMultipleBroadcast()
			{
				Hotkeys = payload,
			}, true, Channel.Reliable);
		}

		#endregion

		#region Persistence

		/// <summary>
		/// Records the character's current bar for the next persistence flush.
		/// </summary>
		/// <param name="playerCharacter">The character whose bar changed.</param>
		private void StageHotkeyPersist(IPlayerCharacter playerCharacter)
		{
			if (playerCharacter == null ||
				playerCharacter.ID <= 0 ||
				playerCharacter.Hotkeys == null ||
				!Server.DataContainerRegistry.TryGet<IHotkeySystemRuntimeData>(out var runtimeData))
			{
				return;
			}

			runtimeData.StageHotkeyWrite(playerCharacter.ID, playerCharacter.Hotkeys);
		}

		/// <summary>
		/// Periodic pump: writes every staged bar.
		/// </summary>
		/// <param name="deltaTime">Seconds since the previous invocation.</param>
		private void OnPeriodicPersistFlush(float deltaTime)
		{
			FlushPendingHotkeyWrites();
		}

		/// <summary>
		/// Prunes bindings whose item or ability no longer exists, before the bar is sent.
		/// </summary>
		/// <param name="conn">The connecting connection.</param>
		/// <param name="character">The connecting character.</param>
		/// <remarks>
		/// <para>
		/// A binding is validated when it is MADE, but nothing revalidates it afterwards: sell the
		/// item, unequip and bank the sword, forget and re-craft the ability, and the stored bar
		/// still names a slot index or ability id that resolves to nothing. Doing this here, on the
		/// server, before <c>SendNonDbCharacterData</c> broadcasts the bar, is what makes the prune
		/// authoritative and persistent — the client can only hide a stale slot, and a client that
		/// broadcast its own clears during login would race its own inventory payload and wipe the
		/// bar of anyone whose items arrived a frame late.
		/// </para>
		/// <para>
		/// Runs on OnConnect specifically because inventory, equipment and abilities are all
		/// populated by then (they are applied during the character instantiation that raises this
		/// event) and the hotkey broadcast has not gone out yet.
		/// </para>
		/// </remarks>
		private void CharacterSystem_OnConnect(NetworkConnection conn, IPlayerCharacter character)
		{
			if (character == null || character.Hotkeys == null)
			{
				return;
			}

			bool changed = false;
			for (int i = 0; i < character.Hotkeys.Count; ++i)
			{
				HotkeyData hotkey = character.Hotkeys[i];
				if (hotkey.Type == 0 || hotkey.ReferenceID == HotkeyData.UnsetReferenceID)
				{
					continue;
				}

				if (IsHotkeyReferenceValid(character, hotkey))
				{
					continue;
				}

				character.Hotkeys[i] = new HotkeyData()
				{
					Type = 0,
					Slot = hotkey.Slot,
					ReferenceID = HotkeyData.UnsetReferenceID,
				};
				changed = true;
			}

			if (changed)
			{
				StageHotkeyPersist(character);
			}
		}

		/// <summary>
		/// Returns true if the binding still points at something the character owns.
		/// </summary>
		/// <param name="playerCharacter">The owning character.</param>
		/// <param name="hotkey">The binding to test.</param>
		private static bool IsHotkeyReferenceValid(IPlayerCharacter playerCharacter, HotkeyData hotkey)
		{
			if (hotkey.ReferenceID < 0)
			{
				return false;
			}

			int referenceID = (int)hotkey.ReferenceID;
			switch (hotkey.Type)
			{
				case HotkeyTypeInventory:
					return playerCharacter.TryGet(out IInventoryController inventoryController) &&
						inventoryController.IsValidSlot(referenceID) &&
						inventoryController.TryGetItem(referenceID, out _);
				case HotkeyTypeEquipment:
					return playerCharacter.TryGet(out IEquipmentController equipmentController) &&
						equipmentController.IsValidSlot(referenceID) &&
						equipmentController.TryGetItem(referenceID, out _);
				case HotkeyTypeAbility:
					return playerCharacter.TryGet(out IAbilityController abilityController) &&
						abilityController.KnownAbilities.ContainsKey(hotkey.ReferenceID);
				default:
					// Bank and anything outside the enum are never valid bindings.
					return false;
			}
		}

		/// <summary>
		/// Flushes the character's bar immediately when they disconnect.
		/// </summary>
		/// <param name="conn">The departing connection.</param>
		/// <param name="character">The departing character.</param>
		/// <remarks>
		/// A normal logout must not lose the last few seconds of re-binding, and the character
		/// object is about to be despawned, so this cannot wait for the pump. Staging first picks
		/// up any change made since the last flush; the drain then writes it and removes it from
		/// the stage so the pump does not write it a second time under a newer version.
		/// </remarks>
		private void CharacterSystem_OnDisconnect(NetworkConnection conn, IPlayerCharacter character)
		{
			if (character == null || character.ID <= 0)
			{
				return;
			}

			StageHotkeyPersist(character);

			if (!Server.DataContainerRegistry.TryGet<IHotkeySystemRuntimeData>(out var runtimeData) ||
				!runtimeData.TryDrainHotkeyWrite(character.ID, out HotkeyData[] hotkeys))
			{
				return;
			}

			List<CharacterHotkeyData> dtos = BuildHotkeyDtos(runtimeData, character.ID, hotkeys);
			if (dtos.Count > 0)
			{
				EnqueuePersistence(() => PersistHotkeysAsync(dtos), character.ID);
			}
		}

		/// <summary>
		/// Drains and writes every staged hotkey bar.
		/// </summary>
		private void FlushPendingHotkeyWrites()
		{
			if (Server?.DataContainerRegistry == null ||
				!Server.DataContainerRegistry.TryGet<IHotkeySystemRuntimeData>(out var runtimeData))
			{
				return;
			}

			flushBuffer.Clear();
			if (!runtimeData.DrainHotkeyWrites(flushBuffer))
			{
				return;
			}

			for (int i = 0; i < flushBuffer.Count; ++i)
			{
				long characterID = flushBuffer[i].Key;
				List<CharacterHotkeyData> dtos = BuildHotkeyDtos(runtimeData, characterID, flushBuffer[i].Value);
				if (dtos.Count > 0)
				{
					EnqueuePersistence(() => PersistHotkeysAsync(dtos), characterID);
				}
			}
			flushBuffer.Clear();
		}

		/// <summary>
		/// Converts a staged bar into database DTOs, stamping each row with a fresh version.
		/// </summary>
		/// <param name="runtimeData">The hotkey runtime data supplying versions.</param>
		/// <param name="characterID">The owning character.</param>
		/// <param name="hotkeys">The staged bar.</param>
		/// <returns>One DTO per slot.</returns>
		/// <remarks>
		/// EVERY slot is written, including the empty ones. The rows are keyed
		/// <c>(character_id, slot)</c> and the upsert has no delete path, so writing only the
		/// occupied slots would leave a cleared slot showing its previous binding forever — the
		/// clear would simply never reach the database.
		/// </remarks>
		private static List<CharacterHotkeyData> BuildHotkeyDtos(IHotkeySystemRuntimeData runtimeData, long characterID, HotkeyData[] hotkeys)
		{
			List<CharacterHotkeyData> dtos = new List<CharacterHotkeyData>(hotkeys.Length);
			for (int i = 0; i < hotkeys.Length; ++i)
			{
				HotkeyData hotkey = hotkeys[i];
				dtos.Add(new CharacterHotkeyData(
					id: 0,
					version: runtimeData.NextHotkeyVersion(),
					characterID: characterID,
					type: hotkey.Type,
					slot: hotkey.Slot,
					referenceID: hotkey.ReferenceID));
			}
			return dtos;
		}

		/// <summary>
		/// Writes a character's hotkey bar to the database.
		/// </summary>
		/// <param name="hotkeys">The rows to persist.</param>
		private async Task PersistHotkeysAsync(List<CharacterHotkeyData> hotkeys)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterHotkeyService>(out var hotkeyService))
				{
					return;
				}

				DatabaseResult result = await hotkeyService.PersistAsync(hotkeys);
				if (!result.IsSuccess)
				{
					await Log.Warning("HotkeySystem", $"PersistHotkeysAsync DB error: {result.ErrorCode} - {result.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("HotkeySystem", $"PersistHotkeysAsync failed: {ex}");
			}
		}

		#endregion
	}
}