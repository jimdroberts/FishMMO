using FishNet.Connection;
using FishNet.Object;
using SceneManager = FishNet.Managing.Scened.SceneManager;
using FishNet.Transporting;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
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
using UnityEngine.SceneManagement;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Character loading and scene-entry logic: authentication, async DB fetch, prefab instantiation, scene validation, and client broadcast handshakes.
	/// </summary>
	public partial class CharacterSystem
	{
		/// <summary>
		/// Minimum seconds between auth-callback character load requests per connection.
		/// Prevents a misbehaving or compromised client from spamming expensive DB load operations.
		/// </summary>
		private const float AuthCallbackCooldownSeconds = 2.0f;

		/// <summary>
		/// Tracks the last auth-callback time per account name for rate limiting.
		/// Keying by account name prevents a single user from bypassing the limit by
		/// opening multiple connections.
		/// Entries are removed when the connection disconnects via OnRemoteConnectionStopped.
		/// </summary>
		private readonly ConcurrentDictionary<string, DateTime> authCallbackLastTimeByAccount =
			new ConcurrentDictionary<string, DateTime>();

		/// <summary>
		/// Tracks the last scene-unload broadcast time per connection ClientId for rate limiting.
		/// Scene unload is per-connection, not per-account; a separate dictionary from
		/// authCallbackLastTimeByAccount.
		/// </summary>
		private readonly ConcurrentDictionary<int, DateTime> sceneUnloadLastTimeByClientId =
			new ConcurrentDictionary<int, DateTime>();

		/// <summary>
		/// Tracks the last validated-scene broadcast time per connection ClientId for rate limiting.
		/// Prevents a malicious client from spamming <see cref="OnClientValidatedSceneBroadcastReceived"/>,
		/// which triggers expensive character-spawn and mapping operations.
		/// </summary>
		private readonly ConcurrentDictionary<int, DateTime> validatedSceneLastTimeByClientId =
			new ConcurrentDictionary<int, DateTime>();

		/// <summary>
		/// Minimum seconds between validated-scene broadcasts per connection.
		/// </summary>
		private const float ValidatedSceneBroadcastCooldownSeconds = 2.0f;

		/// <summary>
		/// Deadline by which a connection in <c>WaitingSceneLoadCharacters</c> must have
		/// completed the scene handshake, keyed by ClientId.
		/// </summary>
		/// <remarks>
		/// Entering <c>WaitingSceneLoadCharacters</c> means the character is already claimed and
		/// recorded in <c>SessionTokens</c>, so the lease refresher keeps the claim alive
		/// indefinitely. The only exits were the client's own
		/// <c>ClientValidatedSceneBroadcast</c> and the connection dropping — so a client that
		/// authenticated and then simply stopped talking (hung, modified, or one whose broadcast
		/// was lost) held its character Online, and therefore unloadable on any server, for as
		/// long as it kept the transport open. This bounds that window.
		/// </remarks>
		private readonly ConcurrentDictionary<int, DateTime> sceneLoadDeadlines =
			new ConcurrentDictionary<int, DateTime>();

		/// <summary>How long a client has to finish the scene handshake before it is disconnected.</summary>
		private static readonly TimeSpan SceneLoadHandshakeTimeout = TimeSpan.FromSeconds(90);

		/// <summary>Interval in seconds between scene-handshake timeout sweeps.</summary>
		private const float SceneLoadTimeoutSweepIntervalSeconds = 10f;

		/// <summary>
		/// Disconnects connections that claimed a character but never completed the scene
		/// handshake, so the claim is released instead of being held for the life of the socket.
		/// </summary>
		private void OnPeriodicSceneLoadTimeoutSweep(float deltaTime)
		{
			if (Server == null || Server.ServerState != ConnectionState.Started || sceneLoadDeadlines.IsEmpty)
			{
				return;
			}

			if (!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData))
			{
				return;
			}

			DateTime nowUtc = DateTime.UtcNow;

			foreach (var kvp in sceneLoadDeadlines)
			{
				if (nowUtc < kvp.Value)
				{
					continue;
				}

				sceneLoadDeadlines.TryRemove(kvp.Key, out _);

				NetworkConnection conn = null;
				if (ServerManager != null)
				{
					ServerManager.Clients.TryGetValue(kvp.Key, out conn);
				}
				if (conn == null || !conn.IsActive)
				{
					continue;
				}

				// Only act while the connection is still stuck in the waiting state. A
				// recycled ClientId, or one that completed the handshake between the deadline
				// and this sweep, must be left alone.
				if (!mappingData.WaitingSceneLoadCharacters.ContainsKey(conn))
				{
					continue;
				}

				Log.Warning("CharacterSystem",
					$"Connection {kvp.Key} never acknowledged its scene load within {SceneLoadHandshakeTimeout.TotalSeconds:F0}s; disconnecting to release its character claim.");
				DisconnectWithNotice(conn, DisconnectNoticeReason.SceneHandshakeTimedOut);
			}
		}

		/// <summary>
		/// Deadline by which each authenticated connection must have a character somewhere this
		/// server tracks it, keyed by ClientId.
		/// </summary>
		/// <remarks>
		/// <see cref="sceneLoadDeadlines"/> covers the scene handshake, but it is only armed
		/// once <see cref="InstantiateAndLoadCharacter"/> has put the character into
		/// <c>WaitingSceneLoadCharacters</c> — everything before that point was unwatched. The
		/// load runs on an async worker and hands back through <c>TryEnqueueMainThread</c>,
		/// which is bounded and returns false when saturated; the failure branches then try to
		/// deliver their own disconnect through that same saturated queue. A connection lost
		/// anywhere in that window is authenticated, in no map, and driven by nothing, so it
		/// sits on the scene server behind a loading screen for as long as the transport stays
		/// open.
		/// <para>
		/// This mirrors <c>WorldSceneSystem</c>'s residency watchdog and bounds every such path
		/// at once rather than patching each delivery site: still being here with no character
		/// means the load failed, whatever the cause. Disconnecting hands the client back to its
		/// own reconnect loop, which returns through the world server and is routed again.
		/// </para>
		/// </remarks>
		private readonly ConcurrentDictionary<int, DateTime> characterResidencyDeadlines =
			new ConcurrentDictionary<int, DateTime>();

		/// <summary>
		/// How long an authenticated connection may go without a character before it is treated
		/// as a failed load.
		/// </summary>
		/// <remarks>
		/// Must comfortably exceed a cold character load — a claim with up to five retries
		/// followed by fourteen sub-entity fetches in one transaction — so a merely slow
		/// database is never mistaken for a stuck one. It must also stay below
		/// <see cref="SceneLoadHandshakeTimeout"/>'s scope so the two watchdogs cannot both
		/// apply to the same connection: this one stops the moment the character is registered,
		/// which is the same moment that one starts.
		/// </remarks>
		private static readonly TimeSpan CharacterResidencyTimeout = TimeSpan.FromSeconds(60);

		/// <summary>Interval in seconds between character-residency sweeps.</summary>
		private const float CharacterResidencySweepIntervalSeconds = 10f;

		/// <summary>
		/// Disconnects connections that authenticated but never ended up with a character on
		/// this server. See <see cref="characterResidencyDeadlines"/>.
		/// </summary>
		private void OnPeriodicCharacterResidencySweep(float deltaTime)
		{
			if (Server == null || Server.ServerState != ConnectionState.Started || characterResidencyDeadlines.IsEmpty)
			{
				return;
			}

			if (!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData))
			{
				return;
			}

			DateTime nowUtc = DateTime.UtcNow;

			foreach (var kvp in characterResidencyDeadlines)
			{
				NetworkConnection conn = null;
				if (ServerManager != null)
				{
					ServerManager.Clients.TryGetValue(kvp.Key, out conn);
				}
				if (conn == null || !conn.IsActive)
				{
					characterResidencyDeadlines.TryRemove(kvp.Key, out _);
					continue;
				}

				// The load got somewhere: either the character is waiting on its scene handshake
				// (which sceneLoadDeadlines now owns) or it is fully in the world. Either way
				// this watchdog is done with the connection.
				if (mappingData.WaitingSceneLoadCharacters.ContainsKey(conn) ||
					mappingData.ConnectionCharacters.ContainsKey(conn))
				{
					characterResidencyDeadlines.TryRemove(kvp.Key, out _);
					continue;
				}

				if (nowUtc < kvp.Value)
				{
					continue;
				}

				characterResidencyDeadlines.TryRemove(kvp.Key, out _);

				Log.Warning("CharacterSystem",
					$"Connection {kvp.Key} authenticated but had no character after {CharacterResidencyTimeout.TotalSeconds:F0}s; " +
					"disconnecting so the client can retry through the world server.");
				// Non-terminal: the client's reconnect loop returns through the world server,
				// which re-routes it — the same recovery every other transient load failure gets.
				DisconnectWithNotice(conn, DisconnectNoticeReason.ServerError);
			}
		}

		/// <summary>
		/// Handles client authentication results, loads character data and initiates scene loading.
		/// </summary>
		/// <param name="conn">Network connection of the client.</param>
		/// <param name="authenticated">True if authentication succeeded.</param>
		private void Authenticator_OnClientAuthenticationResult(NetworkConnection conn, bool authenticated)
		{
			DateTime nowUtc = DateTime.UtcNow;

			// Arm the residency backstop before any branch below can return. Every early exit
			// here is supposed to disconnect, but the ones that deliver that disconnect through
			// the main-thread queue can be dropped when it is saturated — which is precisely
			// when this watchdog has to be the thing that ends the connection.
			if (conn != null && conn.IsActive)
			{
				characterResidencyDeadlines[conn.ClientId] = nowUtc + CharacterResidencyTimeout;
			}

			// Resolve account name first so the rate limit can be applied per-account.
			if (!Server.AccountManager.GetAccountNameByConnection(conn, out string accountName))
			{
				DisconnectWithNotice(conn, DisconnectNoticeReason.ProtocolViolation, terminal: true);
				return;
			}

			// Per-account rate limit: prevent repeated auth callbacks from triggering
			// expensive DB load operations in rapid succession. Keying by account name
			// prevents a single user from bypassing the limit by opening multiple connections.
			bool wasCoolingDown = false;
			authCallbackLastTimeByAccount.AddOrUpdate(
				accountName,
				nowUtc,
				(_, lastTime) =>
				{
					if ((nowUtc - lastTime).TotalSeconds < AuthCallbackCooldownSeconds)
					{
						wasCoolingDown = true;
						return lastTime; // Don't update timestamp if cooling down.
					}
					return nowUtc;
				});
			if (wasCoolingDown)
			{
				/* Returning quietly here stranded the client.
				 *
				 * This callback is the ONLY thing that starts a character load, and every
				 * watchdog on this server is armed downstream of it: sceneLoadDeadlines is set
				 * when the character reaches WaitingSceneLoadCharacters, and the transfer
				 * watchdog only covers connections whose character was already handed off. A
				 * connection refused here is therefore authenticated, in no map, and watched by
				 * nothing — it sits on the scene server behind a loading screen until the player
				 * kills the client.
				 *
				 * The limit is still enforced; it just says so. Non-terminal because retrying is
				 * exactly the recovery: the cooldown entry is dropped by
				 * OnRemoteConnectionStopped, so the client's next attempt starts clean. */
				Log.Warning("CharacterSystem", $"Auth callback rate-limited for account {accountName}");
				DisconnectWithNotice(conn, DisconnectNoticeReason.RateLimited);
				return;
			}

			// Is the character already loading?
			if (!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData))
			{
				DisconnectWithNotice(conn, DisconnectNoticeReason.ServerError);
				return;
			}

			if (mappingData.WaitingSceneLoadCharacters.ContainsKey(conn))
			{
				return;
			}
			if (!authenticated ||
				!Server.BehaviourRegistry.TryGet(out ISceneServerSystem<NetworkConnection> sceneServerSystem))
			{
				DisconnectWithNotice(conn, DisconnectNoticeReason.ServerError);
				return;
			}

			if (Server?.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var characterService))
			{
				DisconnectWithNotice(conn, DisconnectNoticeReason.ServerError);
				return;
			}

			// Resolve server ID on the main thread — required for TryClaimAsync.
			long serverID = 0;
			if (Server.DataContainerRegistry.TryGet<ISceneServerRuntimeData>(out var runtimeData))
			{
				serverID = runtimeData.ID;
			}

			if (serverID <= 0)
			{
				Log.Error("CharacterSystem", "Authenticator_OnClientAuthenticationResult: Server ID is invalid, cannot claim session.");
				DisconnectWithNotice(conn, DisconnectNoticeReason.ServerError);
				return;
			}

			// If this account left a body behind mid-combat and it is still standing, hand that
			// body's state and its existing claim to the load rather than starting a fresh one.
			// Checked before the normal path because the claim is still held here: a plain load
			// would contend with ourselves and be kicked.
			if (TryReattachLingeringCharacter(conn, accountName, characterService, serverID))
			{
				return;
			}

			if (!EnqueueAsyncWork(() => LoadCharacterAsync(conn, accountName, characterService, serverID)))
			{
				DisconnectWithNotice(conn, DisconnectNoticeReason.ServerError);
			}
		}

		/// <summary>
		/// Attempts to claim the character's session, retrying briefly while the previous
		/// owner is still handing it back.
		/// </summary>
		/// <remarks>
		/// A scene transfer releases the character on the source scene server and claims it on
		/// the destination, and those are two independent asynchronous operations against the
		/// database. The client's journey between them — disconnect, reconnect to the world
		/// server, re-authenticate, connect to the destination — normally takes far longer than
		/// the release, but under database load the order can invert. A single attempt then
		/// turned a few hundred milliseconds of overlap into a kick, which the player sees as a
		/// failed transfer even though the reconnect loop eventually recovers.
		/// <para>
		/// Only contention is retried. A missing or deleted character is a permanent condition
		/// and fails immediately.
		/// </para>
		/// </remarks>
		/// <returns>The session token on success, or <see cref="Guid.Empty"/> if the claim failed (the connection has then been kicked).</returns>
		private async Task<Guid> ClaimCharacterSessionAsync(NetworkConnection conn, ICharacterService characterService, long characterID, long serverID)
		{
			const int maxAttempts = 5;
			const int retryDelayStepMs = 150;

			DatabaseResult<Guid> claimResult = default;

			for (int attempt = 1; attempt <= maxAttempts; attempt++)
			{
				claimResult = await characterService.TryClaimAsync(characterID, serverID);
				if (claimResult.IsSuccess)
				{
					if (attempt > 1)
					{
						await Log.Debug("CharacterSystem", $"Claimed character {characterID} on attempt {attempt}.");
					}
					return claimResult.Data;
				}

				// Anything other than "someone else owns it" will not resolve by waiting.
				if (claimResult.ErrorCode != DatabaseErrorCodes.InvalidOperation)
				{
					break;
				}

				if (attempt == maxAttempts)
				{
					break;
				}

				// The connection going away mid-retry makes the claim pointless.
				if (conn == null || !conn.IsActive)
				{
					return Guid.Empty;
				}

				await Task.Delay(retryDelayStepMs * attempt);
			}

			await Log.Error("CharacterSystem", $"TryClaimAsync failed for character {characterID}: {claimResult.ErrorCode} - {claimResult.ErrorMessage}");
			TryEnqueueMainThread(() =>
			{
				DisconnectWithNotice(conn, DisconnectNoticeReason.CharacterUnavailable);
			});
			return Guid.Empty;
		}

		/// <summary>
		/// Asynchronously fetches the selected character and ALL related sub-entity data
		/// from the database using a Unit of Work for a consistent snapshot, then queues
		/// the main-thread instantiation and scene loading.
		/// </summary>
		/// <param name="conn">Connection to load for.</param>
		/// <param name="accountName">Authenticated account.</param>
		/// <param name="characterService">Character service.</param>
		/// <param name="serverID">This scene server's ID.</param>
		/// <param name="preClaimedSessionToken">
		/// An ownership token this server already holds for the character, supplied when
		/// reclaiming a combat-logout body. When set the claim step is skipped entirely — the
		/// character is already ours, and claiming again would contend with our own session.
		/// </param>
		/// <param name="preClaimedCharacterID">
		/// Character the <paramref name="preClaimedSessionToken"/> belongs to.
		/// </param>
		/// <remarks>
		/// A pre-claimed token has to be released on every path that abandons the load, exactly
		/// like one claimed here. It is handed over by
		/// <see cref="TryReattachLingeringCharacter"/>, which has already removed it from
		/// <c>SessionTokens</c> — so if this method returns without either installing it or
		/// giving it back, nothing in the process holds it any more and the character stays
		/// Online in the database until its lease expires, kicking the player from every scene
		/// server in the meantime. That is why the token is tracked from the first line rather
		/// than from the point the claim step would otherwise assign it, and why
		/// <paramref name="preClaimedCharacterID"/> exists: the early failures happen before
		/// any character row has been read, so there would otherwise be no ID to release under.
		/// </remarks>
		private async Task LoadCharacterAsync(NetworkConnection conn, string accountName, ICharacterService characterService, long serverID, Guid preClaimedSessionToken = default, long preClaimedCharacterID = 0)
		{
			CharacterData charData = default;
			// Tracked from here, not from the claim step, so an abandoned load always has
			// something to hand back. See the remarks above.
			Guid sessionToken = preClaimedSessionToken;
			long sessionCharacterID = preClaimedSessionToken != Guid.Empty ? preClaimedCharacterID : 0;

			// Releases whatever claim is currently held, if any, so the caller can return.
			async Task ReleaseHeldSessionAsync()
			{
				if (sessionToken == Guid.Empty || sessionCharacterID <= 0)
				{
					return;
				}
				Guid releasing = sessionToken;
				long releasingID = sessionCharacterID;
				sessionToken = Guid.Empty;
				sessionCharacterID = 0;
				await ReleaseCharacterSessionAsync(releasingID, serverID, releasing);
			}

			try
			{
				var serviceRegistry = Server.Database.ServiceRegistry;

				if (!serviceRegistry.TryGet<IUnitOfWorkService>(out var unitOfWorkService))
				{
					await ReleaseHeldSessionAsync();
					TryEnqueueMainThread(() =>
					{
						if (conn != null && conn.IsActive)
						{
							Log.Debug("CharacterSystem", "Failed to resolve IUnitOfWorkService.");
							DisconnectWithNotice(conn, DisconnectNoticeReason.ServerError);
						}
					});
					return;
				}

				// --- Fetch all sub-entity data within a single Unit of Work (consistent snapshot) ---
				IReadOnlyList<CharacterInventoryData> inventoryData = null;
				IReadOnlyList<CharacterBankData> bankData = null;
				IReadOnlyList<CharacterEquipmentData> equipmentData = null;
				IReadOnlyList<CharacterAttributeData> attributeData = null;
				IReadOnlyList<CharacterAbilityData> abilityData = null;
				IReadOnlyList<CharacterKnownAbilityData> knownAbilityData = null;
				IReadOnlyList<CharacterAchievementData> achievementData = null;
				IReadOnlyList<CharacterFriendData> friendData = null;
				CharacterGuildData? guildData = null;
				CharacterPartyData? partyData = null;
				IReadOnlyList<CharacterHotkeyData> hotkeyData = null;
				IReadOnlyList<CharacterBuffData> buffData = null;
				IReadOnlyList<CharacterFactionData> factionData = null;
				IReadOnlyList<CharacterQuestData> questData = null;

				// --- Fetch the character row and claim its session BEFORE the Unit of Work ---
				// The claim is the gate: if another server already owns this character we fail
				// before spending resources on 13 sub-entity fetches, prefab instantiation and
				// scene loading. It runs outside the UoW because it may have to retry, and
				// retrying inside an open transaction would hold that transaction for the whole
				// backoff.
				DatabaseResult<CharacterData?> fetchResult = await characterService.FetchByAccountAsync(accountName, selected: true);
				if (!fetchResult.IsSuccess || !fetchResult.Data.HasValue)
				{
					await ReleaseHeldSessionAsync();
					TryEnqueueMainThread(() =>
					{
						if (conn != null && conn.IsActive)
						{
							Log.Debug("CharacterSystem", "No selected character found for account.");
							DisconnectWithNotice(conn, DisconnectNoticeReason.CharacterUnavailable, terminal: true);
						}
					});
					return;
				}

				charData = fetchResult.Data.Value;
				long characterID = charData.ID;

				if (sessionToken != Guid.Empty)
				{
					/* Reclaiming our own combat-logout body: the claim never left this server.
					 *
					 * The claim is for one specific character, so it can only be reused if the
					 * account's selected character is still that one. CharacterSelectSystem
					 * refuses to move the selection while a character is in the world, which is
					 * what normally guarantees it — but relying on that silently would mean a
					 * mismatch installs the lingering body's token under a different character's
					 * ID, leaving the new character unclaimed (free for a second scene server to
					 * take) and the body's claim unreleasable. Fail the load instead. */
					if (sessionCharacterID != characterID)
					{
						await Log.Error("CharacterSystem",
							$"Reclaimed session belongs to character {sessionCharacterID} but the account's selected character is {characterID}; abandoning load.");
						await ReleaseHeldSessionAsync();
						TryEnqueueMainThread(() => DisconnectWithNotice(conn, DisconnectNoticeReason.CharacterUnavailable, terminal: true));
						return;
					}
				}
				else
				{
					sessionToken = await ClaimCharacterSessionAsync(conn, characterService, characterID, serverID);
					if (sessionToken == Guid.Empty)
					{
						// ClaimCharacterSessionAsync has already logged and kicked.
						return;
					}
					sessionCharacterID = characterID;
				}

				// Re-read the character now that we hold the claim. The row above was read
				// without ownership, so on a scene transfer it can predate the previous
				// server's final save — the save that recorded the destination scene and the
				// position the player is supposed to arrive at. Loading that stale copy puts
				// the player back where they started, and the retry above makes the window
				// wider precisely when a transfer is contended. Everything read from here on
				// is protected by the claim.
				DatabaseResult<CharacterData?> ownedFetch = await characterService.FetchByAccountAsync(accountName, selected: true);
				if (!ownedFetch.IsSuccess || !ownedFetch.Data.HasValue || ownedFetch.Data.Value.ID != characterID)
				{
					// The account's selected character changed between the two reads, so the
					// claim we hold is for a character this connection is no longer loading.
					await Log.Warning("CharacterSystem", $"Selected character changed while claiming {characterID}; abandoning load.");
					await ReleaseHeldSessionAsync();
					TryEnqueueMainThread(() => DisconnectWithNotice(conn, DisconnectNoticeReason.CharacterUnavailable));
					return;
				}
				charData = ownedFetch.Data.Value;

				/* Resolve where an instanced character actually lives.
				 *
				 * The character row records only the instance's database ID; the scene name and
				 * runtime handle it resolves to live on the scene row. Nothing used to perform
				 * that lookup, so InstanceSceneName was never assigned and IPlayerCharacter.
				 * IsInInstance() — which requires it — was permanently false. The load below
				 * then targeted the character's OPEN WORLD scene and handle, which do not exist
				 * on the scene server hosting the instance, so the connection was dropped with
				 * SceneUnavailable, the world server saw IsInInstance in the row and routed the
				 * reconnect straight back, and the character could never enter the world again.
				 *
				 * A resolution that does not name a ready instance on THIS server is not a
				 * transient condition worth retrying — it is what strands the character — so the
				 * flag is cleared here, under the claim we already hold, and the character is
				 * loaded into the open world instead. */
				string instanceSceneName = null;
				long instanceSceneHandle = 0;

				if (charData.Flags.IsFlagged(CharacterFlags.IsInInstance))
				{
					InstanceResolution resolution = await ResolveInstanceSceneAsync(charData, serverID);

					switch (resolution.Outcome)
					{
						case InstanceResolutionOutcome.Resolved:
							instanceSceneName = resolution.SceneName;
							instanceSceneHandle = resolution.SceneHandle;
							break;

						case InstanceResolutionOutcome.HostedElsewhere:
							/* The instance is alive, just not here. Clearing the flag would evict
							 * a player from a dungeon that is still running, so hand the client
							 * back to the world server, which routes from the same scene row and
							 * will send it to the right scene server. Bounded by the client's own
							 * reconnect budget rather than by anything here. */
							await Log.Warning("CharacterSystem",
								$"Character {charData.ID} is in instance {charData.InstanceID}, hosted by scene server {resolution.HostSceneServerID} rather than {serverID}; returning it to the world server to be re-routed.");
							await ReleaseHeldSessionAsync();
							TryEnqueueMainThread(() => DisconnectWithNotice(conn, DisconnectNoticeReason.SceneUnavailable));
							return;

						default:
							await Log.Warning("CharacterSystem",
								$"Character {charData.ID} could not be placed in instance {charData.InstanceID} ({resolution.Reason}); clearing its instance flag and loading it into the open world.");

							int clearedFlags = charData.Flags;
							clearedFlags.DisableBit(CharacterFlags.IsInInstance);

							/* Adopted locally whether or not the write below lands. The character
							 * IS being loaded into the open world, so the in-memory state has to
							 * say so — otherwise the flag survives on a character that is not in
							 * any instance, and the world server routes it straight back here on
							 * its next login. A failed write is recovered by the ordinary save
							 * path, whose version guard accepts the higher counter. */
							charData = charData.WithFlagsVersionAndTimestamp(
								clearedFlags, charData.Version + 1, DateTime.UtcNow);

							// Ownership-gated, like every other write this server makes while it
							// holds a claim, so a lapsed lease cannot overwrite the new owner.
							DatabaseResult clearResult = await characterService.PersistOwnedAsync(
								charData,
								new CharacterSessionLeaseData(charData.ID, serverID, sessionToken));

							if (!clearResult.IsSuccess)
							{
								if (clearResult.ErrorCode == DatabaseErrorCodes.Forbidden)
								{
									// The claim we acquired moments ago is already gone, so this
									// load has no authority over the character at all. Abandon it
									// rather than spawning a second copy of somebody else's.
									await Log.Error("CharacterSystem",
										$"Lost the session claim for character {charData.ID} while releasing it from instance {charData.InstanceID}; abandoning the load.");
									await ReleaseHeldSessionAsync();
									TryEnqueueMainThread(() => DisconnectWithNotice(conn, DisconnectNoticeReason.CharacterUnavailable));
									return;
								}

								await Log.Warning("CharacterSystem",
									$"Failed to persist the cleared instance flag for character {charData.ID}: {clearResult.ErrorCode} - {clearResult.ErrorMessage}. " +
									"The character loads into the open world regardless; the next save carries the change.");
							}
							break;
					}
				}

				DatabaseResult<IUnitOfWork> uowResult = await unitOfWorkService.BeginAsync();
				if (!uowResult.IsSuccess)
				{
					TryEnqueueMainThread(() =>
					{
						if (conn != null && conn.IsActive)
						{
							Log.Debug("CharacterSystem", "Failed to begin UnitOfWork for character load.");
							DisconnectWithNotice(conn, DisconnectNoticeReason.ServerError);
						}
					});
					// The claim is committed at this point, so it has to be handed back
					// explicitly rather than relying on a transaction rollback.
					await ReleaseHeldSessionAsync();
					return;
				}

				await using (IUnitOfWork uow = uowResult.Data)
				{
					// All fetches share the same ambient DbContext/transaction (sequential, consistent snapshot)
					if (serviceRegistry.TryGet<ICharacterInventoryService>(out var inventoryService))
					{
						var result = await inventoryService.FetchAsync(characterID);
						if (result.IsSuccess) inventoryData = result.Data;
					}
					if (serviceRegistry.TryGet<ICharacterBankService>(out var bankService))
					{
						var result = await bankService.FetchAsync(characterID);
						if (result.IsSuccess) bankData = result.Data;
					}
					if (serviceRegistry.TryGet<ICharacterEquipmentService>(out var equipmentService))
					{
						var result = await equipmentService.FetchAsync(characterID);
						if (result.IsSuccess) equipmentData = result.Data;
					}
					if (serviceRegistry.TryGet<ICharacterAttributeService>(out var attributeService))
					{
						var result = await attributeService.FetchAsync(characterID);
						if (result.IsSuccess) attributeData = result.Data;
					}
					if (serviceRegistry.TryGet<ICharacterAbilityService>(out var abilityService))
					{
						var result = await abilityService.FetchAsync(characterID);
						if (result.IsSuccess) abilityData = result.Data;
					}
					if (serviceRegistry.TryGet<ICharacterKnownAbilityService>(out var knownAbilityService))
					{
						var result = await knownAbilityService.FetchAsync(characterID);
						if (result.IsSuccess) knownAbilityData = result.Data;
					}
					if (serviceRegistry.TryGet<ICharacterAchievementService>(out var achievementService))
					{
						var result = await achievementService.FetchAsync(characterID);
						if (result.IsSuccess) achievementData = result.Data;
					}
					if (serviceRegistry.TryGet<ICharacterFriendService>(out var friendService))
					{
						var result = await friendService.FetchAsync(characterID);
						if (result.IsSuccess) friendData = result.Data;
					}
					if (serviceRegistry.TryGet<ICharacterGuildService>(out var guildService))
					{
						var result = await guildService.FetchAsync(characterID);
						if (result.IsSuccess) guildData = result.Data;
					}
					if (serviceRegistry.TryGet<ICharacterPartyService>(out var partyService))
					{
						var result = await partyService.FetchAsync(characterID);
						if (result.IsSuccess) partyData = result.Data;

						/* A party belongs to one world server, and this character may have
						 * arrived on a different one.
						 *
						 * Characters are global on purpose — the same character can be played on
						 * any world server so that friends can play together wherever they are —
						 * but a party is replicated between scene servers through the party update
						 * pump, and that pump is scoped to a world server. Carrying a membership
						 * across would produce a party half of whose members are updated by a pump
						 * the other half cannot see: rosters that never converge, invitations to
						 * instances that do not exist on the other side, and a leader nobody can
						 * reach. Every one of those is a bug that only appears once somebody
						 * changes shard, which is the worst kind to diagnose.
						 *
						 * So the membership is dropped here, at the moment of arrival, before any
						 * of it is applied to the character. Dropped rather than migrated: the
						 * party is still live on the world server it belongs to, and pulling it
						 * across would take its other members with it.
						 */
						if (partyData.HasValue &&
							partyData.Value.PartyID > 0 &&
							serviceRegistry.TryGet<IPartyService>(out var partyLookupService))
						{
							var partyResult = await partyLookupService.FetchAsync(partyData.Value.PartyID);

							if (!partyResult.IsSuccess)
							{
								/* Could not read the party. The membership is left alone: a
								 * transient database fault is not evidence that the character has
								 * changed shard, and evicting on one would break up parties for
								 * no reason. If it really is a cross-server membership it will be
								 * caught on the next load. */
								await Log.Warning("CharacterSystem",
									$"Could not read party {partyData.Value.PartyID} to check its world server for character {characterID}; leaving the membership in place.");
							}
							else if (partyResult.Data == null)
							{
								// The party is gone. Nothing to belong to.
								await DropPartyMembershipAsync(partyData.Value, "the party no longer exists");
								partyData = null;
							}
							else if (partyResult.Data.Value.WorldServerID != 0 &&
									 partyResult.Data.Value.WorldServerID != charData.WorldServerID)
							{
								await DropPartyMembershipAsync(partyData.Value,
									$"it belongs to world server {partyResult.Data.Value.WorldServerID}, not {charData.WorldServerID}");
								partyData = null;
							}
						}
					}
					if (serviceRegistry.TryGet<ICharacterHotkeyService>(out var hotkeyService))
					{
						var result = await hotkeyService.FetchAsync(characterID);
						if (result.IsSuccess) hotkeyData = result.Data;
					}
					if (serviceRegistry.TryGet<ICharacterBuffService>(out var buffService))
					{
						var result = await buffService.FetchAsync(characterID);
						if (result.IsSuccess) buffData = result.Data;
					}
					if (serviceRegistry.TryGet<ICharacterFactionService>(out var factionService))
					{
						var result = await factionService.FetchAsync(characterID);
						if (result.IsSuccess) factionData = result.Data;
					}
					if (serviceRegistry.TryGet<ICharacterQuestService>(out var questService))
					{
						var result = await questService.FetchAsync(characterID);
						if (result.IsSuccess) questData = result.Data;
					}

					// Read-only: commit to cleanly close the transaction
					DatabaseResult commitResult = await uow.CommitAsync();
					if (!commitResult.IsSuccess)
					{
						await Log.Warning("CharacterSystem", $"LoadCharacterAsync: CommitAsync DB error for character {characterID}: {commitResult.ErrorCode} - {commitResult.ErrorMessage}");
					}
				}

				// Marshal back to main thread for Unity object instantiation
				var loadContext = new CharacterLoadContext(
					conn, charData, sessionToken, serverID,
					instanceSceneName, instanceSceneHandle,
					inventoryData, bankData, equipmentData,
					attributeData, abilityData, knownAbilityData,
					achievementData, friendData,
					guildData, partyData,
					hotkeyData, buffData, factionData, questData);
				/* The main-thread queue is bounded and drops work when it is saturated, and this
				 * hand-off is the only thing that installs the claim in SessionTokens. A dropped
				 * item therefore left the character Online with nothing left holding its token —
				 * the exact stranded-claim state every other path here goes out of its way to
				 * avoid — so the destination scene server kicked the player on every retry until
				 * the lease expired. */
				if (!TryEnqueueMainThread(() => InstantiateAndLoadCharacter(loadContext)))
				{
					await Log.Error("CharacterSystem",
						$"Main-thread queue rejected the character spawn for {charData.ID}; releasing its session.");
					await ReleaseHeldSessionAsync();
					TryEnqueueMainThread(() => DisconnectWithNotice(conn, DisconnectNoticeReason.ServerError));
				}
			}
			catch (Exception ex)
			{
				await Log.Error("CharacterSystem", $"LoadCharacterAsync failed: {ex}");

				// If a session is held — claimed here, or handed over by a combat-logout
				// reattach — it has to go back before this load is abandoned.
				if (sessionToken != Guid.Empty && sessionCharacterID > 0)
				{
					try
					{
						await ReleaseHeldSessionAsync();
					}
					catch (Exception releaseEx)
					{
						await Log.Error("CharacterSystem", $"LoadCharacterAsync: Failed to release session after error for character {sessionCharacterID}: {releaseEx.Message}");
					}
				}

				TryEnqueueMainThread(() => DisconnectWithNotice(conn, DisconnectNoticeReason.ServerError));
			}
		}

		/// <summary>
		/// Resolves the Unity scene a character belongs to.
		/// </summary>
		/// <remarks>
		/// The character names its scene instance by row ID, which is the only identifier that
		/// means the same thing on every scene server; the scene manager understands only this
		/// process's own handle. This is where the two meet, and going through the scene server's
		/// instance registry to do it also verifies the instance is one this server actually
		/// hosts — asking the scene manager directly would silently accept whatever local scene
		/// happened to share the number.
		/// </remarks>
		/// <param name="character">Character whose scene to resolve.</param>
		/// <param name="scene">The resolved scene, or default when it is not hosted here.</param>
		/// <returns><c>true</c> when the character's scene instance is loaded on this server.</returns>
		private bool TryGetCharacterScene(IPlayerCharacter character, out Scene scene)
		{
			scene = default;

			if (character == null ||
				!Server.BehaviourRegistry.TryGet(out ISceneServerSystem<NetworkConnection> sceneServerSystem))
			{
				return false;
			}

			bool inInstance = character.IsInInstance();
			string sceneName = inInstance ? character.InstanceSceneName : character.SceneName;
			long sceneID = inInstance ? character.InstanceSceneHandle : character.SceneHandle;

			if (!sceneServerSystem.TryGetSceneInstanceDetails(character.WorldServerID, sceneName, sceneID, out ISceneInstanceDetails details))
			{
				return false;
			}

			scene = SceneManager.GetScene(details.Handle);
			return true;
		}

		/// <summary>
		/// Replaces an unusable stored rotation with identity.
		/// </summary>
		/// <remarks>
		/// A zero quaternion is not a rotation. Unity treats it as invalid and it propagates NaN
		/// through the motor, which then reports a position of NaN and fails every bounds test
		/// forever. Character rows written before a given rotation column existed hold exactly
		/// that value, so it has to be normalised on the way in rather than trusted.
		/// </remarks>
		private static Quaternion NormalizeRotation(Quaternion rotation)
		{
			float sqrMagnitude = (rotation.x * rotation.x) + (rotation.y * rotation.y) +
								 (rotation.z * rotation.z) + (rotation.w * rotation.w);

			// Also rejects NaN: every comparison against NaN is false, so this test fails and
			// identity is returned.
			return sqrMagnitude > 1e-6f ? rotation : Quaternion.identity;
		}

		#region Instance Scene Resolution

		/// <summary>How an instance lookup for a character turned out.</summary>
		private enum InstanceResolutionOutcome : byte
		{
			/// <summary>The instance is ready and hosted by this scene server.</summary>
			Resolved = 0,
			/// <summary>The instance is ready, but another scene server hosts it.</summary>
			HostedElsewhere = 1,
			/// <summary>The instance cannot be entered and the character must be released from it.</summary>
			Unavailable = 2,
		}

		/// <summary>Result of resolving a character's <c>InstanceID</c> to a live scene instance.</summary>
		private readonly struct InstanceResolution
		{
			/// <summary>What the lookup concluded.</summary>
			public readonly InstanceResolutionOutcome Outcome;
			/// <summary>Scene name of the instance, when <see cref="Outcome"/> is <see cref="InstanceResolutionOutcome.Resolved"/>.</summary>
			public readonly string SceneName;
			/// <summary>Scene row ID of the instance, when resolved.</summary>
			public readonly long SceneHandle;
			/// <summary>Scene server that hosts the instance, when it is hosted elsewhere.</summary>
			public readonly long HostSceneServerID;
			/// <summary>Why the instance is unavailable, for diagnostics.</summary>
			public readonly string Reason;

			private InstanceResolution(InstanceResolutionOutcome outcome, string sceneName, long sceneHandle, long hostSceneServerID, string reason)
			{
				Outcome = outcome;
				SceneName = sceneName;
				SceneHandle = sceneHandle;
				HostSceneServerID = hostSceneServerID;
				Reason = reason;
			}

			/// <summary>The instance is ready here.</summary>
			public static InstanceResolution Resolved(string sceneName, long sceneHandle)
				=> new InstanceResolution(InstanceResolutionOutcome.Resolved, sceneName, sceneHandle, 0, null);

			/// <summary>The instance is ready on another scene server.</summary>
			public static InstanceResolution HostedElsewhere(long hostSceneServerID)
				=> new InstanceResolution(InstanceResolutionOutcome.HostedElsewhere, null, 0, hostSceneServerID, null);

			/// <summary>The instance cannot be entered.</summary>
			public static InstanceResolution Unavailable(string reason)
				=> new InstanceResolution(InstanceResolutionOutcome.Unavailable, null, 0, 0, reason);
		}

		/// <summary>
		/// Resolves a character's <c>InstanceID</c> to the scene name and runtime handle of the
		/// instance it should be loaded into.
		/// </summary>
		/// <remarks>
		/// Every condition that is not "a ready instance of this world, hosted by this server,
		/// whose scene is actually loaded here" resolves to
		/// <see cref="InstanceResolutionOutcome.Unavailable"/>. That is deliberate: the caller
		/// responds by releasing the character from the instance, and a wrong answer in the
		/// other direction is what makes a character permanently unloadable. The scene row is
		/// checked against the locally loaded scene as well as against the database, because the
		/// row can outlive the scene it describes if a scene server unloads a scene and the
		/// delete has not landed yet.
		/// </remarks>
		/// <param name="charData">The character being loaded.</param>
		/// <param name="serverID">This scene server's database ID.</param>
		private async Task<InstanceResolution> ResolveInstanceSceneAsync(CharacterData charData, long serverID)
		{
			if (charData.InstanceID <= 0)
			{
				return InstanceResolution.Unavailable("the character carries the instance flag but no instance ID");
			}

			if (Server?.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<ISceneService>(out var sceneService))
			{
				return InstanceResolution.Unavailable("the scene service is unavailable");
			}

			DatabaseResult<SceneData> sceneResult = await sceneService.FetchAsync(charData.InstanceID);
			if (!sceneResult.IsSuccess)
			{
				// Includes the row having been reaped by the world server's stale-scene sweep,
				// which is the ordinary end state for an instance that never became ready.
				return InstanceResolution.Unavailable($"instance scene row {charData.InstanceID} could not be read: {sceneResult.ErrorCode}");
			}

			SceneData sceneData = sceneResult.Data;

			if ((SceneStatus)sceneData.SceneStatus != SceneStatus.Ready)
			{
				return InstanceResolution.Unavailable($"instance scene {charData.InstanceID} is {(SceneStatus)sceneData.SceneStatus}, not Ready");
			}

			if (sceneData.WorldServerID != charData.WorldServerID)
			{
				return InstanceResolution.Unavailable(
					$"instance scene {charData.InstanceID} belongs to world server {sceneData.WorldServerID}, not {charData.WorldServerID}");
			}

			if (sceneData.SceneServerID != serverID)
			{
				/* Being sent to the wrong scene server is normally transient — the world server
				 * routes off this same row and will get it right — but it is only transient if
				 * the server the row names is actually alive. When it is not, the world server
				 * keeps resolving its stale registration to an address that now answers as
				 * somebody else (a restarted scene server reusing the port is the ordinary way
				 * this happens), the client is sent here again, and refusing again produces an
				 * unbounded redirect loop with no clock on it: the row is Ready and its age says
				 * nothing, so none of the other bounds apply.
				 *
				 * Liveness is the thing that distinguishes the two cases, so ask. A host that has
				 * stopped pulsing is gone, and with it the instance, whatever the row still says.
				 */
				if (await IsSceneServerLiveAsync(sceneData.SceneServerID))
				{
					return InstanceResolution.HostedElsewhere(sceneData.SceneServerID);
				}

				return InstanceResolution.Unavailable(
					$"instance scene {charData.InstanceID} is registered to scene server {sceneData.SceneServerID}, which is no longer pulsing");
			}

			// The row says this server hosts it; confirm the scene is genuinely loaded here
			// before committing the character to it. Main-thread state, so read it there.
			bool present = false;
			string sceneName = sceneData.SceneName;
			long instanceSceneID = sceneData.ID;

			if (!await RunOnMainThreadAsync(() =>
			{
				present = Server.BehaviourRegistry.TryGet(out ISceneServerSystem<NetworkConnection> sceneServerSystem) &&
					sceneServerSystem.TryGetSceneInstanceDetails(sceneData.WorldServerID, sceneName, instanceSceneID, out _);
			}))
			{
				return InstanceResolution.Unavailable("the main-thread queue could not confirm the instance scene is loaded");
			}

			if (!present)
			{
				return InstanceResolution.Unavailable(
					$"instance scene {sceneName} (SceneID={instanceSceneID}) is recorded against this server but is not loaded here");
			}

			return InstanceResolution.Resolved(sceneName, instanceSceneID);
		}

		/// <summary>
		/// How long a scene server may go without pulsing before it is presumed dead.
		/// </summary>
		/// <remarks>
		/// An order of magnitude above the default 5s pulse rate, so a slow pulse or a brief
		/// database stall is never mistaken for a dead server, and well under the two-minute
		/// session lease so a conclusion drawn here cannot outlive the claim it reasons about.
		/// </remarks>
		private static readonly TimeSpan SceneServerPulseStaleAfter = TimeSpan.FromSeconds(60);

		/// <summary>
		/// Whether the named scene server is still registered and pulsing.
		/// </summary>
		/// <remarks>
		/// A crashed scene server leaves its registration behind — the row is only deleted on a
		/// graceful shutdown — so existence alone proves nothing and the pulse timestamp is what
		/// has to be read.
		/// </remarks>
		/// <param name="sceneServerID">Scene server to check.</param>
		private async Task<bool> IsSceneServerLiveAsync(long sceneServerID)
		{
			if (sceneServerID <= 0 ||
				Server?.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<ISceneServerService>(out var sceneServerService))
			{
				// Cannot tell. Treat as live so a lookup failure never evicts a character from a
				// dungeon that is running perfectly well; the caller's other bounds still apply.
				return true;
			}

			DatabaseResult<SceneServerData> result = await sceneServerService.FetchAsync(sceneServerID);
			if (!result.IsSuccess)
			{
				// The registration is gone, so the server shut down cleanly and took its scenes.
				return false;
			}

			return (DateTime.UtcNow - result.Data.LastPulse) < SceneServerPulseStaleAfter;
		}

		/// <summary>
		/// Runs <paramref name="action"/> on the main thread and waits for it to finish.
		/// </summary>
		/// <remarks>
		/// Bounded rather than open-ended: a queued action only runs while this behaviour is
		/// being updated, so anything enqueued after deinitialization would never run and an
		/// awaiting worker would hold an async-worker slot for the life of the process. Reaching
		/// the timeout means the main-thread queue is not draining at all, and the caller's own
		/// failure path is the correct response. Mirrors
		/// <c>WorldSceneSystem.RunOnMainThreadAsync</c>.
		/// </remarks>
		/// <returns><c>false</c> when the action could not be queued or was never drained.</returns>
		private async Task<bool> RunOnMainThreadAsync(Action action)
		{
			const int MainThreadDispatchTimeoutMs = 30_000;

			var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
			if (!TryEnqueueMainThread(() =>
			{
				try
				{
					action();
					tcs.TrySetResult(true);
				}
				catch (Exception ex)
				{
					tcs.TrySetException(ex);
				}
			}))
			{
				return false;
			}

			using (var timeoutCts = new System.Threading.CancellationTokenSource())
			{
				Task timeoutTask = Task.Delay(MainThreadDispatchTimeoutMs, timeoutCts.Token);
				Task completed = await Task.WhenAny(tcs.Task, timeoutTask);
				if (!ReferenceEquals(completed, tcs.Task))
				{
					await Log.Error("CharacterSystem",
						$"Main-thread dispatch was not drained within {MainThreadDispatchTimeoutMs}ms; abandoning the operation.");
					return false;
				}
				timeoutCts.Cancel();
			}

			// Awaited rather than returned so an exception thrown by the action surfaces to the
			// caller's try/catch.
			return await tcs.Task;
		}

		#endregion

		/// <summary>
		/// Instantiates the player character from CharacterData, populates all controllers
		/// with pre-fetched sub-entity data, and initiates scene loading.
		/// Must be called on the main Unity thread.
		/// </summary>
		/// <param name="ctx">Bundled character data, session info, and all sub-entity data for loading.</param>
		private void InstantiateAndLoadCharacter(CharacterLoadContext ctx)
		{
			NetworkConnection conn = ctx.Connection;
			CharacterData charData = ctx.CharacterData;
			Guid sessionToken = ctx.SessionToken;
			long serverID = ctx.ServerID;
			IReadOnlyList<CharacterInventoryData> inventoryData = ctx.InventoryData;
			IReadOnlyList<CharacterBankData> bankData = ctx.BankData;
			IReadOnlyList<CharacterEquipmentData> equipmentData = ctx.EquipmentData;
			IReadOnlyList<CharacterAttributeData> attributeData = ctx.AttributeData;
			IReadOnlyList<CharacterAbilityData> abilityData = ctx.AbilityData;
			IReadOnlyList<CharacterKnownAbilityData> knownAbilityData = ctx.KnownAbilityData;
			IReadOnlyList<CharacterAchievementData> achievementData = ctx.AchievementData;
			IReadOnlyList<CharacterFriendData> friendData = ctx.FriendData;
			CharacterGuildData? guildData = ctx.GuildData;
			CharacterPartyData? partyData = ctx.PartyData;
			IReadOnlyList<CharacterHotkeyData> hotkeyData = ctx.HotkeyData;
			IReadOnlyList<CharacterBuffData> buffData = ctx.BuffData;
			IReadOnlyList<CharacterFactionData> factionData = ctx.FactionData;
			IReadOnlyList<CharacterQuestData> questData = ctx.QuestData;

			if (conn == null || !conn.IsActive)
			{
				// Connection died between async load and main-thread marshal — release the claimed session
				ReleaseSessionSafely(charData.ID, serverID, sessionToken);
				return;
			}

			if (!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData))
			{
				ReleaseSessionSafely(charData.ID, serverID, sessionToken);
				DisconnectWithNotice(conn, DisconnectNoticeReason.ServerError);
				return;
			}

			if (mappingData.CharactersByID.ContainsKey(charData.ID))
			{
				Log.Debug("CharacterSystem", $"{charData.ID} is already loaded or loading.");
				ReleaseSessionSafely(charData.ID, serverID, sessionToken);
				// Another session on this server already holds the character. Retrying is the
				// right answer once that one finishes tearing down.
				DisconnectWithNotice(conn, DisconnectNoticeReason.SessionSuperseded);
				return;
			}

			if (!Server.BehaviourRegistry.TryGet(out ISceneServerSystem<NetworkConnection> sceneServerSystem))
			{
				ReleaseSessionSafely(charData.ID, serverID, sessionToken);
				DisconnectWithNotice(conn, DisconnectNoticeReason.ServerError);
				return;
			}

			OnBeforeLoadCharacter?.Invoke(conn, charData.ID);

			// Look up the race template and instantiate the character prefab
			RaceTemplate raceTemplate = RaceTemplate.Get<RaceTemplate>(charData.RaceID);
			if (raceTemplate == null || raceTemplate.Prefab == null)
			{
				Log.Debug("CharacterSystem", "Failed to fetch character: invalid race template.");
				ReleaseSessionSafely(charData.ID, serverID, sessionToken);
				// A bad race template will be just as bad on the next attempt.
				DisconnectWithNotice(conn, DisconnectNoticeReason.CharacterUnavailable, terminal: true);
				return;
			}

			/* An instanced character does not spawn at its open-world coordinates.
			 *
			 * X/Y/Z is where the character stands in the world scene it will return to; the
			 * dungeon's own entry point is InstanceX/Y/Z, written by the dungeon finder from the
			 * destination scene's respawn points. Using the world position dropped players at
			 * whatever coordinates they happened to be standing on outside, which inside a
			 * dungeon geometry is arbitrary — through the floor as often as not. */
			bool enteringInstance = !string.IsNullOrWhiteSpace(ctx.InstanceSceneName);

			Vector3 worldPosition = new Vector3(charData.X, charData.Y, charData.Z);
			Quaternion worldRotation = NormalizeRotation(
				new Quaternion(charData.RotX, charData.RotY, charData.RotZ, charData.RotW));

			Vector3 position = enteringInstance
				? new Vector3(charData.InstanceX, charData.InstanceY, charData.InstanceZ)
				: worldPosition;
			Quaternion rotation = enteringInstance
				? NormalizeRotation(new Quaternion(charData.InstanceRotX, charData.InstanceRotY, charData.InstanceRotZ, charData.InstanceRotW))
				: worldRotation;

			NetworkObject nob = Server.NetworkWrapper.NetworkManager.GetPooledInstantiated(
				raceTemplate.Prefab, position, rotation, true);
			if (nob == null)
			{
				Log.Debug("CharacterSystem", "Failed to instantiate character prefab.");
				ReleaseSessionSafely(charData.ID, serverID, sessionToken);
				DisconnectWithNotice(conn, DisconnectNoticeReason.ServerError);
				return;
			}

			IPlayerCharacter character = nob.GetComponent<IPlayerCharacter>();
			if (character == null)
			{
				Server.NetworkWrapper.NetworkManager.StorePooledInstantiated(nob, true);
				Log.Debug("CharacterSystem", "Failed to get IPlayerCharacter from instantiated prefab.");
				ReleaseSessionSafely(charData.ID, serverID, sessionToken);
				DisconnectWithNotice(conn, DisconnectNoticeReason.CharacterUnavailable, terminal: true);
				return;
			}

			// Initialize motor at the spawn position to match the teleport path.
			// Without this, the motor's internal velocity/tick/grounding state is uninitialized
			// and the first tick may miss ground detection with the 0.005f minimum probe.
			character.Motor.SetPositionAndRotationAndVelocity(position, rotation, Vector3.zero);

			// Populate character fields from CharacterData
			character.ID = charData.ID;
			character.CharacterName = charData.Name;
			character.CharacterNameLower = charData.NameLowercase;
			character.Account = charData.Account;
			character.Version = charData.Version;
			character.WorldServerID = charData.WorldServerID;
			character.AccessLevel = (AccessLevel)(int)charData.AccessLevel;
			character.TimeCreated = charData.TimeCreated;
			character.RaceID = charData.RaceID;
			character.ModelIndex = charData.ModelIndex;
			character.SceneName = charData.SceneName;
			character.SceneHandle = charData.SceneHandle;
			character.BindScene = charData.BindScene;
			character.BindPosition = new Vector3(charData.BindX, charData.BindY, charData.BindZ);
			character.InstanceID = charData.InstanceID;
			character.Flags = charData.Flags;

			/* The instance's scene name and handle, resolved from the scene row during the load.
			 * IPlayerCharacter.IsInInstance() is false without the name, so every instance-aware
			 * decision below — which scene to load, which scene population to credit, which
			 * scene's teleporters apply — depends on this assignment. */
			character.InstanceSceneName = ctx.InstanceSceneName;
			character.InstanceSceneHandle = ctx.InstanceSceneHandle;

			/* Where the character goes when it leaves an instance, carried in memory because the
			 * transform can only describe one of the two places at a time. See
			 * BuildCharacterData. Populated unconditionally so it is already correct for a
			 * character that enters an instance later in this session. */
			character.LastWorldPosition = worldPosition;
			character.LastWorldRotation = worldRotation;

			// Combat is transient state — never persist or restore across sessions.
			// The save path also strips this flag, but sanitize on load for defense-in-depth.
			character.DisableFlags(CharacterFlags.IsInCombat);

			// The character is being loaded into a live session, so by definition it is no
			// longer an unattended body. This is not merely tidiness: IsCombatLogged is what
			// makes AnyOnlineAsync skip a character, so a row that kept the flag — which is
			// exactly what a scene server crash mid-linger leaves behind — would let the account
			// log in a second time while this session is still running. Clearing it here bounds
			// that to the crash itself rather than to every session afterwards.
			character.DisableFlags(CharacterFlags.IsCombatLogged);

			if (enteringInstance)
			{
				// Kept in sync with the transform the character was actually placed at, so the
				// save path round-trips the entry point rather than the stored-but-unused value.
				character.InstancePosition = position;
				character.InstanceRotation = rotation;
			}

			// --- Populate all controllers from pre-fetched DB data ---

			// Attributes
			if (attributeData != null && attributeData.Count > 0 &&
				character.TryGet(out ICharacterAttributeController attrController))
			{
				foreach (CharacterAttributeData attr in attributeData)
				{
					// Use the template to determine if this is a resource attribute,
					// NOT CurrentValue. A resource with CurrentValue == 0 (dead character,
					// empty mana) is still a resource and must go into the ResourceAttributes
					// dictionary. Using CurrentValue > 0 puts dead-player health into the
					// base-attribute dictionary, leaving the real resource at Version=0,
					// which causes STALE_STATE on every save.
					CharacterAttributeTemplate template = CharacterAttributeTemplate.Get<CharacterAttributeTemplate>(attr.TemplateID);
					if (template != null && template.IsResourceAttribute)
					{
						attrController.SetResourceAttribute(attr.TemplateID, attr.Value, attr.CurrentValue, null);
						if (attrController.ResourceAttributes.TryGetValue(attr.TemplateID, out var resAttr))
						{
							resAttr.Version = attr.Version;
						}
					}
					else
					{
						attrController.SetAttribute(attr.TemplateID, attr.Value);
						if (attrController.Attributes.TryGetValue(attr.TemplateID, out var charAttr))
						{
							charAttr.Version = attr.Version;
						}
					}
				}
			}

			// Inventory
			if (inventoryData != null && inventoryData.Count > 0 &&
				character.TryGet(out IInventoryController inventoryController))
			{
				foreach (CharacterInventoryData inv in inventoryData)
				{
					Item item = new Item(inv.ID, inv.Seed, inv.TemplateID, inv.Amount);
					item.Version = inv.Version;
					/* The return value is not a formality. SetItemSlot refuses any slot outside the
					 * container, and a refusal here means the item is in no container at all — at
					 * which point memory is authoritative, the next snapshot prunes the row it came
					 * from, and the item is destroyed for good. Relocating a row we cannot place
					 * where it claims to belong is strictly better than dropping it silently. */
					if (!inventoryController.SetItemSlot(item, inv.Slot))
					{
						if (inventoryController.TryAddItem(item, out _))
						{
							Log.Warning("CharacterSystem", $"Character {character.ID}: inventory slot {inv.Slot} is out of range; relocated item {inv.ID} to the first free slot.");
						}
						else
						{
							Log.Error("CharacterSystem", $"Character {character.ID}: could not place inventory item {inv.ID} from slot {inv.Slot}; it will be pruned by the next snapshot.");
						}
					}
				}
			}

			// Bank
			if (bankData != null && bankData.Count > 0 &&
				character.TryGet(out IBankController bankController))
			{
				foreach (CharacterBankData bank in bankData)
				{
					Item item = new Item(bank.ID, bank.Seed, bank.TemplateID, bank.Amount);
					item.Version = bank.Version;
					// Same reasoning as the inventory load above.
					if (!bankController.SetItemSlot(item, bank.Slot))
					{
						if (bankController.TryAddItem(item, out _))
						{
							Log.Warning("CharacterSystem", $"Character {character.ID}: bank slot {bank.Slot} is out of range; relocated item {bank.ID} to the first free slot.");
						}
						else
						{
							Log.Error("CharacterSystem", $"Character {character.ID}: could not place bank item {bank.ID} from slot {bank.Slot}; it will be pruned by the next snapshot.");
						}
					}
				}
			}

			// Equipment
			if (equipmentData != null && equipmentData.Count > 0 &&
				character.TryGet(out IEquipmentController equipmentController))
			{
				foreach (CharacterEquipmentData equip in equipmentData)
				{
					Item item = new Item(equip.ID, equip.Seed, equip.TemplateID, equip.Amount);
					item.Version = equip.Version;
					/* Equipment sockets are typed, so an out-of-range slot is corrupt data rather
					 * than a resizing artefact and there is no sensible slot to relocate to. Report
					 * it loudly — silently dropping it lets the next snapshot delete the row. */
					if (!equipmentController.SetItemSlot(item, equip.Slot))
					{
						Log.Error("CharacterSystem", $"Character {character.ID}: equipment slot {equip.Slot} is not a valid socket; item {equip.ID} could not be loaded and will be pruned by the next snapshot.");
						continue;
					}

					// Apply equipment attribute modifiers via externalModifier
					if (item.IsEquippable)
					{
						item.Equippable.Equip(character);
					}
				}
			}

			// Abilities (crafted ability instances)
			if (abilityData != null && abilityData.Count > 0 &&
				character.TryGet(out IAbilityController abilityController))
			{
				foreach (CharacterAbilityData ability in abilityData)
				{
					Ability newAbility = new Ability(ability.ID, ability.TemplateID, ability.AbilityEvents);
					newAbility.Version = ability.Version;
					abilityController.LearnAbility(newAbility, ability.Cooldown);
				}

				// Known base abilities
				if (knownAbilityData != null && knownAbilityData.Count > 0)
				{
					List<BaseAbilityTemplate> knownTemplates = new List<BaseAbilityTemplate>();
					foreach (CharacterKnownAbilityData known in knownAbilityData)
					{
						BaseAbilityTemplate template = BaseAbilityTemplate.Get<BaseAbilityTemplate>(known.TemplateID);
						if (template != null)
						{
							knownTemplates.Add(template);
						}
					}
					if (knownTemplates.Count > 0)
					{
						abilityController.LearnBaseAbilities(knownTemplates);
					}
				}
			}

			// Achievements
			if (achievementData != null && achievementData.Count > 0 &&
				character.TryGet(out IAchievementController achievementController))
			{
				foreach (CharacterAchievementData achievement in achievementData)
				{
					achievementController.SetAchievement(achievement.TemplateID, achievement.Tier, achievement.Value, true);
					if (achievementController.Achievements.TryGetValue(achievement.TemplateID, out Achievement ach))
					{
						ach.Version = achievement.Version;
					}
				}
			}

			// Friends
			if (friendData != null && friendData.Count > 0 &&
				character.TryGet(out IFriendController friendController))
			{
				foreach (CharacterFriendData friend in friendData)
				{
					friendController.AddFriend(friend.FriendCharacterID);
				}
			}

			// Guild
			if (guildData.HasValue &&
				character.TryGet(out IGuildController guildController))
			{
				guildController.ID = guildData.Value.GuildID;
				/* The ladder POSITION, as stored. What that position is allowed to do is not
				 * knowable from the membership row alone — it depends on the guild's own rank
				 * rows — so the permission mask stays empty until GuildSystem resolves and
				 * publishes it. Empty is the safe direction: the pre-filters that read this mask
				 * refuse, and every guild operation re-resolves before acting anyway. */
				guildController.RankOrder = guildData.Value.Rank;
				guildController.Permissions = GuildPermissions.None;
			}

			// Party
			if (partyData.HasValue &&
				character.TryGet(out IPartyController partyController))
			{
				partyController.ID = partyData.Value.PartyID;
				partyController.Rank = (PartyRank)partyData.Value.Rank;
			}

			// Hotkeys
			if (hotkeyData != null && hotkeyData.Count > 0)
			{
				foreach (CharacterHotkeyData hotkey in hotkeyData)
				{
					if (hotkey.Slot >= 0 && hotkey.Slot < character.Hotkeys.Count)
					{
						character.Hotkeys[hotkey.Slot] = new HotkeyData()
						{
							Type = hotkey.Type,
							Slot = hotkey.Slot,
							ReferenceID = hotkey.ReferenceID,
						};
					}
				}
			}

			// Buffs
			if (buffData != null && buffData.Count > 0 &&
				character.TryGet(out IBuffController buffController))
			{
				float loadTickDelta = (float)Server.NetworkWrapper.NetworkManager.TimeManager.TickDelta;
				uint loadCurrentTick = Server.NetworkWrapper.NetworkManager.TimeManager.LocalTick;
				foreach (CharacterBuffData buff in buffData)
				{
					uint expiryTick = loadCurrentTick + (uint)Math.Max(1.0, Math.Ceiling(buff.RemainingTime / loadTickDelta));
					uint nextTickTick = loadCurrentTick + (uint)Math.Max(1.0, Math.Ceiling(buff.TickTime / loadTickDelta));
					Buff newBuff = new Buff(buff.TemplateID, expiryTick, nextTickTick, loadTickDelta, buff.Stacks, buff.TickCount);
					newBuff.Version = buff.Version;
					buffController.Apply(newBuff);
				}
			}

			// Factions
			if (factionData != null && factionData.Count > 0 &&
				character.TryGet(out IFactionController factionController))
			{
				foreach (CharacterFactionData faction in factionData)
				{
					factionController.SetFaction(faction.TemplateID, faction.Value, true);
					if (factionController.Factions.TryGetValue(faction.TemplateID, out Faction fac))
					{
						fac.Version = faction.Version;
					}
				}
			}

			// Quests
			if (questData != null && questData.Count > 0 &&
				character.TryGet(out IQuestController questController))
			{
				foreach (CharacterQuestData quest in questData)
				{
					QuestTemplate questTemplate = QuestTemplate.Get<QuestTemplate>(quest.TemplateID);
					if (questTemplate == null)
					{
						continue;
					}

					long[] objectiveValues = ParseObjectiveValues(quest.ObjectiveValues);
					questController.SetQuest(questTemplate, (QuestStatus)quest.Status, objectiveValues);
				}
			}

			string sceneName = character.SceneName;
			long sceneID = character.SceneHandle;

			// Check if the character is in an instance or not.
			if (character.IsInInstance())
			{
				// Have the player enter the instance.
				sceneName = character.InstanceSceneName;
				sceneID = character.InstanceSceneHandle;
			}

			// Check if the scene is valid, loaded, and cached properly
			if (sceneServerSystem.TryGetSceneInstanceDetails(character.WorldServerID, sceneName, sceneID, out ISceneInstanceDetails instance) &&
				sceneServerSystem.TryLoadSceneForConnection(conn, instance))
			{
				OnAfterLoadCharacter?.Invoke(conn, character);

				// Store the session token now that we've reached the success path.
				// From here, RemoveCharacterConnectionMapping / SceneManager_OnClientLoadedStartScenes
				// will handle cleanup via the SessionTokens dictionary.
				mappingData.SessionTokens[charData.ID] = new CharacterSessionInfo(sessionToken, serverID);
				mappingData.WaitingSceneLoadCharacters.Add(conn, character);
				sceneLoadDeadlines[conn.ClientId] = DateTime.UtcNow + SceneLoadHandshakeTimeout;

				// The client has usually acknowledged its start scenes long before this load
				// finished — that acknowledgement is never repeated, so this is the point at
				// which the handshake has to proceed. See startScenesAckedClientIds.
				if (startScenesAckedClientIds.ContainsKey(conn.ClientId))
				{
					ValidateSceneAndAcknowledge(mappingData, conn, character);
				}
			}
			else
			{
				Log.Debug("CharacterSystem", "Failed to load scene for connection.");

				ReleaseSessionSafely(charData.ID, serverID, sessionToken);

				Server.NetworkWrapper.NetworkManager.StorePooledInstantiated(nob, true);
				// The instance this character belongs to is not present on this server. The
				// world server will route the retry to one that has it.
				DisconnectWithNotice(conn, DisconnectNoticeReason.SceneUnavailable);
			}
		}

		/// <summary>
		/// Connections that have acknowledged their start scenes, keyed by ClientId.
		/// </summary>
		/// <remarks>
		/// FishNet raises <c>OnClientLoadedStartScenes</c> exactly once per connection, and it
		/// raises it early: the server solicits that acknowledgement from inside
		/// <c>ServerManager.ClientAuthenticated</c>, which runs immediately <em>before</em> the
		/// authenticator's own result event starts the character load. With no global scenes
		/// configured the acknowledgement is one round trip away, while the load is several
		/// database round trips plus a main-thread queue drain — so the acknowledgement normally
		/// arrives first, and it never comes again.
		/// <para>
		/// Keying the whole scene handshake off that single event therefore made world entry a
		/// race with two losing outcomes: an acknowledgement that arrived before the character
		/// was registered found nothing waiting and disconnected a perfectly healthy login, and
		/// a load that finished afterwards had no event left to drive it, so the client sat in
		/// <c>WaitingSceneLoadCharacters</c> holding its session claim until the 90s handshake
		/// timeout. Recording the acknowledgement makes the order irrelevant: whichever of the
		/// two lands second runs <see cref="ValidateSceneAndAcknowledge"/>, and exactly one of
		/// them does, because both run on the main thread.
		/// </para>
		/// <para>
		/// Cleared when the connection stops. A ClientId is reused, and a stale entry would make
		/// the next session on that id believe a handshake it never performed had completed.
		/// </para>
		/// </remarks>
		private readonly ConcurrentDictionary<int, byte> startScenesAckedClientIds =
			new ConcurrentDictionary<int, byte>();

		/// <summary>
		/// Called when a client acknowledges the start scenes it was sent on authentication.
		/// </summary>
		/// <param name="conn">Network connection of the client.</param>
		/// <param name="asServer">True if loaded as server.</param>
		private void SceneManager_OnClientLoadedStartScenes(NetworkConnection conn, bool asServer)
		{
			if (conn == null)
			{
				return;
			}

			startScenesAckedClientIds[conn.ClientId] = 0;

			/* No character yet is the ordinary case, not an error.
			 *
			 * This used to disconnect, which meant the server kicked a client for completing the
			 * very first step of world entry on schedule. The character load is still in flight;
			 * it will call ValidateSceneAndAcknowledge itself when it registers, having seen the
			 * marker set above. A load that never arrives is already covered by the residency
			 * watchdog, which is what bounds this window. */
			if (!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData) ||
				!mappingData.WaitingSceneLoadCharacters.TryGetValue(conn, out IPlayerCharacter character))
			{
				return;
			}

			ValidateSceneAndAcknowledge(mappingData, conn, character);
		}

		/// <summary>
		/// Confirms the character's scene is genuinely loaded on this server and tells the client
		/// it may begin its own world preload.
		/// </summary>
		/// <remarks>
		/// Runs exactly once per connection, driven by whichever of the start-scene
		/// acknowledgement and the character registration happens second. Both are main-thread
		/// only, so the two cannot interleave and the acknowledgement cannot be sent twice.
		/// </remarks>
		/// <param name="mappingData">Character mapping data.</param>
		/// <param name="conn">Connection whose scene is being validated.</param>
		/// <param name="character">The character waiting to be spawned.</param>
		private void ValidateSceneAndAcknowledge(
			ICharacterMappingData<NetworkConnection> mappingData,
			NetworkConnection conn,
			IPlayerCharacter character)
		{
			// Get the characters scene
			bool resolvedScene = TryGetCharacterScene(character, out Scene scene);

			// Validate the characters scene.
			if (!resolvedScene ||
				!scene.IsValid() ||
				!scene.isLoaded)
			{
				Log.Debug("CharacterSystem", "Scene is not valid.");

				mappingData.WaitingSceneLoadCharacters.Remove(conn);
				sceneLoadDeadlines.TryRemove(conn.ClientId, out _);

				/* Announce the departure, exactly as the ordinary teardown does.
				 *
				 * OnAfterLoadCharacter has already credited this character to its scene
				 * instance's population, and every path that removes a character debits it again
				 * by raising OnDisconnect. This branch raised nothing: taking the character out
				 * of WaitingSceneLoadCharacters first means the disconnect below reaches
				 * RemoveCharacterConnectionMapping with the connection in neither map, so that
				 * returns immediately. The instance was left permanently over-populated — never
				 * stale, therefore never unloaded, advertising less capacity than it has, and
				 * eventually reading as full while standing empty. */
				DispatchCharacterEvent(OnDisconnect, conn, character, nameof(OnDisconnect));

				TryExtractAndReleaseSession(mappingData, character.ID);

				Server.NetworkWrapper.NetworkManager.StorePooledInstantiated(character.NetworkObject, true);
				DisconnectWithNotice(conn, DisconnectNoticeReason.SceneUnavailable);
				return;
			}

			Server.NetworkWrapper.Broadcast(conn, new ClientValidatedSceneBroadcast(), true, Channel.Reliable);
		}

		/// <summary>
		/// Called when a client completely finishes loading into a world scene. Spawns character and sets online status.
		/// </summary>
		/// <param name="conn">Network connection of the client.</param>
		/// <param name="msg">ClientValidatedSceneBroadcast message.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		private void OnClientValidatedSceneBroadcastReceived(NetworkConnection conn, ClientValidatedSceneBroadcast msg, Channel channel)
		{
			if (!Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData))
			{
				return;
			}

			/* Rate-limit only the acknowledgement that actually does work.
			 *
			 * Stamping the limiter before checking WaitingSceneLoadCharacters meant any earlier
			 * broadcast on this connection — a duplicate from a client that acknowledged twice,
			 * or a stale acknowledgement left over from a previous session on a recycled
			 * ClientId — silently swallowed the real one that arrived within the cooldown. The
			 * connection then sat in WaitingSceneLoadCharacters holding its character claim
			 * until the 90s handshake timeout disconnected it, which the player experiences as
			 * a loading screen that never ends. A connection with nothing waiting reaches only
			 * a dictionary lookup here, so leaving it unlimited costs nothing.
			 *
			 * Same ordering rule as OnClientScenesUnloadedBroadcastReceived below. */
			if (mappingData.WaitingSceneLoadCharacters.TryGetValue(conn, out IPlayerCharacter character))
			{
				DateTime now2 = DateTime.UtcNow;
				if (validatedSceneLastTimeByClientId.TryGetValue(conn.ClientId, out DateTime lastValidated) &&
					(now2 - lastValidated).TotalSeconds < ValidatedSceneBroadcastCooldownSeconds)
				{
					return;
				}
				validatedSceneLastTimeByClientId[conn.ClientId] = now2;

				if (character == null)
				{
					DisconnectWithNotice(conn, DisconnectNoticeReason.CharacterUnavailable);
					return;
				}

				// Remove the waiting scene load character
				mappingData.WaitingSceneLoadCharacters.Remove(conn);
				sceneLoadDeadlines.TryRemove(conn.ClientId, out _);

				// Add a connection->character map for ease of use
				mappingData.ConnectionCharacters[conn] = character;
				// Add a characterName->character map for ease of use
				mappingData.CharactersByID[character.ID] = character;
				mappingData.CharactersByLowerCaseName[character.CharacterNameLower] = character;
				// Add a worldID<characterID->character> map for ease of use
				if (!mappingData.CharactersByWorld.TryGetValue(character.WorldServerID, out Dictionary<long, IPlayerCharacter> characters))
				{
					mappingData.CharactersByWorld.Add(character.WorldServerID, characters = new Dictionary<long, IPlayerCharacter>());
				}
				characters[character.ID] = character;

				// Get the characters scene
				bool resolvedScene = TryGetCharacterScene(character, out Scene scene);

				// Validate the scene
				if (!resolvedScene ||
					!scene.IsValid() ||
					!scene.isLoaded)
				{
					Log.Debug("CharacterSystem", "Scene is not valid.");

					// Clean up all mappings that were just added
					mappingData.ConnectionCharacters.Remove(conn);
					mappingData.CharactersByID.Remove(character.ID);
					mappingData.CharactersByLowerCaseName.Remove(character.CharacterNameLower);
					if (mappingData.CharactersByWorld.TryGetValue(character.WorldServerID, out var worldChars))
					{
						worldChars.Remove(character.ID);
					}

					/* Same reason as the invalid-scene branch in
					 * SceneManager_OnClientLoadedStartScenes: the character was credited to the
					 * scene instance by OnAfterLoadCharacter, the mappings have just been undone
					 * by hand, and the disconnect below therefore finds nothing left to tear
					 * down. Without this the instance keeps a resident it does not have. */
					DispatchCharacterEvent(OnDisconnect, conn, character, nameof(OnDisconnect));

					// Release the session for this character
					TryExtractAndReleaseSession(mappingData, character.ID);

					Server.NetworkWrapper.NetworkManager.StorePooledInstantiated(character.NetworkObject, true);
					DisconnectWithNotice(conn, DisconnectNoticeReason.SceneUnavailable);
					return;
				}

				// Set the proper physics scene for the character, scene stacking requires separated physics
				character.Motor?.SetPhysicsScene(scene.GetPhysicsScene());

				// Character becomes mortal when loaded into the scene
				if (character.TryGet(out ICharacterDamageController damageController))
				{
					damageController.Immortal = false;
				}

				// Ensure the game object is active, pooled objects are disabled
				character.GameObject.SetActive(true);

				// Ensure the character is marked as loaded so that controllers can check this flag to prevent actions before the character is fully in the world
				character.EnableFlags(CharacterFlags.IsLoaded);

				// If the player was dead when they loaded into the scene,
				// re-send the death broadcast so the death dialog reappears.
				// Handles reconnect-while-dead and scene transitions.
				if (character.IsFlagged(CharacterFlags.IsDead))
				{
					Server.NetworkWrapper.Broadcast(conn,
						new DeathBroadcast(), true, FishNet.Transporting.Channel.Reliable);
				}

				// Spawn the nob over the network
				ServerManager.Spawn(character.NetworkObject, conn, scene);

				OnSpawnCharacter?.Invoke(conn, character, scene);

				OnConnect?.Invoke(conn, character);

				// Send non-DB data immediately on the main thread
				SendNonDbCharacterData(character);

				// Capture social IDs on the main thread for async DB fetch
				long guildID = 0;
				if (character.TryGet(out IGuildController gc))
				{
					guildID = gc.ID;
				}
				long partyID = 0;
				if (character.TryGet(out IPartyController pc))
				{
					partyID = pc.ID;
				}
				List<long> friendIDs = null;
				if (character.TryGet(out IFriendController fc) &&
					fc.Friends != null &&
					fc.Friends.Count > 0)
				{
					friendIDs = new List<long>(fc.Friends);
				}
				NetworkConnection owner = character.Owner;
				/* Captured on the main thread with everything else. The guild roster payload needs
				 * to know WHOSE roster it is in order to apply the officer-note filter, and the
				 * async path must not reach back into the character to ask. */
				long socialCharacterID = character.ID;

				// Enqueue only the DB-dependent social data fetch
				if (!EnqueueAsyncWork(() => SendAllCharacterDataAsync(owner, socialCharacterID, guildID, partyID, friendIDs)))
				{
					Log.Warning("CharacterSystem", $"Failed to enqueue social data broadcast fetch for character {character.ID}.");
				}

				//Log.Debug("CharacterSystem", character.CharacterName + " has been spawned at: " + character.SceneName + " " + character.Transform.position.ToString());
			}
		}

		/// <summary>
		/// The client notified the server it unloaded scenes. Disconnects connection if character is not loaded.
		/// </summary>
		/// <param name="conn">Network connection of the client.</param>
		/// <param name="msg">ClientScenesUnloadedBroadcast message.</param>
		/// <param name="channel">Network channel used for the broadcast.</param>
		private void OnClientScenesUnloadedBroadcastReceived(NetworkConnection conn, ClientScenesUnloadedBroadcast msg, Channel channel)
		{
			if (msg.UnloadedScenes == null || msg.UnloadedScenes.Length == 0)
			{
				Log.Debug("CharacterSystem", "No unloaded scenes received.");
				return;
			}

			// Check whether the connection still has a character BEFORE consulting the rate
			// limiter. A client unloads scenes on the way in as well as on the way out, so the
			// benign entry-time broadcast used to stamp the limiter and then swallow the
			// teleport broadcast that arrived within the next five seconds — leaving the player
			// connected to a scene server that had already released their character, with
			// nothing left to move them on. Teleporters placed near a spawn point hit this
			// every time.
			//
			// WaitingSceneLoadCharacters counts as "still has a character". A connection in
			// that map is mid-handshake: its character is claimed and instantiated but not yet
			// spawned, and the client is actively swapping scenes to get there — so the unload
			// report it sends on the way in is the most ordinary thing it can do. Treating it
			// as "nothing left here" disconnected the client during its own world entry, which
			// released the claim and sent it back around the login loop.
			if (Server.DataContainerRegistry.TryGet<ICharacterMappingData<NetworkConnection>>(out var mappingData) &&
				(mappingData.ConnectionCharacters.ContainsKey(conn) ||
				 mappingData.WaitingSceneLoadCharacters.ContainsKey(conn)))
			{
				return;
			}

			// No character: the connection has nothing left to do here and must be sent back to
			// the world server. Rate-limit only this branch, and only to avoid repeating the
			// disconnect for a client that keeps talking while the disconnect settles.
			DateTime nowUtc = DateTime.UtcNow;
			if (sceneUnloadLastTimeByClientId.TryGetValue(conn.ClientId, out DateTime lastUnload) &&
				(nowUtc - lastUnload).TotalSeconds < 5.0)
			{
				return;
			}
			sceneUnloadLastTimeByClientId[conn.ClientId] = nowUtc;

			//Log.Debug($"Connection unloaded scene: {msg.UnloadedScenes[0].Name}|{msg.UnloadedScenes[0].Handle}");

			conn.Disconnect(false);
		}

		/// <summary>
		/// Parses a comma-separated objective values string into a long array.
		/// </summary>
		private static long[] ParseObjectiveValues(string objectiveValues)
		{
			if (string.IsNullOrEmpty(objectiveValues))
			{
				return Array.Empty<long>();
			}

			string[] parts = objectiveValues.Split(',');
			long[] values = new long[parts.Length];
			for (int i = 0; i < parts.Length; i++)
			{
				long.TryParse(parts[i], out values[i]);
			}
			return values;
		}

		/// <summary>
		/// Bundles all pre-fetched character data from the async DB load for main-thread instantiation.
		/// </summary>
		private sealed class CharacterLoadContext
		{
			/// <summary>Network connection of the owning client.</summary>
			public readonly NetworkConnection Connection;
			/// <summary>Core character data (name, position, scene, etc.).</summary>
			public readonly CharacterData CharacterData;
			/// <summary>Session ownership token for the claimed character.</summary>
			public readonly Guid SessionToken;
			/// <summary>ID of the server claiming this character session.</summary>
			public readonly long ServerID;
			/// <summary>
			/// Scene name of the instance this character enters, or <c>null</c> when it is not
			/// entering one. Resolved from the character's <c>InstanceID</c> during the load.
			/// </summary>
			public readonly string InstanceSceneName;
			/// <summary>Scene row ID of the instance, or 0 when not entering one.</summary>
			public readonly long InstanceSceneHandle;
			/// <summary>Pre-fetched inventory item data.</summary>
			public readonly IReadOnlyList<CharacterInventoryData> InventoryData;
			/// <summary>Pre-fetched bank item data.</summary>
			public readonly IReadOnlyList<CharacterBankData> BankData;
			/// <summary>Pre-fetched equipment item data.</summary>
			public readonly IReadOnlyList<CharacterEquipmentData> EquipmentData;
			/// <summary>Pre-fetched attribute and resource data.</summary>
			public readonly IReadOnlyList<CharacterAttributeData> AttributeData;
			/// <summary>Pre-fetched crafted ability data.</summary>
			public readonly IReadOnlyList<CharacterAbilityData> AbilityData;
			/// <summary>Pre-fetched known base ability data.</summary>
			public readonly IReadOnlyList<CharacterKnownAbilityData> KnownAbilityData;
			/// <summary>Pre-fetched achievement data.</summary>
			public readonly IReadOnlyList<CharacterAchievementData> AchievementData;
			/// <summary>Pre-fetched friend data.</summary>
			public readonly IReadOnlyList<CharacterFriendData> FriendData;
			/// <summary>Pre-fetched guild membership data, if any.</summary>
			public readonly CharacterGuildData? GuildData;
			/// <summary>Pre-fetched party membership data, if any.</summary>
			public readonly CharacterPartyData? PartyData;
			/// <summary>Pre-fetched hotkey bar data.</summary>
			public readonly IReadOnlyList<CharacterHotkeyData> HotkeyData;
			/// <summary>Pre-fetched active buff data.</summary>
			public readonly IReadOnlyList<CharacterBuffData> BuffData;
			/// <summary>Pre-fetched faction standing data.</summary>
			public readonly IReadOnlyList<CharacterFactionData> FactionData;
			/// <summary>Pre-fetched quest state data.</summary>
			public readonly IReadOnlyList<CharacterQuestData> QuestData;

			/// <summary>
			/// Initializes a new CharacterLoadContext with all pre-fetched character data.
			/// </summary>
			/// <param name="connection">Network connection of the owning client.</param>
			/// <param name="characterData">Core character data.</param>
			/// <param name="sessionToken">Session ownership token.</param>
			/// <param name="serverID">Server ID that claimed the session.</param>
			/// <param name="instanceSceneName">Resolved instance scene name, or null.</param>
			/// <param name="instanceSceneHandle">Resolved instance scene handle, or 0.</param>
			/// <param name="inventoryData">Pre-fetched inventory items.</param>
			/// <param name="bankData">Pre-fetched bank items.</param>
			/// <param name="equipmentData">Pre-fetched equipment items.</param>
			/// <param name="attributeData">Pre-fetched attribute data.</param>
			/// <param name="abilityData">Pre-fetched crafted ability data.</param>
			/// <param name="knownAbilityData">Pre-fetched known base ability data.</param>
			/// <param name="achievementData">Pre-fetched achievement data.</param>
			/// <param name="friendData">Pre-fetched friend data.</param>
			/// <param name="guildData">Pre-fetched guild membership data.</param>
			/// <param name="partyData">Pre-fetched party membership data.</param>
			/// <param name="hotkeyData">Pre-fetched hotkey bar data.</param>
			/// <param name="buffData">Pre-fetched active buff data.</param>
			/// <param name="factionData">Pre-fetched faction standing data.</param>
			/// <param name="questData">Pre-fetched quest state data.</param>
			public CharacterLoadContext(
				NetworkConnection connection, CharacterData characterData,
				Guid sessionToken, long serverID,
				string instanceSceneName, long instanceSceneHandle,
				IReadOnlyList<CharacterInventoryData> inventoryData,
				IReadOnlyList<CharacterBankData> bankData,
				IReadOnlyList<CharacterEquipmentData> equipmentData,
				IReadOnlyList<CharacterAttributeData> attributeData,
				IReadOnlyList<CharacterAbilityData> abilityData,
				IReadOnlyList<CharacterKnownAbilityData> knownAbilityData,
				IReadOnlyList<CharacterAchievementData> achievementData,
				IReadOnlyList<CharacterFriendData> friendData,
				CharacterGuildData? guildData,
				CharacterPartyData? partyData,
				IReadOnlyList<CharacterHotkeyData> hotkeyData,
				IReadOnlyList<CharacterBuffData> buffData,
				IReadOnlyList<CharacterFactionData> factionData,
				IReadOnlyList<CharacterQuestData> questData)
			{
				Connection = connection;
				CharacterData = characterData;
				SessionToken = sessionToken;
				ServerID = serverID;
				InstanceSceneName = instanceSceneName;
				InstanceSceneHandle = instanceSceneHandle;
				InventoryData = inventoryData;
				BankData = bankData;
				EquipmentData = equipmentData;
				AttributeData = attributeData;
				AbilityData = abilityData;
				KnownAbilityData = knownAbilityData;
				AchievementData = achievementData;
				FriendData = friendData;
				GuildData = guildData;
				PartyData = partyData;
				HotkeyData = hotkeyData;
				BuffData = buffData;
				FactionData = factionData;
				QuestData = questData;
			}
		}
		/// <summary>
		/// Drops a party membership this character cannot keep, during load.
		/// </summary>
		/// <remarks>
		/// Delegated to the party system rather than deleting the row here, because removing a
		/// member is not a single delete: leadership has to move first if the leaver led, and the
		/// party itself has to be retired if nobody is left. Doing it inline would be a second,
		/// divergent copy of rules that already exist.
		/// <para>
		/// A missing party system is not fatal to the load. The membership stays in the database
		/// and the character simply loads without it applied, which is the same outcome the caller
		/// wanted; the next load will try again.
		/// </para>
		/// </remarks>
		/// <param name="membership">The membership row being dropped.</param>
		/// <param name="reason">Why, for the log line.</param>
		private async Task DropPartyMembershipAsync(CharacterPartyData membership, string reason)
		{
			if (Server == null ||
				!Server.BehaviourRegistry.TryGet(out IPartySystem<NetworkConnection> partySystem))
			{
				await Log.Warning("CharacterSystem",
					$"Character {membership.CharacterID} should have been removed from party {membership.PartyID} ({reason}) but the party system is unavailable.");
				return;
			}

			/* The result is deliberately not acted on here. This runs while the character is
			 * still loading and the caller drops the membership from the payload either way; a
			 * refusal leaves a row that this same check finds and deletes on the character's next
			 * load, and the party system has already logged it. */
			await partySystem.RemoveCharacterFromPartyAsync(
				membership.CharacterID,
				membership.PartyID,
				reason);
		}

	}
}