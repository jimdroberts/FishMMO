using System.Collections;
using System.Collections.Generic;
using FishMMO.Shared;
using FishNet.Managing;
using FishNet.Object;
using FishNet.Transporting.Tugboat;
using KinematicCharacterController;
using UnityEngine;

namespace FishMMO.TestHarness
{
	/// <summary>
	/// The shared "a real FishNet server, in this process, with no database and no clients"
	/// bootstrap that every simulation scene stands on.
	/// </summary>
	/// <remarks>
	/// It reproduces exactly the three things production's <c>ServerLauncher</c>/<c>Server</c> do
	/// that spawned characters actually depend on — cache the addressable templates (IDs are
	/// assigned by <c>AddToCache</c> and are 0 without it), initialize KCC, start the server
	/// connection — and nothing else. Everything a real server does beyond that is database or
	/// scene-routing work no simulation needs.
	/// </remarks>
	public sealed class SimServer
	{
		public NetworkManager NetworkManager { get; private set; }

		/// <summary>
		/// Boots the server. Yields until it is started; check <see cref="NetworkManager"/> for
		/// null afterwards, which means the boot failed and said why.
		/// </summary>
		/// <param name="manifest">Supplies the NetworkManager prefab and the mock assets to cache.</param>
		/// <param name="parent">Transform to hang the network object off, for tidy cleanup.</param>
		/// <param name="port">Tugboat listen port. Nothing ever connects; it just has to be free.</param>
		public IEnumerator Boot(CombatSimManifest manifest, Transform parent, ushort port)
		{
			if (manifest == null || manifest.NetworkPrefab == null)
			{
				Debug.LogError("[SimServer] No manifest (or no network prefab in it) — run " +
					"FishMMO → Test Scenes → Generate Combat Sim first; every sim scene shares that manifest.");
				yield break;
			}

			// 1. Templates: the shipped labels, plus the mock content by hand (it is deliberately
			// not addressable-registered, so it never arrives through the labels).
			AddressableLoadProcessor.OnAddressableLoaded += OnAddressableLoaded;
			AddressableLoadProcessor.EnqueueLoad(new List<string>
			{
				"Server_Static_Permanent",
				Constants.SharedStaticLabel,
			});
			AddressableLoadBatch batch = AddressableLoadProcessor.BeginProcessQueue();
			yield return new WaitUntil(() => batch.IsComplete);
			foreach (ScriptableObject asset in manifest.CacheAssets)
			{
				if (asset is ICachedObject cached)
				{
					cached.AddToCache(asset.name);
				}
			}

			// 2. KCC, exactly as Server.cs does it.
			KinematicCharacterSystem.EnsureCreation();
			KinematicCharacterSystem.Settings.AutoSimulation = false;

			// 3. The server itself, from the generator-authored prefab (saved inactive so the
			// port can be set before Awake).
			GameObject netGo = Object.Instantiate(manifest.NetworkPrefab);
			netGo.name = "SimNetwork";
			netGo.transform.SetParent(parent, false);
			netGo.GetComponent<Tugboat>().SetPort(port);
			NetworkManager = netGo.GetComponent<NetworkManager>();
			netGo.SetActive(true);
			NetworkManager.ServerManager.StartConnection();
			yield return new WaitUntil(() => NetworkManager.ServerManager.Started);
		}

		public void Shutdown()
		{
			AddressableLoadProcessor.OnAddressableLoaded -= OnAddressableLoaded;
			if (NetworkManager != null)
			{
				if (NetworkManager.ServerManager != null)
				{
					NetworkManager.ServerManager.StopConnection(true);
				}
				/* Explicit, not left to hierarchy teardown: the manager prefab sets
				 * _dontDestroyOnLoad false, but if that ever regresses FishNet reparents the GO
				 * out of the harness hierarchy and a "destroyed" harness leaves a live manager
				 * behind — which FishNet then treats as the survivor and destroys the NEXT sim's
				 * manager as a duplicate. */
				Object.Destroy(NetworkManager.gameObject);
				NetworkManager = null;
			}
		}

		private static void OnAddressableLoaded(Object addressable)
		{
			if (addressable is ICachedObject cached)
			{
				cached.AddToCache(addressable.name);
			}
		}

		/// <summary>
		/// Instantiates a character prefab under an inactive parent (so the caller can add or
		/// configure components before <c>BaseCharacter.Awake</c> registers behaviours), then
		/// activates and network-spawns it server-owned, the way <c>ObjectSpawner</c> does.
		/// </summary>
		/// <param name="prefab">The character prefab.</param>
		/// <param name="position">Spawn position.</param>
		/// <param name="rotation">Spawn rotation.</param>
		/// <param name="scene">Scene to spawn into.</param>
		/// <param name="configure">Runs while the clone is still inactive.</param>
		/// <param name="afterActivate">Runs once active but before the network spawn — where
		/// anything that needs a live component but must precede <c>OnStartServer</c> goes
		/// (<c>AIController.Initialize</c>, which warps the NavMeshAgent, is the example).</param>
		public GameObject Spawn(GameObject prefab, Vector3 position, Quaternion rotation,
			UnityEngine.SceneManagement.Scene scene, System.Action<GameObject> configure = null,
			System.Action<GameObject> afterActivate = null)
		{
			GameObject staging = new GameObject("Staging");
			staging.SetActive(false);
			GameObject clone = Object.Instantiate(prefab, position, rotation, staging.transform);
			configure?.Invoke(clone);
			clone.transform.SetParent(null, true);
			Object.Destroy(staging);

			afterActivate?.Invoke(clone);

			NetworkObject nob = clone.GetComponent<NetworkObject>();
			NetworkManager.ServerManager.Spawn(nob, null, scene);
			return clone;
		}
	}
}
