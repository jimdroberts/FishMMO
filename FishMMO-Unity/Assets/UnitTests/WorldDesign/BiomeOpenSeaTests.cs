using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Phase I2p of the biome suite (2026-10-25, decision 42): what drifts on the open sea (BiomeSeaFloats, SeaFloatSystem, FishMMO/Sea
	/// Floats, riding FishWaterSwell.hlsl, the sea's own wave sum split out of FishWaterWaves.hlsl), and the adrift sites. Every default
	/// is what was there before: no biome floats anything, no biome hosts an adrift site, and the sea's surface is drawn from the same sum.
	/// </summary>
	public class BiomeOpenSeaTests
	{
		private const string BiomeFolder = "Assets/Templates/Entity/Biomes";
		private const string WaterShaders = "Assets/Plugins/FishMMO Water/Shaders";
		private const string ClientShaders = "Assets/Prefabs/Client/Weather/Shaders";
		private const uint Seed = 0x5EA5EEDu;

		private static IEnumerable<BiomeSeaFloatKind> Kinds()
		{
			foreach (BiomeSeaFloatKind kind in Enum.GetValues(typeof(BiomeSeaFloatKind)))
			{
				yield return kind;
			}
		}

		// ── Defaults: nothing changes ─────────────────────────────────

		[Test]
		public void Defaults_FloatNothingAtSea_ForEveryBiome()
		{
			Assert.That(BiomeArtSpec.Entries.Length, Is.EqualTo(83));
			// An authored round's own floats are pinned in BiomeAuthoredRoundTests.
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries.Where(e => !BiomeAuthoredRounds.Contains(e.Biome)))
			{
				Assert.That(entry.SeaFloats(), Is.Empty, $"{entry.Biome} floats nothing at sea yet");
			}
			int templates = 0;
			foreach (string path in Directory.GetFiles(BiomeFolder, "*.asset"))
			{
				string text = File.ReadAllText(path);
				if (!Regex.IsMatch(text, "^  MinTemperature:", RegexOptions.Multiline))
				{
					continue;
				}
				templates++;
				if (BiomeAuthoredRounds.Contains(Path.GetFileNameWithoutExtension(path)))
				{
					continue;
				}
				Assert.That(Regex.IsMatch(text, @"^  seaFloats:\s*\n  - ", RegexOptions.Multiline), Is.False, $"{Path.GetFileName(path)} floats something at sea");
			}
			Assert.That(templates, Is.EqualTo(83));
			string source = File.ReadAllText("Assets/Scripts/Shared/Implementation/Tools/Biomes/BiomeTemplate.cs");
			StringAssert.Contains("private List<BiomeSeaFloat> seaFloats = new List<BiomeSeaFloat>();", source);
			Assert.That(BiomeSeaFloats.Any(BiomeSeaFloats.None()), Is.False);
			Assert.That(BiomeSeaFloats.Any(new List<BiomeSeaFloat> { new BiomeSeaFloat(BiomeSeaFloatKind.Sargassum, 0f) }), Is.False, "a zero weight floats nothing");
		}

		[Test]
		public void SeaFloatKinds_AreAppendOnly_AndEachHasTraits()
		{
			Assert.That((int)BiomeSeaFloatKind.Sargassum, Is.EqualTo(0));
			Assert.That((int)BiomeSeaFloatKind.Velella, Is.EqualTo(1));
			Assert.That((int)BiomeSeaFloatKind.GooseLog, Is.EqualTo(2));
			Assert.That((int)BiomeSeaFloatKind.PropaguleRaft, Is.EqualTo(3));
			Assert.That((int)BiomeSeaFloatKind.Corkline, Is.EqualTo(4));
			Assert.That((int)BiomeSeaFloatKind.Growler, Is.EqualTo(5));
			Assert.That((int)BiomeSeaFloatKind.PumiceRaft, Is.EqualTo(6));
			Assert.That((int)BiomeSeaFloatKind.PancakeIce, Is.EqualTo(7));
			Assert.That((int)BiomeSeaFloatKind.BrashIce, Is.EqualTo(8));
			Assert.That(Enum.GetValues(typeof(BiomeSeaFloatKind)).Length, Is.EqualTo(BiomeSeaFloats.KindCount));
			foreach (BiomeSeaFloatKind kind in Kinds())
			{
				BiomeSeaFloatTraits t = BiomeSeaFloats.Traits(kind);
				Assert.That(t.Chance, Is.GreaterThan(0f).And.LessThanOrEqualTo(1f), kind.ToString());
				Assert.That(t.Spacing.x, Is.GreaterThan(0f), kind.ToString());
				Assert.That(t.Spacing.y, Is.GreaterThan(0f), kind.ToString());
				Assert.That(t.DrawMetres, Is.GreaterThan(0f).And.LessThanOrEqualTo(BiomeSeaFloats.MaxDrawMetres), kind + ": inside the sea's wave fade");
				Assert.That(t.Size.y, Is.GreaterThanOrEqualTo(t.Size.x), kind.ToString());
				Assert.That(t.DriftMetresPerSecond, Is.GreaterThan(0f), kind.ToString());
			}
			Assert.That(BiomeSeaFloats.MaxDrawMetres, Is.LessThan(900f), "under the sea material's _WaveFadeStart (900 m)");
		}

		[Test]
		public void Ice_FloatsOnlyOnColdSeas()
		{
			const float degree = 1f / 33.1f;
			Assert.That(BiomeSeaFloats.LivesAt(BiomeSeaFloatKind.Growler, 10f * degree), Is.False);
			Assert.That(BiomeSeaFloats.LivesAt(BiomeSeaFloatKind.Growler, 2f * degree), Is.True);
			Assert.That(BiomeSeaFloats.LivesAt(BiomeSeaFloatKind.PancakeIce, 0f), Is.False);
			Assert.That(BiomeSeaFloats.LivesAt(BiomeSeaFloatKind.PancakeIce, -3f * degree), Is.True);
			Assert.That(BiomeSeaFloats.LivesAt(BiomeSeaFloatKind.BrashIce, 3f * degree), Is.False);
			Assert.That(BiomeSeaFloats.LivesAt(BiomeSeaFloatKind.Sargassum, 1f), Is.True);
			Assert.That(BiomeSeaFloats.LivesAt(BiomeSeaFloatKind.Sargassum, -1f), Is.True);
		}

		// ── The drift ─────────────────────────────────────────────────

		[Test]
		public void SplitDrift_IsExact_AndTheRemainderUnderAPitch()
		{
			foreach (BiomeSeaFloatKind kind in Kinds())
			{
				float pitch = BiomeSeaFloats.Traits(kind).Spacing.x;
				foreach (double seconds in new[] { 0.0, 0.3, 17.5, 3600.0, 86400.0 * 3 + 0.125, 86400.0 * 365 * 40 + 12.75, 1e10 })
				{
					double drift = BiomeSeaFloats.Drifted(kind, seconds);
					BiomeSeaFloats.SplitDrift(drift, pitch, out int steps, out float remainder);
					Assert.That(remainder, Is.GreaterThanOrEqualTo(0f).And.LessThan(pitch), $"{kind} at {seconds}");
					double whole = Math.Floor(drift / pitch);
					Assert.That(unchecked((int)(long)whole), Is.EqualTo(steps), $"{kind} at {seconds}");
					Assert.That(whole * pitch + remainder, Is.EqualTo(drift).Within(Math.Max(1e-3, drift * 1e-12)), $"{kind} at {seconds}");
				}
			}
		}

		/// <summary>The GPU's way (slot, steps, remainder in floats) finds every float exactly where its identity and the drift in double put it.</summary>
		[Test]
		public void SplitLattice_FindsTheReferenceFloats()
		{
			Vector2 drift = new Vector2(0.8f, 0.6f).normalized;
			foreach (BiomeSeaFloatKind kind in Kinds())
			{
				BiomeSeaFloatTraits traits = BiomeSeaFloats.Traits(kind);
				foreach (double seconds in new[] { 5.0, 7200.5, 86400.0 * 30 + 0.75, 86400.0 * 365 * 20 })
				{
					double drifted = BiomeSeaFloats.Drifted(kind, seconds);
					BiomeSeaFloats.SplitDrift(drifted, traits.Spacing.x, out int steps, out float remainder);
					// Slots across a scene-sized patch of sea round the origin.
					for (int slotU = -60; slotU <= 60; slotU += 7)
					{
						for (int j = -40; j <= 40; j += 9)
						{
							bool gpu = BiomeSeaFloats.FromSlot(kind, Seed, slotU, j, 1f, drift, steps, remainder, out int identity, out Vector2 at);
							Assert.That(identity, Is.EqualTo(unchecked(slotU - steps)));
							// The reference in double: the float's own identity carried by the whole drift (k·s + D, millions of metres
							// for an old clock, exact in double).
							bool reference = BiomeSeaFloats.Reference(kind, Seed, identity, j, 1f, drift, drifted, out Vector2 expected);
							Assert.That(gpu, Is.EqualTo(reference), $"{kind} {slotU},{j}: the same float is there");
							Assert.That(Vector2.Distance(at, expected), Is.LessThan(2e-3f), $"{kind} at {seconds}, slot {slotU},{j}: in the same place");
						}
					}
				}
			}
		}

		/// <summary>No pop: as the remainder runs past a pitch the next step takes the float over in the same place, carried exactly rate·dt.</summary>
		[Test]
		public void Drift_IsContinuous_AcrossAStep()
		{
			Vector2 drift = new Vector2(0.3f, -0.95f).normalized;
			foreach (BiomeSeaFloatKind kind in Kinds())
			{
				BiomeSeaFloatTraits traits = BiomeSeaFloats.Traits(kind);
				double rate = traits.DriftMetresPerSecond;
				// A moment just before the drift reaches a whole number of pitches, and one just after.
				double crossing = Math.Ceiling(BiomeSeaFloats.Drifted(kind, 86400.0 * 9) / traits.Spacing.x) * traits.Spacing.x / rate;
				double before = crossing - 0.05, after = crossing + 0.05;
				// A float that lies in the scene at that moment (slot 50): the identities there are large, the slots small.
				BiomeSeaFloats.SplitDrift(BiomeSeaFloats.Drifted(kind, before), traits.Spacing.x, out int stepsBefore, out _);
				int identity = unchecked(50 - stepsBefore);
				const int j = -7;
				Vector2 At(double seconds)
				{
					BiomeSeaFloats.SplitDrift(BiomeSeaFloats.Drifted(kind, seconds), traits.Spacing.x, out int steps, out float remainder);
					BiomeSeaFloats.FromSlot(kind, Seed, unchecked(identity + steps), j, 1f, drift, steps, remainder, out int found, out Vector2 at);
					Assert.That(found, Is.EqualTo(identity));
					return at;
				}
				Vector2 a = At(before), b = At(after);
				Vector2 moved = b - a;
				Assert.That(Vector2.Dot(moved, drift), Is.EqualTo((float)(rate * 0.1)).Within(2e-3f), $"{kind}: carried rate·dt downwind");
				Assert.That(Mathf.Abs(Vector2.Dot(moved, BiomeSeaFloats.Across(drift))), Is.LessThan(1e-3f), $"{kind}: not across it");
			}
		}

		/// <summary>Where every float is depends on the moment alone, never on what was asked before (no state).</summary>
		[Test]
		public void Floats_AreAPureFunctionOfTheClock()
		{
			Vector2 drift = Vector2.right;
			List<Vector2> At(double seconds)
			{
				var into = new List<Vector2>();
				BiomeSeaFloatTraits t = BiomeSeaFloats.Traits(BiomeSeaFloatKind.Sargassum);
				BiomeSeaFloats.SplitDrift(BiomeSeaFloats.Drifted(BiomeSeaFloatKind.Sargassum, seconds), t.Spacing.x, out int steps, out float remainder);
				for (int u = -30; u < 30; u++)
				{
					for (int j = -60; j < 60; j++)
					{
						if (BiomeSeaFloats.FromSlot(BiomeSeaFloatKind.Sargassum, Seed, u, j, 1f, drift, steps, remainder, out _, out Vector2 at))
						{
							into.Add(at);
						}
					}
				}
				return into;
			}
			List<Vector2> first = At(100.0);
			At(5000.0);
			List<Vector2> again = At(100.0);
			Assert.That(first, Is.Not.Empty, "sargassum lies somewhere in 7 200 lattice points");
			Assert.That(again, Is.EqualTo(first));
		}

		[Test]
		public void Sargassum_LiesInWindrows_AcrossTheWind()
		{
			BiomeSeaFloatTraits t = BiomeSeaFloats.Traits(BiomeSeaFloatKind.Sargassum);
			int band = Math.Max(1, Mathf.RoundToInt(t.RowPitch / t.Spacing.y));
			int rowsWithWeed = 0, emptyBetween = 0;
			for (int j = 0; j < band * 6; j++)
			{
				float cover = 0f;
				for (int k = 0; k < 400; k++)
				{
					cover += BiomeSeaFloats.Cover(t, Seed, 0, k, j, 1f);
				}
				if (cover > 1f)
				{
					rowsWithWeed++;
				}
				else
				{
					emptyBetween++;
				}
			}
			Assert.That(rowsWithWeed, Is.GreaterThan(0));
			Assert.That(emptyBetween, Is.GreaterThan(rowsWithWeed * 3), "most of the sea between the windrows is clear");
		}

		[Test]
		public void SlotRange_CoversEveryLatticePointInTheCell()
		{
			Vector2 drift = new Vector2(-0.4f, 0.9f).normalized;
			var spacing = new Vector2(1.6f, 2.3f);
			const float remainder = 0.7f, x0 = 256f, z0 = -128f, side = 128f;
			BiomeSeaFloats.SlotRange(drift, spacing, remainder, x0, z0, side, out int fu, out int fv, out int cu, out int cv);
			int inside = 0;
			for (int u = fu - 20; u < fu + cu + 20; u++)
			{
				for (int v = fv - 20; v < fv + cv + 20; v++)
				{
					Vector2 p = BiomeSeaFloats.SlotPosition(drift, spacing, remainder, u, v);
					bool inCell = p.x >= x0 && p.x < x0 + side && p.y >= z0 && p.y < z0 + side;
					if (inCell)
					{
						inside++;
						Assert.That(u >= fu && u < fu + cu && v >= fv && v < fv + cv, Is.True, $"slot {u},{v} in the cell but outside the range");
					}
				}
			}
			Assert.That(inside, Is.EqualTo(Mathf.RoundToInt(side * side / (spacing.x * spacing.y))).Within(200));
		}

		// ── The sea's own wave sum, shared ────────────────────────────

		[Test]
		public void Swell_IsTheSeasOwnSum_SplitOutOfTheWaves()
		{
			string waves = File.ReadAllText(WaterShaders + "/FishWaterWaves.hlsl");
			string swell = File.ReadAllText(WaterShaders + "/FishWaterSwell.hlsl");
			StringAssert.Contains("#include \"FishWaterInput.hlsl\"", waves);
			Assert.That(waves.IndexOf("#include \"FishWaterInput.hlsl\""), Is.LessThan(waves.IndexOf("#include \"FishWaterSwell.hlsl\"")), "the sea's cbuffer and globals first");
			// The ocean surface sums the cascades through the shared function, then the open sea and the calm, as before.
			string displace = waves.Substring(waves.IndexOf("FishWaterSurface FishWaterDisplace("));
			StringAssert.Contains("FishWaterSwellCascades(flatPositionWS.xz, footprintMetres, displacement, slope, folding, kept);", displace);
			StringAssert.Contains("FishWaterOpenSea(flatPositionWS.xz)", displace);
			StringAssert.Contains("surface.calm = FishWaterCalm(surface.depth);", displace);
			// Each moved function is defined once, in the swell header.
			foreach (string function in new[] { "void FishWaterCascade(", "float FishWaterCalm(", "float FishWaterSeabedDepth(", "float FishWaterShoreDistance(", "void FishWaterSwellCascades(" })
			{
				Assert.That(Regex.Matches(waves, Regex.Escape(function)).Count, Is.EqualTo(0), function + " left in FishWaterWaves.hlsl");
				Assert.That(Regex.Matches(swell, Regex.Escape(function)).Count, Is.EqualTo(1), function + " in FishWaterSwell.hlsl");
			}
			// Its globals are declared only when the sea's input header is not there (one shader, one declaration).
			StringAssert.Contains("#ifndef FISHMMO_WATER_INPUT_INCLUDED", swell);
			StringAssert.DoesNotContain("CBUFFER_START(UnityPerMaterial)", swell);
			string at = swell.Substring(swell.IndexOf("float3 FishWaterSwellAt("));
			StringAssert.Contains("FishWaterSwellCascades(xz, footprintMetres, displacement, slope, folding, kept);", at);
			StringAssert.Contains("FishWaterOpenSea(xz)", at);
			StringAssert.Contains("FishWaterCalm(FishWaterSeabedDepth(xz))", at);
		}

		[Test]
		public void FloatShaders_UseDxc_RideTheSwell_AndMirrorTheCSharpHash()
		{
			string shader = File.ReadAllText(ClientShaders + "/FishSeaFloats.shader");
			string pass = File.ReadAllText(ClientShaders + "/FishSeaFloats.hlsl");
			string compute = File.ReadAllText(ClientShaders + "/FishSeaFloats.compute");
			Assert.That(Regex.Matches(shader, "HLSLPROGRAM").Count, Is.EqualTo(Regex.Matches(shader, @"#pragma use_dxc vulkan").Count), "every pass on DXC");
			StringAssert.Contains("#pragma use_dxc vulkan", compute);
			StringAssert.DoesNotContain("FishIndirectInstancing", pass);
			StringAssert.Contains("FishWaterSwell.hlsl", pass);
			StringAssert.DoesNotContain("FishWaterInput.hlsl", pass);
			StringAssert.Contains("FishWaterSwellAt(", pass);
			// The compute's hash and noise constants are BiomeSeaFloats'.
			foreach (string constant in new[] { "0x7feb352du", "0x846ca68bu", "0x9E3779B1u", "0x85EBCA6Bu", "0xC2B2AE35u", "0x27D4EB2Fu", "0x5EAF1047u", "0x0A0Bu", "0x77u" })
			{
				StringAssert.Contains(constant, compute, constant);
			}
			string sea = File.ReadAllText(ClientShaders + "/FishSeaLife.hlsl");
			StringAssert.Contains("FishWaterSwell.hlsl", sea);
			StringAssert.DoesNotContain("FishWaterInput.hlsl", sea);
		}

		// ── Adrift sites ──────────────────────────────────────────────

		private static readonly POIType[] AdriftKinds = { POIType.AdriftDerelict, POIType.WhaleFall, POIType.BarnacleLog, POIType.Longline, POIType.WhaleTow };

		[Test]
		public void AdriftKinds_AreAfloat_Offshore_HostedByNoBiomeYet_AndNamed()
		{
			PointOfInterestRules rules = PointOfInterestRules.Default();
			foreach (POIType kind in AdriftKinds)
			{
				PointOfInterestKindRule rule = rules.RuleFor(kind);
				Assert.IsNotNull(rule, kind.ToString());
				Assert.IsTrue(rule.Underwater && rule.Afloat, kind.ToString());
				Assert.AreEqual(50f, rule.MinDepth, kind.ToString());
				Assert.AreEqual(1000f, rule.OffshoreMetres, kind.ToString());
				Assert.AreEqual(0f, rule.DefaultBiomeWeight, kind + ": no biome hosts it until its round, so no scene changes");
				Assert.IsFalse(PointOfInterestKinds.Info(kind).Has(PointOfInterestTraits.Underwater), kind + ": on the water, not under it");
				Assert.AreEqual(kind.ToString(), ((MapMarkerType)Enum.Parse(typeof(MapMarkerType), kind.ToString())).ToString());
				Assert.GreaterOrEqual(PlaceNameDefaults.TypeWordsFor(kind).Length, 6, kind.ToString());
			}
			foreach (string biome in new[] { "Ocean", "Deep Ocean", "Coastal Water", "Grassland" })
			{
				foreach ((POIType kind, float weight) in rules.KindWeightsFor(biome))
				{
					Assert.IsFalse(Array.IndexOf(AdriftKinds, kind) >= 0 && weight > 0f, $"{biome} hosts {kind} before its round");
				}
			}
			// Appended, never inserted: the ordinals are serialized.
			Assert.AreEqual((int)POIType.SunkenStatue + 1, (int)POIType.AdriftDerelict);
			Assert.AreEqual((int)MapMarkerType.SunkenStatue + 1, (int)MapMarkerType.AdriftDerelict);
		}

		[Test]
		public void AdriftDerelict_IsMooredWithNoFloor_InTheHarboursWay()
		{
			string source = File.ReadAllText("Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/PointsOfInterest/Features/PointOfInterestAdrift.cs");
			StringAssert.Contains("Floor = float.NegativeInfinity", source);
			StringAssert.Contains("PointOfInterestHarbour.Moor(", source);
			StringAssert.Contains("AddComponent<FloatingBoats>().Configure(", source);
			string planner = File.ReadAllText("Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/PointsOfInterest/PointOfInterestPlanner.Placement.cs");
			StringAssert.Contains("rule.OffshoreMetres > 0f && (!Input.HasSea || G.LandDistance[i] < rule.OffshoreMetres)", planner);
			StringAssert.Contains("site.Y = Input.SeaLevel;", planner);
		}
	}
}
