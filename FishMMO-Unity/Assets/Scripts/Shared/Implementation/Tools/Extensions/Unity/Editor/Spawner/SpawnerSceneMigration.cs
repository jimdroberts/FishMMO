using System.Collections.Generic;
using System.Text;
using FishMMO.Server.Implementation.World.SceneServer.Spawner;
using FishNet.Object;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FishMMO.Shared
{
	/// <summary>
	/// Turns scene-placed spawners into authoring-only objects: tagged <c>EditorOnly</c> and no
	/// longer networked.
	/// </summary>
	/// <remarks>
	/// The one-shot migration for the move of the spawner to the server assembly. A spawner used to
	/// be a <c>NetworkBehaviour</c> on its own scene <see cref="NetworkObject"/>; now it is baked into
	/// a server-only table, so the network object is dead weight that FishNet would still spawn for
	/// every client, and the untagged GameObject would ship its configuration to them.
	/// </remarks>
	public static class SpawnerSceneMigration
	{
		/// <summary>Log category.</summary>
		private const string LOG = "SpawnerSceneMigration";

		/// <summary>
		/// Migrates every world scene and bakes the spawn tables.
		/// </summary>
		[DashboardTool(DashboardToolAttribute.SpawnTables, "Migrate Scene Spawners", Section = "Migration", Order = 0, Tooltip = "Tags every spawner EditorOnly, removes NetworkObjects that existed only for spawners, saves the changed scenes and bakes the tables.", Confirm = "Migrate the spawners in every world scene? Changed scenes are saved.")]
		public static void MigrateAll()
		{
			StringBuilder report = new StringBuilder();
			int changedScenes = 0;

			foreach (string path in SpawnTableBaker.FindWorldScenePaths())
			{
				Scene scene = SceneManager.GetSceneByPath(path);
				bool opened = false;
				if (!scene.IsValid() || !scene.isLoaded)
				{
					scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Additive);
					opened = true;
				}

				try
				{
					List<string> changes = MigrateScene(scene);
					if (changes.Count > 0)
					{
						EditorSceneManager.MarkSceneDirty(scene);
						EditorSceneManager.SaveScene(scene);
						changedScenes++;
						report.AppendLine($"  {path}:");
						for (int i = 0; i < changes.Count; ++i)
						{
							report.AppendLine("    " + changes[i]);
						}
					}
				}
				finally
				{
					if (opened)
					{
						EditorSceneManager.CloseScene(scene, true);
					}
				}
			}

			List<string> problems = new List<string>();
			int baked = SpawnTableBaker.BakeAll(problems);

			Debug.Log($"[{LOG}] Migrated {changedScenes} scene(s); baked {baked} spawner(s).\n{report}");
			for (int i = 0; i < problems.Count; ++i)
			{
				Debug.LogWarning($"[{LOG}] {problems[i]}");
			}
		}

		/// <summary>
		/// Migrates the spawners of one open scene. Does not save it.
		/// </summary>
		/// <param name="scene">An open world scene.</param>
		/// <returns>One line per change made.</returns>
		public static List<string> MigrateScene(Scene scene)
		{
			List<string> changes = new List<string>();
			foreach (ObjectSpawner spawner in SpawnTableBaker.CollectSpawners(scene))
			{
				GameObject go = spawner.gameObject;

				if (!go.CompareTag(ObjectSpawner.EditorOnlyTag))
				{
					go.tag = ObjectSpawner.EditorOnlyTag;
					changes.Add($"{go.name}: tagged {ObjectSpawner.EditorOnlyTag}");
				}

				NetworkObject networkObject = go.GetComponent<NetworkObject>();
				if (networkObject == null)
				{
					continue;
				}

				/* Only a network object that exists for the spawner alone. Anything else networked on
				 * the same GameObject, or beneath it, is somebody else's and is reported instead. */
				bool otherNetworked = go.GetComponents<NetworkBehaviour>().Length > 0 ||
					go.GetComponentsInChildren<NetworkObject>(true).Length > 1;
				if (otherNetworked)
				{
					changes.Add($"{go.name}: KEPT its NetworkObject — other networked components share it; move the spawner to its own GameObject");
					continue;
				}

				Object.DestroyImmediate(networkObject, true);
				changes.Add($"{go.name}: removed its NetworkObject");
			}
			return changes;
		}
	}

	/// <summary>
	/// Removes spawner authoring components from every scene a player build processes.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Belt and braces beside the <c>EditorOnly</c> tag, for scenes built into the player itself.
	/// Play mode is left alone — the editor server reads the baked tables, and the authoring
	/// components are inert.
	/// </para>
	/// <para>
	/// <b>Not a guard for addressable scene bundles.</b> The scriptable build pipeline's scene
	/// dependency pass does not run scene processors (pinned by
	/// <c>SpawnTableBakeTests.TheDependencyCheck_WouldSeeSpawnerDataThatReachedTheBuild</c>), so
	/// world scenes rely on the tag alone — which is why <c>CustomBuildTool</c> refuses to build
	/// while any spawner lacks it.
	/// </para>
	/// </remarks>
	public sealed class SpawnerBuildStripper : IProcessSceneWithReport
	{
		public int callbackOrder => 0;

		public void OnProcessScene(Scene scene, BuildReport report)
		{
			if (report == null && Application.isPlaying)
			{
				return;
			}

			int stripped = 0;
			foreach (ObjectSpawner spawner in SpawnTableBaker.CollectSpawners(scene))
			{
				if (!spawner.CompareTag(ObjectSpawner.EditorOnlyTag))
				{
					Debug.LogWarning($"[SpawnerBuildStripper] {scene.name}/{spawner.gameObject.name} is not tagged {ObjectSpawner.EditorOnlyTag}; stripping its spawner component.");
				}
				Object.DestroyImmediate(spawner);
				stripped++;
			}

			if (stripped > 0)
			{
				Debug.Log($"[SpawnerBuildStripper] Stripped {stripped} spawner(s) from {scene.name}.");
			}
		}
	}
}
