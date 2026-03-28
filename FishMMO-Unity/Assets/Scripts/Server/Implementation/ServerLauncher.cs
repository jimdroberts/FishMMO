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
			AddressableLoadProcessor.OnAddressableLoaded -= AddressableLoadProcessor_OnAddressableLoaded;
			AddressableLoadProcessor.OnAddressableUnloaded -= AddressableLoadProcessor_OnAddressableUnloaded;

			// Subscribe to addressable asset load/unload events.
			AddressableLoadProcessor.OnAddressableLoaded += AddressableLoadProcessor_OnAddressableLoaded;
			AddressableLoadProcessor.OnAddressableUnloaded += AddressableLoadProcessor_OnAddressableUnloaded;

			// Load static permanent addressables (e.g., templates) before loading scenes to ensure they're available in the cache.
			AddressableLoadProcessor.EnqueueLoad(new List<string>()
			{
				"Server_Static_Permanent",
				Constants.SharedStaticLabel,
			});

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
				var serverTypeMap = new Dictionary<string, string>
				{
					{ "LOGIN", "LoginServer" },
					{ "WORLD", "WorldServer" },
					{ "SCENE", "SceneServer" },
				};
				string key = args[1].ToUpper();
				if (serverTypeMap.TryGetValue(key, out string sceneName))
				{
					initialScenes.Add(new AddressableSceneLoadData(sceneName));
				}
				else
				{
					// Unknown argument: close the server.
					Close();
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