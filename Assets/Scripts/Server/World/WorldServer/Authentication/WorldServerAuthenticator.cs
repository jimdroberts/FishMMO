using Server.Services;

namespace Server
{
	// World that allows clients to connect with basic password authentication.
	public class WorldServerAuthenticator : RelayServerAuthenticator
	{
		internal override ClientAuthResultBroadcast TryLogin(string username, string password)
		{
			ClientAuthenticationResult result = ClientAuthenticationResult.InvalidUsernameOrPassword;

			// TODO: replace this whenever the authenticators have the factory from server
			using var dbContext = new ServerDbContextFactory().CreateDbContext();
			// if the username is valid try to get the account for the client...
			if (IsAllowedUsername(username))
			{
				result = AccountService.TryLogin(dbContext, username, password);
			}

			// make sure we have selected a character
			if (result == ClientAuthenticationResult.LoginSuccess &&
				Database.Instance.TryGetSelectedCharacterDetails(username, out string characterName))
			{
				result = ClientAuthenticationResult.WorldLoginSuccess;
			}

			return new ClientAuthResultBroadcast() { result = result, };
		}
	}
}