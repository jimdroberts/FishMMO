using FishNet.Broadcast;

namespace Server
{
	public struct SceneServerAuthBroadcast : IBroadcast
	{
		public string password;
	}

	public struct SceneServerAuthResultBroadcast : IBroadcast
	{
	}
}