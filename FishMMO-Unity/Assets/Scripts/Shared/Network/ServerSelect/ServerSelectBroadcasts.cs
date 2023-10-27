using System.Collections.Generic;
using FishNet.Broadcast;

namespace FishMMO.Shared
{
	public struct RequestServerListBroadcast : IBroadcast
	{
	}

	public struct ServerListBroadcast : IBroadcast
	{
		public List<WorldServerDetails> servers;
	}
	public struct WorldSceneConnectBroadcast : IBroadcast
	{
		public string address;
		public ushort port;
	}
}