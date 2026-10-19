using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The cold glow and the wall-mounted street lights (2026-10-18, phase I6g; decisions 28 and 33): the Glow emitter kind
	/// (appended; its tint, its soft pulse and the stormglass flicker are pure functions of the shared clock and the place, the
	/// shader's copy constant for constant; faint from before dusk), the pieces that give it off (glowjar, rune stone, crystal
	/// post), the flicker reaching a crystal that flickers wherever it lights; and street lights hung on the street-facing
	/// walls (the plan, its post fallback, its own random stream; every biome's default lays exactly what it laid before).
	/// </summary>
	[TestFixture]
	public class SceneGlowAndWallLightTests
	{
		private const string World = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World";
		private const string Shaders = "Assets/Prefabs/Client/Weather/Shaders";

		private static string Code(string path) => File.ReadAllText(Path.Combine(World, path));

		private static string F(float v) => v.ToString("R", CultureInfo.InvariantCulture);

		// ── The glow ──────────────────────────────────────────────────

		[Test]
		public void GlowKind_IsAppended_AndItsHuesAreStable()
		{
			string[] kinds = { "Smoke", "Fire", "Forge", "Lamp", "Hearth", "Indoor", "Crystal", "Torch", "Glow" };
			Assert.That(Enum.GetNames(typeof(SceneEmitterKind)), Is.EqualTo(kinds), "append only: scene assets store the kind");
			Assert.That((int)SceneEmitterKind.Glow, Is.EqualTo(8));
			Assert.That(Enum.GetNames(typeof(SceneGlowHue)), Is.EqualTo(new[] { "Violet", "Blue", "Green", "Amber", "Rune", "Faerie" }));
			Assert.That((int)SceneGlowHue.Faerie, Is.EqualTo(5));
		}

		[Test]
		public void HueAndFlickerBits_RoundTrip_AndKeepTheRestOfTheSeed()
		{
			uint[] seeds = { 0u, 0xFFFFFFFFu, 0x12345678u, 0x9E3779B9u, 0x00000800u, 0x00000700u };
			foreach (uint seed in seeds)
			{
				foreach (SceneGlowHue hue in (SceneGlowHue[])Enum.GetValues(typeof(SceneGlowHue)))
				{
					uint withHue = SceneGlow.WithHue(seed, hue);
					Assert.That(SceneGlow.HueOf(withHue), Is.EqualTo(hue));
					Assert.That(withHue & ~0x700u, Is.EqualTo(seed & ~0x700u), "only bits 8–10 change");
					foreach (bool flickers in new[] { false, true })
					{
						uint both = SceneGlow.WithFlicker(withHue, flickers);
						Assert.That(SceneGlow.Flickers(both), Is.EqualTo(flickers));
						Assert.That(SceneGlow.HueOf(both), Is.EqualTo(hue), "the flicker leaves the hue");
						Assert.That(both & ~SceneGlow.FlickerBit, Is.EqualTo(withHue & ~SceneGlow.FlickerBit), "only bit 11 changes");
						Assert.That(SceneGlow.Flickers(SceneGlow.WithHue(both, SceneGlowHue.Rune)), Is.EqualTo(flickers), "the hue leaves the flicker");
					}
				}
				// A crystal's hue: the four crystal lights keep their numbers; −1 one of the four by the seed's own bits 8–9.
				for (int crystal = 0; crystal < 4; crystal++)
				{
					Assert.That((int)SceneGlow.HueOf(SceneGlow.WithCrystalHue(seed, crystal)), Is.EqualTo(crystal));
				}
				Assert.That((uint)SceneGlow.HueOf(SceneGlow.WithCrystalHue(seed, -1)), Is.EqualTo((seed >> 8) & 3u));
			}
			Assert.That(SceneGlow.HueOf(0x600u), Is.EqualTo(SceneGlowHue.Rune), "hue bits 6 and 7 read as rune-light");
			Assert.That(SceneGlow.HueOf(0x700u), Is.EqualTo(SceneGlowHue.Rune));
			Assert.That(SceneGlow.FlickerBit, Is.EqualTo(0x800u));
		}

		[Test]
		public void Colours_AreCool_ExceptAmber_AndRuneAndFaerieAreTheirs()
		{
			Color rune = SceneGlow.Colour(SceneGlowHue.Rune), faerie = SceneGlow.Colour(SceneGlowHue.Faerie);
			Assert.That(rune.b, Is.GreaterThan(rune.r), "rune-light blue-white");
			Assert.That(rune.r, Is.GreaterThan(0.6f));
			Assert.That(faerie.g, Is.GreaterThan(faerie.b).And.GreaterThan(faerie.r * 0.9f), "faerie-light green-gold");
			// The crystal hues are the cave crystal lights' own (SiteLightPool's Crystal case).
			Assert.That(SceneGlow.Colour(SceneGlowHue.Violet), Is.EqualTo(new Color(0.62f, 0.4f, 1f)));
			Assert.That(SceneGlow.Colour(SceneGlowHue.Blue), Is.EqualTo(new Color(0.35f, 0.62f, 1f)));
			Assert.That(SceneGlow.Colour(SceneGlowHue.Green), Is.EqualTo(new Color(0.4f, 1f, 0.7f)));
			Assert.That(SceneGlow.Colour(SceneGlowHue.Amber), Is.EqualTo(new Color(1f, 0.68f, 0.32f)));
		}

		[Test]
		public void EveryBeat_IsAWholeNumberOfCyclesInTheWrap()
		{
			// So the wave meets itself where the shared clock wraps (WorldMotion.ShaderWrapSeconds) instead of jumping.
			foreach (float hz in new[] { SceneGlow.SlowHz, SceneGlow.SlowerHz, SceneGlow.CrackleAHz, SceneGlow.CrackleBHz, SceneGlow.CrackleCHz, SceneGlow.ShiverHz })
			{
				double cycles = (double)(decimal)hz * SceneGlow.WrapSeconds;
				Assert.That(Math.Abs(cycles - Math.Round(cycles)), Is.LessThan(1e-6), $"{hz} Hz: {cycles} cycles");
			}
			Assert.That(SceneGlow.WrapSeconds, Is.EqualTo(FishMMO.Shared.Celestial.WorldMotion.ShaderWrapSeconds));
			float wrap = (float)SceneGlow.WrapSeconds;
			foreach ((float x, float z) in new[] { (0f, 0f), (123.4f, -56.7f), (-900f, 1400f) })
			{
				Assert.That(SceneGlow.Pulse(wrap, x, z), Is.EqualTo(SceneGlow.Pulse(0f, x, z)).Within(0.02f), "the pulse meets itself");
				Assert.That(SceneGlow.Flicker(wrap, x, z), Is.EqualTo(SceneGlow.Flicker(0f, x, z)).Within(0.06f), "the flicker meets itself");
			}
		}

		[Test]
		public void Pulse_IsSoftAndSlow_AndOneForALightAndItsStone()
		{
			float min = float.MaxValue, max = float.MinValue, steepest = 0f;
			for (int i = 0; i < 20000; i++)
			{
				float t = i * 0.37f;
				float x = (i % 97) * 13.1f - 600f, z = (i % 89) * 11.7f - 500f;
				float p = SceneGlow.Pulse(t, x, z);
				min = Mathf.Min(min, p);
				max = Mathf.Max(max, p);
				steepest = Mathf.Max(steepest, Mathf.Abs(SceneGlow.Pulse(t + 0.1f, x, z) - p));
				// A rune stone and its light 0.35–0.5 m apart, a crystal and its light 0.16 m: one pulse.
				Assert.That(Mathf.Abs(SceneGlow.Pulse(t, x + 0.4f, z - 0.3f) - p), Is.LessThan(0.02f));
			}
			Assert.That(min, Is.GreaterThanOrEqualTo(SceneGlow.PulseMiddle - SceneGlow.SlowDepth - SceneGlow.SlowerDepth - 1e-4f));
			Assert.That(max, Is.LessThanOrEqualTo(1f + 1e-4f));
			Assert.That(max - min, Is.GreaterThan(0.3f), "it pulses");
			Assert.That(steepest, Is.LessThan(0.02f), "softly: a tenth of a second moves it under 2%");
			// Glows a street apart breathe at different moments.
			Assert.That(SceneGlow.Pulse(100f, 0f, 0f), Is.Not.EqualTo(SceneGlow.Pulse(100f, 14f, 0f)).Within(0.01f));
		}

		[Test]
		public void Flicker_IsALowGlowWithUnevenFlashes_SharedByACrystalAndItsLight()
		{
			int samples = 0, flashing = 0, bright = 0;
			float min = float.MaxValue, max = float.MinValue, near = 0f, apart = 0f;
			for (int i = 0; i < 40000; i++)
			{
				float t = i * 0.013f;
				float f = SceneGlow.Flicker(t, 12.5f, -40f);
				min = Mathf.Min(min, f);
				max = Mathf.Max(max, f);
				samples++;
				flashing += f > SceneGlow.FlickerBase + 0.05f ? 1 : 0;
				bright += f > 0.85f ? 1 : 0;
				// The crystal (at the cradle) and its light (0.16 m over it): the same instant; posts 12 m apart, their own.
				near += Mathf.Abs(SceneGlow.Flicker(t, 12.5f, -40f) - SceneGlow.Flicker(t, 12.5f, -40f + 1e-3f));
				apart += Mathf.Abs(SceneGlow.Flicker(t, 12.5f, -40f) - SceneGlow.Flicker(t, 24.5f, -40f));
			}
			Assert.That(min, Is.GreaterThanOrEqualTo(SceneGlow.FlickerBase - 1e-4f));
			Assert.That(max, Is.LessThanOrEqualTo(1f + 1e-4f).And.GreaterThan(0.9f), "flashes reach near full");
			float share = flashing / (float)samples;
			Assert.That(share, Is.InRange(0.03f, 0.4f), "flashes, not a steady light and not a strobe");
			Assert.That(bright, Is.GreaterThan(0));
			Assert.That(near / samples, Is.LessThan(0.005f));
			Assert.That(apart / samples, Is.GreaterThan(0.02f));
		}

		[Test]
		public void Visibility_IsNoneByDay_FaintAtDusk_FullAtNight()
		{
			Assert.That(SceneGlow.Visibility(1f), Is.EqualTo(0f));
			Assert.That(SceneGlow.Visibility(SceneGlow.FirstShows), Is.EqualTo(0f).Within(1e-5f));
			Assert.That(SceneGlow.Visibility(SceneGlow.LampsLight), Is.EqualTo(SceneGlow.DuskShare).Within(1e-5f), "a third lit as the lamps light");
			Assert.That(SceneGlow.Visibility(SceneGlow.FullNight), Is.EqualTo(1f).Within(1e-5f));
			Assert.That(SceneGlow.Visibility(-1f), Is.EqualTo(1f));
			Assert.That(SceneGlow.Visibility(0.2f), Is.InRange(0.001f, SceneGlow.DuskShare), "faintly before the lamps");
			float last = 0f;
			for (float h = 1f; h >= -1f; h -= 0.005f)
			{
				float v = SceneGlow.Visibility(h);
				Assert.That(v, Is.GreaterThanOrEqualTo(last - 1e-6f), $"never dims as the sun goes down (at {h})");
				last = v;
			}
			// SkySystem's lamps light from 0.12 down to −0.04: the glow's knots are theirs.
			Assert.That(SceneGlow.LampsLight, Is.EqualTo(0.12f));
			Assert.That(SceneGlow.FullNight, Is.EqualTo(-0.04f));
		}

		[Test]
		public void Light_IsTheWaveTimesWhatShows_AllOfItUnderground()
		{
			var at = new Vector3(31f, 4f, -7f);
			Assert.That(SceneGlow.Light(50f, at, 0f, false), Is.EqualTo(0f), "dark by day in the open");
			Assert.That(SceneGlow.Light(50f, at, 0f, true), Is.EqualTo(SceneGlow.Intensity * SceneGlow.Pulse(50f, at.x, at.z)).Within(1e-6f));
			Assert.That(SceneGlow.Light(50f, at, 0.5f, false), Is.EqualTo(0.5f * SceneGlow.Intensity * SceneGlow.Pulse(50f, at.x, at.z)).Within(1e-6f));
			Assert.That(SceneGlow.Light(50f, at, 1f, false, true), Is.EqualTo(SceneGlow.Intensity * SceneGlow.Flicker(50f, at.x, at.z)).Within(1e-6f));
			Assert.That(SceneGlow.Wave(50f, at, false), Is.EqualTo(SceneGlow.Pulse(50f, at.x, at.z)));
			Assert.That(SceneGlow.Wave(50f, at, true), Is.EqualTo(SceneGlow.Flicker(50f, at.x, at.z)));
		}

		[Test]
		public void Shader_PulsesAndFlickers_ConstantForConstant()
		{
			string hlsl = File.ReadAllText(Path.Combine(Shaders, "FishStructureWeathering.hlsl"));
			string Body(string name)
			{
				int at = hlsl.IndexOf(name + "(float3 originWS", StringComparison.Ordinal);
				Assert.That(at, Is.GreaterThan(0), name);
				return hlsl.Substring(at, hlsl.IndexOf("\n}", at, StringComparison.Ordinal) - at);
			}
			string pulse = Body("FishStructureGlowLight");
			Assert.That(pulse, Does.Contain($"frac({F(SceneGlow.SlowHz)} * seconds + frac(originWS.x * {F(SceneGlow.SlowWaveX)} + originWS.z * {F(SceneGlow.SlowWaveZ)}))"));
			Assert.That(pulse, Does.Contain($"frac({F(SceneGlow.SlowerHz)} * seconds + frac(originWS.x * {F(SceneGlow.SlowerWaveX)} + originWS.z * {F(SceneGlow.SlowerWaveZ)}))"));
			Assert.That(pulse, Does.Contain($"{F(SceneGlow.PulseMiddle)} + {F(SceneGlow.SlowDepth)} * sin(6.28318530718 * slow) + {F(SceneGlow.SlowerDepth)} * sin(6.28318530718 * slower)"));
			Assert.That(pulse, Does.Contain("_FishGlowLights").And.Contain("_FishWeatherMisc.w"));
			string flicker = Body("FishGlowFlicker");
			Assert.That(flicker, Does.Contain($"frac({F(SceneGlow.CrackleAHz)} * seconds + frac(originWS.x * {F(SceneGlow.CrackleAX)} + originWS.z * {F(SceneGlow.CrackleAZ)}))"));
			Assert.That(flicker, Does.Contain($"frac({F(SceneGlow.CrackleBHz)} * seconds + frac(originWS.x * {F(SceneGlow.CrackleBX)} + originWS.z * {F(SceneGlow.CrackleBZ)}))"));
			Assert.That(flicker, Does.Contain($"frac({F(SceneGlow.CrackleCHz)} * seconds + frac(originWS.x * {F(SceneGlow.CrackleCX)} + originWS.z * {F(SceneGlow.CrackleCZ)}))"));
			Assert.That(flicker, Does.Contain($"frac({F(SceneGlow.ShiverHz)} * seconds + frac(originWS.x * {F(SceneGlow.CrackleAX)} + originWS.z * {F(SceneGlow.CrackleAZ)}))"));
			Assert.That(flicker, Does.Contain($"saturate((a * b * c - {F(SceneGlow.FlashFrom)}) / {F(1f - SceneGlow.FlashFrom)})"));
			Assert.That(flicker, Does.Contain($"{F(SceneGlow.FlickerBase)} + {F(SceneGlow.FlashDepth)} * flash * flash * ({F(1f - SceneGlow.ShiverDepth)} + {F(SceneGlow.ShiverDepth)} * shiver)"));
			string pass = File.ReadAllText(Path.Combine(Shaders, "FishWeatherLitForwardPass.hlsl"));
			Assert.That(pass, Does.Contain("if (_FishStructure > 2.5h)").And.Contain("surfaceData.emission *= FishStructureGlowLight(input.fishRockOrigin, fishUnderground);"));
			Assert.That(pass, Does.Contain("else if (_FishStructure < -0.5h)").And.Contain("surfaceData.emission *= FishGlowFlicker(input.fishRockOrigin);"));
			Assert.That(BiomeArtGenerator.FlickeringCrystalFlag, Is.LessThan(-0.5f));
			// Who publishes and who reads.
			string sky = File.ReadAllText("Assets/Scripts/Client/World/Weather/Sky/SkySystem.cs");
			Assert.That(sky, Does.Contain("Shader.SetGlobalFloat(GlowLightsId, SceneGlow.Visibility(sunDirections[0].y));"));
			string pool = File.ReadAllText("Assets/Scripts/Client/World/Terrain/SiteLightPool.cs");
			Assert.That(pool, Does.Contain("case SceneEmitterKind.Glow:").And.Contain("SceneGlow.Light(time, e.Position, glow, buried, SceneGlow.Flickers(e.Seed))")
				.And.Contain("SceneGlow.Colour(SceneGlow.HueOf(e.Seed))"));
		}

		// ── The pieces that give it off ───────────────────────────────

		[Test]
		public void GlowPieces_TheirTints_AndTheirMaterialsMarks()
		{
			Assert.That(StructurePieces.GlowHueOf("RuneStone"), Is.EqualTo(SceneGlowHue.Rune));
			Assert.That(StructurePieces.GlowHueOf("Glowjar"), Is.EqualTo(SceneGlowHue.Faerie));
			Assert.That(StructurePieces.GlowHueOf(StructurePieces.CrystalPostId), Is.Null, "a crystal post's glow is its crystal's");
			Assert.That(StructurePieces.GlowHueOf("LanternPost"), Is.Null);
			foreach (StructureMaterial m in (StructureMaterial[])Enum.GetValues(typeof(StructureMaterial)))
			{
				float expected = m == StructureMaterial.Glass ? 2f : m == StructureMaterial.Runelight || m == StructureMaterial.Faelight ? 3f : 1f;
				Assert.That(StructureSurfaces.StructureFlag(m), Is.EqualTo(expected), m.ToString());
				Assert.That(StructureSurfaces.TryGlow(m, out SceneGlowHue hue), Is.EqualTo(expected == 3f), m.ToString());
				if (expected == 3f)
				{
					Assert.That(hue, Is.EqualTo(m == StructureMaterial.Runelight ? SceneGlowHue.Rune : SceneGlowHue.Faerie));
				}
			}
			// The palette's mirror of the materials keeps step (BiomeStructurePalette's enum names the same materials).
			Assert.That(Enum.IsDefined(typeof(BiomeStructureMaterial), "Runelight") && Enum.IsDefined(typeof(BiomeStructureMaterial), "Faelight"));
			Assert.That((int)BiomeStructureMaterial.Runelight, Is.EqualTo((int)StructureMaterial.Runelight));
			Assert.That((int)BiomeStructureMaterial.Faelight, Is.EqualTo((int)StructureMaterial.Faelight));
			string lights = Code("ProceduralArt/StructurePieces.Lights.cs");
			Assert.That(lights, Does.Contain("StructureMaterial.Faelight, 12, StructureRole.Detail));\n\t\t\td.Emit(jar + Vector3.up * 0.075f, SceneEmitterKind.Glow);"));
			Assert.That(lights, Does.Contain("StructureMaterial.Runelight, k, StructureRole.Detail)"));
			Assert.That(System.Text.RegularExpressions.Regex.Matches(lights, @"d\.Emit\([^;]*SceneEmitterKind\.Glow\);").Count, Is.EqualTo(3), "glowjar, crystal post, rune stone");
		}

		[Test]
		public void Flicker_ReachesOnlyACrystalThatFlickers_AndNoneDoesYet()
		{
			foreach (CaveMineral mineral in ProceduralArtCatalogue.CaveMinerals)
			{
				Assert.That(mineral.Flickers, Is.False, $"{mineral.Kind}: no kind flickers before its biome's round (stormglass)");
			}
			string site = Code("PointsOfInterest/PointOfInterestSiteContext.cs");
			Assert.That(site, Does.Contain("seed = SceneGlow.WithFlicker(seed, cradled && postMineral.Flickers);"), "the bit always written on a glow");
			string dressing = Code("PointsOfInterest/Features/PointOfInterestCaveDressing.cs");
			Assert.That(dressing, Does.Contain("Glow(laid + Vector3.up * 0.8f, reach, mineral.LightHue >= 0 ? mineral.LightHue : set.Hue, mineral.Flickers);"));
			Assert.That(dressing, Does.Contain("Emit(local, SceneEmitterKind.Glow, SceneGlow.WithFlicker(SceneGlow.WithCrystalHue(seed, hue), true), reach);"));
			string glow = Code("PointsOfInterest/Features/PointOfInterestCaveDressing.Glow.cs");
			Assert.That(glow, Does.Contain("if (art.Flickers)").And.Contain("SceneGlow.WithFlicker(SceneGlow.WithCrystalHue(seed, art.EmitterHue), true)"));
			Assert.That(Code("ProceduralArt/CaveArt.cs"), Does.Contain("Flickers = mineral.Flickers,"));
			Assert.That(Code("ProceduralArt/BiomeArtGenerator.CaveGlow.cs"), Does.Contain("m.SetFloat(\"_FishStructure\", FlickeringCrystalFlag);"));
		}

		// ── Wall-mounted street lights ────────────────────────────────

		[Test]
		public void WallLights_AreTheWallMounts_AndEachHasAPostToFallBackOn()
		{
			var walls = ((BiomeWayLightKind[])Enum.GetValues(typeof(BiomeWayLightKind))).Where(k => BiomeWayLights.MountOf(k) == BiomeWayLightMount.Wall).ToArray();
			Assert.That(walls, Is.EquivalentTo(new[] { BiomeWayLightKind.WallTorch, BiomeWayLightKind.WallLantern, BiomeWayLightKind.HangingBracket }));
			Assert.That(BiomeWayLights.PostFor(BiomeWayLightKind.WallTorch), Is.EqualTo(BiomeWayLightKind.TorchPost));
			Assert.That(BiomeWayLights.PostFor(BiomeWayLightKind.WallLantern), Is.EqualTo(BiomeWayLightKind.LanternPost));
			Assert.That(BiomeWayLights.PostFor(BiomeWayLightKind.HangingBracket), Is.EqualTo(BiomeWayLightKind.CrookLantern));
			foreach (BiomeWayLightKind k in (BiomeWayLightKind[])Enum.GetValues(typeof(BiomeWayLightKind)))
			{
				BiomeWayLightKind post = BiomeWayLights.PostFor(k);
				if (BiomeWayLights.MountOf(k) == BiomeWayLightMount.Ground)
				{
					Assert.That(post, Is.EqualTo(k), "a standing light is its own");
				}
				else
				{
					Assert.That(BiomeWayLights.MountOf(post), Is.EqualTo(BiomeWayLightMount.Ground), $"{k}'s fallback stands on the ground");
				}
			}
		}

		[Test]
		public void Defaults_NameNoWallLight_SoEverySiteLaysWhatItLaidBefore()
		{
			// The rule: the standing lights' branch is the old one, word for word; the wall pass runs only for a wall light.
			string props = Code("PointsOfInterest/PropsFeature.cs");
			const string Standing = "if (town && BiomeTemplateArt.WayLight(siteBiome, record.Kind, out BiomeWayLight light) && BiomeWayLights.MountOf(light.Light) == BiomeWayLightMount.Ground)";
			const string Hung = "else if (town && BiomeTemplateArt.WayLight(siteBiome, record.Kind, out light) && BiomeWayLights.MountOf(light.Light) == BiomeWayLightMount.Wall)";
			Assert.That(props, Does.Contain(Standing).And.Contain(Hung));
			int hung = props.IndexOf(Hung, StringComparison.Ordinal);
			int end = props.IndexOf("public override void Build(PointOfInterestSiteContext context)", hung, StringComparison.Ordinal);
			Assert.That(hung, Is.GreaterThan(props.IndexOf(Standing, StringComparison.Ordinal)));
			string pass = props.Substring(hung, end - hung);
			Assert.That(pass, Does.Not.Contain("random."), "the wall pass never draws on the site's stream");
			Assert.That(pass, Does.Contain("PointOfInterestWallLights.StreetSeeds(record.Id, streets.Count)"));
			// The default and every biome's spec and template light their streets with standing lights only (until a round names one).
			Assert.That(BiomeWayLights.Default().All(l => BiomeWayLights.MountOf(l.Light) == BiomeWayLightMount.Ground));
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				foreach (BiomeWayLight l in entry.WayLights())
				{
					Assert.That(BiomeWayLights.MountOf(l.Light), Is.Not.EqualTo(BiomeWayLightMount.Wall),
						$"{entry.Biome}: {l} — a deliberate round's change; update this snapshot when a biome is given wall lights");
				}
			}
			int assets = 0, entries = 0;
			foreach (string path in Directory.GetFiles("Assets/Templates/Entity/Biomes", "*.asset"))
			{
				string[] lines = File.ReadAllLines(path);
				int at = Array.FindIndex(lines, l => l == "  wayLights:");
				if (at < 0)
				{
					continue;
				}
				assets++;
				for (int i = at + 1; i < lines.Length && (lines[i].StartsWith("  - ", StringComparison.Ordinal) || lines[i].StartsWith("    ", StringComparison.Ordinal)); i++)
				{
					string t = lines[i].Trim();
					if (t.StartsWith("Light: ", StringComparison.Ordinal))
					{
						entries++;
						var kind = (BiomeWayLightKind)int.Parse(t.Substring(7), CultureInfo.InvariantCulture);
						Assert.That(BiomeWayLights.MountOf(kind), Is.Not.EqualTo(BiomeWayLightMount.Wall), $"{Path.GetFileName(path)}: {kind}");
					}
				}
			}
			Assert.That(assets, Is.GreaterThanOrEqualTo(80), "the templates' way lights were read");
			Assert.That(entries, Is.GreaterThan(300));
		}

		/// <summary>The standing lights' places along a street, exactly as PropsFeature.AddClutter walks it.</summary>
		private static List<(Vector2 at, float yaw)> Standing((Vector2 a, Vector2 b, float half) street, float spacing)
		{
			var list = new List<(Vector2, float)>();
			Vector2 ab = street.b - street.a;
			float length = ab.magnitude;
			if (length < 10f)
			{
				return list;
			}
			Vector2 along = ab / length, across = new Vector2(-along.y, along.x);
			int side = 1;
			for (float t = 6f; t < length - 6f; t += spacing, side = -side)
			{
				list.Add((street.a + along * t + across * side * (street.half + 0.7f), Mathf.Atan2(-across.x * side, -across.y * side) * Mathf.Rad2Deg));
			}
			return list;
		}

		private static readonly (Vector2 a, Vector2 b, float half)[] Streets =
		{
			(new Vector2(0f, 0f), new Vector2(60f, 0f), 2f),
			(new Vector2(10f, -30f), new Vector2(10f, 25f), 1.5f),
			(new Vector2(100f, 100f), new Vector2(105f, 103f), 2f),
			(new Vector2(-40f, 20f), new Vector2(-10f, 60f), 2.5f),
		};

		[Test]
		public void Plan_WithNoWalls_StandsAPostWhereEveryStandingLightStood()
		{
			List<PointOfInterestWallLights.WallLight> plan = PointOfInterestWallLights.Plan(Streets, new List<PointOfInterestWallLights.WallSpan>(), 13f, 5f);
			var expected = new List<(Vector2 at, float yaw)>();
			foreach ((Vector2 a, Vector2 b, float half) street in Streets)
			{
				expected.AddRange(Standing(street, 13f));
			}
			Assert.That(plan, Has.Count.EqualTo(expected.Count));
			for (int i = 0; i < plan.Count; i++)
			{
				Assert.That(plan[i].OnWall, Is.False);
				Assert.That(plan[i].At, Is.EqualTo(expected[i].at), $"light {i}");
				Assert.That(plan[i].Yaw, Is.EqualTo(expected[i].yaw), $"light {i}");
			}
			Assert.That(plan.Select(l => l.Street).Distinct(), Is.EquivalentTo(new[] { 0, 1, 3 }), "a street under 10 m is unlit");
			Assert.That(PointOfInterestWallLights.Plan(null, null, 13f, 5f), Is.Empty);
			Assert.That(PointOfInterestWallLights.Plan(Streets, null, 0f, 5f), Is.Empty);
		}

		/// <summary>A wall stretch at world height y, its face looking along <paramref name="normal"/> (x, z).</summary>
		private static PointOfInterestWallLights.WallSpan Wall(Vector2 a, Vector2 b, float y, Vector2 normal) => new PointOfInterestWallLights.WallSpan
		{
			A = new Vector3(a.x, y, a.y),
			B = new Vector3(b.x, y, b.y),
			Normal = new Vector3(normal.x, 0f, normal.y).normalized,
		};

		[Test]
		public void Plan_HangsEachLightOnTheNearestWallFacingTheStreet_ElseAPost()
		{
			// One street along +x, 4 m wide; the first light at t = 6 on the +z side (side 1: across = +z), the next at 19 on −z.
			var street = new[] { (new Vector2(0f, 0f), new Vector2(60f, 0f), 2f) };
			var walls = new List<PointOfInterestWallLights.WallSpan>
			{
				// +z side, 4 m back from the street's edge, facing the street (−z): carries the light at t = 6.
				Wall(new Vector2(3f, 6f), new Vector2(9f, 6f), 2.7f, new Vector2(0f, -1f)),
				// −z side, facing AWAY from the street: never carries one.
				Wall(new Vector2(15f, -5f), new Vector2(23f, -5f), 2.7f, new Vector2(0f, -1f)),
				// +z side, too far back (12 m): never.
				Wall(new Vector2(28f, 14f), new Vector2(36f, 14f), 2.6f, new Vector2(0f, -1f)),
				// −z side, facing the street, near t = 45 (place 3 at t = 45 is side −1).
				Wall(new Vector2(40f, -3f), new Vector2(44f, -3f), 2.8f, new Vector2(0f, 1f)),
			};
			List<PointOfInterestWallLights.WallLight> plan = PointOfInterestWallLights.Plan(street, walls, 13f, 5f);
			List<(Vector2 at, float yaw)> standing = Standing(street[0], 13f);
			Assert.That(plan, Has.Count.EqualTo(standing.Count).And.Count.EqualTo(4));
			// t = 6: hung on the first wall, at its height, its fixing on the face, facing the street.
			Assert.That(plan[0].OnWall, Is.True);
			Assert.That(plan[0].At.x, Is.EqualTo(6f).Within(1e-4f));
			Assert.That(plan[0].At.y, Is.EqualTo(6f).Within(1e-4f));
			Assert.That(plan[0].Height, Is.EqualTo(2.7f));
			Assert.That(plan[0].Yaw, Is.EqualTo(PointOfInterestWallLights.YawFacing(new Vector2(0f, -1f))));
			// t = 19 (−z side): its own side's wall faces away, the other side has none in slide → a post where the standing light stood.
			Assert.That(plan[1].OnWall, Is.False);
			Assert.That(plan[1].At, Is.EqualTo(standing[1].at));
			Assert.That(plan[1].Yaw, Is.EqualTo(standing[1].yaw));
			Assert.That(float.IsNaN(plan[1].Height));
			// t = 32 (+z side): the only wall there is 12 m back → a post.
			Assert.That(plan[2].OnWall, Is.False);
			Assert.That(plan[2].At, Is.EqualTo(standing[2].at));
			// t = 45 (−z side): within the slide (0.35 × 13) of its wall's end at x = 44 → hung there.
			Assert.That(plan[3].OnWall, Is.True);
			Assert.That(plan[3].At.x, Is.EqualTo(44f).Within(1e-4f));
			Assert.That(plan[3].Height, Is.EqualTo(2.8f));
			Assert.That(plan.Select(l => l.Place), Is.EqualTo(new[] { 0, 1, 2, 3 }));
			// Deterministic: the same streets and walls, the same lights.
			List<PointOfInterestWallLights.WallLight> again = PointOfInterestWallLights.Plan(street, walls, 13f, 5f);
			Assert.That(again.Select(l => (l.At, l.OnWall, l.Yaw)), Is.EqualTo(plan.Select(l => (l.At, l.OnWall, l.Yaw))));
		}

		[Test]
		public void Plan_TakesTheOtherSidesWall_AndNeverCrowdsTwoOnOne()
		{
			var street = new[] { (new Vector2(0f, 0f), new Vector2(40f, 0f), 2f) };
			// Only a −z wall, facing the street, long: t = 6 (side +1) has nothing of its own and takes it.
			var walls = new List<PointOfInterestWallLights.WallSpan> { Wall(new Vector2(0f, -3f), new Vector2(40f, -3f), 2.7f, new Vector2(0f, 1f)) };
			List<PointOfInterestWallLights.WallLight> plan = PointOfInterestWallLights.Plan(street, walls, 8f, 5f);
			Assert.That(plan.All(l => l.OnWall), Is.True);
			Assert.That(plan[0].At.y, Is.EqualTo(-3f).Within(1e-4f));
			for (int i = 0; i < plan.Count; i++)
			{
				for (int j = i + 1; j < plan.Count; j++)
				{
					Assert.That(Vector2.Distance(plan[i].At, plan[j].At), Is.GreaterThanOrEqualTo(4f - 1e-3f), "half the spacing at least");
				}
			}
		}

		[Test]
		public void YawFacing_TurnsTheKitsMinusZOutOfTheWall()
		{
			// Kit wall lights stand out along their −z; a yaw θ turns −z to (−sin θ, −cos θ).
			foreach (Vector2 n in new[] { new Vector2(1f, 0f), new Vector2(0f, 1f), new Vector2(-1f, 0f), new Vector2(0f, -1f), new Vector2(0.6f, -0.8f) })
			{
				float yaw = PointOfInterestWallLights.YawFacing(n) * Mathf.Deg2Rad;
				Assert.That(-Mathf.Sin(yaw), Is.EqualTo(n.x).Within(1e-5f));
				Assert.That(-Mathf.Cos(yaw), Is.EqualTo(n.y).Within(1e-5f));
			}
		}

		[Test]
		public void StreetSeeds_AreTheSitesOwn_AndStable()
		{
			int[] a = PointOfInterestWallLights.StreetSeeds(1234, 6);
			Assert.That(a, Has.Length.EqualTo(6));
			Assert.That(PointOfInterestWallLights.StreetSeeds(1234, 6), Is.EqualTo(a));
			Assert.That(PointOfInterestWallLights.StreetSeeds(1234, 3), Is.EqualTo(a.Take(3)), "a street's seed does not depend on how many follow");
			Assert.That(PointOfInterestWallLights.StreetSeeds(1235, 6), Is.Not.EqualTo(a));
			Assert.That(a.Distinct().Count(), Is.EqualTo(6));
			Assert.That(PointOfInterestWallLights.StreetSeeds(1, 0), Is.Empty);
		}

		[Test]
		public void Mountable_RefusesPanesLightsClothIronAndThatch()
		{
			foreach (StructureMaterial m in new[] { StructureMaterial.Glass, StructureMaterial.Lamplight, StructureMaterial.Runelight, StructureMaterial.Faelight,
				StructureMaterial.Canvas, StructureMaterial.Dyed, StructureMaterial.Iron, StructureMaterial.Thatch, StructureMaterial.Reed, StructureMaterial.Ash })
			{
				Assert.That(PointOfInterestWallLights.Mountable(m), Is.False, m.ToString());
			}
			foreach (StructureMaterial m in new[] { StructureMaterial.Timber, StructureMaterial.Stone, StructureMaterial.Plaster, StructureMaterial.Log, StructureMaterial.Adobe })
			{
				Assert.That(PointOfInterestWallLights.Mountable(m), Is.True, m.ToString());
			}
		}

		// ── Unity only (builds the kit's houses: native maths) ────────

		[Test]
		public void EveryLivedInPiece_OffersWallsBetweenTwoFourAndThreeMetres_ClearOfItsDoors()
		{
			int pieces = 0, withWalls = 0, spans = 0;
			foreach (StructurePiece piece in StructurePieces.All)
			{
				for (int v = 0; v < piece.Variants; v++)
				{
					if (StructurePieces.RoomsOf(piece, v).Count == 0)
					{
						continue;
					}
					pieces++;
					IReadOnlyList<PointOfInterestWallLights.WallSpan> found = PointOfInterestWallLights.SpansOf(piece, v, null);
					withWalls += found.Count > 0 ? 1 : 0;
					foreach (PointOfInterestWallLights.WallSpan w in found)
					{
						spans++;
						Assert.That(w.A.y, Is.InRange(2.4f - 1e-4f, 3f + 1e-4f), $"{piece.Id} v{v}: the fixing's height");
						Assert.That(w.B.y, Is.EqualTo(w.A.y).Within(1e-4f));
						Assert.That(w.Normal.y, Is.EqualTo(0f).Within(1e-5f));
						Assert.That(w.Normal.magnitude, Is.EqualTo(1f).Within(1e-4f));
						Assert.That(Vector3.Distance(w.A, w.B) + PointOfInterestWallLights.Step, Is.GreaterThanOrEqualTo(PointOfInterestWallLights.MinStretch - 1e-3f), $"{piece.Id} v{v}");
						foreach (StructureDoor d in StructurePieces.DoorsOf(piece, v))
						{
							// Never over a doorway: the stretch keeps a margin either side of the leaf where it crosses its line.
							Vector3 across = d.Across.normalized;
							for (float k = 0f; k <= 1f; k += 0.1f)
							{
								Vector3 p = Vector3.Lerp(w.A, w.B, k);
								float u = Vector3.Dot(p - d.Hinge, across);
								float depth = Mathf.Abs(Vector3.Dot(p - d.Hinge, d.Inward.normalized));
								bool overDoor = u > -PointOfInterestWallLights.DoorMargin + 0.01f && u < d.Width + PointOfInterestWallLights.DoorMargin - 0.01f && depth < 1f
									&& p.y - PointOfInterestWallLights.Below < d.Height + PointOfInterestWallLights.DoorHeadroom - 0.01f;
								Assert.That(overDoor, Is.False, $"{piece.Id} v{v}: a fixing over its door at {p}");
							}
						}
					}
				}
			}
			Assert.That(pieces, Is.GreaterThan(10));
			Assert.That(withWalls, Is.GreaterThan(pieces / 2), "most lived-in buildings offer a wall");
			Console.WriteLine($"[wall lights] {withWalls} of {pieces} lived-in piece variants offer {spans} stretch(es)");
		}

		/// <summary>
		/// A wall light keeps clear of what else stands off its wall (2026-10-18, I6h): the tavern's sign on its bracket by the door,
		/// and the eaves of a low roof (no light is hung where its lantern would stand in the roof); every span of every lived-in
		/// piece, its roof variants' too. Unity only (it builds the pieces).
		/// </summary>
		[Test]
		public void WallLights_KeepClearOfTheTavernsSign_AndOfTheEaves()
		{
			StructurePiece tavern = StructurePieces.Find("Tavern");
			for (int v = 0; v < tavern.Variants; v++)
			{
				List<StructureSolid> solids = StructurePieces.BuildSolids(tavern, v, 0, StructurePieces.DefaultSeed);
				// The sign: its bracket, its board and its painted face (StructurePieces.Tavern, keys 410 and 414–415).
				List<Bounds> sign = solids.Where(x => x.Key == 410 || x.Key == 414 || x.Key == 415).Select(x => x.Bounds).ToList();
				Assert.That(sign.Count, Is.EqualTo(3), $"Tavern v{v}: its sign");
				IReadOnlyList<PointOfInterestWallLights.WallSpan> found = PointOfInterestWallLights.SpansOf(tavern, v, null);
				Assert.That(found.Count, Is.GreaterThan(0), $"Tavern v{v} still offers walls");
				foreach (PointOfInterestWallLights.WallSpan w in found)
				{
					for (float k = 0f; k <= 1f; k += 0.125f)
					{
						Vector3 at = Vector3.Lerp(w.A, w.B, k);
						Vector3 across = Vector3.Cross(Vector3.up, w.Normal);
						var room = new Bounds(at + w.Normal * 0.45f, Vector3.zero);
						foreach (float o in new[] { PointOfInterestWallLights.ClearFrom, PointOfInterestWallLights.ClearOut })
						{
							foreach (float a in new[] { -PointOfInterestWallLights.ClearBeside, PointOfInterestWallLights.ClearBeside })
							{
								room.Encapsulate(at + w.Normal * (o - PointOfInterestWallLights.Proud) + across * a + Vector3.down * PointOfInterestWallLights.Below);
								room.Encapsulate(at + w.Normal * (o - PointOfInterestWallLights.Proud) + across * a + Vector3.up * PointOfInterestWallLights.Above);
							}
						}
						foreach (Bounds b in sign)
						{
							Assert.That(b.Intersects(room), Is.False, $"Tavern v{v}: a wall light at {at} hangs into its sign ({b})");
						}
					}
				}
			}
			// No light's lantern stands in a roof: half a metre out at the top of its room, the eaves are clear.
			int checkedSpans = 0;
			foreach (StructurePiece piece in StructurePieces.All)
			{
				for (int v = 0; v < piece.Variants; v++)
				{
					if (StructurePieces.RoomsOf(piece, v).Count == 0)
					{
						continue;
					}
					var roofs = new List<StructureRoofPitch?> { null };
					if (StructureRoofs.TryGetFollower(piece, v, out StructureRoofFollower f))
					{
						roofs.AddRange(StructureRoofs.Pitches.Where(p => p != StructureRoofs.PitchOf(f.Roof) && !StructureRoofs.SameAsKit(f, p)).Select(p => (StructureRoofPitch?)p));
					}
					foreach (StructureRoofPitch? roof in roofs)
					{
						List<StructureSolid> roofSolids = StructurePieces.BuildSolids(piece, v, 0, StructurePieces.DefaultSeed, roof).Where(x => x.Role == StructureRole.Roof && x.Faces.Count >= 4).ToList();
						foreach (PointOfInterestWallLights.WallSpan w in PointOfInterestWallLights.SpansOf(piece, v, roof))
						{
							checkedSpans++;
							foreach (Vector3 at in new[] { w.A, (w.A + w.B) * 0.5f, w.B })
							{
								Vector3 lantern = at + w.Normal * 0.5f + Vector3.up * (PointOfInterestWallLights.Above - 0.05f);
								foreach (StructureSolid r in roofSolids)
								{
									bool inside = r.Faces.All(face => face.Points.Count == 0 || Vector3.Dot(lantern - face.Points[0], face.Normal) <= 0.02f);
									Assert.That(inside, Is.False, $"{piece.Id} v{v} {(roof == null ? "kit" : StructureRoofs.Suffix(roof.Value))}: a wall light at {at} hangs into its roof");
								}
							}
						}
					}
				}
			}
			Assert.That(checkedSpans, Is.GreaterThan(100));
		}
	}
}
