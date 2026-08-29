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

			// Lingering bodies count: they are not in CharactersByID but they are still
			// resident and still accumulating state worth writing. See
			// AppendLingeringCharacterSnapshots.
			if (data.CharactersByID.Count == 0 && LingeringCharacterCount == 0)
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

			// Snapshot character data on the main thread, pairing each with the claim this
			// server holds for it so the write can prove ownership.
			var characterDataList = new List<(CharacterData Data, CharacterSessionInfo? Ownership)>(data.CharactersByID.Count);
			var buffDataList = new List<CharacterBuffData>(data.CharactersByID.Count * 4);
			var attributeDataList = new List<CharacterAttributeData>(data.CharactersByID.Count * 16);
			var abilityDataList = new List<CharacterAbilityData>(data.CharactersByID.Count * 8);
			var petDataList = new List<PetSnapshot>();
			foreach (var character in data.CharactersByID.Values)
			{
				CharacterSessionInfo? ownership = data.SessionTokens.TryGetValue(character.ID, out CharacterSessionInfo held)
					? held
					: (CharacterSessionInfo?)null;
				characterDataList.Add((BuildCharacterData(character), ownership));
				AppendBuffData(character, buffDataList);
				AppendAttributeData(character, attributeDataList);
				AppendAbilityData(character, abilityDataList);
				AppendPetData(character, petDataList);
			}

			// Combat-logout bodies have no connection and so are absent from the map above.
			AppendLingeringCharacterSnapshots(data, characterDataList, buffDataList, attributeDataList, abilityDataList, petDataList);

			if (characterDataList.Count == 0)
			{
				runtimeData.EndSave();
				return;
			}

			if (!EnqueueAsyncWork(() => SaveAllCharactersAsync(characterDataList)))
			{
				runtimeData.EndSave();
				Log.Warning("CharacterSystem", "OnPeriodicSave: Failed to enqueue SaveAllCharactersAsync work item.");
			}

			if (buffDataList.Count > 0)
			{
				EnqueuePersistence(() => SaveBuffsAsync(buffDataList));
			}
			if (attributeDataList.Count > 0)
			{
				EnqueuePersistence(() => SaveAttributesAsync(attributeDataList));
			}
			if (abilityDataList.Count > 0)
			{
				EnqueuePersistence(() => SaveAbilitiesAsync(abilityDataList));
			}
			if (petDataList.Count > 0)
			{
				EnqueuePersistence(() => SavePetsAsync(petDataList));
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
			var petDataList = new List<PetSnapshot>(1);
			AppendBuffData(character, buffDataList);
			AppendAttributeData(character, attributeDataList);
			AppendAbilityData(character, abilityDataList);
			AppendPetData(character, petDataList);

			/* Save everything, THEN release the session (Online → Offline).
			 *
			 * All of it in one work item, on purpose. The sub-entity writes used to be enqueued
			 * separately, which put them on a different worker lane than the save-and-release —
			 * so the claim could be handed back while a character's buffs, attributes, abilities
			 * or pet were still unwritten. The destination scene server claims the moment the
			 * release lands and reads the row immediately, and whatever had not been flushed yet
			 * simply was not there: a player zoning could arrive missing the pet they had out,
			 * and the write that finally landed a moment later described a character that had
			 * already moved on.
			 *
			 * The async worker pool is bounded and drops writes when full, so this enqueue can
			 * fail. The session token has already been taken out of SessionTokens by the
			 * caller, which means a dropped work item used to leave nothing anywhere that
			 * could ever release the claim: the character stayed Online until its lease
			 * expired, and every attempt to load it on the destination scene server was
			 * kicked for the whole two minutes. Hand it to the retry queue instead. */
			if (!EnqueueAsyncWork(() => SaveAndReleaseCharacterAsync(
					charData, buffDataList, attributeDataList, abilityDataList, petDataList, sessionInfo)))
			{
				Log.Warning("CharacterSystem", $"SaveAndDespawnCharacter: Failed to enqueue save/release for character {charData.ID} — queued for retry.");
				QueuePendingFlush(charData.ID, charData, sessionInfo);
			}

			// Immediately log out for now.. we could add a timeout later on..?
			if (character.NetworkObject.IsSpawned)
			{
				DispatchCharacterEvent(OnDespawnCharacter, conn, character, nameof(OnDespawnCharacter));

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

			/* The transform describes wherever the character currently is, and that is not always
			 * the open world.
			 *
			 * Inside an instance the live transform belongs in the instance columns, and the
			 * open-world columns must keep the position the character will be put back at when
			 * it leaves. Writing the transform to both — which is what happened before — meant
			 * one save inside a dungeon replaced the character's world position with dungeon
			 * coordinates, so anything that returned them to the open world without an explicit
			 * destination (an instance reaped as stale, a cleared instance flag) placed them at
			 * a point that has no relationship to the world scene at all.
			 *
			 * Writing the live transform into the instance columns is also what makes progress
			 * inside a dungeon survive a relog: the entry point was previously re-saved on every
			 * pass, so reconnecting always returned the player to the dungeon's front door. */
			bool inInstance = character.IsInInstance();

			Vector3 worldPos = inInstance ? character.LastWorldPosition : pos;
			Quaternion worldRot = inInstance ? character.LastWorldRotation : rot;
			Vector3 instancePos = inInstance ? pos : character.InstancePosition;
			Quaternion instanceRot = inInstance ? rot : character.InstanceRotation;

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
				instanceX: instancePos.x,
				instanceY: instancePos.y,
				instanceZ: instancePos.z,
				instanceRotX: instanceRot.x,
				instanceRotY: instanceRot.y,
				instanceRotZ: instanceRot.z,
				instanceRotW: instanceRot.w,
				raceID: character.RaceID,
				modelIndex: character.ModelIndex,
				x: worldPos.x,
				y: worldPos.y,
				z: worldPos.z,
				rotX: worldRot.x,
				rotY: worldRot.y,
				rotZ: worldRot.z,
				rotW: worldRot.w,
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
		private async Task<bool> SaveCharacterAsync(CharacterData charData, CharacterSessionInfo? ownership = null)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var characterService))
				{
					return false;
				}

				/* Write through the ownership-gated path whenever this server holds a claim.
				 *
				 * The plain PersistAsync is guarded only by the monotonic Version, which does
				 * not identify the writer. A server whose lease lapsed while it was still
				 * running therefore kept saving characters another server had legitimately
				 * claimed — and won, because its version counter had been climbing for the
				 * whole session while the new owner restarted from the persisted row. The
				 * claim was advisory on the write path; this makes it binding. */
				DatabaseResult result = ownership.HasValue
					? await characterService.PersistOwnedAsync(
						charData,
						new CharacterSessionLeaseData(charData.ID, ownership.Value.ServerID, ownership.Value.Token))
					: await characterService.PersistAsync(charData);

				if (result.IsSuccess)
				{
					return true;
				}

				if (result.ErrorCode == DatabaseErrorCodes.Forbidden)
				{
					/* The claim is gone: another server owns this character now and has been
					 * authoritative since it loaded. Everything accumulated here since the last
					 * successful save is unpersistable — writing it would overwrite the live
					 * session's progress, which is a worse loss than dropping ours. Say exactly
					 * what was dropped, then evict so we stop simulating a character we cannot
					 * save. Retrying is pointless: the claim never comes back. */
					await Log.Error("CharacterSystem",
						$"Character {charData.ID} ('{charData.Name}') is no longer claimed by this server; " +
						$"discarding the unsaved snapshot at version {charData.Version} and evicting it. " +
						"This means the session lease lapsed while the character was still resident — " +
						"check for database outages or async-worker saturation lasting over the lease duration.");
					RequestEviction(charData.ID, "session claim lost");
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
		private async Task SaveAndReleaseCharacterAsync(
			CharacterData charData,
			List<CharacterBuffData> buffs,
			List<CharacterAttributeData> attributes,
			List<CharacterAbilityData> abilities,
			List<PetSnapshot> pets,
			CharacterSessionInfo? sessionInfo)
		{
			// Save first — we must persist while still holding the session lock, and prove that
			// ownership in the same statement so a lapsed lease cannot overwrite the new owner.
			bool saved = await SaveCharacterAsync(charData, sessionInfo);

			/* Sub-entities before the release, not alongside it. Everything the next owner is
			 * about to read has to be in the database before the claim it reads under is
			 * available. Awaited in sequence rather than in parallel so a saturated pool cannot
			 * reorder them behind the release. */
			if (attributes != null && attributes.Count > 0)
			{
				await SaveAttributesAsync(attributes);
			}
			if (buffs != null && buffs.Count > 0)
			{
				await SaveBuffsAsync(buffs);
			}
			if (abilities != null && abilities.Count > 0)
			{
				await SaveAbilitiesAsync(abilities);
			}
			if (pets != null && pets.Count > 0)
			{
				await SavePetsAsync(pets);
			}

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
		private async Task SaveAllCharactersAsync(List<(CharacterData Data, CharacterSessionInfo? Ownership)> characterDataList)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ICharacterService>(out _))
				{
					return;
				}

				foreach (var entry in characterDataList)
				{
					try
					{
						/* Routed through SaveCharacterAsync rather than calling the service
						 * directly, so the periodic save gets the same ownership gate and the
						 * same lost-claim eviction as every other write path. This loop was the
						 * main corruption vector: a server whose lease had lapsed kept rewriting
						 * characters another server owned, every save interval, indefinitely. */
						await SaveCharacterAsync(entry.Data, entry.Ownership);
					}
					catch (Exception ex)
					{
						await Log.Error("CharacterSystem", $"SaveAllCharactersAsync failed for character {entry.Data.ID}: {ex}");
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
					/* Gated on the claim when we still hold one. When we do not (the release
					 * already landed and only the save is outstanding) the version guard is the
					 * only protection available, and dropping the write entirely would lose the
					 * state outright — so the ungated path stays for that case, as before. */
					if (await SaveCharacterAsync(pending.CharacterData.Value, pending.Session))
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

		#region Lost-Claim Eviction

		/// <summary>
		/// Character IDs already scheduled for eviction, so a claim lost during a batch does
		/// not queue one eviction per failing save.
		/// </summary>
		private readonly ConcurrentDictionary<long, byte> evictionRequested = new ConcurrentDictionary<long, byte>();

		/// <summary>
		/// Schedules <paramref name="characterID"/> to be removed from this server because its
		/// session claim is no longer held here.
		/// </summary>
		/// <remarks>
		/// Safe to call from an async worker; the removal itself is marshalled to the main
		/// thread, which is the only place FishNet and the mapping dictionaries may be touched.
		/// </remarks>
		private void RequestEviction(long characterID, string reason)
		{
			if (characterID <= 0 || !evictionRequested.TryAdd(characterID, 0))
			{
				return;
			}

			if (!TryEnqueueMainThread(() => EvictLostCharacter(characterID, reason)))
			{
				// Nothing will run the eviction, so allow a later save failure to try again.
				evictionRequested.TryRemove(characterID, out _);
				Log.Error("CharacterSystem",
					$"Could not schedule eviction of character {characterID} ({reason}); it will keep failing to save until the next attempt succeeds.");
			}
		}

		/// <summary>
		/// Removes a character this server no longer owns, without saving or releasing it.
		/// </summary>
		/// <remarks>
		/// Deliberately does not save: another server has held the claim since it loaded, and
		/// the state here has been diverging ever since. Writing it would overwrite the live
		/// session's progress — the opposite of preserving information. It also does not
		/// release: the row's owner token is somebody else's, so a release would either match
		/// nothing or, worse, would be wrong to perform.
		/// <para>
		/// The connection is disconnected rather than left in place. The client's reconnect
		/// loop takes it back through the world server, which routes it to whichever scene
		/// server actually owns the character, and it reloads the authoritative row — the same
		/// recovery path a dropped scene server already produces.
		/// </para>
		/// </remarks>
		private void EvictLostCharacter(long characterID, string reason)
		{
			evictionRequested.TryRemove(characterID, out _);

			// The snapshot queued for retry belongs to a session we no longer own.
			pendingFlushes.TryRemove(characterID, out _);

			if (!Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> data))
			{
				return;
			}

			// Drop the claim bookkeeping without releasing — see the remarks above.
			data.SessionTokens.Remove(characterID);

			/* A lingering body has no connection and is not in CharactersByID, so it is removed
			 * through its own path: balance the scene count the linger took and despawn. That
			 * path is terminal for this character, so return once it runs. */
			if (lingeringCharacters.ContainsKey(characterID))
			{
				Log.Error("CharacterSystem",
					$"Evicting combat-logout body for character {characterID}: {reason}. " +
					"Its unsaved damage is discarded; the owning server's state stands.");
				DropLingeringCharacter(characterID);
				return;
			}

			if (!data.CharactersByID.TryGetValue(characterID, out IPlayerCharacter character))
			{
				// Not resident (already gone, or still in the waiting-scene-load stage keyed by
				// connection). The waiting case is handled by its own handshake timeout.
				Log.Warning("CharacterSystem", $"Evicting character {characterID} ({reason}): not resident; claim bookkeeping dropped.");
				return;
			}

			Log.Error("CharacterSystem",
				$"Evicting {character.CharacterName} ({characterID}) from this scene server: {reason}. " +
				"Its client will reconnect and reload the state held by the server that owns it now.");

			NetworkConnection owner = character.Owner;

			data.CharactersByID.Remove(characterID);
			data.CharactersByLowerCaseName.Remove(character.CharacterNameLower);
			if (data.CharactersByWorld.TryGetValue(character.WorldServerID, out Dictionary<long, IPlayerCharacter> worldChars))
			{
				worldChars.Remove(characterID);
			}
			if (owner != null)
			{
				data.ConnectionCharacters.Remove(owner);
			}

			// Keeps scene population and every social system in step, exactly as a normal
			// disconnect would.
			DispatchCharacterEvent(OnDisconnect, owner, character, nameof(OnDisconnect));

			if (character.NetworkObject != null)
			{
				DispatchCharacterEvent(OnDespawnCharacter, owner, character, nameof(OnDespawnCharacter));

				if (character.NetworkObject.IsSpawned)
				{
					ServerManager.Despawn(character.NetworkObject, DespawnType.Pool);
				}
				else
				{
					Server.NetworkWrapper.NetworkManager.StorePooledInstantiated(character.NetworkObject, true);
				}
			}

			if (owner != null && owner.IsActive)
			{
				// Non-terminal: the world server will route the reconnect to whichever scene
				// server holds the claim now, which is exactly the recovery this eviction wants.
				DisconnectWithNotice(owner, DisconnectNoticeReason.SessionSuperseded);
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
					/* Every entry we send is a session this server believes it owns, so a short
					 * count means at least one claim is gone — released underneath us, or taken
					 * over by another server after our lease lapsed.
					 *
					 * The count alone does not say which, and until it did, nothing could act on
					 * it: this server kept simulating and saving characters it no longer owned,
					 * overwriting the live owner's state on every periodic save. Resolve the
					 * specific characters and evict them. The ownership-gated save in
					 * SaveCharacterAsync is the hard guarantee; this is what makes the recovery
					 * prompt (one refresh interval) instead of waiting for a save to be refused. */
					await Log.Warning("CharacterSystem",
						$"Session lease refresh updated {result.Data} of {leases.Count} held sessions; " +
						"identifying the claims this server has lost.");

					DatabaseResult<IReadOnlyList<long>> lostResult = await characterService.FetchUnownedSessionsAsync(leases);
					if (!lostResult.IsSuccess || lostResult.Data == null)
					{
						await Log.Error("CharacterSystem",
							$"Could not identify lost session claims: {lostResult.ErrorCode} - {lostResult.ErrorMessage}. " +
							"Affected characters will be evicted when their next save is refused.");
						return;
					}

					foreach (long characterID in lostResult.Data)
					{
						RequestEviction(characterID, "session lease lapsed and the claim was taken by another server");
					}
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
				/* Unchanged since the database last confirmed it, so there is nothing to write.
				 * The row is not merely rejected further down — it is never built, never sent, and
				 * never probed. */
				if (!attr.PersistenceDirty)
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
				if (!resAttr.PersistenceDirty)
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
		/// One character's complete pet state, captured together.
		/// </summary>
		/// <remarks>
		/// The three pet tables are snapshotted and written as a unit because they share a
		/// version stream — see <see cref="AppendPetData"/>. Splitting them into three flat
		/// lists, the way the character's own sub-entities are handled, would lose the grouping
		/// that makes a restored pet's health provably belong to the pet row it came with.
		/// </remarks>
		private readonly struct PetSnapshot
		{
			/// <summary>The pet row: which template, which abilities, and whether it was out.</summary>
			public readonly CharacterPetData Pet;

			/// <summary>The pet's attribute values at the moment of the snapshot.</summary>
			public readonly List<CharacterPetAttributeData> Attributes;

			/// <summary>The pet's active buffs at the moment of the snapshot.</summary>
			public readonly List<CharacterPetBuffData> Buffs;

			public PetSnapshot(CharacterPetData pet, List<CharacterPetAttributeData> attributes, List<CharacterPetBuffData> buffs)
			{
				Pet = pet;
				Attributes = attributes;
				Buffs = buffs;
			}
		}

		/// <summary>
		/// Appends a snapshot of the character's live pet — the pet row, its attributes and its
		/// buffs — so a pet survives a logout or a scene-server handover with the health and
		/// effects it actually had.
		/// </summary>
		/// <remarks>
		/// <para>
		/// Main-thread only, and it must run <em>after</em> <see cref="BuildCharacterData"/> for
		/// the same character: all three pet tables are stamped with the character's own version
		/// counter, which that method increments.
		/// </para>
		/// <para>
		/// Sharing the character's counter is what makes the pet's version stream work without a
		/// read. A pet is pooled and re-summoned freely, so it has no durable identity of its own
		/// to hang a counter on — a freshly summoned pet starting from version 1 would be
		/// rejected as stale against rows left by the pet before it. The character's counter is
		/// monotonic, survives restarts because it is loaded from the character row, and is
		/// already incremented exactly once per snapshot.
		/// </para>
		/// <para>
		/// It also gives the restore path a correctness check it could not otherwise have: rows
		/// written in one pass all carry the same version, so
		/// <c>PetSystem.LoadAndSpawnPetAsync</c> can require an attribute or buff row to match
		/// the pet row's version and thereby ignore leftovers from a previous pet that happened
		/// to know an attribute this one does not.
		/// </para>
		/// </remarks>
		/// <param name="character">The character whose pet to snapshot.</param>
		/// <param name="pets">Destination for the snapshot.</param>
		private void AppendPetData(IPlayerCharacter character, List<PetSnapshot> pets)
		{
			if (!character.TryGet(out IPetController petController))
			{
				return;
			}

			Pet pet = petController.Pet;
			if (pet == null)
			{
				/* No pet out, nothing to say. Whatever the pet row holds is already correct —
				 * it was written to spawned = false by whichever path dismissed or killed the
				 * pet — and writing a row here would only be able to say "no template", which
				 * is not a state the schema can express. */
				return;
			}

			int templateID = pet.PetAbilityTemplate != null ? pet.PetAbilityTemplate.ID : 0;
			if (templateID <= 0)
			{
				Log.Warning("CharacterSystem", $"Character {character.ID} has a pet with no resolvable ability template; its state was not persisted.");
				return;
			}

			// BuildCharacterData has already incremented this for the current pass.
			long version = Math.Max(1L, character.Version);

			// Abilities granted at summon time live only on the controller until this runs.
			pet.CaptureKnownAbilities();

			float currentHealth = 0.0f;
			if (pet.TryGet(out ICharacterAttributeController petAttributeController) &&
				petAttributeController.TryGetHealthAttribute(out CharacterResourceAttribute health))
			{
				currentHealth = health.CurrentValue;
			}

			CharacterPetData petData = new CharacterPetData(
				id: 0,
				version: version,
				characterID: character.ID,
				templateID: templateID,
				abilities: pet.PetAbilityIDs != null ? new List<int>(pet.PetAbilityIDs) : new List<int>(),
				spawned: currentHealth > 0.0f);

			var attributeData = new List<CharacterPetAttributeData>(16);
			AppendPetAttributeData(character.ID, version, petAttributeController, attributeData);

			var buffData = new List<CharacterPetBuffData>(4);
			AppendPetBuffData(character.ID, version, pet, buffData);

			pets.Add(new PetSnapshot(petData, attributeData, buffData));
		}

		/// <summary>
		/// Appends the pet's attribute values.
		/// </summary>
		/// <remarks>
		/// Unlike the owner's own attributes, nothing is skipped for a zero version: a pet's
		/// attributes are built from its prefab on every spawn and never carry a version loaded
		/// from the database, so the owner's "skip template defaults" rule would skip every row
		/// a pet has. The stamped version comes from the character instead.
		/// </remarks>
		/// <param name="characterID">The owning character.</param>
		/// <param name="version">Version to stamp every row with.</param>
		/// <param name="attributeController">The pet's attribute controller, or null.</param>
		/// <param name="attributes">Destination for the rows.</param>
		private static void AppendPetAttributeData(
			long characterID,
			long version,
			ICharacterAttributeController attributeController,
			List<CharacterPetAttributeData> attributes)
		{
			if (attributeController == null)
			{
				return;
			}

			foreach (var kvp in attributeController.Attributes)
			{
				attributes.Add(new CharacterPetAttributeData(
					id: 0,
					version: version,
					characterID: characterID,
					templateID: kvp.Key,
					value: kvp.Value.Value,
					currentValue: 0.0f));
			}

			foreach (var kvp in attributeController.ResourceAttributes)
			{
				attributes.Add(new CharacterPetAttributeData(
					id: 0,
					version: version,
					characterID: characterID,
					templateID: kvp.Key,
					value: kvp.Value.Value,
					currentValue: kvp.Value.CurrentValue));
			}
		}

		/// <summary>
		/// Appends the pet's active buffs, converting absolute ticks into remaining seconds the
		/// same way the owner's buffs are converted.
		/// </summary>
		/// <param name="characterID">The owning character.</param>
		/// <param name="version">Version to stamp every row with.</param>
		/// <param name="pet">The pet.</param>
		/// <param name="buffs">Destination for the rows.</param>
		private void AppendPetBuffData(long characterID, long version, Pet pet, List<CharacterPetBuffData> buffs)
		{
			if (!pet.TryGet(out IBuffController buffController) || buffController.Buffs.Count == 0)
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

				// Absolute ticks → remaining seconds. Clamp negatives to 0 (about to expire).
				int remainingTicks = (int)(buff.ExpiryTick - currentTick);
				int nextTickTicks = (int)(buff.NextTickTick - currentTick);

				buffs.Add(new CharacterPetBuffData(
					id: 0,
					version: version,
					characterID: characterID,
					templateID: buff.Template.ID,
					remainingTime: remainingTicks > 0 ? remainingTicks * tickDelta : 0f,
					tickTime: nextTickTicks > 0 ? nextTickTicks * tickDelta : 0f,
					stacks: buff.Stacks,
					tickCount: buff.TickCount));
			}
		}

		/// <summary>
		/// Persists pet state — row, attributes and buffs — for every snapshotted character.
		/// </summary>
		/// <remarks>
		/// The pet row goes first. If the two dependent writes fail after it, the restore path's
		/// version check simply rejects the older attribute and buff rows and the pet comes back
		/// at its template defaults — which is a far better outcome than the reverse ordering,
		/// where a pet row that failed to write would leave the next login restoring a previous
		/// pet's health onto the wrong creature.
		/// </remarks>
		/// <param name="pets">Snapshots to write.</param>
		private async Task SavePetsAsync(List<PetSnapshot> pets)
		{
			try
			{
				if (Server?.Database?.ServiceRegistry == null || pets == null || pets.Count == 0)
				{
					return;
				}

				/* Each of the three writes is reported and none aborts the others. A superseded
				 * pet row means a newer write for that character already landed — a dismissal, or
				 * a logout that overtook the periodic pass — and the attribute and buff rows are
				 * then simply ignored on restore by the version match they no longer satisfy.
				 * Abandoning the remaining writes would leave those tables one pass further out
				 * of date for no benefit. */
				if (Server.Database.ServiceRegistry.TryGet<ICharacterPetService>(out var petService))
				{
					var petRows = pets.Select(p => p.Pet).ToList();
					await BulkWriteReporting.ReportAsync("CharacterSystem", "Pet save", await petService.PersistAsync(petRows));
				}

				if (Server.Database.ServiceRegistry.TryGet<ICharacterPetAttributeService>(out var petAttributeService))
				{
					var attributeRows = pets.SelectMany(p => p.Attributes).ToList();
					if (attributeRows.Count > 0)
					{
						await BulkWriteReporting.ReportAsync("CharacterSystem", "Pet attribute save", await petAttributeService.PersistAsync(attributeRows));
					}
				}

				if (Server.Database.ServiceRegistry.TryGet<ICharacterPetBuffService>(out var petBuffService))
				{
					var buffRows = pets.SelectMany(p => p.Buffs).ToList();
					if (buffRows.Count > 0)
					{
						await BulkWriteReporting.ReportAsync("CharacterSystem", "Pet buff save", await petBuffService.PersistAsync(buffRows));
					}
				}
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterSystem", $"SavePetsAsync failed: {ex}");
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

				await BulkWriteReporting.ReportAsync("CharacterSystem", "Buff save", await buffService.PersistAsync(buffs));
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

				bool written = await BulkWriteReporting.ReportAsync("CharacterSystem", "Attribute save", await attrService.PersistAsync(attributes));

				/* Only a write that landed clears the dirty marks, and it clears them back on the
				 * main thread because that is the only thread the attributes are touched from.
				 * A failed write leaves everything marked, so the next pass carries it — which is
				 * the behaviour the unconditional save had by accident and this has to keep on
				 * purpose, since the periodic path has no other retry. */
				if (written)
				{
					TryEnqueueMainThread(() => MarkAttributesPersisted(attributes));
				}
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterSystem", $"SaveAttributesAsync failed: {ex}");
			}
		}

		/// <summary>
		/// Clears the dirty mark on every attribute a completed write covered.
		/// </summary>
		/// <remarks>
		/// Runs on the main thread. Each attribute is cleared only if its version still matches the
		/// one written: an attribute that moved while the save was in flight has advanced past it
		/// and stays dirty, so the change that landed mid-write is not mistaken for one already
		/// stored. A character that has since logged out is simply gone from the map and skipped.
		/// </remarks>
		/// <param name="attributes">The snapshot that was written.</param>
		private void MarkAttributesPersisted(List<CharacterAttributeData> attributes)
		{
			if (attributes == null ||
				Server?.DataContainerRegistry == null ||
				!Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> data))
			{
				return;
			}

			for (int i = 0; i < attributes.Count; ++i)
			{
				CharacterAttributeData saved = attributes[i];

				if (!data.CharactersByID.TryGetValue(saved.CharacterID, out IPlayerCharacter character) ||
					character == null ||
					!character.TryGet(out ICharacterAttributeController attrController))
				{
					continue;
				}

				if (attrController.Attributes.TryGetValue(saved.TemplateID, out CharacterAttribute attribute))
				{
					attribute.MarkPersisted(saved.Version);
				}
				else if (attrController.ResourceAttributes.TryGetValue(saved.TemplateID, out CharacterResourceAttribute resourceAttribute))
				{
					resourceAttribute.MarkPersisted(saved.Version);
				}
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

				await BulkWriteReporting.ReportAsync("CharacterSystem", "Ability save", await abilityService.PersistAsync(abilities));
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterSystem", $"SaveAbilitiesAsync failed: {ex}");
			}
		}

		#endregion
	}
}