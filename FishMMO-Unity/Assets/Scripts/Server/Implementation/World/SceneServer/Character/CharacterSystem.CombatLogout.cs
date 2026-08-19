using FishNet.Connection;
using FishNet.Object;
using System;
using System.Collections.Generic;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishMMO.Logging;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Combat-logout protection: a character that leaves the world while in combat keeps its
	/// body in the scene, still targetable and still killable, until its combat timer runs out.
	/// </summary>
	/// <remarks>
	/// Without this, closing the client is a guaranteed escape from any losing fight: the
	/// disconnect path saved, despawned and released the character within milliseconds, so
	/// Alt+F4 was strictly better than fleeing. Keeping the body present converts that into the
	/// opposite trade — the player gives up all control while remaining a valid target.
	/// <para>
	/// The claim is deliberately held for the whole linger. It is what keeps this server
	/// authoritative over the body; releasing it would let another scene server load a second
	/// copy of the same character from a snapshot that predates whatever is still happening to
	/// this one.
	/// </para>
	/// </remarks>
	public partial class CharacterSystem
	{
		/// <summary>
		/// A character whose owner has gone but whose body is still in the world.
		/// </summary>
		private sealed class LingeringCharacter
		{
			/// <summary>The still-spawned character.</summary>
			public IPlayerCharacter Character;
			/// <summary>Account that owns it, used to match a reconnecting player.</summary>
			public string Account;
			/// <summary>Hard deadline after which the body is removed regardless of combat state.</summary>
			public DateTime ExpiresUtc;
		}

		/// <summary>
		/// Bodies currently lingering, keyed by character ID. Main-thread only.
		/// </summary>
		private readonly Dictionary<long, LingeringCharacter> lingeringCharacters =
			new Dictionary<long, LingeringCharacter>();

		/// <summary>
		/// Account name (lower-case) to lingering character ID, so a reconnecting player can be
		/// matched before their character has been identified.
		/// </summary>
		private readonly Dictionary<string, long> lingeringCharactersByAccount =
			new Dictionary<string, long>(StringComparer.OrdinalIgnoreCase);

		/// <summary>Interval in seconds between combat-linger sweeps.</summary>
		private const float CombatLingerSweepIntervalSeconds = 1f;

		/// <inheritdoc/>
		public int LingeringCharacterCount => lingeringCharacters.Count;

		/// <summary>
		/// Attempts to keep <paramref name="character"/> in the world instead of despawning it,
		/// because its owner disconnected mid-combat.
		/// </summary>
		/// <remarks>
		/// Ownership is dropped here rather than left dangling. FishNet despawns everything in
		/// <c>connection.Objects</c> from <c>ServerObjects.ClientDisconnected</c>, and the only
		/// built-in opt-out is a serialized per-prefab flag this code cannot set. Because
		/// <c>OnRemoteConnectionState(Stopped)</c> is raised *before* that cleanup runs,
		/// removing ownership now takes the body out of the collection in time and FishNet
		/// leaves it alone.
		/// </remarks>
		/// <param name="character">Character whose owner just disconnected.</param>
		/// <returns><c>true</c> when the body was retained; <c>false</c> to fall through to the normal save-and-despawn.</returns>
		private bool TryBeginCombatLinger(IPlayerCharacter character)
		{
			if (!enableCombatLogoutLinger || combatLogoutLingerSeconds <= 0f)
			{
				return false;
			}

			if (character == null || character.NetworkObject == null || !character.NetworkObject.IsSpawned)
			{
				return false;
			}

			// A dead character has nothing left to escape, and an already-lingering one must not
			// be registered twice.
			if (character.IsFlagged(CharacterFlags.IsDead) || lingeringCharacters.ContainsKey(character.ID))
			{
				return false;
			}

			if (!character.IsFlagged(CharacterFlags.IsInCombat))
			{
				return false;
			}

			if (string.IsNullOrEmpty(character.Account))
			{
				Log.Warning("CharacterSystem", $"Cannot linger character {character.ID}: no account recorded.");
				return false;
			}

			try
			{
				character.NetworkObject.RemoveOwnership();
			}
			catch (Exception ex)
			{
				// Without this the body would be despawned by FishNet moments from now, so a
				// half-applied linger is worse than none: fall back to the normal path.
				Log.Error("CharacterSystem", $"Failed to remove ownership for lingering character {character.ID}: {ex}");
				return false;
			}

			/* Stop whatever the character was in the middle of doing.
			 *
			 * A despawn would have done this for free — FishNet's ResetState cancels the
			 * activation — but the whole point of a linger is that the body is NOT despawned,
			 * so an in-flight cast simply carries on. The server keeps ticking
			 * AbilityController.OnReplicate for an unowned object, and a tick with no input
			 * re-asserts IsHeld from the replicated flags rather than clearing it, so a
			 * channelled or charged ability holds indefinitely and a plain cast runs to
			 * completion and fires — spawning projectiles and raising ECA events on behalf of a
			 * player who is no longer connected and cannot aim, retarget or stop them.
			 *
			 * Cancel() is the same call ResetState makes: it clears the current activation,
			 * drops the persistent IsHeld/IsConsumable/IsMount flags and unlocks any consumable
			 * inventory slot the activation had reserved. Leaving that slot locked would follow
			 * the character into its next session, because the linger's snapshot is what the
			 * reattach reloads.
			 *
			 * Done before the flags and bookkeeping below so a throw here cannot leave a body
			 * half-registered as lingering. */
			try
			{
				if (character.TryGet(out IAbilityController abilityController) &&
					abilityController.IsActivating)
				{
					abilityController.Cancel();
				}
			}
			catch (Exception ex)
			{
				Log.Error("CharacterSystem", $"Failed to cancel the in-flight ability of lingering character {character.ID}: {ex}");
			}

			// Persisted, unlike IsInCombat: it is what tells the login path that this account's
			// only "online" character is a body waiting to be reclaimed rather than a live
			// session, so the player is not locked out of rejoining it.
			character.EnableFlags(CharacterFlags.IsCombatLogged);

			var entry = new LingeringCharacter
			{
				Character = character,
				Account = character.Account,
				ExpiresUtc = DateTime.UtcNow + TimeSpan.FromSeconds(combatLogoutLingerSeconds),
			};

			lingeringCharacters[character.ID] = entry;
			lingeringCharactersByAccount[character.Account] = character.ID;

			// Put the character back into the scene's population. OnDisconnect has already
			// decremented it, but the body is still physically present: leaving it decremented
			// makes a scene holding only unattended bodies report itself empty, which marks it
			// a stale-pulse candidate for unload and advertises capacity that is not free.
			// Unloading that scene would destroy the bodies and strand their claims.
			AdjustLingeringSceneCount(character, 1);

			// Persist immediately. The body can be damaged or killed from here on with no
			// connection attached, so a crash mid-linger would otherwise roll the character back
			// to whatever the last periodic save held — including undoing its own death.
			PersistCharacterSnapshot(character);

			Log.Debug("CharacterSystem",
				$"{character.CharacterName} disconnected in combat; body remains for up to {combatLogoutLingerSeconds:F0}s.");
			return true;
		}

		/// <summary>
		/// Ends lingers whose combat has finished, whose deadline has passed, or whose character
		/// has died.
		/// </summary>
		private void OnPeriodicCombatLingerSweep(float deltaTime)
		{
			if (Server == null || Server.ServerState != ConnectionState.Started || lingeringCharacters.Count == 0)
			{
				return;
			}

			DateTime nowUtc = DateTime.UtcNow;
			List<long> finished = null;

			foreach (var kvp in lingeringCharacters)
			{
				LingeringCharacter entry = kvp.Value;
				IPlayerCharacter character = entry.Character;

				string reason = null;
				if (character == null || character.NetworkObject == null || !character.NetworkObject.IsSpawned)
				{
					// Something else already removed the body; drop the bookkeeping.
					reason = "body no longer spawned";
				}
				else if (character.IsFlagged(CharacterFlags.IsDead))
				{
					reason = "killed while logged out";
				}
				else if (!character.IsFlagged(CharacterFlags.IsInCombat))
				{
					/* The body exists to stay exposed for the fight its owner walked out of.
					 * Once the damage controller's window lapses with no further attacks there
					 * is nothing left to expose it to, so holding it any longer is a penalty
					 * with no purpose.
					 *
					 * This is only safe because CharacterDamageController.EvaluateCombatTimer
					 * re-baselines on a backwards tick. Removing ownership switches the combat
					 * timer from the owner's replicate tick to the server's slower local tick,
					 * and read naively that regression clears combat on the very first sweep —
					 * which would turn this feature off entirely. With the re-baseline the
					 * window restarts at the disconnect, so the body stays for a full combat
					 * window and any attack on it refreshes that window. */
					reason = "combat ended";
				}
				else if (nowUtc >= entry.ExpiresUtc)
				{
					// Backstop for a body held in combat indefinitely by continued attacks, so a
					// player cannot be pinned forever by someone chipping at them.
					reason = "linger deadline reached";
				}

				if (reason != null)
				{
					(finished ??= new List<long>()).Add(kvp.Key);
					pendingLingerReasons[kvp.Key] = reason;
				}
			}

			if (finished == null)
			{
				return;
			}

			foreach (long characterID in finished)
			{
				pendingLingerReasons.TryGetValue(characterID, out string reason);
				pendingLingerReasons.Remove(characterID);
				FinalizeCombatLinger(characterID, reason ?? "unspecified");
			}
		}

		/// <summary>Scratch map so the sweep can report why each linger ended without allocating per entry.</summary>
		private readonly Dictionary<long, string> pendingLingerReasons = new Dictionary<long, string>();

		/// <summary>
		/// Removes a lingering body from the world, persisting it and releasing its session.
		/// </summary>
		/// <param name="characterID">Character whose linger is ending.</param>
		/// <param name="reason">Why it ended, for diagnostics.</param>
		private void FinalizeCombatLinger(long characterID, string reason)
		{
			if (!lingeringCharacters.TryGetValue(characterID, out LingeringCharacter entry))
			{
				return;
			}

			lingeringCharacters.Remove(characterID);
			if (entry.Account != null &&
				lingeringCharactersByAccount.TryGetValue(entry.Account, out long mapped) &&
				mapped == characterID)
			{
				lingeringCharactersByAccount.Remove(entry.Account);
			}

			IPlayerCharacter character = entry.Character;

			CharacterSessionInfo? sessionInfo = null;
			if (Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> data) &&
				data.SessionTokens.TryGetValue(characterID, out CharacterSessionInfo si))
			{
				data.SessionTokens.Remove(characterID);
				sessionInfo = si;
			}

			if (character == null)
			{
				// Nothing left to save or despawn, but the claim still has to be handed back.
				if (sessionInfo.HasValue)
				{
					ReleaseSessionSafely(characterID, sessionInfo.Value.ServerID, sessionInfo.Value.Token);
				}
				return;
			}

			Log.Debug("CharacterSystem", $"Combat linger ended for {character.CharacterName}: {reason}.");

			// Balance the increment taken when the linger began; the body is leaving the scene.
			AdjustLingeringSceneCount(character, -1);

			// SaveAndDespawnCharacter clears IsCombatLogged before snapshotting, so the row this
			// writes no longer claims a body is present.
			SaveAndDespawnCharacter(null, character, sessionInfo);
		}

		/// <summary>
		/// Removes a lingering body's bookkeeping without saving or releasing it.
		/// </summary>
		/// <remarks>
		/// Used only when this server has lost the character's session claim. The normal exit is
		/// <see cref="FinalizeCombatLinger"/>, which saves and hands the claim back; neither is
		/// valid once the claim belongs to someone else, so this just balances the scene count
		/// and drops the entry. The caller despawns the body.
		/// </remarks>
		private void DropLingeringCharacter(long characterID)
		{
			if (!lingeringCharacters.TryGetValue(characterID, out LingeringCharacter entry))
			{
				return;
			}

			lingeringCharacters.Remove(characterID);
			if (entry.Account != null &&
				lingeringCharactersByAccount.TryGetValue(entry.Account, out long mapped) &&
				mapped == characterID)
			{
				lingeringCharactersByAccount.Remove(entry.Account);
			}
			pendingLingerReasons.Remove(characterID);

			// Balance the increment taken when the linger began.
			AdjustLingeringSceneCount(entry.Character, -1);

			DespawnLingeringBody(entry.Character);
		}

		/// <summary>
		/// Ends every active linger. Used on shutdown so no body is left owning a claim.
		/// </summary>
		private void FinalizeAllCombatLingers(string reason)
		{
			if (lingeringCharacters.Count == 0)
			{
				return;
			}

			var ids = new List<long>(lingeringCharacters.Keys);
			foreach (long characterID in ids)
			{
				FinalizeCombatLinger(characterID, reason);
			}
		}

		/// <summary>
		/// Reclaims a lingering body for a player who has just reconnected.
		/// </summary>
		/// <remarks>
		/// The body is persisted and despawned, then the normal load path runs against the row
		/// that was just written — reusing the whole tested load pipeline instead of grafting a
		/// live connection onto an already-spawned object. The save and the load are chained
		/// inside a single work item so the load cannot read the character back before its
		/// current state has landed; damage taken while logged out would otherwise be refunded.
		/// <para>
		/// The existing session token is carried through rather than released and re-claimed, so
		/// there is no window in which another server could take the character mid-handover.
		/// </para>
		/// </remarks>
		/// <param name="conn">The reconnecting connection.</param>
		/// <param name="accountName">Account that authenticated.</param>
		/// <param name="characterService">Character service for the load.</param>
		/// <param name="serverID">This scene server's ID.</param>
		/// <returns><c>true</c> when a lingering body was claimed and the load was started.</returns>
		private bool TryReattachLingeringCharacter(NetworkConnection conn, string accountName, ICharacterService characterService, long serverID)
		{
			if (string.IsNullOrEmpty(accountName) ||
				!lingeringCharactersByAccount.TryGetValue(accountName, out long characterID) ||
				!lingeringCharacters.TryGetValue(characterID, out LingeringCharacter entry))
			{
				return false;
			}

			IPlayerCharacter character = entry.Character;

			lingeringCharacters.Remove(characterID);
			lingeringCharactersByAccount.Remove(entry.Account ?? accountName);

			// Balance the increment taken when the linger began, here rather than on the success
			// path, so the early returns below cannot leave the scene permanently over-counted.
			// The normal load this hands off to increments again via OnAfterLoadCharacter.
			AdjustLingeringSceneCount(character, -1);

			// Take the claim out of SessionTokens and hand it to the load. On success the load
			// puts it back; on failure it releases it — the same contract every other load path
			// follows, so no branch here can strand it.
			CharacterSessionInfo? sessionInfo = null;
			if (Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> data) &&
				data.SessionTokens.TryGetValue(characterID, out CharacterSessionInfo si))
			{
				data.SessionTokens.Remove(characterID);
				sessionInfo = si;
			}

			if (character == null || character.NetworkObject == null || !character.NetworkObject.IsSpawned)
			{
				// The body went away between the sweep and here. Fall back to a normal load,
				// releasing the orphaned claim so the fresh claim can succeed.
				if (sessionInfo.HasValue)
				{
					ReleaseSessionSafely(characterID, sessionInfo.Value.ServerID, sessionInfo.Value.Token);
				}
				return false;
			}

			if (!sessionInfo.HasValue)
			{
				// A lingering body must always hold its claim; if it somehow does not, the
				// normal load path (which claims from scratch) is the correct recovery.
				Log.Warning("CharacterSystem", $"Lingering character {characterID} had no session token; falling back to a normal load.");
				DespawnLingeringBody(character);
				return false;
			}

			Log.Debug("CharacterSystem", $"{character.CharacterName} reconnected during combat logout; reclaiming body.");

			// Snapshot everything the body accumulated while unattended, then remove it.
			character.DisableFlags(CharacterFlags.IsCombatLogged);
			character.DisableFlags(CharacterFlags.IsInCombat);
			character.DisableFlags(CharacterFlags.IsLoaded);

			CharacterData charData = BuildCharacterData(character);
			var buffDataList = new List<CharacterBuffData>(8);
			var attributeDataList = new List<CharacterAttributeData>(16);
			var abilityDataList = new List<CharacterAbilityData>(8);
			AppendBuffData(character, buffDataList);
			AppendAttributeData(character, attributeDataList);
			AppendAbilityData(character, abilityDataList);

			DespawnLingeringBody(character);

			Guid heldToken = sessionInfo.Value.Token;
			long heldServerID = sessionInfo.Value.ServerID;

			if (!EnqueueAsyncWork(() => ReattachAndLoadAsync(
					conn, accountName, characterService, serverID,
					charData, buffDataList, attributeDataList, abilityDataList, heldToken)))
			{
				// Nothing will run the save or the load, so hand the claim back rather than
				// leaving the character owned by a server that has forgotten about it.
				Log.Error("CharacterSystem", $"Failed to enqueue reattach for character {characterID}; releasing its session.");
				QueuePendingFlush(characterID, charData, new CharacterSessionInfo(heldToken, heldServerID));
				DisconnectWithNotice(conn, DisconnectNoticeReason.ServerError);
			}

			return true;
		}

		/// <summary>
		/// Adds or removes a lingering body from its scene instance's character count.
		/// </summary>
		/// <remarks>
		/// Mirrors what <c>SceneServerSystem</c> does on connect and disconnect, so a scene
		/// holding unattended bodies is neither reported as empty nor advertised as having
		/// capacity it does not have. Every path that starts or ends a linger must call this in
		/// balanced pairs; a reattach hands off to the normal load, which increments again.
		/// </remarks>
		private void AdjustLingeringSceneCount(IPlayerCharacter character, int amount)
		{
			if (character == null ||
				!Server.BehaviourRegistry.TryGet(out ISceneServerSystem<NetworkConnection> sceneServerSystem))
			{
				return;
			}

			if (character.IsInInstance())
			{
				sceneServerSystem.AdjustSceneCharacterCount(character.WorldServerID, character.InstanceSceneName, character.InstanceSceneHandle, amount);
			}
			else
			{
				sceneServerSystem.AdjustSceneCharacterCount(character.WorldServerID, character.SceneName, character.SceneHandle, amount);
			}
		}

		/// <summary>
		/// Despawns a lingering body without saving or releasing — the caller owns both.
		/// </summary>
		private void DespawnLingeringBody(IPlayerCharacter character)
		{
			if (character?.NetworkObject == null)
			{
				return;
			}

			DispatchCharacterEvent(OnDespawnCharacter, null, character, nameof(OnDespawnCharacter));

			if (character.NetworkObject.IsSpawned)
			{
				ServerManager.Despawn(character.NetworkObject, DespawnType.Pool);
			}
			else
			{
				Server.NetworkWrapper.NetworkManager.StorePooledInstantiated(character.NetworkObject, true);
			}
		}

		/// <summary>
		/// Persists the reclaimed body, then runs the normal character load against it.
		/// </summary>
		private async System.Threading.Tasks.Task ReattachAndLoadAsync(
			NetworkConnection conn,
			string accountName,
			ICharacterService characterService,
			long serverID,
			CharacterData charData,
			List<CharacterBuffData> buffs,
			List<CharacterAttributeData> attributes,
			List<CharacterAbilityData> abilities,
			Guid heldToken)
		{
			// Ordering matters: the load below re-reads this row, so anything not written yet is
			// silently rolled back — most visibly the health the character lost while it was
			// standing there unattended.
			//
			// Ownership-gated: this write carries the claim the lingering body was holding, so
			// if that claim lapsed while the body stood there the reclaimed state is refused
			// rather than overwriting whichever server took the character.
			if (!await SaveCharacterAsync(charData, new CharacterSessionInfo(heldToken, serverID)))
			{
				await Log.Error("CharacterSystem",
					$"Failed to persist reclaimed body for character {charData.ID}; the reload may restore pre-logout state.");
				QueuePendingFlush(charData.ID, charData, null);
			}

			if (attributes.Count > 0)
			{
				await SaveAttributesAsync(attributes);
			}
			if (buffs.Count > 0)
			{
				await SaveBuffsAsync(buffs);
			}
			if (abilities.Count > 0)
			{
				await SaveAbilitiesAsync(abilities);
			}

			// The character ID travels with the token: LoadCharacterAsync has to be able to hand
			// this claim back on any path that abandons the load, including the ones that fail
			// before a character row has been read.
			await LoadCharacterAsync(conn, accountName, characterService, serverID, heldToken, charData.ID);
		}

		/// <summary>
		/// Appends every lingering body to the periodic save snapshot.
		/// </summary>
		/// <remarks>
		/// A lingering body is deliberately not in <c>CharactersByID</c> — it has no connection
		/// — so the periodic save walked straight past it. The only writes it got were the one
		/// taken when the linger began and the one taken when it ended, which left everything
		/// that happened in between unpersisted: precisely the damage the body is left standing
		/// there to receive. A scene server that died mid-linger therefore restored the
		/// character at full health, refunding a fight it had already lost and handing the
		/// player exactly the escape combat-logout protection exists to deny.
		/// <para>
		/// Each entry carries the claim this server still holds for it, so these writes are
		/// ownership-gated like every other save and a body whose claim has lapsed is refused
		/// rather than allowed to overwrite whichever server took the character.
		/// </para>
		/// </remarks>
		/// <param name="mappingData">Mapping data holding the session claims.</param>
		/// <param name="characterData">Destination for the character rows.</param>
		/// <param name="buffs">Destination for buff rows.</param>
		/// <param name="attributes">Destination for attribute rows.</param>
		/// <param name="abilities">Destination for ability rows.</param>
		private void AppendLingeringCharacterSnapshots(
			ICharacterMappingData<NetworkConnection> mappingData,
			List<(CharacterData Data, CharacterSessionInfo? Ownership)> characterData,
			List<CharacterBuffData> buffs,
			List<CharacterAttributeData> attributes,
			List<CharacterAbilityData> abilities)
		{
			if (lingeringCharacters.Count == 0)
			{
				return;
			}

			foreach (var kvp in lingeringCharacters)
			{
				IPlayerCharacter character = kvp.Value.Character;
				if (character == null || character.NetworkObject == null || !character.NetworkObject.IsSpawned)
				{
					// The sweep will drop the bookkeeping; nothing here to write.
					continue;
				}

				CharacterSessionInfo? ownership = mappingData.SessionTokens.TryGetValue(kvp.Key, out CharacterSessionInfo held)
					? held
					: (CharacterSessionInfo?)null;

				characterData.Add((BuildCharacterData(character), ownership));
				AppendBuffData(character, buffs);
				AppendAttributeData(character, attributes);
				AppendAbilityData(character, abilities);
			}
		}

		/// <summary>
		/// Enqueues a save of the character's current state without releasing its session.
		/// </summary>
		private void PersistCharacterSnapshot(IPlayerCharacter character)
		{
			// The linger keeps its claim in SessionTokens on purpose, so this write can prove
			// ownership like every other save the server makes while it holds one.
			CharacterSessionInfo? ownership = null;
			if (Server.DataContainerRegistry.TryGet(out ICharacterMappingData<NetworkConnection> mappingData) &&
				mappingData.SessionTokens.TryGetValue(character.ID, out CharacterSessionInfo held))
			{
				ownership = held;
			}

			CharacterData charData = BuildCharacterData(character);
			var buffDataList = new List<CharacterBuffData>(8);
			var attributeDataList = new List<CharacterAttributeData>(16);
			var abilityDataList = new List<CharacterAbilityData>(8);
			AppendBuffData(character, buffDataList);
			AppendAttributeData(character, attributeDataList);
			AppendAbilityData(character, abilityDataList);

			if (!EnqueueAsyncWork(() => SaveCharacterAsync(charData, ownership)))
			{
				QueuePendingFlush(charData.ID, charData, null);
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
	}
}
