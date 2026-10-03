#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>The growth form a tree is built with.</summary>
	public enum TreeForm
	{
		/// <summary>One straight stem, whorls of drooping needle sprays narrowing to a spire.</summary>
		Conifer,
		/// <summary>A stem that splits into curving limbs with leaf clusters round a rounded crown.</summary>
		Broadleaf,
		/// <summary>The broadleaf skeleton, leafless, with a few bare twigs.</summary>
		Dead,
		/// <summary>A leaning ringed stem with a crown of arching fronds.</summary>
		Palm,
		/// <summary>A ribbed column with upturned arms.</summary>
		Cactus,
		/// <summary>Several stems spreading from the ground into a flat umbrella crown.</summary>
		Umbrella,
		/// <summary>A clump of tall segmented culms with leaf sprays up their upper half.</summary>
		Bamboo,
	}

	/// <summary>One tree species' recipe.</summary>
	public struct TreeSpecies
	{
		public string Name;
		public TreeForm Form;
		public float Height;
		public float TrunkRadius;
		/// <summary>Crown radius as a fraction of the height.</summary>
		public float CrownWidth;
		/// <summary>Where the crown starts, as a fraction of the height.</summary>
		public float CrownBase;
		public int Branches;
		/// <summary>The bark family (<see cref="Bark"/>) its trunk material uses.</summary>
		public string BarkFamily;
		/// <summary>Leaf colours, picked between per card.</summary>
		public Color LeafA, LeafB;
		public FoliageCell LeafCell;
		/// <summary>Leaf card size, metres.</summary>
		public float LeafSize;
		/// <summary>Turns in autumn and drops its leaves in winter.</summary>
		public bool Deciduous;
	}

	/// <summary>
	/// Trees as two sub-meshes — 0 bark, 1 leaves — at a level of detail; the billboard last LOD is
	/// made separately (<see cref="BillboardImpostor"/>).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Levels share a skeleton.</b> Every level is grown from the same seeded skeleton with fewer
	/// sides on each tube and fewer, larger leaf cards, so the crown keeps its shape as it steps
	/// down instead of turning into a different tree.
	/// </para>
	/// <para>
	/// <b>Wind data.</b> TEXCOORD1.x is how far a vertex sways with the whole tree:
	/// <c>(height fraction)² × height / 10</c>, so a twelve-metre tree's crown swings further than
	/// a three-metre bush's, and the trunk's foot not at all; y is leaf flutter, 1 on leaves and 0
	/// on wood. Bark and leaves use the same sway, so leaves stay on their branches.
	/// </para>
	/// </remarks>
	public static class TreeMeshes
	{
		public const int BarkSubmesh = 0;
		public const int LeafSubmesh = 1;

		/// <summary>Level 0 is the full tree; each level above roughly quarters the triangles.</summary>
		public static MeshBuilder Build(in TreeSpecies species, int lod, int seed)
		{
			var mesh = new MeshBuilder(2);
			TreeSpecies sp = species;
			var builder = new Grower(mesh, in sp, Mathf.Clamp(lod, 0, 1), ProceduralNoise.SeedFor(sp.Name, seed));
			switch (sp.Form)
			{
				case TreeForm.Conifer: builder.Conifer(); break;
				case TreeForm.Broadleaf: builder.Broadleaf(true); break;
				case TreeForm.Dead: builder.Broadleaf(false); break;
				case TreeForm.Palm: builder.Palm(); break;
				case TreeForm.Cactus: builder.Cactus(); break;
				case TreeForm.Umbrella: builder.Umbrella(); break;
				case TreeForm.Bamboo: builder.Bamboo(); break;
			}
			mesh.RecalculateNormals(true, onlyMissing: true);
			mesh.RecalculateTangents();
			return mesh;
		}

		private sealed class Grower
		{
			private readonly MeshBuilder mesh;
			private readonly TreeSpecies sp;
			private readonly int lod;
			private readonly int seed;
			private DeterministicRNG rng;
			private readonly List<Vector3> path = new List<Vector3>();
			private readonly List<float> radii = new List<float>();
			private readonly List<Color32> colours = new List<Color32>();
			private readonly List<Vector2> wind = new List<Vector2>();

			public Grower(MeshBuilder mesh, in TreeSpecies species, int lod, int seed)
			{
				this.mesh = mesh;
				sp = species;
				this.lod = lod;
				this.seed = seed;
				rng = new DeterministicRNG(seed);
			}

			/// <summary>
			/// A fresh stream for one part of the skeleton (a limb, a twig, a frond). Each part draws
			/// from its own, so a level that hangs fewer cards on one limb does not shift where the
			/// next limb grows: the levels share the skeleton and differ only in how finely it is drawn.
			/// </summary>
			private void Reseed(int part) => rng = new DeterministicRNG(seed ^ (int)ProceduralNoise.Mix((uint)part * 0x9e3779b1u + 1u));

			private Vector2 Sway(Vector3 p, float flutter)
			{
				float f = Mathf.Clamp01(p.y / Mathf.Max(0.1f, sp.Height));
				return new Vector2(f * f * sp.Height / 10f, flutter);
			}

			private int Sides(int full) => lod == 0 ? full : Mathf.Max(3, full / 2);

			private Color32 Bark => new Color32(255, 255, 255, 0);

			private Color32 Leaf(float pick) => PlantParts.C32(Color.Lerp(sp.LeafA, sp.LeafB, pick), 1f);

			/// <summary>A tube along a quadratic curve from a to c bending through b.</summary>
			private void Limb(Vector3 a, Vector3 b, Vector3 c, float r0, float r1, int sides, int segments, bool point)
			{
				path.Clear(); radii.Clear(); colours.Clear(); wind.Clear();
				for (int s = 0; s <= segments; s++)
				{
					float t = (float)s / segments;
					Vector3 p = (1 - t) * (1 - t) * a + 2 * (1 - t) * t * b + t * t * c;
					path.Add(p);
					radii.Add(point && s == segments ? 0f : Mathf.Lerp(r0, r1, t));
					colours.Add(Bark);
					wind.Add(Sway(p, 0f));
				}
				PlantParts.Tube(mesh, BarkSubmesh, path, radii, Sides(sides), 1f, colours, wind);
			}

			private Vector3 Curve(Vector3 a, Vector3 b, Vector3 c, float t) => (1 - t) * (1 - t) * a + 2 * (1 - t) * t * b + t * t * c;

			private void Spray(Vector3 root, Vector3 direction, float length, float width, Vector3 crown, FoliageCell cell, float bend = 0.6f)
			{
				float pick = rng.NextFloat();
				PlantParts.SprayCard(mesh, LeafSubmesh, root, direction, length, width, rng.Range(-30f, 30f), cell, Leaf(pick),
					Sway(root, 0.3f), Sway(root + direction * length, 1f), crown, bend);
				if (lod == 0)
				{
					// A second card across the first gives the spray depth from every side.
					PlantParts.SprayCard(mesh, LeafSubmesh, root, direction, length, width, rng.Range(60f, 120f), cell, Leaf(pick),
						Sway(root, 0.3f), Sway(root + direction * length, 1f), crown, bend);
				}
			}

			public void Conifer()
			{
				float h = sp.Height;
				Limb(Vector3.zero, new Vector3(0f, h * 0.5f, 0f), new Vector3(0f, h, 0f), sp.TrunkRadius, sp.TrunkRadius * 0.08f, 8, lod == 0 ? 8 : 4, true);
				int whorls = lod == 0 ? Mathf.Max(6, sp.Branches) : Mathf.Max(4, sp.Branches / 2);
				float baseY = h * sp.CrownBase;
				var crown = new Vector3(0f, (baseY + h) * 0.5f, 0f);
				for (int w = 0; w < whorls; w++)
				{
					float t = (w + 0.5f) / whorls;
					Reseed(1000 + Mathf.RoundToInt(t * 997f));
					float y = Mathf.Lerp(baseY, h * 0.97f, t);
					float reach = sp.CrownWidth * h * (1f - t) * rng.Range(0.85f, 1.1f) + 0.3f;
					int arms = lod == 0 ? 7 : 5;
					float twist = rng.NextFloat() * 360f;
					for (int a = 0; a < arms; a++)
					{
						float yaw = Mathf.Deg2Rad * (twist + a * 360f / arms + rng.Range(-15f, 15f));
						var outward = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
						// Lower boughs droop; the top ones reach up.
						var dir = (outward + Vector3.down * Mathf.Lerp(0.45f, -0.35f, t)).normalized;
						var root = new Vector3(0f, y, 0f) + outward * sp.TrunkRadius * (1f - t);
						Spray(root, dir, reach, Mathf.Max(0.5f, reach * 0.55f), crown, sp.LeafCell, 0.5f);
					}
				}
				// A leader at the top.
				Spray(new Vector3(0f, h * 0.9f, 0f), Vector3.up, h * 0.12f, h * 0.06f, crown, sp.LeafCell, 0.3f);
			}

			public void Broadleaf(bool leaves)
			{
				float h = sp.Height;
				float split = h * sp.CrownBase;
				Reseed(0);
				Vector3 lean = new Vector3(rng.Range(-0.05f, 0.05f), 0f, rng.Range(-0.05f, 0.05f)) * h;
				Vector3 top = new Vector3(0f, split, 0f) + lean;
				Limb(Vector3.zero, new Vector3(0f, split * 0.5f, 0f), top, sp.TrunkRadius, sp.TrunkRadius * 0.7f, 10, lod == 0 ? 5 : 3, false);
				var crown = new Vector3(lean.x, (split + h) * 0.55f, lean.z);
				float crownRadius = sp.CrownWidth * h;
				int limbs = Mathf.Max(3, sp.Branches);
				for (int l = 0; l < limbs; l++)
				{
					Reseed(100 + l);
					float yaw = Mathf.Deg2Rad * (l * 360f / limbs + rng.Range(-20f, 20f));
					var outward = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
					float rise = rng.Range(0.6f, 1.1f);
					Vector3 start = top + Vector3.down * rng.Range(0f, split * 0.15f);
					Vector3 end = crown + outward * crownRadius * rng.Range(0.55f, 0.85f) + Vector3.up * (h - crown.y) * rise * 0.6f;
					if (!leaves)
					{
						// Dead limbs reach and twist instead of filling a crown.
						end += new Vector3(rng.Range(-1f, 1f), rng.Range(-0.5f, 0.8f), rng.Range(-1f, 1f)) * crownRadius * 0.25f;
					}
					Vector3 mid = Vector3.Lerp(start, end, 0.5f) + Vector3.up * (h * 0.06f) + outward * (crownRadius * 0.1f);
					Limb(start, mid, end, sp.TrunkRadius * 0.55f, sp.TrunkRadius * 0.12f, 6, lod == 0 ? 4 : 2, true);

					int twigs = lod == 0 ? 3 : 2;
					for (int k = 0; k < twigs; k++)
					{
						Reseed(10000 + l * 16 + k);
						float t = rng.Range(0.45f, 0.9f);
						Vector3 at = Curve(start, mid, end, t);
						Vector3 side = Quaternion.AngleAxis(rng.Range(-70f, 70f), Vector3.up) * outward;
						Vector3 twigEnd = at + (side + Vector3.up * rng.Range(0.1f, 0.7f)).normalized * crownRadius * rng.Range(0.35f, 0.55f);
						if (lod == 0 || !leaves)
						{
							Limb(at, Vector3.Lerp(at, twigEnd, 0.5f) + Vector3.up * 0.2f, twigEnd, sp.TrunkRadius * 0.15f, sp.TrunkRadius * 0.05f, 4, 2, true);
						}
						if (leaves)
						{
							Cluster(twigEnd, crown, lod == 0 ? 5 : 3);
						}
						else if (lod == 0)
						{
							Spray(twigEnd, (twigEnd - at).normalized, sp.LeafSize, sp.LeafSize * 0.8f, crown, FoliageCell.Twigs, 0.2f);
						}
					}
					if (leaves)
					{
						Reseed(20000 + l);
						Cluster(end, crown, lod == 0 ? 6 : 3);
					}
				}
			}

			private void Cluster(Vector3 at, Vector3 crown, int cards)
			{
				for (int c = 0; c < cards; c++)
				{
					Vector3 outward = (at - crown).normalized;
					Vector3 dir = (outward + Random3() * 0.9f + Vector3.up * 0.3f).normalized;
					float size = sp.LeafSize * rng.Range(0.8f, 1.2f) * (lod == 0 ? 1f : 1.4f);
					Vector3 root = at - dir * size * 0.35f + Random3() * size * 0.2f;
					Spray(root, dir, size, size, crown, sp.LeafCell, 0.75f);
				}
			}

			private Vector3 Random3() => new Vector3(rng.Range(-1f, 1f), rng.Range(-1f, 1f), rng.Range(-1f, 1f));

			public void Palm()
			{
				float h = sp.Height;
				Reseed(0);
				var leanDir = new Vector3(rng.Range(-1f, 1f), 0f, rng.Range(-1f, 1f)).normalized;
				if (leanDir.sqrMagnitude < 1e-6f)
				{
					leanDir = Vector3.forward;
				}
				Vector3 top = new Vector3(0f, h, 0f) + leanDir * h * 0.15f;
				Vector3 mid = new Vector3(0f, h * 0.55f, 0f) + leanDir * h * 0.02f;
				Limb(Vector3.zero, mid, top, sp.TrunkRadius, sp.TrunkRadius * 0.75f, 8, lod == 0 ? 10 : 5, false);
				int fronds = lod == 0 ? Mathf.Max(8, sp.Branches) : Mathf.Max(6, sp.Branches - 3);
				var spine = new List<Vector3>();
				var widths = new List<float>();
				var frondColours = new List<Color32>();
				var frondWind = new List<Vector2>();
				int segments = lod == 0 ? 5 : 3;
				for (int f = 0; f < fronds; f++)
				{
					Reseed(100 + f);
					float yaw = Mathf.Deg2Rad * (f * 360f / fronds + rng.Range(-12f, 12f));
					var outward = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
					float length = sp.CrownWidth * h * rng.Range(0.85f, 1.15f);
					float rise = rng.Range(0.3f, 0.8f);
					Color32 colour = Leaf(rng.NextFloat());
					spine.Clear(); widths.Clear(); frondColours.Clear(); frondWind.Clear();
					for (int s = 0; s <= segments; s++)
					{
						float t = (float)s / segments;
						Vector3 p = top + outward * length * t + Vector3.up * length * (rise * t - 0.9f * t * t);
						spine.Add(p);
						widths.Add(sp.LeafSize * (s == segments ? 0f : 1f));
						frondColours.Add(colour);
						frondWind.Add(Sway(p, t));
					}
					PlantParts.Strip(mesh, LeafSubmesh, spine, widths, Vector3.Cross(Vector3.up, outward), FoliageCell.PalmFrond, frondColours, frondWind,
						(Vector3.up + outward * 0.4f).normalized);
				}
			}

			public void Cactus()
			{
				float h = sp.Height;
				int sides = lod == 0 ? 12 : 8;
				ColumnCactus(Vector3.zero, Vector3.up * h * 0.5f, Vector3.up * h, sp.TrunkRadius, sides, lod == 0 ? 8 : 4);
				int arms = Mathf.Max(0, sp.Branches);
				for (int a = 0; a < arms; a++)
				{
					Reseed(100 + a);
					float yaw = Mathf.Deg2Rad * (a * 360f / Mathf.Max(1, arms) + rng.Range(-30f, 30f));
					var outward = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
					float y = h * rng.Range(0.3f, 0.55f);
					Vector3 start = new Vector3(0f, y, 0f);
					float reach = sp.TrunkRadius * rng.Range(2.2f, 3f);
					Vector3 elbow = start + outward * reach;
					Vector3 end = elbow + Vector3.up * h * rng.Range(0.25f, 0.4f);
					ColumnCactus(start, elbow + outward * 0.1f, end, sp.TrunkRadius * 0.65f, sides, lod == 0 ? 6 : 3);
				}
			}

			private void ColumnCactus(Vector3 a, Vector3 b, Vector3 c, float radius, int sides, int segments)
			{
				path.Clear(); radii.Clear(); colours.Clear(); wind.Clear();
				for (int s = 0; s <= segments; s++)
				{
					float t = (float)s / segments;
					Vector3 p = Curve(a, b, c, t);
					path.Add(p);
					// A rounded crown: the last fifth closes over.
					float close = t > 0.8f ? Mathf.Sqrt(Mathf.Clamp01(1f - (t - 0.8f) / 0.2f)) : 1f;
					radii.Add(s == segments ? 0f : radius * Mathf.Max(0.25f, close));
					colours.Add(Bark);
					wind.Add(Vector2.zero);
				}
				PlantParts.Tube(mesh, BarkSubmesh, path, radii, sides, 0.5f, colours, wind, ribs: sides, ribDepth: 0.1f, analyticNormals: false);
			}

			public void Umbrella()
			{
				float h = sp.Height;
				int stems = Mathf.Max(2, sp.Branches);
				float crownRadius = sp.CrownWidth * h;
				var crown = new Vector3(0f, h * 0.85f, 0f);
				for (int s = 0; s < stems; s++)
				{
					Reseed(100 + s);
					float yaw = Mathf.Deg2Rad * (s * 360f / stems + rng.Range(-25f, 25f));
					var outward = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
					Vector3 end = new Vector3(0f, h * rng.Range(0.8f, 0.9f), 0f) + outward * crownRadius * rng.Range(0.4f, 0.6f);
					Vector3 mid = new Vector3(0f, h * 0.45f, 0f) + outward * crownRadius * 0.15f;
					Limb(outward * sp.TrunkRadius * 0.3f, mid, end, sp.TrunkRadius * 0.6f, sp.TrunkRadius * 0.2f, 6, lod == 0 ? 4 : 2, true);
				}
				// The crown: a flat layer of leaf cards lying almost level.
				int cards = lod == 0 ? 26 : 10;
				Reseed(5000);
				for (int c = 0; c < cards; c++)
				{
					float a = rng.NextFloat() * Mathf.PI * 2f;
					float r = Mathf.Sqrt(rng.NextFloat()) * crownRadius;
					var at = new Vector3(Mathf.Cos(a) * r, h * rng.Range(0.82f, 0.95f) - r * r / (crownRadius * crownRadius) * h * 0.08f, Mathf.Sin(a) * r);
					var dir = (new Vector3(Mathf.Cos(a), 0.15f, Mathf.Sin(a)) + Random3() * 0.3f).normalized;
					float size = sp.LeafSize * (lod == 0 ? 1f : 1.6f);
					PlantParts.SprayCard(mesh, LeafSubmesh, at - dir * size * 0.5f, dir, size, size, rng.Range(70f, 110f), sp.LeafCell, Leaf(rng.NextFloat()),
						Sway(at, 0.4f), Sway(at, 1f), crown + Vector3.down * h * 0.3f, 0.8f);
				}
			}

			public void Bamboo()
			{
				float h = sp.Height;
				int culms = lod == 0 ? Mathf.Max(5, sp.Branches) : Mathf.Max(3, sp.Branches / 2);
				var crown = new Vector3(0f, h * 0.7f, 0f);
				for (int c = 0; c < culms; c++)
				{
					Reseed(100 + c);
					float a = rng.NextFloat() * Mathf.PI * 2f;
					var foot = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * rng.Range(0f, sp.CrownWidth * h * 0.25f);
					float height = h * rng.Range(0.75f, 1.05f);
					var lean = new Vector3(foot.x, 0f, foot.z).normalized * height * rng.Range(0.05f, 0.15f);
					Vector3 top = foot + Vector3.up * height + lean;
					Limb(foot, foot + Vector3.up * height * 0.5f + lean * 0.2f, top, sp.TrunkRadius, sp.TrunkRadius * 0.7f, 6, lod == 0 ? 6 : 3, false);
					int sprays = lod == 0 ? 7 : 3;
					for (int s = 0; s < sprays; s++)
					{
						float t = Mathf.Lerp(0.45f, 1f, (s + 0.5f) / sprays);
						Vector3 at = Curve(foot, foot + Vector3.up * height * 0.5f + lean * 0.2f, top, t);
						float yaw = rng.NextFloat() * Mathf.PI * 2f;
						var dir = (new Vector3(Mathf.Cos(yaw), rng.Range(0.1f, 0.6f), Mathf.Sin(yaw))).normalized;
						Spray(at, dir, sp.LeafSize, sp.LeafSize, crown, sp.LeafCell, 0.6f);
					}
				}
			}
		}
	}
}
#endif
