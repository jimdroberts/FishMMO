using System;
using System.Collections.Generic;
using System.IO;

namespace FishMMO.Client
{
	public static class ClientNamingSystem
	{
		internal static Client Client;

		private static Dictionary<NamingSystemType, Dictionary<long, string>> idToName = new Dictionary<NamingSystemType, Dictionary<long, string>>();
		private static Dictionary<NamingSystemType, Dictionary<long, Action<string>>> pendingNameRequests = new Dictionary<NamingSystemType, Dictionary<long, Action<string>>>();

		public static void InitializeOnce(Client client)
		{
			Client = client;
			if (Client != null) Client.NetworkManager.ClientManager.RegisterBroadcast<NamingBroadcast>(OnClientNamingBroadcastReceived);

			string workingDirectory = Client.GetWorkingDirectory();
			foreach (NamingSystemType type in EnumExtensions.ToArray<NamingSystemType>())
			{
				if (!idToName.TryGetValue(type, out Dictionary<long, string> map))
				{
					idToName.Add(type, map = new Dictionary<long, string>());
				}
				DictionaryExtensions.ReadCompressedFromFile(map, Path.Combine(workingDirectory, type.ToString() + ".bin"));
			}
		}

		public static void Destroy()
		{
			if (Client != null) Client.NetworkManager.ClientManager.UnregisterBroadcast<NamingBroadcast>(OnClientNamingBroadcastReceived);

			string workingDirectory = Client.GetWorkingDirectory();
			foreach (KeyValuePair<NamingSystemType, Dictionary<long, string>> pair in idToName)
			{
				pair.Value.WriteCompressedToFile(Path.Combine(workingDirectory, pair.Key.ToString() + ".bin"));
			}
		}

		/// <summary>
		/// Checks if the name matching the ID and type are known. If they are not the value will be retreived from the server and set at a later time.
		/// Values learned this way are saved to the clients hard drive when the game closes and loaded when the game loads.
		/// </summary>
		public static void SetName(NamingSystemType type, long id, Action<string> action)
		{
			if (!idToName.TryGetValue(type, out Dictionary<long, string> typeNames))
			{
				idToName.Add(type, typeNames = new Dictionary<long, string>());
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
			if (!idToName.TryGetValue(msg.type, out Dictionary<long, string> knownNames))
			{
				idToName.Add(msg.type, knownNames = new Dictionary<long, string>());
			}
			knownNames[msg.id] = msg.name;
		}
	}
}
