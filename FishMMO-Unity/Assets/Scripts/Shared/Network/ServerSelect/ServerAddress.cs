using System;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Serializable class containing a list of server addresses for server selection.
	/// </summary>
	[Serializable]
	public class ServerAddresses
	{
		/// <summary>List of available server addresses.</summary>
		public List<ServerAddress> Addresses;
	}

	/// <summary>
	/// Serializable struct representing a server address, including IP and port.
	/// Provides a method to format the address as an HTTPS URL.
	/// </summary>
	[Serializable]
	public struct ServerAddress
	{
		/// <summary>IP address or hostname of the server.</summary>
		public string Address;
		/// <summary>Port number for the server.</summary>
		public ushort Port;

		/// <summary>
		/// Returns the server address formatted as an HTTPS URL, ensuring proper prefix and trailing slash.
		/// </summary>
		/// <returns>Formatted HTTPS URL for the server address.</returns>
		public string HTTPSAddress()
		{
			string fullAddress = Address + ":" + Port;
			// Format the PatcherHost Address with HTTPS
			if (!fullAddress.StartsWith("https://"))
			{
				fullAddress = "https://" + fullAddress;
			}
			if (!fullAddress.EndsWith("/"))
			{
				fullAddress += "/";
			}
			return fullAddress;
		}
	}
}