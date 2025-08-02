using FishNet.Managing.Server;
using UnityEngine;
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using FishMMO.Shared;
using FishMMO.Logging;

namespace FishMMO.Server
{
	/// <summary>
	/// Base class for all server-side behaviours in the FishMMO server architecture.
	/// Provides registration, initialization, and lifecycle management for server behaviours.
	/// </summary>
	public abstract class ServerBehaviour : MonoBehaviour
	{
		/// <summary>
		/// Static dictionary mapping behaviour types to their instances for global access and management.
		/// </summary>
		private static Dictionary<Type, ServerBehaviour> behaviours = new Dictionary<Type, ServerBehaviour>();

		/// <summary>
		/// Registers a server behaviour instance for global access.
		/// </summary>
		/// <typeparam name="T">Type of the server behaviour.</typeparam>
		/// <param name="behaviour">The behaviour instance to register.</param>
		internal static void Register<T>(T behaviour) where T : ServerBehaviour
		{
			if (behaviour == null)
			{
				return;
			}
			Type type = behaviour.GetType();
			if (behaviours.ContainsKey(type))
			{
				return;
			}
			Log.Debug("ServerBehaviour", "Registered " + type.Name);
			behaviours.Add(type, behaviour);
		}

		/// <summary>
		/// Unregisters a server behaviour instance from global access.
		/// </summary>
		/// <typeparam name="T">Type of the server behaviour.</typeparam>
		/// <param name="behaviour">The behaviour instance to unregister.</param>
		internal static void Unregister<T>(T behaviour) where T : ServerBehaviour
		{
			if (behaviour == null)
			{
				return;
			}
			else
			{
				Type type = behaviour.GetType();
				Log.Debug("ServerBehaviour", "Unregistered " + type.Name);
				behaviours.Remove(type);
			}
		}

		/// <summary>
		/// Attempts to get a registered server behaviour of type T.
		/// </summary>
		/// <typeparam name="T">Type of the server behaviour.</typeparam>
		/// <param name="control">The output behaviour instance if found.</param>
		/// <returns>True if found, false otherwise.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
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

		/// <summary>
		/// Gets a registered server behaviour of type T, or null if not found.
		/// </summary>
		/// <typeparam name="T">Type of the server behaviour.</typeparam>
		/// <returns>The behaviour instance if found, otherwise null.</returns>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static T Get<T>() where T : ServerBehaviour
		{
			if (behaviours.TryGetValue(typeof(T), out ServerBehaviour result))
			{
				return result as T;
			}
			return null;
		}

		/// <summary>
		/// Initializes all registered server behaviours with the provided server and server manager.
		/// </summary>
		/// <param name="server">The server instance.</param>
		/// <param name="serverManager">The server manager instance.</param>
		public static void InitializeOnceInternal(Server server, ServerManager serverManager)
		{
			if (behaviours == null ||
				behaviours.Count == 0)
			{
				return;
			}

			Log.Debug("ServerBehaviour", "Initializing");

			foreach (ServerBehaviour behaviour in behaviours.Values)
			{
				behaviour.InternalInitializeOnce(server, serverManager);
			}

			Log.Debug("ServerBehaviour", "Initialization Complete");
		}

		/// <summary>
		/// Indicates whether this behaviour has been initialized.
		/// </summary>
		public bool Initialized { get; private set; }
		/// <summary>
		/// Reference to the server instance associated with this behaviour.
		/// </summary>
		public Server Server { get; private set; }
		/// <summary>
		/// Reference to the server manager instance associated with this behaviour.
		/// </summary>
		public ServerManager ServerManager { get; private set; }

		/// <summary>
		/// Internal initialization logic for this behaviour. Sets server and manager references and calls InitializeOnce.
		/// </summary>
		/// <param name="server">The server instance.</param>
		/// <param name="serverManager">The server manager instance.</param>
		internal void InternalInitializeOnce(Server server, ServerManager serverManager)
		{
			if (Initialized)
				return;

			Server = server;
			ServerManager = serverManager;
			Initialized = true;

			InitializeOnce();

			Log.Debug("ServerBehaviour", "Initialized[" + this.GetType().Name + "]");
		}

		/// <summary>
		/// Called once to initialize the behaviour. Must be implemented by derived classes.
		/// </summary>
		public abstract void InitializeOnce();

		/// <summary>
		/// Called when the behaviour is being destroyed. Must be implemented by derived classes.
		/// </summary>
		public abstract void Destroying();

		/// <summary>
		/// Unity Awake callback. Registers this behaviour instance.
		/// </summary>
		private void Awake()
		{
			ServerBehaviour.Register(this);
		}

		/// <summary>
		/// Unity OnDestroy callback. Calls Destroying and unregisters this behaviour instance.
		/// </summary>
		private void OnDestroy()
		{
			Destroying();

			ServerBehaviour.Unregister(this);
		}

		/// <summary>
		/// Unity OnApplicationQuit callback. Calls Destroying and unregisters this behaviour instance.
		/// </summary>
		private void OnApplicationQuit()
		{
			Destroying();

			ServerBehaviour.Unregister(this);
		}
	}
}