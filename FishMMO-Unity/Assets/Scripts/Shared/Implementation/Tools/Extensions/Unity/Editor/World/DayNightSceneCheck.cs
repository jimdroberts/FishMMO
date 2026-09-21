#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Checks that every world scene is wired for the sky: a day/night cycle with its sun and moon
	/// lights, a place on the atlas, and no leftover skybox writer fighting the sky system.
	/// </summary>
	/// <remarks>
	/// The rebuilt <see cref="WorldDayNightCycle"/> drives the sun and moon from the world clock and
	/// the scene's place on the atlas. A scene that has the component but no lights, or no atlas
	/// entry, looks fine in the editor and then shows a sky that never moves, which is hard to
	/// notice in a running server. Each scene is opened on its own, read, and closed again; nothing
	/// is written.
	/// </remarks>
	public static class DayNightSceneCheck
	{
		[DashboardTool(DashboardToolAttribute.Validate, "Check world scene skies", Section = "World", Order = 30,
			Tooltip = "Opens every world scene and reports missing day/night wiring: no cycle, no sun or moon light, no atlas entry, or a scene still writing the skybox itself.")]
		public static void CheckFromDashboard()
		{
			if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
			{
				return;
			}
			List<string> problems = Check(out int scenes);
			if (problems.Count == 0)
			{
				Debug.Log($"[Sky check] All {scenes} world scene(s) are wired for the sky.");
				return;
			}
			Debug.LogWarning($"[Sky check] {problems.Count} problem(s) in {scenes} world scene(s):\n" + string.Join("\n", problems));
		}

		[DashboardTool(DashboardToolAttribute.Validate, "Wire world scene skies", Section = "World", Order = 31,
			Tooltip = "Gives every world scene's day/night cycle a sun and a moon light, creating them when the scene has none, and SAVES each scene it changes.",
			Confirm = "Wire every world scene's day/night cycle to a sun and moon light? Scenes that change are saved. The open scenes are closed first (you are asked to save them).")]
		public static void WireFromDashboard()
		{
			if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
			{
				return;
			}
			var notes = new List<string>();
			HashSet<string> paths = DirectoryExtensions.GetAllFiles(Constants.Configuration.WorldScenePath, ".unity");
			SceneSetup[] setup = System.Array.FindAll(EditorSceneManager.GetSceneManagerSetup(), s => !string.IsNullOrEmpty(s.path));
			int changed = 0;
			try
			{
				foreach (string path in paths)
				{
					Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
					if (!scene.IsValid() || !Wire(scene, notes))
					{
						continue;
					}
					EditorSceneManager.SaveScene(scene);
					changed++;
				}
			}
			finally
			{
				if (setup.Length > 0)
				{
					EditorSceneManager.RestoreSceneManagerSetup(setup);
				}
			}
			Debug.Log($"[Sky wiring] {changed} scene(s) saved.\n" + (notes.Count > 0 ? string.Join("\n", notes) : "Nothing to do."));
		}

		/// <summary>
		/// Gives one scene's cycle a sun and a moon. The sun is the scene's own directional light
		/// when it has one — often a child of the object the old cycle rotated — and a new one
		/// otherwise. The moon is always made if it is missing, because no scene had one before.
		/// </summary>
		private static bool Wire(Scene scene, List<string> notes)
		{
			string name = scene.name;
			WorldDayNightCycle cycle = null;
			var lights = new List<Light>();
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				if (cycle == null)
				{
					cycle = root.GetComponentInChildren<WorldDayNightCycle>(true);
				}
				lights.AddRange(root.GetComponentsInChildren<Light>(true));
			}
			if (cycle == null)
			{
				notes.Add($"{name}: no day/night cycle, so nothing to wire. Add the component if this scene should have a sky.");
				return false;
			}

			// Nothing to wire any more, and nothing is made. The sky creates its sun and its moon when
			// the scene runs and drives them from the solar system; the cycle no longer takes lights.
			// What is left to find is the opposite fault: a sun or a moon still sitting in the scene
			// from when it did — the old SunAndMoon prefab, or the pair this tool used to create —
			// which now lights the scene a second time, from wherever it was left pointing. Those are
			// switched off, not deleted: a scene is somebody's work, and off can be put back.
			bool changed = false;
			foreach (Light light in lights)
			{
				if (light == null || light.type != LightType.Directional || !light.enabled || !IsLegacySkyLight(light))
				{
					continue;
				}
				Undo.RecordObject(light, "Switch off legacy sky light");
				light.enabled = false;
				EditorUtility.SetDirty(light);
				notes.Add($"{name}: '{light.name}' was a sun or moon light from before the sky made its own. Switched off; delete it when you are happy with the scene.");
				changed = true;
			}
			if (changed)
			{
				EditorUtility.SetDirty(cycle);
			}
			return changed;
		}

		/// <summary>
		/// A directional light that was standing in for the sun or the moon: named for one, which is
		/// how the old cycle found them and what this tool and the SunAndMoon prefab both called them.
		/// </summary>
		/// <remarks>
		/// By name and nothing cleverer, on purpose. A scene may well have a directional light that is
		/// meant — a fill in a dungeon, a rim on a set piece — and those are not the sky's business.
		/// Anything not called a sun or a moon is left alone and not reported.
		/// </remarks>
		private static bool IsLegacySkyLight(Light light)
		{
			string name = light.name;
			return name.IndexOf("sun", System.StringComparison.OrdinalIgnoreCase) >= 0
				|| name.IndexOf("moon", System.StringComparison.OrdinalIgnoreCase) >= 0;
		}

		/// <summary>Reads every world scene and returns what is missing. Opens and closes each scene.</summary>
		public static List<string> Check(out int sceneCount)
		{
			var problems = new List<string>();
			HashSet<string> paths = DirectoryExtensions.GetAllFiles(Constants.Configuration.WorldScenePath, ".unity");
			sceneCount = paths.Count;
			SolarSystemProfile system = WorldEditorAssets.FindFirst<SolarSystemProfile>();
			WorldAtlas atlas = WorldEditorAssets.FindFirst<WorldAtlas>();
			if (system == null)
			{
				problems.Add("There is no solar system: every scene falls back to a plain 6-hour clock. Create it on the Solar System page.");
			}
			if (atlas == null)
			{
				problems.Add("There is no world atlas: no scene has a latitude, so every sky is an equator sky.");
			}

			SceneSetup[] setup = System.Array.FindAll(EditorSceneManager.GetSceneManagerSetup(), s => !string.IsNullOrEmpty(s.path));
			try
			{
				foreach (string path in paths)
				{
					string name = Path.GetFileNameWithoutExtension(path);
					Scene scene = EditorSceneManager.OpenScene(path, OpenSceneMode.Single);
					if (!scene.IsValid())
					{
						continue;
					}
					CheckScene(scene, name, problems);
				}
			}
			finally
			{
				if (setup.Length > 0)
				{
					EditorSceneManager.RestoreSceneManagerSetup(setup);
				}
			}
			return problems;
		}

		private static void CheckScene(Scene scene, string name, List<string> problems)
		{
			WorldDayNightCycle cycle = null;
			var skyboxWriters = new List<string>();
			var legacyLights = new List<string>();
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				if (cycle == null)
				{
					cycle = root.GetComponentInChildren<WorldDayNightCycle>(true);
				}
				foreach (Light light in root.GetComponentsInChildren<Light>(true))
				{
					if (light.type == LightType.Directional && light.enabled && IsLegacySkyLight(light))
					{
						legacyLights.Add(light.gameObject.name);
					}
				}
				foreach (Skybox skybox in root.GetComponentsInChildren<Skybox>(true))
				{
					// A camera Skybox component overrides RenderSettings.skybox, which the sky owns.
					skyboxWriters.Add(skybox.gameObject.name);
				}
			}

			WorldAtlasScene entry = WorldAtlasScene.Find(name);
			if (entry == null)
			{
				problems.Add($"{name}: no atlas entry, so it has no latitude, longitude or body. Add it on the World Atlas page.");
			}
			else if (!entry.Placed)
			{
				problems.Add($"{name}: its atlas entry is not placed, so it sits at 0°, 0° on the home world.");
			}

			if (cycle == null)
			{
				problems.Add($"{name}: no WorldDayNightCycle, so nothing drives the sun, the moon or the day/night triggers.");
				return;
			}
			foreach (string leftover in legacyLights)
			{
				problems.Add($"{name}: '{leftover}' is a directional sun or moon light left in the scene. The sky makes and drives its own, so this one lights the scene a second time. Switch it off or delete it.");
			}
			foreach (string writer in skyboxWriters)
			{
				problems.Add($"{name}: '{writer}' has a Skybox component, which overrides the sky the SkySystem draws. Remove it.");
			}
		}
	}
}
#endif
