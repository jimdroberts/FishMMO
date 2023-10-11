using System;
using System.Collections.Generic;
using System.IO;

namespace FishMMO.Client
{
	public static class ClientNamingSystem
	{
		internal static Client Client;

		private static Dictionary<NamingSystemType, Dictionary<long, string>> names = new Dictionary<NamingSystemType, Dictionary<long, string>>();
		private static Dictionary<NamingSystemType, Dictionary<long, Action<string>>> pendingNameRequests = new Dictionary<NamingSystemType, Dictionary<long, Action<string>>>();

		public static void InitializeOnce(Client client)
		{
			Client = client;
			if (Client != null) Client.NetworkManager.ClientManager.RegisterBroadcast<NamingBroadcast>(OnClientNamingBroadcastReceived);

			foreach (NamingSystemType type in EnumExtensions.ToArray<NamingSystemType>())
			{
				if (!names.TryGetValue(type, out Dictionary<long, string> map))
				{
					map = new Dictionary<long, string>();
				}
				DictionaryExtensions.ReadCompressedFromFile(map, Path.Combine(Client.GetWorkingDirectory(), type.ToString() + ".bin"));
			}
		}

		public static void Destroy()
		{
			if (Client != null) Client.NetworkManager.ClientManager.UnregisterBroadcast<NamingBroadcast>(OnClientNamingBroadcastReceived);

			foreach (KeyValuePair<NamingSystemType, Dictionary<long, string>> pair in names)
			{
				pair.Value.WriteCompressedToFile(Path.Combine(Client.GetWorkingDirectory(), pair.Key.ToString() + ".bin"));
			}
		}

		public static void SetName(NamingSystemType type, long id, Action<string> action)
		{
			if (!names.TryGetValue(type, out Dictionary<long, string> typeNames))
			{
				names.Add(type, typeNames = new Dictionary<long, string>());
			}
			if (typeNames.TryGetValue(id, out string name))
			{
				action?.Invoke(name);
			}
			else if (Client != null)
			{
				if (!pendingNameRequests.TryGetValue(type, out Dictionary<long, Action<string>> pendingActions))
				{
					pendingNameRequests.Add(type, pendingActions = new Dictionary<long, Action<string>>());
				}
				if (!pendingActions.TryGetValue(id, out Action<string> pendingAction))
				{
					pendingActions.Add(id, action);

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
			if (pendingNameRequests.TryGetValue(msg.type, out Dictionary<long, Action<string>> pendingRequests))
			{
				if (pendingRequests.TryGetValue(msg.id, out Action<string> pendingActions))
				{
					pendingActions?.Invoke(msg.name);
					pendingActions = null;
					pendingRequests.Remove(msg.id);
				}
			}
			if (!names.TryGetValue(msg.type, out Dictionary<long, string> knownNames))
			{
				names.Add(msg.type, knownNames = new Dictionary<long, string>());
			}
			knownNames[msg.id] = msg.name;
		}
	}
}
