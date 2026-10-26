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
		public static bool IsFloating(DetailKind kind)
		{
			switch (kind)
			{
				case DetailKind.Duckweed:
				case DetailKind.WaterLily:
				case DetailKind.WaterHyacinth:
				case DetailKind.Frogbit:
				// The drafts' kinds (2026-10-25, I2i-b).
				case DetailKind.Tasselweed:
				case DetailKind.SacredLotus:
				case DetailKind.WaterChestnut:
				case DetailKind.Watercress:
				case DetailKind.AmericanLotus:
				case DetailKind.WaterClover:
				case DetailKind.WaterLettuce:
				case DetailKind.WaterSnowflake:
				case DetailKind.Ottelia:
				case DetailKind.Azolla:
				case DetailKind.FloatingHeart:
				case DetailKind.WaterSoldier:
				case DetailKind.WaterFern:
				case DetailKind.Bladderwort:
				case DetailKind.Spongeplant:
				case DetailKind.Pennywort:
				case DetailKind.BergyBits:
				case DetailKind.SaltRaft:
				case DetailKind.AmphibiousBistort:
				case DetailKind.Starwort:
				case DetailKind.WaterCrowfoot:
				case DetailKind.Pondweed:
				case DetailKind.BlueLotus:
				case DetailKind.FrothRaft:
				case DetailKind.PropaguleRaft:
				case DetailKind.TanninFoam:
				case DetailKind.Gutweed:
					return true;
				default:
					return false;
			}
		}

		/// <summary>Builds <paramref name="p"/> when its kind is one of these; false (touching nothing) otherwise.</summary>
		public static bool Build(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			switch (p.Kind)
			{
				case DetailKind.Duckweed: Duckweed(mesh, in p, rng); return true;
				case DetailKind.WaterLily: WaterLily(mesh, in p, rng); return true;
				case DetailKind.WaterHyacinth: WaterHyacinth(mesh, in p, rng); return true;
				case DetailKind.Frogbit: Frogbit(mesh, in p, rng); return true;
				default: return BuildDrafted(mesh, in p, rng);
			}
		}

		/// <summary>
		/// How far a kind's mesh may reach under the water line (x) and over it (y), metres: the old four lie on it (a hyacinth's
		/// spike stands as tall as it grows); a lotus raises leaves and flowers, a water soldier and the bergy bits ride half sunk,
		/// Ottelia's ribbons, tasselweed's threads and bladderwort's tangle lie under it.
		/// </summary>
		public static Vector2 Reach(in DetailPlant p)
		{
			float w = Mathf.Max(0f, p.Width), h = Mathf.Max(0f, p.Height);
			switch (p.Kind)
			{
				case DetailKind.WaterHyacinth: return new Vector2(0f, float.PositiveInfinity);
				case DetailKind.Tasselweed: return new Vector2(0.03f, 0.15f);
				case DetailKind.SacredLotus:
				case DetailKind.AmericanLotus: return new Vector2(0f, 1.15f * h + 0.6f * w);
				case DetailKind.WaterLettuce: return new Vector2(0f, 1.2f * w + 0.03f);
				case DetailKind.Ottelia: return new Vector2(0.25f, 0.15f);
				case DetailKind.WaterSoldier: return new Vector2(0.16f, 1.15f * h + 0.03f);
				case DetailKind.Bladderwort: return new Vector2(0.17f, h + 0.06f);
				case DetailKind.BergyBits: return new Vector2(0.9f * w + 0.01f, 0.35f * w + 0.01f);
				case DetailKind.AmphibiousBistort:
				case DetailKind.Pondweed: return new Vector2(0f, h + 0.07f);
				case DetailKind.BlueLotus: return new Vector2(0f, BlueLotusLift + h + 0.04f);
				default: return new Vector2(0f, 0.15f);
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
		private static void Flower(MeshBuilder mesh, Vector3 at, float radius, int rings, int perRing, float yaw, Color petal, Color heart,
			float riseFrom = 20f, float riseTo = 62f, float widthShare = 0.42f)
		{
			Color32 p32 = PlantParts.C32(petal, 1f), tip = PlantParts.C32(Color.Lerp(petal, Color.white, 0.25f), 1f);
			for (int ring = 0; ring < rings; ring++)
			{
				float rise = Mathf.Lerp(riseFrom, riseTo, rings > 1 ? ring / (float)(rings - 1) : 0f) * Mathf.Deg2Rad;
				float length = radius * Mathf.Lerp(1f, 0.72f, rings > 1 ? ring / (float)(rings - 1) : 0f);
				for (int k = 0; k < perRing; k++)
				{
					float a = yaw + (k + 0.5f * ring) * Mathf.PI * 2f / perRing;
					var dir = new Vector3(Mathf.Cos(a) * Mathf.Cos(rise), Mathf.Sin(rise), Mathf.Sin(a) * Mathf.Cos(rise));
					Petal(mesh, at + Vector3.up * (0.004f * ring), dir, length, length * widthShare, p32, tip);
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

		// ══ The drafts' kinds (2026-10-25, phase I2i-b) ═══════════════════
		// Each from the shared parts above and below: carpets of cards, rosettes and chains of flat leaves, lotus dishes on stalks,
		// sword rosettes half sunk, plates of salt, froth and foam, and lumps of ice. Built at the water line as the first four; a
		// kind's Reach says how far under and over it may go. Things that are not plants (ice, salt, froth, foam, propagules) are
		// fixed parts, never thinned by the shader's fullness.

		/// <summary>How high a blue lotus holds its flowers over the water, metres.</summary>
		public const float BlueLotusLift = 0.1f;

		private static bool BuildDrafted(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			switch (p.Kind)
			{
				case DetailKind.Tasselweed: Tasselweed(mesh, in p, rng); return true;
				case DetailKind.SacredLotus:
				case DetailKind.AmericanLotus: Lotus(mesh, in p, rng); return true;
				case DetailKind.WaterChestnut:
					Rosettes(mesh, in p, rng, 8, 11, 1f, 0.75f, 0.9f, 0f, 8, true, 0.12f, 2, 4, 1, 0.015f, 2100);
					return true;
				case DetailKind.Watercress: Carpet(mesh, in p, rng, FoliageCell.SmallLeaves, 0.6f, PadLift * 0.5f, 2110, false, 5); return true;
				case DetailKind.WaterClover: Rosettes(mesh, in p, rng, 4, 4, 1f, 0.9f, 1f, 0f, 6, false, 0f, 0, 0, 0, 0f, 2120); return true;
				case DetailKind.WaterLettuce: Lettuce(mesh, in p, rng); return true;
				case DetailKind.WaterSnowflake:
				case DetailKind.FloatingHeart:
					// Heart-shaped pads (the notch to the rosette's middle), a fringed five-petalled flower on every other rosette.
					Rosettes(mesh, in p, rng, 2, 3, 1f, 0.95f, 0.9f, 0.5f, 10, false, 0f, 2, 5, 2, 0.012f, 2130);
					return true;
				case DetailKind.Ottelia: Ottelia(mesh, in p, rng); return true;
				case DetailKind.Azolla: Carpet(mesh, in p, rng, FoliageCell.SmallLeaves, 1f, PadLift * 0.5f, 2150, true, 0); return true;
				case DetailKind.WaterSoldier: WaterSoldier(mesh, in p, rng); return true;
				case DetailKind.WaterFern: Carpet(mesh, in p, rng, FoliageCell.Frond, 0.4f, PadLift * 0.6f, 2170, false, 0); return true;
				case DetailKind.Bladderwort: Bladderwort(mesh, in p, rng); return true;
				case DetailKind.Spongeplant: Rosettes(mesh, in p, rng, 5, 7, 1f, 0.95f, 1f, 0f, 8, false, 0f, 3, 3, 1, 0.012f, 2190); return true;
				case DetailKind.Pennywort: Pennywort(mesh, in p, rng); return true;
				case DetailKind.BergyBits: BergyBits(mesh, in p, rng); return true;
				case DetailKind.SaltRaft: Plates(mesh, in p, rng, 0.004f, 0f, false, PlantPart.Fixed, 2220); return true;
				case DetailKind.AmphibiousBistort:
				case DetailKind.Pondweed: Spikes(mesh, in p, rng); return true;
				case DetailKind.Starwort: Rosettes(mesh, in p, rng, 8, 8, 1f, 0.4f, 0.6f, 0f, 5, false, 0f, 0, 0, 0, 0f, 2240); return true;
				case DetailKind.WaterCrowfoot: Crowfoot(mesh, in p, rng); return true;
				case DetailKind.BlueLotus: BlueLotus(mesh, in p, rng); return true;
				case DetailKind.FrothRaft: Plates(mesh, in p, rng, 0.006f, 0f, true, PlantPart.Fixed, 2270); return true;
				case DetailKind.PropaguleRaft: Propagules(mesh, in p, rng); return true;
				case DetailKind.TanninFoam: Plates(mesh, in p, rng, 0.006f, 0f, true, PlantPart.Fixed, 2290); return true;
				case DetailKind.Gutweed: Plates(mesh, in p, rng, 0.002f, 0.004f, false, PlantPart.Leaf, 2300); return true;
				default: return false;
			}
		}

		/// <summary>Every later part a fixed one: drawn whole, never thinned (ice, salt, froth, foam, a raft of seedlings).</summary>
		private static void FixedPart(MeshBuilder mesh, Vector3 root) => mesh.SetPart(root, 0f, 0f, PlantPart.Fixed);

		/// <summary>
		/// A leaf lying on the water, an ellipse <paramref name="length"/> by <paramref name="width"/> (half-axes) about
		/// <paramref name="centre"/>, its long axis along <paramref name="yaw"/>: a fan of <paramref name="segments"/> triangles, a
		/// wedge of <paramref name="notch"/> radians left open at its foot (toward −yaw, where its stalk meets it), drawn to a rhomb
		/// when <paramref name="diamond"/>, every other rim point pushed out by <paramref name="teeth"/> (a toothed edge). Faces up.
		/// </summary>
		private static void Oval(MeshBuilder mesh, Vector3 centre, float length, float width, float yaw, float notch, int segments, bool diamond, float teeth,
			float rimLift, Color32 middle, Color32 rim)
		{
			var major = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
			var minor = new Vector3(-major.z, 0f, major.x);
			int c = mesh.AddVertex(centre, Vector3.up, SolidUV(0.5f, 0.5f), middle);
			int previous = -1;
			float sweep = Mathf.PI * 2f - notch;
			for (int s = 0; s <= segments; s++)
			{
				float a = Mathf.PI + 0.5f * notch + sweep * s / segments;
				float ca = Mathf.Cos(a), sa = Mathf.Sin(a);
				float r = diamond ? 1f / Mathf.Max(0.5f, Mathf.Abs(ca) + Mathf.Abs(sa)) : 1f;
				if (teeth > 0f && (s & 1) == 1 && s < segments)
				{
					r *= 1f + teeth;
				}
				Vector3 at = centre + (major * (ca * length) + minor * (sa * width)) * r + Vector3.up * rimLift;
				int v = mesh.AddVertex(at, Vector3.up, SolidUV(0.5f + 0.5f * ca, 0.5f + 0.5f * sa), rim);
				if (previous >= 0)
				{
					mesh.AddTriangle(0, c, previous, v, Vector3.up);
				}
				previous = v;
			}
		}

		/// <summary>
		/// A round leaf held up off the water (a lotus's): a shallow dish <paramref name="radius"/> across facing
		/// <paramref name="up"/>, its rim <paramref name="cup"/> above its middle: <paramref name="segments"/> triangles.
		/// </summary>
		private static void Dish(MeshBuilder mesh, Vector3 centre, Vector3 up, float radius, float cup, int segments, Color32 middle, Color32 rim)
		{
			up = up.sqrMagnitude > 1e-8f ? up.normalized : Vector3.up;
			Vector3 u = Vector3.Cross(up, Vector3.forward);
			if (u.sqrMagnitude < 1e-6f)
			{
				u = Vector3.Cross(up, Vector3.right);
			}
			u.Normalize();
			Vector3 w = Vector3.Cross(up, u);
			int c = mesh.AddVertex(centre, up, SolidUV(0.5f, 0.5f), middle);
			int first = -1, previous = -1;
			for (int s = 0; s < segments; s++)
			{
				float a = s * Mathf.PI * 2f / segments;
				Vector3 dir = u * Mathf.Cos(a) + w * Mathf.Sin(a);
				float r = radius * (0.95f + 0.05f * Mathf.Cos(a * 5f));
				int v = mesh.AddVertex(centre + dir * r + up * cup, up, SolidUV(0.5f + 0.5f * Mathf.Cos(a), 0.5f + 0.5f * Mathf.Sin(a)), rim);
				if (previous >= 0)
				{
					mesh.AddTriangle(0, c, previous, v, up);
				}
				else
				{
					first = v;
				}
				previous = v;
			}
			mesh.AddTriangle(0, c, previous, first, up);
		}

		/// <summary>A thin stalk from <paramref name="foot"/> to <paramref name="head"/>: two crossed cards <paramref name="width"/> across, 4 triangles.</summary>
		private static void Stalk(MeshBuilder mesh, Vector3 foot, Vector3 head, float width, Color colour)
		{
			Vector3 along = head - foot;
			if (along.sqrMagnitude < 1e-8f)
			{
				return;
			}
			Vector3 side = Vector3.Cross(along, Vector3.up);
			side = side.sqrMagnitude > 1e-8f ? side.normalized : Vector3.right;
			Vector3 other = Vector3.Cross(along.normalized, side).normalized;
			Color32 c = PlantParts.C32(colour, 1f);
			PlantParts.Card(mesh, 0, (foot + head) * 0.5f, side * width, along, FoliageCell.Solid, c, Vector2.zero, Vector2.zero, Vector3.up);
			PlantParts.Card(mesh, 0, (foot + head) * 0.5f, other * width, along, FoliageCell.Solid, c, Vector2.zero, Vector2.zero, Vector3.up);
		}

		/// <summary>A flat card on (or just under) the water: <paramref name="length"/> along <paramref name="yaw"/>, <paramref name="width"/> across, 2 triangles.</summary>
		private static void FlatCard(MeshBuilder mesh, Vector3 at, float yaw, float length, float width, FoliageCell cell, Color colour)
		{
			var along = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
			Vector3 across = Vector3.Cross(Vector3.up, along);
			PlantParts.Card(mesh, 0, at, across * width, along * length, cell, PlantParts.C32(colour, 1f), Vector2.zero, Vector2.zero, Vector3.up);
		}

		/// <summary>
		/// A carpet (Azolla, watercress, floating fern): <see cref="DetailPlant.Count"/> flat cards of <paramref name="cell"/>,
		/// <see cref="DetailPlant.Width"/> long and <paramref name="aspect"/> of that across, overlapped over
		/// <see cref="DetailPlant.Radius"/> and thicker toward the middle, as duckweed's; <paramref name="twoTone"/> takes each card
		/// near one colour or the other (Azolla green in shade, red in sun) instead of between; every
		/// <paramref name="shootEvery"/>th card (0 none) a crossed pair of the cell standing <see cref="DetailPlant.Height"/> up.
		/// </summary>
		private static void Carpet(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng, FoliageCell cell, float aspect, float y, int salt, bool twoTone, int shootEvery)
		{
			(int cards, int core) = VegetationMeshes.Spared(p.Count, 1.15f);
			for (int k = 0; k < cards && Fits(mesh, shootEvery > 0 ? 6 : 2); k++)
			{
				Vector3 at = Disc(rng, Mathf.Max(0.05f, p.Radius), 1.3f);
				at.y = y + 0.0004f * (k % 7);
				VegetationMeshes.Part(mesh, k, cards, core, at, PlantPart.Leaf, salt);
				float yaw = rng.NextFloat() * Mathf.PI * 2f;
				float size = Mathf.Max(0.02f, p.Width) * rng.Range(0.75f, 1.3f);
				float t = rng.NextFloat();
				Color colour = twoTone ? Color.Lerp(p.ColourA, p.ColourB, t < 0.55f ? 0.25f * t : 0.75f + 0.25f * t) : Color.Lerp(p.ColourA, p.ColourB, t);
				FlatCard(mesh, at, yaw, size, size * aspect, cell, colour);
				if (shootEvery > 0 && k % shootEvery == 0)
				{
					float h = Mathf.Max(0.02f, p.Height) * rng.Range(0.7f, 1.1f);
					var across = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
					Color32 c = PlantParts.C32(Color.Lerp(colour, p.ColourB, 0.3f), 1f);
					PlantParts.Card(mesh, 0, at + Vector3.up * (0.5f * h), across * (0.8f * h), Vector3.up * h, cell, c, Vector2.zero, Vector2.zero, Vector3.up);
					PlantParts.Card(mesh, 0, at + Vector3.up * (0.5f * h), Vector3.Cross(Vector3.up, across) * (0.8f * h), Vector3.up * h, cell, c,
						Vector2.zero, Vector2.zero, Vector3.up);
				}
			}
		}

		/// <summary>
		/// Rosettes of flat leaves (water chestnut, water clover, the floating-hearts, spongeplant, starwort):
		/// <see cref="DetailPlant.Count"/> rosettes over <see cref="DetailPlant.Radius"/>, each of <paramref name="minLeaves"/> to
		/// <paramref name="maxLeaves"/> <see cref="Oval"/> leaves <see cref="DetailPlant.Width"/> × <paramref name="lengthShare"/> long
		/// and × <paramref name="widthShare"/> wide, their middles <paramref name="offset"/> of a length out from the rosette's,
		/// notched toward it; every <paramref name="flowerEvery"/>th rosette (0 none) a flower of <paramref name="rings"/> rings of
		/// <paramref name="petals"/> (<see cref="DetailPlant.Height"/> long, the first accent; heart the second) <paramref name="flowerLift"/>
		/// over it. A rosette in the third accent now and then (water chestnut's autumn red).
		/// </summary>
		private static void Rosettes(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng, int minLeaves, int maxLeaves, float lengthShare, float widthShare,
			float offset, float notch, int segments, bool diamond, float teeth, int flowerEvery, int petals, int rings, float flowerLift, int salt)
		{
			int rosettes = Mathf.Max(1, p.Count);
			for (int r = 0; r < rosettes && Fits(mesh, maxLeaves * segments); r++)
			{
				Vector3 centre = Disc(rng, Mathf.Max(0.05f, p.Radius));
				centre.y = PadLift;
				float turn = rng.NextFloat() * Mathf.PI * 2f;
				int leaves = minLeaves + Mathf.FloorToInt(VegetationMeshes.Hash(r, salt + 1) * (maxLeaves - minLeaves + 1));
				leaves = Mathf.Clamp(leaves, minLeaves, maxLeaves);
				bool turned = p.Accents != null && p.Accents.Length > 2 && VegetationMeshes.Hash(r, salt + 3) < 0.3f;
				for (int k = 0; k < leaves; k++)
				{
					VegetationMeshes.Part(mesh, r * maxLeaves + k, rosettes * maxLeaves, rosettes * maxLeaves, centre, PlantPart.Leaf, salt);
					float yaw = turn + k * Mathf.PI * 2f / leaves + rng.Range(-0.12f, 0.12f);
					float length = Mathf.Max(0.005f, p.Width) * lengthShare * rng.Range(0.8f, 1.15f);
					var flat = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
					Color leaf = turned ? Color.Lerp(Pick(in p, rng), Accent(in p, 2, p.ColourB), 0.7f) : Pick(in p, rng);
					Oval(mesh, centre + flat * (length * offset) + Vector3.up * (0.0008f * k), length, length * widthShare / lengthShare, yaw, notch, segments,
						diamond, teeth, 0.002f, PlantParts.C32(leaf, 1f), PlantParts.C32(leaf * 0.85f, 1f));
				}
				if (flowerEvery > 0 && r % flowerEvery == 0 && Fits(mesh, rings * petals * 2 + 6))
				{
					VegetationMeshes.Part(mesh, rosettes * maxLeaves + r, rosettes * (maxLeaves + 1), rosettes * maxLeaves, centre, PlantPart.Leaf, salt + 2);
					Flower(mesh, centre + Vector3.up * flowerLift, Mathf.Max(0.004f, p.Height), rings, petals, turn, Accent(in p, 0, Color.white),
						Accent(in p, 1, new Color(0.95f, 0.85f, 0.3f)));
				}
			}
		}

		/// <summary>
		/// A lotus stand (sacred, American): <see cref="DetailPlant.Count"/> round leaves <see cref="DetailPlant.Width"/> in radius,
		/// the first half lying on the water and the rest held up on stalks to as much as <see cref="DetailPlant.Height"/>, tipped and
		/// cupped; a cup flower or two over them (first accent, heart the second) and a seed pod (third).
		/// </summary>
		private static void Lotus(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			int leaves = Mathf.Max(2, p.Count);
			float height = Mathf.Max(0.2f, p.Height);
			Color stalk = Color.Lerp(p.ColourA, new Color(0.45f, 0.55f, 0.3f), 0.5f);
			for (int k = 0; k < leaves && Fits(mesh, 16); k++)
			{
				Vector3 at = Disc(rng, Mathf.Max(0.1f, p.Radius));
				VegetationMeshes.Part(mesh, k, leaves, leaves, at, PlantPart.Leaf, 2050);
				float radius = Mathf.Max(0.05f, p.Width) * rng.Range(0.7f, 1.2f);
				Color leaf = Pick(in p, rng);
				Color32 middle = PlantParts.C32(Color.Lerp(leaf, Color.white, 0.12f), 1f), rim = PlantParts.C32(leaf * 0.9f, 1f);
				if (k < leaves / 2)
				{
					// Peltate: the stalk joins under the middle, so a lying leaf is a whole round, no notch.
					at.y = PadLift + 0.0015f * (k % 4);
					Pad(mesh, at, radius, rng.NextFloat() * Mathf.PI * 2f, 0f, 12, 0.006f, middle, rim);
					continue;
				}
				float up = height * rng.Range(0.35f, 0.9f);
				float tip = rng.Range(10f, 35f) * Mathf.Deg2Rad, yaw = rng.NextFloat() * Mathf.PI * 2f;
				var tilt = new Vector3(Mathf.Cos(yaw) * Mathf.Sin(tip), Mathf.Cos(tip), Mathf.Sin(yaw) * Mathf.Sin(tip));
				Vector3 head = at + Vector3.up * up;
				Stalk(mesh, at, head, 0.012f, stalk);
				Dish(mesh, head, tilt, radius, 0.12f * radius, 10, middle, rim);
			}
			int flowers = 1 + (VegetationMeshes.Hash(p.Count, 2051) < 0.6f ? 1 : 0);
			for (int f = 0; f < flowers && Fits(mesh, 2 * 8 * 2 + 6 + 4); f++)
			{
				Vector3 at = Disc(rng, Mathf.Max(0.1f, p.Radius) * 0.6f);
				VegetationMeshes.Part(mesh, leaves + f, leaves + flowers + 1, leaves + flowers + 1, at, PlantPart.Leaf, 2052);
				Vector3 head = at + Vector3.up * (height * rng.Range(0.75f, 1f));
				Stalk(mesh, at, head, 0.01f, stalk);
				Flower(mesh, head, Mathf.Max(0.04f, 0.35f * p.Width), 2, 8, rng.NextFloat() * Mathf.PI * 2f, Accent(in p, 0, new Color(0.94f, 0.72f, 0.78f)),
					Accent(in p, 1, new Color(0.9f, 0.75f, 0.25f)), 45f, 80f, 0.55f);
			}
			if (Fits(mesh, 8 + 4))
			{
				// A seed pod gone over: a flat-topped cone on its own stalk, nodding a little.
				Vector3 at = Disc(rng, Mathf.Max(0.1f, p.Radius) * 0.7f);
				VegetationMeshes.Part(mesh, leaves + flowers, leaves + flowers + 1, leaves + flowers + 1, at, PlantPart.Leaf, 2053);
				Vector3 head = at + Vector3.up * (height * rng.Range(0.6f, 0.85f));
				Stalk(mesh, at, head, 0.01f, stalk);
				Dish(mesh, head, new Vector3(0.2f, 1f, 0.1f), Mathf.Max(0.03f, 0.18f * p.Width), -0.03f, 8,
					PlantParts.C32(Accent(in p, 2, new Color(0.5f, 0.45f, 0.3f)), 1f), PlantParts.C32(Accent(in p, 2, new Color(0.5f, 0.45f, 0.3f)) * 0.8f, 1f));
			}
		}

		/// <summary>
		/// A blue lotus: water-lily pads (the third accent bleeding into their rims: purple undersides), and a star-pointed flower or
		/// two (first accent, gold heart the second) held <see cref="BlueLotusLift"/> over the water on a stalk, petals
		/// <see cref="DetailPlant.Height"/> long.
		/// </summary>
		private static void BlueLotus(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			int pads = Mathf.Max(1, p.Count);
			const int Segments = 12;
			for (int k = 0; k < pads && Fits(mesh, Segments); k++)
			{
				Vector3 at = Disc(rng, Mathf.Max(0.05f, p.Radius));
				at.y = PadLift + 0.0015f * (k % 4);
				VegetationMeshes.Part(mesh, k, pads, pads, at, PlantPart.Leaf, 2260);
				float radius = Mathf.Max(0.03f, p.Width) * rng.Range(0.7f, 1.25f);
				Color pad = Pick(in p, rng);
				Color rim = Color.Lerp(pad, Accent(in p, 2, new Color(0.35f, 0.22f, 0.4f)), 0.45f);
				Pad(mesh, at, radius, rng.NextFloat() * Mathf.PI * 2f, 0.35f, Segments, 0.004f, PlantParts.C32(pad, 1f), PlantParts.C32(rim, 1f));
			}
			int flowers = 1 + (VegetationMeshes.Hash(p.Count, 2261) < 0.5f ? 1 : 0);
			for (int f = 0; f < flowers && Fits(mesh, 2 * 8 * 2 + 6 + 4); f++)
			{
				Vector3 at = Disc(rng, Mathf.Max(0.05f, p.Radius) * 0.7f);
				VegetationMeshes.Part(mesh, pads + f, pads + flowers, pads + flowers, at, PlantPart.Leaf, 2262);
				Vector3 head = at + Vector3.up * BlueLotusLift;
				Stalk(mesh, at, head, 0.008f, new Color(0.3f, 0.42f, 0.25f));
				Flower(mesh, head, Mathf.Max(0.02f, p.Height) * rng.Range(0.85f, 1.1f), 2, 8, rng.NextFloat() * Mathf.PI * 2f, Accent(in p, 0, new Color(0.54f, 0.69f, 0.91f)),
					Accent(in p, 1, new Color(0.91f, 0.75f, 0.25f)), 30f, 70f, 0.3f);
			}
		}

		/// <summary>
		/// Water lettuce: <see cref="DetailPlant.Count"/> rosettes of eight pale velvety leaves (the paddle cell, ribbed),
		/// <see cref="DetailPlant.Width"/> long, rising from the water 25–55° about a sunken middle, the inner ones steeper.
		/// </summary>
		private static void Lettuce(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			int rosettes = Mathf.Max(1, p.Count);
			const int Leaves = 8;
			for (int r = 0; r < rosettes && Fits(mesh, Leaves * 2); r++)
			{
				Vector3 centre = Disc(rng, Mathf.Max(0.05f, p.Radius));
				centre.y = PadLift * 0.5f;
				float turn = rng.NextFloat() * Mathf.PI * 2f, scale = rng.Range(0.8f, 1.15f);
				for (int k = 0; k < Leaves; k++)
				{
					VegetationMeshes.Part(mesh, r * Leaves + k, rosettes * Leaves, rosettes * 7, centre, PlantPart.Leaf, 2100 + 60);
					float yaw = turn + k * Mathf.PI * 2f / Leaves + rng.Range(-0.15f, 0.15f);
					float rise = Mathf.Lerp(25f, 55f, (k & 1) == 0 ? 0.2f : 0.9f) * Mathf.Deg2Rad;
					var flat = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
					Vector3 dir = flat * Mathf.Cos(rise) + Vector3.up * Mathf.Sin(rise);
					float length = Mathf.Max(0.03f, p.Width) * scale * ((k & 1) == 0 ? 1f : 0.8f);
					Vector3 side = Vector3.Cross(Vector3.up, flat);
					PlantParts.Card(mesh, 0, centre + dir * (0.5f * length), side * (0.75f * length), dir * length, FoliageCell.Paddle,
						PlantParts.C32(Pick(in p, rng), 1f), Vector2.zero, Vector2.zero, Vector3.up);
				}
			}
		}

		/// <summary>
		/// Water soldier: <see cref="DetailPlant.Count"/> rosettes of fourteen serrated sword leaves (the blade cell)
		/// <see cref="DetailPlant.Width"/> wide, from a middle 0.12 m under the water up 35–70° to as much as
		/// <see cref="DetailPlant.Height"/> long; a three-petalled white flower on every other one, just over the water.
		/// </summary>
		private static void WaterSoldier(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			int rosettes = Mathf.Max(1, p.Count);
			const int Leaves = 14;
			for (int r = 0; r < rosettes && Fits(mesh, Leaves * 2); r++)
			{
				Vector3 centre = Disc(rng, Mathf.Max(0.05f, p.Radius));
				centre.y = -0.12f;
				float turn = rng.NextFloat() * Mathf.PI * 2f;
				for (int k = 0; k < Leaves; k++)
				{
					VegetationMeshes.Part(mesh, r * Leaves + k, rosettes * Leaves, rosettes * 12, centre, PlantPart.Leaf, 2160);
					float yaw = turn + k * 2.4f;
					float rise = rng.Range(35f, 70f) * Mathf.Deg2Rad;
					var flat = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
					Vector3 dir = flat * Mathf.Cos(rise) + Vector3.up * Mathf.Sin(rise);
					float length = Mathf.Max(0.1f, p.Height) * rng.Range(0.7f, 1.05f);
					// The foot a little out from the middle, so the rosette is a cup, not a point.
					Vector3 foot = centre + flat * 0.02f;
					PlantParts.Card(mesh, 0, foot + dir * (0.5f * length), Vector3.Cross(Vector3.up, flat) * Mathf.Max(0.01f, p.Width), dir * length,
						FoliageCell.Blade, PlantParts.C32(Pick(in p, rng), 1f), Vector2.zero, Vector2.zero, Vector3.up);
				}
				if (r % 2 == 0 && Fits(mesh, 3 * 2 + 6))
				{
					VegetationMeshes.Part(mesh, rosettes * Leaves + r, rosettes * (Leaves + 1), rosettes * Leaves, centre, PlantPart.Leaf, 2161);
					Flower(mesh, new Vector3(centre.x, 0.03f, centre.z), 0.025f, 1, 3, turn, Accent(in p, 0, new Color(0.96f, 0.96f, 0.93f)),
						Accent(in p, 1, new Color(0.95f, 0.85f, 0.3f)));
				}
			}
		}

		/// <summary>
		/// Ottelia: <see cref="DetailPlant.Count"/> long ribbon leaves (the strap cell) <see cref="DetailPlant.Width"/> long lying
		/// 5–20 cm under the water, rising a little toward it; a few white three-petalled flowers (petals
		/// <see cref="DetailPlant.Height"/>) open on the surface on short stalks from below.
		/// </summary>
		private static void Ottelia(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			int ribbons = Mathf.Max(1, p.Count);
			for (int k = 0; k < ribbons && Fits(mesh, 2); k++)
			{
				Vector3 at = Disc(rng, Mathf.Max(0.05f, p.Radius));
				at.y = -rng.Range(0.07f, 0.18f);
				VegetationMeshes.Part(mesh, k, ribbons, ribbons, at, PlantPart.Leaf, 2140);
				float yaw = rng.NextFloat() * Mathf.PI * 2f;
				var flat = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
				float length = Mathf.Max(0.1f, p.Width) * rng.Range(0.7f, 1.1f);
				Vector3 dir = (flat + Vector3.up * 0.12f).normalized;
				PlantParts.Card(mesh, 0, at, Vector3.Cross(Vector3.up, flat) * (0.1f * length), dir * length, FoliageCell.Strap,
					PlantParts.C32(Pick(in p, rng), 1f), Vector2.zero, Vector2.zero, Vector3.up);
			}
			int flowers = 2 + (VegetationMeshes.Hash(p.Count, 2141) < 0.5f ? 1 : 0);
			for (int f = 0; f < flowers && Fits(mesh, 3 * 2 + 6 + 4); f++)
			{
				Vector3 at = Disc(rng, Mathf.Max(0.05f, p.Radius) * 0.7f);
				at.y = PadLift;
				VegetationMeshes.Part(mesh, ribbons + f, ribbons + flowers, ribbons + flowers, at, PlantPart.Leaf, 2142);
				Stalk(mesh, at + Vector3.down * 0.1f, at, 0.006f, new Color(0.35f, 0.45f, 0.25f));
				Flower(mesh, at, Mathf.Max(0.01f, p.Height), 1, 3, rng.NextFloat() * Mathf.PI * 2f, Accent(in p, 0, new Color(0.97f, 0.96f, 0.94f)),
					Accent(in p, 1, new Color(0.94f, 0.82f, 0.32f)), 8f, 8f, 0.7f);
			}
		}

		/// <summary>
		/// Tasselweed: <see cref="DetailPlant.Count"/> cards of fine threads (the strand cell) <see cref="DetailPlant.Width"/> long
		/// lying just under the surface, and the seed tassels (the twig cell, the first accent) <see cref="DetailPlant.Height"/>
		/// across floating on it over a third of them.
		/// </summary>
		private static void Tasselweed(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			(int cards, int core) = VegetationMeshes.Spared(p.Count, 1.15f);
			for (int k = 0; k < cards && Fits(mesh, 4); k++)
			{
				Vector3 at = Disc(rng, Mathf.Max(0.05f, p.Radius), 1.2f);
				at.y = -0.005f - 0.0025f * (k % 6);
				VegetationMeshes.Part(mesh, k, cards, core, at, PlantPart.Leaf, 2040 + 60);
				float yaw = rng.NextFloat() * Mathf.PI * 2f;
				float length = Mathf.Max(0.05f, p.Width) * rng.Range(0.7f, 1.2f);
				FlatCard(mesh, at, yaw, length, 0.22f * length, FoliageCell.Strand, Pick(in p, rng));
				if (k % 3 == 0)
				{
					FlatCard(mesh, new Vector3(at.x, 0.003f, at.z), yaw + 1.1f, Mathf.Max(0.01f, p.Height), Mathf.Max(0.01f, p.Height), FoliageCell.Twigs,
						Accent(in p, 0, new Color(0.54f, 0.54f, 0.35f)));
				}
			}
		}

		/// <summary>
		/// Water crowfoot: a mat of thread-leaf cards (the strand cell) <see cref="DetailPlant.Width"/> long on the water and white
		/// gold-eyed buttercups (petals <see cref="DetailPlant.Height"/>) over a third of them.
		/// </summary>
		private static void Crowfoot(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			(int cards, int core) = VegetationMeshes.Spared(p.Count, 1.15f);
			for (int k = 0; k < cards && Fits(mesh, 2 + 16); k++)
			{
				Vector3 at = Disc(rng, Mathf.Max(0.05f, p.Radius), 1.2f);
				at.y = 0.002f + 0.0004f * (k % 7);
				VegetationMeshes.Part(mesh, k, cards, core, at, PlantPart.Leaf, 2240 + 2);
				float yaw = rng.NextFloat() * Mathf.PI * 2f;
				float length = Mathf.Max(0.05f, p.Width) * rng.Range(0.7f, 1.2f);
				FlatCard(mesh, at, yaw, length, 0.5f * length, FoliageCell.Strand, Pick(in p, rng));
				if (k % 3 == 0)
				{
					Flower(mesh, new Vector3(at.x, PadLift + 0.004f, at.z), Mathf.Max(0.006f, p.Height), 1, 5, yaw, Accent(in p, 0, new Color(0.96f, 0.96f, 0.93f)),
						Accent(in p, 1, new Color(0.94f, 0.78f, 0.19f)), 12f, 12f, 0.75f);
				}
			}
		}

		/// <summary>
		/// Bladderwort: a dark tangle (the twig cell) of <see cref="DetailPlant.Count"/> cards <see cref="DetailPlant.Width"/> across
		/// lying 3–15 cm under the water, and a few stalks rising <see cref="DetailPlant.Height"/> out of it, each with three yellow
		/// snapdragon flowers (the first accent) up its top.
		/// </summary>
		private static void Bladderwort(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			int cards = Mathf.Max(1, p.Count);
			for (int k = 0; k < cards && Fits(mesh, 2 + 16 * 6); k++)
			{
				Vector3 at = Disc(rng, Mathf.Max(0.05f, p.Radius), 1.1f);
				at.y = -rng.Range(0.03f, 0.15f);
				VegetationMeshes.Part(mesh, k, cards, cards, at, PlantPart.Leaf, 2180);
				float size = Mathf.Max(0.05f, p.Width) * rng.Range(0.7f, 1.2f);
				FlatCard(mesh, at, rng.NextFloat() * Mathf.PI * 2f, size, size, FoliageCell.Twigs, Pick(in p, rng));
			}
			int stalks = 3 + (VegetationMeshes.Hash(p.Count, 2181) < 0.5f ? 2 : 1);
			Color yellow = Accent(in p, 0, new Color(0.94f, 0.82f, 0.13f));
			for (int s = 0; s < stalks && Fits(mesh, 4 + 3 * 4); s++)
			{
				Vector3 at = Disc(rng, Mathf.Max(0.05f, p.Radius) * 0.8f);
				at.y = -0.03f;
				VegetationMeshes.Part(mesh, cards + s, cards + stalks, cards + stalks, at, PlantPart.Leaf, 2182);
				float height = Mathf.Max(0.05f, p.Height) * rng.Range(0.8f, 1.1f);
				Vector3 head = at + Vector3.up * (height + 0.03f);
				Stalk(mesh, at, head, 0.004f, new Color(0.3f, 0.35f, 0.2f));
				for (int f = 0; f < 3; f++)
				{
					float yaw = f * 2.1f + s;
					var across = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
					Vector3 bloom = head - Vector3.up * (0.035f * f);
					Color32 c = PlantParts.C32(yellow, 1f);
					PlantParts.Card(mesh, 0, bloom, across * 0.03f, Vector3.up * 0.03f, FoliageCell.Flower, c, Vector2.zero, Vector2.zero, Vector3.up);
					PlantParts.Card(mesh, 0, bloom, Vector3.Cross(Vector3.up, across) * 0.03f, Vector3.up * 0.03f, FoliageCell.Flower, c, Vector2.zero, Vector2.zero, Vector3.up);
				}
			}
		}

		/// <summary>
		/// Pennywort: four runners out across the water, each <see cref="DetailPlant.Count"/> nodes long, a round crinkled leaf
		/// <see cref="DetailPlant.Width"/> in radius lying beside each node, the runner a thin card between.
		/// </summary>
		private static void Pennywort(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			const int Runners = 4, Segments = 8;
			int nodes = Mathf.Max(2, p.Count);
			float radius = Mathf.Max(0.01f, p.Width);
			for (int r = 0; r < Runners; r++)
			{
				Vector3 at = Disc(rng, Mathf.Max(0.05f, p.Radius) * 0.5f);
				at.y = PadLift * 0.5f;
				float heading = rng.NextFloat() * Mathf.PI * 2f;
				for (int n = 0; n < nodes && Fits(mesh, Segments + 2); n++)
				{
					VegetationMeshes.Part(mesh, r * nodes + n, Runners * nodes, Runners * nodes, at, PlantPart.Leaf, 2200);
					heading += rng.Range(-0.4f, 0.4f);
					var step = new Vector3(Mathf.Cos(heading), 0f, Mathf.Sin(heading)) * (radius * 2.6f);
					FlatCard(mesh, at + 0.5f * step, heading, step.magnitude, 0.006f, FoliageCell.Solid, new Color(0.55f, 0.62f, 0.38f));
					Vector3 side = Vector3.Cross(Vector3.up, step.normalized) * (radius * ((n & 1) == 0 ? 1.1f : -1.1f));
					Color leaf = Pick(in p, rng);
					Pad(mesh, at + side + Vector3.up * (0.0008f * (n % 3)), radius * rng.Range(0.8f, 1.15f), heading + Mathf.PI * 0.5f, 0.25f, Segments, 0.002f,
						PlantParts.C32(leaf, 1f), PlantParts.C32(leaf * 0.85f, 1f));
					at += step;
				}
			}
		}

		/// <summary>
		/// Amphibious bistort and broad-leaved pondweed: <see cref="DetailPlant.Count"/> oval leaves <see cref="DetailPlant.Width"/>
		/// long lying on the water (bistort's slimmer), and a flower spike (the first accent: bistort pink, pondweed brown) standing
		/// <see cref="DetailPlant.Height"/> out of it over every third one.
		/// </summary>
		private static void Spikes(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			int leaves = Mathf.Max(1, p.Count);
			float slim = p.Kind == DetailKind.AmphibiousBistort ? 0.4f : 0.55f;
			const int Segments = 8;
			for (int k = 0; k < leaves && Fits(mesh, Segments + 8); k++)
			{
				Vector3 at = Disc(rng, Mathf.Max(0.05f, p.Radius));
				at.y = PadLift + 0.0012f * (k % 4);
				VegetationMeshes.Part(mesh, k, leaves, leaves, at, PlantPart.Leaf, 2230);
				float length = Mathf.Max(0.02f, p.Width) * rng.Range(0.75f, 1.2f);
				float yaw = rng.NextFloat() * Mathf.PI * 2f;
				Color leaf = Pick(in p, rng);
				Oval(mesh, at, length, length * slim, yaw, 0.2f, Segments, false, 0f, 0.003f, PlantParts.C32(leaf, 1f), PlantParts.C32(leaf * 0.85f, 1f));
				if (k % 3 == 0)
				{
					var flat = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
					Vector3 foot = at - flat * (0.8f * length);
					float height = Mathf.Max(0.05f, p.Height) * rng.Range(0.8f, 1.1f);
					Vector3 head = foot + Vector3.up * height;
					Stalk(mesh, foot, head - Vector3.up * 0.03f, 0.005f, new Color(0.4f, 0.4f, 0.25f));
					Color32 c = PlantParts.C32(Accent(in p, 0, new Color(0.88f, 0.48f, 0.6f)), 1f);
					Vector3 spike = head - Vector3.up * 0.015f;
					PlantParts.Card(mesh, 0, spike, flat * 0.025f, Vector3.up * 0.06f, FoliageCell.Flower, c, Vector2.zero, Vector2.zero, Vector3.up);
					PlantParts.Card(mesh, 0, spike, Vector3.Cross(Vector3.up, flat) * 0.025f, Vector3.up * 0.06f, FoliageCell.Flower, c, Vector2.zero, Vector2.zero, Vector3.up);
				}
			}
		}

		/// <summary>
		/// Plates on the water (salt rafts, froth rafts, tannin foam, gutweed sheets): <see cref="DetailPlant.Count"/> ragged plates
		/// <see cref="DetailPlant.Width"/> in radius at most, their rims (the first accent) raised <paramref name="rimLift"/>, the
		/// rim jittered <paramref name="crinkle"/> up and down (a sheet of weed); <paramref name="bubbles"/> sets two smaller blobs of
		/// froth (the second accent) on each.
		/// </summary>
		private static void Plates(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng, float rimLift, float crinkle, bool bubbles, PlantPart part, int salt)
		{
			int plates = Mathf.Max(1, p.Count);
			const int Segments = 7;
			for (int k = 0; k < plates && Fits(mesh, Segments + (bubbles ? 12 : 0)); k++)
			{
				Vector3 centre = Disc(rng, Mathf.Max(0.05f, p.Radius));
				centre.y = PadLift * 0.5f + 0.0006f * (k % 5);
				if (part == PlantPart.Fixed)
				{
					FixedPart(mesh, centre);
				}
				else
				{
					VegetationMeshes.Part(mesh, k, plates, plates, centre, part, salt);
				}
				float radius = Mathf.Max(0.02f, p.Width) * rng.Range(0.4f, 1f);
				Color body = Pick(in p, rng);
				Color32 middle = PlantParts.C32(body, 1f), rim = PlantParts.C32(Color.Lerp(body, Accent(in p, 0, body), 0.6f), 1f);
				int c = mesh.AddVertex(centre, Vector3.up, SolidUV(0.5f, 0.5f), middle);
				int first = -1, previous = -1;
				float turn = rng.NextFloat() * Mathf.PI * 2f;
				for (int s = 0; s < Segments; s++)
				{
					float a = turn + s * Mathf.PI * 2f / Segments + rng.Range(-0.25f, 0.25f);
					var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
					Vector3 at = centre + dir * (radius * rng.Range(0.65f, 1f)) + Vector3.up * (rimLift + (crinkle > 0f ? rng.Range(-crinkle, crinkle) : 0f));
					at.y = Mathf.Max(at.y, 0.0005f);
					int v = mesh.AddVertex(at, Vector3.up, SolidUV(0.5f + 0.5f * dir.x, 0.5f + 0.5f * dir.z), rim);
					if (previous >= 0)
					{
						mesh.AddTriangle(0, c, previous, v, Vector3.up);
					}
					else
					{
						first = v;
					}
					previous = v;
				}
				mesh.AddTriangle(0, c, previous, first, Vector3.up);
				if (bubbles)
				{
					Color froth = Accent(in p, 1, Color.Lerp(body, Color.white, 0.35f));
					for (int b = 0; b < 2; b++)
					{
						Vector3 at = centre + Disc(rng, radius * 0.5f) + Vector3.up * (rimLift + 0.004f);
						Pad(mesh, at, radius * rng.Range(0.2f, 0.35f), rng.NextFloat() * Mathf.PI * 2f, 0f, 6, 0.004f,
							PlantParts.C32(Color.Lerp(froth, Color.white, 0.2f), 1f), PlantParts.C32(froth, 1f));
					}
				}
			}
		}

		/// <summary>
		/// Bergy bits: <see cref="DetailPlant.Count"/> lumps of glacier ice up to <see cref="DetailPlant.Width"/> in radius, each
		/// a ragged seven-sided body of rings riding with a third of its height over the water and the rest under it, white where
		/// it is dry (the first colour), blue where it is dense and wet (the second).
		/// </summary>
		private static void BergyBits(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			const int Sides = 7;
			// Ring heights and radii as shares of the lump's radius, from its keel to its crown.
			float[] heights = { -0.7f, -0.35f, 0.05f, 0.24f };
			float[] radii = { 0.55f, 0.95f, 1f, 0.7f };
			const float Keel = -0.85f, Crown = 0.32f;
			int lumps = Mathf.Max(1, p.Count);
			int cost = Sides * (heights.Length - 1) * 2 + 2 * Sides;
			for (int l = 0; l < lumps && Fits(mesh, cost); l++)
			{
				Vector3 centre = Disc(rng, Mathf.Max(0.05f, p.Radius));
				centre.y = 0f;
				FixedPart(mesh, centre);
				float size = Mathf.Max(0.1f, p.Width) * rng.Range(0.5f, 1f);
				float turn = rng.NextFloat() * Mathf.PI * 2f;
				var rings = new int[heights.Length, Sides];
				for (int h = 0; h < heights.Length; h++)
				{
					Color tone = heights[h] > 0f ? p.ColourA : Color.Lerp(p.ColourA, p.ColourB, Mathf.InverseLerp(0.1f, -0.7f, heights[h]));
					Color32 c = PlantParts.C32(tone, 1f);
					for (int s = 0; s < Sides; s++)
					{
						float a = turn + s * Mathf.PI * 2f / Sides;
						var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
						float r = size * radii[h] * rng.Range(0.82f, 1.12f);
						Vector3 at = centre + dir * r + Vector3.up * (size * heights[h]);
						rings[h, s] = mesh.AddVertex(at, (dir + Vector3.up * heights[h]).normalized, SolidUV(0.5f + 0.5f * dir.x, 0.5f + 0.5f * dir.z), c);
					}
				}
				for (int h = 0; h + 1 < heights.Length; h++)
				{
					for (int s = 0; s < Sides; s++)
					{
						int n = (s + 1) % Sides;
						float a = turn + (s + 0.5f) * Mathf.PI * 2f / Sides;
						var outward = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
						mesh.AddQuad(0, rings[h, s], rings[h, n], rings[h + 1, n], rings[h + 1, s], outward);
					}
				}
				int crown = mesh.AddVertex(centre + Vector3.up * (size * Crown), Vector3.up, SolidUV(0.5f, 0.5f), PlantParts.C32(p.ColourA, 1f));
				int keel = mesh.AddVertex(centre + Vector3.up * (size * Keel), Vector3.down, SolidUV(0.5f, 0.5f), PlantParts.C32(p.ColourB, 1f));
				for (int s = 0; s < Sides; s++)
				{
					int n = (s + 1) % Sides;
					mesh.AddTriangle(0, crown, rings[heights.Length - 1, s], rings[heights.Length - 1, n], Vector3.up);
					mesh.AddTriangle(0, keel, rings[0, s], rings[0, n], Vector3.down);
				}
			}
		}

		/// <summary>
		/// A raft of mangrove propagules in a creek: <see cref="DetailPlant.Count"/> pencil-like seedlings
		/// <see cref="DetailPlant.Width"/> long lying every which way (green, browning to the tip: the second colour), crossed cards
		/// for their roundness, and half as many yellow fallen leaves (the paddle cell, the first accent) among them.
		/// </summary>
		private static void Propagules(MeshBuilder mesh, in DetailPlant p, DeterministicRNG rng)
		{
			int count = Mathf.Max(1, p.Count);
			for (int k = 0; k < count && Fits(mesh, 4); k++)
			{
				Vector3 at = Disc(rng, Mathf.Max(0.05f, p.Radius));
				at.y = 0.008f;
				FixedPart(mesh, at);
				float yaw = rng.NextFloat() * Mathf.PI * 2f;
				float length = Mathf.Max(0.05f, p.Width) * rng.Range(0.75f, 1.1f);
				var along = new Vector3(Mathf.Cos(yaw), 0f, Mathf.Sin(yaw));
				Color32 c = PlantParts.C32(Color.Lerp(p.ColourA, p.ColourB, rng.Range(0f, 0.5f)), 1f);
				PlantParts.Card(mesh, 0, at, Vector3.Cross(Vector3.up, along) * 0.014f, along * length, FoliageCell.Solid, c, Vector2.zero, Vector2.zero, Vector3.up);
				PlantParts.Card(mesh, 0, at, Vector3.up * 0.014f, along * length, FoliageCell.Solid, c, Vector2.zero, Vector2.zero, Vector3.up);
			}
			for (int k = 0; k < count / 2 && Fits(mesh, 2); k++)
			{
				Vector3 at = Disc(rng, Mathf.Max(0.05f, p.Radius));
				at.y = PadLift * 0.5f + 0.0005f * (k % 4);
				FixedPart(mesh, at);
				Color leaf = Color.Lerp(Accent(in p, 0, new Color(0.85f, 0.75f, 0.25f)), Accent(in p, 1, new Color(0.55f, 0.42f, 0.2f)), rng.NextFloat() * 0.5f);
				FlatCard(mesh, at, rng.NextFloat() * Mathf.PI * 2f, 0.1f, 0.05f, FoliageCell.Paddle, leaf);
			}
		}
	}
}
#endif
