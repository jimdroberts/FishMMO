using FishNet.Connection;
using FishMMO.Database.Npgsql;
using FishMMO.Server.Core.World.WorldServer;
using FishMMO.Server.DatabaseServices;
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
		/// <param name="dbContext">Database context for authentication queries.</param>
		/// <param name="result">Initial authentication result.</param>
		/// <param name="username">Username of the client attempting to log in.</param>
		/// <returns>ClientAuthenticationResult indicating the outcome.</returns>
		internal override ClientAuthenticationResult TryLogin(NpgsqlDbContext dbContext, ClientAuthenticationResult result, string username)
		{
			// Check if the world server is full.
			if (Server.DataContainerRegistry.TryGet<IWorldSceneMappingData<NetworkConnection>>(out var sceneData) &&
				sceneData.ConnectionCount >= MaxPlayers)
			{
				return ClientAuthenticationResult.ServerFull;
			}
			// Check for valid database context.
			else if (dbContext == null)
			{
				return ClientAuthenticationResult.InvalidUsernameOrPassword;
			}
			// If login is successful, assign the character to the world server.
			else if (result == ClientAuthenticationResult.LoginSuccess &&
				Server.DataContainerRegistry.TryGet<IWorldServerSystemRuntimeData>(out var worldData) &&
				CharacterService.GetSelected(dbContext, username))
			{
				// Update the character's world assignment in the database.
				CharacterService.SetWorld(dbContext, username, worldData.ID);

				return ClientAuthenticationResult.WorldLoginSuccess;
			}
			return result;
		}
	}
}