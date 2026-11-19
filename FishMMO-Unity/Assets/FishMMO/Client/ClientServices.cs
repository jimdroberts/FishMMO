using System;
using System.Collections.Generic;
using FishNet.Managing;

namespace FishMMO.Client
{
	/// <summary>
	/// A module's piece of the client that lives as long as the client does (Phase 4 of the core and modules plan, 2026-11-18):
	/// the weather's mirror of the scene server's weather. The client makes one of each registered service when it starts,
	/// ticks them every frame, clears them when the world is left or the scene server changes, and shuts them down with itself.
	/// </summary>
	public interface IClientService
	{
		/// <summary>Called once when the client starts: register broadcasts here.</summary>
		void Initialize(NetworkManager manager);

		/// <summary>Called every frame.</summary>
		void Tick(float deltaTime);

		/// <summary>Called when the world is left or another scene server takes over: drop what the last one sent.</summary>
		void Clear();

		/// <summary>Called once when the client shuts down: unregister here.</summary>
		void Shutdown();
	}

	/// <summary>The services modules add to the client (<see cref="IClientService"/>), by id, in registration order.</summary>
	public static class ClientServices
	{
		private static readonly List<KeyValuePair<string, Func<IClientService>>> factories = new List<KeyValuePair<string, Func<IClientService>>>();

		/// <summary>Registers a service; registering an id again replaces it (a module's installer runs on every domain load).</summary>
		public static void Register(string id, Func<IClientService> create)
		{
			if (string.IsNullOrEmpty(id) || create == null)
			{
				return;
			}
			for (int i = 0; i < factories.Count; i++)
			{
				if (factories[i].Key == id)
				{
					factories[i] = new KeyValuePair<string, Func<IClientService>>(id, create);
					return;
				}
			}
			factories.Add(new KeyValuePair<string, Func<IClientService>>(id, create));
		}

		/// <summary>The registered services' ids.</summary>
		public static IEnumerable<string> Ids
		{
			get
			{
				foreach (KeyValuePair<string, Func<IClientService>> entry in factories)
				{
					yield return entry.Key;
				}
			}
		}

		/// <summary>One new instance of every registered service, initialized.</summary>
		public static List<IClientService> CreateAll(NetworkManager manager)
		{
			var services = new List<IClientService>(factories.Count);
			foreach (KeyValuePair<string, Func<IClientService>> entry in factories)
			{
				IClientService service = entry.Value();
				if (service != null)
				{
					service.Initialize(manager);
					services.Add(service);
				}
			}
			return services;
		}
	}
}
