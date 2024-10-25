using FishNet.Broadcast;

namespace FishMMO.Shared
{
	public struct ChatBroadcast : IBroadcast
	{
		public ChatChannel Channel;
		public long SenderID;
		public string Text;
	}
}