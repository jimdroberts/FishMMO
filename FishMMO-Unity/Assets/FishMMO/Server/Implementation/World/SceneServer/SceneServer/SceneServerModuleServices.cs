using System;
using System.Collections.Generic;
using FishNet.Connection;
using UnityEngine.SceneManagement;
using FishMMO.Server.Core.World.SceneServer;
using FishMMO.Shared;

namespace FishMMO.Server.Implementation.World.SceneServer
{
	/// <summary>
	/// A module's piece of the scene server that lives as long as it does: the
	/// weather, hosted on the core's world clock. Started after the clock, ticked every frame after it, stopped before it.
	/// </summary>
	public interface ISceneServerModuleService
	{
		/// <summary>Starts the service.</summary>
		void Initialize(SceneServerSystem system, ICharacterSystem<NetworkConnection, Scene> characterSystem, ICharacterMappingData<NetworkConnection> characterMapping);

		/// <summary>Called every frame.</summary>
		void Tick(float deltaTime);

		/// <summary>Stops the service.</summary>
		void Shutdown();
	}

	/// <summary>The moduleServices modules add to the scene server (<see cref="ISceneServerModuleService"/>), by id, in registration order.</summary>
	public static class SceneServerModuleServices
	{
		private static readonly List<KeyValuePair<string, Func<ISceneServerModuleService>>> factories = new List<KeyValuePair<string, Func<ISceneServerModuleService>>>();

		/// <summary>Registers a service; registering an id again replaces it.</summary>
		public static void Register(string id, Func<ISceneServerModuleService> create)
		{
			if (string.IsNullOrEmpty(id) || create == null)
			{
				return;
			}
			for (int i = 0; i < factories.Count; i++)
			{
				if (factories[i].Key == id)
				{
					factories[i] = new KeyValuePair<string, Func<ISceneServerModuleService>>(id, create);
					return;
				}
			}
			factories.Add(new KeyValuePair<string, Func<ISceneServerModuleService>>(id, create));
		}

		/// <summary>One new instance of every registered service, not yet initialized.</summary>
		public static List<ISceneServerModuleService> CreateAll()
		{
			var moduleServices = new List<ISceneServerModuleService>(factories.Count);
			foreach (KeyValuePair<string, Func<ISceneServerModuleService>> entry in factories)
			{
				ISceneServerModuleService service = entry.Value();
				if (service != null)
				{
					moduleServices.Add(service);
				}
			}
			return moduleServices;
		}
	}

	public partial class SceneServerSystem
	{
		private List<ISceneServerModuleService> moduleServices;

		/// <summary>The first running module service of a type, or null (the staff tools find the weather here).</summary>
		public T Service<T>() where T : class, ISceneServerModuleService
		{
			if (moduleServices == null)
			{
				return null;
			}
			for (int i = 0; i < moduleServices.Count; i++)
			{
				if (moduleServices[i] is T found)
				{
					return found;
				}
			}
			return null;
		}

		private void InitializeModuleServices(ICharacterSystem<NetworkConnection, Scene> characterSystem, ICharacterMappingData<NetworkConnection> characterMapping)
		{
			moduleServices = SceneServerModuleServices.CreateAll();
			foreach (ISceneServerModuleService service in moduleServices)
			{
				service.Initialize(this, characterSystem, characterMapping);
			}
		}

		private void UpdateModuleServices(float deltaTime)
		{
			if (moduleServices == null)
			{
				return;
			}
			for (int i = 0; i < moduleServices.Count; i++)
			{
				moduleServices[i].Tick(deltaTime);
			}
		}

		private void DeinitializeModuleServices()
		{
			if (moduleServices == null)
			{
				return;
			}
			for (int i = moduleServices.Count - 1; i >= 0; i--)
			{
				moduleServices[i].Shutdown();
			}
			moduleServices = null;
		}
	}
}
