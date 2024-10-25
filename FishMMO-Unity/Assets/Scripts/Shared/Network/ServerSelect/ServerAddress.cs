using System;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	[Serializable]
	public class ServerAddresses
	{
		public List<ServerAddress> Addresses;
	}

	[Serializable]
	public struct ServerAddress
	{
		public string Address;
		public ushort Port;

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