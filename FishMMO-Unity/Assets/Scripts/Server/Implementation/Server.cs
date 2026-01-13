using System;
using System.Linq;
using System.Runtime.CompilerServices;
using UnityEngine;
using UnityEditor;
using FishNet.Managing;
using FishNet.Transporting;
using KinematicCharacterController;
using FishMMO.Logging;
using FishMMO.Server.Core;
using FishMMO.Shared;
using FishMMO.Server.Core.Account;
using FishNet.Connection;
using System.Collections.Generic;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Composition root: orchestrates Core and Implementation into a running server.
	/// </summary>
	public class Server : MonoBehaviour,
		IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour>,
		IServer<INetworkManagerWrapper, NetworkConnection, IRuntimeDataContainer>,
		IPeriodicUpdateSystem
	{
		/// <summary>
		/// Optional override for the server's bind address.
		/// </summary>
		[Header("Overrides")]
		public string AddressOverride;

		/// <summary>
		/// Optional override for the server's bind port.
		/// </summary>
		public ushort PortOverride;

		/// <summary>
		/// Gets the core server logic instance.
		/// </summary>
		public ICoreServer CoreServer { get; private set; }

		/// <summary>
		/// Gets the network manager wrapper instance.
		/// </summary>
		public INetworkManagerWrapper NetworkWrapper { get; private set; }

		/// <summary>
		/// Gets the server address provider instance.
		/// </summary>
		public IServerAddressProvider AddressProvider { get; private set; }

		/// <summary>
		/// Gets the server configuration instance.
		/// </summary>
		public IServerConfiguration Configuration { get; private set; }

		/// <summary>
		/// Gets the account manager instance.
		/// </summary>
		public IAccountManager<NetworkConnection> AccountManager { get; private set; }

		/// <summary>
		/// Gets the server events instance.
		/// </summary>
		public IServerEvents ServerEvents { get; private set; }

		/// <summary>
		/// Current connection state of the server.
		/// </summary>
		private ConnectionState serverState;

		/// <summary>
		/// Gets the current connection state of the server.
		/// </summary>
		public ConnectionState ServerState => serverState;

		/// <summary>
		/// List of all server behaviours attached to the server.
		/// </summary>
		[SerializeField]
		public List<ServerBehaviour> ServerBehaviours = new List<ServerBehaviour>();

		/// <summary>
		/// List of all runtime data containers managed by the server.
		/// </summary>
		public List<RuntimeDataContainer> DataContainers = new List<RuntimeDataContainer>();

		/// <summary>
		/// Registry that manages all server behaviours.
		/// </summary>
		public IServerBehaviourRegistry<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> BehaviourRegistry { get; private set; }

		/// <summary>
		/// Registry that manages all runtime data containers.
		/// </summary>
		public IServerComponentRegistry<INetworkManagerWrapper, NetworkConnection, IRuntimeDataContainer> DataContainerRegistry { get; private set; }

		/// <summary>
		/// Dictionary of registered periodic callbacks indexed by their callback delegate.
		/// </summary>
		private Dictionary<Action<float>, PeriodicCallbackData> periodicCallbacks = new Dictionary<Action<float>, PeriodicCallbackData>();

		/// <summary>
		/// Flag indicating whether the server has already performed shutdown.
		/// </summary>
		private bool hasShutdown = false;

		/// <summary>
		/// Unity Start method. Initializes and composes all server components.
		/// </summary>
		void Start()
		{
			hasShutdown = false;
			Log.Debug("Server", "Server is starting...");

			NetworkManager networkManager = FindFirstObjectByType<NetworkManager>();
			if (networkManager == null)
				throw new UnityException("Server: NetworkManager could not be found! Make sure you have a NetworkManager in your scene.");

			Configuration = new FileServerConfiguration();
			ServerEvents = new ServerEvents();

			CoreServer = new CoreServer(Configuration, ServerEvents);
			NetworkWrapper = new FishNetNetworkWrapper(networkManager, Configuration, this);

			ServerEvents.OnLoginServerInitialized -= () => Log.Debug("Server", "LoginServer initialized.");
			ServerEvents.OnWorldServerInitialized -= () => Log.Debug("Server", "WorldServer initialized.");
			ServerEvents.OnSceneServerInitialized -= () => Log.Debug("Server", "SceneServer initialized.");

			ServerEvents.OnLoginServerInitialized += () => Log.Debug("Server", "LoginServer initialized.");
			ServerEvents.OnWorldServerInitialized += () => Log.Debug("Server", "WorldServer initialized.");
			ServerEvents.OnSceneServerInitialized += () => Log.Debug("Server", "SceneServer initialized.");

			StartCoroutine(NetHelper.FetchExternalIPAddress(OnFinalizeSetup));
		}

		/// <summary>
		/// Finalizes server setup after fetching the external IP address.
		/// </summary>
		/// <param name="remoteAddress">The external remote address of the server.</param>
		private void OnFinalizeSetup(string remoteAddress)
		{
			if (string.IsNullOrWhiteSpace(remoteAddress))
				throw new UnityException("Server: Failed to retrieve Remote IP Address.");

			CoreServer.Initialize(remoteAddress, gameObject.scene.name);

			AddressProvider = new ServerAddressProvider(
				NetworkWrapper.NetworkManager.TransportManager.Transport,
				AddressOverride,
				PortOverride,
				CoreServer.Address,
				CoreServer.RemoteAddress);

			NetworkWrapper.ApplyTransportConfiguration();
			NetworkWrapper.AttachLoginAuthenticator(this);
			NetworkWrapper.RegisterServerConnectionStateEventHandler(ServerManager_OnServerConnectionState);

			AccountManager = new AccountManager();

			// Initialize all registered runtime data containers
			DataContainerRegistry = new RuntimeDataContainerRegistry();
			DiscoverAndCreateDataContainers();
			RegisterAllDataContainers();
			DataContainerRegistry.InitializeAll(this);

			// Initialize all registered server behaviours
			BehaviourRegistry = new ServerBehaviourRegistry() as IServerBehaviourRegistry<INetworkManagerWrapper, NetworkConnection, IServerBehaviour>;
			RegisterAllBehaviours();
			BehaviourRegistry.InitializeAll(this);

			KinematicCharacterSystem.EnsureCreation();
			KinematicCharacterSystem.Settings.AutoSimulation = false;

			NetworkWrapper.StartServer();

			Log.Debug("Server", "Initialization Complete");
		}

		/// <summary>
		/// Handles server connection state changes and logs address information.
		/// </summary>
		/// <param name="args">The server connection state arguments.</param>
		private void ServerManager_OnServerConnectionState(ServerConnectionStateArgs args)
		{
			serverState = MapConnectionState(args.ConnectionState);

			if (AddressProvider.TryGetServerIPAddress(out ServerAddress address))
			{
				Log.Debug("Server",
					$"Local: {address.Address}:{address.Port} Remote: {CoreServer.RemoteAddress}:{address.Port} - {args.ConnectionState}");
			}
		}

		/// <summary>
		/// Maps FishNet's LocalConnectionState to the server's ConnectionState enum.
		/// </summary>
		private ConnectionState MapConnectionState(LocalConnectionState fishNetState)
		{
			return fishNetState switch
			{
				LocalConnectionState.Started => ConnectionState.Started,
				LocalConnectionState.Starting => ConnectionState.Starting,
				LocalConnectionState.Stopping => ConnectionState.Stopping,
				LocalConnectionState.Stopped => ConnectionState.Stopped,
				_ => ConnectionState.Stopped
			};
		}

		/// <summary>
		/// Unity LateUpdate method. Orchestrates server behaviour updates and periodic callback dispatch.
		/// </summary>
		void LateUpdate()
		{
			float deltaTime = Time.deltaTime;
			UpdateServerBehaviours(deltaTime);
			UpdatePeriodicCallbacks(deltaTime);
		}

		/// <summary>
		/// Updates all registered ServerBehaviours.
		/// </summary>
		/// <param name="deltaTime">Time elapsed since last frame.</param>
		private void UpdateServerBehaviours(float deltaTime)
		{
			if (ServerBehaviours == null)
				return;

			for (int i = 0; i < ServerBehaviours.Count; i++)
			{
				var behaviour = ServerBehaviours[i];
				if (behaviour != null && behaviour.Initialized)
				{
					behaviour.OnLateUpdate(deltaTime);
				}
			}
		}

		/// <summary>
		/// Processes and dispatches periodic callbacks that are ready to execute.
		/// </summary>
		/// <param name="deltaTime">Time elapsed since last frame.</param>
		private void UpdatePeriodicCallbacks(float deltaTime)
		{
			if (periodicCallbacks.Count == 0)
				return;

			// Create temporary list to avoid modification during iteration
			var callbacksToInvoke = new List<PeriodicCallbackData>();

			foreach (var kvp in periodicCallbacks)
			{
				var data = kvp.Value;
				data.TimeRemaining -= deltaTime;

				if (data.TimeRemaining <= 0)
				{
					callbacksToInvoke.Add(data);
				}
			}

			// Invoke callbacks that are ready
			foreach (var data in callbacksToInvoke)
			{
				try
				{
					data.Callback?.Invoke(deltaTime);
					data.TimeRemaining = data.Interval; // Reset timer
				}
				catch (Exception ex)
				{
					Log.Error("Server", $"Error invoking periodic callback {data.Callback.Method.DeclaringType?.Name}.{data.Callback.Method.Name}: {ex.Message}");
				}
			}
		}

		/// <summary>
		/// Unity OnDestroy callback. Deinitializes all server components and cleans up resources.
		/// </summary>
		void OnDestroy()
		{
			PerformShutdown();
		}

		/// <summary>
		/// Unity OnApplicationQuit callback. Deinitializes all server components and cleans up resources.
		/// </summary>
		void OnApplicationQuit()
		{
			PerformShutdown();
		}

		private void PerformShutdown()
		{
			if (hasShutdown) return;
			hasShutdown = true;

			periodicCallbacks.Clear();

			DeinitializeAllBehaviours();
			UnregisterAllBehaviours();

			DeinitializeAllDataContainers();
			UnregisterAllDataContainers();

			NetworkWrapper?.StopServer();
			CoreServer?.Deinitialize();
			AccountManager?.Clear();

			ServerEvents.OnLoginServerInitialized -= () => Log.Debug("Server", "LoginServer initialized.");
			ServerEvents.OnWorldServerInitialized -= () => Log.Debug("Server", "WorldServer initialized.");
			ServerEvents.OnSceneServerInitialized -= () => Log.Debug("Server", "SceneServer initialized.");

			NetworkWrapper.UnregisterServerConnectionStateEventHandler(ServerManager_OnServerConnectionState);
		}

		/// <summary>
		/// Registers all server behaviours in order.
		/// </summary>
		private void RegisterAllBehaviours()
		{
			if (ServerBehaviours != null && BehaviourRegistry != null)
			{
				// Register in order
				for (int i = 0; i < ServerBehaviours.Count; i++)
				{
					var behaviour = ServerBehaviours[i];
					if (behaviour != null && !behaviour.Initialized)
					{
						// Register to registry before initializing
						BehaviourRegistry.Register(behaviour);
					}
				}
			}
		}

		/// <summary>
		/// Discovers and creates all RuntimeDataContainers required by ServerBehaviours.
		/// Automatically instantiates containers based on RequiresDataContainer attributes.
		/// Multiple systems can declare the same container type, and only one instance will be created.
		/// </summary>
		private void DiscoverAndCreateDataContainers()
		{
			var factory = new RuntimeDataContainerFactory();
			var containerTypes = new HashSet<Type>(); // Prevent duplicates
			var containersByPriority = new SortedDictionary<int, List<Type>>();

			// Scan all ServerBehaviours for RequiresDataContainer attributes
			foreach (var behaviour in ServerBehaviours)
			{
				if (behaviour == null)
					continue;

				var attributes = behaviour.GetType()
					.GetCustomAttributes(typeof(RequiresDataContainerAttribute), false)
					.Cast<RequiresDataContainerAttribute>();

				foreach (var attr in attributes)
				{
					if (!containerTypes.Add(attr.ContainerType))
						continue; // Already registered - skip duplicate

					if (!factory.IsValidContainerType(attr.ContainerType))
					{
						Log.Warning("Server",
							$"Invalid container type {attr.ContainerType.Name} required by {behaviour.GetType().Name}. " +
							"Container must be a non-abstract class with a parameterless constructor.");
						continue;
					}

					// Group by priority
					if (!containersByPriority.ContainsKey(attr.InitializationPriority))
						containersByPriority[attr.InitializationPriority] = new List<Type>();

					containersByPriority[attr.InitializationPriority].Add(attr.ContainerType);
				}
			}

			// Create containers in priority order
			foreach (var priorityGroup in containersByPriority.Values)
			{
				foreach (var containerType in priorityGroup)
				{
					var container = factory.CreateContainer(containerType);
					DataContainers.Add((RuntimeDataContainer)container);
					Log.Debug("Server", $"Auto-created RuntimeDataContainer: {containerType.Name}");
				}
			}
		}

		/// <summary>
		/// Registers all runtime data containers in order.
		/// </summary>
		private void RegisterAllDataContainers()
		{
			if (DataContainers != null && DataContainerRegistry != null)
			{
				// Register in order
				for (int i = 0; i < DataContainers.Count; i++)
				{
					var container = DataContainers[i];
					if (container != null && !container.Initialized)
					{
						// Register to registry before initializing
						DataContainerRegistry.Register(container);
					}
				}
			}
		}

		/// <summary>
		/// Deinitializes all registered server behaviours in reverse order.
		/// </summary>
		private void DeinitializeAllBehaviours()
		{
			if (ServerBehaviours != null && BehaviourRegistry != null)
			{
				// Deinitialize in reverse order to ensure proper cleanup dependencies
				for (int i = ServerBehaviours.Count - 1; i >= 0; i--)
				{
					var behaviour = ServerBehaviours[i];
					if (behaviour != null && behaviour.Initialized)
					{
						// Deinitialize the behaviour (calls OnDeinitialize and clears references)
						behaviour.Deinitialize();
					}
				}
			}
		}

		/// <summary>
		/// Unregisters all registered server behaviours in reverse order.
		/// </summary>
		private void UnregisterAllBehaviours()
		{
			if (ServerBehaviours != null && BehaviourRegistry != null)
			{
				// Unregister in reverse order to ensure proper cleanup dependencies
				for (int i = ServerBehaviours.Count - 1; i >= 0; i--)
				{
					var behaviour = ServerBehaviours[i];
					if (behaviour != null && behaviour.Initialized)
					{
						// Unregister from registry before deinitializing
						BehaviourRegistry.Unregister(behaviour);
					}
				}
			}
		}

		/// <summary>
		/// Deinitializes all registered runtime data containers in reverse order.
		/// </summary>
		private void DeinitializeAllDataContainers()
		{
			if (DataContainerRegistry != null)
			{
				DataContainerRegistry.DeinitializeAll();
			}
		}

		/// <summary>
		/// Unregisters all registered runtime data containers in reverse order.
		/// </summary>
		private void UnregisterAllDataContainers()
		{
			if (DataContainers != null && DataContainerRegistry != null)
			{
				// Unregister in reverse order to ensure proper cleanup dependencies
				for (int i = DataContainers.Count - 1; i >= 0; i--)
				{
					var container = DataContainers[i];
					if (container != null && container.Initialized)
					{
						// Unregister from registry
						DataContainerRegistry.Unregister(container);
					}
				}
			}
		}

		/// <summary>
		/// Quits the application or exits play mode in the Unity Editor.
		/// </summary>
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static void Quit()
		{
#if UNITY_EDITOR
			EditorApplication.ExitPlaymode();
#else
			Application.Quit();
#endif
		}

		#region IPeriodicUpdateSystem Implementation

		/// <summary>
		/// Registers a callback to be invoked periodically at the specified interval.
		/// </summary>
		/// <param name="interval">Time in seconds between callback invocations.</param>
		/// <param name="callback">The callback to invoke. Receives delta time since last invocation.</param>
		public void RegisterPeriodicCallback(float interval, Action<float> callback)
		{
			if (callback == null)
			{
				Log.Error("Server", "Cannot register null periodic callback.");
				return;
			}

			if (interval <= 0)
			{
				Log.Error("Server", $"Cannot register periodic callback with non-positive interval: {interval}");
				return;
			}

			if (periodicCallbacks.ContainsKey(callback))
			{
				Log.Warning("Server", $"Periodic callback {callback.Method.DeclaringType?.Name}.{callback.Method.Name} is already registered. Updating interval.");
				periodicCallbacks[callback].Interval = interval;
				periodicCallbacks[callback].TimeRemaining = interval;
				return;
			}

			periodicCallbacks[callback] = new PeriodicCallbackData(interval, callback);
			Log.Debug("Server", $"Registered periodic callback: {callback.Method.DeclaringType?.Name}.{callback.Method.Name} (interval: {interval}s)");
		}

		/// <summary>
		/// Unregisters a previously registered periodic callback.
		/// </summary>
		/// <param name="callback">The callback to unregister.</param>
		public void UnregisterPeriodicCallback(Action<float> callback)
		{
			if (callback == null)
			{
				Log.Error("Server", "Cannot unregister null periodic callback.");
				return;
			}

			if (periodicCallbacks.Remove(callback))
			{
				Log.Debug("Server", $"Unregistered periodic callback: {callback.Method.DeclaringType?.Name}.{callback.Method.Name}");
			}
			else
			{
				Log.Warning("Server", $"Attempted to unregister non-existent periodic callback: {callback.Method.DeclaringType?.Name}.{callback.Method.Name}");
			}
		}

		/// <summary>
		/// Updates the interval for an existing periodic callback.
		/// </summary>
		/// <param name="callback">The callback whose interval to update.</param>
		/// <param name="newInterval">The new interval in seconds.</param>
		public void UpdateCallbackInterval(Action<float> callback, float newInterval)
		{
			if (callback == null)
			{
				Log.Error("Server", "Cannot update interval for null periodic callback.");
				return;
			}

			if (newInterval <= 0)
			{
				Log.Error("Server", $"Cannot update periodic callback with non-positive interval: {newInterval}");
				return;
			}

			if (periodicCallbacks.TryGetValue(callback, out var data))
			{
				data.Interval = newInterval;
				data.TimeRemaining = newInterval;
				Log.Debug("Server", $"Updated periodic callback interval: {callback.Method.DeclaringType?.Name}.{callback.Method.Name} (new interval: {newInterval}s)");
			}
			else
			{
				Log.Warning("Server", $"Attempted to update interval for non-existent periodic callback: {callback.Method.DeclaringType?.Name}.{callback.Method.Name}");
			}
		}

		#endregion
	}
}