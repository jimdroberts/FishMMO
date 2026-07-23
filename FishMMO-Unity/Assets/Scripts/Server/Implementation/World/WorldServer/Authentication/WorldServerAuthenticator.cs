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
		/// Tracks the number of entries in <see cref="recentAdmissionsByAccount"/>.
		/// Updated atomically via Interlocked — incremented on first admission for a
		/// username, decremented on sweep removal. Avoids the systematic undercount that
		/// would occur from iterating a ConcurrentDictionary snapshot while concurrent
		/// admissions add new entries.
		/// </summary>
		private int recentAdmissionCount;

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
			username = Authentication.NormalizeAccountLookup(username);
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
				//
				// TryAdd avoids double-counting when an existing entry is updated.
				recentAdmissionsByAccount.AddOrUpdate(
					username,
					_ => { Interlocked.Increment(ref recentAdmissionCount); return DateTime.UtcNow; },
					(_, _) => DateTime.UtcNow);
				return ClientAuthenticationResult.WorldLoginSuccess;
			}

			// No selected character: don't penalise the user with a 1 s debounce on a
			// terminal failure they cannot fix without going to character selection first.
			loginAttemptByAccount.Remove(username);
			return ClientAuthenticationResult.NoCharacterSelected;
		}

		/// <summary>
		/// Returns the number of distinct usernames admitted within the recent-admission
		/// window. This uses an <see cref="Interlocked"/>-maintained counter rather than
		/// iterating <see cref="recentAdmissionsByAccount"/>, avoiding the systematic
		/// undercount that a foreach snapshot would produce when concurrent admissions
		/// add entries during iteration.
		///
		/// <para>Removals happen in <see cref="OnAuthSweep"/>, which decrements the counter
		/// when it removes expired entries. Between sweeps the counter may include a small
		/// number of expired-but-not-yet-swept entries — this overcount is conservative
		/// (slightly under-admits), which is the safe direction.</para>
		/// </summary>
		private int CountRecentAdmissions(DateTime now) => Thread.VolatileRead(ref recentAdmissionCount);

		/// <summary>
		/// Sweeps expired login-attempt rate-limit entries to prevent unbounded memory growth.
		/// </summary>
		protected override void OnAuthSweep()
		{
			base.OnAuthSweep();
			loginAttemptByAccount.SweepExpired(DateTime.UtcNow, sweepMaxScan, sweepMaxRemove);

			// Bounded sweep of the recent-admission map. Keeps the dictionary from
			// retaining stale entries across server uptime even if no new logins arrive.
			// Decrements the admission counter atomically so CountRecentAdmissions
			// remains consistent without iterating the dictionary.
			DateTime cutoff = DateTime.UtcNow - TimeSpan.FromSeconds(recentAdmissionWindowSeconds);
			int scanned = 0;
			foreach (var kvp in recentAdmissionsByAccount)
			{
				if (++scanned > sweepMaxScan) break;
				if (kvp.Value < cutoff && recentAdmissionsByAccount.TryRemove(kvp.Key, out _))
				{
					Interlocked.Decrement(ref recentAdmissionCount);
				}
			}
		}
	}
}