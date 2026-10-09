using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The jointed cliff sections (<see cref="CliffSections"/>): every level of every variant closed, within its budget,
	/// free of the hidden tunnels a too-thin joint overlap leaves, grounded and centred, and the same on every build.
	/// </summary>
	[TestFixture]
	public class CliffSectionTests
	{
		private const int Seed = 20261002;

		private static readonly Dictionary<(string, int), MeshBuilder[]> built = new Dictionary<(string, int), MeshBuilder[]>();

		private static MeshBuilder[] Levels(string style, int variant)
		{
			if (!built.TryGetValue((style, variant), out MeshBuilder[] levels))
			{
				built[(style, variant)] = levels = CliffSections.BuildMeshes(style, variant, Seed, out _);
			}
			return levels;
		}

		private static IEnumerable<(string Style, int Variant)> Variants()
		{
			foreach (string style in CliffSections.Styles)
				for (int v = 0; v < CliffSections.VariantCount; v++)
					yield return (style, v);
		}

		/// <summary>
		/// Vertices merged by position (crease copies share one exactly), then V − E + F: 2 for a closed surface with no
		/// handle, 2 − 2g with g handles. Also counts edges not used by exactly two faces.
		/// </summary>
		private static int EulerCharacteristic(MeshBuilder m, out int badEdges)
		{
			var weld = new Dictionary<Vector3, int>();
			var use = new Dictionary<(int, int), int>();
			List<int> t = m.Submeshes[0];
			int Id(int v)
			{
				if (!weld.TryGetValue(m.Positions[v], out int id)) weld[m.Positions[v]] = id = weld.Count;
				return id;
			}
			for (int i = 0; i < t.Count; i += 3)
			{
				int a = Id(t[i]), b = Id(t[i + 1]), c = Id(t[i + 2]);
				foreach ((int x, int y) in new[] { (a, b), (b, c), (c, a) })
				{
					var key = x < y ? (x, y) : (y, x);
					use[key] = use.TryGetValue(key, out int n) ? n + 1 : 1;
				}
			}
			badEdges = use.Values.Count(n => n != 2);
			return weld.Count - use.Count + t.Count / 3;
		}

		[Test]
		public void EveryLevelIsClosedAndValid()
		{
			foreach ((string style, int v) in Variants())
			{
				MeshBuilder[] levels = Levels(style, v);
				Assert.That(levels.Length, Is.EqualTo(CliffSections.LevelCount));
				for (int lod = 0; lod < levels.Length; lod++)
				{
					string name = CliffSections.MeshName(style, v, lod);
					List<string> problems = levels[lod].Validate(true);
					Assert.That(problems, Is.Empty, $"{name}: {string.Join("; ", problems.Take(3))}");
					EulerCharacteristic(levels[lod], out int badEdges);
					Assert.That(badEdges, Is.Zero, $"{name} has edges not on exactly two faces");
				}
			}
		}

		[Test]
		public void EveryLevelMeetsItsBudget()
		{
			foreach ((string style, int v) in Variants())
			{
				MeshBuilder[] levels = Levels(style, v);
				for (int lod = 0; lod < levels.Length; lod++)
				{
					int budget = CliffSections.LodTrianglesOf(style)[lod];
					int count = levels[lod].TriangleCount;
					// At most the budget, and near it: a level that stalls far above is carrying hidden structure.
					Assert.That(count, Is.InRange((int)(budget * 0.9f), budget), $"{CliffSections.MeshName(style, v, lod)}: {count} triangles");
				}
			}
		}

		/// <summary>
		/// With the closed joints overlapping by less than the bevel, the rounded corners where three columns meet stayed
		/// open as vertical channels: 5–32 handles a section, which no simplification removes (the massif's last level
		/// stalled at ~380 triangles). An arch is a real handle; a few are allowed.
		/// </summary>
		[Test]
		public void SectionsHaveNoHiddenTunnels()
		{
			var genera = new List<int>();
			foreach ((string style, int v) in Variants())
			{
				MeshBuilder[] levels = Levels(style, v);
				int genus = (2 - EulerCharacteristic(levels[levels.Length - 1], out _)) / 2;
				Assert.That(genus, Is.LessThanOrEqualTo(6), $"{style} {v}: genus {genus}");
				genera.Add(genus);
			}
			// Before the fix the mean was ~12 (5–32); now 0–3 with the odd tall massif at 5.
			Assert.That(genera.Average(), Is.LessThanOrEqualTo(2.0), $"mean genus {genera.Average():0.0}");
		}

		[Test]
		public void VariantsHaveTheirOwnProportions()
		{
			foreach (string style in CliffSections.Styles)
			{
				CliffStyle v0 = CliffSections.StyleOf(style, 0), declared = CliffSections.StyleOf(style);
				Assert.That((v0.Length, v0.Depth, v0.Height), Is.EqualTo((declared.Length, declared.Depth, declared.Height)), $"{style}: variant 0 is the style as declared");
				var sizes = new HashSet<(float, float, float)>();
				for (int v = 0; v < CliffSections.VariantCount; v++)
				{
					CliffStyle st = CliffSections.StyleOf(style, v);
					Assert.That(st.Height / declared.Height, Is.InRange(0.85f, 1.2f), $"{style} {v}");
					sizes.Add((st.Length, st.Depth, st.Height));
				}
				Assert.That(sizes.Count, Is.EqualTo(CliffSections.VariantCount), $"{style}: every variant its own proportions");
			}
		}

		[Test]
		public void SectionsStandOnTheOriginFacingMinusZ()
		{
			foreach ((string style, int v) in Variants())
			{
				MeshBuilder[] levels = Levels(style, v);
				Bounds b = levels[0].Bounds;
				CliffStyle st = CliffSections.StyleOf(style, v);
				Assert.That(Mathf.Abs(b.center.x), Is.LessThan(0.01f), $"{style} {v} centred in x");
				Assert.That(Mathf.Abs(b.center.z), Is.LessThan(0.01f), $"{style} {v} centred in z");
				// Its foot just below the ground it stands on, and about its style's height.
				Assert.That(b.min.y, Is.InRange(-0.5f, 0f), $"{style} {v} foot");
				Assert.That(b.max.y, Is.InRange(st.Height * 0.5f, st.Height * 1.2f), $"{style} {v} height");
				// A coarser level rounds the foot off a little (a 40 m scarp's 250-triangle collider by up to ~1.3 m, underground), never far.
				float slack = Mathf.Max(0.3f, 0.035f * Mathf.Max(b.size.x, Mathf.Max(b.size.y, b.size.z)));
				foreach (MeshBuilder level in levels)
				{
					Assert.That(level.Bounds.min.y, Is.GreaterThan(b.min.y - slack), $"{style} {v}: a coarser level reaches below the foot");
				}
			}
		}

		[Test]
		public void BuildIsDeterministicAndVariantsDiffer()
		{
			MeshBuilder[] a = CliffSections.BuildMeshes("Ledges", 0, Seed, out _);
			MeshBuilder[] b = Levels("Ledges", 0);
			for (int lod = 0; lod < a.Length; lod++)
			{
				Assert.That(a[lod].Positions, Is.EqualTo(b[lod].Positions), $"LOD{lod} positions");
				Assert.That(a[lod].Submeshes[0], Is.EqualTo(b[lod].Submeshes[0]), $"LOD{lod} triangles");
			}
			Assert.That(Levels("Ledges", 1)[0].Positions, Is.Not.EqualTo(b[0].Positions));
		}

		[Test]
		public void EveryMeshIsInThePayload()
		{
			var names = CliffSections.AllMeshes().Select(m => CliffSections.MeshName(m.Style, m.Variant, m.Lod)).ToList();
			Assert.That(names.Count, Is.EqualTo(CliffSections.Styles.Length * CliffSections.VariantCount * CliffSections.LevelCount));
			Assert.That(names.Distinct().Count(), Is.EqualTo(names.Count));
			var paths = new HashSet<string>(ProceduralArtCatalogue.RockPayloadPaths());
			foreach (string name in names)
			{
				Assert.That(paths.Contains(ProceduralArtCatalogue.MeshPath(name)), Is.True, $"{name} not in RockPayloadPaths");
			}
			foreach (string file in new[] { "CliffSections.cs", "ProceduralSurfaceNets.cs", "MeshDecimator.cs" })
			{
				Assert.That(ProceduralArtPayload.SourceFiles, Does.Contain(file), $"{file} decides generated bytes but is not hashed");
			}
		}
	}
}
