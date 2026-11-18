using FishMMO.Client;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.WorldDesign;
using FishMMO.Shared;
using NUnit.Framework;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// A sea hall's vents, plumes and brinicles as the Ambient Life module draws them (split from SeaHallTests, Phase 4, 2026-11-17).
	/// </summary>
	[TestFixture]
	public class SeaHallVentsTests
	{
		private const string ShaderPath = "Assets/FishMMO/Modules/AmbientLife/Shaders/FishSeaHallVents.shader";

		[Test]
		public void TheSeaHallForm_IsTheAmbientCaveForm()
		{
			Assert.That((int)SceneCaveForm.SeaHall, Is.EqualTo(6));
			Assert.That((int)AmbientCaveLife.FormOf(SceneCaveForm.SeaHall), Is.EqualTo((int)AmbientCaveForms.SeaHall));
		}

		/// <summary>
		/// The client reads a hall's spots back (as the World Design module's dressing stores them: one Hall, its sea as the widest
		/// Pool, a Smoker per vent with the Great flag on the great one, the Brine pools): its water, its smokers and their plumes,
		/// its sea's floor. Hand-made spots, so the client's half stands without the generator (Phase 4, 2026-11-18).
		/// </summary>
		[Test]
		public void TheClient_ReadsAHallsSpotsBack()
		{
			const int site = 77;
			const float water = -40f;
			var spots = new List<SceneCaveLifeSpot>
			{
				new SceneCaveLifeSpot { Kind = SceneCaveLifeKind.Hall, Form = SceneCaveForm.SeaHall, SiteId = site, Biome = 1234, Position = new Vector3(0f, -45f, 0f), Extent = new Vector3(40f, 20f, 30f) },
				new SceneCaveLifeSpot { Kind = SceneCaveLifeKind.Pool, Form = SceneCaveForm.SeaHall, SiteId = site, Biome = 1234, Position = new Vector3(0f, water, 0f), Extent = new Vector3(30f, 12f, 30f) },
				new SceneCaveLifeSpot { Kind = SceneCaveLifeKind.Pool, Form = SceneCaveForm.SeaHall, SiteId = site, Biome = 1234, Position = new Vector3(5f, -30f, 5f), Extent = new Vector3(3f, 1f, 3f) },
				new SceneCaveLifeSpot { Kind = SceneCaveLifeKind.Smoker, Form = SceneCaveForm.SeaHall, SiteId = site, Biome = 1234, Position = new Vector3(4f, -51f, 2f), Extent = new Vector3(1.5f, 4f, 30f) },
				new SceneCaveLifeSpot { Kind = SceneCaveLifeKind.Smoker, Form = SceneCaveForm.SeaHall, SiteId = site, Biome = 1234, Flags = SceneCaveLifeFlags.Great, Position = new Vector3(-6f, -38f, 1f), Extent = new Vector3(2.5f, 9f, 12f) },
				new SceneCaveLifeSpot { Kind = SceneCaveLifeKind.Brine, Form = SceneCaveForm.SeaHall, SiteId = site, Biome = 1234, Position = new Vector3(8f, -51.5f, -3f), Extent = new Vector3(2f, 0.3f, 2f) },
			};
			SceneCaveLifeSpot sea = spots[1];
			Assert.That(SeaLifeSystem.IsCaveSea(in sea), Is.True, "its sea is wide and deep enough for its own sea life");
			List<SeaHallVents.Hall> halls = SeaHallVents.HallsOf(spots);
			Assert.That(halls.Count, Is.EqualTo(1));
			Assert.That(halls[0].Water, Is.EqualTo(water).Within(1e-3f), "the widest pool is its sea");
			Assert.That(halls[0].Smokers.Count, Is.EqualTo(2));
			Assert.That(halls[0].Smokers.Count(s => s.Kind == SceneSeaHallVents.Great), Is.EqualTo(1));
			foreach (SeaHallVents.Smoker s in halls[0].Smokers)
			{
				float rise = SeaHallVents.RiseOf(in s, halls[0].Water);
				Assert.That(rise, Is.GreaterThan(0f));
				if (s.Orifice.y < halls[0].Water)
				{
					Assert.That(s.Orifice.y + rise, Is.LessThanOrEqualTo(halls[0].Water), "an underwater plume stops under the surface");
				}
			}
			Assert.That(SeaHallVents.HallsOf(spots.Where(p => p.Kind != SceneCaveLifeKind.Pool).ToList()), Is.Empty, "a hall with no sea is not drawn");
			// A cave sea's floor: water within its pool, none beyond.
			var ground = new SeaLifeSystem.CavePoolGround(sea.Position, sea.Extent.x, sea.Extent.y, null);
			Assert.That(ground.TryGround(sea.Position.x, sea.Position.z, out float bed) && Mathf.Abs(bed - (sea.Position.y - sea.Extent.y)) < 1e-3f, Is.True);
			Assert.That(ground.TryGround(sea.Position.x + 2f * sea.Extent.x, sea.Position.z, out float none) && float.IsPositiveInfinity(none), Is.True);
		}

		[Test]
		public void ThePlumes_AreTheTidesAndTheFlickers_PureAndInRange()
		{
			for (uint seed = 1; seed < 400; seed += 7)
			{
				for (float t = 0f; t < SceneSeaHallVents.Wrap; t += 97.3f)
				{
					float s = SceneSeaHallVents.Strength(0.65f, t, seed, false), g = SceneSeaHallVents.Strength(0.3f, t, seed, true);
					Assert.That(s, Is.InRange(0f, 1f));
					Assert.That(g, Is.InRange(SceneSeaHallVents.GreatFloor, 1f), "the Great Smoker never under 0.85");
					Assert.That(SceneSeaHallVents.Strength(0.65f, t, seed, false), Is.EqualTo(s), "pure");
				}
			}
			Assert.That(SceneSeaHallVents.Tide(0.3f), Is.EqualTo(0.75f).Within(1e-6f));
			Assert.That(SceneSeaHallVents.Tide(1f), Is.EqualTo(1f).Within(1e-6f));
			// Barely faltering: on over most of the time.
			double sum = 0;
			int n = 0;
			for (float t = 0f; t < SceneSeaHallVents.Wrap; t += 3.1f, n++)
			{
				sum += SceneSeaHallVents.Flicker(t, 12345u);
			}
			Assert.That(sum / n, Is.GreaterThan(0.75), "a smoker is on most of the time");
			// Continuous across the wrap and smooth (30 s ramps): never more than a ramp's step in a tenth of a second.
			foreach (uint seed in new uint[] { 3u, 99u, 4242u })
			{
				Assert.That(Mathf.Abs(SceneSeaHallVents.Flicker(SceneSeaHallVents.Wrap - 0.01f, seed) - SceneSeaHallVents.Flicker(0f, seed)), Is.LessThan(0.02f));
				for (float t = 0f; t < 4000f; t += 0.1f)
				{
					Assert.That(Mathf.Abs(SceneSeaHallVents.Flicker(t + 0.1f, seed) - SceneSeaHallVents.Flicker(t, seed)), Is.LessThan(0.02f));
				}
			}
			foreach (float slot in SceneSeaHallVents.FlickerSlots)
			{
				Assert.That(SceneSeaHallVents.Wrap % slot, Is.EqualTo(0f), $"{slot} divides the wrap");
			}
		}

		[Test]
		public void NothingScrubs_EveryPhaseRunsOnAWholePeriod_AndBriniclesGrowAndSnap()
		{
			foreach (float s in new[] { 7.3f, 33f, 120f, 412f, 9999f })
			{
				float p = SceneSeaHallVents.WholePeriod(s, SceneSeaHallVents.Wrap);
				Assert.That(SceneSeaHallVents.Wrap / p, Is.EqualTo(Mathf.Round(SceneSeaHallVents.Wrap / p)).Within(1e-3f));
			}
			foreach (float hours in SceneSeaHallVents.BriniclePeriodsHours)
			{
				Assert.That(SceneSeaHallVents.BrinicleWrap % (3600f * hours), Is.EqualTo(0f), $"{hours} h divides 36 h");
			}
			const uint seed = 777u;
			float longest = SceneSeaHallVents.BrinicleLongestOver(seed, 20f);
			Assert.That(longest, Is.InRange(SceneSeaHallVents.BrinicleShortest, SceneSeaHallVents.BrinicleLongest));
			Assert.That(SceneSeaHallVents.BrinicleLongestOver(seed, 1f), Is.LessThanOrEqualTo(0.5f), "never into the water");
			float period = SceneSeaHallVents.BriniclePeriod(seed);
			float last = SceneSeaHallVents.BrinicleLength(0f, seed, longest, out _);
			int snaps = 0;
			for (float t = 60f; t <= SceneSeaHallVents.BrinicleWrap; t += 60f)
			{
				float l = SceneSeaHallVents.BrinicleLength(t, seed, longest, out float since);
				Assert.That(l, Is.InRange(0f, longest));
				Assert.That(since, Is.InRange(0f, period));
				if (l < last)
				{
					snaps++;
				}
				else
				{
					Assert.That(l - last, Is.LessThan(longest * 60f / period + 1e-3f), "it grows steadily");
				}
				last = l;
			}
			Assert.That(snaps, Is.EqualTo(Mathf.RoundToInt(SceneSeaHallVents.BrinicleWrap / period)).Within(1), "one snap a period");
			Assert.That(SceneSeaHallVents.BrinicleSeconds(SceneSeaHallVents.BrinicleWrap * 3.0 + 12.5), Is.EqualTo(12.5f).Within(0.01f));
		}

		[Test]
		public void TheShader_MirrorsTheNumbers_AndCompilesTheWebGpuWay()
		{
			string shader = ProjectFiles.ReadAllText(ShaderPath);
			Assert.That(shader, Does.Contain("#pragma use_dxc vulkan"));
			Assert.That(shader, Does.Not.Contain("#include \"FishIndirectInstancing"));
			Assert.That(shader, Does.Contain("#define HALL_SMOKERS " + SceneSeaHallVents.MaxSmokers));
			Assert.That(shader, Does.Contain("#define HALL_WRAP 10000.0"));
			Assert.That(shader, Does.Contain("#define HALL_BRINICLE_WRAP 129600.0"));
			Assert.That(shader, Does.Contain("#define HALL_PER_GROVE " + SceneSeaHallVents.BriniclesPerGrove));
			Assert.That(shader, Does.Contain("k == 0u ? 6.0 : k == 1u ? 9.0 : k == 2u ? 12.0 : 18.0"), "the brinicle periods, a select chain");
			Assert.That(shader, Does.Contain("HallHash(seed, 3u, 1u)"), "the rise speed's hash");
			Assert.That(shader, Does.Contain("HallHash(seed, 32u, 0u)"), "the brinicle phase's hash");
			Assert.That(shader, Does.Not.Contain("[unroll]"), "no forced-unroll loop (FXC's WebGPU path)");
			string profile = ProjectFiles.ReadAllText("Assets/FishMMO/Modules/AmbientLife/Client/AmbientLife/AmbientLifeRenderProfile.cs");
			Assert.That(profile, Does.Contain("public Shader SeaHallVentsShader;"));
			string assets = ProjectFiles.ReadAllText("Assets/FishMMO/Modules/AmbientLife/Editor/AmbientLifeRenderAssets.cs");
			Assert.That(assets, Does.Contain("ambient.SeaHallVentsShader = AssetDatabase.LoadAssetAtPath<Shader>(SeaHallVentsShaderPath);"));
		}
	}
}
