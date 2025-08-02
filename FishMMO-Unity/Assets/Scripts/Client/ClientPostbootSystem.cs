using FishMMO.Shared;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// Manages the client-side post-boot operations, including camera state management and scene loading.
	/// </summary>
	public class ClientPostbootSystem : BootstrapSystem
	{
		/// <summary>
		/// Stores the initial position of the main camera for scene reloads.
		/// </summary>
		private Vector3 cameraInitialPosition;
		/// <summary>
		/// Stores the initial rotation of the main camera for scene reloads.
		/// </summary>
		private Quaternion cameraInitialRotation;

		/// <summary>
		/// Called during preload phase. Captures initial camera state and loads template cache.
		/// </summary>
		public override void OnPreload()
		{
			// Try to capture the initial camera position and rotation
			if (Camera.main != null)
			{
				cameraInitialPosition = Camera.main.transform.position;
				cameraInitialRotation = Camera.main.transform.rotation;
			}

			// Subscribe to addressable load/unload events and enqueue template cache load.
			AddressableLoadProcessor.OnAddressableLoaded += AddressableLoadProcessor_OnAddressableLoaded;
			AddressableLoadProcessor.OnAddressableUnloaded += AddressableLoadProcessor_OnAddressableUnloaded;
			AddressableLoadProcessor.EnqueueLoad(Constants.TemplateTypeCache);
		}

		/// <summary>
		/// Called when the system is being destroyed. Unsubscribes from addressable events.
		/// </summary>
		public override void OnDestroying()
		{
			AddressableLoadProcessor.OnAddressableLoaded -= AddressableLoadProcessor_OnAddressableLoaded;
			AddressableLoadProcessor.OnAddressableUnloaded -= AddressableLoadProcessor_OnAddressableUnloaded;
		}

		/// <summary>
		/// Handler for when an addressable asset is loaded. Adds it to the cache if possible.
		/// </summary>
		/// <param name="addressable">The loaded addressable Unity object.</param>
		public void AddressableLoadProcessor_OnAddressableLoaded(UnityEngine.Object addressable)
		{
			ICachedObject cachedObject = addressable as ICachedObject;
			if (cachedObject != null)
			{
				cachedObject.AddToCache(addressable.name);
			}
		}

		/// <summary>
		/// Handler for when an addressable asset is unloaded. Removes it from the cache if possible.
		/// </summary>
		/// <param name="addressable">The unloaded addressable Unity object.</param>
		public void AddressableLoadProcessor_OnAddressableUnloaded(UnityEngine.Object addressable)
		{
			ICachedObject cachedObject = addressable as ICachedObject;
			if (cachedObject != null)
			{
				cachedObject.RemoveFromCache();
			}
		}

		/// <summary>
		/// Sets up client event handlers for scene management.
		/// </summary>
		/// <param name="client">The client instance.</param>
		public void SetClient(Client client)
		{
			client.OnQuitToLogin += ReloadPostloadScenes;
			client.OnEnterGameWorld += UnloadPostloadScenes;
		}

		/// <summary>
		/// Removes client event handlers for scene management.
		/// </summary>
		/// <param name="client">The client instance.</param>
		public void UnsetClient(Client client)
		{
			client.OnQuitToLogin -= ReloadPostloadScenes;
			client.OnEnterGameWorld -= UnloadPostloadScenes;
		}

		/// <summary>
		/// Unloads postload scenes using the addressable load processor.
		/// </summary>
		private void UnloadPostloadScenes()
		{
			AddressableLoadProcessor.UnloadSceneByLabelAsync(PostloadScenes);
		}

		/// <summary>
		/// Reloads postload scenes and resets the camera to its initial state.
		/// </summary>
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