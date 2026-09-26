using FishNet.Transporting;
using System;
using System.Collections.Generic;
using System.IO;
using FishMMO.Shared;
using FishMMO.Logging;

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
		internal static Client client;

		/// <summary>
		/// Maps naming system type and ID to name. Used for fast lookup and caching.
		/// </summary>
		private static Dictionary<NamingSystemType, Dictionary<long, string>> idToName = new Dictionary<NamingSystemType, Dictionary<long, string>>();
		/// <summary>
		/// Maps character names to their unique IDs. Assumes character names are unique.
		/// </summary>
		private static Dictionary<string, long> nameToID = new Dictionary<string, long>();
		/// <summary>
		/// Pending name requests by type: who waits on which ID, which IDs go out in the next batch,
		/// and when each was last sent. See <see cref="PendingNameRequests"/>.
		/// </summary>
		/// <remarks>
		/// An ask queues its ID and <see cref="FlushNameRequests"/> sends the queue once a frame, one
		/// <see cref="NamingRequestBatchBroadcast"/> per type; it used to send one request per ID the
		/// moment it was asked (hot-path audit M18). The server budgets naming requests per
		/// connection (a token bucket) and answers an ID over its budget with nothing, so an ID still
		/// waiting after the retry window is queued again by the next ask for it.
		/// </remarks>
		private static Dictionary<NamingSystemType, PendingNameRequests> pendingNameRequests = new Dictionary<NamingSystemType, PendingNameRequests>();

		/// <summary>
		/// Reusable ID list for one outgoing batch. Main thread only.
		/// </summary>
		private static readonly List<long> nameBatchScratch = new List<long>(NamingRequestBatchBroadcast.MaxIDs);

		/// <summary>
		/// Tracks pending ID requests by type and name, with callbacks to invoke when IDs are received.
		/// </summary>
		private static Dictionary<NamingSystemType, Dictionary<string, Action<long>>> pendingIdRequests = new Dictionary<NamingSystemType, Dictionary<string, Action<long>>>();

		/// <summary>
		/// When each pending ID request (a reverse lookup by name) was last sent, so a lookup the
		/// server did not answer is sent again the next time something asks for that name, instead
		/// of holding every later caller forever. Reverse lookups stay one name per request: they
		/// come from a player typing a name, never in bursts.
		/// </summary>
		private static Dictionary<NamingSystemType, Dictionary<string, double>> pendingIdSentAt = new Dictionary<NamingSystemType, Dictionary<string, double>>();

		/// <summary>Seconds after which a still-pending name or ID request is sent again on the next ask.</summary>
		private const double NameRequestRetrySeconds = 2.0;

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

			ClientNamingSystem.client = client;

			Client.NetworkManager.ClientManager.RegisterBroadcast<NamingBroadcast>(OnClientNamingBroadcastReceived);
			Client.NetworkManager.ClientManager.RegisterBroadcast<NamingBatchBroadcast>(OnClientNamingBatchBroadcastReceived);
			Client.NetworkManager.ClientManager.RegisterBroadcast<ReverseNamingBroadcast>(OnClientReverseNamingBroadcastReceived);

			/* LateUpdate: after every script's Update and every handler of the frame has asked, so
			 * one frame's asks leave as one request per type. */
			if (Client.NetworkManager.TimeManager != null)
			{
				Client.NetworkManager.TimeManager.OnLateUpdate -= FlushNameRequests;
				Client.NetworkManager.TimeManager.OnLateUpdate += FlushNameRequests;
			}

