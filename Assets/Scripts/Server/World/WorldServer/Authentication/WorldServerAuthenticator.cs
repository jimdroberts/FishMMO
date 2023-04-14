using Server.Services;

namespace Server
{
	// World that allows clients to connect with basic password authentication.
	public class WorldServerAuthenticator : RelayServerAuthenticator
	{
		internal override ClientAuthResultBroadcast TryLogin(string username, string password)
		{
			ClientAuthenticationResult result = ClientAuthenticationResult.InvalidUsernameOrPassword;

			using ServerDbContext dbContext = DBContextFactory.CreateDbContext();

			// if the username is valid try to get the account for the client...
			if (IsAllowedUsername(username))
			{
				result = AccountService.TryLogin(dbContext, username, password);
			}

			// make sure we have selected a character
			if (result == ClientAuthenticationResult.LoginSuccess &&
				CharacterService.TryGetSelectedCharacterDetails(dbContext, username, out string characterName))
			{
				result = ClientAuthenticationResult.WorldLoginSuccess;
			}

			return new ClientAuthResultBroadcast() { result = result, };
		}
	}
}