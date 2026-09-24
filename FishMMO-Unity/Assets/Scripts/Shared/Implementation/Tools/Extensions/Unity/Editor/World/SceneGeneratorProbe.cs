#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Cuts a scene, checks what came out, and deletes it again.
	/// </summary>
	/// <remarks>
	/// A dashboard button rather than a unit test because generating a scene needs a real editor —
	/// new scenes, terrain data assets and an asset database — and because what it proves is that
	/// the whole chain works together, which is the part no pure test reaches. It tidies up after
	/// itself, so running it leaves the project as it was found.
	/// </remarks>
	public static class SceneGeneratorProbe
	{
		public const string ProbeSceneName = "Generator Probe Scene";

		[DashboardTool(DashboardToolAttribute.Validate, "Probe: cut a scene from a planet", Section = "World", Order = 20,
			Tooltip = "Generates a throwaway scene from the first world body, reports what it contains, and deletes it again.")]
		public static void Run()
		{
			WorldBody body = null;
			foreach (WorldBody candidate in WorldEditorAssets.FindAll<WorldBody>())
			{
				if (PlanetSurfaceBaker.HasGround(candidate))
				{
					body = candidate;
					break;
				}
			}
			if (body == null)
			{
				Debug.LogError("[Generator probe] No world body with ground to cut from.");
				return;
			}

			/* Reloaded after the tidy-up, not carried across it.
			 *
			 * Cleanup ends in AssetDatabase.Refresh(), and a ScriptableObject reference held across
			 * a refresh can come back as Unity's fake-null: not null to C#, null to every Unity
			 * operator. The symptom here was the generator refusing with "a scene has to stand on a
			 * celestial body" about a body that is plainly still on disk. */
			/* Paths, not the body, from here on.
			 *
			 * A UnityEngine.Object reference does not survive an AssetDatabase.Refresh(): it comes
			 * back fake-null, false to ReferenceEquals and null to every Unity operator. Both the
			 * tidy-up below and SceneGenerator.Generate itself end in a refresh, so a body captured
			 * once and used afterwards is null by the second use — silently, since WorldFolder just
			 * answers "Unplaced" and everything then deletes and verifies an empty folder that does
			 * not exist. A string cannot do that. */
			string bodyPath = AssetDatabase.GetAssetPath(body);
			string worldFolder = SceneGenerator.WorldFolder(body);
			string scenePath = $"{worldFolder}/{ProbeSceneName}.unity";
			string terrainFolder = SceneGenerator.TerrainFolder(body, ProbeSceneName);

			Cleanup(worldFolder, terrainFolder, scenePath);
			body = AssetDatabase.LoadAssetAtPath<WorldBody>(bodyPath);
			if (body == null)
			{
				Debug.LogError($"[Generator probe] '{bodyPath}' vanished during the tidy-up.");
				return;
			}

			var request = new SceneGenerationRequest
			{
				SceneName = ProbeSceneName,
				Body = body,
				Layer = null,
				Latitude = 18.0,
				Longitude = -46.0,
				SizeKm = new Vector2(2.4f, 2.4f),
				FineDetail = true,
			};

			SceneGenerationResult result = SceneGenerator.Generate(request);
			if (!result.Success)
			{
				Debug.LogError($"[Generator probe] Refused: {result.Problem}");
				return;
			}

			Debug.Log($"[Generator probe] {body.ResolvedName}: {result.Plan}");
			Debug.Log($"[Generator probe] relief {result.ReliefMetres:0} m, standing at {result.BaseAltitudeMetres:0} m above sea level");
			foreach (string wrote in result.Wrote)
			{
				Debug.Log($"[Generator probe] wrote {wrote} ({(File.Exists(wrote) ? "present" : "MISSING")})");
			}

			Inspect(result.ScenePath);

			string leftovers = Cleanup(worldFolder, terrainFolder, scenePath);
			Debug.Log(leftovers == null
				? "[Generator probe] Cleaned up."
				: $"[Generator probe] CLEANUP FAILED, remove by hand: {leftovers}");
		}

		/// <summary>Opens the generated scene and reports what is actually in it.</summary>
		private static void Inspect(string scenePath)
		{
			UnityEngine.SceneManagement.Scene scene =
				UnityEditor.SceneManagement.EditorSceneManager.OpenScene(scenePath, UnityEditor.SceneManagement.OpenSceneMode.Additive);
			try
			{
				SceneWorldFacts facts = SceneWorldSystems.Read(scene);
				Debug.Log($"[Generator probe] settings={facts.HasSettings} dayNight={facts.HasDayNightCycle} " +
					$"tiles={facts.TerrainTiles} landmass={facts.LandmassHeightMetres:0} m area={facts.DirectorAreaSquareKm:0.###} km2");

				int boundaries = 0, cameras = 0, layers = 0;
				bool simController = false;
				foreach (GameObject root in scene.GetRootGameObjects())
				{
					boundaries += root.GetComponentsInChildren<IBoundary>(true).Length;
					cameras += root.GetComponentsInChildren<Camera>(true).Length;
					foreach (Terrain terrain in root.GetComponentsInChildren<Terrain>(true))
					{
						if (terrain != null && terrain.terrainData != null)
						{
							layers = Mathf.Max(layers, terrain.terrainData.terrainLayers.Length);
						}
					}
					// By type name: the controller lives in the test harness, which this assembly
					// cannot reference, and a server-subtarget editor does not compile it at all.
					foreach (MonoBehaviour behaviour in root.GetComponentsInChildren<MonoBehaviour>(true))
					{
						simController |= behaviour != null && behaviour.GetType().Name == "WorldSimController";
					}
				}
				Debug.Log($"[Generator probe] boundaries={boundaries} cameras={cameras} simController={simController} terrainLayers={layers}");

				/* The climate the scene actually resolves, which is the question worth asking:
				 * WorldSceneSettings.climate is empty on a generated scene by design, and the
				 * answer is meant to come from the body's base climate through the atlas entry. */
				WorldSceneSettings settings = null;
				foreach (GameObject root in scene.GetRootGameObjects())
				{
					settings = root.GetComponentInChildren<WorldSceneSettings>(true);
					if (settings != null)
					{
						break;
					}
				}
				Debug.Log(settings == null
					? "[Generator probe] climate: no settings component to ask"
					: $"[Generator probe] climate resolves to: {(settings.Climate != null ? settings.Climate.name : "NOTHING - falls back to the derived model")}");
				if (boundaries == 0)
				{
					Debug.LogError("[Generator probe] No IBoundary: the details cache would refuse this scene outright.");
				}
			}
			finally
			{
				UnityEditor.SceneManagement.EditorSceneManager.CloseScene(scene, true);
			}
		}

		/// <summary>
		/// Removes everything a probe run leaves behind. Returns what survived, or null when the
		/// project is as it was found.
		/// </summary>
		/// <remarks>
		/// <para>
		/// The generated scene is closed first. <c>AssetDatabase.DeleteAsset</c> will not delete a
		/// scene that is open, and it says so by returning false rather than by throwing — so the
		/// first version of this reported "Cleaned up" while leaving a whole scene, four terrain
		/// assets and an atlas entry in the project. Anything that tidies up after itself has to
		/// check that it did.
		/// </para>
		/// <para>
		/// The scene is open because the generator makes it the only one when nothing titled was
		/// open, which is always the case in a batch editor.
		/// </para>
		/// </remarks>
		private static string Cleanup(string worldFolder, string terrainFolder, string scenePath)
		{
			UnityEditor.SceneManagement.EditorSceneManager.NewScene(
				UnityEditor.SceneManagement.NewSceneSetup.EmptyScene,
				UnityEditor.SceneManagement.NewSceneMode.Single);

			WorldAtlasScene entry = WorldAtlasScene.Find(ProbeSceneName);
			string entryPath = entry != null ? AssetDatabase.GetAssetPath(entry) : null;

			Remove(scenePath);
			Remove(terrainFolder);
			Remove(entryPath);

			// The world folder itself, but only if the probe left it empty.
			if (Directory.Exists(worldFolder)
				&& Directory.GetFileSystemEntries(worldFolder).Length == 0)
			{
				Remove(worldFolder);
			}

			AssetDatabase.Refresh();
			WorldAtlasScene.EditorLookup.Invalidate();

			// Asked of the file system, not of the return values. See Remove.
			var survivors = new System.Collections.Generic.List<string>();
			foreach (string path in new[] { scenePath, terrainFolder, entryPath })
			{
				if (!string.IsNullOrEmpty(path) && (File.Exists(path) || Directory.Exists(path)))
				{
					survivors.Add(path);
				}
			}
			return survivors.Count > 0 ? string.Join(", ", survivors) : null;
		}

		/// <summary>
		/// Deletes a file or folder, through the asset database and then for real.
		/// </summary>
		/// <remarks>
		/// <c>AssetDatabase.DeleteAsset</c> returns <c>true</c> and the thing is still on disk when
		/// the editor quits before the deletion flushes, which is what a <c>-executeMethod</c> run
		/// always does. The first version of this trusted the return value and reported a clean
		/// tidy-up over a scene, four terrain assets and an atlas entry that were all still there.
		/// <c>WorldMapBaker.CleanBakedMaps</c> already carried the same fallback for the same
		/// reason.
		/// </remarks>
		private static void Remove(string path)
		{
			if (string.IsNullOrEmpty(path))
			{
				return;
			}
			AssetDatabase.DeleteAsset(path);

			if (Directory.Exists(path))
			{
				Directory.Delete(path, true);
			}
			else if (File.Exists(path))
			{
				File.Delete(path);
			}
			string meta = path + ".meta";
			if (File.Exists(meta))
			{
				File.Delete(meta);
			}
		}
	}
}
#endif
