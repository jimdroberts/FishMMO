using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FishMMO.Shared;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.Weather;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Phase I2q-b (2026-10-25, decisions 53, 66, 84, 90, 93, 95): vent layouts along the scene's stored lines, vent timings on the
	/// shared clock, the new geothermal kinds, a template's own site rate, line heat, the thin-air lean, the melt look, and that every
	/// template keeps today's vents unless its round names otherwise. Pure: no Unity object is made.
	/// </summary>
	public class GeothermalVentLayoutTests
	{
		private const string BiomeFolder = "Assets/Templates/Entity/Biomes";

		/// <summary>The rounds' vent columns (decisions 53, 66, 90, 93, 95): biome → layout/timing signature.</summary>
		private static readonly Dictionary<string, string> NamedVents = new Dictionary<string, string>
		{
			{ "Ice Geyser Field", new BiomeVents(BiomeVentLayout.Fissures, BiomeVentTiming.PlumeTide).Signature() },
			{ "Nitrogen Ice Field", new BiomeVents(BiomeVentLayout.Grid, BiomeVentTiming.Solar, 4).Signature() },
			{ "Volcanic Temple", new BiomeVents(BiomeVentLayout.Terrace, BiomeVentTiming.Strombolian).Signature() },
			{ "Volcanic", new BiomeVents(BiomeVentLayout.Fissures, BiomeVentTiming.Episode).Signature() },
			{ "Tidal Fracture", new BiomeVents(BiomeVentLayout.Ridges, BiomeVentTiming.Kneading).Signature() },
		};

		private static VentSky Sky(double seconds, float orbitTurn = 0.37f, float sunHeight = 0.5f)
		{
			var glow = SceneGlowSky.Calm;
			glow.OrbitTurn = orbitTurn;
			glow.SunHeight = sunHeight;
			return VentSky.Of(seconds, glow);
		}

		private static readonly BiomeVentTiming[] Timings = (BiomeVentTiming[])Enum.GetValues(typeof(BiomeVentTiming));

		[Test]
		public void TheEnums_AreAppendOnly()
		{
			Assert.That((int)GeothermalKind.MudPot, Is.EqualTo(3));
			Assert.That((int)GeothermalKind.Jet, Is.EqualTo(4));
			Assert.That((int)GeothermalKind.SulphurPool, Is.EqualTo(5), "decision 97: landing order");
			Assert.That((int)GeothermalKind.Smoker, Is.EqualTo(6), "decision 97: the next free value");
			Assert.That((int)BiomeVentLayout.Grid, Is.EqualTo(0));
			Assert.That((int)BiomeVentLayout.Ridges, Is.EqualTo(3));
			Assert.That((int)BiomeVentTiming.Steady, Is.EqualTo(0));
			Assert.That((int)BiomeVentTiming.Kneading, Is.EqualTo(5));
		}

		[Test]
		public void EveryTiming_IsPureInTheClock_InRange_AndSteadyIsOne()
		{
			var episodes = default(BiomeFountainEpisodes);
			for (int i = 0; i < 400; i++)
			{
				double t = 1.7e9 + i * 137.31;
				foreach (BiomeVentTiming timing in Timings)
				{
					VentSky sky = Sky(t, (i * 0.0137f) % 1f, Mathf.Sin(i * 0.05f));
					float a = VentTiming.Strength(timing, sky, 12345u + (uint)i, 0.7f, episodes);
					float b = VentTiming.Strength(timing, Sky(t, (i * 0.0137f) % 1f, Mathf.Sin(i * 0.05f)), 12345u + (uint)i, 0.7f, episodes);
					Assert.That(a, Is.EqualTo(b), timing + " is pure");
					Assert.That(a, Is.InRange(0f, 1f), timing.ToString());
				}
				Assert.That(VentTiming.Strength(BiomeVentTiming.Steady, Sky(t), 7u, 0f, episodes), Is.EqualTo(1f));
			}
		}

		[Test]
		public void EveryTiming_IsContinuousAcrossTheWrap()
		{
			// A whole number of wraps of the motion clock, so the wrapped clock jumps 10 000 → 0 there.
			double wrap = Math.Round(1.7e9 / 10000.0) * 10000.0;
			var episodes = default(BiomeFountainEpisodes);
			foreach (BiomeVentTiming timing in Timings)
			{
				for (uint seed = 1; seed < 400; seed++)
				{
					float before = VentTiming.Strength(timing, Sky(wrap - 0.01), seed * 2654435761u, 1f, episodes);
					float after = VentTiming.Strength(timing, Sky(wrap + 0.01), seed * 2654435761u, 1f, episodes);
					Assert.That(Mathf.Abs(after - before), Is.LessThan(0.02f), $"{timing} seed {seed}");
				}
			}
		}

		[Test]
		public void EveryTiming_MovesSmoothly_NeverJumping()
		{
			var episodes = default(BiomeFountainEpisodes);
			foreach (BiomeVentTiming timing in Timings)
			{
				for (uint seed = 1; seed < 6; seed++)
				{
					double t0 = 1.7e9 + seed * 777.0;
					float last = VentTiming.Strength(timing, Sky(t0), seed, 0.3f, episodes);
					// Every 0.05 s for 2.5 hours: the steepest ramp is a 1.5 s burst (a share of 0.92 over 1.5 s ⇒ < 0.05 a step).
					for (int k = 1; k < 180000; k++)
					{
						float now = VentTiming.Strength(timing, Sky(t0 + k * 0.05), seed, 0.3f, episodes);
						Assert.That(Mathf.Abs(now - last), Is.LessThan(0.06f), $"{timing} seed {seed} at step {k}");
						last = now;
					}
				}
			}
		}

		[Test]
		public void ThePlumeTide_PeaksAtApocentre_AndTheGreatPlumeNeverFallsUnder04()
		{
			Assert.That(SceneGlowModes.PlumeTideOf(0f), Is.EqualTo(0.3f).Within(1e-5f));
			Assert.That(SceneGlowModes.PlumeTideOf(0.5f), Is.EqualTo(1f).Within(1e-5f));
			var episodes = default(BiomeFountainEpisodes);
			bool everOff = false;
			for (int i = 0; i < 2000; i++)
			{
				VentSky sky = Sky(1.7e9 + i * 31.7, 0f);
				float jet = VentTiming.Strength(BiomeVentTiming.PlumeTide, sky, 99u, 0f, episodes);
				float great = VentTiming.Strength(BiomeVentTiming.PlumeTide, sky, 99u, 0f, episodes, true);
				Assert.That(jet, Is.LessThanOrEqualTo(0.3f + 1e-4f), "at pericentre a jet is at most the tide's 0.3");
				Assert.That(great, Is.GreaterThanOrEqualTo(VentTiming.GreatPlumeFloor));
				everOff |= jet < 0.01f;
			}
			Assert.That(everOff, Is.True, "a jet's flicker turns it off between its spans");
			Assert.That(VentTiming.FlickerSlots.All(l => 10000 % l == 0), Is.True, "every flicker slot divides the wrap");
		}

		[Test]
		public void Strombolian_IsI6isBurst_AndEpisode_IsTheFountainSlots_AndSolar_SleepsAtNight()
		{
			var episodes = default(BiomeFountainEpisodes);
			for (int i = 0; i < 500; i++)
			{
				VentSky sky = Sky(1.7e9 + i * 3.3);
				Assert.That(VentTiming.Strength(BiomeVentTiming.Strombolian, sky, 42u, 0f, episodes),
					Is.EqualTo(Mathf.Max(VentTiming.BurstQuiet, VentBursts.At(sky.Wrapped, 42u))));
				Assert.That(VentTiming.Strength(BiomeVentTiming.Episode, sky, 42u, 0f, episodes),
					Is.EqualTo(VentTiming.EpisodeQuiet + (1f - VentTiming.EpisodeQuiet) * BiomeFountainEpisodes.At(BiomeFountainEpisodes.Volcanic, sky.Wrapped, 42u)));
				Assert.That(VentTiming.Strength(BiomeVentTiming.Solar, Sky(1.7e9 + i * 3600.0, 0f, -0.2f), 42u, 0f, episodes), Is.EqualTo(0f), "no sun, no plume");
			}
		}

		[Test]
		public void Kneading_FollowsTheStressAcrossTheCrack_AsTheGlowDoes()
		{
			var episodes = default(BiomeFountainEpisodes);
			for (int i = 0; i < 64; i++)
			{
				VentSky sky = Sky(1.7e9, i / 64f);
				var glow = SceneGlowSky.Calm;
				glow.OrbitTurn = i / 64f;
				float x = 137f * i, z = -91f * i;
				float vent = VentTiming.Strength(BiomeVentTiming.Kneading, sky, 1u, VentTiming.FieldBearing(x, z), episodes);
				float glowing = SceneGlowModes.Value(SceneGlowMode.Kneading, 0, default, 0f, x, z, glow);
				Assert.That(vent, Is.EqualTo(glowing).Within(1e-4f), "a grid vent and the glow beside it knead as one");
			}
		}

		// ── Layouts ──

		private static GeothermalGround Hot(BiomeVentLayout layout, bool lined = true, bool allKinds = false)
		{
			return new GeothermalGround
			{
				Chance = 0.5f,
				AllKinds = allKinds,
				Shares = new BiomeGeothermalShares(0f, 0.15f, 0.6f, 0.25f),
				Vents = new BiomeVents(layout, BiomeVentTiming.Steady),
				BiomeID = 7,
				Lined = lined,
			};
		}

		private static List<VentLine> StraightLine(BiomeVentLayout layout, float length = 2000f)
		{
			// One line in two segments, as a landform stores it (along 0 at its start).
			return new List<VentLine>
			{
				new VentLine { A = new Vector2(0f, 0f), B = new Vector2(0.4f * length, 0f), AlongA = 0f, LineLength = length, Half = 20f, Seed = 77u, BiomeID = 7, Layout = layout },
				new VentLine { A = new Vector2(0.4f * length, 0f), B = new Vector2(length, 0f), AlongA = 0.4f * length, LineLength = length, Half = 20f, Seed = 77u, BiomeID = 7, Layout = layout },
			};
		}

		private static readonly Rect Area = new Rect(-5000f, -5000f, 10000f, 10000f);

		[Test]
		public void Fissures_LayAJetEvery40To80Metres_Deterministically_NeverDoubledWhereSegmentsMeet()
		{
			List<VentLine> lines = StraightLine(BiomeVentLayout.Fissures);
			var a = new List<GeothermalSite>();
			var b = new List<GeothermalSite>();
			GeothermalVents.FindAlong(lines, Area, 5u, p => Hot(BiomeVentLayout.Fissures), a, int.MaxValue);
			GeothermalVents.FindAlong(lines, Area, 5u, p => Hot(BiomeVentLayout.Fissures), b, int.MaxValue);
			Assert.That(a.Count, Is.EqualTo(b.Count));
			for (int i = 0; i < a.Count; i++)
			{
				Assert.That(a[i].Position, Is.EqualTo(b[i].Position));
				Assert.That(a[i].Seed, Is.EqualTo(b[i].Seed));
				Assert.That(a[i].Kind, Is.EqualTo(GeothermalKind.Jet));
				Assert.That(a[i].OnLine, Is.True);
				Assert.That(Mathf.Abs(a[i].Position.y), Is.LessThanOrEqualTo(GeothermalVents.AcrossScatter * 20f + 1e-3f), "on its fissure");
			}
			// 2000 m at 60 m: 33 or so slots, every one held.
			Assert.That(a.Count, Is.InRange(31, 35));
			List<float> xs = a.Select(s => s.Position.x).OrderBy(x => x).ToList();
			for (int i = 1; i < xs.Count; i++)
			{
				Assert.That(xs[i] - xs[i - 1], Is.InRange(0.4f * GeothermalVents.FissureSpacing - 1e-3f, 1.6f * GeothermalVents.FissureSpacing + 1e-3f), "spacing");
			}
			// Another scene's seed lays them elsewhere.
			var c = new List<GeothermalSite>();
			GeothermalVents.FindAlong(lines, Area, 6u, p => Hot(BiomeVentLayout.Fissures), c, int.MaxValue);
			Assert.That(c.Select(s => s.Position).SequenceEqual(a.Select(s => s.Position)), Is.False);
		}

		[Test]
		public void Lines_LayNothing_WhereTheGroundIsNotLaidOnLines_OrIsCold()
		{
			List<VentLine> lines = StraightLine(BiomeVentLayout.Fissures);
			var sites = new List<GeothermalSite>();
			GeothermalVents.FindAlong(lines, Area, 5u, p => Hot(BiomeVentLayout.Fissures, lined: false), sites, int.MaxValue);
			Assert.That(sites, Is.Empty, "a biome not laid on lines keeps its grid");
			GeothermalVents.FindAlong(lines, Area, 5u, p => Hot(BiomeVentLayout.Grid), sites, int.MaxValue);
			Assert.That(sites, Is.Empty);
			GeothermalVents.FindAlong(lines, Area, 5u, p => GeothermalGround.None, sites, int.MaxValue);
			Assert.That(sites, Is.Empty);
			GeothermalVents.FindAlong(null, Area, 5u, p => Hot(BiomeVentLayout.Fissures), sites, int.MaxValue);
			Assert.That(sites, Is.Empty);
		}

		[Test]
		public void TheGrid_SkipsALinedBiome_AndIsAsBeforeForEveryOther()
		{
			var before = new List<GeothermalSite>();
			var unlined = new List<GeothermalSite>();
			var lined = new List<GeothermalSite>();
			GeothermalGround today = Hot(BiomeVentLayout.Grid, lined: false);
			today.Vents = BiomeVents.Today;
			GeothermalVents.Find(Vector2.zero, new Rect(0f, 0f, 2000f, 2000f), 9u, p => today, before, GeothermalVents.CellMetres, int.MaxValue);
			GeothermalVents.Find(Vector2.zero, new Rect(0f, 0f, 2000f, 2000f), 9u, p => Hot(BiomeVentLayout.Fissures, lined: false), unlined, GeothermalVents.CellMetres, int.MaxValue);
			GeothermalVents.Find(Vector2.zero, new Rect(0f, 0f, 2000f, 2000f), 9u, p => Hot(BiomeVentLayout.Fissures, lined: true), lined, GeothermalVents.CellMetres, int.MaxValue);
			Assert.That(before.Count, Is.GreaterThan(100));
			Assert.That(unlined.Select(s => (s.Position, s.Kind, s.Size, s.Seed)).SequenceEqual(before.Select(s => (s.Position, s.Kind, s.Size, s.Seed))), Is.True,
				"no lines stored: the grid as before");
			Assert.That(lined, Is.Empty, "lines stored: no grid site");
		}

		[Test]
		public void Ridges_AreSparser_AndTerraces_ClusterInThreeZones()
		{
			var ridges = new List<GeothermalSite>();
			GeothermalVents.FindAlong(StraightLine(BiomeVentLayout.Ridges, 6000f), Area, 5u, p => Hot(BiomeVentLayout.Ridges), ridges, int.MaxValue);
			Assert.That(ridges.Count, Is.InRange(10, 26), "six in ten of 30 slots");
			Assert.That(ridges.All(s => s.Kind == GeothermalKind.Jet && Mathf.Abs(s.Bearing) < 1e-4f), Is.True, "a jet with its line's bearing");

			var terrace = new List<GeothermalSite>();
			GeothermalVents.FindAlong(StraightLine(BiomeVentLayout.Terrace, 1000f), Area, 5u, p => Hot(BiomeVentLayout.Terrace, allKinds: true), terrace, int.MaxValue);
			Assert.That(terrace.Count, Is.InRange(3, 9));
			foreach (GeothermalSite s in terrace)
			{
				float nearest = new[] { 250f, 500f, 750f }.Min(z => Mathf.Abs(s.Position.x - z));
				Assert.That(nearest, Is.LessThanOrEqualTo(GeothermalVents.TerraceSpread + 1e-3f), "in a zone");
				Assert.That(s.Kind, Is.Not.EqualTo(GeothermalKind.Geyser), "of its ground's split (no geysers)");
			}
			var pad = new List<GeothermalSite>();
			var padLine = new List<VentLine> { new VentLine { A = new Vector2(30f, 40f), B = new Vector2(30f, 40f), Half = 50f, Seed = 3u, BiomeID = 7, Layout = BiomeVentLayout.Terrace } };
			GeothermalVents.FindAlong(padLine, Area, 5u, p => Hot(BiomeVentLayout.Terrace), pad, int.MaxValue);
			Assert.That(pad.Count, Is.InRange(1, GeothermalVents.TerraceVents));
			Assert.That(pad.All(s => (s.Position - new Vector2(30f, 40f)).magnitude <= 25f), Is.True);
		}

		[Test]
		public void TheLines_ComeFromTheLandformsOfLinedBiomes_InOrder()
		{
			var segs = new List<BiomeLandformSegment>
			{
				new BiomeLandformSegment(new Vector2(0f, 0f), new Vector2(100f, 0f), 15f, 10f, 0f, 200f),
				new BiomeLandformSegment(new Vector2(100f, 0f), new Vector2(200f, 0f), 15f, 10f, 100f, 200f),
				new BiomeLandformSegment(new Vector2(0f, 350f), new Vector2(200f, 350f), 15f, 10f, 0f, 200f),
			};
			var sites = new List<BiomeLandformSite>
			{
				new BiomeLandformSite { BiomeID = 7, Seed = 11u, Segments = segs },
				new BiomeLandformSite { BiomeID = 8, Seed = 12u, Segments = segs },
			};
			var pads = new List<BiomeLandformPad> { new BiomeLandformPad { BiomeID = 9, Centre = new Vector2(5f, 5f), Radius = 30f }, new BiomeLandformPad { BiomeID = 7, Centre = Vector2.one, Radius = 30f } };
			BiomeVentLayout Of(int id) => id == 7 ? BiomeVentLayout.Fissures : id == 9 ? BiomeVentLayout.Terrace : BiomeVentLayout.Grid;
			List<VentLine> lines = IceStripes.FromLandforms(sites, pads, Of);
			Assert.That(lines.Count, Is.EqualTo(4), "biome 7's three segments and biome 9's pad; biome 8 on the grid, a Fissures pad none");
			Assert.That(lines[0].Seed, Is.EqualTo(lines[1].Seed), "one line, one seed");
			Assert.That(lines[2].Seed, Is.Not.EqualTo(lines[0].Seed), "the parallel line its own");
			Assert.That(lines[3].IsPad && lines[3].Layout == BiomeVentLayout.Terrace, Is.True);
			Assert.That(IceStripes.HasBiome(lines, 7) && IceStripes.HasBiome(lines, 9) && !IceStripes.HasBiome(lines, 8), Is.True);
			Assert.That(IceStripes.FromLandforms(sites, pads, id => BiomeVentLayout.Grid), Is.Empty, "every biome on the grid: no lines, as before");
		}

		[Test]
		public void TheHeatMask_TakesTheLines()
		{
			List<VentLine> lines = StraightLine(BiomeVentLayout.Fissures, 1000f);
			List<BiomeHeatSource> heat = IceStripes.Heat(lines);
			Assert.That(heat.All(h => h.Shape == BiomeHeatSourceShape.Line), Is.True);
			BiomeHeatMask mask = BiomeHeatMask.Bake(heat, new Rect(-2000f, -2000f, 4000f, 4000f));
			float reach = Mathf.Clamp(IceStripes.ReachPerHalf * 20f, IceStripes.LeastReach, IceStripes.MostReach);
			Assert.That(mask.At(500f, 0f), Is.GreaterThan(0.95f), "the stripe floor is hot");
			Assert.That(mask.At(500f, 0.5f * reach), Is.InRange(0.15f, 0.4f), "(1 − ½)²: a quarter, half way out");
			Assert.That(mask.At(500f, reach + 10f), Is.EqualTo(0f));
		}

		[Test]
		public void KindOf_OldSplitsAreTheOldComparison_AndNewKindsTakeTheirShares()
		{
			var basin = new BiomeGeothermalShares(0.25f, 0.40f, 0.20f, 0.15f);
			// Serialized shares and the production thresholds are single precision. Store the
			// legacy thresholds too: Mono may otherwise retain excess precision in the comparison.
			float total = basin.Total;
			float[] thresholds = { basin.Geyser / total, (basin.Geyser + basin.HotSpring) / total, (basin.Geyser + basin.HotSpring + basin.Fumarole) / total };
			for (int i = 0; i < 10000; i++)
			{
				float k = i / 10000f;
				GeothermalKind old = k < thresholds[0] ? GeothermalKind.Geyser : k < thresholds[1] ? GeothermalKind.HotSpring
					: k >= thresholds[2] ? GeothermalKind.MudPot : GeothermalKind.Fumarole;
				Assert.That(GeothermalVents.KindOf(k, basin), Is.EqualTo(old), $"draw {k:R}, total {total:R}");
				// A rate and a tint are not kinds: the split still falls as it did.
				Assert.That(GeothermalVents.KindOf(k, basin.WithRate(0.3f, Color.yellow)), Is.EqualTo(old));
			}
			var flats = new BiomeGeothermalShares(0f, 0f, 0.7f, 0f).WithMore(0f, 0.3f).WithRate(0.35f);
			var counts = new int[Enum.GetValues(typeof(GeothermalKind)).Length];
			const int n = 20000;
			for (int i = 0; i < n; i++)
			{
				counts[(int)GeothermalVents.KindOf((i + 0.5f) / n, flats)]++;
			}
			Assert.That(counts[(int)GeothermalKind.Fumarole] / (float)n, Is.EqualTo(0.7f).Within(0.001f));
			Assert.That(counts[(int)GeothermalKind.SulphurPool] / (float)n, Is.EqualTo(0.3f).Within(0.001f));
			Assert.That(GeothermalVents.KindOf(0.9999999f, new BiomeGeothermalShares(0f, 0f, 0.5f, 0f).WithMore(0.5f, 0f)), Is.EqualTo(GeothermalKind.Jet));
		}

		[Test]
		public void TheNewKinds_HaveTheirGround_AndPoolsAreDugLikeSprings()
		{
			foreach (GeothermalKind kind in new[] { GeothermalKind.Jet, GeothermalKind.Smoker, GeothermalKind.SulphurPool })
			{
				var site = new GeothermalSite { Kind = kind, Size = 0.5f };
				Assert.That(GeothermalVents.SourceRadius(site), Is.GreaterThan(0f), kind.ToString());
				Assert.That(GeothermalVents.HeatReach(site), Is.GreaterThan(GeothermalVents.SourceRadius(site)), kind.ToString());
				Assert.That(GeothermalVents.HeatPeak(site), Is.InRange(0.5f, 1f), kind.ToString());
			}
			Assert.That(GeothermalVents.SourceRadius(new GeothermalSite { Kind = GeothermalKind.SulphurPool, Size = 0f }) * 2f, Is.EqualTo(3f).Within(1e-4f), "basin 3 m");
			Assert.That(GeothermalVents.SourceRadius(new GeothermalSite { Kind = GeothermalKind.SulphurPool, Size = 1f }) * 2f, Is.EqualTo(12f).Within(1e-4f), "… to 12 m");
			Assert.That(GeothermalVents.IsPool(GeothermalKind.SulphurPool) && GeothermalVents.IsPool(GeothermalKind.HotSpring) && !GeothermalVents.IsPool(GeothermalKind.Jet), Is.True);
			var pool = new GeothermalGroundCarve.Dug { Site = new GeothermalSite { Kind = GeothermalKind.SulphurPool, Size = 0.5f, Position = new Vector2(10f, 10f) }, Base = 5f };
			Assert.That(GeothermalGroundCarve.Offset(pool, 10f, 10f, 5f), Is.LessThan(-0.5f), "a dug pan");
			Assert.That(GeothermalGroundCarve.PoolLake(pool), Is.Not.Null, "flooded as a pool lake");
		}

		[Test]
		public void TheGreatPlume_IsTheLargestJetOnALine()
		{
			var sites = new List<GeothermalSite>
			{
				new GeothermalSite { Kind = GeothermalKind.Jet, Size = 0.99f, Seed = 1, OnLine = false },
				new GeothermalSite { Kind = GeothermalKind.Jet, Size = 0.8f, Seed = 9, OnLine = true },
				new GeothermalSite { Kind = GeothermalKind.Jet, Size = 0.8f, Seed = 4, OnLine = true },
				new GeothermalSite { Kind = GeothermalKind.Fumarole, Size = 1f, Seed = 2, OnLine = true },
			};
			Assert.That(GeothermalVents.GreatPlumeIndex(sites), Is.EqualTo(2));
			Assert.That(GeothermalVents.GreatPlumeIndex(sites, i => i != 2), Is.EqualTo(1));
			Assert.That(GeothermalVents.GreatPlumeIndex(new List<GeothermalSite>()), Is.EqualTo(-1));
		}

		[Test]
		public void ThinAirPlumes_LeanDownwind_ThickAirAsBefore()
		{
			var plume = new VolcanicPlume.Plume { Top = 8000f, TopOffset = new Vector2(100f, 0f), Wind = new Vector2(10f, 0f) };
			Assert.That(VolcanicPlume.DrawnTopOffset(plume, 1f), Is.EqualTo(plume.TopOffset), "thick air: its own lean");
			Assert.That(VolcanicPlume.DrawnTopOffset(plume, 0.05f), Is.EqualTo(plume.TopOffset));
			Vector2 thin = VolcanicPlume.DrawnTopOffset(plume, 1.4e-5f);
			Assert.That(thin.x, Is.GreaterThan(10000f), "Triton's: lying over downwind");
			Assert.That(Mathf.Abs(thin.y), Is.LessThan(1e-3f));
			plume.Wind = Vector2.zero;
			Assert.That(VolcanicPlume.DrawnTopOffset(plume, 1.4e-5f), Is.EqualTo(plume.TopOffset), "calm: no way to lean");
		}

		[Test]
		public void TheMarks_HoldForEveryOldValue()
		{
			Assert.That(BiomeVents.Today.IsToday, Is.True);
			Assert.That(BiomeVents.Today.Signature(), Is.EqualTo("Grid/Steady"));
			Assert.That(BiomeGeothermalShares.Today.Signature(), Is.EqualTo("g2500|s4000|f3500|m0"), "today's split signs as it did");
			Assert.That(BiomeGeothermalShares.Today.WithRate(0.35f).IsToday, Is.True, "a rate is not a kind");
			Assert.That(BiomeGeothermalShares.Today.WithMore(0.1f, 0f).IsToday, Is.False);
			StringAssert.Contains("|r3500", BiomeGeothermalShares.Today.WithRate(0.35f).Signature());
			Assert.That(BiomeWaterLooks.Today.Signature().Contains("melt"), Is.False);
			BiomeWaterLook melt = BiomeWaterLooks.Today;
			melt.Melt = true;
			Assert.That(melt.Approximately(BiomeWaterLooks.Today), Is.False);
			StringAssert.EndsWith("melt|", melt.Signature());
		}

		[Test]
		public void Defaults_AreTodays_OnEverySpec_AndEveryTemplate()
		{
			Assert.That(BiomeArtSpec.Entries.Length, Is.EqualTo(83));
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				bool named = NamedVents.TryGetValue(entry.Biome, out string vents);
				Assert.That(entry.VentPlan.HasValue, Is.EqualTo(named), entry.Biome);
				if (named)
				{
					Assert.That(entry.VentPlan.Value.Signature(), Is.EqualTo(vents), entry.Biome);
				}
				Assert.That(entry.WaterLook().Melt, Is.EqualTo(entry.Biome == "Sulphur Flats"), entry.Biome);
				BiomeGeothermalShares shares = entry.GeothermalShares ?? BiomeGeothermalShares.Today;
				Assert.That(shares.OwnRate, Is.EqualTo(entry.Biome == "Sulphur Flats" || entry.Biome == "Volcanic Temple"), entry.Biome);
			}
			Assert.That(BiomeArtSpec.For("Sulphur Flats").GeothermalShares.Value.Rate, Is.EqualTo(0.35f));
			Assert.That(BiomeArtSpec.For("Ice Geyser Field").Emission, Is.EqualTo(("Plume Frost", 0.15f)));
			Assert.That(BiomeArtSpec.For("Volcanic").Emission, Is.EqualTo(("Vog", 0.12f)));
			int templates = 0;
			foreach (string path in Directory.GetFiles(BiomeFolder, "*.asset"))
			{
				string text = File.ReadAllText(path);
				if (!text.Contains("m_Script: {fileID: 11500000, guid: b10de7e3a4c14e1f8a9d5c2b6f7e8a01"))
				{
					continue;
				}
				templates++;
				string name = Path.GetFileNameWithoutExtension(path);
				Match block = Regex.Match(text, @"\n  vents:\n    Layout: (\d+)\n    Timing: (\d+)\n    MaxPlumes: (\d+)");
				if (block.Success)
				{
					var stored = new BiomeVents((BiomeVentLayout)int.Parse(block.Groups[1].Value), (BiomeVentTiming)int.Parse(block.Groups[2].Value), int.Parse(block.Groups[3].Value));
					// Today's until Author biome art writes a round's column; then exactly the column.
					Assert.That(stored.IsToday || (NamedVents.TryGetValue(name, out string vents) && stored.Signature() == vents), Is.True, path);
				}
				else
				{
					Assert.That(text.Contains("\n  vents:"), Is.False, path + ": vents in an unexpected shape");
				}
				// No site rate, jet or sulphur pool share anywhere but the rounds that name one.
				bool rated = name == "Sulphur Flats" || name == "Volcanic Temple";
				Assert.That(Regex.IsMatch(text, @"\n    Rate: 0\.[0-9]*[1-9]"), Is.False.Or.EqualTo(rated), path);
				Assert.That(Regex.IsMatch(text, @"\n    SulphurPool: 0\.[0-9]*[1-9]"), Is.False.Or.EqualTo(name == "Sulphur Flats"), path);
				Assert.That(Regex.IsMatch(text, @"\n    Jet: 0\.[0-9]*[1-9]"), Is.False, path);
				Assert.That(Regex.IsMatch(text, @"\n    Melt: 1"), Is.False.Or.EqualTo(name == "Sulphur Flats"), path);
			}
			Assert.That(templates, Is.EqualTo(83));
		}

		[Test]
		public void TheContentGenerator_ShipsTheRoundsSubstances_AndTheVentedFlag()
		{
			string content = File.ReadAllText("Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/WeatherContentGenerator.cs");
			foreach (string name in new[] { "Plume Frost", "Plume Soot", "Acid Fog", "Kiln Haze", "Vog", "Methane Mist" })
			{
				StringAssert.Contains("\"" + name + "\"", content, name);
			}
			StringAssert.Contains("With(Vented(Grain(\"Volcanic Ash\"", content, "the vented flag the assets carried by hand is the generator's now");
			StringAssert.Contains("With(Vented(Grain(\"Cryo Tephra\"", content);
			StringAssert.Contains("substance.Vented = spec.Vented;", content);
			// Only Volcanic Ash and Cryo Tephra carried it before: the refresh changes no existing substance's flag.
			foreach (string path in Directory.GetFiles("Assets/Templates/Weather/Substances", "*.asset"))
			{
				bool vented = File.ReadAllText(path).Contains("\n  Vented: 1");
				string name = Path.GetFileNameWithoutExtension(path);
				Assert.That(vented, Is.EqualTo(name == "Volcanic Ash" || name == "Cryo Tephra" || name == "Plume Frost" || name == "Plume Soot" || name == "Vog"), path);
			}
		}

		[Test]
		public void TheCutAndTheClient_ReadTheSameLines()
		{
			string carve = File.ReadAllText("Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/Terrain/GeothermalGroundCarve.cs");
			StringAssert.Contains("map.SetVentLines(lines);\n\t\t\t\tGeothermalVents.SceneSites(map, sites);", carve.Replace("\r", string.Empty), "the temp map carries the lines before the search");
			StringAssert.Contains("springHeat.AddRange(IceStripes.Heat(lines));", carve);
			string generator = File.ReadAllText("Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/SceneGenerator.cs");
			StringAssert.Contains("map.SetVentLines(hotGround.Lines);", generator);
			string vents = File.ReadAllText("Assets/Scripts/Shared/Implementation/Weather/GeothermalVents.cs");
			StringAssert.Contains("FindAlong(lines, scene, SeedOf(map)", vents);
			StringAssert.Contains("FindAlong(lines, near, SeedOf(map)", vents);
		}
	}
}
