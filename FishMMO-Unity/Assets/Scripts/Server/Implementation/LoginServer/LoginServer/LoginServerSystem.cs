using System;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Server.Core;
using FishMMO.Server.Core.LoginServer;
using FishMMO.Shared;
using FishMMO.Auth.Implementation;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Server.Implementation.LoginServer
{
	/// <summary>
	/// Manages login server lifecycle, including database registration and heartbeat updates for the login service.
	/// </summary>
	[CreateAssetMenu(fileName = "LoginServerSystem", menuName = "FishMMO/Server/LoginServer/Login Server System", order = 1)]
	[RequiresDataContainer(typeof(LoginServerRuntimeData))]
	[RequiresDataContainer(typeof(AsyncWorkerData))]
	public class LoginServerSystem : ServerBehaviour, ILoginServerSystem
	{
		/// <summary>
		/// Interval in seconds between database heartbeat pulses.
		/// </summary>
		[SerializeField] private float pulseRate = 5.0f;

		/// <summary>
		/// Interval in seconds between database heartbeat pulses.
		/// </summary>
		public float PulseRate => pulseRate;

		/// <summary>
		/// Token-signing HMAC key rotation interval in hours. When the active key has been in use
		/// for longer than this, the next periodic pulse rotates it. Set to 0 to disable rotation.
		/// </summary>
		/// <remarks>
		/// The DB layer (LoginServerSigningKeyService.UpsertAsync) deactivates the prior key but
		/// retains it for verification of in-flight tokens (default 7-day grace window enforced by
		/// DeleteAsync). Issued auth tokens carry the signing key ID inside their HMAC envelope,
		/// so WorldServers and SceneServers continue to validate pre-rotation tokens via FetchByIdAsync
		/// until those tokens expire or the grace window elapses, whichever is shorter.
		/// </remarks>
		[SerializeField] private float signingKeyRotationHours = 24.0f;

		/// <summary>UTC timestamp when the currently active signing key was issued.</summary>
		private DateTime signingKeyIssuedUtc;

		/// <summary>Guards against re-entrant rotation while a previous rotation is still in flight.</summary>
		private int rotationInFlight;

		/// <summary>
		/// 32-byte AES-256 KEK used by <see cref="KeyEnvelope"/> to wrap signing-key material before
		/// it is written to the database. Loaded once from configuration in <see cref="InitializeOnce"/>.
		/// </summary>
		private byte[] signingKeyKek;

		/// <summary>
		/// Initializes the login server system, registers event handlers, and adds the server to the database.
		/// </summary>
		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				Log.Error("LoginServerSystem", "InitializeOnce: Server is null");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			// H13: Disable core dumps and unprivileged ptrace as early as possible so subsequent
			// allocations of TOTP/signing-key material are not exposed via /proc/<pid>/mem or core
			// files. Failure is non-fatal on platforms without prctl (Windows, macOS).
			//
			// Set FISHMMO_ALLOW_COREDUMPS=1 to skip this hardening step — required for
			// debugging native crashes (SEGV, SIGBUS) in libfishmmo_webtransport or MsQuic.
			// Core dumps MUST be re-disabled before returning to production.
			bool allowCoredumps = string.Equals(
				Environment.GetEnvironmentVariable("FISHMMO_ALLOW_COREDUMPS"),
				"1", StringComparison.OrdinalIgnoreCase);
			if (allowCoredumps)
			{
				Log.Warning("LoginServerSystem",
					"FISHMMO_ALLOW_COREDUMPS=1 — core dumps are ENABLED. " +
					"Key material may be exposed in core files. " +
					"Unset this variable before returning to production.");
			}
			else if (ProcessHardening.TryDisableCoreDumpAndPtrace(out string hardeningStatus))
			{
				Log.Debug("LoginServerSystem", $"Process hardening: {hardeningStatus}");
			}
			else
			{
				Log.Warning("LoginServerSystem", $"Process hardening skipped: {hardeningStatus}");
			}

#if !UNITY_EDITOR && !DEVELOPMENT_BUILD
			if (Server.Configuration.TryGetBool("AutoVerifyAccounts", out bool autoVerify) && autoVerify)
			{
				Log.Error("LoginServerSystem",
					"FATAL: AutoVerifyAccounts=true is not allowed in production builds. " +
					"Set AutoVerifyAccounts=false in the server .cfg file.");
				throw new InvalidOperationException(
					"AutoVerifyAccounts must be false in production builds.");
			}
#endif

			if (!Server.DataContainerRegistry.TryGet<ILoginServerRuntimeData>(out var runtimeData))
			{
				Log.Error("LoginServerSystem", "Failed to initialize: ILoginServerRuntimeData not found");
				return ServerComponentInitializationStatus.FailedToGetDataContainer;
			}

			if (!Server.AddressProvider.TryGetServerIPAddress(out ServerAddress server))
			{
				Log.Error("LoginServerSystem", "Failed to initialize: Could not get server IP address");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			if (!Server.Configuration.TryGetString("ServerName", out string name))
			{
				Log.Error("LoginServerSystem", "Failed to initialize: ServerName not configured");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			// Register login server in database
			if (Server.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<ILoginServerService>(out var loginServerService))
			{
				Log.Error("LoginServerSystem", "Failed to resolve ILoginServerService from database service registry");
				return ServerComponentInitializationStatus.FailedToGetDbContext;
			}

			// Register login server in database.
			// BLOCKING THE MAIN THREAD IS INTENTIONAL: Unity's SynchronizationContext captures
			// the main thread. If we awaited directly, any continuation that needs the main thread
			// (e.g., FishNet callbacks, coroutine resumptions) would deadlock because the main thread
			// is blocked. We use Task.Run to escape the SynchronizationContext, then Wait() to block
			// synchronously on a background-thread task. The 30s timeout prevents indefinite hang.
			const int dbRegistrationTimeoutMs = 30_000;
			Task<DatabaseResult<LoginServerData>> registerTask = Task.Run(() =>
				loginServerService.PersistAsync(name, server.Address, server.Port));

			// Block the main thread intentionally during startup (see comment above).
			if (!registerTask.Wait(dbRegistrationTimeoutMs))
			{
				Log.Error("LoginServerSystem", $"Login server DB registration timed out after {dbRegistrationTimeoutMs}ms");
				return ServerComponentInitializationStatus.FailedToGetDbContext;
			}

			DatabaseResult<LoginServerData> dbResult = registerTask.GetAwaiter().GetResult();

			if (!dbResult.IsSuccess)
			{
				Log.Error("LoginServerSystem", $"Failed to register login server in database: [{dbResult.ErrorCode}] {dbResult.ErrorMessage} (IsTransient={dbResult.IsTransient})");
				return ServerComponentInitializationStatus.FailedToGetDbContext;
			}

			runtimeData.ID = dbResult.Data.ID;

			// Generate and persist HMAC signing key for token issuance
			if (Server.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<ILoginServerSigningKeyService>(out var signingKeyService))
			{
				Log.Error("LoginServerSystem", "Failed to resolve ILoginServerSigningKeyService from database service registry");
				return ServerComponentInitializationStatus.FailedToGetDbContext;
			}

			byte[] hmacKey = CryptoHelper.GenerateKey(CryptoHelper.HmacKeyLength);

			// Load the deployment-shared KEK and wrap the signing key before persistence.
			// A DB-only attacker (read or write) cannot recover or forge usable signing keys
			// without also possessing the KEK, which is provisioned out-of-band.
			if (!SigningKeyKekProvider.TryLoad(Server.Configuration, out signingKeyKek, out string kekError))
			{
#if UNITY_EDITOR || DEVELOPMENT_BUILD
				Log.Warning("LoginServerSystem", $"Signing-key KEK not provisioned — persisting raw HMAC key. {kekError}");
				signingKeyKek = null;
#else
				Log.Error("LoginServerSystem", $"Signing-key KEK is REQUIRED in production. {kekError}");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
#endif
			}

			byte[] wrappedHmacKey = signingKeyKek != null ? KeyEnvelope.Wrap(signingKeyKek, hmacKey, SigningKeyKekProvider.BuildAad(runtimeData.ID)) : hmacKey;

			Task<DatabaseResult<LoginServerSigningKeyData>> keyTask = Task.Run(() =>
				signingKeyService.UpsertAsync(runtimeData.ID, wrappedHmacKey));

			// Block the main thread intentionally during startup (see comment above).
			if (!keyTask.Wait(dbRegistrationTimeoutMs))
			{
				Log.Error("LoginServerSystem", $"HMAC signing key persistence timed out after {dbRegistrationTimeoutMs}ms");
				return ServerComponentInitializationStatus.FailedToGetDbContext;
			}

			DatabaseResult<LoginServerSigningKeyData> keyResult = keyTask.GetAwaiter().GetResult();

			if (!keyResult.IsSuccess)
			{
				Log.Error("LoginServerSystem", $"Failed to persist HMAC signing key: [{keyResult.ErrorCode}] {keyResult.ErrorMessage}");
				return ServerComponentInitializationStatus.FailedToGetDbContext;
			}

			// Configure the authenticator for token issuance
			signingKeyIssuedUtc = DateTime.UtcNow;
			if (Server.NetworkWrapper.NetworkManager.ServerManager.GetAuthenticator() is ServerAuthenticator authenticator)
			{
				authenticator.TokenSigningKey = hmacKey;
				authenticator.LoginServerId = runtimeData.ID;
				authenticator.TokenSigningKeyId = keyResult.Data.ID;

				// C7: Derive the TOTP master KEK through the KMS provider abstraction. The default
				// LocalDeriveKmsProvider preserves the legacy HMAC-SHA256 behaviour; production
				// deployments can register an external IKmsProvider via the service registry to
				// keep the cleartext KEK off the application heap for the process lifetime.
				byte[] totpMasterKey;
				using (var localKms = new LocalDeriveKmsProvider(hmacKey))
				{
					totpMasterKey = localKms.DeriveKey("fishmmo-totp-master-key-v1");
				}
				authenticator.TotpMasterKey = totpMasterKey;

				// Wire the same TOTP master key to the account creation system.
				// Each consumer gets its own copy so a ZeroMemory on one does not
				// corrupt the other's key material.
				if (Server.BehaviourRegistry.TryGet<IAccountCreationSystem<FishNet.Connection.NetworkConnection>>(out var accountSystem) &&
					accountSystem is AccountCreationSystem concreteAccountSystem)
				{
					byte[] totpMasterKeyCopy = new byte[totpMasterKey.Length];
					Buffer.BlockCopy(totpMasterKey, 0, totpMasterKeyCopy, 0, totpMasterKeyCopy.Length);
					concreteAccountSystem.TotpMasterKey = totpMasterKeyCopy;
				}
			}
			else
			{
				Log.Error("LoginServerSystem", "Failed to configure authenticator: ServerAuthenticator not found");
				return ServerComponentInitializationStatus.FailedToFindRequiredDependency;
			}

			// Periodic callbacks
			if (Server is IPeriodicUpdateSystem periodicSystem)
			{
				periodicSystem.RegisterPeriodicCallback(PulseRate, OnPeriodicPulse);
			}

			Log.Debug("LoginServerSystem", $"Initialized (ServerID={runtimeData.ID}, Address={server.Address}:{server.Port}, PulseRate={PulseRate}s)");
			return ServerComponentInitializationStatus.Initialized;
		}

		/// <summary>
		/// Cleans up the login server system.
		/// </summary>
		public override void OnDeinitialize()
		{
			if (Server == null)
			{
				Log.Error("LoginServerSystem", "OnDeinitialize: Server is null");
				return;
			}

			// Periodic callbacks
			if (Server is IPeriodicUpdateSystem periodicSystem)
			{
				periodicSystem.UnregisterPeriodicCallback(OnPeriodicPulse);
			}

			// Zero the authenticator's signing key and TOTP master key before shutdown
			if (Server.NetworkWrapper.NetworkManager.ServerManager.GetAuthenticator() is ServerAuthenticator authenticator)
			{
				if (authenticator.TokenSigningKey != null)
				{
					CryptographicOperationsCompat.ZeroMemory(authenticator.TokenSigningKey);
					authenticator.TokenSigningKey = null;
				}
				if (authenticator.TotpMasterKey != null)
				{
					CryptographicOperationsCompat.ZeroMemory(authenticator.TotpMasterKey);
					authenticator.TotpMasterKey = null;
				}
			}

			// Zero the account creation system's TOTP master key
			if (Server.BehaviourRegistry.TryGet<IAccountCreationSystem<FishNet.Connection.NetworkConnection>>(out var accountSystem) &&
				accountSystem is AccountCreationSystem concreteAccountSystem &&
				concreteAccountSystem.TotpMasterKey != null)
			{
				CryptographicOperationsCompat.ZeroMemory(concreteAccountSystem.TotpMasterKey);
				concreteAccountSystem.TotpMasterKey = null;
			}

			// Zero the deployment signing-key KEK so a post-shutdown core dump cannot recover it.
			if (this.signingKeyKek != null)
			{
				CryptographicOperationsCompat.ZeroMemory(this.signingKeyKek);
				this.signingKeyKek = null;
			}

			// Deregister login server and signing key from database on shutdown
			if (Server.DataContainerRegistry.TryGet<ILoginServerRuntimeData>(out var runtimeData) &&
				runtimeData.ID > 0 &&
				Server.Database?.ServiceRegistry != null)
			{
				if (Server.Database.ServiceRegistry.TryGet<ILoginServerSigningKeyService>(out var signingKeyService))
				{
					try
					{
						// BLOCKING THE MAIN THREAD DURING SHUTDOWN IS INTENTIONAL: We use Task.Run to escape
						// Unity's SynchronizationContext and Wait(5000) to block synchronously with a timeout.
						// At this point the server is shutting down, so blocking the main thread momentarily
						// is acceptable and ensures the DB cleanup completes before process exit.
						var keyDeleteTask = Task.Run(() => signingKeyService.DeleteAsync(runtimeData.ID));
						if (!keyDeleteTask.Wait(5000))
						{
							Log.Warning("LoginServerSystem", $"Signing key deletion timed out after 5000ms");
						}
						else
						{
							DatabaseResult keyDeleteResult = keyDeleteTask.GetAwaiter().GetResult();
							if (!keyDeleteResult.IsSuccess)
							{
								Log.Warning("LoginServerSystem", $"Failed to delete signing key from DB: [{keyDeleteResult.ErrorCode}] {keyDeleteResult.ErrorMessage}");
							}
						}
					}
					catch (Exception ex)
					{
						Log.Error("LoginServerSystem", $"Failed to delete signing key from DB: {ex}");
					}
				}

				if (Server.Database.ServiceRegistry.TryGet<ILoginServerService>(out var loginServerService))
				{
					try
					{
						// BLOCKING THE MAIN THREAD DURING SHUTDOWN IS INTENTIONAL (see comment above).
						var deregisterTask = Task.Run(() => loginServerService.DeleteAsync(runtimeData.ID));
						if (!deregisterTask.Wait(5000))
						{
							Log.Warning("LoginServerSystem", $"Login server deregistration timed out after 5000ms");
						}
						else
						{
							DatabaseResult deregisterResult = deregisterTask.GetAwaiter().GetResult();
							if (!deregisterResult.IsSuccess)
							{
								Log.Warning("LoginServerSystem", $"Failed to deregister login server from DB: [{deregisterResult.ErrorCode}] {deregisterResult.ErrorMessage}");
							}
						}
					}
					catch (Exception ex)
					{
						Log.Error("LoginServerSystem", $"Failed to deregister login server from DB: {ex}");
					}
				}
			}
		}

		/// <summary>
		/// Periodic callback that sends a heartbeat pulse to the database.
		/// </summary>
		/// <param name="deltaTime">Delta time parameter (unused).</param>
		private void OnPeriodicPulse(float deltaTime)
		{
			if (!Initialized || Server == null || Server.ServerState != ConnectionState.Started)
			{
				return;
			}

			if (Server.DataContainerRegistry.TryGet<ILoginServerRuntimeData>(out var runtimeData))
			{
				if (!TryEnqueueAsyncWork(() => PulseAsync(runtimeData.ID)))
				{
					Log.Warning("LoginServerSystem", "Failed to enqueue pulse work item.");
				}

				// C6: Token signing key rotation. Inspects the age of the in-memory active key and
				// triggers an asynchronous rotation when it exceeds the configured interval. A single
				// rotation may be in flight at a time (CAS-guarded by rotationInFlight).
				// NOTE: Key rotation is triggered only during periodic heartbeats. If the heartbeat
				// mechanism fails, key rotation also stops. Consider adding an independent rotation
				// timer.
				if (signingKeyRotationHours > 0f &&
					(DateTime.UtcNow - signingKeyIssuedUtc).TotalHours >= signingKeyRotationHours &&
					System.Threading.Interlocked.CompareExchange(ref rotationInFlight, 1, 0) == 0)
				{
					if (!TryEnqueueAsyncWork(() => RotateSigningKeyAsync(runtimeData.ID)))
					{
						System.Threading.Interlocked.Exchange(ref rotationInFlight, 0);
						Log.Warning("LoginServerSystem", "Failed to enqueue signing key rotation work item.");
					}
				}
			}
		}

		/// <summary>
		/// Rotates the token-signing HMAC key. Generates a fresh key, persists it to the DB (which
		/// deactivates the prior key and starts the verification grace window), and atomically swaps
		/// the in-memory key + key ID + derived TOTP master key on the authenticator. The prior
		/// in-memory key buffer is intentionally NOT zero-filled here: signing operations on other
		/// threads may hold transient references, so we release the reference and let GC reclaim it
		/// once those calls drain. Zeroization of the live key still occurs on OnDeinitialize.
		/// </summary>
		private async Task RotateSigningKeyAsync(long serverId)
		{
			try
			{
				if (Server == null ||
					Server.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ILoginServerSigningKeyService>(out var signingKeyService))
				{
					return;
				}

				byte[] newHmacKey = CryptoHelper.GenerateKey(CryptoHelper.HmacKeyLength);

				// Wrap the rotated key under the deployment KEK before persistence. If the
				// KEK is not yet loaded (rotation racing init) we fail closed and retry on the
				// next tick rather than silently writing a plaintext key.
				if (signingKeyKek == null)
				{
					CryptographicOperationsCompat.ZeroMemory(newHmacKey);
					await Log.Warning("LoginServerSystem", "Signing key rotation aborted: KEK not loaded.");
					return;
				}
				byte[] wrappedNewHmacKey = KeyEnvelope.Wrap(signingKeyKek, newHmacKey, SigningKeyKekProvider.BuildAad(serverId));
				DatabaseResult<LoginServerSigningKeyData> keyResult = await signingKeyService.UpsertAsync(serverId, wrappedNewHmacKey);
				if (!keyResult.IsSuccess)
				{
					CryptographicOperationsCompat.ZeroMemory(newHmacKey);
					await Log.Warning("LoginServerSystem", $"Signing key rotation persistence failed: [{keyResult.ErrorCode}] {keyResult.ErrorMessage}");
					return;
				}

				byte[] newTotpMasterKey;
				using (var localKms = new LocalDeriveKmsProvider(newHmacKey))
				{
					newTotpMasterKey = localKms.DeriveKey("fishmmo-totp-master-key-v1");
				}

				if (Server.NetworkWrapper.NetworkManager.ServerManager.GetAuthenticator() is ServerAuthenticator authenticator)
				{
					// Use atomic swap so concurrent token-issuance always sees a consistent
					// (key, keyId, totpMasterKey) tuple. Prior key material is zeroed inside.
					authenticator.AtomicSwapSigningKey(newHmacKey, keyResult.Data.ID, newTotpMasterKey);

					if (Server.BehaviourRegistry.TryGet<IAccountCreationSystem<FishNet.Connection.NetworkConnection>>(out var accountSystem) &&
						accountSystem is AccountCreationSystem concreteAccountSystem)
					{
						// Copy so ZeroMemory on the authenticator's copy does not affect the account system.
						byte[] totpMasterKeyCopy = new byte[newTotpMasterKey.Length];
						Buffer.BlockCopy(newTotpMasterKey, 0, totpMasterKeyCopy, 0, totpMasterKeyCopy.Length);
						concreteAccountSystem.TotpMasterKey = totpMasterKeyCopy;
					}
				}

				signingKeyIssuedUtc = DateTime.UtcNow;
				await Log.Debug("LoginServerSystem", $"Rotated token-signing key (ServerID={serverId}, NewKeyID={keyResult.Data.ID}).");
			}
			catch (Exception ex)
			{
				await Log.Error("LoginServerSystem", $"Error during signing key rotation: {ex}");
			}
			finally
			{
				System.Threading.Interlocked.Exchange(ref rotationInFlight, 0);
			}
		}

		/// <summary>
		/// Asynchronously sends a heartbeat pulse to the database.
		/// </summary>
		/// <param name="serverId">The login server's database ID.</param>
		private async Task PulseAsync(long serverId)
		{
			try
			{
				if (Server.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<ILoginServerService>(out var loginServerService))
				{
					return;
				}

				DatabaseResult dbResult = await loginServerService.PulseAsync(serverId);

				if (!dbResult.IsSuccess)
				{
					await Log.Warning("LoginServerSystem", $"Pulse failed: [{dbResult.ErrorCode}] {dbResult.ErrorMessage}");
				}
			}
			catch (Exception ex)
			{
				await Log.Error("LoginServerSystem", $"Error during pulse: {ex}");
			}
		}
	}
}