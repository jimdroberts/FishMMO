using FishMMO_DB;
using FishMMO.Server.Services;

namespace FishMMO.Server
{
	// World that allows clients to connect with basic password authentication.
	public class WorldServerAuthenticator : RelayServerAuthenticator
	{
		public uint MaxPlayers = 5000;

		public WorldSceneSystem WorldSceneSystem { get; set; }

		internal override ClientAuthResultBroadcast TryLogin(string username, string password)
		{
			if (WorldSceneSystem != null && WorldSceneSystem.ConnectionCount >= MaxPlayers)
			{
				return new ClientAuthResultBroadcast() { result = ClientAuthenticationResult.ServerFull, };
			}

			ClientAuthenticationResult result = ClientAuthenticationResult.InvalidUsernameOrPassword;

			// if the username is valid get OR create the account for the client
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

				// make sure we have selected a character
				if (result == ClientAuthenticationResult.LoginSuccess &&
					CharacterService.TryGetSelectedDetails(dbContext, username, out long characterId))
				{
					result = ClientAuthenticationResult.WorldLoginSuccess;
				}
			}

			return new ClientAuthResultBroadcast() { result = result, };
		}
	}
}