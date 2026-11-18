#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using UnityEngine;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Celestial;
using FishMMO.Shared.NameGeneration.Editor;
using Debug = UnityEngine.Debug;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Measures what the wind-driven moisture does to a world's climate and to the biomes chosen
	/// from it: the (temperature, humidity) distribution before and after, per-tier gaps, per-biome
	/// coverage, the biomes nothing selects, and what the moisture walk costs per point.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Measured, not reasoned about.</b> The humidity calibration (<see cref="MoistureModel.Span"/>,
	/// <see cref="MoistureModel.Midpoint"/>) and every claim about which biome envelope is reachable
	/// rest on numbers from the real field. This runs the real <see cref="PlanetClimateField"/> —
	/// temperature with the system's own latitude term, the real resolver, the registered biome
	/// templates — over evenly spaced points of every body with ground in the active system, plus an
	/// Earth-like reference (no body) so the numbers can be compared with an out-of-editor harness.
	/// </para>
	/// <para>
	/// Run headless with <c>runmethod.sh &lt;out&gt; FishMMO.Shared.WorldDesign.MoistureProbe.Run</c>,
	/// or through the EditMode test <c>MoistureModelTests.Probe_ReportsTheDistribution</c>. The
	/// report goes to the log and to <c>Temp/MoistureProbe.txt</c>.
	/// </para>
	/// </remarks>
	public static class MoistureProbe
	{
		/// <summary>Points per body for <see cref="Run()"/>.</summary>
		public const int DefaultSamples = 100000;

		/// <summary>Where the full report is written, relative to the project.</summary>
		public const string ReportPath = "Temp/MoistureProbe.txt";

		/// <summary>Probes every body with ground, at <see cref="DefaultSamples"/> points each.</summary>
		public static void Run()
		{
			Run(DefaultSamples);
		}

		/// <summary>Probes every body with ground at <paramref name="samples"/> points each, and returns the report.</summary>
		public static string Run(int samples)
		{
			NamingTemplateEditorLoader.EnsureLoaded();
			SolarSystemProfile system = SolarSystemProfile.Resolve();

			var report = new StringBuilder();
			report.AppendLine($"[Moisture probe] {samples} points per body; {BiomeRegistry.Selectable.Count} selectable biomes; system {(system != null ? system.name : "NONE")}");
			report.Append(ReportBody(null, null, samples, "Earth-like reference (no body)"));

			var bodies = new List<WorldBody>();
			if (system != null && system.HomeWorld != null)
			{
				bodies.Add(system.HomeWorld);
			}
			foreach (WorldBody body in WorldEditorAssets.FindAll<WorldBody>())
			{
				if (body != null && body.Kind != WorldBodyKind.GasGiant && !bodies.Contains(body))
				{
					bodies.Add(body);
				}
			}
			foreach (WorldBody body in bodies)
			{
				report.Append(ReportBody(system, body, samples, body.ResolvedName));
			}

			string text = report.ToString();
			try
			{
				File.WriteAllText(ReportPath, text);
			}
			catch (Exception e)
			{
				Debug.LogWarning($"[Moisture probe] could not write {ReportPath}: {e.Message}");
			}
			// One log entry per body keeps each under the console's truncation.
			foreach (string section in text.Split(new[] { "\n=== " }, StringSplitOptions.None))
			{
				Debug.Log(section.StartsWith("[Moisture probe]") ? section : "=== " + section);
			}
			return text;
		}

		/// <summary>The report for one body. Null body and system: the Earth-like reference.</summary>
		public static string ReportBody(SolarSystemProfile system, WorldBody body, int samples, string label)
		{
			PlanetClimateField field = PlanetClimateField.For(system, body);
			BiomeWorldConditions conditions = field.Conditions;
			var landT = new List<float>();
			var landBefore = new List<float>();
			var landAfter = new List<float>();
			var oceanAfter = new List<float>();
			var before = new Dictionary<string, int>();
			var after = new Dictionary<string, int>();
			var tiers = new int[9];
			var tierGap = new int[9];
			var histogram = new int[10, 10];
			var selectable = BiomeRegistry.Selectable;

			for (int i = 0; i < samples; i++)
			{
				Vector3 direction = PlanetSurface.FibonacciDirection(i, samples);
				PlanetSurface.LatLong(direction, out double latitude, out double longitude);
				PlanetSurfacePoint point = field.At(latitude, longitude);
				float t = point.Climate.Temperature;
				float h = point.Climate.Humidity;
				float hBefore = field.Parameters.HumidityAt(t, point.NormalizedHeight);
				int tier = point.Climate.ElevationTier;

				BiomeTemplate a = BiomeResolver.Select(point.NormalizedHeight, t, h, tier, conditions);
				BiomeTemplate b = BiomeResolver.Select(point.NormalizedHeight, t, hBefore, tier, conditions);
				Count(after, a);
				Count(before, b);
				tiers[Mathf.Clamp(tier, 0, 8)]++;
				bool inside = false;
				for (int k = 0; k < selectable.Count && !inside; k++)
				{
					BiomeTemplate candidate = selectable[k];
					inside = candidate.ElevationTier == tier && conditions.Allows(candidate) && candidate.ContainsClimate(t, h);
				}
				if (!inside)
				{
					tierGap[Mathf.Clamp(tier, 0, 8)]++;
				}

				if (point.UnderWater)
				{
					oceanAfter.Add(h);
				}
				else
				{
					landT.Add(t);
					landBefore.Add(hBefore);
					landAfter.Add(h);
					histogram[Mathf.Clamp((int)((t + 1f) * 5f), 0, 9), Mathf.Clamp((int)((h + 1f) * 5f), 0, 9)]++;
				}
			}

			var s = new StringBuilder();
			s.AppendLine();
			s.AppendLine($"=== {label}: atmosphere {conditions.Atmosphere}, water {conditions.Water:0.00}, liquid {conditions.HasLiquidWater}, " +
				$"surface liquid for moisture {field.Moisture.SurfaceLiquid}, belts {field.Moisture.Belts.CellDegrees:0.0}°, " +
				$"vapour scale height {field.Moisture.VapourScaleHeight:0} m, land {100.0 * landT.Count / samples:0.0}%");
			if (landT.Count > 0)
			{
				s.AppendLine($"  land T             {Quantiles(landT)}");
				s.AppendLine($"  land H before      {Quantiles(landBefore)}");
				s.AppendLine($"  land H after       {Quantiles(landAfter)}");
			}
			if (oceanAfter.Count > 0)
			{
				s.AppendLine($"  ocean H after      {Quantiles(oceanAfter)}");
			}
			s.AppendLine("  tiers (% of globe / % of tier outside every allowed envelope): " +
				string.Join("  ", Enumerable.Range(0, 9).Where(k => tiers[k] > 0).Select(k => $"{k}: {100.0 * tiers[k] / samples:0.0}/{100.0 * tierGap[k] / tiers[k]:0}")));
			if (landT.Count > 0)
			{
				s.AppendLine("  land T x H after (% of land), rows T +0.8 … -1.0, columns H -1.0 … +0.8 in 0.2 steps:");
				for (int ti = 9; ti >= 0; ti--)
				{
					s.Append($"    T{-1f + ti * 0.2f:+0.0;-0.0}");
					for (int hi = 0; hi < 10; hi++)
					{
						s.Append($"{100.0 * histogram[ti, hi] / landT.Count,6:0.0}");
					}
					s.AppendLine();
				}
			}
			s.AppendLine("  biome coverage, % of globe (before -> after):");
			foreach (string key in before.Keys.Union(after.Keys).OrderByDescending(k => after.TryGetValue(k, out int n) ? n : 0))
			{
				before.TryGetValue(key, out int nb);
				after.TryGetValue(key, out int na);
				s.AppendLine($"    {key,-26} {100.0 * nb / samples,6:0.00} -> {100.0 * na / samples,6:0.00}");
			}
			var never = selectable.Where(b => conditions.Allows(b) && !after.ContainsKey(b.ResolvedDisplayName)).Select(b => b.ResolvedDisplayName);
			s.AppendLine("  allowed on this world but never selected: " + string.Join(", ", never));
			s.AppendLine("  " + Timing(field, Math.Min(samples, 20000)));
			return s.ToString();
		}

		/// <summary>What the moisture term costs per point in this editor's runtime, against the rest of a point.</summary>
		private static string Timing(in PlanetClimateField field, int samples)
		{
			var directions = new Vector3[samples];
			var latitudes = new double[samples];
			var longitudes = new double[samples];
			for (int i = 0; i < samples; i++)
			{
				directions[i] = PlanetSurface.FibonacciDirection(i, samples);
				PlanetSurface.LatLong(directions[i], out latitudes[i], out longitudes[i]);
			}
			float sink = 0f;
			var watch = Stopwatch.StartNew();
			for (int i = 0; i < samples; i++)
			{
				sink += field.At(latitudes[i], longitudes[i]).Climate.Temperature;
			}
			double point = watch.Elapsed.TotalMilliseconds * 1e6 / samples;
			watch.Restart();
			for (int i = 0; i < samples; i++)
			{
				sink += field.Moisture.Anomaly(latitudes[i], directions[i]);
			}
			double moisture = watch.Elapsed.TotalMilliseconds * 1e6 / samples;
			return $"timing: a whole point (At, moisture included) {point:0} ns; the moisture walk alone {moisture:0} ns; " +
				$"a 2048 x 1024 bake spends {moisture * 2048 * 1024 / 1e9:0.0} s on moisture per point, about a third of that through MoistureRowTerrain (sink {sink:0})";
		}

		private static void Count(Dictionary<string, int> counts, BiomeTemplate biome)
		{
			string key = biome != null ? biome.ResolvedDisplayName : "(none)";
			counts[key] = counts.TryGetValue(key, out int n) ? n + 1 : 1;
		}

		private static string Quantiles(List<float> values)
		{
			values.Sort();
			float At(double p) => values[Math.Min(values.Count - 1, (int)(p * (values.Count - 1)))];
			return $"p05 {At(0.05):+0.00;-0.00}  p25 {At(0.25):+0.00;-0.00}  p50 {At(0.5):+0.00;-0.00}  p75 {At(0.75):+0.00;-0.00}  p95 {At(0.95):+0.00;-0.00}";
		}
	}
}
#endif