#if !UNITY_EDITOR && !UNITY_WEBGL
			string workingDirectory = Constants.GetWorkingDirectory();
			foreach (NamingSystemType type in EnumExtensions.ToArray<NamingSystemType>())
			{
				idToName[type] = DictionaryCompression.ReadFromGZipFile(Path.Combine(workingDirectory, type.ToString() + ".bin"));
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
			if (client != null)
			{
				Client.NetworkManager.ClientManager.UnregisterBroadcast<NamingBroadcast>(OnClientNamingBroadcastReceived);
				Client.NetworkManager.ClientManager.UnregisterBroadcast<NamingBatchBroadcast>(OnClientNamingBatchBroadcastReceived);
				Client.NetworkManager.ClientManager.UnregisterBroadcast<ReverseNamingBroadcast>(OnClientReverseNamingBroadcastReceived);
				if (Client.NetworkManager.TimeManager != null)
				{
					Client.NetworkManager.TimeManager.OnLateUpdate -= FlushNameRequests;
				}
			}

#if !UNITY_EDITOR && !UNITY_WEBGL
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
			else if (client != null)
			{
				/* Queued, not sent: FlushNameRequests sends every ID asked for this frame as one
				 * request. An ID already waiting is queued again only once its last request is old
				 * enough to have gone unanswered; the reply resolves the whole chain. */
				if (!pendingNameRequests.TryGetValue(type, out PendingNameRequests requests))
				{
					pendingNameRequests.Add(type, requests = new PendingNameRequests(NameRequestRetrySeconds));
				}
				requests.Request(id, action, UnityEngine.Time.unscaledTimeAsDouble);
			}
		}

		/// <summary>
		/// Sends the name requests queued this frame: one <see cref="NamingRequestBatchBroadcast"/>
		/// per type, of up to <see cref="NamingRequestBatchBroadcast.MaxIDs"/> IDs. The rest go
		/// next frame. Runs in LateUpdate.
		/// </summary>
		/// <remarks>
		/// Held while the client is not connected: a request sent then is dropped by the transport,
		/// and an ID stamped as sent would wait out a retry window for nothing.
		/// </remarks>
		private static void FlushNameRequests()
		{
			if (client == null ||
				pendingNameRequests.Count == 0 ||
				Client.NetworkManager == null ||
				!Client.NetworkManager.IsClientStarted)
			{
				return;
			}

			double now = UnityEngine.Time.unscaledTimeAsDouble;
			foreach (KeyValuePair<NamingSystemType, PendingNameRequests> pair in pendingNameRequests)
			{
				if (pair.Value.QueuedCount == 0)
				{
					continue;
				}

				nameBatchScratch.Clear();
				if (pair.Value.TakeBatch(now, NamingRequestBatchBroadcast.MaxIDs, nameBatchScratch) > 0)
				{
					Client.Broadcast(new NamingRequestBatchBroadcast()
					{
						Type = pair.Key,
						IDs = nameBatchScratch.ToArray(),
					}, Channel.Reliable);
				}
			}
			nameBatchScratch.Clear();
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
			else if (client != null)
			{
				var nameLowerCase = name.ToLowerInvariant().Trim();

				// Send request to server to get the ID for this name.
				if (!pendingIdRequests.TryGetValue(NamingSystemType.CharacterName, out Dictionary<string, Action<long>> pendingActions))
				{
					pendingIdRequests.Add(NamingSystemType.CharacterName, pendingActions = new Dictionary<string, Action<long>>());
				}
				if (!pendingIdSentAt.TryGetValue(NamingSystemType.CharacterName, out Dictionary<string, double> sentAt))
				{
					pendingIdSentAt.Add(NamingSystemType.CharacterName, sentAt = new Dictionary<string, double>());
				}
				double now = UnityEngine.Time.unscaledTimeAsDouble;
				bool send;
				if (!pendingActions.ContainsKey(nameLowerCase))
				{
					pendingActions.Add(nameLowerCase, action);
					send = true;
				}
				else
				{
					// Multiple callbacks for the same name are combined.
					pendingActions[nameLowerCase] += action;

					// Sent again if the first request is old enough to have gone unanswered.
					send = !sentAt.TryGetValue(nameLowerCase, out double last) || now - last >= NameRequestRetrySeconds;
				}

				if (send)
				{
					sentAt[nameLowerCase] = now;
					Client.Broadcast(new ReverseNamingBroadcast()
					{
						Type = NamingSystemType.CharacterName,
						NameLowerCase = nameLowerCase,
						ID = 0,
						Name = "",
					}, Channel.Reliable);
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
			ApplyNameAnswer(msg.Type, msg.ID, msg.Name, UnityEngine.Time.unscaledTimeAsDouble);
		}

		/// <summary>
		/// Handler for batched naming answers from the server. Applies each entry as a single answer.
		/// </summary>
		/// <param name="msg">The batched naming broadcast.</param>
		/// <param name="channel">The network channel used.</param>
		private static void OnClientNamingBatchBroadcastReceived(NamingBatchBroadcast msg, Channel channel)
		{
			if (msg.IDs == null || msg.Names == null)
			{
				return;
			}

			double now = UnityEngine.Time.unscaledTimeAsDouble;
			int count = Math.Min(Math.Min(msg.IDs.Length, msg.Names.Length), NamingBatchBroadcast.MaxEntries);
			for (int i = 0; i < count; i++)
			{
				ApplyNameAnswer(msg.Type, msg.IDs[i], msg.Names[i], now);
			}
		}

		/// <summary>
		/// Applies one answer for an ID: records the name and calls everything waiting on it, or,
		/// for an empty name (no such entity), drops what was waiting.
		/// </summary>
		/// <param name="type">The naming system type.</param>
		/// <param name="id">The ID answered.</param>
		/// <param name="name">Its name, or empty when the server found no such entity.</param>
		/// <param name="now">The current time, in seconds.</param>
		private static void ApplyNameAnswer(NamingSystemType type, long id, string name, double now)
		{
			pendingNameRequests.TryGetValue(type, out PendingNameRequests requests);

			if (string.IsNullOrEmpty(name))
			{
				/* Not cached, and nothing is called: the callers expect a name. What was waiting is
				 * released instead of held for the session, and the ID is not asked for again until
				 * its retry window has passed. */
				requests?.ResolveMissing(id, now);
				return;
			}

			// Known before the callbacks run, so one that asks for the same name again finds it.
			if (id != 0)
			{
				UpdateKnownNames(type, id, name);
			}

			if (requests != null && requests.Resolve(id, out Action<string> callbacks) && callbacks != null)
			{
				/* One failing callback costs its own ID, not the rest of a batch answer. */
				try
				{
					callbacks(name);
				}
				catch (Exception ex)
				{
					Log.Error("ClientNamingSystem", $"A callback waiting on {type} {id} threw: {ex}");
				}
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
				if (pendingIdSentAt.TryGetValue(msg.Type, out Dictionary<string, double> sentAt))
				{
					sentAt.Remove(msg.NameLowerCase);
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