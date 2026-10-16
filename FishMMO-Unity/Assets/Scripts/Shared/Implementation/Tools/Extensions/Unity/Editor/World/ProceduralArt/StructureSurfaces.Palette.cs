#if UNITY_EDITOR
using System.Threading.Tasks;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The structure palette's own surfaces (2026-10-16, I6a: "new surfaces such as log, shingle, slate, adobe, sandstone,
	/// bamboo, marble, ice block, bone, reed"): what a biome's palette builds a role of where the kit's nine will not do.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Each is built as the kit's patterned surfaces are (<see cref="StructureSurfaces"/>): a height and a linear colour made
	/// here, finished by SurfaceSynth's normal, occlusion and colour passes, so the maps have the same layout (albedo with
	/// smoothness, OpenGL normal, mask R metallic G occlusion B height A smoothness) and every finish (moss, char, algae) is
	/// made from them as from the others. Reed alone is a plain SurfaceSynth recipe, the thatch's fibres finer and paler.
	/// </para>
	/// <para>
	/// <b>Tiling.</b> Every count across a tile is whole (courses, blocks, culms, bones) and every noise periodic on the tile,
	/// so each surface tiles without a seam, and a course's joints are worked out from the course's own hash, never a running
	/// generator: the rows build in parallel and come out the same.
	/// </para>
	/// <para>
	/// <b>Orientation.</b> Planar faces take u along the part's grain or the face's horizontal and v up the face (down a roof's
	/// slope, whose grain runs along the eave): courses of logs, shingles, slates, bricks and blocks run along u, stacked in v;
	/// bamboo culms and reed run along v, so they stand on a wall and run down a roof.
	/// </para>
	/// </remarks>
	public static partial class StructureSurfaces
	{
		/// <summary>Metres one tile of a palette surface covers; null for a surface that is not one of them.</summary>
		private static float? PaletteTileMetres(string surface)
		{
			switch (surface)
			{
				case "Log": return 2f;
				case "Shingle": return 1.5f;
				case "Slate": return 1.2f;
				case "Adobe": return 3f;
				case "Sandstone": return 2.4f;
				case "Bamboo": return 1f;
				case "Marble": return 3f;
				case "IceBlock": return 2f;
				case "Bone": return 1.5f;
				case "Reed": return 2f;
				default: return null;
			}
		}

		/// <summary>
		/// How much moss a material grows in a finish (MossAmounts.ForStructure, which this extends for the palette's
		/// materials so that file, shared with the rocks and cliffs, is untouched): rough wood and stone most, mud brick and
		/// bone little, marble less than sandstone, ice none.
		/// </summary>
		public static float MossOf(StructureMaterial material, StructureFinish finish)
		{
			float amount;
			switch (material)
			{
				case StructureMaterial.Log: amount = 0.6f; break;
				case StructureMaterial.Shingle: amount = 0.6f; break;
				case StructureMaterial.Slate: amount = 0.5f; break;
				case StructureMaterial.Sandstone: amount = 0.6f; break;
				case StructureMaterial.Reed: amount = 0.65f; break;
				case StructureMaterial.Bamboo: amount = 0.35f; break;
				case StructureMaterial.Marble: amount = 0.4f; break;
				case StructureMaterial.Adobe: amount = 0.2f; break;
				case StructureMaterial.BoneWork: amount = 0.15f; break;
				case StructureMaterial.IceBlock: amount = 0f; break;
				default: return MossAmounts.ForStructure(material, finish);
			}
			switch (finish)
			{
				case StructureFinish.Mossy: return amount > 0f ? Mathf.Min(1f, amount + 0.35f) : 0f;
				case StructureFinish.Charred: return amount * 0.2f;
				case StructureFinish.Algae: return amount * 0.6f;
				default: return amount;
			}
		}

		/// <summary>Reed bundles laid as thatch: the thatch's fibres finer and paler, a grey-gold, a little smoother.</summary>
		private static void ReedRecipe(ref SurfaceRecipe r)
		{
			r.ReliefMetres = 0.05f; r.Dark = Hex("#5d553c"); r.Mid = Hex("#a1946a"); r.Light = Hex("#cfc196"); r.Warp = 0.04f;
			r.Motif = SurfaceMotif.Fibres; r.Vertical = true; r.MotifCells = 96; r.MotifSize = 0.012f; r.MotifWeight = 0.8f;
			r.MotifDark = Hex("#6b6145"); r.MotifLight = Hex("#d8cc9f");
			r.Smoothness = 0.12f; r.SmoothnessVariation = 0.06f; r.Occlusion = 0.8f;
		}

		// ── Helpers ───────────────────────────────────────────────────

		/// <summary>Dark → mid → light by t in [0, 1].</summary>
		private static Color Ramp(Color dark, Color mid, Color light, float t)
		{
			t = Mathf.Clamp01(t);
			return t < 0.5f ? Color.Lerp(dark, mid, t * 2f) : Color.Lerp(mid, light, (t - 0.5f) * 2f);
		}

		private static Color Lin(string hex) => SurfaceSynth.ToLinear(Hex(hex));

		/// <summary>The distance, in tile units, from u to the nearest of a course's cuts (sorted, in [0, 1), wrapping).</summary>
		private static float ToCut(float u, float[] cuts, out int index, out float along)
		{
			int n = cuts.Length;
			index = n - 1;
			for (int i = 0; i < n; i++)
			{
				if (u < cuts[i])
				{
					index = i - 1;
					break;
				}
			}
			float start = index >= 0 ? cuts[index] : cuts[n - 1] - 1f;
			float end = index + 1 < n ? cuts[index + 1] : cuts[0] + 1f;
			if (index < 0)
			{
				index = n - 1;
			}
			along = (u - start) / Mathf.Max(1e-5f, end - start);
			return Mathf.Min(u - start, end - u);
		}

		/// <summary>
		/// A course's cuts: <paramref name="count"/> pieces of uneven width (each 1 ± <paramref name="spread"/> of the mean),
		/// shifted along the course by a hash, sorted, wrapping in [0, 1). Pure: the same course and seed always cut alike.
		/// </summary>
		private static float[] Cuts(int course, int count, float spread, int seed)
		{
			var widths = new float[count];
			float total = 0f;
			for (int i = 0; i < count; i++)
			{
				widths[i] = 1f + spread * (ProceduralNoise.ToUnit(ProceduralNoise.Hash(course, i, seed)) * 2f - 1f);
				total += widths[i];
			}
			float shift = ProceduralNoise.ToUnit(ProceduralNoise.Hash(course, 977, seed));
			var cuts = new float[count];
			float at = 0f;
			for (int i = 0; i < count; i++)
			{
				float c = at / total + shift;
				cuts[i] = c - Mathf.Floor(c);
				at += widths[i];
			}
			System.Array.Sort(cuts);
			return cuts;
		}

		// ── Log ───────────────────────────────────────────────────────

		/// <summary>
		/// Six courses of logs a tile, grain along u: some round with their bark mostly gone, some adze-hewn flat on the face;
		/// daub chinking in the seams, checks along the grain, a butt joint where two logs meet in a course.
		/// </summary>
		private static SurfaceMaps Logs(int size, int seed)
		{
			const int courses = 6;
			float tile = PaletteTileMetres("Log").Value;
			var height = new float[size * size];
			var colour = new Color[size * size];
			Color dark = Lin("#4a3a2a"), mid = Lin("#7a6247"), light = Lin("#a3896a"), chink = Lin("#77705f"), bark = Lin("#3b2e22");
			Parallel.For(0, size, y =>
			{
				float v = (y + 0.5f) / size;
				int course = Mathf.Min(courses - 1, (int)(v * courses));
				float across = v * courses - course;
				uint h = ProceduralNoise.Hash(course, 3, seed);
				bool hewn = (h & 1u) == 0u;
				float joint = ProceduralNoise.ToUnit(ProceduralNoise.Mix(h ^ 0x61u));
				float tone = ProceduralNoise.ToUnit(ProceduralNoise.Mix(h ^ 0x15u));
				for (int x = 0; x < size; x++)
				{
					float u = (x + 0.5f) / size;
					float c = across * 2f - 1f;
					float round = Mathf.Sqrt(Mathf.Max(0f, 1f - c * c));
					float profile = hewn ? Mathf.Pow(round, 0.35f) : round;
					float seam = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(profile / 0.25f));
					float grain = ProceduralNoise.PeriodicFbm(u, v, 4, 96, 4, 0.55f, seed + course * 17);
					float check = Mathf.Clamp01((ProceduralNoise.PeriodicRidged(u, v, 3, 2, 0.5f, seed + 40 + course) - 0.9f) * 14f) * (across > 0.25f && across < 0.75f ? 1f : 0f);
					float du = Mathf.Abs(u - joint);
					du = Mathf.Min(du, 1f - du) * tile;
					float butt = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((du - 0.004f) / 0.02f));
					float barkLeft = !hewn ? Mathf.Clamp01((ProceduralNoise.PeriodicFbm(u, v, 6, 6, 3, 0.5f, seed + 70 + course) - 0.25f) * 4f) : 0f;
					int i = y * size + x;
					height[i] = seam * (0.55f + 0.45f * profile) * butt + 0.06f * grain - 0.25f * check - 0.05f * barkLeft;
					Color wood = Ramp(dark, mid, light, 0.5f + 0.3f * grain + (tone - 0.5f) * 0.5f);
					wood = Color.Lerp(wood, bark, barkLeft * 0.8f);
					wood = Color.Lerp(wood, dark, check * 0.7f);
					colour[i] = Color.Lerp(chink, wood, Mathf.Min(seam, butt + (1f - butt) * 0.4f));
				}
			});
			return Compose(height, colour, size, tile, 0.06f, 0.2f, 0.1f, 0f, 0.8f);
		}

		// ── Shingle ───────────────────────────────────────────────────

		/// <summary>
		/// Ten courses of riven shingles a tile (15 cm to the weather), each course eight to eleven shingles of uneven width with
		/// its joints off the course below; each shingle thickest at its butt, split grain running down it, weathered silver.
		/// </summary>
		private static SurfaceMaps Shingles(int size, int seed)
		{
			const int courses = 10;
			float tile = PaletteTileMetres("Shingle").Value;
			var height = new float[size * size];
			var colour = new Color[size * size];
			Color dark = Lin("#463e35"), mid = Lin("#746857"), light = Lin("#9e9079"), key = Lin("#2a241e");
			var cuts = new float[courses][];
			for (int c = 0; c < courses; c++)
			{
				cuts[c] = Cuts(c, 8 + (int)(ProceduralNoise.Hash(c, 1, seed) % 4u), 0.45f, seed);
			}
			Parallel.For(0, size, y =>
			{
				float v = (y + 0.5f) / size;
				int course = Mathf.Min(courses - 1, (int)(v * courses));
				float across = v * courses - course;
				for (int x = 0; x < size; x++)
				{
					float u = (x + 0.5f) / size;
					float gap = ToCut(u, cuts[course], out int shingle, out _) * tile;
					uint sh = ProceduralNoise.Hash(course, shingle + 31, seed);
					float tone = ProceduralNoise.ToUnit(sh);
					float keyway = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((gap - 0.002f) / 0.006f));
					// Thick at the butt (the course's foot, across 0), thin where the next course covers it.
					float taper = 1f - 0.6f * across;
					float butt = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(across / 0.05f));
					float grain = ProceduralNoise.PeriodicFbm(u, v, 160, 6, 3, 0.5f, seed + 9 + course);
					float split = Mathf.Clamp01((ProceduralNoise.PeriodicRidged(u, v, 24, 2, 0.5f, seed + 21) - 0.88f) * 10f);
					int i = y * size + x;
					height[i] = keyway * (0.35f + 0.5f * taper + 0.08f * grain) * (0.6f + 0.4f * butt) - 0.15f * split;
					Color wood = Ramp(dark, mid, light, 0.5f + 0.3f * grain + (tone - 0.5f) * 0.7f);
					wood = Color.Lerp(key, wood, Mathf.Clamp01(0.35f + 0.65f * butt));
					colour[i] = Color.Lerp(key, wood, keyway * (1f - split * 0.5f));
				}
			});
			return Compose(height, colour, size, tile, 0.02f, 0.18f, 0.08f, 0f, 0.8f);
		}

		// ── Slate ─────────────────────────────────────────────────────

		/// <summary>
		/// Ten courses of split slate a tile (12 cm to the weather), five slates a course broken half-bond with the course below;
		/// each a thin plate with a chipped butt and its own tilt and tone, blue-grey with a little rust and lichen.
		/// </summary>
		private static SurfaceMaps Slates(int size, int seed)
		{
			const int courses = 10, slates = 5;
			float tile = PaletteTileMetres("Slate").Value;
			var height = new float[size * size];
			var colour = new Color[size * size];
			Color dark = Lin("#2b3035"), mid = Lin("#434a51"), light = Lin("#616970"), joint = Lin("#16191c");
			Color rust = Lin("#6a5440"), lichen = Lin("#9a9c80");
			Parallel.For(0, size, y =>
			{
				float v = (y + 0.5f) / size;
				for (int x = 0; x < size; x++)
				{
					float u = (x + 0.5f) / size;
					// A chipped butt: the course line wanders a little along u.
					float chip = (ProceduralNoise.PeriodicFbm(u, 0.5f, 64, 1, 3, 0.5f, seed + 3) * 0.5f) / courses * 0.12f;
					float vv = v + chip;
					vv -= Mathf.Floor(vv);
					int course = Mathf.Min(courses - 1, (int)(vv * courses));
					float across = vv * courses - course;
					float offset = (course & 1) * 0.5f / slates + (ProceduralNoise.ToUnit(ProceduralNoise.Hash(course, 5, seed)) - 0.5f) * 0.04f;
					float f = u * slates + offset * slates;
					int slate = Mathf.FloorToInt(f);
					float inSlate = f - slate;
					slate = ((slate % slates) + slates) % slates;
					uint sh = ProceduralNoise.Hash(course, slate, seed);
					float tone = ProceduralNoise.ToUnit(sh);
					float tilt = (ProceduralNoise.ToUnit(ProceduralNoise.Mix(sh ^ 0x3u)) - 0.5f) * 0.3f;
					float du = Mathf.Min(inSlate, 1f - inSlate) * tile / slates;
					float side = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((du - 0.002f) / 0.005f));
					float butt = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(across / 0.04f));
					float cleave = ProceduralNoise.PeriodicFbm(u, v, 20, 12, 3, 0.5f, seed + 7);
					float fleck = Mathf.Clamp01((ProceduralNoise.PeriodicFbm(u, v, 40, 3, 0.5f, seed + 11) - 0.35f) * 5f);
					int i = y * size + x;
					height[i] = side * (0.45f + 0.35f * (1f - across) + tilt * (inSlate - 0.5f) + 0.06f * cleave) * (0.55f + 0.45f * butt);
					Color stone = Ramp(dark, mid, light, 0.5f + 0.25f * cleave + (tone - 0.5f) * 0.6f);
					stone = Color.Lerp(stone, rust, Mathf.Clamp01((tone - 0.85f) * 6f) * 0.5f);
					stone = Color.Lerp(stone, lichen, fleck * 0.35f);
					colour[i] = Color.Lerp(joint, stone, side * Mathf.Clamp01(0.3f + 0.7f * butt));
				}
			});
			return Compose(height, colour, size, tile, 0.012f, 0.32f, 0.12f, 0f, 0.75f);
		}

		// ── Adobe ─────────────────────────────────────────────────────

		/// <summary>
		/// Mud brick, 0.375 × 0.12 m in running bond, laid in thick mud mortar, under a mud wash that has fallen away in
		/// patches: where it holds the face is smooth and pale and crazed, where it has gone the bricks show with straw in them.
		/// </summary>
		private static SurfaceMaps MudBrick(int size, int seed)
		{
			const int courses = 25, bricks = 8;
			float tile = PaletteTileMetres("Adobe").Value;
			var height = new float[size * size];
			var colour = new Color[size * size];
			Color dark = Lin("#7d5c3d"), mid = Lin("#a17b54"), light = Lin("#bf9a6e"), mortar = Lin("#8a6b4b"), wash = Lin("#c5a47c"), straw = Lin("#d6bf86");
			Parallel.For(0, size, y =>
			{
				float v = (y + 0.5f) / size;
				int course = Mathf.Min(courses - 1, (int)(v * courses));
				float inCourse = v * courses - course;
				float offset = (course & 1) * 0.5f + (ProceduralNoise.ToUnit(ProceduralNoise.Hash(course, 9, seed)) - 0.5f) * 0.2f;
				for (int x = 0; x < size; x++)
				{
					float u = (x + 0.5f) / size;
					float f = u * bricks + offset;
					int brick = Mathf.FloorToInt(f);
					float inBrick = f - brick;
					brick = ((brick % bricks) + bricks) % bricks;
					float tone = ProceduralNoise.ToUnit(ProceduralNoise.Hash(course, brick, seed));
					float du = Mathf.Min(inBrick, 1f - inBrick) * tile / bricks;
					float dv = Mathf.Min(inCourse, 1f - inCourse) * tile / courses;
					float face = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Mathf.Min(du, dv) - 0.008f) / 0.025f));
					float erosion = ProceduralNoise.PeriodicFbm(u, v, 24, 4, 0.5f, seed + 4);
					float fleck = Mathf.Clamp01((ProceduralNoise.PeriodicRidged(u, v, 90, 2, 0.5f, seed + 13) - 0.93f) * 16f);
					float held = Mathf.Clamp01((ProceduralNoise.PeriodicFbm(u, v, 3, 4, 0.55f, seed + 1) + 0.05f) * 3.5f);
					float craze = Mathf.Clamp01((ProceduralNoise.PeriodicRidged(u, v, 10, 3, 0.5f, seed + 2) - 0.86f) * 10f);
					int i = y * size + x;
					float brickHeight = face * (0.7f + 0.15f * erosion) - 0.1f * fleck;
					height[i] = Mathf.Lerp(brickHeight, 0.92f + 0.04f * erosion - 0.2f * craze, held);
					Color mud = Color.Lerp(mortar, Ramp(dark, mid, light, 0.5f + 0.3f * erosion + (tone - 0.5f) * 0.5f), face);
					mud = Color.Lerp(mud, straw, fleck * 0.6f);
					Color washed = Color.Lerp(wash, mortar, craze * 0.6f) * (0.95f + 0.1f * erosion);
					colour[i] = Color.Lerp(mud, washed, held);
				}
			});
			return Compose(height, colour, size, tile, 0.02f, 0.1f, 0.06f, 0f, 0.6f);
		}

		// ── Sandstone ─────────────────────────────────────────────────

		/// <summary>
		/// Sandstone ashlar: six courses a tile, two or three blocks a course, the arrises worn round, fine bedding lines across
		/// each block and the odd honeycomb of weathering, warm ochre and buff.
		/// </summary>
		private static SurfaceMaps SandstoneAshlar(int size, int seed)
		{
			const int courses = 6;
			float tile = PaletteTileMetres("Sandstone").Value;
			var height = new float[size * size];
			var colour = new Color[size * size];
			Color dark = Lin("#9b7650"), mid = Lin("#bf9767"), light = Lin("#d9b785"), mortar = Lin("#a08766"), band = Lin("#a9784c");
			Parallel.For(0, size, y =>
			{
				float v = (y + 0.5f) / size;
				int course = Mathf.Min(courses - 1, (int)(v * courses));
				float inCourse = v * courses - course;
				uint ch = ProceduralNoise.Hash(course, 11, seed);
				int blocks = (ch & 1u) == 0u ? 2 : 3;
				float offset = ProceduralNoise.ToUnit(ProceduralNoise.Mix(ch ^ 0x47u));
				for (int x = 0; x < size; x++)
				{
					float u = (x + 0.5f) / size;
					float f = u * blocks + offset;
					int block = Mathf.FloorToInt(f);
					float inBlock = f - block;
					block = ((block % blocks) + blocks) % blocks;
					uint bh = ProceduralNoise.Hash(course, block, seed);
					float du = Mathf.Min(inBlock, 1f - inBlock) * tile / blocks;
					float dv = Mathf.Min(inCourse, 1f - inCourse) * tile / courses;
					float face = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Mathf.Min(du, dv) - 0.006f) / 0.04f));
					float bedding = Mathf.Sin((v * courses * 9f + ProceduralNoise.PeriodicFbm(u, v, 6, 6, 3, 0.5f, seed + 3) * 1.5f) * Mathf.PI * 2f);
					float tafoni = Mathf.Clamp01((ProceduralNoise.PeriodicFbm(u, v, 30, 3, 0.5f, seed + 5) - 0.42f) * 5f)
						* Mathf.Clamp01((ProceduralNoise.PeriodicFbm(u, v, 3, 2, 0.5f, seed + 6) - 0.1f) * 3f);
					float grain = ProceduralNoise.PeriodicFbm(u, v, 48, 3, 0.5f, seed + 8);
					int i = y * size + x;
					height[i] = face * (0.8f + 0.04f * bedding + 0.05f * grain) - 0.3f * tafoni * face;
					Color stone = Ramp(dark, mid, light, 0.5f + 0.15f * grain + (ProceduralNoise.ToUnit(bh) - 0.5f) * 0.6f);
					stone = Color.Lerp(stone, band, Mathf.Clamp01(bedding * 0.5f + 0.1f) * 0.35f);
					stone = Color.Lerp(stone, dark * 0.8f, tafoni * 0.5f);
					colour[i] = Color.Lerp(mortar, stone, face);
				}
			});
			return Compose(height, colour, size, tile, 0.018f, 0.2f, 0.1f, 0f, 0.75f);
		}

		// ── Bamboo ────────────────────────────────────────────────────

		/// <summary>
		/// Twelve bamboo culms a metre, side by side along v, each its own girth and straw-gold to green-grey, banded by its
		/// nodes (three a metre, each culm's at its own height), with dark gaps between.
		/// </summary>
		private static SurfaceMaps Culms(int size, int seed)
		{
			const int culms = 12;
			float tile = PaletteTileMetres("Bamboo").Value;
			var height = new float[size * size];
			var colour = new Color[size * size];
			Color dark = Lin("#7b6c3c"), mid = Lin("#a89255"), light = Lin("#ccb777"), green = Lin("#7d8250"), gap = Lin("#2e2717");
			var cuts = Cuts(0, culms, 0.25f, seed);
			Parallel.For(0, size, y =>
			{
				float v = (y + 0.5f) / size;
				for (int x = 0; x < size; x++)
				{
					float u = (x + 0.5f) / size;
					ToCut(u, cuts, out int culm, out float along);
					uint ch = ProceduralNoise.Hash(culm, 2, seed);
					float tone = ProceduralNoise.ToUnit(ch);
					float c = along * 2f - 1f;
					float round = Mathf.Sqrt(Mathf.Max(0f, 1f - c * c));
					float phase = ProceduralNoise.ToUnit(ProceduralNoise.Mix(ch ^ 0x9u));
					float n = v * 3f + phase;
					float node = Mathf.Abs(n - Mathf.Round(n)) * tile / 3f;
					float ring = Mathf.Clamp01(1f - node / 0.012f);
					float streak = ProceduralNoise.PeriodicFbm(u, v, 96, 4, 3, 0.5f, seed + 3);
					int i = y * size + x;
					height[i] = round * (0.85f + 0.03f * streak) + 0.12f * ring * round;
					Color cane = Ramp(dark, mid, light, 0.5f + 0.25f * streak + (tone - 0.5f) * 0.5f);
					cane = Color.Lerp(cane, green, Mathf.Clamp01((tone - 0.7f) * 3f) * 0.5f);
					cane = Color.Lerp(cane, dark * 0.75f, ring * 0.7f);
					colour[i] = Color.Lerp(gap, cane, Mathf.Clamp01(round * 2.5f));
				}
			});
			return Compose(height, colour, size, tile, 0.02f, 0.38f, 0.15f, 0f, 0.7f);
		}

		// ── Marble ────────────────────────────────────────────────────

		/// <summary>
		/// Great blocks of white marble (three courses of a metre a tile, two blocks a course), joints so fine they read as
		/// lines, the faces honed smooth, grey veins and a faint gold running through each block its own way.
		/// </summary>
		private static SurfaceMaps MarbleBlocks(int size, int seed)
		{
			const int courses = 3, blocks = 2;
			float tile = PaletteTileMetres("Marble").Value;
			var height = new float[size * size];
			var colour = new Color[size * size];
			Color dark = Lin("#cfcac0"), mid = Lin("#e3dfd6"), light = Lin("#f2efe9"), vein = Lin("#8d8e8f"), gold = Lin("#c5ae82"), joint = Lin("#a8a49b");
			Parallel.For(0, size, y =>
			{
				float v = (y + 0.5f) / size;
				int course = Mathf.Min(courses - 1, (int)(v * courses));
				float inCourse = v * courses - course;
				float offset = (course & 1) * 0.5f;
				for (int x = 0; x < size; x++)
				{
					float u = (x + 0.5f) / size;
					float f = u * blocks + offset;
					int block = Mathf.FloorToInt(f);
					float inBlock = f - block;
					block = ((block % blocks) + blocks) % blocks;
					uint bh = ProceduralNoise.Hash(course, block, seed);
					float du = Mathf.Min(inBlock, 1f - inBlock) * tile / blocks;
					float dv = Mathf.Min(inCourse, 1f - inCourse) * tile / courses;
					float face = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((Mathf.Min(du, dv) - 0.002f) / 0.006f));
					int s = seed + (int)(bh & 0xffu);
					float warp = ProceduralNoise.PeriodicFbm(u, v, 3, 4, 0.55f, s + 1);
					float w = ProceduralNoise.PeriodicFbm(u + 0.15f * warp, v + 0.15f * warp, 4, 5, 0.6f, s + 2);
					float veins = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(w) * 6f), 6f);
					float fine = Mathf.Pow(Mathf.Clamp01(1f - Mathf.Abs(ProceduralNoise.PeriodicFbm(u, v, 9, 4, 0.5f, s + 3)) * 9f), 8f);
					float cloud = ProceduralNoise.PeriodicFbm(u, v, 5, 4, 0.5f, s + 4);
					int i = y * size + x;
					height[i] = face * (0.9f + 0.02f * cloud) - 0.03f * veins;
					Color stone = Ramp(dark, mid, light, 0.55f + 0.3f * cloud + (ProceduralNoise.ToUnit(bh) - 0.5f) * 0.2f);
					stone = Color.Lerp(stone, vein, veins * 0.75f + fine * 0.3f);
					stone = Color.Lerp(stone, gold, Mathf.Clamp01(veins * Mathf.Clamp01(cloud + 0.2f)) * 0.3f);
					colour[i] = Color.Lerp(joint, stone, face);
				}
			});
			return Compose(height, colour, size, tile, 0.004f, 0.58f, 0.08f, 0f, 0.4f);
		}

		// ── Ice block ─────────────────────────────────────────────────

		/// <summary>
		/// Blocks of clear ice, 0.5 × 0.4 m in running bond, their joints packed with rime; each block deeper blue toward its
		/// heart, with flaws and strings of bubbles frozen in it, the faces glassy.
		/// </summary>
		private static SurfaceMaps IceBlocks(int size, int seed)
		{
			const int courses = 5, blocks = 4;
			float tile = PaletteTileMetres("IceBlock").Value;
			var height = new float[size * size];
			var colour = new Color[size * size];
			Color deep = Lin("#7fa5bd"), mid = Lin("#a9c7d8"), pale = Lin("#d3e5ee"), rime = Lin("#eef4f7"), flaw = Lin("#e6f0f5");
			Parallel.For(0, size, y =>
			{
				float v = (y + 0.5f) / size;
				int course = Mathf.Min(courses - 1, (int)(v * courses));
				float inCourse = v * courses - course;
				float offset = (course & 1) * 0.5f + (ProceduralNoise.ToUnit(ProceduralNoise.Hash(course, 13, seed)) - 0.5f) * 0.15f;
				for (int x = 0; x < size; x++)
				{
					float u = (x + 0.5f) / size;
					float f = u * blocks + offset;
					int block = Mathf.FloorToInt(f);
					float inBlock = f - block;
					block = ((block % blocks) + blocks) % blocks;
					uint bh = ProceduralNoise.Hash(course, block, seed);
					float du = Mathf.Min(inBlock, 1f - inBlock) * tile / blocks;
					float dv = Mathf.Min(inCourse, 1f - inCourse) * tile / courses;
					float d = Mathf.Min(du, dv);
					float face = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d - 0.005f) / 0.015f));
					// Deeper toward the block's heart, rounded (a nearest-edge falloff drew the block's diagonals).
					float heart = Mathf.Sqrt(Mathf.Sin(Mathf.PI * inBlock) * Mathf.Sin(Mathf.PI * inCourse));
					int s = seed + (int)(bh & 0xffu);
					float crack = Mathf.Clamp01((ProceduralNoise.PeriodicRidged(u, v, 6, 3, 0.5f, s + 1) - 0.88f) * 12f);
					float bubbles = Mathf.Clamp01((ProceduralNoise.PeriodicFbm(u, v, 80, 2, 0.5f, s + 2) - 0.5f) * 8f)
						* Mathf.Clamp01((ProceduralNoise.PeriodicFbm(u, v, 4, 2, 0.5f, s + 3) + 0.1f) * 3f);
					float frost = ProceduralNoise.PeriodicFbm(u, v, 64, 3, 0.5f, seed + 4);
					int i = y * size + x;
					height[i] = face * 0.85f + (1f - face) * (0.35f + 0.1f * frost);
					Color ice = Ramp(pale, mid, deep, heart * 0.9f + (ProceduralNoise.ToUnit(bh) - 0.5f) * 0.25f);
					ice = Color.Lerp(ice, flaw, crack * 0.7f + bubbles * 0.5f);
					colour[i] = Color.Lerp(rime * (0.94f + 0.06f * frost), ice, face);
				}
			});
			return Compose(height, colour, size, tile, 0.008f, 0.78f, 0.15f, 0f, 0.35f);
		}

		// ── Bone ──────────────────────────────────────────────────────

		/// <summary>
		/// Long bones laid in rows (ten a tile), end to end and lashed with rawhide where they meet, each swelling to its
		/// knuckles; dark hide shows between the rows. Ivory, pitted and cracked with age.
		/// </summary>
		private static SurfaceMaps Bones(int size, int seed)
		{
			const int rows = 10;
			float tile = PaletteTileMetres("Bone").Value;
			var height = new float[size * size];
			var colour = new Color[size * size];
			Color dark = Lin("#a89a7a"), mid = Lin("#cbbf9f"), light = Lin("#e6dcc4"), hide = Lin("#3a2b1f"), lashing = Lin("#6e4c30");
			var cuts = new float[rows][];
			for (int r = 0; r < rows; r++)
			{
				cuts[r] = Cuts(r, 2 + (int)(ProceduralNoise.Hash(r, 4, seed) % 2u), 0.3f, seed + 50);
			}
			Parallel.For(0, size, y =>
			{
				float v = (y + 0.5f) / size;
				int row = Mathf.Min(rows - 1, (int)(v * rows));
				float across = v * rows - row;
				for (int x = 0; x < size; x++)
				{
					float u = (x + 0.5f) / size;
					float toJoint = ToCut(u, cuts[row], out int bone, out float along) * tile;
					uint bh = ProceduralNoise.Hash(row, bone, seed);
					// The shaft's half-thickness across the row, swelling at both knuckles.
					float ends = Mathf.Pow(Mathf.Abs(along * 2f - 1f), 6f);
					float radius = 0.32f + 0.14f * ends;
					float c = (across - 0.5f) / radius;
					float round = Mathf.Sqrt(Mathf.Max(0f, 1f - c * c * 0.25f * 4f));
					float gapEnd = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((toJoint - 0.003f) / 0.01f));
					float wrap = Mathf.Clamp01(1f - Mathf.Abs(toJoint - 0.03f) / 0.02f) * ((bh & 1u) == 0u ? 1f : 0f) * (round > 0f ? 1f : 0f);
					float pits = Mathf.Clamp01((ProceduralNoise.PeriodicFbm(u, v, 70, 2, 0.5f, seed + 5) - 0.45f) * 6f);
					float crack = Mathf.Clamp01((ProceduralNoise.PeriodicRidged(u, v, 8, 3, 0.5f, seed + 6) - 0.9f) * 12f);
					float grain = ProceduralNoise.PeriodicFbm(u, v, 6, 48, 3, 0.5f, seed + 7);
					int i = y * size + x;
					float b = round * gapEnd;
					height[i] = b * (0.8f + 0.05f * grain) - 0.2f * pits * b - 0.25f * crack * b + 0.12f * wrap;
					Color ivory = Ramp(dark, mid, light, 0.5f + 0.2f * grain + (ProceduralNoise.ToUnit(bh) - 0.5f) * 0.5f);
					ivory = Color.Lerp(ivory, dark * 0.7f, pits * 0.5f + crack * 0.6f);
					ivory = Color.Lerp(ivory, lashing, wrap);
					colour[i] = Color.Lerp(hide, ivory, Mathf.Clamp01(b * 3f));
				}
			});
			return Compose(height, colour, size, tile, 0.04f, 0.3f, 0.12f, 0f, 0.85f);
		}
	}
}
#endif
