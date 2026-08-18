using FishNet.Connection;
using FishNet.Object;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Auth.Core;
using FishMMO.Shared.Core;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Character saving and session management: periodic saves, character data serialization, async persistence, and session lifecycle (claim/release).
	/// </summary>
	public partial class CharacterSystem
	{
		/// <summary>
		/// Periodic callback for saving all characters to the database.
		/// </summary>
		/// <param name="deltaTime">Delta time parameter (unused).</param>
		private void OnPeriodicSave(float deltaTime)
		{
			if (!Initialized || Server == null)
			{
				return;
			}

			if (!Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> data))
			{
				return;
			}

			if (data.CharactersByID.Count == 0)
			{
				return;
			}

			Log.Debug("CharacterSystem", "Save" + "[" + DateTime.UtcNow + "]");

			if (!Server.DataContainerRegistry.TryGet<ICharacterSystemRuntimeData>(out var runtimeData))
			{
				return;
			}

			if (!runtimeData.TryBeginSave())
			{
				return;
			}

			// Snapshot character data on the main thread
			var characterDataList = new List<CharacterData>(data.CharactersByID.Count);
			var buffDataList = new List<CharacterBuffData>(data.CharactersByID.Count * 4);
			var attributeDataList = new List<CharacterAttributeData>(data.CharactersByID.Count * 16);
			var abilityDataList = new List<CharacterAbilityData>(data.CharactersByID.Count * 8);
			foreach (var character in data.CharactersByID.Values)
			{
				characterDataList.Add(BuildCharacterData(character));
				AppendBuffData(character, buffDataList);
				AppendAttributeData(character, attributeDataList);
				AppendAbilityData(character, abilityDataList);
			}

			if (!EnqueueAsyncWork(() => SaveAllCharactersAsync(characterDataList)))
			{
				runtimeData.EndSave();
				Log.Warning("CharacterSystem", "OnPeriodicSave: Failed to enqueue SaveAllCharactersAsync work item.");
			}

			if (buffDataList.Count > 0)
			{
				EnqueueAsyncWork(() => SaveBuffsAsync(buffDataList));
			}
			if (attributeDataList.Count > 0)
			{
				EnqueueAsyncWork(() => SaveAttributesAsync(attributeDataList));
			}
			if (abilityDataList.Count > 0)
			{
				EnqueueAsyncWork(() => SaveAbilitiesAsync(abilityDataList));
			}
		}

		/// <summary>
		/// Saves the character state, despawns the character from the scene, and fully releases the session.
		/// Always performs a full release (Online → Offline) because the destination
		/// Scene Server's TryClaimAsync requires session_state = Offline (or expired lease).
		/// </summary>
		/// <param name="conn">Network connection of the character.</param>
		/// <param name="character">Player character to save and despawn.</param>
		/// <param name="sessionInfo">Session ownership info to release after save, or null if no session is claimed.</param>
		private void SaveAndDespawnCharacter(NetworkConnection conn, IPlayerCharacter character, CharacterSessionInfo? sessionInfo = null)
		{
			// Combat is transient state — clear it before saving so it never persists to DB.
			// (BuildCharacterData also masks the flag for defense-in-depth.)
			character.DisableFlags(CharacterFlags.IsInCombat);

			// The body is going away, so it is no longer waiting to be reclaimed. Clearing this
			// centrally — rather than at each call site — is what guarantees the flag cannot
			// survive in the database: a stuck IsCombatLogged would make AnyOnlineAsync ignore
			// the character permanently and let the account hold two live sessions at once.
			character.DisableFlags(CharacterFlags.IsCombatLogged);

			// Remove loaded state so when a character is reloaded into a different scene/server, it will properly clamp attributes
			// and prevent actions until fully loaded in the new scene.
			character.DisableFlags(CharacterFlags.IsLoaded);

			// Snapshot character data on the main thread
			CharacterData charData = BuildCharacterData(character);
			var buffDataList = new List<CharacterBuffData>(8);
			var attributeDataList = new List<CharacterAttributeData>(16);
			var abilityDataList = new List<CharacterAbilityData>(8);
			AppendBuffData(character, buffDataList);
			AppendAttributeData(character, attributeDataList);
			AppendAbilityData(character, abilityDataList);

			// Save then fully release session (Online → Offline).
			//
			// The async worker pool is bounded and drops writes when full, so this enqueue can
			// fail. The session token has already been taken out of SessionTokens by the
			// caller, which means a dropped work item used to leave nothing anywhere that
			// could ever release the claim: the character stayed Online until its lease
			// expired, and every attempt to load it on the destination scene server was
			// kicked for the whole two minutes. Hand it to the retry queue instead.
			if (!EnqueueAsyncWork(() => SaveAndReleaseCharacterAsync(charData, sessionInfo)))
			{
				Log.Warning("CharacterSystem", $"SaveAndDespawnCharacter: Failed to enqueue save/release for character {charData.ID} — queued for retry.");
				QueuePendingFlush(charData.ID, charData, sessionInfo);
			}

			if (buffDataList.Count > 0)
			{
				EnqueueAsyncWork(() => SaveBuffsAsync(buffDataList));
			}
			if (attributeDataList.Count > 0)
			{
				EnqueueAsyncWork(() => SaveAttributesAsync(attributeDataList));
			}
			if (abilityDataList.Count > 0)
			{
				EnqueueAsyncWork(() => SaveAbilitiesAsync(abilityDataList));
			}

			// Immediately log out for now.. we could add a timeout later on..?
			if (character.NetworkObject.IsSpawned)
			{
				OnDespawnCharacter?.Invoke(conn, character);

				ServerManager.Despawn(character.NetworkObject, DespawnType.Pool);
			}
		}

		/// <summary>
		/// Builds a CharacterData DTO from an IPlayerCharacter, capturing all fields on the main thread.
		/// Increments character.Version for sequence-based optimistic concurrency.
		/// </summary>
		private CharacterData BuildCharacterData(IPlayerCharacter character)
		{
			character.Version++;
			Vector3 pos = character.Transform.position;
			Quaternion rot = character.Motor != null ? character.Motor.Transform.rotation : character.Transform.rotation;

			return new CharacterData(
				id: character.ID,
				name: character.CharacterName,
				nameLowercase: character.CharacterNameLower,
				account: character.Account,
				selected: true,
				worldServerID: character.WorldServerID,
				sceneName: character.SceneName,
				sceneHandle: character.SceneHandle,
				bindScene: character.BindScene,
				bindX: character.BindPosition.x,
				bindY: character.BindPosition.y,
				bindZ: character.BindPosition.z,
				instanceID: character.InstanceID,
				instanceX: character.InstancePosition.x,
				instanceY: character.InstancePosition.y,
				instanceZ: character.InstancePosition.z,
				instanceRotX: character.InstanceRotation.x,
				instanceRotY: character.InstanceRotation.y,
				instanceRotZ: character.InstanceRotation.z,
				instanceRotW: character.InstanceRotation.w,
				raceID: character.RaceID,
				modelIndex: character.ModelIndex,
				x: pos.x,
				y: pos.y,
				z: pos.z,
				rotX: rot.x,
				rotY: rot.y,
				rotZ: rot.z,
				rotW: rot.w,
				accessLevel: (byte)(int)character.AccessLevel,
				online: true,
				flags: character.Flags & ~(1 << (int)CharacterFlags.IsInCombat),
				version: character.Version,
				timeCreated: character.TimeCreated,
				lastSaved: DateTime.UtcNow
			);
		}

		/// <summary>
		/// Saves a single character asynchronously via the database service.
		/// </summary>
		/// <returns>
		/// <c>true</c> when the row is persisted, or when the write was rejected for a reason
		/// that retrying cannot change; <c>false</c> for transient failures worth retrying.
		/// </returns>
		private async Task<bool> SaveCharacterAsync(CharacterData charData)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var characterService))
				{
					return false;
				}

				DatabaseResult result = await characterService.PersistAsync(charData);
				if (result.IsSuccess)
				{
					return true;
				}

				await Log.Warning("CharacterSystem", $"SaveCharacterAsync DB error for character {charData.ID}: {result.ErrorCode} - {result.ErrorMessage}");

				// A stale or duplicate write means newer state is already persisted; replaying
				// this snapshot would keep losing to the same version guard.
				return result.ErrorCode == DatabaseErrorCodes.StaleState ||
					   result.ErrorCode == DatabaseErrorCodes.NotFound ||
					   result.ErrorCode == DatabaseErrorCodes.ValidationError;
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterSystem", $"SaveCharacterAsync failed for character {charData.ID}: {ex}");
				return false;
			}
		}

		/// <summary>
		/// Saves a character and then releases the session in a single sequential async task.
		/// This ensures the character data is persisted while we still hold the session lock,
		/// preventing another server from claiming the character before the save completes.
		/// </summary>
		private async Task SaveAndReleaseCharacterAsync(CharacterData charData, CharacterSessionInfo? sessionInfo)
		{
			// Save first — we must persist while still holding the session lock
			bool saved = await SaveCharacterAsync(charData);

			// Release regardless of whether the save landed. Holding the claim back because a
			// write failed would strand the character far more visibly than losing one save:
			// the destination scene server could not claim it and would kick the player.
			bool released = true;
			if (sessionInfo.HasValue)
			{
				released = await ReleaseCharacterSessionAsync(charData.ID, sessionInfo.Value.ServerID, sessionInfo.Value.Token);
			}

			if (!saved || !released)
			{
				// Retrying the save after the release is safe: the snapshot is the state we
				// held while we owned the character, and the version guard drops it if the
				// next owner has already written something newer.
				QueuePendingFlush(charData.ID, saved ? (CharacterData?)null : charData, released ? null : sessionInfo);
			}
		}

		/// <summary>
		/// Saves all characters asynchronously with a processing guard to prevent overlapping saves.
		/// </summary>
		/// <remarks>
		/// Session leases are deliberately not touched here. They are refreshed on their own
		/// timer by <see cref="OnPeriodicSessionLeaseRefresh"/> so that liveness of a claim
		/// never depends on how long this sequential save walk takes.
		/// </remarks>
		private async Task SaveAllCharactersAsync(List<CharacterData> characterDataList)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var characterService))
				{
					return;
				}

				foreach (CharacterData charData in characterDataList)
				{
					try
					{
						DatabaseResult result = await characterService.PersistAsync(charData);
						if (!result.IsSuccess)
						{
							await Log.Warning("CharacterSystem", $"SaveAllCharactersAsync DB error for character {charData.ID}: {result.ErrorCode} - {result.ErrorMessage}");
						}
					}
					catch (Exception ex)
					{
						await Log.Error("CharacterSystem", $"SaveAllCharactersAsync failed for character {charData.ID}: {ex}");
					}
				}
			}
			finally
			{
				if (Server?.DataContainerRegistry.TryGet<ICharacterSystemRuntimeData>(out var runtimeData) == true)
				{
					runtimeData.EndSave();
				}
			}
		}

		/// <summary>
		/// Removes a character's session token from the mapping data and enqueues an async release.
		/// Returns the extracted CharacterSessionInfo if found, or null if no session was stored.
		/// </summary>
		private CharacterSessionInfo? TryExtractAndReleaseSession(ICharacterMappingData<NetworkConnection> mappingData, long characterID)
		{
			if (mappingData.SessionTokens.TryGetValue(characterID, out CharacterSessionInfo sessionInfo))
			{
				mappingData.SessionTokens.Remove(characterID);
				ReleaseSessionSafely(characterID, sessionInfo.ServerID, sessionInfo.Token);
				return sessionInfo;
			}
			return null;
		}

		/// <summary>
		/// Releases a claimed session, guaranteeing the release is not lost if the async worker
		/// pool is saturated or the database call fails.
		/// </summary>
		/// <remarks>
		/// Every abandoned-load path has to go through here. A claim that is dropped instead of
		/// released leaves the character Online in the database with nothing left in this
		/// process holding its token, so the only thing that can free it is lease expiry —
		/// during which the player cannot be loaded anywhere.
		/// </remarks>
		private void ReleaseSessionSafely(long characterID, long serverID, Guid sessionToken)
		{
			var sessionInfo = new CharacterSessionInfo(sessionToken, serverID);
			if (!EnqueueAsyncWork(() => ReleaseSessionWithRetryAsync(characterID, sessionInfo)))
			{
				Log.Warning("CharacterSystem", $"Failed to enqueue session release for character {characterID} — queued for retry.");
				QueuePendingFlush(characterID, null, sessionInfo);
			}
		}

		/// <summary>
		/// Releases a session and queues a retry if the release did not stick.
		/// </summary>
		private async Task ReleaseSessionWithRetryAsync(long characterID, CharacterSessionInfo sessionInfo)
		{
			if (!await ReleaseCharacterSessionAsync(characterID, sessionInfo.ServerID, sessionInfo.Token))
			{
				QueuePendingFlush(characterID, null, sessionInfo);
			}
		}

		#region Pending Flush Retry

		/// <summary>
		/// A save and/or session release that could not be completed on its first attempt.
		/// </summary>
		private sealed class PendingCharacterFlush
		{
			/// <summary>Character snapshot still to persist, or null when only a release is outstanding.</summary>
			public CharacterData? CharacterData;
			/// <summary>Session ownership still to release, or null when only a save is outstanding.</summary>
			public CharacterSessionInfo? Session;
			/// <summary>Earliest UTC time the next attempt may run.</summary>
			public DateTime NextAttemptUtc;
			/// <summary>Attempts made so far, used for backoff and for giving up.</summary>
			public int Attempts;
			/// <summary>1 while an attempt is running; 0 when idle.</summary>
			public int InFlight;
		}

		/// <summary>
		/// Outstanding saves/releases awaiting retry, keyed by character ID.
		/// </summary>
		/// <remarks>
		/// Exists so that a full async-worker channel or a transient database error can never
		/// silently strand a claimed character session. A stranded claim is not a local
		/// problem: the character stays Online in the database, so the scene server the player
		/// is transferring to cannot claim it and kicks them, repeatedly, until the lease
		/// expires.
		/// </remarks>
		private readonly ConcurrentDictionary<long, PendingCharacterFlush> pendingFlushes =
			new ConcurrentDictionary<long, PendingCharacterFlush>();

		/// <summary>Interval in seconds between pending-flush retry passes.</summary>
		private const float PendingFlushRetryIntervalSeconds = 3f;

		/// <summary>
		/// Empties the pending-flush queue and returns its contents, for a final synchronous
		/// flush during shutdown.
		/// </summary>
		private List<KeyValuePair<long, PendingCharacterFlush>> DrainPendingFlushes()
		{
			var drained = new List<KeyValuePair<long, PendingCharacterFlush>>(pendingFlushes.Count);
			foreach (var kvp in pendingFlushes)
			{
				if (pendingFlushes.TryRemove(kvp.Key, out PendingCharacterFlush pending))
				{
					drained.Add(new KeyValuePair<long, PendingCharacterFlush>(kvp.Key, pending));
				}
			}
			return drained;
		}

		/// <summary>
		/// Maximum retry attempts before giving up. With the backoff below this spans well over
		/// the session lease duration, after which the claim expires on its own and any further
		/// release would match nothing anyway.
		/// </summary>
		private const int MaxPendingFlushAttempts = 12;

		/// <summary>
		/// Records a save and/or release for retry, merging with any entry already pending for
		/// the same character.
		/// </summary>
		/// <param name="characterID">Character the work belongs to.</param>
		/// <param name="charData">Character snapshot to persist, or null if only releasing.</param>
		/// <param name="sessionInfo">Session ownership to release, or null if only saving.</param>
		private void QueuePendingFlush(long characterID, CharacterData? charData, CharacterSessionInfo? sessionInfo)
		{
			if (characterID <= 0 || (charData == null && sessionInfo == null))
			{
				return;
			}

			pendingFlushes.AddOrUpdate(
				characterID,
				_ => new PendingCharacterFlush
				{
					CharacterData = charData,
					Session = sessionInfo,
					NextAttemptUtc = DateTime.UtcNow,
					Attempts = 0,
					InFlight = 0,
				},
				(_, existing) =>
				{
					// Keep the newest snapshot; keep whichever session token we have, since a
					// character only ever holds one claim at a time.
					if (charData.HasValue)
					{
						existing.CharacterData = charData;
					}
					if (sessionInfo.HasValue)
					{
						existing.Session = sessionInfo;
					}
					return existing;
				});
		}

		/// <summary>
		/// Periodic retry pass over <see cref="pendingFlushes"/>.
		/// </summary>
		private void OnPeriodicPendingFlushRetry(float deltaTime)
		{
			if (Server == null || Server.ServerState != ConnectionState.Started || pendingFlushes.IsEmpty)
			{
				return;
			}

			DateTime nowUtc = DateTime.UtcNow;

			foreach (var kvp in pendingFlushes)
			{
				long characterID = kvp.Key;
				PendingCharacterFlush pending = kvp.Value;

				if (nowUtc < pending.NextAttemptUtc)
				{
					continue;
				}

				if (Interlocked.CompareExchange(ref pending.InFlight, 1, 0) != 0)
				{
					continue;
				}

				if (!EnqueueAsyncWork(() => RunPendingFlushAsync(characterID, pending)))
				{
					// Still saturated; leave it queued and try again next pass.
					Interlocked.Exchange(ref pending.InFlight, 0);
				}
			}
		}

		/// <summary>
		/// Executes one retry of a pending save/release, rescheduling or dropping the entry
		/// according to the outcome.
		/// </summary>
		private async Task RunPendingFlushAsync(long characterID, PendingCharacterFlush pending)
		{
			bool completed = false;
			try
			{
				pending.Attempts++;

				if (pending.CharacterData.HasValue)
				{
					// A stale snapshot is rejected by the version guard rather than
					// overwriting newer state, so replaying it is safe.
					if (await SaveCharacterAsync(pending.CharacterData.Value))
					{
						pending.CharacterData = null;
					}
				}

				if (pending.Session.HasValue)
				{
					if (await ReleaseCharacterSessionAsync(characterID, pending.Session.Value.ServerID, pending.Session.Value.Token))
					{
						pending.Session = null;
					}
				}

				completed = pending.CharacterData == null && pending.Session == null;
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterSystem", $"RunPendingFlushAsync failed for character {characterID}: {ex}");
			}
			finally
			{
				if (completed)
				{
					pendingFlushes.TryRemove(characterID, out _);
				}
				else if (pending.Attempts >= MaxPendingFlushAttempts)
				{
					pendingFlushes.TryRemove(characterID, out _);
					_ = Log.Error("CharacterSystem",
						$"Giving up on pending flush for character {characterID} after {pending.Attempts} attempts. " +
						"Any unreleased session will free itself when its lease expires.");
				}
				else
				{
					// Linear backoff: fast enough that a normal scene transition is not held
					// up, slow enough that a database outage is not hammered.
					pending.NextAttemptUtc = DateTime.UtcNow + TimeSpan.FromSeconds(Math.Min(3 * pending.Attempts, 30));
				}
				Interlocked.Exchange(ref pending.InFlight, 0);
			}
		}

		#endregion

		#region Session Lease Refresh

		/// <summary>
		/// Extends the lease on every session this server currently holds, in one round trip.
		/// </summary>
		/// <remarks>
		/// Runs on its own timer rather than inside the save loop. The save loop walks
		/// characters sequentially with a database round trip each, so on a busy shard with a
		/// slow database the characters near the end of the list could go longer than the lease
		/// duration between refreshes and become claimable by another server while still
		/// online. Refresh cost here is independent of how many characters are resident.
		/// </remarks>
		private void OnPeriodicSessionLeaseRefresh(float deltaTime)
		{
			if (!Initialized || Server == null || Server.ServerState != ConnectionState.Started)
			{
				return;
			}

			if (!Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> data) ||
				data.SessionTokens.Count == 0)
			{
				return;
			}

			var leases = new List<CharacterSessionLeaseData>(data.SessionTokens.Count);
			foreach (var kvp in data.SessionTokens)
			{
				leases.Add(new CharacterSessionLeaseData(kvp.Key, kvp.Value.ServerID, kvp.Value.Token));
			}

			if (!EnqueueAsyncWork(() => RefreshSessionLeasesAsync(leases)))
			{
				Log.Warning("CharacterSystem", "OnPeriodicSessionLeaseRefresh: Failed to enqueue lease refresh work item.");
			}
		}

		/// <summary>
		/// Sends a batched session-lease refresh to the database.
		/// </summary>
		private async Task RefreshSessionLeasesAsync(List<CharacterSessionLeaseData> leases)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var characterService))
				{
					return;
				}

				DatabaseResult<int> result = await characterService.RefreshSessionLeasesAsync(leases);
				if (!result.IsSuccess)
				{
					await Log.Warning("CharacterSystem", $"RefreshSessionLeasesAsync DB error: {result.ErrorCode} - {result.ErrorMessage}");
					return;
				}

				if (result.Data < leases.Count)
				{
					// Every entry we send is a session this server believes it owns, so a short
					// count means at least one claim is gone — released underneath us, or taken
					// over after a lease lapse. Worth surfacing: it is the signature of the
					// split-brain window the lease exists to bound.
					await Log.Warning("CharacterSystem",
						$"Session lease refresh updated {result.Data} of {leases.Count} held sessions; " +
						"the remainder are no longer owned by this server.");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterSystem", $"RefreshSessionLeasesAsync failed: {ex}");
			}
		}

		#endregion

		/// <summary>
		/// Releases a character session from Online → Offline in a single step.
		/// </summary>
		/// <returns>
		/// <c>true</c> when the session is known to be released, or when retrying could not
		/// possibly help; <c>false</c> only for transient failures worth another attempt.
		/// </returns>
		private async Task<bool> ReleaseCharacterSessionAsync(long characterID, long serverID, Guid sessionToken)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var characterService))
				{
					await Log.Error("CharacterSystem", $"ReleaseCharacterSessionAsync: ICharacterService not available for character {characterID}.");
					return false;
				}

				if (sessionToken == Guid.Empty || serverID <= 0)
				{
					await Log.Error("CharacterSystem", $"ReleaseCharacterSessionAsync: Invalid session token or server ID for character {characterID}.");
					// Nothing to retry with — a malformed token can never match a row.
					return true;
				}

				DatabaseResult releaseResult = await characterService.ReleaseAsync(characterID, serverID, sessionToken);
				if (releaseResult.IsSuccess)
				{
					return true;
				}

				// The session is no longer ours: either it was already released, or the lease
				// expired and another server claimed it. Retrying would keep matching nothing,
				// and must not be mistaken for a stuck claim.
				if (releaseResult.ErrorCode == DatabaseErrorCodes.InvalidOperation ||
					releaseResult.ErrorCode == DatabaseErrorCodes.NotFound)
				{
					await Log.Warning("CharacterSystem", $"ReleaseCharacterSessionAsync: session for character {characterID} is no longer owned by this server ({releaseResult.ErrorCode}); nothing to release.");
					return true;
				}

				await Log.Error("CharacterSystem", $"ReleaseCharacterSessionAsync: ReleaseAsync failed for character {characterID}: {releaseResult.ErrorCode} - {releaseResult.ErrorMessage}");
				return false;
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterSystem", $"ReleaseCharacterSessionAsync failed for character {characterID}: {ex}");
				return false;
			}
		}

		#region Buff / Attribute / Ability Snapshot + Persistence

		/// <summary>
		/// Appends a snapshot of the character's active buffs (with deterministic tick→seconds conversion).
		/// Main-thread only. Each appended DTO has its Version bumped on the runtime Buff for optimistic concurrency.
		/// </summary>
		private void AppendBuffData(IPlayerCharacter character, List<CharacterBuffData> buffs)
		{
			if (!character.TryGet(out IBuffController buffController) || buffController.Buffs.Count == 0)
			{
				return;
			}

			var timeManager = Server?.NetworkWrapper?.NetworkManager?.TimeManager;
			if (timeManager == null)
			{
				return;
			}
			float tickDelta = (float)timeManager.TickDelta;
			uint currentTick = buffController.ResolveAuthoritativeTick(timeManager.LocalTick);

			foreach (var kvp in buffController.Buffs)
			{
				Buff buff = kvp.Value;
				if (buff == null || buff.Template == null)
				{
					continue;
				}

				// Convert absolute ticks → remaining seconds. Clamp negatives to 0 (about to expire).
				int remainingTicks = (int)(buff.ExpiryTick - currentTick);
				int nextTickTicks = (int)(buff.NextTickTick - currentTick);
				float remainingTime = remainingTicks > 0 ? remainingTicks * tickDelta : 0f;
				float tickTime = nextTickTicks > 0 ? nextTickTicks * tickDelta : 0f;

				buff.Version++;
				buffs.Add(new CharacterBuffData(
					id: 0,
					version: buff.Version,
					characterID: character.ID,
					templateID: buff.Template.ID,
					remainingTime: remainingTime,
					tickTime: tickTime,
					stacks: buff.Stacks,
					tickCount: buff.TickCount
				));
			}
		}

		/// <summary>
		/// Appends a snapshot of the character's attributes (base + resources) including external modifier.
		/// Main-thread only. Each appended DTO has its Version bumped on the runtime attribute.
		/// </summary>
		private void AppendAttributeData(IPlayerCharacter character, List<CharacterAttributeData> attributes)
		{
			if (!character.TryGet(out ICharacterAttributeController attrController))
			{
				return;
			}

			foreach (var kvp in attrController.Attributes)
			{
				var attr = kvp.Value;
				// Skip attributes with Version <= 0: these are template-initialized defaults
				// that were never loaded from DB. Saving them with a stale/invalid version
				// would poison the batch upsert with a STALE_STATE rejection.
				if (attr.Version <= 0)
					continue;
				attr.Version++;
				attributes.Add(new CharacterAttributeData(
					id: 0,
					version: attr.Version,
					characterID: character.ID,
					templateID: kvp.Key,
					value: attr.Value,
					currentValue: 0.0f
				));
			}
			foreach (var kvp in attrController.ResourceAttributes)
			{
				var resAttr = kvp.Value;
				if (resAttr.Version <= 0)
					continue;
				resAttr.Version++;
				attributes.Add(new CharacterAttributeData(
					id: 0,
					version: resAttr.Version,
					characterID: character.ID,
					templateID: kvp.Key,
					value: resAttr.Value,
					currentValue: resAttr.CurrentValue
				));
			}
		}

		/// <summary>
		/// Appends a snapshot of the character's crafted abilities (template + event IDs).
		/// Cooldowns are NOT persisted by design — they reset on relog (gameplay-friendly).
		/// Main-thread only. Each appended DTO has its Version bumped on the runtime Ability.
		/// </summary>
		private void AppendAbilityData(IPlayerCharacter character, List<CharacterAbilityData> abilities)
		{
			if (!character.TryGet(out IAbilityController abilityController) || abilityController.KnownAbilities.Count == 0)
			{
				return;
			}

			foreach (var kvp in abilityController.KnownAbilities)
			{
				Ability ability = kvp.Value;
				if (ability == null || ability.Template == null)
				{
					continue;
				}

				List<int> eventIds = ability.AbilityEvents.Keys.ToList();

				ability.Version++;
				abilities.Add(new CharacterAbilityData(
					id: ability.ID,
					version: ability.Version,
					characterID: character.ID,
					templateID: ability.Template.ID,
					abilityEvents: eventIds,
					cooldown: 0.0f
				));
			}
		}

		/// <summary>
		/// Persists a snapshot of buff state asynchronously.
		/// </summary>
		private async Task SaveBuffsAsync(List<CharacterBuffData> buffs)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterBuffService>(out var buffService))
				{
					return;
				}

				DatabaseResult result = await buffService.PersistAsync(buffs);
				if (!result.IsSuccess)
				{
					await Log.Warning("CharacterSystem", $"SaveBuffsAsync DB error: {result.ErrorCode} - {result.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterSystem", $"SaveBuffsAsync failed: {ex}");
			}
		}

		/// <summary>
		/// Persists a snapshot of attribute state asynchronously.
		/// </summary>
		private async Task SaveAttributesAsync(List<CharacterAttributeData> attributes)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterAttributeService>(out var attrService))
				{
					return;
				}

				DatabaseResult result = await attrService.PersistAsync(attributes);
				if (!result.IsSuccess)
				{
					await Log.Warning("CharacterSystem", $"SaveAttributesAsync DB error: {result.ErrorCode} - {result.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterSystem", $"SaveAttributesAsync failed: {ex}");
			}
		}

		/// <summary>
		/// Persists a snapshot of crafted ability state asynchronously.
		/// </summary>
		private async Task SaveAbilitiesAsync(List<CharacterAbilityData> abilities)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterAbilityService>(out var abilityService))
				{
					return;
				}

				DatabaseResult result = await abilityService.PersistAsync(abilities);
				if (!result.IsSuccess)
				{
					await Log.Warning("CharacterSystem", $"SaveAbilitiesAsync DB error: {result.ErrorCode} - {result.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterSystem", $"SaveAbilitiesAsync failed: {ex}");
			}
		}

		#endregion
	}
}