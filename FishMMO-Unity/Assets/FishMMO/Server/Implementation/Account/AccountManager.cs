using FishNet.Connection;
using FishMMO.Auth.Implementation;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Unity/FishNet concrete account manager for <see cref="NetworkConnection"/>.
	/// All logic lives in <see cref="AccountManager{TConnection}"/> in FishMMO-Auth.
	/// </summary>
	public class AccountManager : AccountManager<NetworkConnection>
	{
	}
}