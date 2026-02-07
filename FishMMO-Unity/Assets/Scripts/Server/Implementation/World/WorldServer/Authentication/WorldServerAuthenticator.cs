using System.Threading.Tasks;
using FishNet.Connection;
using FishMMO.Database;
using FishMMO.Database.Data;
using FishMMO.Database.Npgsql.Services.Interfaces;
using FishMMO.Server.Core.World.WorldServer;
using FishMMO.Shared;

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
		public uint MaxPlayers = 5000;

		/// <summary>
		/// Attempts to authenticate a client login and assign the character to the world server.
		/// Returns a result indicating success, failure, or server full.
		/// </summary>
		/// <param name="result">Initial authentication result.</param>
		/// <param name="username">Username of the client attempting to log in.</param>
		/// <returns>ClientAuthenticationResult indicating the outcome.</returns>
		internal override async Task<ClientAuthenticationResult> TryLoginAsync(ClientAuthenticationResult result, string username)
		{
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

			// If login is successful, assign the character to the world server.
			if (result == ClientAuthenticationResult.LoginSuccess &&
				Server.DataContainerRegistry.TryGet<IWorldServerSystemRuntimeData>(out var worldData))
			{
				// Verify the account has a selected character
				DatabaseResult<CharacterData?> fetchResult = await characterService.FetchByAccountAsync(username, selected: true);
				if (fetchResult.IsSuccess && fetchResult.Data.HasValue)
				{
					return ClientAuthenticationResult.WorldLoginSuccess;
				}
			}

			return result;
		}
	}
}