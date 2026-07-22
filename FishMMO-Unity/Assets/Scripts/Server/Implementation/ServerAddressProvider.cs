using FishNet.Transporting;
using FishMMO.Logging;
using FishMMO.Shared;
using FishMMO.Server.Core;
using System;
using System.Net;

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
				string bindAddress = transport.GetServerBindAddress(IPAddressType.IPv4);
				if (!string.IsNullOrWhiteSpace(bindAddress))
				{
					address = new ServerAddress()
					{
						Address = bindAddress,
						Port = transport.GetPort(),
					};
					return true;
				}
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
				string bindAddress = transport.GetServerBindAddress(IPAddressType.IPv6);
				if (!string.IsNullOrWhiteSpace(bindAddress))
				{
					address = new ServerAddress()
					{
						Address = bindAddress,
						Port = transport.GetPort(),
					};
					return true;
				}
			}
			address = default;
			return false;
		}

		/// <summary>
		/// Attempts to get the server's IP address (either IPv4 or IPv6), using overrides if provided.
		/// When the core server address is a loopback address (127.0.0.1, ::1, localhost, etc.),
		/// falls back to the core server's remote address. This enables local-only deployments to
		/// function while allowing production servers to register their public-facing address.
		/// Loopback detection delegates to <see cref="NetHelper.IsLoopbackAddress"/>,
		/// which uses <see cref="System.Net.IPAddress.IsLoopback"/> and covers all RFC 5735
		/// loopback variants — not just the magic strings 127.0.0.1 and localhost.
		/// </summary>
		public bool TryGetServerIPAddress(out ServerAddress address)
		{
			if (!string.IsNullOrEmpty(addressOverride))
			{
				// Validate override format before accepting it.
				// Reject addresses that are too long (253 is max DNS hostname; 45 is max IPv6)
				// or contain null bytes / control characters which could indicate injection.
				if (addressOverride.Length > 253 ||
					ContainsControlCharacter(addressOverride))
				{
					Log.Warning("ServerAddressProvider",
						$"Invalid address override '{addressOverride}' (length={addressOverride.Length}); falling through to transport address.");
				}
				else
				{
					address = new ServerAddress()
					{
						Address = addressOverride,
						Port = portOverride > 0 ? portOverride : (transport != null ? transport.GetPort() : (ushort)0),
					};
					return true;
				}
			}

			if (transport != null)
			{
				string actualAddress = "127.0.0.1";
				if (!string.IsNullOrWhiteSpace(coreServerAddress) &&
					!NetHelper.IsLoopbackAddress(coreServerAddress))
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

		/// <summary>
		/// Returns true if <paramref name="value"/> contains any ASCII control
		/// character (U+0000 through U+001F), indicating a potentially malformed
		/// or injected address string.
		/// </summary>
		private static bool ContainsControlCharacter(string value)
		{
			for (int i = 0; i < value.Length; i++)
			{
				if (value[i] < 0x20)
				{
					return true;
				}
			}
			return false;
		}
	}
}