using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using FishNet.Connection;
using FishNet.Transporting;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Logging;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Server.Implementation.World.SceneServer.Interactable
{
	/// <summary>
	/// Portals: owns the server's <see cref="PortalActivationStore"/>, reads it from and writes it to
	/// the database, and tells clients which portals are open for them.
	/// </summary>
	/// <remarks>
	/// <para><b>What this file does not do.</b> It never decides a use of a portal; that is
	/// <see cref="PortalGate"/>, called by the teleporters in the shared assembly. The gate reads and
	/// writes the store installed here as <see cref="PortalActivationStore.Current"/>; this file
	/// hears about the writes (<see cref="PortalActivationStore.CharacterActivated"/>,
	/// <see cref="PortalActivationStore.WorldActivated"/>) and does the two things the shared
	/// assembly cannot: persist them and announce them.</para>
	/// <para><b>Reading.</b> A character's own activations (<c>character_portals</c>) are read when
	/// it finishes loading, the way the dialogue choice cache is; a scene's world openings
	/// (<c>world_portal_state</c>) when its first portal registers, and again every
	/// <see cref="portalWorldRefreshSeconds"/> so an opening made on another scene server reaches
	/// this one. Until a record is read the gate refuses with "not ready" rather than treating the
	/// portal as closed.</para>
	/// <para><b>Writing.</b> Each activation is merged at once; whatever the database has not
	/// confirmed stays dirty in the store and is retried every <see cref="portalFlushIntervalSeconds"/>
	/// and when the character leaves. Both merges are idempotent (OR, and the later deadline), so a
	/// retry can never undo anything.</para>
	/// <para><b>Telling clients.</b> Broadcasts only (the observer policy): the full set for a scene
	/// to a player entering it or once its records load, one change to the owner for a per-character
	/// activation, and one change to every player in the scene for a world opening.</para>
	/// <para>Without the two database services the store is memory-only and every record counts as
	/// loaded: portals still work, and forget on restart.</para>
	/// </remarks>
	public partial class InteractableSystem
	{
		[Header("Portals")]
		[Tooltip("Seconds between retries of portal activations the database has not confirmed.")]
		[SerializeField] private float portalFlushIntervalSeconds = 30.0f;

		[Tooltip("Seconds between re-reads of the world portal rows of every scene with a portal, so an opening made on another scene server shows up here.")]
		[SerializeField] private float portalWorldRefreshSeconds = 120.0f;

		/// <summary>Seconds after which an in-flight portal read is assumed lost and may be retried.</summary>
		private const float PortalLoadTimeoutSeconds = 30.0f;

		private PortalActivationStore portalStore;
		private ICharacterSystem<NetworkConnection, Scene> portalCharacterSystem;
		private float portalFlushTimer;
		private float portalRefreshTimer;

		/// <summary>Characters loaded on this server, by ID. Main thread.</summary>
		private readonly Dictionary<long, IPlayerCharacter> portalResidents = new Dictionary<long, IPlayerCharacter>();

		/// <summary>Character reads in flight and when they started (unscaled time).</summary>
		private readonly Dictionary<long, float> portalCharactersLoading = new Dictionary<long, float>();

		/// <summary>Scene reads in flight and when they started (unscaled time).</summary>
		private readonly Dictionary<string, float> portalScenesLoading = new Dictionary<string, float>(StringComparer.Ordinal);

		private readonly List<WaypointPageSnapshot> portalPageScratch = new List<WaypointPageSnapshot>();
		private readonly List<PortalActivation> portalScratch = new List<PortalActivation>();

		private void InitializePortals()
		{
			portalFlushIntervalSeconds = Mathf.Max(1.0f, portalFlushIntervalSeconds);
			portalWorldRefreshSeconds = Mathf.Max(5.0f, portalWorldRefreshSeconds);

			bool persistent = TryGetDbService(out ICharacterPortalService _) && TryGetDbService(out IWorldPortalStateService _);
			if (!persistent)
			{
				Log.Warning("InteractableSystem", "Portal services (ICharacterPortalService / IWorldPortalStateService) unavailable: portal activations are kept in memory only and forgotten on restart.");
			}

			portalStore = new PortalActivationStore(assumeLoaded: !persistent);
			portalStore.CharacterActivated += PortalStore_OnCharacterActivated;
			portalStore.WorldActivated += PortalStore_OnWorldActivated;
			portalStore.LoadRequested += PortalStore_OnLoadRequested;
			PortalActivationStore.Current = portalStore;

			PortalRegistry.Registered += PortalRegistry_OnRegistered;

			if (Server.BehaviourRegistry.TryGet(out portalCharacterSystem) && portalCharacterSystem != null)
			{
				portalCharacterSystem.OnAfterLoadCharacter += CharacterSystem_OnPortalCharacterLoaded;
				portalCharacterSystem.OnDisconnect += CharacterSystem_OnPortalCharacterDisconnected;
				portalCharacterSystem.OnSpawnCharacter += CharacterSystem_OnPortalCharacterSpawned;
			}
			else
			{
				Log.Warning("InteractableSystem", "InitializePortals: ICharacterSystem not found; per-character portal activations will never load, so per-character portals will answer \"not ready\".");
			}

			// Scenes loaded before this system started already registered their portals.
			var sceneNames = new HashSet<string>(StringComparer.Ordinal);
			PortalRegistry.CollectSceneNames(sceneNames);
			foreach (string sceneName in sceneNames)
			{
				TryLoadScenePortals(sceneName);
			}
		}

		private void DeinitializePortals()
		{
			PortalRegistry.Registered -= PortalRegistry_OnRegistered;

			if (portalCharacterSystem != null)
			{
				portalCharacterSystem.OnAfterLoadCharacter -= CharacterSystem_OnPortalCharacterLoaded;
				portalCharacterSystem.OnDisconnect -= CharacterSystem_OnPortalCharacterDisconnected;
				portalCharacterSystem.OnSpawnCharacter -= CharacterSystem_OnPortalCharacterSpawned;
				portalCharacterSystem = null;
			}

			if (portalStore != null)
			{
				// One last attempt; the merges are idempotent, so a duplicate of a write in flight is harmless.
				FlushPortals();

				var dirtyCharacters = new List<long>();
				portalStore.CollectDirtyCharacters(dirtyCharacters);
				if (dirtyCharacters.Count > 0 || portalStore.UnconfirmedWorldCount > 0)
				{
					Log.Warning("InteractableSystem", $"Shutting down with unconfirmed portal activations: {dirtyCharacters.Count} character(s) [{string.Join(", ", dirtyCharacters)}], {portalStore.UnconfirmedWorldCount} world opening(s). The final flush above may still land them.");
				}

				portalStore.CharacterActivated -= PortalStore_OnCharacterActivated;
				portalStore.WorldActivated -= PortalStore_OnWorldActivated;
				portalStore.LoadRequested -= PortalStore_OnLoadRequested;
				if (ReferenceEquals(PortalActivationStore.Current, portalStore))
				{
					PortalActivationStore.Current = null;
				}
				portalStore = null;
			}

			portalResidents.Clear();
			portalCharactersLoading.Clear();
			portalScenesLoading.Clear();
		}

		/// <summary>The flush and refresh timers. Called from <c>OnUpdate</c>.</summary>
		private void UpdatePortals(float deltaTime)
		{
			if (portalStore == null || portalStore.AssumeLoaded)
			{
				return;
			}

			portalFlushTimer += deltaTime;
			if (portalFlushTimer >= portalFlushIntervalSeconds)
			{
				portalFlushTimer = 0.0f;
				FlushPortals();
			}

			portalRefreshTimer += deltaTime;
			if (portalRefreshTimer >= portalWorldRefreshSeconds)
			{
				portalRefreshTimer = 0.0f;
				var sceneNames = new HashSet<string>(StringComparer.Ordinal);
				PortalRegistry.CollectSceneNames(sceneNames);
				foreach (string sceneName in sceneNames)
				{
					TryLoadScenePortals(sceneName, refresh: true);
				}
			}
		}

		#region Character lifecycle

		private void CharacterSystem_OnPortalCharacterLoaded(NetworkConnection conn, IPlayerCharacter character)
		{
			if (character == null || character.ID <= 0)
			{
				return;
			}
			portalResidents[character.ID] = character;
			TryLoadCharacterPortals(character.ID);
		}

		private void CharacterSystem_OnPortalCharacterDisconnected(NetworkConnection conn, IPlayerCharacter character)
		{
			if (character == null || portalStore == null)
			{
				return;
			}
			portalResidents.Remove(character.ID);
			// The last chance to write what this server holds for the character; the record stays, unloaded, until confirmed.
			EnqueuePortalCharacterMerge(character.ID);
			portalStore.ReleaseCharacter(character.ID);
		}

		private void CharacterSystem_OnPortalCharacterSpawned(NetworkConnection conn, IPlayerCharacter character, Scene scene)
		{
			SendPortalStates(character);
		}

		private void PortalRegistry_OnRegistered(PortalActivation portal)
		{
			if (portal != null)
			{
				TryLoadScenePortals(portal.SceneName);
			}
		}

		private void PortalStore_OnLoadRequested(long characterID, string sceneName)
		{
			if (characterID > 0 && portalResidents.ContainsKey(characterID))
			{
				TryLoadCharacterPortals(characterID);
			}
			if (!string.IsNullOrEmpty(sceneName))
			{
				TryLoadScenePortals(sceneName);
			}
		}

		#endregion

		#region Reading

		/// <summary>Starts reading a character's activations, unless loaded or already in flight.</summary>
		private void TryLoadCharacterPortals(long characterID)
		{
			if (portalStore == null || portalStore.IsCharacterLoaded(characterID))
			{
				return;
			}

			float now = Time.unscaledTime;
			if (portalCharactersLoading.TryGetValue(characterID, out float startedAt) && now - startedAt < PortalLoadTimeoutSeconds)
			{
				return;
			}

			if (!TryGetDbService(out ICharacterPortalService service))
			{
				return;
			}

			portalCharactersLoading[characterID] = now;
			PortalActivationStore store = portalStore;
			if (!TryEnqueueAsyncWork(() => LoadCharacterPortalsAsync(service, store, characterID), characterID))
			{
				portalCharactersLoading.Remove(characterID);
			}
		}

		private async Task LoadCharacterPortalsAsync(ICharacterPortalService service, PortalActivationStore store, long characterID)
		{
			IReadOnlyList<CharacterPortalData> rows = null;
			try
			{
				DatabaseResult<IReadOnlyList<CharacterPortalData>> result = await service.FetchAsync(characterID);
				if (result.IsSuccess)
				{
					rows = result.Data;
				}
				else
				{
					await Log.Warning("InteractableSystem", $"LoadCharacterPortalsAsync DB error (CharID={characterID}): {result.ErrorCode} - {result.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("InteractableSystem", $"LoadCharacterPortalsAsync failed (CharID={characterID}): {ex}");
			}

			if (!TryEnqueueMainThread(() => ApplyLoadedCharacterPortals(store, characterID, rows)))
			{
				// The in-flight marker expires on its own; the next use of a portal retries.
				await Log.Warning("InteractableSystem", $"LoadCharacterPortalsAsync: main-thread queue rejected the hand-off for CharID={characterID}.");
			}
		}

		/// <summary>Installs a character's rows. Main thread.</summary>
		private void ApplyLoadedCharacterPortals(PortalActivationStore store, long characterID, IReadOnlyList<CharacterPortalData> rows)
		{
			portalCharactersLoading.Remove(characterID);
			if (rows == null || !ReferenceEquals(store, portalStore))
			{
				// A failed read stays unloaded so the next use retries rather than answering "closed".
				return;
			}
			if (!portalResidents.TryGetValue(characterID, out IPlayerCharacter character))
			{
				// Left while the read was in flight; loading it now would refill what the disconnect cleared.
				return;
			}

			for (int i = 0; i < rows.Count; ++i)
			{
				CharacterPortalData row = rows[i];
				store.RestoreCharacterPage(characterID, row.SceneName, row.Page, row.Mask);
			}
			store.MarkCharacterLoaded(characterID);

			// Bits set on an earlier visit whose write never landed are back in the record; write them now.
			EnqueuePortalCharacterMerge(characterID);

			SendPortalStates(character);
		}

		/// <summary>Starts reading a scene's world rows, unless loaded (or <paramref name="refresh"/>) or in flight.</summary>
		private void TryLoadScenePortals(string sceneName, bool refresh = false)
		{
			if (portalStore == null || string.IsNullOrEmpty(sceneName) || portalStore.AssumeLoaded)
			{
				return;
			}
			if (!refresh && portalStore.IsSceneLoaded(sceneName))
			{
				return;
			}

			float now = Time.unscaledTime;
			if (portalScenesLoading.TryGetValue(sceneName, out float startedAt) && now - startedAt < PortalLoadTimeoutSeconds)
			{
				return;
			}

			if (!TryGetDbService(out IWorldPortalStateService service))
			{
				return;
			}

			portalScenesLoading[sceneName] = now;
			PortalActivationStore store = portalStore;
			if (!TryEnqueueAsyncWork(() => LoadScenePortalsAsync(service, store, sceneName)))
			{
				portalScenesLoading.Remove(sceneName);
			}
		}

		private async Task LoadScenePortalsAsync(IWorldPortalStateService service, PortalActivationStore store, string sceneName)
		{
			List<(int Index, PortalWorldState State)> rows = null;
			try
			{
				DatabaseResult<IReadOnlyList<WorldPortalStateData>> result = await service.FetchAsync(sceneName);
				if (result.IsSuccess)
				{
					rows = new List<(int Index, PortalWorldState State)>(result.Data.Count);
					foreach (WorldPortalStateData row in result.Data)
					{
						rows.Add((row.PortalIndex, new PortalWorldState(row.Permanent, ToUnixMs(row.ActiveUntilUtc))));
					}
				}
				else
				{
					await Log.Warning("InteractableSystem", $"LoadScenePortalsAsync DB error (Scene={sceneName}): {result.ErrorCode} - {result.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("InteractableSystem", $"LoadScenePortalsAsync failed (Scene={sceneName}): {ex}");
			}

			if (!TryEnqueueMainThread(() => ApplyLoadedScenePortals(store, sceneName, rows)))
			{
				await Log.Warning("InteractableSystem", $"LoadScenePortalsAsync: main-thread queue rejected the hand-off for '{sceneName}'.");
			}
		}

		/// <summary>Installs a scene's world rows and tells the players standing in it. Main thread.</summary>
		private void ApplyLoadedScenePortals(PortalActivationStore store, string sceneName, List<(int Index, PortalWorldState State)> rows)
		{
			portalScenesLoading.Remove(sceneName);
			if (rows == null || !ReferenceEquals(store, portalStore))
			{
				return;
			}

			bool firstLoad = !store.IsSceneLoaded(sceneName);
			List<int> grew = store.RestoreWorldScene(sceneName, rows);
			if (!firstLoad && grew.Count == 0)
			{
				return;
			}

			// A first read answers every player who was told "not ready"; a refresh only matters when another server opened something.
			foreach (IPlayerCharacter character in portalResidents.Values)
			{
				if (character?.GameObject != null &&
					string.Equals(character.GameObject.scene.name, sceneName, StringComparison.Ordinal))
				{
					SendPortalStates(character);
				}
			}
		}

		#endregion

		#region Writing

		private void PortalStore_OnCharacterActivated(long characterID, string sceneName, int portalIndex)
		{
			// Site discoveries share the record (PointOfInterestDiscovery); they are persisted here but are not portal states.
			if (!PointOfInterestDiscovery.IsDiscoveryKey(sceneName) &&
				portalResidents.TryGetValue(characterID, out IPlayerCharacter character) && character?.Owner != null)
			{
				Server.NetworkWrapper.Broadcast(character.Owner, new PortalStateChangedBroadcast()
				{
					SceneName = sceneName,
					Index = (ushort)portalIndex,
					RemainingSeconds = PortalActivationRules.NoExpiry,
				}, true, Channel.Reliable);
			}
			EnqueuePortalCharacterMerge(characterID);
		}

		private void PortalStore_OnWorldActivated(string sceneName, int portalIndex, PortalWorldState state)
		{
			int remaining = state.RemainingSeconds(portalStore != null ? portalStore.NowUnixMs : DateTimeOffset.UtcNow.ToUnixTimeMilliseconds());
			foreach (IPlayerCharacter character in portalResidents.Values)
			{
				if (character?.Owner == null || character.GameObject == null ||
					!string.Equals(character.GameObject.scene.name, sceneName, StringComparison.Ordinal))
				{
					continue;
				}
				Server.NetworkWrapper.Broadcast(character.Owner, new PortalStateChangedBroadcast()
				{
					SceneName = sceneName,
					Index = (ushort)portalIndex,
					RemainingSeconds = remaining,
				}, true, Channel.Reliable);
			}
			EnqueuePortalWorldMerge();
		}

		/// <summary>Retries everything the database has not confirmed.</summary>
		private void FlushPortals()
		{
			if (portalStore == null || portalStore.AssumeLoaded)
			{
				return;
			}
			var dirty = new List<long>();
			portalStore.CollectDirtyCharacters(dirty);
			for (int i = 0; i < dirty.Count; ++i)
			{
				EnqueuePortalCharacterMerge(dirty[i]);
			}
			EnqueuePortalWorldMerge();
		}

		/// <summary>Merges a character's unconfirmed pages and confirms them in the store on success.</summary>
		private void EnqueuePortalCharacterMerge(long characterID)
		{
			if (portalStore == null || portalStore.AssumeLoaded || !TryGetDbService(out ICharacterPortalService service))
			{
				return;
			}

			portalPageScratch.Clear();
			portalStore.CollectDirtyPages(characterID, portalPageScratch);
			if (portalPageScratch.Count == 0)
			{
				return;
			}

			var rows = new List<CharacterPortalData>(portalPageScratch.Count);
			for (int i = 0; i < portalPageScratch.Count; ++i)
			{
				WaypointPageSnapshot page = portalPageScratch[i];
				rows.Add(new CharacterPortalData(characterID, page.SceneName, (short)page.Page, page.Mask));
			}

			PortalActivationStore store = portalStore;
			EnqueuePersistence(async () =>
			{
				try
				{
					DatabaseResult result = await service.MergeAsync(rows);
					if (!result.IsSuccess)
					{
						await Log.Warning("InteractableSystem", $"Portal activation save failed (CharID={characterID}); held for the next flush: [{result.ErrorCode}] {result.ErrorMessage}");
						return;
					}
					TryEnqueueMainThread(() =>
					{
						for (int i = 0; i < rows.Count; ++i)
						{
							store.MarkCharacterPersisted(rows[i].CharacterID, rows[i].SceneName, rows[i].Page, rows[i].Mask);
						}
					});
				}
				catch (Exception ex)
				{
					await Log.Error("InteractableSystem", $"Portal activation save threw (CharID={characterID}): {ex}");
				}
			}, characterID);
		}

		/// <summary>Merges every unconfirmed world opening and confirms them in the store on success.</summary>
		private void EnqueuePortalWorldMerge()
		{
			if (portalStore == null || portalStore.AssumeLoaded || portalStore.UnconfirmedWorldCount == 0 ||
				!TryGetDbService(out IWorldPortalStateService service))
			{
				return;
			}

			var pending = new List<(string Scene, int Index, PortalWorldState State)>();
			portalStore.CollectDirtyWorld(pending);
			var rows = new List<WorldPortalStateData>(pending.Count);
			for (int i = 0; i < pending.Count; ++i)
			{
				(string scene, int index, PortalWorldState state) = pending[i];
				rows.Add(new WorldPortalStateData(scene, index, state.Permanent, FromUnixMs(state.ActiveUntilUnixMs)));
			}

			PortalActivationStore store = portalStore;
			EnqueuePersistence(async () =>
			{
				try
				{
					DatabaseResult result = await service.MergeAsync(rows);
					if (!result.IsSuccess)
					{
						await Log.Warning("InteractableSystem", $"World portal save failed ({rows.Count} opening(s)); held for the next flush: [{result.ErrorCode}] {result.ErrorMessage}");
						return;
					}
					TryEnqueueMainThread(() =>
					{
						for (int i = 0; i < pending.Count; ++i)
						{
							store.MarkWorldPersisted(pending[i].Scene, pending[i].Index, pending[i].State);
						}
					});
				}
				catch (Exception ex)
				{
					await Log.Error("InteractableSystem", $"World portal save threw: {ex}");
				}
			});
		}

		#endregion

		/// <summary>
		/// Tells a player every portal open for them in the scene they stand in, replacing what their
		/// client held for it. Skipped while a record that decides it is unread; the read sends it.
		/// </summary>
		private void SendPortalStates(IPlayerCharacter character)
		{
			if (portalStore == null || character?.Owner == null || character.GameObject == null)
			{
				return;
			}

			Scene scene = character.GameObject.scene;
			portalScratch.Clear();
			PortalRegistry.Collect(scene.handle, portalScratch);
			if (portalScratch.Count == 0)
			{
				return;
			}

			long now = portalStore.NowUnixMs;
			var indices = new List<ushort>(portalScratch.Count);
			var remaining = new List<int>(portalScratch.Count);
			for (int i = 0; i < portalScratch.Count; ++i)
			{
				PortalActivation portal = portalScratch[i];
				bool? characterActive = portalStore.IsCharacterActive(character.ID, scene.name, portal.PortalIndex);
				PortalWorldState? world = portalStore.WorldState(scene.name, portal.PortalIndex);
				bool? active = PortalActivationRules.IsActive(portal.Scope, characterActive, world, now);
				if (!active.HasValue)
				{
					return;
				}
				if (active.Value)
				{
					indices.Add((ushort)portal.PortalIndex);
					remaining.Add(PortalActivationRules.RemainingSeconds(portal.Scope, characterActive, world, now));
				}
			}
			portalScratch.Clear();

			Server.NetworkWrapper.Broadcast(character.Owner, new PortalStatesBroadcast()
			{
				SceneName = scene.name,
				Indices = indices.ToArray(),
				RemainingSeconds = remaining.ToArray(),
			}, true, Channel.Reliable);
		}

		private static long ToUnixMs(DateTime utc)
		{
			return new DateTimeOffset(DateTime.SpecifyKind(utc, DateTimeKind.Utc)).ToUnixTimeMilliseconds();
		}

		private static DateTime FromUnixMs(long unixMs)
		{
			return DateTimeOffset.FromUnixTimeMilliseconds(Math.Max(0L, unixMs)).UtcDateTime;
		}
	}
}
