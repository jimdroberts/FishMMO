using System;
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
		/// Tracks last TryLoginAsync attempt time per account for rate limiting.
		/// Prevents repeated expensive DB calls (FetchByAccountAsync) from rapid re-auth attempts.
		/// Entries expire automatically and are swept via <see cref="OnAuthSweep"/>.
		/// </summary>
		private readonly ExpiringKeyTracker<string> loginAttemptByAccount =
			new ExpiringKeyTracker<string>(StringComparer.OrdinalIgnoreCase);

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

			// Check if the world server is full.
			if (Server.DataContainerRegistry.TryGet<IWorldSceneMappingData<NetworkConnection>>(out var sceneData) &&
				sceneData.ConnectionCount >= MaxPlayers)
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
				return ClientAuthenticationResult.WorldLoginSuccess;
			}

			return ClientAuthenticationResult.NoCharacterSelected;
		}

		/// <summary>
		/// Sweeps expired login-attempt rate-limit entries to prevent unbounded memory growth.
		/// </summary>
		protected override void OnAuthSweep()
		{
			base.OnAuthSweep();
			loginAttemptByAccount.SweepExpired(DateTime.UtcNow, SweepMaxScan, SweepMaxRemove);
		}
	}
}