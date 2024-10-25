using FishNet.Transporting;
using System;
using System.Collections.Generic;
#if !UNITY_EDITOR
using System.IO;
#endif
using FishMMO.Shared;

namespace FishMMO.Client
{
	public static class ClientNamingSystem
	{
		internal static Client Client;

		private static Dictionary<NamingSystemType, Dictionary<long, string>> idToName = new Dictionary<NamingSystemType, Dictionary<long, string>>();
		// character names are unique so we can presume this works properly
		private static Dictionary<string, long> nameToID = new Dictionary<string, long>();
		private static Dictionary<NamingSystemType, Dictionary<long, Action<string>>> pendingNameRequests = new Dictionary<NamingSystemType, Dictionary<long, Action<string>>>();
		private static Dictionary<NamingSystemType, Dictionary<string, Action<long>>> pendingIdRequests = new Dictionary<NamingSystemType, Dictionary<string, Action<long>>>();

		public static void InitializeOnce(Client client)
		{
			if (client == null)
			{
				return;
			}

			Client = client;

			Client.NetworkManager.ClientManager.RegisterBroadcast<NamingBroadcast>(OnClientNamingBroadcastReceived);
			Client.NetworkManager.ClientManager.RegisterBroadcast<ReverseNamingBroadcast>(OnClientReverseNamingBroadcastReceived);

#if !UNITY_EDITOR
			string workingDirectory = Client.GetWorkingDirectory();
			foreach (NamingSystemType type in EnumExtensions.ToArray<NamingSystemType>())
			{
				idToName[type] = DictionaryExtensions.ReadFromGZipFile(Path.Combine(workingDirectory, type.ToString() + ".bin"));
			}

			Dictionary<long, string> characterNames = idToName[NamingSystemType.CharacterName];
			if (characterNames != null && characterNames.Count > 0)
			{
				foreach (KeyValuePair<long, string> pair in characterNames)
				{
					nameToID[pair.Value] = pair.Key;
				}
			}
#endif
		}

		public static void Destroy()
		{
			if (Client != null)
			{
				Client.NetworkManager.ClientManager.UnregisterBroadcast<NamingBroadcast>(OnClientNamingBroadcastReceived);
				Client.NetworkManager.ClientManager.UnregisterBroadcast<ReverseNamingBroadcast>(OnClientReverseNamingBroadcastReceived);
			}

#if !UNITY_EDITOR
			if (idToName.Count < 1)
			{
				return;
			}

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
					Client.Broadcast(new NamingBroadcast()
					{
						Type = type,
						ID = id,
						Name = "",
					}, Channel.Reliable);
				}
				else
				{
					//UnityEngine.Debug.Log("Adding pending Name for: " + id);
					pendingActions[id] += action;
				}
			}
		}

		public static void GetCharacterID(string name, Action<long> action)
		{
			if (nameToID.TryGetValue(name, out long id))
			{
				// if we find the name we're done
				action?.Invoke(id);
			}
			else if (Client != null)
			{
				var nameLowerCase = name.ToLower().Trim();

				// request the name from the server
				if (!pendingIdRequests.TryGetValue(NamingSystemType.CharacterName, out Dictionary<string, Action<long>> pendingActions))
				{
					pendingIdRequests.Add(NamingSystemType.CharacterName, pendingActions = new Dictionary<string, Action<long>>());
				}
				if (!pendingActions.ContainsKey(nameLowerCase))
				{
					pendingActions.Add(nameLowerCase, action);

					//UnityEngine.Debug.Log("Requesting Id for: " + name);

					// send the request to the server to get the id and correct name
					Client.Broadcast(new ReverseNamingBroadcast()
					{
						Type = NamingSystemType.CharacterName,
						NameLowerCase = nameLowerCase,
						ID = 0,
						Name = "",
					}, Channel.Reliable);
				}
				else
				{
					//UnityEngine.Debug.Log("Adding pending Id for: " + name);
					pendingActions[nameLowerCase] += action;
				}
			}
		}

		private static void OnClientNamingBroadcastReceived(NamingBroadcast msg, Channel channel)
		{
			if (pendingNameRequests.TryGetValue(msg.Type, out Dictionary<long, Action<string>> pendingRequests))
			{
				if (pendingRequests.TryGetValue(msg.ID, out Action<string> pendingActions))
				{
					//UnityEngine.Debug.Log("Processing Name for: " + msg.id + ":" + msg.name);

					pendingActions?.Invoke(msg.Name);
					pendingRequests[msg.ID] = null;
					pendingRequests.Remove(msg.ID);
				}
			}
			if (msg.ID != 0)
			{
				UpdateKnownNames(msg.Type, msg.ID, msg.Name);
			}
		}

		private static void OnClientReverseNamingBroadcastReceived(ReverseNamingBroadcast msg, Channel channel)
		{
			if (pendingIdRequests.TryGetValue(msg.Type, out Dictionary<string, Action<long>> pendingRequests))
			{
				if (pendingRequests.TryGetValue(msg.NameLowerCase, out Action<long> pendingActions))
				{
					//UnityEngine.Debug.Log("Processing Id for: " + msg.id + ":" + msg.name);

					pendingActions?.Invoke(msg.ID);
					pendingRequests[msg.NameLowerCase] = null;
					pendingRequests.Remove(msg.NameLowerCase);
				}
			}

			if (msg.ID != 0)
			{
				UpdateKnownNames(msg.Type, msg.ID, msg.Name);
			}
		}

		private static void UpdateKnownNames(NamingSystemType type, long id, string name)
		{
			if (!idToName.TryGetValue(type, out Dictionary<long, string> knownNames))
			{
				idToName.Add(type, knownNames = new Dictionary<long, string>());
			}
			knownNames[id] = name;
			nameToID[name] = id;
		}
	}
}