using UnityEngine;
using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Logging;

namespace FishMMO.Server.Implementation
{
	/// <summary>
	/// Launches the server by preloading required scenes and handling addressable asset events.
	/// Supports command-line arguments for selecting server type.
	/// </summary>
	public class ServerLauncher : BootstrapSystem
	{
		/// <summary>
		/// List of default server scenes to boot if no command-line argument is provided.
		/// </summary>
		public string[] BootList = new string[]
		{
			"LoginServer",
			"WorldServer",
			"SceneServer",
		};

		/// <summary>
		/// Called before the main load process. Sets up addressable asset event handlers and determines which scenes to load based on command-line arguments or BootList.
		/// </summary>
		public override void OnPreload()
		{
			// Subscribe to addressable asset load/unload events.
			AddressableLoadProcessor.OnAddressableLoaded += AddressableLoadProcessor_OnAddressableLoaded;
			AddressableLoadProcessor.OnAddressableUnloaded += AddressableLoadProcessor_OnAddressableUnloaded;
			// Load the template type cache asset.
			AddressableLoadProcessor.EnqueueLoad(Constants.TemplateTypeCache);

			List<AddressableSceneLoadData> initialScenes = new List<AddressableSceneLoadData>();

#if !UNITY_EDITOR && !UNITY_EDITOR_LINUX
			// Get command-line arguments to determine which server to launch.
			string[] args = System.Environment.GetCommandLineArgs();
			if (args == null || args.Length < 2)
			{
#endif
			// No command-line argument: load all scenes in BootList.
			foreach (string serverName in BootList)
			{
				initialScenes.Add(new AddressableSceneLoadData(serverName));
			}
#if !UNITY_EDITOR
			}
			else
			{
				// Use the second argument to select which server scene to load.
				switch (args[1].ToUpper())
				{
					case "LOGIN":
						initialScenes.Add(new AddressableSceneLoadData("LoginServer"));
						break;
					case "WORLD":
						initialScenes.Add(new AddressableSceneLoadData("WorldServer"));
						break;
					case "SCENE":
						initialScenes.Add(new AddressableSceneLoadData("SceneServer"));
						break;
					default:
						// Unknown argument: close the server.
						Close();
						break;
				}
			}
#endif
			// Enqueue the selected scenes for loading.
			AddressableLoadProcessor.EnqueueLoad(initialScenes);
		}

		/// <summary>
		/// Closes the server if an unknown server type is provided via command-line argument.
		/// </summary>
		private void Close()
		{
			Log.Debug("ServerLauncher", "Unknown server type. Available servers {Login, World, Scene}");
			Server.Quit();
		}

		/// <summary>
		/// Called when the object is being destroyed. Unsubscribes from addressable asset events.
		/// </summary>
		public override void OnDestroying()
		{
			AddressableLoadProcessor.OnAddressableLoaded -= AddressableLoadProcessor_OnAddressableLoaded;
			AddressableLoadProcessor.OnAddressableUnloaded -= AddressableLoadProcessor_OnAddressableUnloaded;
		}

		/// <summary>
		/// Event handler called when an addressable asset is loaded. Adds the loaded object to the cache if it implements ICachedObject.
		/// </summary>
		/// <param name="addressable">The loaded addressable Unity object.</param>
		public void AddressableLoadProcessor_OnAddressableLoaded(Object addressable)
		{
			ICachedObject cachedObject = addressable as ICachedObject;
			if (cachedObject != null)
			{
				cachedObject.AddToCache(addressable.name);
			}
		}

		/// <summary>
		/// Event handler called when an addressable asset is unloaded. Removes the object from the cache if it implements ICachedObject.
		/// </summary>
		/// <param name="addressable">The unloaded addressable Unity object.</param>
		public void AddressableLoadProcessor_OnAddressableUnloaded(Object addressable)
		{
			ICachedObject cachedObject = addressable as ICachedObject;
			if (cachedObject != null)
			{
				cachedObject.RemoveFromCache();
			}
		}
	}
}