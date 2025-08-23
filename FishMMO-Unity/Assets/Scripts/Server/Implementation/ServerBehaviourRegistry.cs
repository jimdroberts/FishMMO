using System;
using System.Collections.Generic;
using FishMMO.Logging;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Handles registration, lookup, and initialization of <see cref="ServerBehaviour"/> instances.
	/// Provides global access and lifecycle management for server-side behaviours.
	/// </summary>
	public static class ServerBehaviourRegistry
	{
		/// <summary>
		/// Static dictionary mapping behaviour types to their instances for global access and management.
		/// </summary>
		private static Dictionary<Type, ServerBehaviour> behaviours = new Dictionary<Type, ServerBehaviour>();

		/// <summary>
		/// Registers a <see cref="ServerBehaviour"/> instance for global access.
		/// </summary>
		/// <typeparam name="T">Type of the server behaviour.</typeparam>
		/// <param name="behaviour">The behaviour instance to register.</param>
		public static void Register<T>(T behaviour) where T : ServerBehaviour
		{
			if (behaviour == null)
				return;
			Type type = behaviour.GetType();
			if (behaviours.ContainsKey(type))
				return;
			Log.Debug("ServerBehaviourRegistry", "Registered " + type.Name);
			behaviours.Add(type, behaviour);
		}

		/// <summary>
		/// Unregisters a <see cref="ServerBehaviour"/> instance from global access.
		/// </summary>
		/// <typeparam name="T">Type of the server behaviour.</typeparam>
		/// <param name="behaviour">The behaviour instance to unregister.</param>
		public static void Unregister<T>(T behaviour) where T : ServerBehaviour
		{
			if (behaviour == null)
				return;
			Type type = behaviour.GetType();
			Log.Debug("ServerBehaviourRegistry", "Unregistered " + type.Name);
			behaviours.Remove(type);
		}

		/// <summary>
		/// Attempts to get a registered <see cref="ServerBehaviour"/> of type <typeparamref name="T"/>.
		/// </summary>
		/// <typeparam name="T">Type of the server behaviour.</typeparam>
		/// <param name="control">The output behaviour instance if found.</param>
		/// <returns>True if found, false otherwise.</returns>
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
		/// Gets a registered <see cref="ServerBehaviour"/> of type <typeparamref name="T"/>, or null if not found.
		/// </summary>
		/// <typeparam name="T">Type of the server behaviour.</typeparam>
		/// <returns>The behaviour instance if found, otherwise null.</returns>
		public static T Get<T>() where T : ServerBehaviour
		{
			if (behaviours.TryGetValue(typeof(T), out ServerBehaviour result))
			{
				return result as T;
			}
			return null;
		}

		/// <summary>
		/// Initializes all registered <see cref="ServerBehaviour"/>s with the provided server and server manager.
		/// </summary>
		/// <param name="server">The server instance.</param>
		/// <param name="serverManager">The server manager instance.</param>
		public static void InitializeOnceInternal(Server server)
		{
			if (behaviours == null || behaviours.Count == 0)
				return;
			Log.Debug("ServerBehaviourRegistry", "Initializing");
			foreach (ServerBehaviour behaviour in behaviours.Values)
			{
				behaviour.InternalInitializeOnce(server, server.NetworkWrapper.NetworkManager.ServerManager);
			}
			Log.Debug("ServerBehaviourRegistry", "Initialization Complete");
		}
	}
}