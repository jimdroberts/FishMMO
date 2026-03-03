using System;
using System.Reflection;
using UnityEngine;
using FishMMO.Logging;
using FishMMO.Server.Core;
using FishMMO.Server.Core.World.SceneServer;
using FishNet.Connection;

namespace FishMMO.Server.Implementation.World.SceneServer.Interactable
{
	/// <summary>
	/// ScriptableObject initializer for registering interactable handlers in the FishMMO server.
	/// Uses reflection-based auto-discovery: any class implementing IInteractableHandler with a
	/// [HandlesInteractable] attribute is automatically found and registered. Adding a new handler
	/// requires only creating the class — no modifications to this initializer.
	/// </summary>
	[CreateAssetMenu(fileName = "FishMMO Interactable Handler Initializer", menuName = "FishMMO/Interactables/FishMMO Interactable Handler Initializer", order = 1)]
	public class InteractableHandlerInitializer : ScriptableObject, IInteractableHandlerInitializer
	{
		/// <summary>
		/// Discovers and registers all interactable handlers with the InteractableSystem via reflection.
		/// Handlers must implement IInteractableHandler and be decorated with [HandlesInteractable].
		/// </summary>
		/// <param name="server">Server context passed to handler instances.</param>
		/// <param name="system">Target interactable system instance.</param>
		public void RegisterHandlers(IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour> server, InteractableSystem system)
		{
			if (system == null)
			{
				return;
			}

			Type handlerInterface = typeof(IInteractableHandler);
			Type serverType = typeof(IServer<INetworkManagerWrapper, NetworkConnection, IServerBehaviour>);

			foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
			{
				Type[] types;
				try
				{
					types = assembly.GetTypes();
				}
				catch (ReflectionTypeLoadException ex)
				{
					// Some assemblies may fail to load all types; use the ones that succeeded.
					types = ex.Types;
				}

				foreach (Type type in types)
				{
					if (type == null || type.IsAbstract || type.IsInterface)
					{
						continue;
					}

					if (!handlerInterface.IsAssignableFrom(type))
					{
						continue;
					}

					var attr = type.GetCustomAttribute<HandlesInteractableAttribute>();
					if (attr == null)
					{
						continue;
					}

					try
					{
						// All handlers accept the server instance as a constructor parameter.
						ConstructorInfo ctor = type.GetConstructor(new[] { serverType });
						if (ctor == null)
						{
							Log.Warning("InteractableHandlerInitializer",
								$"Handler {type.Name} has [HandlesInteractable] but no constructor accepting IServer. Skipping.");
							continue;
						}

						IInteractableHandler handler = (IInteractableHandler)ctor.Invoke(new object[] { server });
						system.RegisterInteractableHandler(attr.InteractableType, handler);
					}
					catch (Exception ex)
					{
						Log.Error("InteractableHandlerInitializer",
							$"Failed to create handler {type.Name} for {attr.InteractableType.Name}: {ex.Message}");
					}
				}
			}
		}
	}
}