#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>The kind of small plant or ground clutter a detail mesh is.</summary>
	public enum DetailKind
	{
		Grass,
		Reeds,
		Fern,
		Flowers,
		Shrub,
		DryShrub,
		Debris,
		Kelp,
		Coral,
		BarrelCactus,
	}

	/// <summary>One detail plant's recipe.</summary>
	public struct DetailPlant
	{
		public string Name;
		public DetailKind Kind;
		/// <summary>Blades, fronds, flowers, cards or strands.</summary>
		public int Count;
		/// <summary>Typical height in metres.</summary>
		public float Height;
		/// <summary>Blade or card width in metres.</summary>
		public float Width;
		/// <summary>How far from the centre parts are planted, metres.</summary>
		public float Radius;
		/// <summary>The plant's own colour range.</summary>
		public Color ColourA, ColourB;
		/// <summary>Flower petals, coral tips: picked per part.</summary>
		public Color[] Accents;
		/// <summary>How far blades lean over, 0 upright .. 1 half over.</summary>
		public float Lean;
		/// <summary>Segments along each blade or frond.</summary>
		public int Segments;
	}

	/// <summary>
	/// Detail-channel meshes: grass clumps, reeds, ferns, flowers, small shrubs, ground debris,
	/// kelp, coral, small cacti. Each is one sub-mesh for one material, as Unity's detail
	/// prototypes require, small in triangle count (a grass clump is about 60), with the vertex
	/// layout <c>FishVegetation.shader</c> reads (see FishVegetationPasses.hlsl).
	/// </summary>
	/// <remarks>
	/// Normals on blades and cards are bent toward the sky: a clump of grass is lit as a soft
	/// mass the way it reads from a few metres away, not as forty flat cards each catching the
	/// sun at its own angle. Bark-textured parts (cactus) use real normals.
	/// </remarks>
	public static class VegetationMeshes
	{
		public static MeshBuilder Build(in DetailPlant plant, int seed)
		{
			var rng = new DeterministicRNG(ProceduralNoise.SeedFor(plant.Name, seed));
			var mesh = new MeshBuilder(1);
			DetailPlant p = plant;
			switch (p.Kind)
			{
				case DetailKind.Grass: Blades(mesh, in p, rng, FoliageCell.Blade); break;
				case DetailKind.Reeds: Blades(mesh, in p, rng, FoliageCell.Strap); break;
				case DetailKind.Fern: Fern(mesh, in p, rng); break;
				case DetailKind.Flowers: Flowers(mesh, in p, rng); break;
				case DetailKind.Shrub: Shrub(mesh, in p, rng, FoliageCell.SmallLeaves, 1f); break;
				case DetailKind.DryShrub: Shrub(mesh, in p, rng, FoliageCell.Twigs, 0f); break;
				case DetailKind.Debris: Debris(mesh, in p, rng); break;
				case DetailKind.Kelp: Kelp(mesh, in p, rng); break;
				case DetailKind.Coral: Coral(mesh, in p, rng); break;
				case DetailKind.BarrelCactus: BarrelCactus(mesh, in p, rng); break;
			}
			mesh.RecalculateNormals(true, onlyMissing: true);
			mesh.RecalculateTangents();
			return mesh;
		}

		private static Color Pick(in DetailPlant p, DeterministicRNG rng) => Color.Lerp(p.ColourA, p.ColourB, rng.NextFloat());

		private static void Blades(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng, FoliageCell cell)
		{
			int segments = Mathf.Max(1, p.Segments);
			var spine = new List<Vector3>();
			var widths = new List<float>();
			var colours = new List<Color32>();
			var wind = new List<Vector2>();
			for (int b = 0; b < p.Count; b++)
			{
				float angle = rng.NextFloat() * Mathf.PI * 2f;
				float r = Mathf.Sqrt(rng.NextFloat()) * p.Radius;
				var root = new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
				float height = p.Height * rng.Range(0.65f, 1.15f);
				float leanYaw = rng.NextFloat() * Mathf.PI * 2f;
				// Outer blades lean outward more: a clump opens like a fountain.
				float lean = p.Lean * rng.Range(0.3f, 1f) * (0.5f + r / Mathf.Max(0.01f, p.Radius));
				var leanDir = new Vector3(Mathf.Cos(leanYaw), 0f, Mathf.Sin(leanYaw));
				if (r > 1e-3f)
				{
					leanDir = Vector3.Slerp(leanDir, root.normalized, 0.6f);
				}
				Color colour = Pick(in p, rng);
				spine.Clear(); widths.Clear(); colours.Clear(); wind.Clear();
				bool pointed = cell == FoliageCell.Blade;
				for (int s = 0; s <= segments; s++)
				{
					float t = (float)s / segments;
					spine.Add(root + Vector3.up * (height * t * (1f - lean * 0.35f * t)) + leanDir * (height * lean * t * t));
					widths.Add(pointed && s == segments ? 0f : p.Width * (1f - 0.75f * Mathf.Pow(t, 1.4f)));
					// Darker at the root, where the clump shades itself.
					colours.Add(PlantParts.C32(colour * Mathf.Lerp(0.55f, 1f, t), 1f));
					wind.Add(new Vector2(Mathf.Pow(t, 1.5f) * height, 0.25f * t));
				}
				Vector3 side = Vector3.Cross(Vector3.up, leanDir);
				if (side.sqrMagnitude < 1e-6f)
				{
					side = Vector3.right;
				}
				// Each blade turned its own way about its axis, so a clump has no flat side.
				side = Quaternion.AngleAxis(rng.Range(-50f, 50f), Vector3.up) * side;
				Vector3 light = (Vector3.up * 0.75f + leanDir * 0.25f).normalized;
				PlantParts.Strip(mesh, 0, spine, widths, side, cell, colours, wind, light);
			}
		}

		private static void Fern(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			int segments = Mathf.Max(2, p.Segments);
			var spine = new List<Vector3>();
			var widths = new List<float>();
			var colours = new List<Color32>();
			var wind = new List<Vector2>();
			for (int f = 0; f < p.Count; f++)
			{
				float yaw = (f + rng.Range(-0.3f, 0.3f)) * Mathf.PI * 2f / p.Count;
				var dir = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
				float length = p.Height * rng.Range(0.8f, 1.2f);
				float elevation = Mathf.Deg2Rad * rng.Range(45f, 70f);
				float droop = rng.Range(0.5f, 0.8f);
				Color colour = Pick(in p, rng);
				spine.Clear(); widths.Clear(); colours.Clear(); wind.Clear();
				for (int s = 0; s <= segments; s++)
				{
					float t = (float)s / segments;
					// Rises from the crown and arches over under its own weight.
					spine.Add(dir * (length * t * Mathf.Cos(elevation)) + Vector3.up * (length * (Mathf.Sin(elevation) * t - droop * t * t)) + dir * p.Radius);
					widths.Add(p.Width);
					colours.Add(PlantParts.C32(colour * Mathf.Lerp(0.7f, 1f, t), 1f));
					wind.Add(new Vector2(t * length * 0.6f, 0.4f * t));
				}
				Vector3 side = Vector3.Cross(Vector3.up, dir);
				PlantParts.Strip(mesh, 0, spine, widths, side, FoliageCell.Frond, colours, wind, (Vector3.up * 0.7f + dir * 0.3f).normalized);
			}
		}

		private static void Flowers(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			var spine = new List<Vector3>();
			var widths = new List<float>();
			var colours = new List<Color32>();
			var wind = new List<Vector2>();
			for (int f = 0; f < p.Count; f++)
			{
				float angle = rng.NextFloat() * Mathf.PI * 2f;
				float r = Mathf.Sqrt(rng.NextFloat()) * p.Radius;
				var root = new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
				float height = p.Height * rng.Range(0.7f, 1.2f);
				var lean = new Vector3(rng.Range(-0.15f, 0.15f), 0f, rng.Range(-0.15f, 0.15f)) * height;
				Vector3 top = root + Vector3.up * height + lean;
				Color stem = Pick(in p, rng);
				spine.Clear(); widths.Clear(); colours.Clear(); wind.Clear();
				for (int s = 0; s <= 2; s++)
				{
					float t = s / 2f;
					spine.Add(Vector3.Lerp(root, top, t));
					widths.Add(0.012f);
					colours.Add(PlantParts.C32(stem * Mathf.Lerp(0.6f, 1f, t), 1f));
					wind.Add(new Vector2(Mathf.Pow(t, 1.5f) * height, 0.1f));
				}
				PlantParts.Strip(mesh, 0, spine, widths, PlantParts.Perpendicular(top - root), FoliageCell.Blade, colours, wind, Vector3.up);

				// The head: a card facing mostly up and one across it, so it reads from above and the side.
				Color petal = p.Accents != null && p.Accents.Length > 0 ? p.Accents[rng.Next(p.Accents.Length)] : Color.white;
				Color32 head = PlantParts.C32(petal, 0.3f);
				float size = p.Width * rng.Range(0.8f, 1.2f);
				float yaw = rng.NextFloat() * 360f;
				Quaternion turn = Quaternion.Euler(0f, yaw, 0f);
				Vector3 right = turn * Vector3.right * size;
				Vector3 tilted = turn * Quaternion.Euler(-60f, 0f, 0f) * Vector3.up * size;
				var headWind = new Vector2(height, 0.5f);
				PlantParts.Card(mesh, 0, top, right, tilted, FoliageCell.Flower, head, headWind, headWind, Vector3.up);
				PlantParts.Card(mesh, 0, top, turn * Vector3.forward * size, tilted, FoliageCell.Flower, head, headWind, headWind, Vector3.up);
			}
			// A few leaves at the base.
			DetailPlant leaves = p;
			leaves.Count = Mathf.Max(2, p.Count / 2);
			leaves.Height = p.Height * 0.35f;
			leaves.Width = 0.03f;
			leaves.Segments = 2;
			leaves.Lean = 0.5f;
			Blades(mesh, in leaves, rng, FoliageCell.Blade);
		}

		private static void Shrub(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng, FoliageCell cell, float tintable)
		{
			var centre = new Vector3(0f, p.Height * 0.5f, 0f);
			for (int k = 0; k < p.Count; k++)
			{
				// Cards spread over a dome, each facing roughly out from the bush's heart.
				float yaw = (k + rng.Range(-0.4f, 0.4f)) * Mathf.PI * 2f / p.Count;
				float up = rng.Range(0.1f, 0.9f);
				var outward = new Vector3(Mathf.Cos(yaw) * (1f - up * 0.6f), up, Mathf.Sin(yaw) * (1f - up * 0.6f)).normalized;
				Vector3 root = centre * 0.4f + outward * p.Radius * 0.3f;
				Color colour = Pick(in p, rng);
				float length = p.Height * rng.Range(0.7f, 1.0f);
				var direction = (outward + Vector3.up * 0.8f).normalized;
				PlantParts.SprayCard(mesh, 0, root, direction, length, p.Width * rng.Range(0.8f, 1.2f), rng.Range(-40f, 40f), cell,
					PlantParts.C32(colour, tintable), new Vector2(0.05f, 0.1f), new Vector2(length * 0.5f, 0.6f), centre, 0.7f);
			}
		}

		private static void Debris(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			for (int k = 0; k < p.Count; k++)
			{
				float angle = rng.NextFloat() * Mathf.PI * 2f;
				float r = Mathf.Sqrt(rng.NextFloat()) * p.Radius;
				var centre = new Vector3(Mathf.Cos(angle) * r, 0.01f + k * 0.003f, Mathf.Sin(angle) * r);
				Quaternion yaw = Quaternion.Euler(0f, rng.NextFloat() * 360f, 0f);
				bool twig = (k % 3) == 0;
				float size = p.Width * rng.Range(0.7f, 1.3f);
				Color colour = Pick(in p, rng);
				PlantParts.Card(mesh, 0, centre, yaw * Vector3.right * size, yaw * Vector3.forward * size * (twig ? 1.4f : 1f),
					twig ? FoliageCell.Twigs : FoliageCell.SmallLeaves, PlantParts.C32(colour, twig ? 0f : 0.6f), Vector2.zero, Vector2.zero, Vector3.up);
			}
		}

		private static void Kelp(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			int segments = Mathf.Max(3, p.Segments);
			var spine = new List<Vector3>();
			var widths = new List<float>();
			var colours = new List<Color32>();
			var wind = new List<Vector2>();
			for (int k = 0; k < p.Count; k++)
			{
				float angle = rng.NextFloat() * Mathf.PI * 2f;
				var root = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle)) * (p.Radius * Mathf.Sqrt(rng.NextFloat()));
				float height = p.Height * rng.Range(0.7f, 1.2f);
				float phase = rng.NextFloat() * Mathf.PI * 2f;
				var wave = new Vector3(Mathf.Cos(angle + 1.3f), 0f, Mathf.Sin(angle + 1.3f));
				Color colour = Pick(in p, rng);
				spine.Clear(); widths.Clear(); colours.Clear(); wind.Clear();
				for (int s = 0; s <= segments; s++)
				{
					float t = (float)s / segments;
					spine.Add(root + Vector3.up * height * t + wave * (Mathf.Sin(t * 5f + phase) * 0.15f * t));
					widths.Add(p.Width * (s == segments ? 0.3f : 1f));
					colours.Add(PlantParts.C32(colour * Mathf.Lerp(0.6f, 1f, t), 1f));
					wind.Add(new Vector2(t * height * 0.5f, 0.3f * t));
				}
				PlantParts.Strip(mesh, 0, spine, widths, Vector3.Cross(Vector3.up, wave), FoliageCell.Strap, colours, wind, Vector3.zero);
			}
		}

		private static void Coral(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			var path = new List<Vector3>();
			var radii = new List<float>();
			var colours = new List<Color32>();
			var wind = new List<Vector2>();
			Vector2 SolidUV(float u, float v) => FoliageAtlas.CellUV(FoliageCell.Solid, u, Mathf.Repeat(v, 1f));
			void Branch(Vector3 start, Vector3 dir, float length, float radius, Color colour, int depth)
			{
				path.Clear(); radii.Clear(); colours.Clear(); wind.Clear();
				for (int s = 0; s <= 3; s++)
				{
					float t = s / 3f;
					path.Add(start + dir * length * t + Vector3.up * (length * 0.2f * t * t));
					radii.Add(radius * (s == 3 ? 0f : 1f - 0.4f * t));
					colours.Add(PlantParts.C32(colour * Mathf.Lerp(0.75f, 1.05f, t), 0f));
					wind.Add(Vector2.zero);
				}
				PlantParts.Tube(mesh, 0, path, radii, 4, 1f, colours, wind, uvMap: SolidUV);
				if (depth <= 0)
				{
					return;
				}
				Vector3 tip = start + dir * length * 0.7f + Vector3.up * (length * 0.1f);
				for (int c = 0; c < 2; c++)
				{
					Vector3 child = Quaternion.AngleAxis(rng.Range(-40f, 40f), Vector3.up) * Quaternion.AngleAxis(rng.Range(-30f, 30f), PlantParts.Perpendicular(dir)) * dir;
					Branch(tip, (child + Vector3.up * 0.4f).normalized, length * 0.6f, radius * 0.65f, colour, depth - 1);
				}
			}
			for (int k = 0; k < p.Count; k++)
			{
				float yaw = (k + rng.Range(-0.3f, 0.3f)) * Mathf.PI * 2f / p.Count;
				var dir = (new Vector3(Mathf.Cos(yaw), 1.4f, Mathf.Sin(yaw))).normalized;
				Color colour = p.Accents != null && p.Accents.Length > 0 ? p.Accents[rng.Next(p.Accents.Length)] : p.ColourA;
				Branch(Vector3.zero, dir, p.Height * rng.Range(0.6f, 0.9f), p.Width, colour, 1);
			}
		}

		private static void BarrelCactus(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			var path = new List<Vector3>();
			var radii = new List<float>();
			var colours = new List<Color32>();
			var wind = new List<Vector2>();
			int rings = 6;
			for (int i = 0; i <= rings; i++)
			{
				float t = (float)i / rings;
				path.Add(Vector3.up * (p.Height * t) + Vector3.down * 0.02f);
				// A squat barrel: widest a third of the way up, closing to a point at the crown.
				radii.Add(i == rings ? 0f : p.Radius * Mathf.Sin(Mathf.PI * (0.25f + 0.75f * t)) * (1f - 0.15f * t));
				colours.Add(PlantParts.C32(Color.white, 0f));
				wind.Add(Vector2.zero);
			}
			PlantParts.Tube(mesh, 0, path, radii, 14, 0.5f, colours, wind, ribs: 14, ribDepth: 0.12f, analyticNormals: false);
		}
	}
}
#endif
