using FishNet.Connection;
using FishMMO.Auth.Implementation;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Unity/FishNet concrete SRP account manager for <see cref="NetworkConnection"/>.
	/// All logic lives in <see cref="SrpAccountManager{TConnection}"/> in FishMMO-Auth.
	/// </summary>
	public class SrpAccountManager : SrpAccountManager<NetworkConnection>
	{
	}
}