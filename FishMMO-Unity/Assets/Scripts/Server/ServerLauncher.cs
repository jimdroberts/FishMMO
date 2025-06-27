using UnityEngine;
using System.Collections.Generic;
using FishMMO.Shared;

namespace FishMMO.Server
{
	public class ServerLauncher : BootstrapSystem
	{
		public string[] BootList = new string[]
		{
			"LoginServer",
			"WorldServer",
			"SceneServer",
		};

		public override void OnPreload()
		{
			// Load Template Cache
			AddressableLoadProcessor.OnAddressableLoaded += AddressableLoadProcessor_OnAddressableLoaded;
			AddressableLoadProcessor.OnAddressableUnloaded += AddressableLoadProcessor_OnAddressableUnloaded;
			AddressableLoadProcessor.EnqueueLoad(Constants.TemplateTypeCache);

			List<AddressableSceneLoadData> initialScenes = new List<AddressableSceneLoadData>();

#if !UNITY_EDITOR && !UNITY_EDITOR_LINUX
			string[] args = System.Environment.GetCommandLineArgs();
			if (args == null || args.Length < 2)
			{
#endif
				foreach (string serverName in BootList)
				{
					initialScenes.Add(new AddressableSceneLoadData(serverName));
				}
#if !UNITY_EDITOR
			}
			else
			{
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
						Close();
						break;
				}
			}
#endif
			AddressableLoadProcessor.EnqueueLoad(initialScenes);
		}

		private void Close()
		{
			Log.Debug("ServerLauncher: Unknown server type. Available servers {Login, World, Scene}");
			Server.Quit();
		}

		public override void OnDestroying()
		{
			AddressableLoadProcessor.OnAddressableLoaded -= AddressableLoadProcessor_OnAddressableLoaded;
			AddressableLoadProcessor.OnAddressableUnloaded -= AddressableLoadProcessor_OnAddressableUnloaded;
		}

		public void AddressableLoadProcessor_OnAddressableLoaded(Object addressable)
		{
			ICachedObject cachedObject = addressable as ICachedObject;
			if (cachedObject != null)
			{
				cachedObject.AddToCache(addressable.name);
			}
		}

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