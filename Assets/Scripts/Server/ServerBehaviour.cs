using FishNet.Managing.Client;
using FishNet.Managing.Server;
using UnityEngine;

namespace Server
{
	public abstract class ServerBehaviour : MonoBehaviour
	{
		public bool Initialized { get; private set; }
		public Server Server { get; private set; }
		public ServerManager ServerManager { get; private set; }
		// ClientManager is used on the servers for Server<->Server communication. *NOTE* Check if null!
		public ClientManager ClientManager { get; private set; }

		internal void InternalInitializeOnce(Server server, ServerManager serverManager)
		{
			InternalInitializeOnce(server, serverManager, null);
		}

		internal void InternalInitializeOnce(Server server, ServerManager serverManager, ClientManager clientManager)
		{
			if (Initialized)
				return;

			Server = server;
			ServerManager = serverManager;
			ClientManager = clientManager;
			Initialized = true;

			InitializeOnce();
		}

		public abstract void InitializeOnce();
	}
}