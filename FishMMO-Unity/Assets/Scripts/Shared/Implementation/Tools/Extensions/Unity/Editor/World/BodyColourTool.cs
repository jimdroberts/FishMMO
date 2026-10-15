#if UNITY_EDITOR
using System.Collections.Generic;
using System.Text;
using FishMMO.Shared.Celestial;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Gives the worlds of systems already generated the colours their physics says they have (<see cref="BodyColours"/>):
	/// what <see cref="SolarSystemGenerator"/> now does for a new system, for the systems generated before it did.
	/// </summary>
	public static class BodyColourTool
	{
		[DashboardTool(DashboardToolAttribute.Maintenance, "Recolour worlds from their physics", Section = "Content", Order = 4,
			Tooltip = "Sets every world's colour, sky haze colour and ring colour from its air, water and temperature, then re-bakes the planet surfaces. Logs each change.")]
		public static void RecolourFromDashboard()
		{
			Recolour(true);
		}

		/// <summary>Headless: <c>-executeMethod FishMMO.Shared.WorldDesign.BodyColourTool.RecolourFromCommandLine</c>.</summary>
		public static void RecolourFromCommandLine()
		{
			Recolour(true);
		}

		/// <summary>Recolours every world of every system; re-bakes the planet surfaces when <paramref name="bake"/>. Returns how many changed.</summary>
		public static int Recolour(bool bake)
		{
			var log = new StringBuilder();
			int changed = 0;
			var seen = new HashSet<WorldBody>();
			foreach (SolarSystemProfile system in WorldEditorAssets.FindAll<SolarSystemProfile>())
			{
				if (system == null)
				{
					continue;
				}
				foreach (CelestialBody body in system.Bodies)
				{
					if (!(body is WorldBody world) || !seen.Add(world))
					{
						continue;
					}
					Color before = world.Tint;
					BodyColours.Physics physics = BodyColours.PhysicsOf(system, world);
					BodyColours.Apply(system, world);
					EditorUtility.SetDirty(world);
					if (!Same(before, world.Tint))
					{
						changed++;
					}
					log.AppendLine($"  {world.ResolvedName} ({world.Kind}, {world.Atmosphere} air, water {world.Water:0.##}, {physics.Kelvin:0} K, {physics.Condensate}): " +
						$"#{ColorUtility.ToHtmlStringRGB(before)} → #{ColorUtility.ToHtmlStringRGB(world.Tint)}, haze #{ColorUtility.ToHtmlStringRGB(world.HazeColor)}");
				}
			}
			AssetDatabase.SaveAssets();
			Debug.Log($"[Body colours] {changed} of {seen.Count} world(s) recoloured from their physics:\n{log}");
			if (bake)
			{
				int baked = PlanetSurfaceBaker.BakeAll(out _);
				Debug.Log($"[Body colours] {baked} planet surface(s) re-baked.");
			}
			return changed;
		}

		private static bool Same(Color a, Color b)
			=> Mathf.Abs(a.r - b.r) < 0.004f && Mathf.Abs(a.g - b.g) < 0.004f && Mathf.Abs(a.b - b.b) < 0.004f;
	}
}
#endif
