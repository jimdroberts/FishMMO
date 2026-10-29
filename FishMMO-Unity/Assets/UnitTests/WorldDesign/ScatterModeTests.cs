using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Phase SCATTER-MODES of the biome suite (2026-10-26, decision 34): scatter modes on spawn rules (facing, lean, anchors, patterns,
	/// power-law sizes). Off (the default on every rule, and every spec entry until a round names one) nothing changes: no mark, no
	/// fingerprint, no weight, no mode words (so the GPU detail scatter and the client's tree renderer draw exactly what they drew). On,
	/// each pure function is deterministic, and the words the GPU and the client read mirror the C# arithmetic.
	/// </summary>
	public class ScatterModeTests
	{
		private const string Terrain = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/Terrain";
		private const string World = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World";
		private const string Art = World + "/ProceduralArt";
		private const string Client = "Assets/Scripts/Client/World/Terrain";
		private const string Compute = "Assets/Prefabs/Client/Weather/Shaders/FishDetailScatter.compute";
		private const string ModeFile = "Assets/Scripts/Shared/Implementation/Tools/Biomes/Terrain/BiomeScatterMode.cs";

		private static string Read(string relative) => File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), relative)).Replace("\r\n", "\n");

		private static IEnumerable<BiomeScatterMode> NamedModes()
		{
			yield return BiomeScatterMode.LeeDrift();
			yield return BiomeScatterMode.DownslopeAligned();
			yield return BiomeScatterMode.AlongLandformLine();
			yield return BiomeScatterMode.AlongStrike();
			yield return BiomeScatterMode.MeridianLean();
			yield return BiomeScatterMode.PlasmaFacing();
			yield return BiomeScatterMode.Nursed("Mesquite");
			yield return BiomeScatterMode.UnderShrub("Saltbush");
			yield return BiomeScatterMode.EvenSpacing(4f);
			yield return BiomeScatterMode.LeanSunward();
			yield return BiomeScatterMode.PowerLaw();
			yield return BiomeScatterMode.LaidFlat();
			yield return BiomeScatterMode.CliffFoot();
			yield return BiomeScatterMode.WallMounted();
			yield return BiomeScatterMode.SeepLine();
			yield return BiomeScatterMode.RainCellClusters();
			yield return BiomeScatterMode.FairyRings();
			yield return BiomeScatterMode.ClusteredFields();
			yield return BiomeScatterMode.NearSites("Village");
		}

		private static readonly BiomeScatterCompass Compass = new BiomeScatterCompass
		{
			WindTo = new Vector2(1f, 0f),
			East = Vector2.right,
			Equator = Vector2.down,
		};

		// ── Defaults: nothing changes ───────────────────────────────

		[Test]
		public void Default_IsOff_WithNoMark_AndNoWords()
		{
			BiomeScatterMode off = default;
			Assert.That(off.IsOff, Is.True);
			Assert.That(off.Signature(), Is.Empty, "a mark written before the modes existed still matches");
			Assert.That(off.NeedsObstacles, Is.False);
			Assert.That(off.Weighs, Is.False);
			Assert.That(new PrefabSpawnRule().scatterMode.IsOff, Is.True, "a new rule has no mode");
			BiomeScatterModeWord.Pack(off, Compass, true, out uint word, out uint angles, out bool faced);
			Assert.That(word, Is.EqualTo(0u));
			Assert.That(angles, Is.EqualTo(0u));
			Assert.That(faced, Is.True);
		}

		[Test]
		public void EverySpecEntry_ScattersAsBefore()
		{
			int entries = 0, rules = 0;
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				entries++;
				foreach ((BiomeArtSpec.Layer _, BiomeArtSpec.Scatter s) in entry.Rules())
				{
					var versions = new List<BiomeArtSpec.Scatter> { s };
					versions.AddRange(s.Earlier);
					foreach (BiomeArtSpec.Scatter v in versions)
					{
						rules++;
						if (BiomeAuthoredRounds.Contains(entry.Biome))
						{
							// An authored round names its own modes: ToRule writes exactly the spec's, and the mark is the rule's own.
							PrefabSpawnRule own = BiomeArtAuthoring.ToRule(entry.Biome, v, new GameObject[0]);
							Assert.That(own.scatterMode.ToString(), Is.EqualTo(v.Mode.ToString()), $"{entry.Biome} '{v.Name}'");
							Assert.That(own.SpecFingerprint, Is.EqualTo(BiomeArtAuthoring.Fingerprint(own)), $"{entry.Biome} '{v.Name}'");
							continue;
						}
						Assert.That(v.Mode.IsOff, Is.True, $"{entry.Biome} '{v.Name}' names a scatter mode before its round");
						PrefabSpawnRule rule = BiomeArtAuthoring.ToRule(entry.Biome, v, new GameObject[0]);
						Assert.That(rule.scatterMode.IsOff, Is.True, $"{entry.Biome} '{v.Name}': ToRule wrote a mode (the cut's ModesFor would take a mode path)");
						// The mark is what it was: a rule with its mode reset (the field as it stood before the modes) hashes the same.
						PrefabSpawnRule plain = BiomeArtAuthoring.ToRule(entry.Biome, v, new GameObject[0]);
						plain.scatterMode = default;
						Assert.That(BiomeArtAuthoring.Fingerprint(rule), Is.EqualTo(BiomeArtAuthoring.Fingerprint(plain)), $"{entry.Biome} '{v.Name}'");
						Assert.That(rule.SpecFingerprint, Is.EqualTo(BiomeArtAuthoring.Fingerprint(rule)));
						BiomeScatterModeWord.Pack(rule.scatterMode, Compass, true, out uint word, out uint angles, out _);
						Assert.That(word | angles, Is.EqualTo(0u), $"{entry.Biome} '{v.Name}' would carry mode words to the GPU");
					}
				}
			}
			Assert.That(entries, Is.GreaterThanOrEqualTo(83), "every drafted biome's entry");
			Assert.That(rules, Is.GreaterThan(0));
			// Pinned from before the modes (ClimateBandTests pins the same): a default rule's mark.
			Assert.That(BiomeArtAuthoring.Fingerprint(new PrefabSpawnRule { prefabs = new GameObject[0] }), Is.EqualTo("1c8a98b57d35dd8c"));
		}

		[Test]
		public void BiomeAssets_CarryOnlyOffModes()
		{
			string folder = Path.Combine(Directory.GetCurrentDirectory(), "Assets/Templates/Entity/Biomes");
			Assume.That(Directory.Exists(folder), Is.True);
			var block = new Regex(@"scatterMode:\n((?:\s{8,}\w+:.*\n)+)");
			int seen = 0;
			foreach (string path in Directory.GetFiles(folder, "*.asset"))
			{
				string text = File.ReadAllText(path).Replace("\r\n", "\n");
				foreach (Match m in block.Matches(text))
				{
					seen++;
					foreach (string line in m.Groups[1].Value.Split('\n'))
					{
						string t = line.Trim();
						if (t.Length == 0)
						{
							continue;
						}
						string value = t.Substring(t.IndexOf(':') + 1).Trim();
						Assert.That(value == "0" || value.Length == 0, Is.True, $"{Path.GetFileName(path)}: '{t}' (a mode authored before any round names one)");
					}
				}
			}
			TestContext.Out?.WriteLine($"{seen} serialised scatter modes, all off");
		}

		// ── Marks and fingerprints change only when set ─────────────

		[Test]
		public void NamedModes_AreOn_WithDistinctMarks()
		{
			var marks = new HashSet<string>();
			foreach (BiomeScatterMode mode in NamedModes())
			{
				Assert.That(mode.IsOff, Is.False, mode.ToString());
				string mark = mode.Signature();
				Assert.That(mark, Is.Not.Empty);
				Assert.That(marks.Add(mark), Is.True, $"two named modes share the mark '{mark}'");
				Assert.That(mode.Signature(), Is.EqualTo(mark), "deterministic");
			}
		}

		[Test]
		public void Fingerprint_ChangesOnlyWhenAModeIsSet()
		{
			var rule = new PrefabSpawnRule { prefabs = new GameObject[0] };
			string before = BiomeArtAuthoring.Fingerprint(rule);
			rule.scatterMode = BiomeScatterMode.LeeDrift(6f);
			string lee = BiomeArtAuthoring.Fingerprint(rule);
			Assert.That(lee, Is.Not.EqualTo(before));
			rule.scatterMode = BiomeScatterMode.LeeDrift(9f);
			Assert.That(BiomeArtAuthoring.Fingerprint(rule), Is.Not.EqualTo(lee), "a different tail is a different mark");
			rule.scatterMode = default;
			Assert.That(BiomeArtAuthoring.Fingerprint(rule), Is.EqualTo(before), "off again: the mark it had");
		}

		// ── The pure functions: identity off, deterministic on ──────

		[Test]
		public void Facing_IsIdentityOff_AndStraysBySpread()
		{
			BiomeScatterMode off = default;
			Assert.That(BiomeScatterModes.Facing(off, 1.25f, 0.9f), Is.EqualTo(1.25f));
			BiomeScatterMode m = BiomeScatterMode.DownslopeAligned(30f, 6f);
			float centre = BiomeScatterModes.Facing(m, 0f, 0.5f);
			Assert.That(centre, Is.EqualTo(30f * Mathf.Deg2Rad).Within(1e-5f));
			Assert.That(BiomeScatterModes.Facing(m, 0f, 0f), Is.EqualTo(24f * Mathf.Deg2Rad).Within(1e-5f));
			Assert.That(BiomeScatterModes.Facing(m, 0f, 1f), Is.EqualTo(36f * Mathf.Deg2Rad).Within(1e-5f));
			Assert.That(BiomeScatterModes.YawOf(new Vector2(0f, 1f)), Is.EqualTo(0f).Within(1e-6f), "+Z along +z");
			Assert.That(BiomeScatterModes.YawOf(new Vector2(1f, 0f)), Is.EqualTo(Mathf.PI / 2f).Within(1e-6f), "+Z along +x");
			Assert.That(BiomeScatterModes.CompassDirection(BiomeScatterFacing.Wind, default(BiomeScatterCompass), out _), Is.False, "no wind, no heading");
		}

		[Test]
		public void PowerLaw_IsIdentityOff_MonotoneAndBounded()
		{
			for (float t = 0f; t <= 1f; t += 0.125f)
			{
				Assert.That(BiomeScatterModes.PowerLawShare(t, 0f, 0.5f, 2f), Is.EqualTo(t));
				Assert.That(BiomeScatterModes.PowerLawShare(t, 1.6f, 1f, 1f), Is.EqualTo(t), "an empty range");
			}
			Assert.That(BiomeScatterModes.PowerLawShare(0f, 1.6f, 0.5f, 3f), Is.EqualTo(0f).Within(1e-6f));
			Assert.That(BiomeScatterModes.PowerLawShare(1f, 1.6f, 0.5f, 3f), Is.EqualTo(1f).Within(1e-5f));
			float last = -1f;
			for (int i = 0; i <= 64; i++)
			{
				float s = BiomeScatterModes.PowerLawShare(i / 64f, 1.6f, 0.5f, 3f);
				Assert.That(s, Is.InRange(0f, 1f));
				Assert.That(s, Is.GreaterThanOrEqualTo(last), "monotone");
				last = s;
			}
			Assert.That(BiomeScatterModes.PowerLawShare(0.5f, 1.6f, 0.5f, 3f), Is.LessThan(0.25f), "most are small");
			Assert.That(BiomeScatterModes.PowerLawShare(0.37f, 1.6f, 0.5f, 3f), Is.EqualTo(BiomeScatterModes.PowerLawShare(0.37f, 1.6f, 0.5f, 3f)));
		}

		[Test]
		public void Anchors_WeighAsDocumented()
		{
			Assert.That(BiomeScatterModes.Anchored(1f, 0f), Is.EqualTo(1f), "a floor of 1: the anchor changes nothing");
			Assert.That(BiomeScatterModes.Anchored(0f, 0.3f), Is.EqualTo(0.3f).Within(1e-6f));
			Assert.That(BiomeScatterModes.Near(1f, 2f, 1f), Is.EqualTo(1f), "within the reach");
			Assert.That(BiomeScatterModes.Near(10f, 2f, 1f), Is.EqualTo(0f), "far past it");

			var wind = new Vector2(1f, 0f);
			Assert.That(BiomeScatterModes.Lee(new Vector2(-3f, 0f), 1f, wind, 8f), Is.EqualTo(0f), "upwind");
			Assert.That(BiomeScatterModes.Lee(new Vector2(1.5f, 0f), 1f, wind, 8f), Is.GreaterThan(0.6f), "just behind");
			Assert.That(BiomeScatterModes.Lee(new Vector2(20f, 0f), 1f, wind, 8f), Is.EqualTo(0f), "past the tail");
			Assert.That(BiomeScatterModes.Lee(new Vector2(3f, 6f), 1f, wind, 8f), Is.EqualTo(0f).Within(1e-6f), "beside it");

			Assert.That(BiomeScatterModes.CliffFoot(5f, 70f, 70f, 70f, 70f), Is.GreaterThan(0.5f), "flat ground under a cliff");
			Assert.That(BiomeScatterModes.CliffFoot(5f, 5f, 5f, 5f, 5f), Is.EqualTo(0f), "no cliff uphill");
			Assert.That(BiomeScatterModes.CliffFoot(60f, 70f, 70f, 70f, 70f), Is.EqualTo(0f), "on the face itself");

			Assert.That(BiomeScatterModes.Wall(30f, 58f), Is.EqualTo(0f));
			Assert.That(BiomeScatterModes.Wall(70f, 58f), Is.EqualTo(1f));

			float phase = BiomeScatterModes.SeepPhase(12, 14f);
			Assert.That(phase, Is.EqualTo(BiomeScatterModes.SeepPhase(12, 14f)), "one phase per biome");
			Assert.That(phase, Is.InRange(0f, 14f));
			Assert.That(BiomeScatterModes.SeepLine(phase + 28f, 5f, 14f, phase), Is.EqualTo(0f), "flat ground never weeps");
			Assert.That(BiomeScatterModes.SeepLine(phase + 28f, 75f, 14f, phase), Is.GreaterThan(0.5f), "a stratum on the face");

			Assert.That(BiomeScatterModes.NearSite(null, "Village", 0f, 0f, 60f), Is.EqualTo(0f));
			var sites = new List<BiomeScatterSite> { new BiomeScatterSite { Centre = new Vector2(100f, 0f), Radius = 20f, Kind = "VillageSmall" } };
			Assert.That(BiomeScatterModes.NearSite(sites, "village", 140f, 0f, 60f), Is.EqualTo(1f), "near a named kind (case aside)");
			Assert.That(BiomeScatterModes.NearSite(sites, "Shrine", 140f, 0f, 60f), Is.EqualTo(0f), "another kind");
			Assert.That(BiomeScatterModes.NearSite(sites, "Village", 400f, 0f, 60f), Is.EqualTo(0f), "too far");

			Assert.That(BiomeScatterModes.Names(null, "Tree_Oak"), Is.True, "no words: every candidate");
			Assert.That(BiomeScatterModes.Names("pine, oak", "Tree_OAK_02"), Is.True);
			Assert.That(BiomeScatterModes.Names("pine,birch", "Tree_Oak"), Is.False);
		}

		[Test]
		public void Obstacles_NurseAndLee()
		{
			var obstacles = new BiomeScatterObstacles();
			obstacles.Add(0f, 0f, 2f, "Tree_Mesquite_A");
			obstacles.Add(40f, 0f, 2f, "Rock_Boulder");
			Assert.That(obstacles.Near(1f, 1f, 1.5f, "mesquite"), Is.EqualTo(1f));
			Assert.That(obstacles.Near(41f, 0f, 1.5f, "mesquite"), Is.EqualTo(0f), "a boulder is no nurse");
			Assert.That(obstacles.Near(41f, 0f, 1.5f, null), Is.EqualTo(1f), "no words: any disc");
			Assert.That(obstacles.Lee(44f, 0f, new Vector2(1f, 0f), 8f, null), Is.GreaterThan(0.3f), "behind the boulder");
			Assert.That(obstacles.Lee(36f, 0f, new Vector2(1f, 0f), 8f, null), Is.EqualTo(0f), "in front of it");
		}

		[Test]
		public void Even_KeepsItsSpacing()
		{
			const float pitch = 4f, jitter = 0.2f, cell = 1f;
			var points = new List<Vector2>();
			for (int i = 0; i < 80; i++)
			{
				for (int j = 0; j < 80; j++)
				{
					if (BiomeScatterModes.EvenPoint(77u, pitch, jitter, i * cell, j * cell, (i + 1) * cell, (j + 1) * cell, out Vector2 p))
					{
						points.Add(p);
						Assert.That(BiomeScatterModes.EvenPoint(77u, pitch, jitter, i * cell, j * cell, (i + 1) * cell, (j + 1) * cell, out Vector2 again), Is.True);
						Assert.That(again, Is.EqualTo(p), "deterministic");
					}
				}
			}
			float expected = 80f * 80f / (pitch * pitch);
			Assert.That(points.Count, Is.InRange((int)(expected * 0.85f), (int)(expected * 1.15f)), "one point per pitch²");
			float least = float.PositiveInfinity;
			for (int a = 0; a < points.Count; a++)
			{
				for (int b = a + 1; b < points.Count; b++)
				{
					least = Mathf.Min(least, (points[a] - points[b]).magnitude);
				}
			}
			Assert.That(least, Is.GreaterThanOrEqualTo(pitch * (1f - 2f * jitter) - 1e-3f), "no two closer than the jittered lattice allows");
			Assert.That(BiomeScatterModes.EvenPitch(default, 25f, 0.5f), Is.EqualTo(2f).Within(1e-5f), "pitch from the density: 25 per 100 m²");
			Assert.That(BiomeScatterModes.EvenPitch(BiomeScatterMode.EvenSpacing(0.2f), 25f, 0.5f), Is.EqualTo(0.5f), "never under a cell");
		}

		[Test]
		public void Patterns_AreOneOff_AndAverageOne()
		{
			Assert.That(BiomeScatterModes.Pattern(default, 3u, 10f, 20f), Is.EqualTo(1f));
			Assert.That(BiomeScatterModes.Pattern(BiomeScatterMode.EvenSpacing(), 3u, 10f, 20f), Is.EqualTo(1f), "Even places, it does not weigh");
			foreach (BiomeScatterMode mode in new[] { BiomeScatterMode.ClusteredFields(40f), BiomeScatterMode.RainCellClusters(40f), BiomeScatterMode.FairyRings(6f) })
			{
				double sum = 0.0;
				int n = 0;
				float span = mode.Pattern == BiomeScatterPattern.Rings ? 600f : 3000f;
				for (float x = 0f; x < span; x += span / 300f)
				{
					for (float z = 0f; z < span; z += span / 300f)
					{
						float w = BiomeScatterModes.Pattern(mode, 9u, x + 0.37f, z + 0.61f);
						Assert.That(w, Is.GreaterThanOrEqualTo(0f));
						sum += w;
						n++;
					}
				}
				Assert.That(sum / n, Is.EqualTo(1.0).Within(0.2), $"{mode.Pattern}: the density stays the average");
				Assert.That(BiomeScatterModes.Pattern(mode, 9u, 123.4f, 56.7f), Is.EqualTo(BiomeScatterModes.Pattern(mode, 9u, 123.4f, 56.7f)));
			}
		}

		[Test]
		public void PositionUnit_IsDeterministic_InUnitRange()
		{
			float a = BiomeScatterModes.PositionUnit(10.5f, -3.25f);
			Assert.That(a, Is.EqualTo(BiomeScatterModes.PositionUnit(10.5f, -3.25f)));
			Assert.That(a, Is.InRange(0f, 1f));
			Assert.That(a, Is.Not.EqualTo(BiomeScatterModes.PositionUnit(-3.25f, 10.5f)), "x and z are not interchangeable");
		}

		[Test]
		public void LandformHeadings_ReadTheStoredLines()
		{
			BiomeLandform form = BiomeLandformPresets.Named("ScreeCone");
			var site = new BiomeLandformSite
			{
				Form = form, BiomeID = 5, Centre = new Vector2(100f, 100f), Radius = 50f, HalfLength = 50f, Yaw = 90f, Seed = 3u, Height = 10f,
				Bounds = new Vector4(40f, 40f, 160f, 160f),
				Segments = new List<BiomeLandformSegment> { new BiomeLandformSegment { A = new Vector2(60f, 100f), B = new Vector2(140f, 100f), Half = 2f } },
			};
			var zones = new BiomeLandformZones(new List<BiomeLandformSite> { site }, new BiomeLandforms.Context());
			Assert.That(zones.LineHeading(5, 100f, 104f, BiomeScatterModes.LineReach, out float line), Is.True);
			Assert.That(line, Is.EqualTo(Mathf.PI / 2f).Within(1e-4f), "along the segment (+x)");
			Assert.That(zones.LineHeading(6, 100f, 104f, BiomeScatterModes.LineReach, out _), Is.False, "another biome's line");
			Assert.That(zones.StrikeHeading(5, 100f, 110f, out float strike), Is.True);
			Assert.That(strike, Is.EqualTo(BiomeScatterModes.YawOfLandform(90f)).Within(1e-5f));
			Assert.That(zones.StrikeHeading(5, 1000f, 1000f, out _), Is.False, "outside every box");
		}

		// ── The words the GPU and the client read ───────────────────

		[Test]
		public void ModeWord_IsIdentityOff()
		{
			Vector3 slope = new Vector3(0.3f, 0.9f, -0.2f).normalized;
			for (float t = 0f; t < 1f; t += 0.1f)
			{
				Assert.That(BiomeScatterModeWord.Yaw(0u, 0u, t, slope), Is.EqualTo(t * 2f * Mathf.PI));
				Assert.That(BiomeScatterModeWord.LeanUp(0u, 0u, slope, t), Is.EqualTo(Vector3.up));
				Assert.That(BiomeScatterModeWord.SizeShare(0u, t, 0.5f, 2f), Is.EqualTo(t));
			}
			Assert.That(BiomeScatterModeWord.Faces(0u) || BiomeScatterModeWord.Leans(0u) || BiomeScatterModeWord.Sizes(0u), Is.False);
		}

		[Test]
		public void ModeWord_CarriesFacingLeanAndSize()
		{
			// A fixed heading from the compass (the wind blows to +x): +Z laid along +x, strayed by at most the spread.
			BiomeScatterModeWord.Pack(BiomeScatterMode.LeeDrift(), Compass, true, out uint word, out uint angles, out bool faced);
			Assert.That(faced, Is.True);
			Assert.That(BiomeScatterModeWord.Faces(word), Is.True);
			Assert.That(BiomeScatterModeWord.Leans(word), Is.False);
			float yaw = BiomeScatterModeWord.Yaw(word, angles, 0.5f, Vector3.up);
			Assert.That(yaw, Is.EqualTo(Mathf.PI / 2f).Within(1e-3f));
			Assert.That(Mathf.Abs(BiomeScatterModeWord.Yaw(word, angles, 1f, Vector3.up) - yaw), Is.EqualTo(8f * Mathf.Deg2Rad).Within(0.01f));

			// Down the fall line plus the offset; flat ground keeps the own turn.
			BiomeScatterModeWord.Pack(BiomeScatterMode.DownslopeAligned(30f, 0f), Compass, true, out word, out angles, out _);
			Vector3 n = new Vector3(0f, 0.8f, 0.6f);
			Assert.That(BiomeScatterModeWord.Yaw(word, angles, 0.3f, n), Is.EqualTo(30f * Mathf.Deg2Rad).Within(1e-3f), "downhill is +z, turned 30°");
			Assert.That(BiomeScatterModeWord.Yaw(word, angles, 0.3f, Vector3.up), Is.EqualTo(0.3f * 2f * Mathf.PI).Within(1e-6f));

			// A place-varying facing has no GPU form.
			BiomeScatterModeWord.Pack(BiomeScatterMode.AlongStrike(), Compass, true, out word, out _, out faced);
			Assert.That(faced, Is.False);
			Assert.That(BiomeScatterModeWord.Faces(word), Is.False);

			// A lean toward the equator (−z here) by 0.6–1 of its degrees.
			BiomeScatterModeWord.Pack(BiomeScatterMode.LeanSunward(20f), Compass, false, out word, out angles, out _);
			Assert.That(BiomeScatterModeWord.Leans(word), Is.True);
			Vector3 up0 = BiomeScatterModeWord.LeanUp(word, angles, Vector3.up, 0f);
			Vector3 up1 = BiomeScatterModeWord.LeanUp(word, angles, Vector3.up, 1f);
			Assert.That(Vector3.Angle(Vector3.up, up0), Is.EqualTo(12f).Within(0.2f));
			Assert.That(Vector3.Angle(Vector3.up, up1), Is.EqualTo(20f).Within(0.2f));
			Assert.That(up1.z, Is.LessThan(-0.3f), "toward the equator");
			Assert.That(Mathf.Abs(up1.x), Is.LessThan(1e-3f));
			Assert.That(up1.magnitude, Is.EqualTo(1f).Within(1e-5f));

			// Ground conforms; downslope leans down the fall line.
			BiomeScatterModeWord.Pack(BiomeScatterMode.LaidFlat(), Compass, false, out word, out angles, out _);
			Assert.That(BiomeScatterModeWord.LeanUp(word, angles, n, 0.5f), Is.EqualTo(n));
			BiomeScatterModeWord.Pack(default(BiomeScatterMode).Leaning(BiomeScatterLean.Downslope, 30f), Compass, false, out word, out angles, out _);
			Assert.That(BiomeScatterModeWord.LeanUp(word, angles, n, 1f).z, Is.GreaterThan(0.4f));
			Assert.That(BiomeScatterModeWord.LeanUp(word, angles, Vector3.up, 1f), Is.EqualTo(Vector3.up), "flat ground: upright");

			// A power law, α quantised to sixteenths.
			BiomeScatterModeWord.Pack(BiomeScatterMode.PowerLaw(1.6f), Compass, false, out word, out _, out _);
			Assert.That(BiomeScatterModeWord.Sizes(word), Is.True);
			Assert.That(BiomeScatterModeWord.SizeShare(word, 0.4f, 0.5f, 3f), Is.EqualTo(BiomeScatterModes.PowerLawShare(0.4f, Mathf.Round(1.6f * 16f) / 16f, 0.5f, 3f)).Within(1e-6f));
		}

		[Test]
		public void LeanRotation_IsIdentityOff_AndTiltsUpOn()
		{
			Assert.That(BiomeScatterModeWord.LeanRotation(0u, 0u, Vector3.up, 0.5f), Is.EqualTo(Quaternion.identity));
			BiomeScatterModeWord.Pack(BiomeScatterMode.LeanSunward(20f), Compass, false, out uint word, out uint angles, out _);
			Quaternion q = BiomeScatterModeWord.LeanRotation(word, angles, Vector3.up, 1f);
			Vector3 up = q * Vector3.up;
			Assert.That(Vector3.Angle(up, BiomeScatterModeWord.LeanUp(word, angles, Vector3.up, 1f)), Is.LessThan(0.05f));
		}

		[Test]
		public void TreeMatrix_IsUnchangedOff_AndLeansOn()
		{
			var origin = new Vector3(100f, 5f, -50f);
			var size = new Vector3(500f, 300f, 500f);
			var at = new Vector3(0.3f, 0.1f, 0.6f);
			Matrix4x4 plain = FishMMO.Client.TerrainTreeMath.InstanceMatrix(origin, size, at, 1.1f, 1.2f, 0.9f);
			Matrix4x4 off = FishMMO.Client.TerrainTreeMath.InstanceMatrix(origin, size, at, 1.1f, 1.2f, 0.9f, 0u, 0u, new Vector3(0.2f, 0.9f, 0f).normalized);
			Assert.That(off, Is.EqualTo(plain), "no lean: exactly the old matrix");
			BiomeScatterModeWord.Pack(BiomeScatterMode.LeanSunward(20f), Compass, false, out uint word, out uint angles, out _);
			Matrix4x4 leaned = FishMMO.Client.TerrainTreeMath.InstanceMatrix(origin, size, at, 1.1f, 1.2f, 0.9f, word, angles, Vector3.up);
			Assert.That((Vector3)leaned.GetColumn(3), Is.EqualTo((Vector3)plain.GetColumn(3)), "it stands where it stood");
			float tilt = Vector3.Angle(Vector3.up, leaned.GetColumn(1));
			Assert.That(tilt, Is.InRange(11.8f, 20.2f), "0.6–1 of its degrees");
			Assert.That(((Vector3)leaned.GetColumn(1)).magnitude, Is.EqualTo(0.9f).Within(1e-4f), "the height scale kept");
			Matrix4x4 again = FishMMO.Client.TerrainTreeMath.InstanceMatrix(origin, size, at, 1.1f, 1.2f, 0.9f, word, angles, Vector3.up);
			Assert.That(again, Is.EqualTo(leaned), "deterministic (its share from its place)");
		}

		[Test]
		public void TreeRecord_CarriesTheLeanOnly()
		{
			string modes = Read(Terrain + "/TerrainScatter.Modes.cs");
			StringAssert.Contains("word &= (0x7u << BiomeScatterModeWord.LeanShift) | (0x7Fu << BiomeScatterModeWord.LeanDegreesShift);", modes,
				"a tree's turn and size are in its instance; only the lean is the renderer's");
			StringAssert.Contains("angles &= 0xFFFF0000u;", modes);
		}

		[Test]
		public void GpuMirror_MatchesTheWord()
		{
			string compute = Read(Compute);
			StringAssert.Contains("#pragma use_dxc vulkan", compute);
			StringAssert.DoesNotContain("FishIndirectInstancing.hlsl\"", compute, "a compute pass: DXC is its compiler");
			void Define(string name, int value) => StringAssert.Contains($"#define {name} {value}u", compute, name);
			Define("SCATTER_MODE_LEAN_STREAM", BiomeScatterModeWord.LeanStream);
			Define("SCATTER_MODE_SIZE_STREAM", BiomeScatterModeWord.SizeStream);
			Define("SCATTER_MODE_FACING_MASK", BiomeScatterModeWord.FacingMask);
			Define("SCATTER_MODE_FACING_DOWNSLOPE", (int)BiomeScatterModeWord.FacingDownslope);
			Define("SCATTER_MODE_SPREAD_SHIFT", BiomeScatterModeWord.SpreadShift);
			Define("SCATTER_MODE_LEAN_SHIFT", BiomeScatterModeWord.LeanShift);
			Define("SCATTER_MODE_LEAN_DEGREES_SHIFT", BiomeScatterModeWord.LeanDegreesShift);
			Define("SCATTER_MODE_POWER_SHIFT", BiomeScatterModeWord.PowerShift);
			Define("SCATTER_MODE_LEAN_DOWNSLOPE", (int)BiomeScatterModeWord.LeanDownslope);
			Define("SCATTER_MODE_LEAN_GROUND", (int)BiomeScatterModeWord.LeanGround);
			// Streams the scatter already draws from are not reused (2 turn, 5 variant, 6 trodden, 7 the count's coin).
			foreach (int taken in new[] { 0, 1, 2, 3, 5, 6, 7 })
			{
				Assert.That(BiomeScatterModeWord.LeanStream, Is.Not.EqualTo(taken));
				Assert.That(BiomeScatterModeWord.SizeStream, Is.Not.EqualTo(taken));
			}
			// The two spare channel uints carry the words; the off path is the old turn, untouched.
			StringAssert.Contains("uint modeWord = _ScatterTable[cb + 8u];", compute);
			StringAssert.Contains("uint modeAngles = _ScatterTable[cb + 9u];", compute);
			StringAssert.Contains("float yaw = turnUnit * 6.2831853;", compute);
			StringAssert.Contains("if ((modeWord & SCATTER_MODE_FACING_MASK) != 0u)", compute);
			foreach (string fn in new[] { "float ScatterModeYaw(uint word, uint angles, float turnUnit, float3 normal)",
				"float3 ScatterModeLeanUp(uint word, uint angles, float3 normal, float leanUnit)", "float ScatterModeSize(uint word, float t, float lo, float hi)",
				"float ScatterPowerLawShare(float t, float alpha, float lo, float hi)" })
			{
				StringAssert.Contains(fn, compute);
			}
			// The C# names its mirror.
			StringAssert.Contains("MIRRORED by FishDetailScatter.compute (ScatterModeYaw, ScatterModeLeanUp, ScatterModeSize)", Read(ModeFile));
			// The table has the room: ten uints a channel, the words in the last two.
			Assert.That(FishMMO.Client.DetailScatterAtlas.ChannelUints, Is.EqualTo(10));
			StringAssert.Contains("#define SCATTER_CHANNEL_UINTS 10u", compute);
		}

		// ── Wiring ──────────────────────────────────────────────────

		[Test]
		public void Wiring_SourceScan()
		{
			string atlas = Read(Client + "/Scatter/DetailScatterAtlas.cs");
			StringAssert.Contains("data[cb + 8] = c < st.Channels ? ch.ModeWord : 0u;", atlas);
			StringAssert.Contains("data[cb + 9] = c < st.Channels ? ch.ModeAngles : 0u;", atlas);

			string terrain = Read(Client + "/Scatter/DetailScatterTerrain.cs");
			StringAssert.Contains("terrain.GetComponent<FishMMO.Shared.TerrainScatterModes>()", terrain);
			StringAssert.Contains("record.TryDetail(all[p].prototype.name, all[p].noiseSeed, out uint word, out uint angles)", terrain,
				"keyed as the cut writes it: prefab name and noise seed");
			StringAssert.Contains("bool leans = ReadModes(terrain, all);", terrain);

			string field = Read(Client + "/TerrainTreeField.cs");
			StringAssert.Contains("record.TreeWords(models.Length, out leanAngles)", field);
			StringAssert.Contains("? TerrainTreeMath.InstanceMatrix(origin, size, t.position, t.rotation, t.widthScale, t.heightScale)", field, "no lean: the old matrix");
			string treeMath = Read(Client + "/TerrainTreeMath.cs");
			StringAssert.Contains("BiomeScatterModes.PositionUnit(world.x, world.z)", treeMath);

			string modes = Read(Terrain + "/TerrainScatter.Modes.cs");
			StringAssert.Contains("if (mode.IsOff)\n\t\t\t{\n\t\t\t\treturn null;", modes, "an off rule takes no mode path");
			StringAssert.Contains("record.Details = new List<TerrainScatterModes.Detail>(details);", modes);
			StringAssert.Contains("Prefab = details[p].prototype.name, NoiseSeed = details[p].noiseSeed", modes);

			string generator = Read(World + "/SceneGenerator.cs");
			StringAssert.Contains("Compass = TerrainScatter.CompassFor(request, system), Sites = TerrainScatter.SitesOf(poi?.Plan),", generator);

			string authoring = Read(Art + "/BiomeArtAuthoring.cs");
			StringAssert.Contains("if (!rule.scatterMode.IsOff)\n\t\t\t{\n\t\t\t\ttext.Append(\"|M\").Append(rule.scatterMode.Signature());", authoring);
			StringAssert.Contains("scatterMode = s.Mode,", authoring);
			StringAssert.Contains("|| !rule.scatterMode.IsOff)", authoring, "a moded rule is never taken for a legacy spec rule");
		}
	}
}
