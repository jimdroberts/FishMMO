#if UNITY_EDITOR
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The plants that float on still fresh water (2026-10-18, phase I2i, decision 27): a duckweed carpet, water-lily pads with a
	/// flower or two, water hyacinth rosettes with their lilac spikes, frogbit's little kidney leaves. One detail mesh each, one
	/// sub-mesh, the vegetation shader's vertex layout, under the detail budget.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Built at the water line.</b> y = 0 is the water's surface: pads and fronds lie a centimetre or two over it (so the water,
	/// drawn after the opaque world, never covers them and they never flicker into it), leaves and flowers rise from there. The
	/// cut lays each as a baked prop with its origin on the water (FloatingPlants), never as a terrain detail: a detail stands on
	/// the heightmap, the lake's bed.
	/// </para>
	/// <para>
	/// <b>Still, upright, lit from the sky.</b> Every normal is bent up as the grass's are, so a carpet is lit as a sheet; nothing
	/// carries wind weight (the material holds them still: a pad does not lean in a breeze), and every part is a leaf part, so
	/// the shader's fullness thins one patch and fills the next.
	/// </para>
	/// </remarks>
	public static class FloatingPlantMeshes
	{
		/// <summary>How high pads and fronds lie over the water, metres.</summary>
		public const float PadLift = 0.012f;

		/// <summary>True for the kinds built here (they lie on the water, not under it and not on the ground).</summary>
		public static bool IsFloating(DetailKind kind) => kind == DetailKind.Duckweed || kind == DetailKind.WaterLily || kind == DetailKind.WaterHyacinth || kind == DetailKind.Frogbit;

		/// <summary>Builds <paramref name="p"/> when its kind is one of these; false (touching nothing) otherwise.</summary>
		public static bool Build(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			switch (p.Kind)
			{
				case DetailKind.Duckweed: Duckweed(mesh, in p, rng); return true;
				case DetailKind.WaterLily: WaterLily(mesh, in p, rng); return true;
				case DetailKind.WaterHyacinth: WaterHyacinth(mesh, in p, rng); return true;
				case DetailKind.Frogbit: Frogbit(mesh, in p, rng); return true;
				default: return false;
			}
		}

		// ── Shared ────────────────────────────────────────────────────

		private const int Budget = FloraMeshes.PlantBudget;

		private static bool Fits(MeshBuilder mesh, int cost) => mesh.TriangleCount + cost <= Budget;

		private static Color Pick(in DetailPlant p, DeterministicRNG rng) => VegetationMeshes.Pick(in p, rng);

		private static Color Accent(in DetailPlant p, int i, Color fallback) =>
			p.Accents != null && p.Accents.Length > 0 ? p.Accents[((i % p.Accents.Length) + p.Accents.Length) % p.Accents.Length] : fallback;

		private static Vector2 SolidUV(float u, float v) => FoliageAtlas.CellUV(FoliageCell.Solid, Mathf.Clamp01(u), Mathf.Clamp01(v));

		/// <summary>A point in a disc on the water, thicker toward the middle when <paramref name="gather"/> is above 1.</summary>
		private static Vector3 Disc(DeterministicRNG rng, float radius, float gather = 1f)
		{
			float angle = rng.NextFloat() * Mathf.PI * 2f;
			float r = Mathf.Pow(rng.NextFloat(), 0.5f * gather) * radius;
			return new Vector3(Mathf.Cos(angle) * r, 0f, Mathf.Sin(angle) * r);
		}

		/// <summary>
		/// A flat round leaf lying on the water (a lily's pad, frogbit's kidney): a fan of <paramref name="segments"/> triangles about
		/// its middle, a wedge of <paramref name="notch"/> radians left open where its stalk meets it, its rim turned up by
		/// <paramref name="rimLift"/>. Faces up.
		/// </summary>
		private static void Pad(MeshBuilder mesh, Vector3 centre, float radius, float yaw, float notch, int segments, float rimLift, Color32 middle, Color32 rim)
		{
			// With no notch the last rim point lies on the first, so the fan closes on itself.
			int c = mesh.AddVertex(centre, Vector3.up, SolidUV(0.5f, 0.5f), middle);
			int previous = -1;
			float sweep = Mathf.PI * 2f - notch;
			for (int s = 0; s <= segments; s++)
			{
				float a = yaw + 0.5f * notch + sweep * s / segments;
				var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
				// The rim a little ragged, as a pad nibbled and torn.
				float r = radius * (0.94f + 0.06f * Mathf.Cos(a * 5f + yaw * 3f));
				int v = mesh.AddVertex(centre + dir * r + Vector3.up * rimLift, Vector3.up, SolidUV(0.5f + 0.5f * dir.x, 0.5f + 0.5f * dir.z), rim);
				if (previous >= 0)
				{
					mesh.AddTriangle(0, c, previous, v, Vector3.up);
				}
				previous = v;
			}
		}

		/// <summary>One petal from <paramref name="foot"/> out along <paramref name="dir"/> (rising), a pointed diamond <paramref name="width"/> across: 2 triangles.</summary>
		private static void Petal(MeshBuilder mesh, Vector3 foot, Vector3 dir, float length, float width, Color32 colour, Color32 tip)
		{
			Vector3 side = Vector3.Cross(Vector3.up, dir);
			side = side.sqrMagnitude > 1e-8f ? side.normalized * (0.5f * width) : Vector3.right * (0.5f * width);
			Vector3 mid = foot + dir * (0.45f * length);
			int a = mesh.AddVertex(foot, Vector3.up, SolidUV(0.5f, 0f), colour);
			int b = mesh.AddVertex(mid + side, Vector3.up, SolidUV(1f, 0.45f), colour);
			int c = mesh.AddVertex(foot + dir * length, Vector3.up, SolidUV(0.5f, 1f), tip);
			int d = mesh.AddVertex(mid - side, Vector3.up, SolidUV(0f, 0.45f), colour);
			Vector3 face = Vector3.Cross(side, dir);
			if (face.y < 0f)
			{
				face = -face;
			}
			mesh.AddQuad(0, a, b, c, d, face.sqrMagnitude > 1e-10f ? face.normalized : Vector3.up);
		}

		/// <summary>A flower open on the water: <paramref name="rings"/> rings of <paramref name="perRing"/> petals, each ring standing higher, round a small heart.</summary>
		private static void Flower(MeshBuilder mesh, Vector3 at, float radius, int rings, int perRing, float yaw, Color petal, Color heart)
		{
			Color32 p32 = PlantParts.C32(petal, 1f), tip = PlantParts.C32(Color.Lerp(petal, Color.white, 0.25f), 1f);
			for (int ring = 0; ring < rings; ring++)
			{
				float rise = Mathf.Lerp(20f, 62f, rings > 1 ? ring / (float)(rings - 1) : 0f) * Mathf.Deg2Rad;
				float length = radius * Mathf.Lerp(1f, 0.72f, rings > 1 ? ring / (float)(rings - 1) : 0f);
				for (int k = 0; k < perRing; k++)
				{
					float a = yaw + (k + 0.5f * ring) * Mathf.PI * 2f / perRing;
					var dir = new Vector3(Mathf.Cos(a) * Mathf.Cos(rise), Mathf.Sin(rise), Mathf.Sin(a) * Mathf.Cos(rise));
					Petal(mesh, at + Vector3.up * (0.004f * ring), dir, length, length * 0.42f, p32, tip);
				}
			}
			Pad(mesh, at + Vector3.up * 0.012f, radius * 0.22f, yaw, 0f, 6, 0.006f, PlantParts.C32(heart, 1f), PlantParts.C32(heart * 0.85f, 1f));
		}

		// ── Duckweed ──────────────────────────────────────────────────

		/// <summary>
		/// A carpet of duckweed: <see cref="DetailPlant.Count"/> flat cards of the atlas's small-leaves cell, each a scatter of
		/// tiny round fronds, lying overlapped on the water over <see cref="DetailPlant.Radius"/>, thicker toward the middle so a
		/// patch's edge breaks up into open water.
		/// </summary>
		private static void Duckweed(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			(int cards, int core) = VegetationMeshes.Spared(p.Count, 1.15f);
			for (int k = 0; k < cards && Fits(mesh, 2); k++)
			{
				Vector3 at = Disc(rng, Mathf.Max(0.05f, p.Radius), 1.3f);
				at.y = PadLift * 0.5f + 0.0004f * (k % 7);
				VegetationMeshes.Part(mesh, k, cards, core, at, PlantPart.Leaf, 2010);
				float yaw = rng.NextFloat() * Mathf.PI * 2f;
				float size = Mathf.Max(0.02f, p.Width) * rng.Range(0.75f, 1.3f);
				var right = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw)) * size;
				Vector3 up = Vector3.Cross(Vector3.up, right);
				PlantParts.Card(mesh, 0, at, right, up, FoliageCell.SmallLeaves, PlantParts.C32(Pick(in p, rng), 1f), Vector2.zero, Vector2.zero, Vector3.up);
			}
		}

		// ── Water-lily ────────────────────────────────────────────────

		/// <summary>
		/// A clump of water-lily pads: <see cref="DetailPlant.Count"/> round pads <see cref="DetailPlant.Width"/> in radius with the
		/// notch where the stalk meets them, the rim a little darker and redder, over <see cref="DetailPlant.Radius"/>; a flower or
		/// two among them (two rings of eight petals in the first accent, a golden heart in the second), standing up to
		/// <see cref="DetailPlant.Height"/>.
		/// </summary>
		private static void WaterLily(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			int pads = Mathf.Max(1, p.Count);
			const int Segments = 12;
			for (int k = 0; k < pads && Fits(mesh, Segments); k++)
			{
				Vector3 at = Disc(rng, Mathf.Max(0.05f, p.Radius));
				at.y = PadLift + 0.0015f * (k % 4);
				VegetationMeshes.Part(mesh, k, pads, pads, at, PlantPart.Leaf, 2020);
				float radius = Mathf.Max(0.03f, p.Width) * rng.Range(0.7f, 1.25f);
				Color pad = Pick(in p, rng);
				Color rim = Color.Lerp(pad, Accent(in p, 2, new Color(0.45f, 0.2f, 0.15f)), 0.35f);
				Pad(mesh, at, radius, rng.NextFloat() * Mathf.PI * 2f, 0.35f, Segments, 0.004f, PlantParts.C32(pad, 1f), PlantParts.C32(rim, 1f));
			}
			int flowers = 1 + (VegetationMeshes.Hash(p.Count, 2021) < 0.5f ? 1 : 0);
			for (int f = 0; f < flowers && Fits(mesh, 2 * 16 + 6); f++)
			{
				Vector3 at = Disc(rng, Mathf.Max(0.05f, p.Radius) * 0.7f);
				at.y = PadLift + 0.01f;
				VegetationMeshes.Part(mesh, pads + f, pads + flowers, pads + flowers, at, PlantPart.Leaf, 2022);
				float radius = Mathf.Max(0.03f, p.Height) * rng.Range(0.8f, 1.1f);
				Flower(mesh, at, radius, 2, 8, rng.NextFloat() * Mathf.PI * 2f, Accent(in p, 0, Color.white), Accent(in p, 1, new Color(0.95f, 0.78f, 0.2f)));
			}
		}

		// ── Water hyacinth ────────────────────────────────────────────

		/// <summary>
		/// A mat of water hyacinth: <see cref="DetailPlant.Count"/> rosettes over <see cref="DetailPlant.Radius"/>, each of six
		/// glossy rounded leaves (the atlas's paddle cell) on swollen stalks rising from the water, and every other one a lilac
		/// flower spike up to <see cref="DetailPlant.Height"/>.
		/// </summary>
		private static void WaterHyacinth(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			int rosettes = Mathf.Max(1, p.Count);
			float leafWidth = Mathf.Max(0.04f, p.Width);
			for (int r = 0; r < rosettes && Fits(mesh, 6 * 4); r++)
			{
				Vector3 centre = Disc(rng, Mathf.Max(0.05f, p.Radius));
				centre.y = PadLift * 0.5f;
				float turn = rng.NextFloat() * Mathf.PI * 2f;
				float scale = rng.Range(0.8f, 1.15f);
				for (int k = 0; k < 6; k++)
				{
					VegetationMeshes.Part(mesh, r * 6 + k, rosettes * 6, rosettes * 5, centre, PlantPart.Leaf, 2030);
					float yaw = turn + k * Mathf.PI * 2f / 6f + rng.Range(-0.2f, 0.2f);
					var flat = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
					// The swollen stalk, out and up from the rosette's middle.
					float stalk = leafWidth * 0.9f * scale;
					Vector3 stalkDir = (flat * Mathf.Cos(0.75f) + Vector3.up * Mathf.Sin(0.75f)).normalized;
					Vector3 foot = centre + flat * 0.01f;
					Vector3 head = foot + stalkDir * stalk;
					Vector3 side = Vector3.Cross(Vector3.up, flat).normalized;
					Color stalkColour = Color.Lerp(Pick(in p, rng), new Color(0.6f, 0.75f, 0.4f), 0.4f);
					PlantParts.Card(mesh, 0, (foot + head) * 0.5f, side * (leafWidth * 0.45f * scale), head - foot, FoliageCell.Solid,
						PlantParts.C32(stalkColour, 1f), Vector2.zero, Vector2.zero, Vector3.up);
					// The leaf blade above it, standing more upright.
					Vector3 bladeDir = (flat * Mathf.Cos(1.15f) + Vector3.up * Mathf.Sin(1.15f)).normalized;
					float blade = leafWidth * 1.3f * scale;
					PlantParts.Card(mesh, 0, head + bladeDir * (0.5f * blade), side * (leafWidth * scale), bladeDir * blade, FoliageCell.Paddle,
						PlantParts.C32(Pick(in p, rng), 1f), Vector2.zero, Vector2.zero, Vector3.up);
				}
				if (r % 2 == 0 && Fits(mesh, 4 + 6 * 4))
				{
					// A spike of lilac flowers: a stem, then flowers in crossed pairs up its top half.
					VegetationMeshes.Part(mesh, rosettes * 6 + r, rosettes * 7, rosettes * 6, centre, PlantPart.Leaf, 2031);
					float height = Mathf.Max(0.1f, p.Height) * scale;
					Color lilac = Accent(in p, 0, new Color(0.62f, 0.5f, 0.85f));
					PlantParts.Card(mesh, 0, centre + Vector3.up * (0.5f * height), Vector3.right * 0.012f, Vector3.up * height, FoliageCell.Solid,
						PlantParts.C32(new Color(0.35f, 0.5f, 0.25f), 1f), Vector2.zero, Vector2.zero, Vector3.up);
					PlantParts.Card(mesh, 0, centre + Vector3.up * (0.5f * height), Vector3.forward * 0.012f, Vector3.up * height, FoliageCell.Solid,
						PlantParts.C32(new Color(0.35f, 0.5f, 0.25f), 1f), Vector2.zero, Vector2.zero, Vector3.up);
					for (int f = 0; f < 6; f++)
					{
						float y = height * (0.55f + 0.4f * f / 5f);
						float yaw = turn + f * 2.4f;
						var across = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
						PlantParts.Card(mesh, 0, centre + Vector3.up * y, across * 0.06f, Vector3.up * 0.05f, FoliageCell.Flower,
							PlantParts.C32(lilac, 1f), Vector2.zero, Vector2.zero, Vector3.up);
						PlantParts.Card(mesh, 0, centre + Vector3.up * y, Vector3.Cross(Vector3.up, across) * 0.06f, Vector3.up * 0.05f, FoliageCell.Flower,
							PlantParts.C32(lilac, 1f), Vector2.zero, Vector2.zero, Vector3.up);
					}
				}
			}
		}

		// ── Frogbit ───────────────────────────────────────────────────

		/// <summary>
		/// Frogbit: <see cref="DetailPlant.Count"/> rosettes over <see cref="DetailPlant.Radius"/>, each of four or five little
		/// kidney-shaped leaves <see cref="DetailPlant.Width"/> in radius lying on the water round its middle, every third one with a
		/// three-petalled white flower held just above them.
		/// </summary>
		private static void Frogbit(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			int rosettes = Mathf.Max(1, p.Count);
			const int Segments = 8;
			for (int r = 0; r < rosettes && Fits(mesh, 5 * Segments); r++)
			{
				Vector3 centre = Disc(rng, Mathf.Max(0.05f, p.Radius));
				centre.y = PadLift;
				float turn = rng.NextFloat() * Mathf.PI * 2f;
				int leaves = 4 + (VegetationMeshes.Hash(r, 2041) < 0.5f ? 1 : 0);
				for (int k = 0; k < leaves; k++)
				{
					VegetationMeshes.Part(mesh, r * 5 + k, rosettes * 5, rosettes * 4, centre, PlantPart.Leaf, 2040);
					float yaw = turn + k * Mathf.PI * 2f / leaves;
					float radius = Mathf.Max(0.01f, p.Width) * rng.Range(0.8f, 1.15f);
					var flat = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
					Color leaf = Pick(in p, rng);
					// Notched toward the rosette's middle, where its stalk joins.
					Pad(mesh, centre + flat * (radius * 0.9f) + Vector3.up * (0.0008f * k), radius, yaw + Mathf.PI - 0.2f, 0.4f, Segments, 0.002f,
						PlantParts.C32(leaf, 1f), PlantParts.C32(leaf * 0.85f, 1f));
				}
				if (r % 3 == 0 && Fits(mesh, 3 * 2 + 6))
				{
					VegetationMeshes.Part(mesh, rosettes * 5 + r, rosettes * 6, rosettes * 5, centre, PlantPart.Leaf, 2042);
					Flower(mesh, centre + Vector3.up * 0.02f, Mathf.Max(0.01f, p.Height), 1, 3, turn, Accent(in p, 0, new Color(0.97f, 0.96f, 0.92f)),
						Accent(in p, 1, new Color(0.95f, 0.85f, 0.3f)));
				}
			}
		}
	}
}
#endif
