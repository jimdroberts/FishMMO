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

			bool changed = false;
			if (cycle.SunLight == null)
			{
				Light sun = Pick(lights, "sun");
				if (sun == null)
				{
					var go = new GameObject("Sun");
					SceneManager.MoveGameObjectToScene(go, scene);
					go.transform.rotation = Quaternion.Euler(50f, 30f, 0f);
					sun = go.AddComponent<Light>();
					sun.type = LightType.Directional;
					sun.shadows = LightShadows.Soft;
					sun.intensity = 1.1f;
					notes.Add($"{name}: no directional light, so a Sun was created.");
				}
				else
				{
					notes.Add($"{name}: the sun is '{sun.name}', the scene's own directional light.");
				}
				cycle.SunLight = sun;
				changed = true;
			}
			if (cycle.MoonLight == null)
			{
				Light moon = Pick(lights, "moon");
				if (moon == null || moon == cycle.SunLight)
				{
					var go = new GameObject("Moon");
					SceneManager.MoveGameObjectToScene(go, scene);
					go.transform.rotation = Quaternion.Euler(-40f, 200f, 0f);
					moon = go.AddComponent<Light>();
					moon.type = LightType.Directional;
					moon.shadows = LightShadows.None;
					moon.intensity = 0f;
					notes.Add($"{name}: a Moon light was created.");
				}
				cycle.MoonLight = moon;
				changed = true;
			}
			if (changed)
			{
				EditorUtility.SetDirty(cycle);
			}
			return changed;
		}

		/// <summary>A directional light whose name hints at the role, else any directional light.</summary>
		private static Light Pick(List<Light> lights, string hint)
		{
			Light any = null;
			foreach (Light light in lights)
			{
				if (light == null || light.type != LightType.Directional)
				{
					continue;
				}
				if (light.name.IndexOf(hint, System.StringComparison.OrdinalIgnoreCase) >= 0)
				{
					return light;
				}
				any = any != null ? any : light;
			}
			return string.Equals(hint, "moon", System.StringComparison.OrdinalIgnoreCase) ? null : any;
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
			foreach (GameObject root in scene.GetRootGameObjects())
			{
				if (cycle == null)
				{
					cycle = root.GetComponentInChildren<WorldDayNightCycle>(true);
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
			if (cycle.SunLight == null)
			{
				problems.Add($"{name}: the day/night cycle has no Sun Light; the sky cannot light the scene.");
			}
			else if (cycle.SunLight.type != LightType.Directional)
			{
				problems.Add($"{name}: the cycle's Sun Light is a {cycle.SunLight.type} light; it must be Directional.");
			}
			if (cycle.MoonLight == null)
			{
				problems.Add($"{name}: the cycle has no Moon Light, so nights have no moonlight and no moon shadows.");
			}
			foreach (string writer in skyboxWriters)
			{
				problems.Add($"{name}: '{writer}' has a Skybox component, which overrides the sky the SkySystem draws. Remove it.");
			}
		}
	}
}
#endif
