using FishMMO_DB;
using FishMMO.Server.Services;

namespace FishMMO.Server
{
	// Scene Server Authenticator, Scene Authenticator connects with basic password authentication.
	public class SceneServerAuthenticator : LoginServerAuthenticator
	{
		/// <summary>
		/// Executed when a player tries to login to the Scene Server.
		/// </summary>
		internal override ClientAuthResultBroadcast TryLogin(string username, string password)
		{
			ClientAuthenticationResult result = ClientAuthenticationResult.InvalidUsernameOrPassword;

			if (DBContextFactory != null && IsAllowedUsername(username))
			{
				using ServerDbContext dbContext = DBContextFactory.CreateDbContext(new string[] { });
				if (!CharacterService.TryGetOnline(dbContext, username))
				{
					result = AccountService.TryLogin(dbContext, username, password);
				}
				else
				{
					result = ClientAuthenticationResult.AlreadyOnline;
				}
			}

			// this is easier...
			if (result == ClientAuthenticationResult.LoginSuccess) result = ClientAuthenticationResult.SceneLoginSuccess;

			return new ClientAuthResultBroadcast() { result = result, };
		}
	}
}