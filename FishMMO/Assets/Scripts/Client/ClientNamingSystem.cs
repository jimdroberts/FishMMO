using System;
using System.Collections.Generic;

namespace FishMMO.Client
{
	public static class ClientNamingSystem
	{
		internal static Client Client;

		private static Dictionary<long, string> names = new Dictionary<long, string>();
		private static Dictionary<long, Action<string>> pendingNameRequests = new Dictionary<long, Action<string>>();

		public static void InitializeOnce(Client client)
		{
			Client = client;
			if (Client != null) Client.NetworkManager.ClientManager.RegisterBroadcast<NamingBroadcast>(OnClientNamingBroadcastReceived);
		}

		public static void Destroy()
		{
			if (Client != null) Client.NetworkManager.ClientManager.UnregisterBroadcast<NamingBroadcast>(OnClientNamingBroadcastReceived);
		}

		public static void SetName(NamingSystemType type, long id, Action<string> action)
		{
			if (names.TryGetValue(id, out string name))
			{
				action?.Invoke(name);
			}
			else if (Client != null)
			{
				if (!pendingNameRequests.TryGetValue(id, out Action<string> pendingAction))
				{
					pendingNameRequests.Add(id, action);

					// send the request to the server to get a name
					Client.NetworkManager.ClientManager.Broadcast(new NamingBroadcast()
					{
						type = type,
						id = id,
						name = "",
					});
				}
				else
				{
					pendingAction += action;
				}
			}
		}

		private static void OnClientNamingBroadcastReceived(NamingBroadcast msg)
		{
			if (pendingNameRequests.TryGetValue(msg.id, out Action<string> pendingActions))
			{
				pendingActions?.Invoke(msg.name);
				pendingActions = null;
				pendingNameRequests.Remove(msg.id);
			}
			names[msg.id] = msg.name;
		}
	}
}
