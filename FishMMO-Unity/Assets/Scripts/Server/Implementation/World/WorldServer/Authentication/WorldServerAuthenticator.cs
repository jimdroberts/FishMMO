using System.Threading.Tasks;
using FishNet.Connection;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Server.Core.World.WorldServer;
using FishMMO.Shared;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.WorldServer
{
	/// <summary>
	/// Authenticator for world server connections, allowing clients to connect with basic password authentication.
	/// Handles player limit and world assignment on login.
	/// </summary>
	public class WorldServerAuthenticator : ServerAuthenticator
	{
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
	}
}