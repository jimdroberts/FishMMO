using System;
using System.IO;
using System.Linq;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Phase I2n-c "drowned galleries" (2026-10-25, decision 86): the water table (caveFlood), the interior's water look, floats on
	/// cave water, caveHanging (authored from the dressing's own per-form lookups), glow-worm hue and mount, root windows, the tea
	/// ring (caveStain). Every default leaves every cave as it was.
	/// </summary>
	[TestFixture]
	public class BiomeCaveDrownedGalleriesTests
	{
		private const string World = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/";
		private const string Shaders = "Assets/Prefabs/Client/Weather/Shaders/";

		private static string Code(string path) => File.ReadAllText(path);

		private static readonly CaveForm[] Forms = (CaveForm[])Enum.GetValues(typeof(CaveForm));

		// ── Defaults ──────────────────────────────────────────────────

		[Test]
		public void EverySpecEntry_FloodsNothing_StainsNothing_AndHangsAsTheDressingDid()
		{
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				Assert.That(entry.CaveFloods(), Is.Empty, entry.Biome);
				Assert.That(entry.CaveStains(), Is.Empty, entry.Biome);
				Assert.That(entry.CaveHangings().Select(h => h.ToString()), Is.EqualTo(BiomeArtSpec.Entry.DefaultCaveHangings().Select(h => h.ToString())), entry.Biome);
				foreach (BiomeCaveGlow glow in entry.CaveGlows())
				{
					Assert.That(string.IsNullOrEmpty(glow.WormHue) && glow.WormMount == BiomeCaveWormMount.Roof, Is.True, $"{entry.Biome} {glow.Form}");
				}
			}
			// The authored hangings are the dressing's own numbers, form by form, no root art or windows.
			foreach (BiomeCaveHanging h in BiomeArtSpec.Entry.DefaultCaveHangings())
			{
				var form = (CaveForm)h.Form;
				CaveHangingPlan coded = CaveHanging.Coded(form);
				Assert.That(h.Silk, Is.EqualTo(coded.Silk), form.ToString());
				Assert.That(h.DeepRoots, Is.EqualTo(coded.DeepRoots), form.ToString());
				Assert.That(h.Guano, Is.EqualTo(coded.Guano), form.ToString());
				Assert.That(h.RootsNearMouth, Is.EqualTo(CaveFungi.CodedRootsNearMouth(form)), form.ToString());
				Assert.That(h.Roots == BiomeCaveRootArt.Hanging && h.RootWindows == 0, Is.True, form.ToString());
				Assert.That(h.ToString().Count(c => c == '|'), Is.EqualTo(3), "an as-before entry marks four numbers");
			}
			Assert.That(BiomeArtSpec.Entry.DefaultCaveHangings().Select(h => (CaveForm)h.Form), Is.EqualTo(Forms));
		}

		[Test]
		public void TheHangingAndFungusPlans_AreTheFormsOwn_WithNoTemplate()
		{
			var traits = new CaveBiomeTraits { Known = true, Vines = true, VineCaves = 0.6f, Trees = 0.4f, Greenery = true };
			foreach (CaveForm form in Forms)
			{
				CaveHangingPlan a = CaveHanging.For(null, form), b = CaveHanging.For(form);
				Assert.That(a.Silk == b.Silk && a.DeepRoots == b.DeepRoots && a.Guano == b.Guano, Is.True, form.ToString());
				BiomeCaveHanging entry = CaveHanging.Entry(null, form);
				Assert.That(entry.RootsNearMouth, Is.EqualTo(CaveFungi.CodedRootsNearMouth(form)), form.ToString());
				// The template's roots near a mouth replace the form's before the land is weighed: equal in, equal out.
				Assert.That(CaveFungi.For(in traits, form, entry.RootsNearMouth).RootsNearMouth, Is.EqualTo(CaveFungi.For(in traits, form).RootsNearMouth), form.ToString());
				Assert.That(CaveFungi.For(default(CaveBiomeTraits), form, entry.RootsNearMouth).RootsNearMouth, Is.EqualTo(CaveFungi.For(default(CaveBiomeTraits), form).RootsNearMouth), form.ToString());
			}
			// The glow entry marks as it did unless the worms' hue or mount is set.
			Assert.That(new BiomeCaveGlow(BiomeCaveForm.Cave, string.Empty, 1f, 1f).ToString(), Is.EqualTo("Cave=chance|1|1"));
			Assert.That(new BiomeCaveGlow(BiomeCaveForm.Cave, "Green", 0.25f, 1.8f, "Rootlight", BiomeCaveWormMount.Roots).ToString(), Is.EqualTo("Cave=Green|0.25|1.8|Rootlight|Roots"));
			Assert.That(new BiomeCaveFlood(BiomeCaveForm.Cave, 1.2f, 0.6f).ToString(), Is.EqualTo("Cave=1.2|0.6"));
			Assert.That(new BiomeCaveHanging(BiomeCaveForm.Cave, 0.4f, 2.2f, 1.6f, 0.4f, BiomeCaveRootArt.WaterRoots, 3).ToString(), Is.EqualTo("Cave=0.4|2.2|1.6|0.4|WaterRoots|3"));
		}

		// ── The pure rules ────────────────────────────────────────────

		[Test]
		public void TheWaterTableRules()
		{
			var cave = new BiomeCaveFlood(BiomeCaveForm.Cave, 1.2f, 0.6f);
			Assert.That(BiomeCaveWaters.DepthOf(in cave), Is.EqualTo(1.2f));
			Assert.That(BiomeCaveWaters.DepthOf(new BiomeCaveFlood(BiomeCaveForm.Cave, 5f, 1f)), Is.EqualTo(BiomeCaveWaters.MaxDepth), "the air gap is kept");
			Assert.That(BiomeCaveWaters.MaxDepth, Is.EqualTo(2.3f - 0.6f).Within(1e-6f));
			Assert.That(BiomeCaveWaters.Drowns(in cave, true, 0.59f), Is.True);
			Assert.That(BiomeCaveWaters.Drowns(in cave, true, 0.6f), Is.False, "the share is a share");
			Assert.That(BiomeCaveWaters.Drowns(in cave, false, 0f), Is.False, "dry ground drowns nothing");
			Assert.That(BiomeCaveWaters.WetHost(false, BiomeHabitat.Wetland | BiomeHabitat.Cave), Is.True);
			Assert.That(BiomeCaveWaters.WetHost(false, BiomeHabitat.Cave), Is.False);
			Assert.That(BiomeCaveWaters.WetHost(true, BiomeHabitat.None), Is.True);
			Assert.That(BiomeCaveWaters.LevelUnder(10f), Is.EqualTo(9.7f).Within(1e-5f));
			Assert.That(BiomeCaveWaters.Floods(BiomeCaveForm.Cave) && BiomeCaveWaters.Floods(BiomeCaveForm.Grotto), Is.True);
			Assert.That(new[] { BiomeCaveForm.SeaCave, BiomeCaveForm.IceCave, BiomeCaveForm.LavaTube, BiomeCaveForm.Mine }.Any(BiomeCaveWaters.Floods), Is.False);
			Assert.That(BiomeCaveWaters.TryFlood(null, BiomeCaveForm.Cave, out _), Is.False);
		}

		[Test]
		public void TheInteriorsLook_OnlyWhereItIsItsOwn()
		{
			BiomeWaterLook today = BiomeWaterLooks.Today;
			var tea = new BiomeWaterLook(new Color(0.29f, 0.18f, 0.07f, 1f), new Color(0.03f, 0.02f, 0.02f, 1f), new Vector3(0.7f, 1.5f, 2.9f), 0.05f,
				new Color(0.18f, 0.13f, 0.06f, 1f), new Color(0.88f, 0.8f, 0.56f, 1f));
			Assert.That(BiomeCaveWaters.LookBiome(true, 42, tea, today), Is.EqualTo(42));
			Assert.That(BiomeCaveWaters.LookBiome(true, 42, today, today), Is.EqualTo(0), "the material's own look: by the shores, as before");
			Assert.That(BiomeCaveWaters.LookBiome(false, 42, tea, today), Is.EqualTo(0), "an unlinked cave's water is the host's");
			Assert.That(BiomeCaveWaters.LookBiome(true, 42, default, today), Is.EqualTo(0), "an unauthored look");
		}

		[Test]
		public void TheRootWindowAndRingRules()
		{
			Assert.That(BiomeCaveWaters.RootWindowsWanted(0, 0.9f), Is.EqualTo(0));
			for (float roll = 0f; roll <= 1f; roll += 0.05f)
			{
				int n = BiomeCaveWaters.RootWindowsWanted(3, roll);
				Assert.That(n >= 1 && n <= 3, Is.True, roll.ToString());
			}
			Assert.That(BiomeCaveWaters.OpensRootWindows(0.5f, true), Is.True);
			Assert.That(BiomeCaveWaters.OpensRootWindows(0.1f, true) || BiomeCaveWaters.OpensRootWindows(0.9f, false), Is.False);
			Assert.That(BiomeCaveWaters.RootWindowMaxRadius, Is.LessThan(CaveSystem.SkylightMinRadius), "a root window is told from a skylight by its size");
			Assert.That(CaveSystem.IsRootWindow(new CaveMouth { Kind = CaveMouthKind.Skylight, Radius = 1.8f }), Is.True);
			Assert.That(CaveSystem.IsRootWindow(new CaveMouth { Kind = CaveMouthKind.Skylight, Radius = CaveSystem.SkylightMinRadius }), Is.False);
			Assert.That(CaveSystem.IsRootWindow(new CaveMouth { Kind = CaveMouthKind.Ground, Radius = 1.8f }), Is.False);
			// The ring: full under the water and up the band, a smooth edge at its top, nothing above.
			Assert.That(BiomeCaveWaters.RingAt(-1f, 1.2f, 0.05f), Is.EqualTo(1f));
			Assert.That(BiomeCaveWaters.RingAt(1.15f, 1.2f, 0.05f), Is.EqualTo(1f));
			Assert.That(BiomeCaveWaters.RingAt(1.175f, 1.2f, 0.05f), Is.EqualTo(0.5f).Within(1e-4f));
			Assert.That(BiomeCaveWaters.RingAt(1.21f, 1.2f, 0.05f), Is.EqualTo(0f));
			Assert.That(BiomeCaveWaters.TryStain(new[] { new BiomeCaveStain(BiomeCaveForm.Cave, new Color(0.35f, 0.23f, 0.1f, 0f), 1f, 0.05f) }, BiomeCaveForm.Cave, out _), Is.False, "no strength, no ring");
			Assert.That(BiomeCaveWaters.RoofShare(BiomeCaveWormMount.Roof), Is.EqualTo(1f));
			Assert.That(BiomeCaveWaters.RoofShare(BiomeCaveWormMount.Roots), Is.EqualTo(BiomeCaveWaters.RootMountRoofShare));
		}

		// ── The plan ──────────────────────────────────────────────────

		/// <summary>A foot at 70 m, a face rising 60 m over 30 m, a rolling plateau behind (CaveIceTests' escarpment).</summary>
		private static Func<float, float, float> Escarpment(float tiltDegrees, int variant)
		{
			float a = tiltDegrees * Mathf.Deg2Rad, c = Mathf.Cos(a), s = Mathf.Sin(a);
			return (x, z) =>
			{
				float u = c * z - s * x, w = s * z + c * x;
				float t = Mathf.Clamp01(u / 30f);
				float face = 10f + 60f * (t * t * (3f - 2f * t));
				float roll = 3f * Mathf.Sin(w * 0.05f + variant) + 2f * Mathf.Sin(u * 0.031f + 0.7f * variant) * Mathf.Clamp01(u / 40f);
				return 60f + face + roll * Mathf.Clamp01((u + 10f) / 30f) - 0.04f * Mathf.Max(0f, -u);
			};
		}

		[Test]
		public void ADrownedCave_IsLaidLevelAtItsWaterTable_AndNoFloodIsAsBefore()
		{
			int drowned = 0;
			for (int seed = 1; seed <= 8; seed++)
			{
				float tilt = seed * 13f % 40f - 20f;
				CaveGround ground = CaveGround.Sample(Escarpment(tilt, seed), -560f, -560f, 560f, 560f, 2f);
				float yaw = Mathf.Repeat(180f - tilt, 360f);
				CaveSystem dry = CaveSystem.Plan(CaveForm.Cave, 1, new Vector3(0f, 0f, -1f), yaw, seed * 7919, seed, ground, 0f, out _);
				CaveSystem same = CaveSystem.Plan(CaveForm.Cave, 1, new Vector3(0f, 0f, -1f), yaw, seed * 7919, seed, ground, 0f, out _,
					float.NaN, default, CaveSystem.LeastDepth, CaveSystem.MaxCaveDepth, float.NaN);
				Assert.That(same?.ToValues(), Is.EqualTo(dry?.ToValues()), $"seed {seed}: no flood plans as before");
				CaveSystem wet = CaveSystem.Plan(CaveForm.Cave, 1, new Vector3(0f, 0f, -1f), yaw, seed * 7919, seed, ground, 0f, out string problem,
					float.NaN, default, CaveSystem.LeastDepth, CaveSystem.MaxCaveDepth, 1.2f);
				if (wet == null)
				{
					continue;
				}
				CaveSystem stored = CaveSystem.FromValues(wet.ToValues(), wet.Origin, wet.Seed);
				Assert.That(stored.Drowned && stored.GradesApproach, Is.True, $"seed {seed}");
				Assert.That(stored.WaterLevel, Is.EqualTo(stored.Origin.y - BiomeCaveWaters.MouthDrop).Within(1e-3f), $"seed {seed}");
				Assert.That(stored.LowestFloor, Is.EqualTo(stored.WaterLevel - 1.2f).Within(1e-3f), $"seed {seed}");
				Assert.That(wet.Character, Is.EqualTo(CaveCharacter.Classic), $"seed {seed}: one line of galleries");
				foreach (CavePassage passage in stored.Passages)
				{
					foreach (CaveNode node in passage.Nodes)
					{
						Assert.That(stored.Origin.y + node.Floor, Is.GreaterThanOrEqualTo(stored.LowestFloor - 0.6f), $"seed {seed}: no floor under the flood's depth");
					}
				}
				drowned++;
			}
			Assert.That(drowned, Is.GreaterThan(0), "some escarpment cave drowns");
		}

		// ── Wiring ────────────────────────────────────────────────────

		[Test]
		public void EveryConsumer_ReadsTheCaveBiomesFields()
		{
			string shaper = Code(World + "PointsOfInterest/Shapers/CaveShaper.cs");
			Assert.That(shaper, Does.Contain("float flood = FloodOf(context, site, form, opening, in planned);"));
			Assert.That(shaper, Does.Contain("depths.x, depths.y, flood);"));
			Assert.That(shaper, Does.Contain("TryRootWindows(context, site, system, ground, in planned);"));
			Assert.That(shaper, Does.Contain("&& !system.Drowned ||"), "a drowned cave's pool is written as a grotto's");
			Assert.That(shaper, Does.Contain("look = CaveRockLayers.WithStain(look, in caves, system.Form, system.WaterLevel);"));
			Assert.That(shaper, Does.Contain("StampWaterLook(context, site, CaveInteriorArt.LookBiome(CaveInteriorArt.Of(context.Request, site, system)));"));
			string hanging = Code(World + "PointsOfInterest/Features/PointOfInterestCaveDressing.Hanging.cs");
			Assert.That(hanging, Does.Contain("CaveHangingPlan plan = CaveHanging.For(biome, system.Form);"));
			Assert.That(hanging, Does.Contain("Drowned();"));
			Assert.That(Code(World + "PointsOfInterest/Features/PointOfInterestCaveDressing.Fungi.cs"), Does.Contain("CaveHanging.Entry(biome, system.Form).RootsNearMouth"));
			Assert.That(Code(World + "PointsOfInterest/Features/PointOfInterestCaveDressing.Drowned.cs"), Does.Contain("BiomeTemplateArt.FloatingPlants(caves.Interior)"));
			string authoring = Code(World + "ProceduralArt/BiomeArtAuthoring.cs");
			foreach (string field in new[] { "CaveFloods", "CaveHangings", "CaveStains" })
			{
				Assert.That(authoring, Does.Contain($"BiomeTemplate.Authored.{field}, biome.{field}, entry.{field}()"), field);
			}
			Assert.That(BiomeTemplate.Authored.CaveFloods, Is.EqualTo("caveFloods"));
			Assert.That(BiomeTemplate.Authored.CaveHangings, Is.EqualTo("caveHangings"));
			Assert.That(BiomeTemplate.Authored.CaveStains, Is.EqualTo("caveStains"));
			// The look: a body's own biome over its shores, on the client.
			Assert.That(Code("Assets/Plugins/FishMMO Water/Scripts/InlandWaterRenderer.Looks.cs"), Does.Contain("if (shore.Own != 0 && BiomeRegistry.TryGetByID(shore.Own, out BiomeTemplate own) && own != null)"));
			// The ring: one branch off by default, its properties in the material buffer and both shaders' blocks.
			Assert.That(Code(Shaders + "FishCaveRock.hlsl"), Does.Contain("if (_FishCaveFlood.w > 0.5 && wall > 0.02h)"));
			Assert.That(Code(Shaders + "FishWeatherLitInput.hlsl"), Does.Contain("float4 _FishCaveFlood;").And.Contain("half4 _FishCaveFloodStain;"));
			foreach (string shader in new[] { "FishWeatherLit.shader", "FishWeatherLitIndirect.shader" })
			{
				Assert.That(Code(Shaders + shader), Does.Contain("_FishCaveFlood(\"Cave tea ring: water level, rise, edge, on\", Vector) = (0, 0, 0.05, 0)"), shader);
			}
			Assert.That(Code(World + "PointsOfInterest/Shapers/CaveRockLayers.cs"), Does.Contain("if (look.FloodStain.a > 0f && material.HasProperty(\"_FishCaveFlood\"))"),
				"an unringed cave's material keeps its bytes");
		}
	}
}
