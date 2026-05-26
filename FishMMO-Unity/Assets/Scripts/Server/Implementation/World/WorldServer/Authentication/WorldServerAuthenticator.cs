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
		/// Debounce window for TryLoginAsync per account.
		/// </summary>
		private static readonly TimeSpan LoginAttemptDebounceWindow = TimeSpan.FromSeconds(1.0);

		/// <summary>
		/// Maximum entries to scan per sweep cycle.
		/// </summary>
		private const int SweepMaxScan = 128;

		/// <summary>
		/// Maximum entries to remove per sweep cycle.
		/// </summary>
		private const int SweepMaxRemove = 64;

		/// <summary>
		/// Window during which a recently admitted username still counts against the
		/// <see cref="MaxPlayers"/> cap, even if the DB-derived <c>ConnectionCount</c>
		/// has not yet been refreshed by <c>UpdateConnectionCountAsync</c>.
		/// Closes the read-then-admit race where N concurrent token authentications
		/// all observe the same pre-refresh count and slip past the cap together.
		/// </summary>
		private static readonly TimeSpan RecentAdmissionWindow = TimeSpan.FromSeconds(30.0);

		/// <summary>
		/// Tracks last TryLoginAsync attempt time per account for rate limiting.
		/// Prevents repeated expensive DB calls (FetchByAccountAsync) from rapid re-auth attempts.
		/// Entries expire automatically and are swept via <see cref="OnAuthSweep"/>.
		/// </summary>
		private readonly ExpiringKeyTracker<string> loginAttemptByAccount =
			new ExpiringKeyTracker<string>(StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// Per-account "recently admitted" timestamps, used to bound the burst-admission
		/// race window described on <see cref="RecentAdmissionWindow"/>. Periodically swept.
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
			if (!loginAttemptByAccount.TryBegin(username, DateTime.UtcNow, LoginAttemptDebounceWindow))
			{
				await Log.Warning("WorldServerAuthenticator", $"Rate-limited TryLoginAsync for account '{username}'");
				return ClientAuthenticationResult.ServerBusy;
			}

			if (Server.DataContainerRegistry.TryGet<IWorldServerSystemRuntimeData>(out var worldData) && worldData.IsLocked)
			{
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
				return ClientAuthenticationResult.ServerFull;
			}

			if (Server.Database?.ServiceRegistry == null ||
				!Server.Database.ServiceRegistry.TryGet<ICharacterService>(out var characterService))
			{
				return ClientAuthenticationResult.ServerBusy;
			}

			// If login is successful, verify the account has a selected character before world entry.
			DatabaseResult<CharacterData?> fetchResult = await characterService.FetchByAccountAsync(username, selected: true);
			if (!fetchResult.IsSuccess)
			{
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

			return ClientAuthenticationResult.NoCharacterSelected;
		}

		/// <summary>
		/// Returns the number of distinct usernames admitted within the last
		/// <see cref="RecentAdmissionWindow"/>. Inline sweep — the map is small and this
		/// method is called once per <see cref="TryLoginAsync"/>, which is rate-limited.
		/// </summary>
		private int CountRecentAdmissions(DateTime now)
		{
			DateTime cutoff = now - RecentAdmissionWindow;
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
			loginAttemptByAccount.SweepExpired(DateTime.UtcNow, SweepMaxScan, SweepMaxRemove);

			// Bounded sweep of the recent-admission map. Keeps the dictionary from
			// retaining stale entries across server uptime even if no new logins arrive.
			DateTime cutoff = DateTime.UtcNow - RecentAdmissionWindow;
			int scanned = 0;
			foreach (var kvp in recentAdmissionsByAccount)
			{
				if (++scanned > SweepMaxScan) break;
				if (kvp.Value < cutoff)
				{
					recentAdmissionsByAccount.TryRemove(kvp.Key, out _);
				}
			}
		}
	}
}