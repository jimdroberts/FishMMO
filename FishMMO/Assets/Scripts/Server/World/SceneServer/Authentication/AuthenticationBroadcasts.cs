using FishNet.Broadcast;

namespace FishMMO.Server
{
	public struct SceneServerAuthBroadcast : IBroadcast
	{
		public string password;
	}

	public struct SceneServerAuthResultBroadcast : IBroadcast
	{
	}
}