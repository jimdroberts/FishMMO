using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;
using FishNet.Connection;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Server.Core;
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
		/// <remarks>
		/// Timed on <see cref="MonotonicClock"/> by both its callers: a debounce is a duration, and on
		/// the wall clock a host clock stepped back refused an account's sign-in for the size of the step.
		/// </remarks>
		private readonly ExpiringKeyTracker<string> loginAttemptByAccount =
			new ExpiringKeyTracker<string>(StringComparer.OrdinalIgnoreCase);

		/// <summary>
		/// Accounts admitted within the last <see cref="recentAdmissionWindowSeconds"/>, keyed by
		/// account, one window per admission on the monotonic clock, used to bound the
		/// burst-admission race (see <see cref="TryLoginAsync"/>). Re-admitting an account restarts
		/// its window at the back.
		/// </summary>
		/// <remarks>
		/// Windows are kept in the order they opened, so expiry is swept head-first. It was a
		/// dictionary swept by enumerating at most <see cref="sweepMaxScan"/> entries from its head,
		/// which never reached an expired entry sitting behind that many live ones — and the
		/// admission count those entries fed never came back down, so under a steady login rate the
		/// world answered ServerFull well short of <see cref="MaxPlayers"/> real players. Monotonic
		/// because the window is a local duration: a wall-clock step back would have held every
		/// reservation for the length of the step.
		/// <para>
		/// Created on first use: its window comes from a serialized field, which Unity has not
		/// applied yet when field initializers run.
		/// </para>
		/// </remarks>
		private FishMMO.Auth.Core.Collections.FixedWindowCounter<string> recentAdmissionsByAccount;

		/// <summary>
		/// The recent-admission windows, or null when the reservation is disabled
		/// (<see cref="recentAdmissionWindowSeconds"/> zero or less).
		/// </summary>
		private FishMMO.Auth.Core.Collections.FixedWindowCounter<string> RecentAdmissions
		{
			get
			{
				if (recentAdmissionWindowSeconds <= 0f)
				{
					return null;
				}
				// Token-auth workers and the main thread both get here; EnsureInitialized
				// publishes exactly one instance.
				return LazyInitializer.EnsureInitialized(ref recentAdmissionsByAccount,
					() => new FishMMO.Auth.Core.Collections.FixedWindowCounter<string>(
						TimeSpan.FromSeconds(recentAdmissionWindowSeconds), StringComparer.OrdinalIgnoreCase));
			}
		}

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
			if (!loginAttemptByAccount.TryBegin(username, MonotonicClock.NowSeconds, TimeSpan.FromSeconds(loginAttemptDebounceSeconds)))
			{
				await Log.Warning("WorldServerAuthenticator", $"Rate-limited TryLoginAsync for account '{username}'");
				return ClientAuthenticationResult.ServerBusy;
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
			int recentCount = CountRecentAdmissions(MonotonicClock.NowSeconds);
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

			/* If login is successful, verify the account has a selected character before world entry,
			 * and read its staff lock with it. One read: the lock columns are on the same row, and
			 * reading it a second time just for them doubled what every world login cost the
			 * database. A failed read fails closed for the lock as it always did. */
			DatabaseResult<(CharacterData Character, CharacterLockState Lock)?> fetchResult = await characterService.FetchSelectedWithLockAsync(username);
			if (!fetchResult.IsSuccess)
			{
				await Log.Warning("WorldServerAuthenticator", $"Selected character fetch failed for account '{username}': [{fetchResult.ErrorCode}] {fetchResult.ErrorMessage}. Answering ServerBusy (fail-closed).");
				loginAttemptByAccount.Remove(username);
				return ClientAuthenticationResult.ServerBusy;
			}

			if (fetchResult.Data.HasValue)
			{
				CharacterData selected = fetchResult.Data.Value.Character;

				/* A staff character lock. Character select refuses a locked character, but a client
				 * that already holds a token can come straight here — a kicked client reconnecting —
				 * and the account's selection still points at the locked character. A locked
				 * character answers NoCharacterSelected: the client goes back to character select,
				 * whose own refusal says who locked it and until when. */
				if (fetchResult.Data.Value.Lock.IsLocked(DateTime.UtcNow))
				{
					loginAttemptByAccount.Remove(username);
					await Log.Info("WorldServerAuthenticator", $"Account '{username}' tried to enter the world with a character locked by staff; refusing.");
					return ClientAuthenticationResult.NoCharacterSelected;
				}

				/* The lock check happens here, after the character is known, because a lock is
				 * not absolute: it closes the world to players while leaving it open to the
				 * people who have to go in and work on it. Locking a world for maintenance and
				 * thereby locking out every administrator would make the feature unusable for
				 * its only purpose.
				 *
				 * The access level comes from the character row this server just read, never
				 * from the client. A scheduled shutdown admits nobody at all, elevated or not —
				 * the world is about to stop, and letting anyone in to be disconnected moments
				 * later helps no one.
				 *
				 * ServerLocked rather than ServerFull: a locked world is not a busy one, and
				 * telling the player it is full sends them away to retry against a condition
				 * that will not clear on its own. */
				if (Server.DataContainerRegistry.TryGet<IWorldServerSystemRuntimeData>(out var worldData))
				{
					AccessLevel accessLevel = (AccessLevel)(int)selected.AccessLevel;

					if (worldData.ShutdownAtUtc.HasValue)
					{
						loginAttemptByAccount.Remove(username);
						return ClientAuthenticationResult.ServerLocked;
					}

					if (worldData.IsLocked && accessLevel <= AccessLevel.Player)
					{
						loginAttemptByAccount.Remove(username);
						return ClientAuthenticationResult.ServerLocked;
					}
				}

				// Reserve a slot for the brief window before UpdateConnectionCountAsync
				// notices this admission. Repeated admissions for the same username (e.g.
				// fast reconnect) refresh the timestamp rather than double-counting.
				var recentAdmissions = RecentAdmissions;
				if (recentAdmissions != null)
				{
					recentAdmissions.Remove(username);
					recentAdmissions.Increment(username, MonotonicClock.NowSeconds);
				}
				return ClientAuthenticationResult.WorldLoginSuccess;
			}

			// No selected character: don't penalise the user with a 1 s debounce on a
			// terminal failure they cannot fix without going to character selection first.
			loginAttemptByAccount.Remove(username);
			return ClientAuthenticationResult.NoCharacterSelected;
		}

		/// <summary>
		/// Returns the number of distinct usernames admitted within the recent-admission window.
		/// </summary>
		/// <remarks>
		/// Expired admissions are swept first, head-first, so the count is exact at the moment it
		/// is read: the sweep costs one comparison per admission that has expired since the last
		/// one, plus one. A per-frame sweep in <see cref="OnAuthSweep"/> keeps that backlog small
		/// and reclaims memory when no one is logging in.
		/// </remarks>
		private int CountRecentAdmissions(double nowSeconds)
		{
			var recentAdmissions = RecentAdmissions;
			if (recentAdmissions == null)
			{
				return 0;
			}
			recentAdmissions.SweepExpired(nowSeconds, int.MaxValue);
			return recentAdmissions.Count;
		}

		/// <summary>
		/// Sweeps expired login-attempt rate-limit entries to prevent unbounded memory growth.
		/// </summary>
		protected override void OnAuthSweep()
		{
			base.OnAuthSweep();
			double nowSeconds = MonotonicClock.NowSeconds;
			loginAttemptByAccount.SweepExpired(nowSeconds, sweepMaxScan, sweepMaxRemove);

			// Head-first over admissions in the order they happened, so the oldest expired
			// ones always go first and a frame with nothing expired costs one comparison.
			RecentAdmissions?.SweepExpired(nowSeconds, sweepMaxRemove);
		}
	}
}