using System.Threading.Tasks;
using FishMMO.Shared;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Scene Server Authenticator using token-based authentication.
	/// </summary>
	public class SceneServerAuthenticator : TokenServerAuthenticator
	{
		/// <summary>
		/// Executed when a player tries to login to the Scene Server.
		/// Returns SceneLoginSuccess only if the initial authentication succeeded.
		/// </summary>
		/// <param name="result">Initial authentication result from the base authenticator.</param>
		/// <param name="username">Username of the player attempting login.</param>
		/// <returns>SceneLoginSuccess if the initial result indicates success; otherwise the original failure result.</returns>
		internal override Task<ClientAuthenticationResult> TryLoginAsync(ClientAuthenticationResult result, string username)
		{
			if (result != ClientAuthenticationResult.LoginSuccess)
			{
				return Task.FromResult(result);
			}

			return Task.FromResult(ClientAuthenticationResult.SceneLoginSuccess);
		}
	}
}