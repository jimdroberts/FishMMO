using FishMMO.Shared;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Interface for a service that provides server address information.
	/// Removes the responsibility from the Server class itself.
	/// </summary>
	public interface IServerAddressProvider
	{
		/// <summary>
		/// Attempts to get the server's IPv4 address from the transport layer.
		/// </summary>
		/// <param name="address">When this method returns, contains the IPv4 server address if found; otherwise, the default value.</param>
		/// <returns><c>true</c> if the IPv4 address was found; otherwise, <c>false</c>.</returns>
		bool TryGetServerIPv4AddressFromTransport(out ServerAddress address);

		/// <summary>
		/// Attempts to get the server's IPv6 address from the transport layer.
		/// </summary>
		/// <param name="address">When this method returns, contains the IPv6 server address if found; otherwise, the default value.</param>
		/// <returns><c>true</c> if the IPv6 address was found; otherwise, <c>false</c>.</returns>
		bool TryGetServerIPv6AddressFromTransport(out ServerAddress address);

		/// <summary>
		/// Attempts to get the server's IP address (either IPv4 or IPv6).
		/// </summary>
		/// <param name="address">When this method returns, contains the server address if found; otherwise, the default value.</param>
		/// <returns><c>true</c> if an IP address was found; otherwise, <c>false</c>.</returns>
		bool TryGetServerIPAddress(out ServerAddress address);
	}
}