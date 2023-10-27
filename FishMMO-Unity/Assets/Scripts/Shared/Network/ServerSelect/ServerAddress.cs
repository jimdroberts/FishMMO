using System;

namespace FishMMO.Shared
{
	[Serializable]
	public struct ServerAddress
	{
		public string address;
		public ushort port;
	}
}