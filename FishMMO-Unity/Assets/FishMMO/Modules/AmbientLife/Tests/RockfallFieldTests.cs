using FishMMO.Shared.Biomes;
using FishMMO.Shared.Weather;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The rockfall's client draw mirrors the shared rockfall (split from RockfallTests, Phase 4, 2026-11-17).
	/// </summary>
	[TestFixture]
	public class RockfallFieldTests
	{
		private const string Shaders = "Assets/FishMMO/Modules/AmbientLife/Shaders";
		private const string World = "Assets/FishMMO/Modules/WorldDesign/Editor";

		private static string Read(string path) => ProjectFiles.ReadAllText(path);

		private static Dictionary<string, string> Defines(string text)
		{
			var d = new Dictionary<string, string>();
			foreach (Match m in Regex.Matches(text, @"#define\s+(ROCK_\w+|BOMB_\w+)\s+([^\s/]+)"))
			{
				d[m.Groups[1].Value] = m.Groups[2].Value;
			}
			return d;
		}

		private static float F(Dictionary<string, string> d, string k) => float.Parse(d[k], CultureInfo.InvariantCulture);
		private static uint X(Dictionary<string, string> d, string k) => Convert.ToUInt32(d[k].TrimEnd('u').Substring(2), 16);

		[Test]
		public void TheShader_MirrorsRockfall_ConstantForConstant()
		{
			string hlsl = Read($"{Shaders}/FishRockfall.hlsl");
			Dictionary<string, string> d = Defines(hlsl);
			Assert.That(F(d, "ROCK_STAGGER_SECONDS"), Is.EqualTo(Rockfall.StaggerSeconds));
			Assert.That(F(d, "ROCK_REST_SECONDS"), Is.EqualTo(Rockfall.RestSeconds));
			Assert.That(F(d, "ROCK_SINK_SECONDS"), Is.EqualTo(Rockfall.SinkSeconds));
			Assert.That(F(d, "ROCK_SCREE_LEAST"), Is.EqualTo(Rockfall.ScreeLeast));
			Assert.That(F(d, "ROCK_SCREE_MOST"), Is.EqualTo(Rockfall.ScreeMost));
			Assert.That(F(d, "ROCK_SCIARA_LEAST"), Is.EqualTo(Rockfall.SciaraLeast));
			Assert.That(F(d, "ROCK_SCIARA_MOST"), Is.EqualTo(Rockfall.SciaraMost));
			Assert.That(F(d, "ROCK_LEAST_PEAK"), Is.EqualTo(Rockfall.LeastPeak));
			Assert.That(F(d, "ROCK_MOST_PEAK"), Is.EqualTo(Rockfall.MostPeak));
			Assert.That(F(d, "ROCK_LEAST_ACCEL"), Is.EqualTo(Rockfall.LeastAccel));
			Assert.That(F(d, "ROCK_MOST_ACCEL"), Is.EqualTo(Rockfall.MostAccel));
			Assert.That(F(d, "ROCK_LEAST_DECEL"), Is.EqualTo(Rockfall.LeastDecel));
			Assert.That(F(d, "ROCK_MOST_DECEL"), Is.EqualTo(Rockfall.MostDecel));
			Assert.That(F(d, "ROCK_LEAST_STOP"), Is.EqualTo(Rockfall.LeastStop));
			Assert.That(F(d, "ROCK_MOST_STOP"), Is.EqualTo(Rockfall.MostStop));
			Assert.That(F(d, "ROCK_LEAST_HOP"), Is.EqualTo(Rockfall.LeastHop));
			Assert.That(F(d, "ROCK_MOST_HOP"), Is.EqualTo(Rockfall.MostHop));
			Assert.That(F(d, "ROCK_LEAST_HOP_HEIGHT"), Is.EqualTo(Rockfall.LeastHopHeight));
			Assert.That(F(d, "ROCK_MOST_HOP_HEIGHT"), Is.EqualTo(Rockfall.MostHopHeight));
			Assert.That(F(d, "ROCK_SCREE_ACROSS"), Is.EqualTo(Rockfall.ScreeAcross));
			Assert.That(F(d, "ROCK_SCIARA_ACROSS"), Is.EqualTo(Rockfall.SciaraAcross));
			Assert.That(F(d, "ROCK_LEAST_WOBBLE"), Is.EqualTo(Rockfall.LeastWobble));
			Assert.That(F(d, "ROCK_MOST_WOBBLE"), Is.EqualTo(Rockfall.MostWobble));
			Assert.That(X(d, "ROCK_SALT_SIZE"), Is.EqualTo(Rockfall.SizeSalt));
			Assert.That(X(d, "ROCK_SALT_DELAY"), Is.EqualTo(Rockfall.DelaySalt));
			Assert.That(X(d, "ROCK_SALT_ACROSS"), Is.EqualTo(Rockfall.AcrossSalt));
			Assert.That(X(d, "ROCK_SALT_PEAK"), Is.EqualTo(Rockfall.PeakSalt));
			Assert.That(X(d, "ROCK_SALT_ACCEL"), Is.EqualTo(Rockfall.AccelSalt));
			Assert.That(X(d, "ROCK_SALT_DECEL"), Is.EqualTo(Rockfall.DecelSalt));
			Assert.That(X(d, "ROCK_SALT_STOP"), Is.EqualTo(Rockfall.StopSalt));
			Assert.That(X(d, "ROCK_SALT_HOP"), Is.EqualTo(Rockfall.HopSalt));
			Assert.That(X(d, "ROCK_SALT_HOP_HEIGHT"), Is.EqualTo(Rockfall.HopHeightSalt));
			Assert.That(X(d, "ROCK_SALT_WOBBLE"), Is.EqualTo(Rockfall.WobbleSalt));
			Assert.That(X(d, "ROCK_SALT_PHASE"), Is.EqualTo(Rockfall.PhaseSalt));
			Assert.That(F(d, "BOMB_COOL_SECONDS"), Is.EqualTo(StromboliBombs.CoolSeconds));
			Assert.That(F(d, "BOMB_REST_SECONDS"), Is.EqualTo(StromboliBombs.RestSeconds));
			Assert.That(F(d, "BOMB_SINK_SECONDS"), Is.EqualTo(StromboliBombs.SinkSeconds));
			// The sizes the client fills.
			Assert.That(F(d, "ROCK_MAX_POINTS"), Is.EqualTo(FishMMO.Client.RockfallField.MaxPoints));
			Assert.That(F(d, "ROCK_MAX_CHUTES"), Is.EqualTo(FishMMO.Client.RockfallField.MaxChutes));
			Assert.That(F(d, "ROCK_MAX_FALLS"), Is.EqualTo(FishMMO.Client.RockfallField.MaxFalls));
			Assert.That(F(d, "ROCK_MAX_BOMBS"), Is.EqualTo(FishMMO.Client.RockfallField.MaxBombs));
			Assert.That(F(d, "ROCK_MAX_BURSTS"), Is.EqualTo(FishMMO.Client.RockfallField.MaxBursts));
			Assert.That(F(d, "ROCK_STONES_PER_FALL"), Is.EqualTo(FishMMO.Client.RockfallField.StonesPerFall));
			Assert.That(F(d, "ROCK_STONES_PER_FALL"), Is.EqualTo(Rockfall.MostStones));
			Assert.That(F(d, "ROCK_MAX_CONES"), Is.EqualTo(FishMMO.Client.RockfallField.MaxCones));
			Dictionary<string, string> marks = Defines(Read($"{Shaders}/FishRockfallMarks.shader"));
			Assert.That(F(marks, "ROCK_MAX_DECALS"), Is.EqualTo(FishMMO.Client.RockfallField.MaxDecals));
			Assert.That(F(marks, "ROCK_BREATH_PER_CONE"), Is.EqualTo(FishMMO.Client.RockfallField.BreathPerCone));
			// The hash and the shapes of the stone, the run and the hop, as the C# draws them.
			Assert.That(hlsl, Does.Contain("uint h = (v * 0x9E3779B1u) ^ seed;"));
			Assert.That(hlsl, Does.Contain("h *= 0x7FEB352Du;").And.Contain("h *= 0x846CA68Bu;"));
			Assert.That(hlsl, Does.Contain("return RockUnitOf(RockMix((i * 0x9E3779B1u) ^ salt, seed));"));
			Assert.That(hlsl, Does.Contain("0.6 * rank + 0.4 * RockU(seed, i, ROCK_SALT_STOP)"));
			Assert.That(hlsl, Does.Contain("RockU(seed, i, ROCK_SALT_WOBBLE ^ 0x77u)"));
			Assert.That(hlsl, Does.Contain("r.peak = sqrt(max(1e-6, 2.0 * r.stop * r.accel * r.decel / (r.accel + r.decel)));"));
			Assert.That(hlsl, Does.Contain("abs(sin(3.14159265 * along / max(0.1, s.hopLength)))"));
			// Every new pass compiles with DXC for Vulkan.
			foreach (string shader in new[] { "FishRockfallStones.shader", "FishRockfallMarks.shader" })
			{
				string text = Read($"{Shaders}/{shader}");
				Assert.That(Regex.Matches(text, "HLSLPROGRAM").Count, Is.EqualTo(Regex.Matches(text, @"#pragma use_dxc vulkan").Count), shader);
			}
		}

		// ── The plumbing and the defaults ──────────────────────────

		[Test]
		public void ThePlumbing_StoresReadsAndDraws()
		{
			string map = Read("Assets/FishMMO/Shared/Implementation/Tools/Biomes/SceneBiomeMap.cs");
			Assert.That(map, Does.Contain("public List<RockChute> Chutes").And.Contain("public List<TalusCone> TalusCones"));
			Assert.That(map, Does.Contain("SetRockChutes(null, null, null);"), "a new grid clears them");
			if (InstalledModules.Has("WorldDesign"))
			{
				string gen = Read($"{World}/SceneGenerator.cs");
				Assert.That(gen, Does.Contain("StoreRockChutes(map, SceneLandforms.Sites(scene), SceneLandforms.Pads(scene)"));
				string scatter = Read($"{World}/Terrain/TerrainScatter.cs");
				Assert.That(scatter, Does.Contain("if (rule.talusGrading && options?.LandformZones != null)"));
				Assert.That(scatter, Does.Contain("scatterRule.GradeZones.TalusGrade(scatterRule.ZoneBiome, worldX, worldZ)"));
				string authoring = Read($"{World}/ProceduralArt/BiomeArtAuthoring.cs");
				Assert.That(authoring, Does.Contain("talusGrading = s.Graded,").And.Contain("to.talusGrading = from.talusGrading;").And.Contain("text.Append(\"|T\");"));
			}
			string profile = Read("Assets/FishMMO/Modules/AmbientLife/Client/AmbientLife/AmbientLifeRenderProfile.cs");
			Assert.That(profile, Does.Contain("public Shader RockfallStonesShader;").And.Contain("public Shader RockfallMarksShader;"));
			string assets = Read("Assets/FishMMO/Modules/AmbientLife/Editor/AmbientLifeRenderAssets.cs");
			Assert.That(assets, Does.Contain("ambient.RockfallStonesShader = AssetDatabase.LoadAssetAtPath<Shader>(RockfallStonesShaderPath)"));
			Assert.That(assets, Does.Contain("ambient.RockfallMarksShader = AssetDatabase.LoadAssetAtPath<Shader>(RockfallMarksShaderPath)"));
			string field = Read("Assets/FishMMO/Modules/AmbientLife/Client/SiteLife/RockfallField.cs");
			Assert.That(field, Does.Contain("WorldMotion.Seconds").And.Contain("WorldMotion.Wrapped(WorldMotion.ShaderWrapSeconds)"));
			Assert.That(field, Does.Not.Contain("Time.time").And.Not.Contain("deltaTime"), "nothing on a private clock");
			Assert.That(field, Does.Contain("if (!chutes && !cones && !strombolian)"), "a scene with none draws nothing");
		}
	}
}
