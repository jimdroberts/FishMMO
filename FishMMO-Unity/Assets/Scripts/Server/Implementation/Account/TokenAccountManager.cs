using FishNet.Connection;
using FishMMO.Auth.Implementation;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Unity/FishNet concrete token account manager for <see cref="NetworkConnection"/>.
	/// All logic lives in <see cref="TokenAccountManager{TConnection}"/> in FishMMO-Auth.
	/// </summary>
	public class TokenAccountManager : TokenAccountManager<NetworkConnection>
	{
		/// <summary>
		/// Initializes the token account manager using <see cref="FishNet.Connection.NetworkConnection.ClientId"/>
		/// as the connection identifier delegate.
		/// </summary>
		public TokenAccountManager()
			: base(conn => conn.ClientId.ToString()) { }
	}
}