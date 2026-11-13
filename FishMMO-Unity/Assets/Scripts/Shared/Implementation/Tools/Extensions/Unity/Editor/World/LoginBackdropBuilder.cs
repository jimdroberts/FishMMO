using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using Unity.Mathematics;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Splines;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Builds the login screen's world: a small temperate woodland cut by the scene generator around a shrine, where the
	/// selected character stands, with the camera's login view and its character select fly-in laid out over it. Then
	/// boots it with the login UI and clears the old hand-made backdrop out of the login UI scene.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The scene is a world scene in every way the content build cares about (its ground, biome art, sky and weather are
	/// baked and shipped like any other's) and is flagged <see cref="WorldSceneSettings.ClientBackdrop"/>, which keeps it
	/// out of the world scene details cache, so no server hosts it and nothing can send a character there.
	/// </para>
	/// <para>
	/// The rig (the stand, the view and the fly-in) is placed once; a later build keeps it, so hand tuning in the scene
	/// view survives. <see cref="Replace"/> places it again from the shrine.
	/// </para>
	/// </remarks>
	public static class LoginBackdropBuilder
	{
		public const string SceneName = "LoginBackdrop";
		/// <summary>The world the backdrop is cut from: it stands beside this scene, on its body and layer.</summary>
		public const string NeighbourScene = "Moron 2";
		public const string WoodlandBiomePath = "Assets/Templates/Entity/Biomes/Woodland.asset";
		public const string LoginGuiScenePath = "Assets/Scenes/Client/ClientLoginGUI.unity";
		public const string PostbootScenePath = "Assets/Scenes/Client/ClientPostboot.unity";
		public const string RigName = "Login Backdrop";
		public static readonly Vector2 SizeKm = new Vector2(1.5f, 1.5f);

		/// <summary>What the login UI scene held of the old backdrop, removed once this one is in.</summary>
		private static readonly string[] OldBackdropRoots = { "CameraSpline", "WorldSceneSettings", "LoginTerrain", "SunAndMoon", "1" };

		/// <summary>Sites a character could stand at, best first.</summary>
		private static readonly POIType[] StandKinds = { POIType.Shrine, POIType.StoneCircle, POIType.Ruins, POIType.Statue };

		private const string ClientAssembly = "FishMMO.Client";

		[DashboardTool(DashboardToolAttribute.Maintenance, "Build login backdrop", Section = "Login", Order = 1,
			Tooltip = "Cuts the login screen's world if it is missing, places its character stand and fly-in if they are missing, bakes its terrain arrays and boots it with the login UI.")]
		public static void BuildFromDashboard() => Report(Build(false, false));

		[DashboardTool(DashboardToolAttribute.Maintenance, "Re-place login backdrop rig", Section = "Login", Order = 2,
			Confirm = "Places the character stand, login view and fly-in again from the shrine, replacing any hand tuning. Continue?",
			Tooltip = "Places the character stand, the login view and the fly-in again from the shrine.")]
		public static void Replace() => Report(Build(false, true));

		[DashboardTool(DashboardToolAttribute.Maintenance, "Re-cut login backdrop", Section = "Login", Order = 3,
			Confirm = "Cuts the login backdrop's terrain again and places its rig again. Continue?",
			Tooltip = "Cuts the login screen's world again from the generator, then places its rig.")]
		public static void Recut() => Report(Build(true, true));

		/// <summary>
		/// Headless entry: <c>-executeMethod FishMMO.Shared.WorldDesign.LoginBackdropBuilder.BuildBatch</c>, with
		/// <c>-recut</c> to cut the terrain again and <c>-rerig</c> to place the rig again. Exits 0 on success.
		/// </summary>
		public static void BuildBatch()
		{
			string[] args = Environment.GetCommandLineArgs();
			bool recut = args.Contains("-recut");
			bool rerig = recut || args.Contains("-rerig");
			List<string> notes = null;
			bool ok = false;
			try
			{
				notes = Build(recut, rerig);
				ok = !notes.Any(n => n.StartsWith("FAILED", StringComparison.Ordinal));
			}
			catch (Exception ex)
			{
				Debug.LogError($"[Login backdrop] {ex}");
			}
			if (notes != null)
			{
				Report(notes);
			}
			EditorApplication.Exit(ok ? 0 : 1);
		}

		private static void Report(List<string> notes)
		{
			Debug.Log("[Login backdrop]\n  " + string.Join("\n  ", notes));
		}

		/// <summary>Builds the backdrop; the notes say what was done, and a FAILED note why it stopped.</summary>
		public static List<string> Build(bool recut, bool rerig)
		{
			var notes = new List<string>();
			if (!Application.isBatchMode && !EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
			{
				notes.Add("FAILED: cancelled while saving open scenes.");
				return notes;
			}

			string scenePath = ExistingScenePath();
			if (scenePath == null || recut)
			{
				scenePath = Cut(scenePath != null, notes);
				if (scenePath == null)
				{
					return notes;
				}
				rerig = true;
			}
			else
			{
				notes.Add($"Kept the cut at '{scenePath}'.");
			}

			Scene scene = EditorSceneManager.OpenScene(scenePath, OpenSceneMode.Single);
			Dress(scene, rerig, notes);
			if (notes.Any(n => n.StartsWith("FAILED", StringComparison.Ordinal)))
			{
				return notes;
			}
			EditorSceneManager.SaveScene(scene);

			List<TerrainArrayBakeReport> baked = TerrainArrayBaker.BakeScene(scene, false);
			notes.Add($"Terrain arrays: {baked?.Count ?? 0} report(s).");
			EditorSceneManager.SaveScene(scene);

			BootWithLogin(notes);
			ClearOldBackdrop(notes);
			AssetDatabase.SaveAssets();
			return notes;
		}

		/// <summary>The backdrop scene's path when it has been cut, else null.</summary>
		public static string ExistingScenePath()
		{
			return WorldEditorAssets.WorldScenePaths().FirstOrDefault(p =>
				string.Equals(Path.GetFileNameWithoutExtension(p), SceneName, StringComparison.Ordinal));
		}

		// ── The cut ─────────────────────────────────────────────

		private static string Cut(bool replace, List<string> notes)
		{
			WorldAtlasScene neighbour = WorldAtlasScene.Find(NeighbourScene);
			if (neighbour == null || neighbour.Body == null)
			{
				notes.Add($"FAILED: no atlas entry for '{NeighbourScene}' to stand the backdrop beside.");
				return null;
			}
			var woodland = AssetDatabase.LoadAssetAtPath<BiomeTemplate>(WoodlandBiomePath);
			if (woodland == null)
			{
				notes.Add($"FAILED: no woodland biome at '{WoodlandBiomePath}'.");
				return null;
			}

			var request = new SceneGenerationRequest
			{
				SceneName = SceneName,
				Body = neighbour.Body,
				Layer = neighbour.Layer,
				Latitude = neighbour.Latitude,
				Longitude = neighbour.Longitude,
				SizeKm = SizeKm,
				FineDetail = true,
				Erosion = true,
				Rivers = true,
				ReplaceExisting = replace,
				PointsOfInterest = SiteSettings(),
				// Every land biome becomes woodland: the backdrop is temperate wherever on the planet it is cut.
				BiomeOverrides = SceneBiomeOverrides.Of(new List<BiomeOverride> { new BiomeOverride { From = null, To = woodland } }),
			};
			SceneGenerationResult result = SceneGenerator.Generate(request);
			if (result == null || !result.Success)
			{
				notes.Add($"FAILED: the cut failed: {result?.Problem ?? "no result"}");
				return null;
			}
			notes.Add($"Cut '{result.ScenePath}' ({SizeKm.x} × {SizeKm.y} km) beside '{NeighbourScene}'. Biomes: {result.BiomeSummary}");
			return result.ScenePath;
		}

		/// <summary>A few sites, one of them a shrine for the character to stand at.</summary>
		private static PointOfInterestSettings SiteSettings()
		{
			string path = PointOfInterestSettings.PathBeside($"{WorldEditorAssets.ScenesFolder}/{SceneName}.asset", WorldEditorAssets.Sanitize(SceneName));
			var settings = AssetDatabase.LoadAssetAtPath<PointOfInterestSettings>(path);
			if (settings == null)
			{
				WorldEditorAssets.EnsureFolder(WorldEditorAssets.ScenesFolder);
				settings = ScriptableObject.CreateInstance<PointOfInterestSettings>();
				AssetDatabase.CreateAsset(settings, path);
			}
			settings.Density = PointOfInterestDensity.Sparse;
			settings.Overrides = new List<PointOfInterestKindOverride>
			{
				new PointOfInterestKindOverride { Kind = POIType.Shrine, Min = 1, Max = 1 },
				new PointOfInterestKindOverride { Kind = POIType.Ruins, Min = 0, Max = 1 },
				new PointOfInterestKindOverride { Kind = POIType.StoneCircle, Min = 0, Max = 1 },
			};
			EditorUtility.SetDirty(settings);
			AssetDatabase.SaveAssetIfDirty(settings);
			return settings;
		}

		// ── The scene ───────────────────────────────────────────

		private static void Dress(Scene scene, bool rerig, List<string> notes)
		{
			WorldSceneSettings settings = FindIn<WorldSceneSettings>(scene);
			if (settings == null)
			{
				notes.Add("FAILED: the cut has no WorldSceneSettings.");
				return;
			}
			settings.ClientBackdrop = true;
			EditorUtility.SetDirty(settings);

			StripGameplay(scene, notes);

			GameObject rig = scene.GetRootGameObjects().FirstOrDefault(g => g.name == RigName);
			if (rig != null && !rerig)
			{
				notes.Add("Kept the placed rig (Re-place to place it again).");
				return;
			}
			if (rig != null)
			{
				UnityEngine.Object.DestroyImmediate(rig);
			}
			PlaceRig(scene, notes);
		}

		/// <summary>
		/// Takes out what only makes sense in a hosted world: networked objects, spawners, gathering nodes, teleporters,
		/// and the test harness's walker and camera. The login screen is drawn by the boot scene's camera, and nothing
		/// here is ever spawned by a server.
		/// </summary>
		private static void StripGameplay(Scene scene, List<string> notes)
		{
			var doomed = new HashSet<GameObject>();
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				foreach (Component c in root.GetComponentsInChildren<Component>(true))
				{
					if (c == null)
					{
						continue;
					}
					Type t = c.GetType();
					string name = t.Name;
					string ns = t.Namespace ?? string.Empty;
					bool gameplay = c is FishNet.Object.NetworkObject
						|| name.Contains("Spawner") || name.Contains("Teleporter") || name == "GatheringNode"
						|| ns.StartsWith("FishMMO.TestHarness", StringComparison.Ordinal)
						|| (c is Camera cam && cam.CompareTag("MainCamera"))
						|| c is AudioListener;
					if (gameplay)
					{
						doomed.Add(c.gameObject);
					}
				}
			}
			int removed = 0;
			foreach (GameObject go in doomed)
			{
				if (go != null)
				{
					UnityEngine.Object.DestroyImmediate(go);
					removed++;
				}
			}
			notes.Add($"Stripped {removed} gameplay object(s) (networked, spawners, nodes, teleporters, harness).");
		}

		private static void PlaceRig(Scene scene, List<string> notes)
		{
			Physics.SyncTransforms();
			ScenePointOfInterest site = PickSite(scene);
			Vector3 centre = site != null ? site.transform.position : Vector3.zero;
			float radius = site != null ? Mathf.Max(3f, site.Radius) : 6f;
			notes.Add(site != null
				? $"Stand: {site.Kind} '{site.PointName}' at {centre} (radius {radius:0.#} m)."
				: "No shrine, stone circle, ruins or statue was sited; the stand is at the scene's centre.");

			// The way the character faces and the camera comes from: the best clear one of eight.
			float bestScore = float.MinValue;
			Vector3 bestDir = Vector3.forward, bestStand = centre;
			for (int i = 0; i < 8; i++)
			{
				float a = i * Mathf.PI * 0.25f;
				var dir = new Vector3(Mathf.Sin(a), 0f, Mathf.Cos(a));
				Vector3 stand = Ground(scene, centre + dir * Mathf.Clamp(radius * 0.35f, 2f, 6f));
				float score = Score(scene, stand, dir, centre);
				if (score > bestScore)
				{
					bestScore = score;
					bestDir = dir;
					bestStand = stand;
				}
			}

			var rig = new GameObject(RigName);
			SceneManager.MoveGameObjectToScene(rig, scene);

			var spot = new GameObject("Character Spot");
			spot.transform.SetParent(rig.transform, false);
			spot.transform.SetPositionAndRotation(bestStand, Quaternion.LookRotation(bestDir, Vector3.up));
			var head = new GameObject("Look At");
			head.transform.SetParent(spot.transform, false);
			head.transform.localPosition = new Vector3(0f, 1.45f, 0f);

			Vector3 close = Clear(scene, bestStand + bestDir * 4.2f + Vector3.up * 1.65f, 1.2f);
			Vector3 wide = Clear(scene, bestStand + bestDir * 70f + Quaternion.AngleAxis(25f, Vector3.up) * bestDir * 10f + Vector3.up * 24f, 14f);

			var view = new GameObject("Login View");
			view.transform.SetParent(rig.transform, false);
			// Over the stand, a little high: the login form sits in the middle of the screen.
			view.transform.SetPositionAndRotation(wide, Quaternion.LookRotation(head.transform.position + Vector3.up * 6f - wide, Vector3.up));

			var flyIn = new GameObject("Fly-in");
			flyIn.transform.SetParent(rig.transform, false);
			flyIn.transform.SetPositionAndRotation(Vector3.zero, Quaternion.identity);   // knots are in world space
			var container = flyIn.AddComponent<SplineContainer>();
			var spline = new Spline();
			Vector3 m1 = Clear(scene, Vector3.Lerp(wide, close, 0.45f) + Vector3.up * 6f, 8f);
			Vector3 m2 = Clear(scene, Vector3.Lerp(wide, close, 0.8f) + Vector3.up * 2f, 3f);
			foreach (Vector3 p in new[] { wide, m1, m2, close })
			{
				spline.Add(new BezierKnot((float3)p), TangentMode.AutoSmooth);
			}
			container.Spline = spline;

			Component cinematic = AddClient(flyIn, "FishMMO.Client.CinematicCamera", notes);
			Set(cinematic, "SplineContainer", container);
			Set(cinematic, "LookAtTarget", head.transform);

			Component preview = AddClient(spot, "FishMMO.Client.CharacterSelectPreview", notes);
			Set(preview, "Spot", spot.transform);

			Component backdrop = AddClient(rig, "FishMMO.Client.LoginBackdrop", notes);
			Set(backdrop, "LoginView", view.transform);
			Set(backdrop, "Cinematic", cinematic);

			EditorSceneManager.MarkSceneDirty(scene);
			notes.Add($"Rig: stand {bestStand}, facing {bestDir}, login view {wide}, fly-in {spline.GetLength():0} m over 4 knots.");
		}

		private static ScenePointOfInterest PickSite(Scene scene)
		{
			List<ScenePointOfInterest> sites = scene.GetRootGameObjects()
				.SelectMany(g => g.GetComponentsInChildren<ScenePointOfInterest>(true)).ToList();
			foreach (POIType kind in StandKinds)
			{
				ScenePointOfInterest hit = sites.Where(s => s.Kind == kind)
					.OrderBy(s => new Vector2(s.transform.position.x, s.transform.position.z).sqrMagnitude).FirstOrDefault();
				if (hit != null)
				{
					return hit;
				}
			}
			return null;
		}

		/// <summary>How good a stand is: the close camera and the wide view both see the head, on level ground.</summary>
		private static float Score(Scene scene, Vector3 stand, Vector3 dir, Vector3 centre)
		{
			Vector3 head = stand + Vector3.up * 1.45f;
			Vector3 close = stand + dir * 4.2f + Vector3.up * 1.65f;
			Vector3 wide = stand + dir * 70f + Vector3.up * 24f;
			float score = 0f;
			if (!Physics.Linecast(close, head))
			{
				score += 3f;
			}
			if (!Physics.Linecast(wide, head))
			{
				score += 2f;
			}
			// The camera's ground should not climb above the eye: facing downhill opens the view.
			float closeGround = Ground(scene, close).y;
			score -= Mathf.Max(0f, closeGround - stand.y) * 0.5f;
			float wideGround = Ground(scene, wide).y;
			score -= Mathf.Max(0f, wideGround - (stand.y + 10f)) * 0.1f;
			return score;
		}

		/// <summary>The top surface under <paramref name="at"/> (props, then the terrain).</summary>
		private static Vector3 Ground(Scene scene, Vector3 at)
		{
			var top = new Vector3(at.x, at.y + 300f, at.z);
			if (Physics.Raycast(top, Vector3.down, out RaycastHit hit, 1000f, ~0, QueryTriggerInteraction.Ignore))
			{
				return hit.point;
			}
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				foreach (Terrain t in root.GetComponentsInChildren<Terrain>(true))
				{
					Vector3 o = t.transform.position, s = t.terrainData.size;
					if (at.x >= o.x && at.x <= o.x + s.x && at.z >= o.z && at.z <= o.z + s.z)
					{
						return new Vector3(at.x, o.y + t.SampleHeight(at), at.z);
					}
				}
			}
			return at;
		}

		/// <summary><paramref name="at"/>, raised to at least <paramref name="clearance"/> over the ground.</summary>
		private static Vector3 Clear(Scene scene, Vector3 at, float clearance)
		{
			float floor = Ground(scene, at).y + clearance;
			return at.y < floor ? new Vector3(at.x, floor, at.z) : at;
		}

		private static Component AddClient(GameObject go, string typeName, List<string> notes)
		{
			Type type = Type.GetType($"{typeName}, {ClientAssembly}");
			if (type == null)
			{
				notes.Add($"FAILED: '{typeName}' is not in this editor (a server-subtarget editor cannot build the login backdrop).");
				return null;
			}
			return go.AddComponent(type);
		}

		private static void Set(Component target, string field, UnityEngine.Object value)
		{
			if (target == null)
			{
				return;
			}
			var so = new SerializedObject(target);
			SerializedProperty p = so.FindProperty(field);
			if (p == null)
			{
				throw new InvalidOperationException($"{target.GetType().Name} has no field '{field}'.");
			}
			p.objectReferenceValue = value;
			so.ApplyModifiedPropertiesWithoutUndo();
		}

		private static T FindIn<T>(Scene scene) where T : Component
		{
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				T found = root.GetComponentInChildren<T>(true);
				if (found != null)
				{
					return found;
				}
			}
			return null;
		}

		// ── The boot and the login UI ───────────────────────────

		/// <summary>Loads the backdrop with the login UI: after it in each of the boot's lists, so it is the active scene.</summary>
		private static void BootWithLogin(List<string> notes)
		{
			Scene boot = EditorSceneManager.OpenScene(PostbootScenePath, OpenSceneMode.Single);
			int added = 0;
			foreach (GameObject root in boot.GetRootGameObjects())
			{
				foreach (MonoBehaviour mb in root.GetComponentsInChildren<MonoBehaviour>(true))
				{
					if (mb == null)
					{
						continue;
					}
					var so = new SerializedObject(mb);
					foreach (string list in new[] { "EditorPostloadScenes", "PostloadScenes", "WebGLPostloadScenes" })
					{
						SerializedProperty scenes = so.FindProperty(list);
						if (scenes == null || !scenes.isArray || !Lists(scenes, "ClientLoginGUI") || Lists(scenes, SceneName))
						{
							continue;
						}
						int at = scenes.arraySize;
						scenes.InsertArrayElementAtIndex(at);
						SerializedProperty entry = scenes.GetArrayElementAtIndex(at);
						entry.FindPropertyRelative("SceneName").stringValue = SceneName;
						entry.FindPropertyRelative("ActivateOnLoad").boolValue = true;
						entry.FindPropertyRelative("LoadSceneMode").enumValueIndex = (int)LoadSceneMode.Additive;
						added++;
					}
					so.ApplyModifiedPropertiesWithoutUndo();
				}
			}
			if (added > 0)
			{
				EditorSceneManager.MarkSceneDirty(boot);
				EditorSceneManager.SaveScene(boot);
			}
			notes.Add(added > 0 ? $"Boot: added '{SceneName}' to {added} postload list(s)." : "Boot: already loads the backdrop.");
		}

		private static bool Lists(SerializedProperty scenes, string sceneName)
		{
			for (int i = 0; i < scenes.arraySize; i++)
			{
				SerializedProperty name = scenes.GetArrayElementAtIndex(i).FindPropertyRelative("SceneName");
				if (name != null && name.stringValue == sceneName)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>Removes the old hand-made backdrop (terrain, sun and moon, camera path, props) from the login UI scene.</summary>
		private static void ClearOldBackdrop(List<string> notes)
		{
			Scene gui = EditorSceneManager.OpenScene(LoginGuiScenePath, OpenSceneMode.Single);
			var removed = new List<string>();
			foreach (GameObject root in gui.GetRootGameObjects())
			{
				if (OldBackdropRoots.Contains(root.name))
				{
					removed.Add(root.name);
					UnityEngine.Object.DestroyImmediate(root);
				}
			}
			if (removed.Count > 0)
			{
				EditorSceneManager.MarkSceneDirty(gui);
				EditorSceneManager.SaveScene(gui);
			}
			notes.Add(removed.Count > 0 ? $"Login UI: removed {string.Join(", ", removed)}." : "Login UI: no old backdrop left.");
		}
	}
}
