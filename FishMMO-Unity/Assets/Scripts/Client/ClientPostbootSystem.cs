using System.Collections.Generic;
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
		/// The client behaviour reference that must be initialized after the client bootstrap sequence finishes.
		/// </summary>
		public Client Client;

		/// <summary>
		/// Stores the initial position of the main camera for scene reloads.
		/// </summary>
		private Vector3 cameraInitialPosition;
		/// <summary>
		/// Stores the initial rotation of the main camera for scene reloads.
		/// </summary>
		private Quaternion cameraInitialRotation;

		public override void OnCompleteProcessing()
		{
			base.OnCompleteProcessing();

			if (Client == null)
			{
				Log.Error("ClientPostbootSystem", $"Client script reference is missing...");
			}
			else
			{
				Client.Initialize();
			}
		}

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

			// Load static permanent addressables (e.g., templates) before loading scenes to ensure they're available in the cache.
			AddressableLoadProcessor.EnqueueLoad(new List<string>()
			{
				"Client_Static_Permanent",
				Constants.SharedStaticLabel,
			});
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
		public void AddressableLoadProcessor_OnAddressableLoaded(Object addressable)
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
		public void AddressableLoadProcessor_OnAddressableUnloaded(Object addressable)
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
				// Watch the batch. This runs on quit-to-login, and a silent failure here
				// leaves the player looking at an empty screen with the world torn down and
				// no login UI to go back to.
				AddressableLoadBatch batch = AddressableLoadProcessor.BeginProcessQueue();
				batch.Completed += (b) =>
				{
					if (b.HasFailures)
					{
						Log.Error("ClientPostbootSystem",
							$"Failed to reload login scene(s) after quitting to login: {string.Join(", ", b.FailedItems)}. The login UI will be missing.");
					}
				};
			}
			catch (UnityException ex)
			{
				Log.Error("ClientPostbootSystem", $"Failed to reload postload scenes...", ex);
			}
		}
	}
}