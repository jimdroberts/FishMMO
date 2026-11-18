using System.Collections.Generic;
using FishMMO.Shared;
using FishMMO.Logging;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// Manages the client-side post-boot operations: starting the client, loading the template cache,
	/// and bringing the login scenes back when the player leaves the world.
	/// </summary>
	public class ClientPostbootSystem : BootstrapSystem
	{
		/// <summary>
		/// The client behaviour reference that must be initialized after the client bootstrap sequence finishes.
		/// </summary>
		public Client Client;

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
		/// Called during preload phase. Subscribes to addressable events and loads the template cache.
		/// </summary>
		public override void OnPreload()
		{
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
		/// Reloads the postload scenes, bringing the login screens back.
		/// </summary>
		/// <remarks>
		/// The camera is not restored here. It used to be — the pose was captured in
		/// <see cref="OnPreload"/> and written back to <c>Camera.main</c> at this point — and it
		/// never worked, because <c>KCCCamera</c> re-derives the camera's position and rotation from
		/// its own state every frame and undid the write. The camera now returns to its authored
		/// pose through <c>Client.QuitToLogin</c>, which releases the follow target as well; one
		/// owner of that pose, not two.
		/// </remarks>
		private void ReloadPostloadScenes()
		{
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