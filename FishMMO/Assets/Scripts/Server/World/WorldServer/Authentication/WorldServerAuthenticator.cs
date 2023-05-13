using FishMMO_DB;
using FishMMO.Server.Services;

namespace FishMMO.Server
{
	// World that allows clients to connect with basic password authentication.
	public class WorldServerAuthenticator : RelayServerAuthenticator
	{
		internal override ClientAuthResultBroadcast TryLogin(string username, string password)
		{
			ClientAuthenticationResult result = ClientAuthenticationResult.InvalidUsernameOrPassword;

			// if the username is valid get OR create the account for the client
			if (DBContextFactory != null && IsAllowedUsername(username))
			{
				using ServerDbContext dbContext = DBContextFactory.CreateDbContext(new string[] { });
				if (!CharacterService.TryGetOnlineCharacter(dbContext, username))
				{
					result = AccountService.TryLogin(dbContext, username, password);
				}
				else
				{
					result = ClientAuthenticationResult.AlreadyOnline;
				}

				// make sure we have selected a character
				if (result == ClientAuthenticationResult.LoginSuccess &&
					CharacterService.TryGetSelectedCharacterDetails(dbContext, username, out long characterId))
				{
					result = ClientAuthenticationResult.WorldLoginSuccess;
				}
			}

			return new ClientAuthResultBroadcast() { result = result, };
		}
	}
}