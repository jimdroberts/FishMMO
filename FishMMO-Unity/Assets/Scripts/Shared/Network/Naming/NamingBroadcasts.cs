using FishNet.Broadcast;

namespace FishMMO.Shared
{
	public struct NamingBroadcast : IBroadcast
	{
		public NamingSystemType type;
		public long id;
		public string name;
	}

	public struct ReverseNamingBroadcast : IBroadcast
	{
		public NamingSystemType type;
		public string nameLowerCase;
		public long id;
		public string name;
	}
}