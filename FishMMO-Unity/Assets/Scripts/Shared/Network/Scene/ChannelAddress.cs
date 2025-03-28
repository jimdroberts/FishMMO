using System;

namespace FishMMO.Shared
{
	[Serializable]
	public struct ChannelAddress
	{
		public string Address;
		public ushort Port;
		public int SceneHandle;
	}
}