using FishMMO.Database.Npgsql;
using FishMMO.Server.DatabaseServices;
using FishMMO.Shared;

namespace FishMMO.Server
{
	// World that allows clients to connect with basic password authentication.
	public class WorldServerAuthenticator : LoginServerAuthenticator
	{
		public uint MaxPlayers = 5000;

		internal override ClientAuthenticationResult TryLogin(NpgsqlDbContext dbContext, ClientAuthenticationResult result, string username)
		{
			if (ServerBehaviour.TryGet(out WorldSceneSystem worldSceneSystem) &&
				worldSceneSystem.ConnectionCount >= MaxPlayers)
			{
				return ClientAuthenticationResult.ServerFull;
			}
			else if (dbContext == null)
			{
				return ClientAuthenticationResult.InvalidUsernameOrPassword;
			}
			else if (result == ClientAuthenticationResult.LoginSuccess &&
				ServerBehaviour.TryGet(out WorldServerSystem worldServerSystem) &&
				CharacterService.GetSelected(dbContext, username))
			{
				// update the characters world
				CharacterService.SetWorld(dbContext, username, worldServerSystem.ID);

				return ClientAuthenticationResult.WorldLoginSuccess;
			}
			return result;
		}
	}
}