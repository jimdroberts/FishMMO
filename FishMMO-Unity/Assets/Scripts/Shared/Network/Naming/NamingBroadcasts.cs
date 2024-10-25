using FishNet.Broadcast;

namespace FishMMO.Shared
{
	public struct NamingBroadcast : IBroadcast
	{
		public NamingSystemType Type;
		public long ID;
		public string Name;
	}

	public struct ReverseNamingBroadcast : IBroadcast
	{
		public NamingSystemType Type;
		public string NameLowerCase;
		public long ID;
		public string Name;
	}
}