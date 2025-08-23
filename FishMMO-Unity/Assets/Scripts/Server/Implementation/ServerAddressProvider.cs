using FishNet.Transporting;
using FishMMO.Shared;
using FishMMO.Server.Core;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Provides various IP address formats by interacting with the FishNet transport layer and server overrides.
	/// </summary>
	public class ServerAddressProvider : IServerAddressProvider
	{
		private readonly Transport transport;
		private readonly string addressOverride;
		private readonly ushort portOverride;
		private readonly string coreServerAddress;
		private readonly string coreServerRemoteAddress;

		/// <summary>
		/// Initializes a new instance of the <see cref="ServerAddressProvider"/> class.
		/// </summary>
		/// <param name="transport">The FishNet transport instance.</param>
		/// <param name="addressOverride">An optional address override.</param>
		/// <param name="portOverride">An optional port override.</param>
		/// <param name="coreServerAddress">The core server's address.</param>
		/// <param name="coreServerRemoteAddress">The core server's remote address.</param>
		public ServerAddressProvider(Transport transport, string addressOverride, ushort portOverride, string coreServerAddress, string coreServerRemoteAddress)
		{
			this.transport = transport;
			this.addressOverride = addressOverride;
			this.portOverride = portOverride;
			this.coreServerAddress = coreServerAddress;
			this.coreServerRemoteAddress = coreServerRemoteAddress;
		}

		/// <summary>
		/// Attempts to get the server's IPv4 address from the transport layer.
		/// </summary>
		/// <param name="address">When this method returns, contains the IPv4 server address if found; otherwise, the default value.</param>
		/// <returns><c>true</c> if the IPv4 address was found; otherwise, <c>false</c>.</returns>
		public bool TryGetServerIPv4AddressFromTransport(out ServerAddress address)
		{
			if (transport != null)
			{
				address = new ServerAddress()
				{
					Address = transport.GetServerBindAddress(IPAddressType.IPv4),
					Port = transport.GetPort(),
				};
				return true;
			}
			address = default;
			return false;
		}

		/// <summary>
		/// Attempts to get the server's IPv6 address from the transport layer.
		/// </summary>
		/// <param name="address">When this method returns, contains the IPv6 server address if found; otherwise, the default value.</param>
		/// <returns><c>true</c> if the IPv6 address was found; otherwise, <c>false</c>.</returns>
		public bool TryGetServerIPv6AddressFromTransport(out ServerAddress address)
		{
			if (transport != null)
			{
				address = new ServerAddress()
				{
					Address = transport.GetServerBindAddress(IPAddressType.IPv6),
					Port = transport.GetPort(),
				};
				return true;
			}
			address = default;
			return false;
		}

		/// <summary>
		/// Attempts to get the server's IP address (either IPv4 or IPv6), using overrides if provided.
		/// </summary>
		/// <param name="address">When this method returns, contains the server address if found; otherwise, the default value.</param>
		/// <returns><c>true</c> if an IP address was found; otherwise, <c>false</c>.</returns>
		public bool TryGetServerIPAddress(out ServerAddress address)
		{
			if (!string.IsNullOrEmpty(addressOverride))
			{
				address = new ServerAddress()
				{
					Address = addressOverride,
					Port = portOverride,
				};
				return true;
			}

			const string LoopBack = "127.0.0.1";
			const string LocalHost = "localhost";

			if (transport != null)
			{
				string actualAddress = LoopBack;
				if (!string.IsNullOrWhiteSpace(coreServerAddress) &&
					(coreServerAddress.Equals(LoopBack) || coreServerAddress.Equals(LocalHost)))
				{
					actualAddress = coreServerAddress;
				}
				else if (!string.IsNullOrWhiteSpace(coreServerRemoteAddress))
				{
					actualAddress = coreServerRemoteAddress;
				}

				address = new ServerAddress()
				{
					Address = actualAddress,
					Port = transport.GetPort(),
				};
				return true;
			}
			address = default;
			return false;
		}
	}
}