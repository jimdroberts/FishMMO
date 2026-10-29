using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FishMMO.Client;
using FishMMO.Shared;
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
			Assert.That(BiomeArtSpec.Entry.DefaultCaveHangings().Select(h => (CaveForm)h.Form), Is.EqualTo(Forms.Where(f => BiomeCaveInteriors.InEveryList((BiomeCaveForm)f))));
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
			Assert.That(BiomeCaveWaters.RoofShare(BiomeCaveWormMount.Wood), Is.EqualTo(BiomeCaveWaters.RootMountRoofShare), "on wood: little roof silk too");
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
			Assert.That(shaper, Does.Contain("&& !system.Drowned && !hall ||"), "a drowned cave's pool is written as a grotto's (and a sea hall's, I2n-b)");
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

		// ── 2026-10-26: the rest of decision 86 (finish) ──────────────

		[Test]
		public void TheFloodsMarks_KeepTheirOldText_AndTheLeechesComeLast()
		{
			Assert.That(new BiomeCaveFlood(BiomeCaveForm.Cave, 1.2f, 0.6f).ToString(), Is.EqualTo("Cave=1.2|0.6"), "an entry authored before marks as it did");
			Assert.That(new BiomeCaveFlood(BiomeCaveForm.Cave, 1.2f, 0.6f, 0.5f).ToString(), Is.EqualTo("Cave=1.2|0.6|0.5"));
			Assert.That(new BiomeCaveFlood(BiomeCaveForm.Grotto, 1.5f, 1f, 0f, 1f).ToString(), Is.EqualTo("Grotto=1.5|1|0|1"));
			Assert.That(new BiomeCaveFlood(BiomeCaveForm.Cave, 1.2f, 0.6f, 0.5f, 1.25f).ToString(), Is.EqualTo("Cave=1.2|0.6|0.5|1.25"));
			// Every asset floods nothing yet (so no cave beads and no leech swims until a round authors them).
			foreach (string asset in Directory.GetFiles("Assets/Templates/Entity/Biomes", "*.asset").Where(a => !a.EndsWith("Default Climate.asset")))
			{
				Assert.That(File.ReadAllText(asset), Does.Contain("caveFloods: []"), asset);
			}
		}

		[Test]
		public void TheWormsHueAndMount_AndTheArtTheyName()
		{
			// The art (appended: the hues the dressing drew stand at their indices).
			Assert.That(CaveArt.HueIndex(CaveGlowKind.GlowWorms, "Blue"), Is.EqualTo(0));
			Assert.That(CaveArt.HueIndex(CaveGlowKind.GlowWorms, "Rootlight"), Is.EqualTo(1));
			foreach (CaveGlowKind roots in new[] { CaveGlowKind.HangingRoots, CaveGlowKind.TapRoots })
			{
				Assert.That(CaveArt.HueIndex(roots, "Earth"), Is.EqualTo(0), roots.ToString());
				Assert.That(CaveArt.HueIndex(roots, "Pale"), Is.EqualTo(1), roots.ToString());
				Assert.That(CaveArt.HueIndex(roots, PointOfInterestCaveDressing.WaterRootsHueName), Is.EqualTo(2), roots.ToString());
			}
			CaveGlowHue rootlight = ProceduralArtCatalogue.GlowWormHues[1];
			Assert.That(rootlight.Emission.maxColorComponent > 0f && rootlight.EmitterHue == 1, Is.True, "Rootlight glows blue");
			Assert.That(rootlight.Emission.b, Is.GreaterThan(rootlight.Emission.g * 3f), "the deeper electric blue");
			CaveGlowHue water = ProceduralArtCatalogue.RootHues[2];
			Assert.That(water.Emission.maxColorComponent == 0f && water.EmitterHue == -1, Is.True, "water roots do not glow");
			Assert.That(water.Albedo.r, Is.GreaterThan(water.Albedo.g).And.GreaterThan(water.Albedo.b), "red-brown");
			// The logs a wood mount hangs them on.
			Assert.That(BiomeCaveWaters.LogsWanted(0f, 1f), Is.EqualTo(0));
			Assert.That(BiomeCaveWaters.LogsWanted(400f, 0f), Is.EqualTo(0), "no glow-worms, no logs");
			Assert.That(BiomeCaveWaters.LogsWanted(10f, 1f), Is.EqualTo(1));
			Assert.That(BiomeCaveWaters.LogsWanted(400f, 1f), Is.EqualTo(10));
			Assert.That(BiomeCaveWaters.LogsWanted(5000f, 2f), Is.EqualTo(BiomeCaveWaters.MostLogs));
			foreach (string log in new[] { "FallenLog_Birch", "Driftwood" })
			{
				Assert.That(ProceduralArtCatalogue.Deadwood.Any(d => d.Name == log), Is.True, log);
			}
			// Wiring: the palette carries both (no draw), the roof colonies take them, the drowned passes use them.
			string palette = Code(World + "PointsOfInterest/Features/CaveGlowPalette.cs");
			StringAssert.Contains("p.WormHue = string.IsNullOrEmpty(glow.WormHue) ? 0 : HueOf(CaveGlowKind.GlowWorms, glow.WormHue, 0);", palette);
			StringAssert.Contains("p.WormMount = glow.WormMount;", palette);
			string glowPass = Code(World + "PointsOfInterest/Features/PointOfInterestCaveDressing.Glow.cs");
			StringAssert.Contains("chance *= Colours().WormChance * BiomeCaveWaters.RoofShare(Colours().WormMount);", glowPass);
			StringAssert.Contains("CaveArt.TryGlow(CaveGlowKind.GlowWorms, Colours().WormHue, variant, out CaveGlowArt art)", glowPass);
			Assert.That(glowPass, Does.Not.Contain("CaveArt.TryGlow(CaveGlowKind.GlowWorms, 0, variant"), "no roof colony keeps the fixed first hue");
			string drowned = Code(World + "PointsOfInterest/Features/PointOfInterestCaveDressing.Drowned.cs");
			StringAssert.Contains("colours.WormMount != BiomeCaveWormMount.Roots", drowned);
			StringAssert.Contains("colours.WormMount != BiomeCaveWormMount.Wood", drowned);
			StringAssert.Contains("FloatsOnStreamPools();", drowned);
			StringAssert.Contains("DrownedWood(new DeterministicRNG(", drowned);
			StringAssert.Contains("CaveArt.TryGlow(CaveGlowKind.HangingRoots, HangingRootsHue(hue)", drowned);
			string hanging = Code(World + "PointsOfInterest/Features/PointOfInterestCaveDressing.Hanging.cs");
			StringAssert.Contains("CaveArt.TryGlow(CaveGlowKind.HangingRoots, HangingRootsHue(hue)", hanging);
			StringAssert.Contains("waterRoots ? CaveGlowPalettes.HueOf(CaveGlowKind.TapRoots, WaterRootsHue, hue) : hue", hanging);
			// The glow-worms' materials are marked so the crystals' glow modes reach them (unmoded, a −2 shines as made).
			StringAssert.Contains("bool isCrystal = family.MaterialSet == CrystalSet || family.MaterialSet == GlowWormSet;", Code(World + "ProceduralArt/BiomeArtGenerator.CaveGlow.cs"));
			Assert.That(ProceduralArtCatalogue.CaveGlow(CaveGlowKind.GlowWorms).MaterialSet, Is.EqualTo(BiomeArtGenerator.GlowWormSet));
		}

		[Test]
		public void TheRootWindowsPeatLip()
		{
			// Full on the throat's rock from under the vault up to the opening, fading out past its rim, nothing beyond.
			Assert.That(BiomeCaveWaters.PeatLip(1.5f, 5f, 2f, 4f, 7f), Is.EqualTo(1f));
			Assert.That(BiomeCaveWaters.PeatLip(2f + BiomeCaveWaters.PeatLipReach + 0.1f, 5f, 2f, 4f, 7f), Is.EqualTo(0f));
			Assert.That(BiomeCaveWaters.PeatLip(1f, 4f - BiomeCaveWaters.PeatLipDrop - 0.1f, 2f, 4f, 7f), Is.EqualTo(0f), "under the lip");
			Assert.That(BiomeCaveWaters.PeatLip(1f, 7.6f, 2f, 4f, 7f), Is.EqualTo(0f), "over the opening");
			float last = 1f;
			for (float d = 2f; d <= 2f + BiomeCaveWaters.PeatLipReach; d += 0.1f)
			{
				float lip = BiomeCaveWaters.PeatLip(d, 5f, 2f, 4f, 7f);
				Assert.That(lip, Is.LessThanOrEqualTo(last + 1e-6f), d.ToString());
				last = lip;
			}
			string habitat = Code(World + "PointsOfInterest/Shapers/CaveHabitat.cs");
			StringAssert.Contains("rootWindows = windows.Count > 0 ? windows.ToArray() : null;", habitat);
			StringAssert.Contains("if (rootWindows != null)", habitat);
			StringAssert.Contains("organic = Mathf.Max(organic, BiomeCaveWaters.PeatLipOrganic * PeatLipAt(p));", habitat);
		}

		private static SceneCaveLifeSpot Spot(SceneCaveLifeKind kind, Vector3 at, Vector3 extent, int site, int biome)
			=> new SceneCaveLifeSpot { Kind = kind, Position = at, Extent = extent, SiteId = site, Biome = biome, Normal = Vector3.up };

		[Test]
		public void ALinkedCavesGlows_ReadItsInteriorsRow()
		{
			var spots = new List<SceneCaveLifeSpot>
			{
				Spot(SceneCaveLifeKind.Floor, new Vector3(0f, -20f, 0f), new Vector3(2f, 5f, 0f), 7, 42),
				Spot(SceneCaveLifeKind.Pool, new Vector3(30f, -22f, 4f), new Vector3(3f, 1.2f, 0f), 7, 42),
				Spot(SceneCaveLifeKind.Mouth, new Vector3(80f, 0f, 0f), new Vector3(3f, 0f, 40f), 7, 42),
				Spot(SceneCaveLifeKind.Floor, new Vector3(500f, -10f, 0f), new Vector3(2f, 4f, 0f), 8, 0),
				Spot(SceneCaveLifeKind.Roost, new Vector3(600f, -12f, 0f), new Vector3(1f, 9f, 0f), 9, 43),
			};
			var caves = new List<SceneGlowCave>();
			SceneGlowCaves.FromSpots(spots, caves);
			Assert.That(caves.Count, Is.EqualTo(2), "one box a linked cave; an unlinked cave's spots make none");
			SceneGlowCave a = caves[0];
			Assert.That(a.SiteId == 7 && a.Biome == 42 && a.Row == -1, Is.True);
			Assert.That(a.Min.x, Is.EqualTo(-2f - SceneGlowCaves.Pad).Within(1e-4f));
			Assert.That(a.Max.x, Is.EqualTo(33f + SceneGlowCaves.Pad).Within(1e-4f));
			Assert.That(a.Max.y, Is.EqualTo(-15f + SceneGlowCaves.Pad).Within(1e-4f), "the floor's air to its roof");
			Assert.That(a.Max.x, Is.LessThan(80f), "a mouth's run out of the cave is not its air");
			a.Row = 12;
			caves[0] = a;
			Assert.That(SceneGlowCaves.RowAt(caves, new Vector3(10f, -16f, 0f), 3), Is.EqualTo(12), "a roof colony in the cave");
			Assert.That(SceneGlowCaves.RowAt(caves, new Vector3(10f, 5f, 0f), 3), Is.EqualTo(3), "a glow on the land over it keeps the land's row");
			Assert.That(SceneGlowCaves.RowAt(caves, new Vector3(600f, -12f, 0f), 3), Is.EqualTo(3), "a box with no row is skipped");
			SceneGlowCaves.Pack(caves[0], out Vector4 pa, out Vector4 pb);
			Assert.That(pa, Is.EqualTo(new Vector4(a.Min.x, a.Min.z, a.Max.x, a.Max.z)));
			Assert.That(pb, Is.EqualTo(new Vector4(a.Min.y, a.Max.y, 12f, 0f)));
			// The GPU's twin, the table's plumbing.
			string hlsl = Code(Shaders + "FishGlowModes.hlsl");
			StringAssert.Contains($"float4 _FishGlowCaves[{2 * SceneGlowCaves.MaxCaves}];", hlsl);
			StringAssert.Contains("int count = (int)_FishGlowPlaceInfo.w;", hlsl);
			StringAssert.Contains("if (originWS.x >= a.x && originWS.x <= a.z && originWS.z >= a.y && originWS.z <= a.w && originWS.y >= b.x && originWS.y <= b.y)", hlsl);
			StringAssert.Contains("row = min(FishGlowCaveRow(originWS, row), 63u);", hlsl);
			Assert.That(hlsl.IndexOf("row = min(FishGlowCaveRow(originWS, row), 63u);", StringComparison.Ordinal),
				Is.GreaterThan(hlsl.IndexOf("uint row = min(FishGlowAnchoredRow(originWS.xz, FishGlowRowAt(originWS.xz)), 63u);", StringComparison.Ordinal)), "the cave's row over the anchor's and the grid's");
			string table = Code("Assets/Scripts/Client/World/Terrain/GlowModeTable.cs");
			StringAssert.Contains("\"_FishGlowCaves\"", table);
			StringAssert.Contains("SceneGlowCaves.FromSpots(set.Spots, found);", table);
			StringAssert.Contains("placeInfo = new Vector4(lineCount, ventCount, anchorCount, caveCount);", table);
			StringAssert.Contains("return SceneGlowCaves.RowAt(caves, position, flat);", table);
			StringAssert.Contains("int firstCaveRow = Mathf.Max(floraRows, 1);", table);
		}

		[Test]
		public void TheBeads_AreTheSharedClocks_AndNeverStraddleTheWrap()
		{
			float life = (SceneCaveWater.BeadsPerBurst - 1) * SceneCaveWater.BeadSpacing + 0.2f + SceneCaveWater.MostDepth / SceneCaveWater.BeadSpeed + SceneCaveWater.RingSeconds;
			foreach (float period in SceneCaveWater.Periods)
			{
				double cycles = SceneCaveWater.Wrap / (double)period;
				Assert.That(cycles, Is.EqualTo(Math.Round(cycles)), $"{period} s divides the wrap");
				Assert.That(period, Is.GreaterThan(life + 1f), $"{period} s: a burst ends before the next");
			}
			Assert.That(SceneCaveWater.VentsOf(0f, 3f), Is.EqualTo(0));
			Assert.That(SceneCaveWater.VentsOf(0.05f, 0.5f), Is.EqualTo(1));
			Assert.That(SceneCaveWater.VentsOf(9f, 3f), Is.EqualTo(SceneCaveWater.MaxVents));
			Assert.That(SceneCaveWater.LeechesOf(0f, 3f), Is.EqualTo(0));
			Assert.That(SceneCaveWater.LeechesOf(9f, 3f), Is.EqualTo(SceneCaveWater.MaxLeeches));
			Assert.That(SceneCaveWater.SeedOf(new Vector3(1f, 2f, 3f)), Is.EqualTo(SceneCaveWater.SeedOf(new Vector3(1f, 2f, 3f))));
			Assert.That(SceneCaveWater.SeedOf(new Vector3(1.01f, 2f, 3f)), Is.Not.EqualTo(SceneCaveWater.SeedOf(new Vector3(1f, 2f, 3f))));
			int seen = 0;
			foreach (uint seed in new[] { 1u, 0xDEADBEEFu, SceneCaveWater.SeedOf(new Vector3(-31.4f, 12f, 88.2f)) })
			{
				for (int vent = 0; vent < SceneCaveWater.MaxVents; vent++)
				{
					Vector2 off = SceneCaveWater.VentOffset(seed, vent, 2f);
					Assert.That(off.magnitude, Is.LessThanOrEqualTo(0.85f * 2f + 1e-4f));
					for (int bead = 0; bead < SceneCaveWater.BeadsPerBurst; bead++)
					{
						// None out over the last second and a half before the wrap: the clock's wrap never cuts a trail.
						for (float t = SceneCaveWater.Wrap - 1.5f; t < SceneCaveWater.Wrap; t += 0.05f)
						{
							Assert.That(SceneCaveWater.Bead(t, seed, vent, bead, 1.7f, 1f, out _, out _), Is.False, $"{seed} {vent} {bead} at {t}");
						}
						// Within a burst: rising, never past the surface, then its ring, short-lived.
						float lastRise = -1f;
						bool rang = false;
						for (float t = 0f; t < 60f; t += 0.02f)
						{
							if (!SceneCaveWater.Bead(t, seed, vent, bead, 1.7f, 1f, out float rise, out float ringAge))
							{
								// Between bursts: the next is a bead of its own.
								lastRise = -1f;
								rang = false;
								continue;
							}
							seen++;
							Assert.That(rise, Is.InRange(0f, 1.7f + 1e-4f));
							Assert.That(ringAge, Is.LessThan(SceneCaveWater.RingSeconds));
							if (ringAge < 0f)
							{
								Assert.That(rise, Is.GreaterThanOrEqualTo(lastRise - 1e-4f), "a bead only rises");
								Assert.That(rang, Is.False, "nothing rises after its ring in a burst");
								lastRise = rise;
							}
							else
							{
								rang = true;
							}
						}
					}
				}
			}
			Assert.That(seen, Is.GreaterThan(0), "beads rise");
		}

		[Test]
		public void TheLeeches_CircleOnTheClock_AndTurnOnlyByTheViewersPull()
		{
			var surface = new Vector3(10f, -20f, 5f);
			for (int leech = 0; leech < SceneCaveWater.MaxLeeches; leech++)
			{
				uint seed = SceneCaveWater.SeedOf(surface);
				Vector3 a = SceneCaveWater.LeechHome(0f, seed, leech, surface, 2.5f, 1.2f);
				Vector3 b = SceneCaveWater.LeechHome(SceneCaveWater.Wrap, seed, leech, surface, 2.5f, 1.2f);
				Assert.That((a - b).magnitude, Is.LessThan(2e-3f), "a whole number of turns in the wrap");
				Assert.That(new Vector2(a.x - surface.x, a.z - surface.z).magnitude, Is.LessThanOrEqualTo(2.5f * 0.85f + 1e-3f), "inside its pool");
				Assert.That(a.y, Is.LessThan(surface.y).And.GreaterThan(surface.y - 1.2f - 0.05f), "in the water");
				// Moving slowly: under 0.2 m/s on its circuit.
				Vector3 c = SceneCaveWater.LeechHome(1f, seed, leech, surface, 2.5f, 1.2f);
				Assert.That((c - a).magnitude, Is.LessThan(0.2f));
				var feet = new Vector3(11f, -20.8f, 5.5f);
				Assert.That(SceneCaveWater.LeechAt(a, feet, 0f, seed, leech, surface, 1.2f), Is.EqualTo(a), "no pull, its home: what every viewer sees");
				Vector3 at = SceneCaveWater.LeechAt(a, feet, 1f, seed, leech, surface, 1.2f);
				Assert.That(new Vector2(at.x - feet.x, at.z - feet.z).magnitude, Is.LessThanOrEqualTo(0.3f), "at the wader's legs");
				Assert.That(at.y, Is.LessThan(surface.y).And.GreaterThan(surface.y - 1.2f), "still in the water");
			}
			// The pull: in over its seconds while someone wades, out after; a viewer's own (nothing else reads it).
			float pull = 0f;
			for (int f = 0; f < 60 * (int)SceneCaveWater.AttractSeconds + 2; f++)
			{
				pull = SceneCaveWater.Attract(pull, true, 1f / 60f);
			}
			Assert.That(pull, Is.EqualTo(1f));
			for (int f = 0; f < 60 * (int)SceneCaveWater.ReleaseSeconds + 2; f++)
			{
				pull = SceneCaveWater.Attract(pull, false, 1f / 60f);
			}
			Assert.That(pull, Is.EqualTo(0f));
			Assert.That(SceneCaveWater.Wades(new Vector3(11f, -20.5f, 5f), surface, 2.5f, 1.2f), Is.True);
			Assert.That(SceneCaveWater.Wades(new Vector3(11f, -19.9f, 5f), surface, 2.5f, 1.2f), Is.False, "standing over the water");
			Assert.That(SceneCaveWater.Wades(new Vector3(20f, -20.5f, 5f), surface, 2.5f, 1.2f), Is.False, "out of the pool");
			// A pool is drawn only where its cave biome names beads or leeches for its form.
			SceneCaveLifeSpot pool = Spot(SceneCaveLifeKind.Pool, surface, new Vector3(2.5f, 1.2f, 0f), 7, 42);
			Assert.That(CaveWaterLife.TryPool(in pool, null, out _), Is.False);
			Assert.That(CaveWaterLife.TryPool(in pool, new[] { new BiomeCaveFlood(BiomeCaveForm.Cave, 1.2f, 0.6f) }, out _), Is.False, "a flood with neither");
			Assert.That(CaveWaterLife.TryPool(in pool, new[] { new BiomeCaveFlood(BiomeCaveForm.Grotto, 1.5f, 1f, 1f, 1f) }, out _), Is.False, "another form's");
			Assert.That(CaveWaterLife.TryPool(in pool, new[] { new BiomeCaveFlood(BiomeCaveForm.Cave, 1.2f, 0.6f, 0.8f, 1f) }, out CaveWaterLife.Pool drawn), Is.True);
			Assert.That(drawn.Vents > 0 && drawn.Leeches > 0 && drawn.Seed == SceneCaveWater.SeedOf(surface), Is.True);
			SceneCaveLifeSpot floor = Spot(SceneCaveLifeKind.Floor, surface, new Vector3(2.5f, 1.2f, 0f), 7, 42);
			Assert.That(CaveWaterLife.TryPool(in floor, new[] { new BiomeCaveFlood(BiomeCaveForm.Cave, 1.2f, 0.6f, 0.8f, 1f) }, out _), Is.False, "pools only");
		}

		[Test]
		public void TheWaterLifeShader_MirrorsTheRules_OnDxc()
		{
			string shader = Code(Shaders + "FishCaveWaterLife.shader");
			Assert.That(System.Text.RegularExpressions.Regex.Matches(shader, "HLSLPROGRAM").Count,
				Is.EqualTo(System.Text.RegularExpressions.Regex.Matches(shader, "#pragma use_dxc vulkan").Count), "every pass on DXC");
			StringAssert.Contains($"#define WATER_POOLS {CaveWaterLife.MaxPools}", shader);
			StringAssert.Contains($"#define WATER_VENTS {SceneCaveWater.MaxVents}", shader);
			StringAssert.Contains($"#define WATER_BEADS {SceneCaveWater.BeadsPerBurst}", shader);
			StringAssert.Contains($"#define WATER_LEECHES {SceneCaveWater.MaxLeeches}", shader);
			StringAssert.Contains("#define WATER_BEAD_SPACING 0.35", shader);
			StringAssert.Contains("#define WATER_BEAD_SPEED 0.24", shader);
			StringAssert.Contains("#define WATER_RING_SECONDS 1.2", shader);
			StringAssert.Contains("#define WATER_MOST_DEPTH 2.0", shader);
			Assert.That(SceneCaveWater.BeadSpacing == 0.35f && SceneCaveWater.BeadSpeed == 0.24f && SceneCaveWater.RingSeconds == 1.2f && SceneCaveWater.MostDepth == 2f, Is.True);
			foreach (string c in new[] { "0x7FEB352Du", "0x846CA68Bu", "0x9E3779B1u", "0x632BE5ABu", "vent * 64u + 7u", "leech + 101u, 11u", "leech + 101u, 12u" })
			{
				StringAssert.Contains(c, shader);
			}
			foreach (float period in SceneCaveWater.Periods)
			{
				StringAssert.Contains(period.ToString(System.Globalization.CultureInfo.InvariantCulture).Contains(".") ? period.ToString(System.Globalization.CultureInfo.InvariantCulture) : period.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".0", shader);
			}
			Assert.That(shader, Does.Not.Contain("static const float"), "no constant arrays (indexed by a dynamic index)");
			// The seed in two halves; a build includes the shader through the render profile.
			StringAssert.Contains("seeds[i] = new Vector4(pool.Seed >> 16, pool.Seed & 0xFFFFu, pool.Bubbles, 0f);", Code("Assets/Scripts/Client/World/Terrain/CaveWaterLife.cs"));
			StringAssert.Contains("uint seed = ((uint)round(seeds.x) << 16) | (uint)round(seeds.y);", shader);
			StringAssert.Contains("public Shader CaveWaterLifeShader;", Code("Assets/Scripts/Client/World/Weather/Presentation/WeatherRenderProfile.cs"));
			StringAssert.Contains("profile.CaveWaterLifeShader = AssetDatabase.LoadAssetAtPath<Shader>(CaveWaterLifeShaderPath);", Code("Assets/Scripts/Client/World/Weather/Editor/WeatherRenderAssets.cs"));
		}
	}
}
