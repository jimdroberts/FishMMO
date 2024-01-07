using FishMMO.Database.Npgsql;
using FishMMO.Server.DatabaseServices;
using FishMMO.Shared;

namespace FishMMO.Server
{
	// World that allows clients to connect with basic password authentication.
	public class WorldServerAuthenticator : LoginServerAuthenticator
	{
		public uint MaxPlayers = 5000;

		public WorldSceneSystem WorldSceneSystem { get; set; }

		internal override ClientAuthenticationResult TryLogin(NpgsqlDbContext dbContext, ClientAuthenticationResult result, string username)
		{
			if (WorldSceneSystem != null && WorldSceneSystem.ConnectionCount >= MaxPlayers)
			{
				return ClientAuthenticationResult.ServerFull;
			}
			else if (dbContext == null)
			{
				return ClientAuthenticationResult.InvalidUsernameOrPassword;
			}
			else if (result == ClientAuthenticationResult.LoginSuccess &&
					 CharacterService.GetSelected(dbContext, username))
			{
				// update the characters world
				CharacterService.SetWorld(dbContext, username, WorldSceneSystem.Server.WorldServerSystem.ID);

				return ClientAuthenticationResult.WorldLoginSuccess;
			}
			return result;
		}
	}
}