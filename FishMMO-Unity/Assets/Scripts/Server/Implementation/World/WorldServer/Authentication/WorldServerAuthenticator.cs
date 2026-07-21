using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using FishNet.Connection;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Server.Core.Collections;
using FishMMO.Server.Core.World.WorldServer;
using FishMMO.Shared;
using FishMMO.Auth.Core;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.WorldServer
{
	/// <summary>
	/// Authenticator for world server connections using token-based authentication.
	/// Handles player limit and world assignment on login.
	/// </summary>
	public class WorldServerAuthenticator : TokenServerAuthenticator
	{
		/// <summary>
		/// Debounce window in seconds for TryLoginAsync per account.
		/// </summary>
		[Tooltip("Debounce window in seconds for TryLoginAsync per account")]
		[SerializeField] private float loginAttemptDebounceSeconds = 1.0f;

		/// <summary>
		/// Maximum entries to scan per sweep cycle.
		/// </summary>
		[Tooltip("Maximum entries to scan per auth sweep cycle")]
		[SerializeField] private int sweepMaxScan = 128;

		/// <summary>
		/// Maximum entries to remove per sweep cycle.
		/// </summary>
		[Tooltip("Maximum entries to remove per auth sweep cycle")]
		[SerializeField] private int sweepMaxRemove = 64;

		/// <summary>
		/// Window in seconds during which a recently admitted username still counts against the
		/// <see cref="MaxPlayers"/> cap, even if the DB-derived <c>ConnectionCount</c>
		/// has not yet been refreshed by <c>UpdateConnectionCountAsync</c>.
		/// Closes the read-then-admit race where N concurrent token authentications
		/// all observe the same pre-refresh count and slip past the cap together.
		/// </summary>
		[Tooltip("Seconds for recent-admission window that bounds burst-admission race")]
		[SerializeField] private float recentAdmissionWindowSeconds = 30.0f;

		/// <summary>
		/// Tracks last TryLoginAsync attempt time per account for rate limiting.
		/// Prevents repeated expensive DB calls (FetchByAccountAsync) from rapid re-auth attempts.
		/// Entries expire automatically and are swept via <see cref="OnAuthSweep"/>.
		/// </summary>
		private readonly ExpiringKeyTracker<string> loginAttemptByAccount =
			new ExpiringKeyTracker<string>(StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// Per-account "recently admitted" timestamps, used to bound the burst-admission
		/// race window described on <see cref="TimeSpan.FromSeconds(recentAdmissionWindowSeconds)"/>. Periodically swept.
		/// </summary>
		private readonly ConcurrentDictionary<string, DateTime> recentAdmissionsByAccount =
			new ConcurrentDictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// Maximum number of players allowed to connect to the world server.
		/// </summary>
		[SerializeField] private uint maxPlayers = 5000;

		/// <summary>
		/// Maximum number of players allowed to connect to the world server.
		/// </summary>
		public uint MaxPlayers => maxPlayers;

		/// <summary>
		/// Attempts to authenticate a client login and assign the character to the world server.
		/// Returns a result indicating success, failure, or server full.
		/// </summary>
		/// <param name="result">Initial authentication result.</param>
		/// <param name="username">Username of the client attempting to log in.</param>
		/// <returns>ClientAuthenticationResult indicating the outcome.</returns>
		internal override async Task<ClientAuthenticationResult> TryLoginAsync(ClientAuthenticationResult result, string username)
		{
			if (result != ClientAuthenticationResult.LoginSuccess)
			{
				return result;
			}

			if (string.IsNullOrWhiteSpace(username))
			{
				return ClientAuthenticationResult.InvalidUsernameOrPassword;
			}

			// Rate-limit TryLoginAsync per account to prevent repeated expensive DB calls.
			if (!loginAttemptByAccount.TryBegin(username, DateTime.UtcNow, TimeSpan.FromSeconds(loginAttemptDebounceSeconds)))
			{
				await Log.Warning("WorldServerAuthenticator", $"Rate-limited TryLoginAsync for account '{username}'");
				return ClientAuthenticationResult.ServerBusy;
			}

			if (Server.DataContainerRegistry.TryGet<IWorldServerSystemRuntimeData>(out var worldData) && worldData.IsLocked)
			{
				loginAttemptByAccount.Remove(username);
				return ClientAuthenticationResult.ServerFull;
			}

			// Atomic admission check: combine the DB-derived ConnectionCount with the number
			// of *recently admitted* usernames whose impact may not have reached the DB yet.
			// Without this, N concurrent token-auth workers can each observe the same
			// pre-refresh ConnectionCount and all squeeze past a check that says "one slot
			// left". The conservative direction here is "slightly under-admit" if a username
			// is already counted in ConnectionCount; the cost is at most one rejected reconnect
			// within the 30 s window, which the client retries.
			int sceneCount = Server.DataContainerRegistry.TryGet<IWorldSceneMappingData<NetworkConnection>>(out var sceneData)
				? sceneData.ConnectionCount
				: 0;
			int recentCount = CountRecentAdmissions(DateTime.UtcNow);
			if ((long)sceneCount + recentCount >= MaxPlayers)
			{
				loginAttemptByAccount.Remove(username);
				return ClientAuthenticationResult.ServerFull;
			}

			if (Server.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var characterService))
			{
				loginAttemptByAccount.Remove(username);
				return ClientAuthenticationResult.ServerBusy;
			}

			// If login is successful, verify the account has a selected character before world entry.
			DatabaseResult<CharacterData?> fetchResult = await characterService.FetchByAccountAsync(username, selected: true);
			if (!fetchResult.IsSuccess)
			{
				loginAttemptByAccount.Remove(username);
				return ClientAuthenticationResult.ServerBusy;
			}

			if (fetchResult.Data.HasValue)
			{
				// Reserve a slot for the brief window before UpdateConnectionCountAsync
				// notices this admission. Repeated admissions for the same username (e.g.
				// fast reconnect) overwrite the timestamp rather than double-counting.
				recentAdmissionsByAccount[username] = DateTime.UtcNow;
				return ClientAuthenticationResult.WorldLoginSuccess;
			}

			// No selected character: don't penalise the user with a 1 s debounce on a
			// terminal failure they cannot fix without going to character selection first.
			loginAttemptByAccount.Remove(username);
			return ClientAuthenticationResult.NoCharacterSelected;
		}

		/// <summary>
		/// Returns the number of distinct usernames admitted within the last
		/// <see cref="TimeSpan.FromSeconds(recentAdmissionWindowSeconds)"/>. Inline sweep — the map is small and this
		/// method is called once per <see cref="TryLoginAsync"/>, which is rate-limited.
		///
		/// <para><b>Snapshot semantics:</b> Iterating a <see cref="ConcurrentDictionary{TKey,TValue}"/>
		/// produces a point-in-time snapshot. Entries that expire (fall below the cutoff)
		/// <i>after</i> the snapshot is taken will be missed until the next sweep. This is
		/// acceptable because the admission window is a best-effort guard against burst admissions;
		/// a transient over-admission is bounded by the sweep rate and corrected on the next call.
		/// The conservative direction (under-admit) is enforced by the DB-derived <c>ConnectionCount</c>.</para>
		///
		/// <para><b>Inline sweep correctness:</b> Calling <see cref="ConcurrentDictionary{TKey,TValue}.TryRemove"/>
		/// during <c>foreach</c> enumeration is safe because <c>ConcurrentDictionary</c> iteration tolerates
		/// concurrent additions and removals (it snapshots keys and does not throw). Removing expired entries
		/// inline avoids allocating a separate collection of keys to purge, at the cost of slightly enlarged
		/// dictionary size between sweeps. The <see cref="OnAuthSweep"/> method provides a bounded fallback
		/// sweep for entries that expire between <c>CountRecentAdmissions</c> calls.</para>
		/// </summary>
		private int CountRecentAdmissions(DateTime now)
		{
			DateTime cutoff = now - TimeSpan.FromSeconds(recentAdmissionWindowSeconds);
			int count = 0;
			foreach (var kvp in recentAdmissionsByAccount)
			{
				if (kvp.Value >= cutoff)
				{
					count++;
				}
				else
				{
					recentAdmissionsByAccount.TryRemove(kvp.Key, out _);
				}
			}
			return count;
		}

		/// <summary>
		/// Sweeps expired login-attempt rate-limit entries to prevent unbounded memory growth.
		/// </summary>
		protected override void OnAuthSweep()
		{
			base.OnAuthSweep();
			loginAttemptByAccount.SweepExpired(DateTime.UtcNow, sweepMaxScan, sweepMaxRemove);

			// Bounded sweep of the recent-admission map. Keeps the dictionary from
			// retaining stale entries across server uptime even if no new logins arrive.
			// NOTE: TryRemove during foreach is safe because ConcurrentDictionary
			// iteration produces a point-in-time snapshot and tolerates concurrent
			// removals. See CountRecentAdmissions for the full rationale.
			DateTime cutoff = DateTime.UtcNow - TimeSpan.FromSeconds(recentAdmissionWindowSeconds);
			int scanned = 0;
			foreach (var kvp in recentAdmissionsByAccount)
			{
				if (++scanned > sweepMaxScan) break;
				if (kvp.Value < cutoff)
				{
					recentAdmissionsByAccount.TryRemove(kvp.Key, out _);
				}
			}
		}
	}
}