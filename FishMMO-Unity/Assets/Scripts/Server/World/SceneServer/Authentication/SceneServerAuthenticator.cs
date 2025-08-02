using FishMMO.Database.Npgsql;
using FishMMO.Shared;

namespace FishMMO.Server
{
	/// <summary>
	/// Scene Server Authenticator for basic password authentication on scene servers.
	/// </summary>
	public class SceneServerAuthenticator : LoginServerAuthenticator
	{
		/// <summary>
		/// Executed when a player tries to login to the Scene Server.
		/// Always returns SceneLoginSuccess for scene server authentication.
		/// </summary>
		/// <param name="dbContext">Database context for authentication.</param>
		/// <param name="result">Initial authentication result.</param>
		/// <param name="username">Username of the player attempting login.</param>
		/// <returns>SceneLoginSuccess result for successful authentication.</returns>
		internal override ClientAuthenticationResult TryLogin(NpgsqlDbContext dbContext, ClientAuthenticationResult result, string username)
		{
			return ClientAuthenticationResult.SceneLoginSuccess;
		}
	}
}