#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Which change to the biomes a body's drainage answers to: the drainage worked out with each selectable biome left out
	/// of the selection in turn (its SelectionWeight zeroed in memory, then put back), each result's rivers, lakes and
	/// drained basins against the untouched one's, and the planet river ids round a scene. For finding what a session that
	/// drained the planet differently had in its registry. Saves nothing: every change is undone before it returns.
	/// FISHMMO_RECUT_SCENE (default Flo Monolith), FISHMMO_DRAIN_OUT.
	/// </summary>
	public static class DrainageSensitivityProbe
	{
		public static void Run()
		{
			string outDir = Environment.GetEnvironmentVariable("FISHMMO_DRAIN_OUT");
			if (string.IsNullOrEmpty(outDir))
			{
				outDir = Path.Combine(Path.GetTempPath(), "drainage-sensitivity");
			}
			Directory.CreateDirectory(outDir);
			var report = new StringBuilder();
			try
			{
				Sweep(report);
			}
			catch (Exception ex)
			{
				report.AppendLine("FAILED: " + ex);
			}
			File.WriteAllText(Path.Combine(outDir, "sensitivity.txt"), report.ToString());
		}

		private static void Sweep(StringBuilder report)
		{
			FishMMO.Shared.NameGeneration.Editor.NamingTemplateEditorLoader.EnsureLoaded();
			string name = Environment.GetEnvironmentVariable("FISHMMO_RECUT_SCENE");
			if (string.IsNullOrEmpty(name))
			{
				name = "Flo Monolith";
			}
			WorldAtlasScene entry = WorldEditorAssets.FindAll<WorldAtlasScene>().First(e => e != null && e.SceneName == name);
			var request = new SceneGenerationRequest
			{
				SceneName = entry.SceneName,
				Body = entry.Body,
				Layer = entry.Layer,
				Latitude = entry.Latitude,
				Longitude = entry.Longitude,
				SizeKm = entry.SizeKm,
				HeadingDegrees = entry.HeadingDegrees,
			};
			SolarSystemProfile system = SolarSystemProfile.Resolve(entry.Body);
			MethodInfo invalidate = typeof(BiomeRegistry).GetMethod("Invalidate", BindingFlags.NonPublic | BindingFlags.Static);

			string Measure()
			{
				PlanetDrainage.ClearCache();
				PlanetDrainage drainage = PlanetDrainage.For(request, system);
				return drainage == null ? "none" : $"{drainage.Result.Rivers.Count} rivers, {drainage.Result.Lakes.Count} lakes, {drainage.Result.DrainedBasins} drained";
			}

			string baseline = Measure();
			report.AppendLine($"baseline: {baseline}");
			List<BiomeTemplate> selectable = BiomeRegistry.Selectable.ToList();
			report.AppendLine($"{selectable.Count} selectable biomes");
			foreach (BiomeTemplate biome in selectable)
			{
				float weight = biome.SelectionWeight;
				biome.SelectionWeight = 0f;
				invalidate?.Invoke(null, null);
				string without;
				try
				{
					without = Measure();
				}
				finally
				{
					biome.SelectionWeight = weight;
					invalidate?.Invoke(null, null);
				}
				report.AppendLine($"without {biome.Key,-28} {without}{(without != baseline ? "   <- differs" : string.Empty)}");
			}
			report.AppendLine($"restored: {Measure()}");
		}
	}
}
#endif
