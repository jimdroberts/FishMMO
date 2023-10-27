using FishMMO.Database;
using FishMMO.Shared;

namespace FishMMO.Server
{
	// Scene Server Authenticator, Scene Authenticator connects with basic password authentication.
	public class SceneServerAuthenticator : LoginServerAuthenticator
	{
		/// <summary>
		/// Executed when a player tries to login to the Scene Server.
		/// </summary>
		internal override ClientAuthenticationResult TryLogin(ServerDbContext dbContext, ClientAuthenticationResult result, string username)
		{
			return ClientAuthenticationResult.SceneLoginSuccess;
		}
	}
}