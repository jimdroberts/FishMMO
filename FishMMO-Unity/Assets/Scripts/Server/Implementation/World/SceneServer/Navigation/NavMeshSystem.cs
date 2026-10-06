using FishMMO.Server.Core;
using FishNet.Managing.Scened;
using UnityEngine;

namespace FishMMO.Server.Implementation.World.SceneServer.Navigation
{
	/// <summary>
	/// Puts each world scene's NavMesh into the world while the scene is loaded on this scene server.
	/// </summary>
	/// <remarks>
	/// Scenes never carry their NavMesh data (see <see cref="SceneNavMeshCatalogue"/>); this is the only thing that
	/// adds it, on FishNet's server-side load and unload. Spawners start a scene one update after it loads, so its
	/// NavMesh is in place before the first NPC is warped onto it.
	/// </remarks>
	[CreateAssetMenu(fileName = "NavMeshSystem", menuName = "FishMMO/Server/Scene Server/NavMesh System", order = 1)]
	public class NavMeshSystem : ServerBehaviour
	{
		[Tooltip("Every world scene's NavMesh. Kept in step by the editor when a scene with a baked NavMesh surface is saved, and by the scene generator's bake.")]
		public SceneNavMeshCatalogue Catalogue;

		/// <summary>The scenes' NavMeshes currently in the world.</summary>
		public SceneNavMeshes NavMeshes { get; private set; }

		private SceneManager sceneManager;

		public override ServerComponentInitializationStatus InitializeOnce()
		{
			if (Server == null)
			{
				return ServerComponentInitializationStatus.FailedToFindServer;
			}
			FishNet.Managing.NetworkManager networkManager = Server.NetworkWrapper?.NetworkManager;
			if (networkManager == null)
			{
				return ServerComponentInitializationStatus.FailedToFindServerManager;
			}
			if (Catalogue == null)
			{
				FishMMO.Logging.Log.Warning("NavMeshSystem", "No NavMesh catalogue is assigned; NPCs will have no NavMesh in any world scene.");
			}
			NavMeshes = new SceneNavMeshes(Catalogue);
			sceneManager = networkManager.SceneManager;
			if (sceneManager != null)
			{
				sceneManager.OnLoadEnd += OnLoadEnd;
				sceneManager.OnUnloadEnd += OnUnloadEnd;
			}
			return ServerComponentInitializationStatus.Initialized;
		}

		public override void OnDeinitialize()
		{
			if (sceneManager != null)
			{
				sceneManager.OnLoadEnd -= OnLoadEnd;
				sceneManager.OnUnloadEnd -= OnUnloadEnd;
				sceneManager = null;
			}
			NavMeshes?.Clear();
			NavMeshes = null;
		}

		private void OnLoadEnd(SceneLoadEndEventArgs args)
		{
			if (!args.QueueData.AsServer || args.LoadedScenes == null || NavMeshes == null)
			{
				return;
			}
			for (int i = 0; i < args.LoadedScenes.Length; ++i)
			{
				NavMeshes.Add(args.LoadedScenes[i].name);
			}
		}

		private void OnUnloadEnd(SceneUnloadEndEventArgs args)
		{
			if (!args.QueueData.AsServer || args.UnloadedScenesV2 == null || NavMeshes == null)
			{
				return;
			}
			for (int i = 0; i < args.UnloadedScenesV2.Count; ++i)
			{
				NavMeshes.Remove(args.UnloadedScenesV2[i].Name);
			}
		}
	}
}
