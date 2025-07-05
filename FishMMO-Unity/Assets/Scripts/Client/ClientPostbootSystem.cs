using FishMMO.Shared;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Client
{
	public class ClientPostbootSystem : BootstrapSystem
	{
		public string UILoadingScreenKey = "UILoadingScreen";

		private Vector3 cameraInitialPosition;
		private Quaternion cameraInitialRotation;

		public override void OnPreload()
		{
			// Try to capture the initial camera position and rotation
			if (Camera.main != null)
			{
				cameraInitialPosition = Camera.main.transform.position;
				cameraInitialRotation = Camera.main.transform.rotation;
			}

			// Load Template Cache
			AddressableLoadProcessor.OnAddressableLoaded += AddressableLoadProcessor_OnAddressableLoaded;
			AddressableLoadProcessor.OnAddressableUnloaded += AddressableLoadProcessor_OnAddressableUnloaded;
			AddressableLoadProcessor.EnqueueLoad(Constants.TemplateTypeCache);
		}

		public override void OnDestroying()
		{
			AddressableLoadProcessor.OnAddressableLoaded -= AddressableLoadProcessor_OnAddressableLoaded;
			AddressableLoadProcessor.OnAddressableUnloaded -= AddressableLoadProcessor_OnAddressableUnloaded;
		}

		public void AddressableLoadProcessor_OnAddressableLoaded(UnityEngine.Object addressable)
		{
			ICachedObject cachedObject = addressable as ICachedObject;
			if (cachedObject != null)
			{
				cachedObject.AddToCache(addressable.name);
			}
		}

		public void AddressableLoadProcessor_OnAddressableUnloaded(UnityEngine.Object addressable)
		{
			ICachedObject cachedObject = addressable as ICachedObject;
			if (cachedObject != null)
			{
				cachedObject.RemoveFromCache();
			}
		}

		public void SetClient(Client client)
		{
			client.OnQuitToLogin += ReloadPostloadScenes;
			client.OnEnterGameWorld += UnloadPostloadScenes;
		}

		public void UnsetClient(Client client)
		{
			client.OnQuitToLogin -= ReloadPostloadScenes;
			client.OnEnterGameWorld -= UnloadPostloadScenes;
		}

		private void UnloadPostloadScenes()
		{
			AddressableLoadProcessor.UnloadSceneByLabelAsync(PostloadScenes);
		}

		private void ReloadPostloadScenes()
		{
			// Try to reset the initial camera position and rotation
			if (Camera.main != null)
			{
				Camera.main.transform.position = cameraInitialPosition;
				Camera.main.transform.rotation = cameraInitialRotation;
			}

			AddressableLoadProcessor.EnqueueLoad(PostloadScenes);
			try
			{
				AddressableLoadProcessor.BeginProcessQueue();
			}
			catch (UnityException ex)
			{
				Log.Error("ClientPostbootSystem", $"Failed to load preload scenes...", ex);
			}
		}
	}
}