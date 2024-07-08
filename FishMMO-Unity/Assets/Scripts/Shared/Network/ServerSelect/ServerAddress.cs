using System;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	[Serializable]
	public class ServerAddresses
	{
		public List<ServerAddress> addresses;
	}

	[Serializable]
	public struct ServerAddress
	{
		public string address;
		public ushort port;

		public string HTTPSAddress()
		{
			string fullAddress = address + ":" + port;
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