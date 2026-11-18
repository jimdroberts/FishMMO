#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;
using FishMMO.Shared.Atlas;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Shapes a scene's ground in memory exactly as a re-cut does (SceneGenerator.Recut's request, the biome registry
	/// loaded first) and reports the ground and the water at chosen points after every step of
	/// <see cref="SceneGround.Shape"/> (<see cref="SceneGround.Observer"/>): which step cut a hollow, raised a river, or laid
	/// one river below another. Saves nothing into the project.
	/// </summary>
	/// <remarks>
	/// FISHMMO_RECUT_SCENE: the atlas scene (default Flo Monolith). FISHMMO_AUDIT_OUT: where the report goes.
	/// FISHMMO_AUDIT_POINTS: "river:first-last:step;…" points of the scene's SAVED hydrology to report at (default
	/// "0:400-480:4;1:480-543:6"), and FISHMMO_AUDIT_AT: extra "x,z;x,z" positions.
	/// </remarks>
	public static class GroundShapeAudit
	{
		private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

		public static void Run()
		{
			string name = Environment.GetEnvironmentVariable("FISHMMO_RECUT_SCENE");
			if (string.IsNullOrEmpty(name))
			{
				name = "Flo Monolith";
			}
			string outDir = Environment.GetEnvironmentVariable("FISHMMO_AUDIT_OUT");
			if (string.IsNullOrEmpty(outDir))
			{
				outDir = Path.Combine(Path.GetTempPath(), "ground-shape-audit");
			}
			Directory.CreateDirectory(outDir);
			var report = new StringBuilder();
			try
			{
				Audit(name, report);
			}
			catch (Exception ex)
			{
				report.AppendLine("FAILED: " + ex);
			}
			finally
			{
				SceneGround.Observer = null;
			}
			File.WriteAllText(Path.Combine(outDir, "report.txt"), report.ToString());
			Debug.Log("[Ground shape audit] written to " + outDir);
		}

		private static void Audit(string name, StringBuilder report)
		{
			// As a cut: the biomes registered first (a batch run has none otherwise, and drains the planet differently).
			FishMMO.Shared.NameGeneration.Editor.NamingTemplateEditorLoader.EnsureLoaded();
			WorldAtlasScene entry = WorldEditorAssets.FindAll<WorldAtlasScene>().FirstOrDefault(e => e != null && e.SceneName == name);
			if (entry == null || entry.Body == null)
			{
				report.AppendLine($"no atlas scene '{name}'");
				return;
			}
			var request = new SceneGenerationRequest
			{
				SceneName = entry.SceneName,
				Body = entry.Body,
				Layer = entry.Layer,
				Latitude = entry.Latitude,
				Longitude = entry.Longitude,
				SizeKm = entry.SizeKm,
				HeadingDegrees = entry.HeadingDegrees,
				FineDetail = true,
				Erosion = true,
				ErosionStrength = entry.ErosionStrength,
				PointsOfInterest = entry.PointsOfInterest,
			};

			// The points: from the scene's saved hydrology, so every step is read where the saved cut has its problem.
			string hydrologyPath = SceneGenerator.HydrologyPath(SceneGenerator.TerrainFolder(entry.Body, name), name);
			var saved = AssetDatabase.LoadAssetAtPath<SceneHydrology>(hydrologyPath);
			var points = new List<(string label, float x, float z, float savedSurface)>();
			string spec = Environment.GetEnvironmentVariable("FISHMMO_AUDIT_POINTS");
			if (string.IsNullOrEmpty(spec))
			{
				spec = "0:400-480:4;1:480-543:6";
			}
			foreach (string part in spec.Split(';'))
			{
				string[] bits = part.Split(':');
				if (saved == null || bits.Length < 3 || !int.TryParse(bits[0], out int r) || r < 0 || r >= saved.Rivers.Count)
				{
					continue;
				}
				string[] range = bits[1].Split('-');
				int from = int.Parse(range[0], Invariant), to = int.Parse(range[range.Length - 1], Invariant), step = Math.Max(1, int.Parse(bits[2], Invariant));
				Vector3[] line = saved.Rivers[r].Points;
				for (int i = from; i <= to && i < line.Length; i += step)
				{
					points.Add(($"r{r}#{i}", line[i].x, line[i].z, line[i].y));
				}
			}
			string at = Environment.GetEnvironmentVariable("FISHMMO_AUDIT_AT");
			if (!string.IsNullOrEmpty(at))
			{
				foreach (string pair in at.Split(';'))
				{
					string[] xz = pair.Split(',');
					if (xz.Length == 2 && float.TryParse(xz[0], NumberStyles.Float, Invariant, out float px) && float.TryParse(xz[1], NumberStyles.Float, Invariant, out float pz))
					{
						points.Add(($"({px:0},{pz:0})", px, pz, float.NaN));
					}
				}
			}
			report.AppendLine($"scene {name}: {points.Count} point(s); saved hydrology {(saved != null ? saved.Rivers.Count + " rivers, " + saved.Lakes.Count + " lakes" : "missing")}");

			var steps = new List<(string step, float[] ground, string water)>();
			SceneGround.Observer = (step, field, water) =>
			{
				var ground = new float[points.Count];
				for (int k = 0; k < points.Count; k++)
				{
					ground[k] = field.MetresAt(points[k].x, points[k].z);
				}
				steps.Add((step, ground, Describe(water, points)));
			};
			SolarSystemProfile system = SolarSystemProfile.Resolve(entry.Body);
			TerrainTilePlan plan = SceneGeneration.PlanTiles(request.SizeKm);
			var notes = new List<string>();
			SceneGround.Shape(request, plan, system, notes, out _, out SceneWater final);
			SceneGround.Observer = null;
			report.AppendLine("notes: " + string.Join(" | ", notes));
			report.AppendLine();

			// The ground at each point after each step, beside the saved cut's water there.
			report.Append("point".PadRight(16)).Append("saved water".PadLeft(12));
			for (int s = 0; s < steps.Count; s++)
			{
				report.Append($"  [{s}]".PadLeft(9));
			}
			report.AppendLine();
			for (int k = 0; k < points.Count; k++)
			{
				report.Append(points[k].label.PadRight(16)).Append(float.IsNaN(points[k].savedSurface) ? "".PadLeft(12) : points[k].savedSurface.ToString("0.00", Invariant).PadLeft(12));
				foreach (var step in steps)
				{
					report.Append(step.ground[k].ToString("0.00", Invariant).PadLeft(9));
				}
				report.AppendLine();
			}
			report.AppendLine();
			for (int s = 0; s < steps.Count; s++)
			{
				report.AppendLine($"[{s}] {steps[s].step}");
				if (!string.IsNullOrEmpty(steps[s].water))
				{
					report.Append(steps[s].water);
				}
			}
		}

		/// <summary>The water at that step: its rivers and lakes, and at each point the nearest river's surface and bed.</summary>
		private static string Describe(SceneWater water, List<(string label, float x, float z, float savedSurface)> points)
		{
			if (water == null)
			{
				return null;
			}
			var text = new StringBuilder();
			text.AppendLine($"  {water.Rivers.Count} river(s), {water.Lakes.Count} lake(s): " + string.Join("; ", water.Rivers.Select(r =>
				$"river {r.Id} (planet {r.PlanetRiver}) {r.Count} pts {r.Start}->{r.End}{(r.JoinsRiver >= 0 ? " joins " + r.JoinsRiver : "")} surface {r.Surface.FirstOrDefault():0.0}..{r.Surface.LastOrDefault():0.0}")));
			foreach (SceneLake lake in water.Lakes)
			{
				text.AppendLine($"  lake {lake.Id} (planet {lake.PlanetLake}) at {lake.Level:0.00}");
			}
			text.AppendLine($"  nearest river at each point (within 20 m): surface / bed");
			foreach (var p in points)
			{
				float best = 400f;
				string found = "none";
				foreach (RiverPath river in water.Rivers)
				{
					for (int i = 0; i < river.Count; i++)
					{
						float dx = river.X[i] - p.x, dz = river.Z[i] - p.z, d2 = dx * dx + dz * dz;
						if (d2 < best)
						{
							best = d2;
							found = $"river {river.Id} #{i} {river.Surface[i]:0.00} / {river.Bed[i]:0.00} ({Mathf.Sqrt(d2):0.0} m off)";
						}
					}
				}
				text.AppendLine($"    {p.label,-14} {found}");
			}
			return text.ToString();
		}
	}
}
#endif
