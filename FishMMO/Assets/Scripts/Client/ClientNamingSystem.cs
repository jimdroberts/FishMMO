using System;
using System.Collections.Generic;
#if !UNITY_EDITOR
using System.IO;
#endif

namespace FishMMO.Client
{
	public static class ClientNamingSystem
	{
		internal static Client Client;

		private static Dictionary<NamingSystemType, Dictionary<long, string>> idToName = new Dictionary<NamingSystemType, Dictionary<long, string>>();
		private static Dictionary<NamingSystemType, Dictionary<long, Action<string>>> pendingNameRequests = new Dictionary<NamingSystemType, Dictionary<long, Action<string>>>();

		public static void InitializeOnce(Client client)
		{
			if (client == null)
			{
				return;
			}

			Client = client;

			Client.NetworkManager.ClientManager.RegisterBroadcast<NamingBroadcast>(OnClientNamingBroadcastReceived);

#if !UNITY_EDITOR
			string workingDirectory = Client.GetWorkingDirectory();
			foreach (NamingSystemType type in EnumExtensions.ToArray<NamingSystemType>())
			{
				idToName[type] = DictionaryExtensions.ReadFromGZipFile(Path.Combine(workingDirectory, type.ToString() + ".bin"));
			}
#endif
		}

		public static void Destroy()
		{
			if (Client != null)
			{
				Client.NetworkManager.ClientManager.UnregisterBroadcast<NamingBroadcast>(OnClientNamingBroadcastReceived);
			}

#if !UNITY_EDITOR
			string workingDirectory = Client.GetWorkingDirectory();
			foreach (KeyValuePair<NamingSystemType, Dictionary<long, string>> pair in idToName)
			{
				pair.Value.WriteToGZipFile(Path.Combine(workingDirectory, pair.Key.ToString() + ".bin"));
			}
#endif
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
				//UnityEngine.Debug.Log("Found Name for: " + id + ":" + name);
				action?.Invoke(name);
			}
			else if (Client != null)
			{
				if (!pendingNameRequests.TryGetValue(type, out Dictionary<long, Action<string>> pendingActions))
				{
					pendingNameRequests.Add(type, pendingActions = new Dictionary<long, Action<string>>());
				}
				if (!pendingActions.ContainsKey(id))
				{
					pendingActions.Add(id, action);

					//UnityEngine.Debug.Log("Requesting Name for: " + id);

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
					//UnityEngine.Debug.Log("Adding pending Name for: " + id);
					pendingActions[id] += action;
				}
			}
		}

		private static void OnClientNamingBroadcastReceived(NamingBroadcast msg)
		{
			if (pendingNameRequests.TryGetValue(msg.type, out Dictionary<long, Action<string>> pendingRequests))
			{
				if (pendingRequests.TryGetValue(msg.id, out Action<string> pendingActions))
				{
					//UnityEngine.Debug.Log("Processing Name for: " + msg.id + ":" + msg.name);

					pendingActions?.Invoke(msg.name);
					pendingRequests[msg.id] = null;
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
