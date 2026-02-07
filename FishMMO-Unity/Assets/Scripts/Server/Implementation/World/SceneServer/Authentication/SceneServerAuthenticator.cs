using System.Threading.Tasks;
using FishMMO.Shared;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// Scene Server Authenticator for basic password authentication on scene servers.
	/// </summary>
	public class SceneServerAuthenticator : ServerAuthenticator
	{
		/// <summary>
		/// Executed when a player tries to login to the Scene Server.
		/// Always returns SceneLoginSuccess for scene server authentication.
		/// </summary>
		/// <param name="result">Initial authentication result.</param>
		/// <param name="username">Username of the player attempting login.</param>
		/// <returns>SceneLoginSuccess result for successful authentication.</returns>
		internal override Task<ClientAuthenticationResult> TryLoginAsync(ClientAuthenticationResult result, string username)
		{
			return Task.FromResult(ClientAuthenticationResult.SceneLoginSuccess);
		}
	}
}