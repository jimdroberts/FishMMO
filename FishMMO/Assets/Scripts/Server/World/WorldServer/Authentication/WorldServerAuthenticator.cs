using FishMMO_DB;
using FishMMO.Server.Services;

namespace FishMMO.Server
{
	// World that allows clients to connect with basic password authentication.
	public class WorldServerAuthenticator : LoginServerAuthenticator
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

				if (result == ClientAuthenticationResult.LoginSuccess &&
					CharacterService.GetSelected(dbContext, username))
				{
					// update the characters world
					CharacterService.SetWorld(dbContext, username, WorldSceneSystem.Server.WorldServerSystem.ID);
					dbContext.SaveChanges();

					result = ClientAuthenticationResult.WorldLoginSuccess;
				}
			}

			return new ClientAuthResultBroadcast() { result = result, };
		}
	}
}