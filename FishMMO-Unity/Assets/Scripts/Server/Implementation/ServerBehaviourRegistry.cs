using System;
using System.Collections.Generic;
using FishMMO.Logging;
using FishMMO.Server.Core;
using FishNet.Connection;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Handles registration, lookup, and initialization of <see cref="IServerBehaviour"/> instances.
	/// Provides global access and lifecycle management for server-side behaviours.
	/// </summary>
	public class ServerBehaviourRegistry : IServerBehaviourRegistry<INetworkManagerWrapper, NetworkConnection, IServerBehaviour>
	{
		/// <summary>
		/// Dictionary mapping behaviour interface types (and concrete types) to their instances.
		/// Keys are interface types that implement <see cref="IServerBehaviour"/> or the concrete behaviour types.
		/// </summary>
		private Dictionary<Type, IServerBehaviour> behaviours = new Dictionary<Type, IServerBehaviour>();

		/// <summary>
		/// Registers a <see cref="IServerBehaviour"/> instance for global access.
		/// The behaviour will be registered under every interface it implements that derives from <see cref="IServerBehaviour"/>,
		/// as well as under its concrete type.
		/// </summary>
		public void Register<T>(T behaviour) where T : class, IServerBehaviour
		{
			if (behaviour == null)
				return;

			Type concreteType = behaviour.GetType();

			// Register under concrete type if not already present
			if (!behaviours.ContainsKey(concreteType))
			{
				behaviours.Add(concreteType, behaviour);
				Log.Debug("ServerBehaviourRegistry", "Registered " + concreteType.Name);
			}

			// Register under all IServerBehaviour interfaces implemented by this behaviour
			var interfaces = concreteType.GetInterfaces();
			foreach (var iface in interfaces)
			{
				if (iface == typeof(IServerBehaviour))
				{
					continue;
				}
				if (!typeof(IServerBehaviour).IsAssignableFrom(iface))
				{
					continue;
				}
				if (!behaviours.ContainsKey(iface))
				{
					behaviours.Add(iface, behaviour);
					Log.Debug("ServerBehaviourRegistry", "Registered interface " + iface.Name + " => " + concreteType.Name);
				}
			}
		}

		/// <summary>
		/// Unregisters a <see cref="IServerBehaviour"/> instance from global access.
		/// Removes any entries keyed by the concrete type and by any interfaces it implemented.
		/// </summary>
		public void Unregister<T>(T behaviour) where T : class, IServerBehaviour
		{
			if (behaviour == null)
				return;

			Type concreteType = behaviour.GetType();

			// Remove concrete type
			if (behaviours.ContainsKey(concreteType))
			{
				behaviours.Remove(concreteType);
				Log.Debug("ServerBehaviourRegistry", "Unregistered " + concreteType.Name);
			}

			// Remove any interface registrations pointing to this behaviour
			var interfaces = concreteType.GetInterfaces();
			foreach (var iface in interfaces)
			{
				if (iface == typeof(IServerBehaviour))
				{
					continue;
				}
				if (!typeof(IServerBehaviour).IsAssignableFrom(iface))
				{
					continue;
				}
				if (behaviours.TryGetValue(iface, out IServerBehaviour existing) && existing == behaviour)
				{
					behaviours.Remove(iface);
					Log.Debug("ServerBehaviourRegistry", "Unregistered interface " + iface.Name + " => " + concreteType.Name);
				}
			}
		}

		/// <summary>
		/// Attempts to get a registered <see cref="IServerBehaviour"/> of type <typeparamref name="T"/>.
		/// </summary>
		/// <typeparam name="T">Type of the server behaviour.</typeparam>
		/// <param name="control">The output behaviour instance if found.</param>
		/// <returns>True if found, false otherwise.</returns>
		public bool TryGet<T>(out T control) where T : class, IServerBehaviour
		{
			Type type = typeof(T);

			if (behaviours.TryGetValue(type, out IServerBehaviour result))
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
		/// Gets a registered <see cref="IServerBehaviour"/> of type <typeparamref name="T"/>, or null if not found.
		/// </summary>
		/// <typeparam name="T">Type of the server behaviour.</typeparam>
		/// <returns>The behaviour instance if found, otherwise null.</returns>
		public T Get<T>() where T : class, IServerBehaviour
		{
			Type type = typeof(T);
			if (behaviours.TryGetValue(type, out IServerBehaviour result))
			{
				return result as T;
			}
			return null;
		}

		/// <summary>
		/// Initializes all registered <see cref="IServerBehaviour"/>s with the provided server and server manager.
		/// </summary>
		/// <param name="server">The server instance.</param>
		/// <param name="serverManager">The server manager instance.</param>
		public void InitializeOnceInternal(IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> server)
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

		/// <summary>
		/// Destroys all registered behaviours.
		/// </summary>
		public void DestroyAll()
		{
			if (behaviours == null || behaviours.Count == 0)
				return;
			Log.Debug("ServerBehaviourRegistry", "Destroying All");
			foreach (ServerBehaviour behaviour in behaviours.Values)
			{
				behaviour.Destroying();
			}
			behaviours.Clear();
			Log.Debug("ServerBehaviourRegistry", "All Destroyed");
		}
	}
}