using FishNet.Transporting;
using System;
using System.Collections.Generic;
using System.IO;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// The ClientNamingSystem class is responsible for managing the mapping between character and object IDs
	/// and their corresponding names in the FishMMO game client. It handles the registration and unregistration
	/// of naming broadcasts, as well as the storage and retrieval of name-ID mappings.
	/// </summary>
	public static class ClientNamingSystem
	{
		/// <summary>
		/// Reference to the client instance for network operations.
		/// </summary>
		internal static Client Client;

		/// <summary>
		/// Maps naming system type and ID to name. Used for fast lookup and caching.
		/// </summary>
		private static Dictionary<NamingSystemType, Dictionary<long, string>> idToName = new Dictionary<NamingSystemType, Dictionary<long, string>>();
		/// <summary>
		/// Maps character names to their unique IDs. Assumes character names are unique.
		/// </summary>
		private static Dictionary<string, long> nameToID = new Dictionary<string, long>();
		/// <summary>
		/// Tracks pending name requests by type and ID, with callbacks to invoke when names are received.
		/// </summary>
		private static Dictionary<NamingSystemType, Dictionary<long, Action<string>>> pendingNameRequests = new Dictionary<NamingSystemType, Dictionary<long, Action<string>>>();
		/// <summary>
		/// Tracks pending ID requests by type and name, with callbacks to invoke when IDs are received.
		/// </summary>
		private static Dictionary<NamingSystemType, Dictionary<string, Action<long>>> pendingIdRequests = new Dictionary<NamingSystemType, Dictionary<string, Action<long>>>();

		/// <summary>
		/// Initializes the naming system, registers broadcast handlers, and loads cached names from disk (outside Unity Editor).
		/// </summary>
		/// <param name="client">The client instance to use for network operations.</param>
		public static void Initialize(Client client)
		{
			if (client == null)
			{
				return;
			}

			Client = client;

			Client.NetworkManager.ClientManager.RegisterBroadcast<NamingBroadcast>(OnClientNamingBroadcastReceived);
			Client.NetworkManager.ClientManager.RegisterBroadcast<ReverseNamingBroadcast>(OnClientReverseNamingBroadcastReceived);

#if !UNITY_EDITOR
			string workingDirectory = Constants.GetWorkingDirectory();
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

		/// <summary>
		/// Cleans up the naming system, unregisters broadcast handlers, and saves cached names to disk (outside Unity Editor).
		/// </summary>
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

			string workingDirectory = Constants.GetWorkingDirectory();
			foreach (KeyValuePair<NamingSystemType, Dictionary<long, string>> pair in idToName)
			{
				pair.Value.WriteToGZipFile(Path.Combine(workingDirectory, pair.Key.ToString() + ".bin"));
			}
#endif
		}

		/// <summary>
		/// Checks if the name matching the ID and type is known. If not, requests it from the server and invokes the callback when available.
		/// Values learned this way are saved to disk when the game closes and loaded when the game loads.
		/// </summary>
		/// <param name="type">The naming system type.</param>
		/// <param name="id">The unique ID to resolve to a name.</param>
		/// <param name="action">Callback to invoke with the resolved name.</param>
		public static void SetName(NamingSystemType type, long id, Action<string> action)
		{
			if (!idToName.TryGetValue(type, out Dictionary<long, string> typeNames))
			{
				idToName.Add(type, typeNames = new Dictionary<long, string>());
			}
			if (typeNames.TryGetValue(id, out string name))
			{
				// Name found in cache, invoke callback immediately.
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

					// Send request to server to get the name for this ID.
					Client.Broadcast(new NamingBroadcast()
					{
						Type = type,
						ID = id,
						Name = "",
					}, Channel.Reliable);
				}
				else
				{
					// Multiple callbacks for the same ID are combined.
					pendingActions[id] += action;
				}
			}
		}

		/// <summary>
		/// Gets the character ID for a given name. If not cached, requests it from the server and invokes the callback when available.
		/// </summary>
		/// <param name="name">The character name to resolve to an ID.</param>
		/// <param name="action">Callback to invoke with the resolved ID.</param>
		public static void GetCharacterID(string name, Action<long> action)
		{
			if (nameToID.TryGetValue(name, out long id))
			{
				// ID found in cache, invoke callback immediately.
				action?.Invoke(id);
			}
			else if (Client != null)
			{
				var nameLowerCase = name.ToLower().Trim();

				// Send request to server to get the ID for this name.
				if (!pendingIdRequests.TryGetValue(NamingSystemType.CharacterName, out Dictionary<string, Action<long>> pendingActions))
				{
					pendingIdRequests.Add(NamingSystemType.CharacterName, pendingActions = new Dictionary<string, Action<long>>());
				}
				if (!pendingActions.ContainsKey(nameLowerCase))
				{
					pendingActions.Add(nameLowerCase, action);

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
					// Multiple callbacks for the same name are combined.
					pendingActions[nameLowerCase] += action;
				}
			}
		}

		/// <summary>
		/// Handler for naming broadcasts from the server. Invokes pending name callbacks and updates local cache.
		/// </summary>
		/// <param name="msg">The naming broadcast message.</param>
		/// <param name="channel">The network channel used.</param>
		private static void OnClientNamingBroadcastReceived(NamingBroadcast msg, Channel channel)
		{
			if (pendingNameRequests.TryGetValue(msg.Type, out Dictionary<long, Action<string>> pendingRequests))
			{
				if (pendingRequests.TryGetValue(msg.ID, out Action<string> pendingActions))
				{
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

		/// <summary>
		/// Handler for reverse naming broadcasts from the server. Invokes pending ID callbacks and updates local cache.
		/// </summary>
		/// <param name="msg">The reverse naming broadcast message.</param>
		/// <param name="channel">The network channel used.</param>
		private static void OnClientReverseNamingBroadcastReceived(ReverseNamingBroadcast msg, Channel channel)
		{
			if (pendingIdRequests.TryGetValue(msg.Type, out Dictionary<string, Action<long>> pendingRequests))
			{
				if (pendingRequests.TryGetValue(msg.NameLowerCase, out Action<long> pendingActions))
				{
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

		/// <summary>
		/// Updates the local cache with a new name and ID mapping for the given type.
		/// </summary>
		/// <param name="type">The naming system type.</param>
		/// <param name="id">The unique ID.</param>
		/// <param name="name">The name to associate with the ID.</param>
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