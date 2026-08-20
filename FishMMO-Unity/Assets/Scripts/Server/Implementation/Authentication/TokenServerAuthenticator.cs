using FishNet.Connection;
using FishNet.Managing;
using FishNet.Transporting;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Server.Core.LoginServer;
using FishMMO.Database.Npgsql.Services.Interfaces;
using System;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Threading;
using System.Threading.Tasks;
using FishMMO.Auth.Core;
using FishMMO.Auth.Implementation;
using FishMMO.Shared;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Token-based server authenticator for World and Scene servers.
	/// Delegates all handshake, channel, worker, and protocol logic to
	/// <see cref="TokenAuthenticatorCore{TConnection}"/> in FishMMO-Auth.
	/// This class bridges FishNet broadcast events to the core and provides Unity/DB callbacks.
	/// </summary>
	public class TokenServerAuthenticator : BaseServerAuthenticator
	{
		/// <summary>
		/// Lifetime of renewal-issued auth tokens, in minutes. Used when this World/Scene
		/// server mints a fresh token after a successful <see cref="TokenAuthBroadcast"/>
		/// and on the periodic refresh that follows. Inspector-configurable.
		/// </summary>
		[SerializeField] private float renewalTokenExpirationMinutes = 10f;

		/// <summary>
		/// Fraction of <see cref="renewalTokenExpirationMinutes"/> after which a connection's
		/// token is re-minted. At the default 0.5 a 10-minute token is replaced every 5
		/// minutes, so the token a client holds is never more than halfway through its life.
		/// </summary>
		/// <remarks>
		/// Without a periodic refresh the token was minted once, when the client authenticated,
		/// and never again. A scene hop is a re-authentication — the scene server disconnects
		/// the client, which reconnects to the World server and presents its stored token — so
		/// any session that stayed in one scene longer than the token lifetime arrived at the
		/// World server with an expired token and was dumped to the login screen. That is the
		/// teleport/death "reconnect" failure: it had nothing to do with the hop itself, only
		/// with how long the player had been standing in the previous scene.
		/// </remarks>
		[SerializeField][Range(0.1f, 0.9f)] private float renewalRefreshFraction = 0.5f;

		/// <summary>Seconds between passes of the periodic token-renewal sweep.</summary>
		private const float RenewalSweepIntervalSeconds = 5f;

		/// <summary>Base backoff applied after a failed renewal attempt.</summary>
		private static readonly TimeSpan RenewalRetryBaseDelay = TimeSpan.FromSeconds(15);

		/// <summary>
		/// Per-connection context needed to re-mint an auth token without a fresh
		/// <see cref="TokenAuthBroadcast"/>. Keyed by ClientId.
		/// </summary>
		private sealed class TokenRenewalState
		{
			/// <summary>Account the token is issued for, from the verified token payload.</summary>
			public string AccountName;
			/// <summary>Access level carried by the original token.</summary>
			public AccessLevel AccessLevel;
			/// <summary>LoginServer that owns the signing key this token chain is bound to.</summary>
			public long LoginServerId;
			/// <summary>UTC time at which the next renewal attempt becomes due.</summary>
			public DateTime NextAttemptUtc;
			/// <summary>1 while a renewal is running for this connection; 0 when idle.</summary>
			public int InFlight;
			/// <summary>Failed attempts since the last success, used for retry backoff.</summary>
			public int ConsecutiveFailures;
		}

		/// <summary>
		/// Renewal context per authenticated connection, keyed by ClientId. Entries are
		/// created on the first successful token auth and removed when the connection stops.
		/// </summary>
		private readonly ConcurrentDictionary<int, TokenRenewalState> renewalStates =
			new ConcurrentDictionary<int, TokenRenewalState>();

		/// <summary>Accumulated time since the last renewal sweep pass.</summary>
		private float renewalSweepAccumulator;

		/// <summary>Effective token lifetime in minutes, clamped to at least one minute.</summary>
		private int EffectiveTokenLifetimeMinutes => Math.Max(1, (int)renewalTokenExpirationMinutes);

		/// <summary>
		/// How long a freshly-issued token is used before it is replaced. Always strictly less
		/// than the token lifetime so a renewal can never come due after the token it replaces
		/// has already expired.
		/// </summary>
		private TimeSpan RenewalInterval
		{
			get
			{
				double lifetimeMinutes = EffectiveTokenLifetimeMinutes;
				double fraction = Mathf.Clamp(renewalRefreshFraction, 0.1f, 0.9f);
				double minutes = lifetimeMinutes * fraction;
				// Floor at 30s so a very short configured lifetime cannot turn the sweep into
				// a mint-per-tick loop, and cap below the lifetime with a margin for the
				// database round trip the renewal itself has to make.
				double maxMinutes = Math.Max(0.5, lifetimeMinutes - 0.5);
				return TimeSpan.FromMinutes(Math.Min(Math.Max(minutes, 0.5), maxMinutes));
			}
		}

		/// <summary>The token-specific core instance. Null until <see cref="InitializeCoreInstance"/> is called.</summary>
		private TokenCore core;

		/// <summary>
		/// Lazily loaded 32-byte AES-256 KEK used to unwrap signing-key blobs returned by the DB.
		/// Cached for the lifetime of the authenticator. <c>null</c> until first fetch attempt.
		/// </summary>
		private volatile byte[] signingKeyKek;
		/// <summary>
		/// Lock object for thread-safe lazy initialization of <see cref="signingKeyKek"/>.
		/// </summary>
		/// <remarks>
		/// A <see cref="SemaphoreSlim"/> rather than a monitor: the KEK is loaded from the
		/// database, and this path runs on worker threads inside async token verification.
		/// A monitor would force a blocking wait around that database round trip; an async
		/// gate lets the worker yield instead.
		/// </remarks>
		private readonly SemaphoreSlim signingKeyKekLock = new SemaphoreSlim(1, 1);

		/// <summary>
		/// Loads (and caches) the deployment KEK. Returns <c>null</c> on failure and emits a
		/// warning log; callers must fail closed.
		/// Uses double-checked locking for thread safety: the outer null check avoids the
		/// gate cost on the hot path; the inner null check under the gate ensures only one
		/// caller reaches <see cref="TryLoadKekFromDatabaseAsync"/>. The gate is awaited, never
		/// blocked on, so a worker yields rather than parking during the database round trip.
		/// The database (deployment_secrets table) is the ONLY source for the KEK — no
		/// environment variable or .cfg file fallbacks are supported.
		/// </summary>
		private async Task<byte[]> TryGetSigningKeyKekAsync(CancellationToken cancellationToken = default)
		{
			if (this.signingKeyKek != null) return this.signingKeyKek;

			await signingKeyKekLock.WaitAsync(cancellationToken).ConfigureAwait(false);
			try
			{
				if (this.signingKeyKek != null) return this.signingKeyKek;
				byte[] kek = await TryLoadKekFromDatabaseAsync(cancellationToken).ConfigureAwait(false);
				if (kek == null)
				{
					await Log.Warning(LogPrefix,
						"Signing-key KEK unavailable. " +
						"The KEK must be in the deployment_secrets database table with key='signing_key_kek'. " +
						"No environment variable or .cfg file fallbacks are supported.").ConfigureAwait(false);
					return null;
				}
				this.signingKeyKek = kek;
				return kek;
			}
			finally
			{
				signingKeyKekLock.Release();
			}
		}

		/// <summary>
		/// Attempts to load the KEK from the deployment_secrets database table.
		/// Returns null if the database is unavailable or the key is not found.
		/// Uses <see cref="SigningKeyKekProvider.LoadFromDatabaseAsync"/> for the actual lookup.
		/// </summary>
		private async Task<byte[]?> TryLoadKekFromDatabaseAsync(CancellationToken cancellationToken = default)
		{
			try
			{
				var server = Server;
				if (server?.Database?.ServiceRegistry == null) return null;
				if (!server.Database.ServiceRegistry.TryGet<IDeploymentSecretService>(out var svc))
					return null;

				var result = await SigningKeyKekProvider.LoadFromDatabaseAsync(svc, cancellationToken).ConfigureAwait(false);
				if (result.Success)
					return result.Kek;

				await Log.Warning(LogPrefix, $"Failed to load KEK from deployment_secrets: {result.Error}").ConfigureAwait(false);
				return null;
			}
			catch (Exception ex)
			{
				await Log.Warning(LogPrefix, $"Exception loading KEK from deployment_secrets: {ex.Message}").ConfigureAwait(false);
				return null;
			}
		}


		/// <summary>
		/// Unwraps a signing key blob using the deployment KEK. Returns <c>null</c> on any
		/// failure (missing KEK for a wrapped key, AEAD authentication failure, or key too short).
		/// The returned array must be zeroed by the caller when no longer needed.
		/// </summary>
		/// <remarks>
		/// Extracted to eliminate duplicated logic between <see cref="FetchSigningKeyCoreAsync"/>
		/// and <see cref="FetchCurrentSigningKeyCoreAsync"/>.
		/// </remarks>
		/// <param name="wrappedKey">The AES-256-GCM envelope (or plain key if not wrapped).</param>
		/// <param name="loginServerId">Owning LoginServer ID, bound into AAD for AEAD authentication.</param>
		/// <returns>The unwrapped (or pass-through plain) key bytes, or <c>null</c> on failure.</returns>
		private async Task<byte[]> UnwrapSigningKeyInternalAsync(byte[] wrappedKey, long loginServerId, CancellationToken cancellationToken = default)
		{
			byte[] kek = await TryGetSigningKeyKekAsync(cancellationToken).ConfigureAwait(false);
			if (kek == null)
			{
				if (KeyEnvelope.LooksWrapped(wrappedKey))
					return null;
				return wrappedKey;
			}

			byte[] unwrapped = KeyEnvelope.Unwrap(kek, wrappedKey, SigningKeyKekProvider.BuildAad(loginServerId));
			if (unwrapped == null)
				return null;

			if (unwrapped.Length < CryptoHelper.HmacKeyLength)
			{
				CryptographicOperationsCompat.ZeroMemory(unwrapped);
				return null;
			}
			return unwrapped;
		}
		/// <inheritdoc/>
		protected override BaseAuthenticatorCore<NetworkConnection> Core => core;

		/// <summary>
		/// World/Scene servers are connected to directly by IP:port (not through the
		/// IPFetch/L4 proxy path used by the LoginServer), so the client never sends a
		/// connection token — see <see cref="BaseServerAuthenticator.RequiresConnectionToken"/>.
		/// </summary>
		protected override bool RequiresConnectionToken => false;

		#region Lifecycle

		/// <inheritdoc/>
		protected override void InitializeCoreInstance()
		{
			var tam = Server.AccountManager as ITokenAccountManager<NetworkConnection>
				?? throw new InvalidOperationException(
					$"{LogPrefix}: Server.AccountManager must implement ITokenAccountManager<NetworkConnection>. " +
					$"Actual type: {Server.AccountManager?.GetType().FullName ?? "null"}.");
			core = new TokenCore(this, tam);

			// Read token auth worker/channel configuration from the server .cfg file.
			if (Server?.Configuration != null)
			{
				core.TokenWorkerCount = Server.Configuration.GetInt("AuthTokenWorkerCount", 2);
				core.TokenChannelCapacity = Server.Configuration.GetInt("AuthTokenChannelCapacity", 500);
			}
		}

		/// <inheritdoc/>
		public override IAccountManager<NetworkConnection> CreateAccountManager() =>
			new TokenAccountManager();

		/// <summary>
		/// Shuts down async workers and zeroes the cached signing-key KEK.
		/// </summary>
		public override void ShutdownWorkers()
		{
			// Stop scheduling renewals before the worker cancellation token fires; any that are
			// still running observe ShutdownToken and abort.
			renewalStates.Clear();

			// Runs on the main thread during teardown, so it must not wait on an in-flight KEK
			// load. Take the gate only if it is free; zero the cached key either way. A load
			// still running is stopped by the worker cancellation in base.ShutdownWorkers().
			bool acquired = signingKeyKekLock.Wait(0);
			try
			{
				byte[] cached = signingKeyKek;
				signingKeyKek = null;
				if (cached != null)
				{
					CryptographicOperationsCompat.ZeroMemory(cached);
				}
			}
			finally
			{
				if (acquired)
				{
					signingKeyKekLock.Release();
				}
			}
			base.ShutdownWorkers();
		}

		/// <inheritdoc/>
		protected override void RegisterProtocolHandlers(NetworkManager networkManager)
		{
			networkManager.ServerManager.RegisterBroadcast<TokenAuthBroadcast>(OnServerTokenAuthBroadcastReceived, false);
		}

		/// <inheritdoc/>
		protected override void OnUpdate()
		{
			SweepTokenRenewals();
		}

		/// <inheritdoc/>
		protected override void OnConnectionStopped(NetworkConnection conn)
		{
			if (conn == null) return;
			// Drop the renewal schedule immediately so a recycled ClientId cannot inherit the
			// previous occupant's account context. Any renewal still in flight for this entry
			// fails its identity check and skips the send.
			renewalStates.TryRemove(conn.ClientId, out _);
		}

		/// <summary>
		/// Registers (or replaces) the renewal schedule for a freshly authenticated connection
		/// and issues its first token.
		/// </summary>
		/// <remarks>
		/// The state is installed with the in-flight guard already claimed so the periodic
		/// sweep cannot start a second renewal alongside this one; <see cref="RunRenewalAsync"/>
		/// releases it and sets the next due time.
		/// </remarks>
		private Task HandleTokenAuthSuccessAsync(NetworkConnection conn, string accountName, AccessLevel accessLevel, long loginServerId)
		{
			if (conn == null)
			{
				return Task.CompletedTask;
			}

			var state = new TokenRenewalState
			{
				AccountName = accountName,
				AccessLevel = accessLevel,
				LoginServerId = loginServerId,
				NextAttemptUtc = DateTime.UtcNow + RenewalInterval,
				InFlight = 1,
				ConsecutiveFailures = 0,
			};

			renewalStates[conn.ClientId] = state;

			return RunRenewalAsync(conn, state);
		}

		#endregion

		#region UDP Receiver Gate (routes to core)

		/// <summary>Routes an incoming <see cref="TokenAuthBroadcast"/> to the core token authentication channel.</summary>
		internal void OnServerTokenAuthBroadcastReceived(NetworkConnection conn, TokenAuthBroadcast msg, Channel channel)
		{
			// Validate wire-format bounds before any allocation or crypto work.
			// Reject oversized payloads on the network thread.
			if (msg.Token == null || msg.Token.Length > AuthSizeLimits.MaxTokenAuthSize)
			{
				conn.Disconnect(true);
				return;
			}
			core?.OnTokenAuthReceived(conn, msg.Token, msg.Seq);
		}

		#endregion

		#region DB Implementations (called by TokenCore)

		/// <summary>
		/// Fetches the token-embedded HMAC signing key from the database.
		/// Returns <c>null</c> if the service is unavailable, the key is not found, or the key is too short.
		/// </summary>
		private async Task<byte[]> FetchSigningKeyCoreAsync(long loginServerId, long signingKeyId)
		{
			if (Server.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<ILoginServerSigningKeyService>(out var svc))
			{
				await Log.Warning(LogPrefix, $"Signing key service unavailable for LoginServer {loginServerId}, key {signingKeyId}.");
				return null;
			}

			var result = await svc.FetchByIdAsync(signingKeyId);

			if (!result.IsSuccess || result.Data.HmacKey == null)
			{
				await Log.Warning(LogPrefix, $"Signing key {signingKeyId} not found for LoginServer {loginServerId}.");
				return null;
			}

			if (result.Data.LoginServerId != loginServerId)
			{
				await Log.Warning(LogPrefix, $"Signing key {signingKeyId} belongs to LoginServer {result.Data.LoginServerId}, not {loginServerId}.");
				return null;
			}

			byte[] unwrapped = await UnwrapSigningKeyInternalAsync(result.Data.HmacKey, loginServerId).ConfigureAwait(false);
			if (unwrapped == null)
			{
				await Log.Warning(LogPrefix, $"Failed to unwrap signing key {signingKeyId} for LoginServer {loginServerId}.");
				return null;
			}

			return unwrapped;
		}

		/// <summary>
		/// Fetches the latest signing key for renewal token issuance.
		/// </summary>
		private async Task<(byte[] Key, long KeyId)> FetchCurrentSigningKeyCoreAsync(long loginServerId)
		{
			if (Server.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<ILoginServerSigningKeyService>(out var svc))
			{
				await Log.Warning(LogPrefix, $"Signing key service unavailable for LoginServer {loginServerId}.");
				return (null, 0);
			}

			var result = await svc.FetchByLoginServerIdAsync(loginServerId);
			if (!result.IsSuccess || result.Data.HmacKey == null)
			{
				await Log.Warning(LogPrefix, $"Current signing key not found for LoginServer {loginServerId}.");
				return (null, 0);
			}

			byte[] unwrapped = await UnwrapSigningKeyInternalAsync(result.Data.HmacKey, loginServerId).ConfigureAwait(false);
			if (unwrapped == null)
			{
				await Log.Warning(LogPrefix, $"Failed to unwrap current signing key for LoginServer {loginServerId}.");
				return (null, 0);
			}

			return (unwrapped, result.Data.ID);
		}

		/// <summary>
		/// Checks whether the token hash has been revoked in the database.
		/// Fails closed: returns <c>true</c> (revoked) if the service is unavailable or the DB query fails.
		/// </summary>
		private async Task<bool> CheckTokenRevocationCoreAsync(string tokenHash)
		{
			if (Server.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<IAuthTokenService>(out var svc))
				return true; // Treat service-unavailable as revoked (fail-closed).

			var result = await svc.FetchByHashAsync(tokenHash);
			if (!result.IsSuccess) return true; // DB error → fail-closed.
			return result.Data.Revoked;
		}

		/// <summary>
		/// Mints a fresh AES-GCM-encrypted auth token for <paramref name="conn"/> using
		/// the existing session encryption channel, persists its hash, and pushes it to
		/// the client via <see cref="RenewTokenResponseBroadcast"/>. Called immediately after a
		/// successful <see cref="TokenAuthBroadcast"/> and then periodically by
		/// <see cref="SweepTokenRenewals"/> for as long as the connection lives. Failures are
		/// logged and reported to the caller — the client retains its current token, which is
		/// still valid, and the next sweep retries.
		/// </summary>
		/// <param name="conn">The connection to issue a token for.</param>
		/// <param name="expectedState">Renewal schedule this attempt belongs to, used to detect a recycled ClientId. May be null to skip the check.</param>
		/// <param name="accountName">Account name extracted from the verified token.</param>
		/// <param name="accessLevel">Access level extracted from the verified token.</param>
		/// <param name="loginServerId">Originating LoginServer ID (used to look up the HMAC signing key).</param>
		/// <param name="ct">Cancellation token tied to server shutdown.</param>
		/// <returns><c>true</c> when a new token was persisted and queued for delivery.</returns>
		private async Task<bool> IssueRenewalTokenCoreAsync(NetworkConnection conn, TokenRenewalState expectedState, string accountName, AccessLevel accessLevel, long loginServerId, CancellationToken ct)
		{
			ct.ThrowIfCancellationRequested();
			if (conn == null || !conn.IsActive)
				return false;

			if (Server.AccountManager is not IAccountManager<NetworkConnection> am)
				return false;

			if (!am.GetConnectionEncryptionData(conn, out ConnectionEncryptionData encryptionData) || encryptionData == null)
				return false;

			// Scene TokenAuth requires a verified RealIp (v4+), so a token minted without one
			// is guaranteed to be rejected at the next hop. Replacing the client's current,
			// still-valid token with a certainly-invalid one is strictly worse than not
			// renewing, so abort and let the next sweep retry once the IP is resolvable.
			string? realIp = ResolveRateLimitKey(conn);
			if (string.IsNullOrEmpty(realIp))
			{
				await Log.Error(LogPrefix,
					$"Renewal token for '{accountName}' aborted: no verified real IP for connection {conn.ClientId}. " +
					"The client keeps its existing token; the next sweep will retry.");
				return false;
			}

			// Renewal is a best-effort path but a transient DB blip here forces the
			// client back through full SRP at the LoginServer, which compounds load
			// during exactly the conditions that caused the blip. Make one short
			// retry with linear backoff before giving up.
			var currentSigningKey = await FetchCurrentSigningKeyCoreAsync(loginServerId);
			ct.ThrowIfCancellationRequested();
			if (currentSigningKey.Key == null)
			{
				await Task.Delay(150, ct);
				if (!conn.IsActive) return false;
				ct.ThrowIfCancellationRequested();
				currentSigningKey = await FetchCurrentSigningKeyCoreAsync(loginServerId);
			}
			byte[] signingKey = currentSigningKey.Key;
			if (signingKey == null)
			{
				await Log.Warning(LogPrefix, $"Renewal token skipped for '{accountName}': signing key unavailable for LoginServer {loginServerId}.");
				return false;
			}

			byte[] rawToken = null;
			try
			{
				int expirationMinutes = EffectiveTokenLifetimeMinutes;

				// NOTE: accessLevel is from the original token's HMAC payload, not a fresh
				// DB lookup. If the account's access level changed between original token
				// issuance and renewal, the renewed token carries the old (possibly higher)
				// privileges. Mitigated by: short token lifetimes (10 min default), and the
				// expectation that access-level changes accompany token revocation.
				// A full fix would require an IAccountManager lookup here, but World/Scene
				// servers may not have access to the accounts table in all deployments.
				//
				// Build and persist BEFORE encrypting. Encryption consumes a sequence number
				// from the connection's server->client AES-GCM nonce context, and the client
				// derives the matching nonce from its own counter — so a token that is
				// encrypted but never sent desynchronises the two counters permanently and
				// every later renewal on this connection silently fails to decrypt. Bailing
				// out on a database error after the counter had already advanced is exactly
				// that case, and it is only survivable today because renewal happens once per
				// connection. Ordering the work this way means every path that can fail does
				// so before the counter moves.
				rawToken = TokenService.BuildToken(
					accountName,
					loginServerId,
					currentSigningKey.KeyId,
					DateTime.UtcNow.AddMinutes(expirationMinutes),
					signingKey,
					accessLevel,
					realIp);

				if (rawToken == null)
				{
					await Log.Warning(LogPrefix, $"Renewal token generation failed for '{accountName}'.");
					return false;
				}

				string tokenHash = TokenService.HashToken(rawToken);

				if (Server.Database?.ServiceRegistry == null ||
					!Server.Database.ServiceRegistry.TryGet<IAuthTokenService>(out var tokenSvc))
				{
					await Log.Warning(LogPrefix, $"Renewal token skipped for '{accountName}': IAuthTokenService unavailable.");
					return false;
				}

				var r = await tokenSvc.IssueAsync(tokenHash, accountName, loginServerId, DateTime.UtcNow.AddMinutes(expirationMinutes));
				if (!r.IsSuccess)
				{
					await Log.Warning(LogPrefix, $"Renewal IssueAsync DB error for '{accountName}': {r.ErrorCode} - {r.ErrorMessage}");
					return false;
				}

				ct.ThrowIfCancellationRequested();
				if (!conn.IsActive)
				{
					// Connection died while the row was being written. Nothing was encrypted,
					// so the nonce counter is untouched and the orphaned row simply expires.
					return false;
				}

				// FishNet recycles ClientIds, so confirm this renewal still belongs to the
				// connection it was scheduled for before touching the encryption channel. The
				// captured encryption data would already have been cleared for a recycled
				// connection and the encrypt below would throw, but failing the cheap identity
				// check first keeps the intent explicit.
				if (expectedState != null &&
					(!renewalStates.TryGetValue(conn.ClientId, out TokenRenewalState currentState) ||
					 !ReferenceEquals(currentState, expectedState)))
				{
					return false;
				}

				// Past this point the send counter has advanced and the client's receive
				// counter must see exactly this message. Encrypt and hand it straight to the
				// main thread with nothing in between that can fail and swallow it.
				byte[] encryptedToken;
				try
				{
					encryptedToken = TokenService.EncryptTokenForSend(rawToken, encryptionData);
				}
				catch (Exception ex)
				{
					await Log.Error(LogPrefix, $"Renewal token encryption failed for '{accountName}': {ex.Message}");
					return false;
				}

				EnqueueMainThreadAction(() =>
				{
					if (!conn.IsActive)
					{
						return;
					}
					try
					{
						NetworkManager.ServerManager.Broadcast(conn,
							new RenewTokenResponseBroadcast
							{
								Token = encryptedToken,
								Result = ClientAuthenticationResult.LoginSuccess,
								Seq = 0,
							},
							false, Channel.Reliable);
					}
					catch (Exception ex)
					{
						// The counter advanced but the client will never receive this
						// message, so its receive counter can no longer track ours and no
						// future renewal on this connection could be decrypted. Drop the
						// connection so the client reconnects and re-establishes a channel
						// instead of silently drifting toward an expired token.
						_ = Log.Error(LogPrefix,
							$"Renewal token broadcast failed for '{accountName}' — disconnecting to force a clean re-auth: {ex.Message}");
						try { conn.Disconnect(true); } catch { /* best effort */ }
					}
				});

				return true;
			}
			finally
			{
				if (rawToken != null)
				{
					CryptographicOperationsCompat.ZeroMemory(rawToken);
				}
				CryptographicOperationsCompat.ZeroMemory(signingKey);
			}
		}

		/// <summary>
		/// Runs a renewal for <paramref name="conn"/> under the per-connection in-flight guard
		/// and reschedules the next attempt based on the outcome.
		/// </summary>
		/// <remarks>
		/// The guard is what makes repeated renewal safe. Two overlapping renewals would each
		/// take a sequence number from the shared send-nonce context and could then be queued
		/// to the main thread in the opposite order, so the client would see the higher
		/// sequence first and reject both — and every renewal after them. Serialising per
		/// connection keeps the sequence the client observes strictly increasing.
		/// </remarks>
		private async Task RunRenewalAsync(NetworkConnection conn, TokenRenewalState state)
		{
			bool issued = false;
			try
			{
				issued = await IssueRenewalTokenCoreAsync(conn, state, state.AccountName, state.AccessLevel, state.LoginServerId, ShutdownToken);
			}
			catch (OperationCanceledException)
			{
				// Server shutting down — leave the schedule untouched.
			}
			catch (Exception ex)
			{
				await Log.Error(LogPrefix, $"Token renewal threw for '{state.AccountName}': {ex.Message}");
			}
			finally
			{
				DateTime nowUtc = DateTime.UtcNow;
				if (issued)
				{
					state.ConsecutiveFailures = 0;
					state.NextAttemptUtc = nowUtc + RenewalInterval;
				}
				else
				{
					// Back off on repeated failure so a database outage does not turn the
					// sweep into a per-tick retry storm, but never past the point where the
					// token would expire — a late retry is still better than none.
					state.ConsecutiveFailures = Math.Min(state.ConsecutiveFailures + 1, 8);
					double backoffSeconds = RenewalRetryBaseDelay.TotalSeconds * Math.Pow(2, state.ConsecutiveFailures - 1);
					double capSeconds = RenewalInterval.TotalSeconds;
					state.NextAttemptUtc = nowUtc + TimeSpan.FromSeconds(Math.Min(backoffSeconds, capSeconds));
				}
				Interlocked.Exchange(ref state.InFlight, 0);
			}
		}

		/// <summary>
		/// Re-mints auth tokens for connections whose current token is halfway through its
		/// lifetime, so a client that stays connected to one server indefinitely still holds a
		/// token that will be accepted by the next server it hops to.
		/// </summary>
		private void SweepTokenRenewals()
		{
			renewalSweepAccumulator += Time.unscaledDeltaTime;
			if (renewalSweepAccumulator < RenewalSweepIntervalSeconds)
			{
				return;
			}
			renewalSweepAccumulator = 0f;

			if (renewalStates.IsEmpty)
			{
				return;
			}

			DateTime nowUtc = DateTime.UtcNow;
			var clients = NetworkManager?.ServerManager?.Clients;

			foreach (var kvp in renewalStates)
			{
				int clientId = kvp.Key;
				TokenRenewalState state = kvp.Value;

				NetworkConnection conn = null;
				if (clients != null)
				{
					clients.TryGetValue(clientId, out conn);
				}

				// Backstop for a Stopped event that never arrived; the normal removal path is
				// OnConnectionStopped.
				if (conn == null || !conn.IsActive || !conn.IsAuthenticated)
				{
					if (Volatile.Read(ref state.InFlight) == 0)
					{
						renewalStates.TryRemove(clientId, out _);
					}
					continue;
				}

				if (nowUtc < state.NextAttemptUtc)
				{
					continue;
				}

				if (Interlocked.CompareExchange(ref state.InFlight, 1, 0) != 0)
				{
					continue;
				}

				_ = RunRenewalAsync(conn, state);
			}
		}

		#endregion

		#region Inner Core (bridges FishNet callbacks to TokenAuthenticatorCore)

		/// <summary>
		/// Inner sealed implementation of <see cref="TokenAuthenticatorCore{TConnection}"/> bound to
		/// <see cref="NetworkConnection"/>. All abstract callbacks route to <see cref="outer"/>
		/// (the enclosing <see cref="TokenServerAuthenticator"/>), which provides FishNet broadcasts,
		/// DB access, and event invocation.
		/// </summary>
		private sealed class TokenCore : TokenAuthenticatorCore<NetworkConnection>
		{
			/// <summary>The enclosing <see cref="TokenServerAuthenticator"/> instance that hosts this core.</summary>
			private readonly TokenServerAuthenticator outer;

			/// <summary>
			/// Initializes the core with the enclosing authenticator and the token account manager.
			/// </summary>
			public TokenCore(TokenServerAuthenticator outer, ITokenAccountManager<NetworkConnection> accountManager)
				: base(accountManager) => this.outer = outer;

			// ── BaseAuthenticatorCore abstracts ──────────────────────────────
			/// <inheritdoc/>
			protected override bool IsConnectionAuthenticated(NetworkConnection conn) => conn.IsAuthenticated;
			/// <inheritdoc/>
			protected override string GetConnectionAddress(NetworkConnection conn) => conn.GetAddress();
			/// <inheritdoc/>
			protected override int GetConnectionClientId(NetworkConnection conn) => conn.ClientId;
			/// <inheritdoc/>
			protected override string ResolveRateLimitKey(NetworkConnection conn) => outer.ResolveRateLimitKey(conn);

			/// <inheritdoc/>
			protected override void BroadcastCookieChallenge(NetworkConnection conn, byte[] cookie) =>
				outer.NetworkManager.ServerManager.Broadcast(conn,
					new ServerHandshake { Cookie = cookie }, false, Channel.Reliable);

			/// <inheritdoc/>
			protected override void BroadcastServerHandshake(NetworkConnection conn, byte[] key, ushort version) =>
				outer.NetworkManager.ServerManager.Broadcast(conn,
					new ServerHandshake { PublicKey = key, AgreedVersion = version }, false, Channel.Reliable);

			/// <inheritdoc/>
			protected override void DisconnectConnection(NetworkConnection conn, bool graceful) =>
				conn.Disconnect(graceful);

			// ── TokenAuthenticatorCore abstracts ─────────────────────────────
			/// <inheritdoc/>
			protected override bool IsConnectionActive(NetworkConnection conn) => conn.IsActive;

			/// <inheritdoc/>
			protected override void OnAuthenticationResult(NetworkConnection conn, bool authenticated)
			{
				outer.OnAuthentication(conn, authenticated);
				outer.InvokeClientAuthenticationResult(conn, authenticated);
			}

			/// <inheritdoc/>
			protected override void BroadcastAuthResult(NetworkConnection conn, ClientAuthenticationResult result, bool reliable) =>
				outer.NetworkManager.ServerManager.Broadcast(conn,
					new ClientAuthResultBroadcast { Result = result }, false,
					reliable ? Channel.Reliable : Channel.Unreliable);

			/// <inheritdoc/>
			protected override void EnqueueMainThread(NetworkConnection conn, Action action) =>
				outer.EnqueueMainThreadAction(action);

			/// <inheritdoc/>
			protected override Task<ClientAuthenticationResult> TryLoginAsync(ClientAuthenticationResult defaultResult, string username) =>
				outer.TryLoginAsync(defaultResult, username);

			// ── DB callbacks ─────────────────────────────────────────────────
			/// <inheritdoc/>
			protected override Task<byte[]> FetchSigningKeyAsync(long loginServerId, long signingKeyId) =>
				outer.FetchSigningKeyCoreAsync(loginServerId, signingKeyId);

			/// <inheritdoc/>
			protected override Task<bool> CheckTokenRevocationAsync(string tokenHash) =>
				outer.CheckTokenRevocationCoreAsync(tokenHash);

			/// <inheritdoc/>
			protected override bool RequiresRealIp => outer.RequiresConnectionToken;

			/// <inheritdoc/>
			/// <inheritdoc/>
			protected override void StoreClientRealIp(NetworkConnection conn, string realIp)
			{
				// Store the real IP recovered from the auth token for rate limiting.
				// This is essential for World/Scene servers behind an L4 proxy where
				// conn.GetAddress() returns 127.0.0.1.
				//
				// Goes through the authenticator's own store rather than writing straight to
				// IAccountCreationSystemRuntimeData: that container is registered only by
				// AccountCreationSystem (a Login Server system), so on World and Scene — the
				// very servers this override exists for — the write silently went nowhere.
				outer.StoreRealIpForConnection(conn.ClientId, realIp);
			}

			/// <inheritdoc/>
			protected override Task OnTokenAuthSuccessAsync(NetworkConnection conn, string accountName, AccessLevel accessLevel, long loginServerId) =>
				outer.HandleTokenAuthSuccessAsync(conn, accountName, accessLevel, loginServerId);
		}

		#endregion
	}
}