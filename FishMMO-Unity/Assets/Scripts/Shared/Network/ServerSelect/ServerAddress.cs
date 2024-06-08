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
	}
}