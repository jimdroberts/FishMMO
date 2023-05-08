using FishNet.Managing.Client;
using FishNet.Managing.Server;
using UnityEngine;
using System;
using System.Collections.Generic;

namespace Server
{
	public abstract class ServerBehaviour : MonoBehaviour
	{
		private static Dictionary<Type, ServerBehaviour> behaviours = new Dictionary<Type, ServerBehaviour>();

		internal static void Register<T>(T behaviour) where T : ServerBehaviour
		{
			if (behaviour == null)
			{
				return;
			}
			if (behaviours.ContainsKey(typeof(T)))
			{
				return;
			}
			//Debug.Log("UIManager: Registered " + control.Name);
			behaviours.Add(typeof(T), behaviour);
		}

		internal static void Unregister<T>(T behaviour) where T : ServerBehaviour
		{
			if (behaviour == null)
			{
				return;
			}
			else
			{
				//Debug.Log("UIManager: Unregistered " + control.Name);
				behaviours.Remove(typeof(T));
			}
		}

		public static bool TryGet<T>(out T control) where T : ServerBehaviour
		{
			if (behaviours.TryGetValue(typeof(T), out ServerBehaviour result))
			{
				if ((control = result as T) != null)
				{
					return true;
				}
			}
			control = null;
			return false;
		}

		public static T Get<T>() where T : ServerBehaviour
		{
			if (behaviours.TryGetValue(typeof(T), out ServerBehaviour result))
			{
				return result as T;
			}
			return null;
		}

		public bool Initialized { get; private set; }
		public Server Server { get; private set; }
		public ServerManager ServerManager { get; private set; }
		// ClientManager is used on the servers for Server<->Server communication. *NOTE* Check if null!
		public ClientManager ClientManager { get; private set; }

		/// <summary>
		/// Initializes the server behaviour. Use this if your system requires only Server management.
		/// </summary>
		internal void InternalInitializeOnce(Server server, ServerManager serverManager)
		{
			InternalInitializeOnce(server, serverManager, null);
		}

		/// <summary>
		/// Initializes the server behaviour. Use this if your system requires both Server and Client management.
		/// </summary>
		internal void InternalInitializeOnce(Server server, ServerManager serverManager, ClientManager clientManager)
		{
			if (Initialized)
				return;

			ServerBehaviour.Register(this);

			Server = server;
			ServerManager = serverManager;
			ClientManager = clientManager;
			Initialized = true;

			InitializeOnce();
		}

		public abstract void InitializeOnce();

		private void OnDestroy()
		{
			ServerBehaviour.Unregister(this);
		}
	}
}