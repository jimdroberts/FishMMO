using FishMMO.Database.Npgsql;
using FishMMO.Server.DatabaseServices;
using FishMMO.Shared;

namespace FishMMO.Server
{
	/// <summary>
	/// Authenticator for world server connections, allowing clients to connect with basic password authentication.
	/// Handles player limit and world assignment on login.
	/// </summary>
	public class WorldServerAuthenticator : LoginServerAuthenticator
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
			if (ServerBehaviour.TryGet(out WorldSceneSystem worldSceneSystem) &&
				worldSceneSystem.ConnectionCount >= MaxPlayers)
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
				ServerBehaviour.TryGet(out WorldServerSystem worldServerSystem) &&
				CharacterService.GetSelected(dbContext, username))
			{
				// Update the character's world assignment in the database.
				CharacterService.SetWorld(dbContext, username, worldServerSystem.ID);

				return ClientAuthenticationResult.WorldLoginSuccess;
			}
			return result;
		}
	}
}