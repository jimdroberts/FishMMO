#if UNITY_EDITOR
using System;
using System.Threading.Tasks;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The structure kit's tileable surfaces — timber planks, plaster, thatch, dressed stone, fieldstone, cloth, iron,
	/// earth, monolith — and the finish albedos (moss, char, algae) every one is also made in.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Plaster, thatch, fieldstone, iron and earth are plain <see cref="SurfaceSynth"/> recipes. Planks, ashlar courses and
	/// woven cloth are patterns SurfaceSynth has no motif for (rows of boards with butt joints, blocks in running bond with
	/// mortar, an over-under weave), and the monolith's jointless weathered face needs its own mix of mottling, pitting and
	/// rare hairline cracks, so their height and colour are built here and finished with SurfaceSynth's own
	/// normal, occlusion and colour passes, giving the same map layout: albedo (sRGB, A smoothness), normal (OpenGL), mask
	/// (R metallic, G occlusion, B height, A smoothness).
	/// </para>
	/// <para>
	/// <b>Mesh UVs are metres</b> (StructureShapes.ToMesh), so a material's texture scale is 1 / <see cref="TileMetres"/>
	/// and a plank is the same width on a crate as on a house.
	/// </para>
	/// <para>
	/// <b>Finishes change only the albedo</b> (Jim, 2026-10-10: tint variants via material, not new meshes): moss settles in
	/// the hollows and patches, char blackens all but a little ember brown in the cracks, algae lays a dark streaked film.
	/// The normal and mask maps are shared, so a finish costs one texture per surface.
	/// </para>
	/// </remarks>
	public static class StructureSurfaces
	{
		/// <summary>Every surface's texture size.</summary>
		public const int Size = 512;

		/// <summary>Every surface, in a fixed order.</summary>
		public static readonly string[] Names = { "Timber", "Plaster", "Thatch", "Ashlar", "Fieldstone", "Cloth", "Iron", "Earth", "Monolith" };

		/// <summary>The surface a material wears.</summary>
		public static string SurfaceOf(StructureMaterial material)
		{
			switch (material)
			{
				case StructureMaterial.Timber: return "Timber";
				case StructureMaterial.Plaster: return "Plaster";
				case StructureMaterial.Thatch: return "Thatch";
				case StructureMaterial.Stone: return "Ashlar";
				case StructureMaterial.Monolith: return "Monolith";
				case StructureMaterial.Fieldstone: return "Fieldstone";
				case StructureMaterial.Canvas:
				case StructureMaterial.Dyed: return "Cloth";
				case StructureMaterial.Iron: return "Iron";
				case StructureMaterial.Bone: return "Plaster";
				default: return "Earth"; // Earth, Ash
			}
		}

		/// <summary>The material's colour over its surface (<c>_BaseColor</c>): the two cloths' dyes, bone's ivory, ash's grey.</summary>
		public static Color TintOf(StructureMaterial material)
		{
			switch (material)
			{
				case StructureMaterial.Canvas: return new Color(0.86f, 0.78f, 0.62f);
				case StructureMaterial.Dyed: return new Color(0.62f, 0.14f, 0.11f);
				case StructureMaterial.Bone: return new Color(0.96f, 0.91f, 0.78f);
				case StructureMaterial.Ash: return new Color(0.3f, 0.29f, 0.28f);
				default: return Color.white;
			}
		}

		/// <summary>Metres one tile of the surface covers.</summary>
		public static float TileMetres(string surface)
		{
			switch (surface)
			{
				case "Timber": return 2f;
				case "Ashlar": return 2.4f;
				case "Monolith": return 2.5f;
				case "Cloth": return 1f;
				case "Thatch": return 2f;
				case "Iron": return 1.5f;
				default: return 3f;
			}
		}

		/// <summary>Builds a surface's maps.</summary>
		public static SurfaceMaps Generate(string surface, int size, int seed)
		{
			int s = ProceduralNoise.SeedFor("StructureSurface/" + surface, seed);
			switch (surface)
			{
				case "Timber": return Planks(size, s);
				case "Ashlar": return Ashlar(size, s);
				case "Cloth": return Weave(size, s);
				case "Monolith": return Monolith(size, s);
				default:
					SurfaceRecipe recipe = Recipe(surface);
					return SurfaceSynth.Generate(in recipe, size, seed);
			}
		}

		private static Color Hex(string hex)
		{
			int v = int.Parse(hex.TrimStart('#'), System.Globalization.NumberStyles.HexNumber);
			return new Color(((v >> 16) & 255) / 255f, ((v >> 8) & 255) / 255f, (v & 255) / 255f, 1f);
		}

		/// <summary>The SurfaceSynth recipes of the plain surfaces.</summary>
		public static SurfaceRecipe Recipe(string surface)
		{
			var r = new SurfaceRecipe { Name = "Structure" + surface, TileMetres = TileMetres(surface), Octaves = 5, Persistence = 0.5f, BaseCells = 4, NormalScale = 1f };
			switch (surface)
			{
				case "Plaster":
					r.ReliefMetres = 0.01f; r.Dark = Hex("#b9ae98"); r.Mid = Hex("#d6ccb6"); r.Light = Hex("#ebe3d0"); r.Warp = 0.15f;
					r.Motif = SurfaceMotif.Cracks; r.MotifCells = 5; r.MotifSize = 0.015f; r.MotifWeight = 0.15f; r.MotifDark = Hex("#8f846f"); r.MotifLight = Hex("#a69a83");
					r.Smoothness = 0.12f; r.SmoothnessVariation = 0.05f; r.Occlusion = 0.4f;
					break;
				case "Thatch":
					r.ReliefMetres = 0.06f; r.Dark = Hex("#4f3d1f"); r.Mid = Hex("#8a6d3a"); r.Light = Hex("#b8995a"); r.Warp = 0.05f;
					r.Motif = SurfaceMotif.Fibres; r.Vertical = true; r.MotifCells = 48; r.MotifSize = 0.02f; r.MotifWeight = 0.75f; r.MotifDark = Hex("#5a4421"); r.MotifLight = Hex("#c2a466");
					r.Smoothness = 0.08f; r.SmoothnessVariation = 0.05f; r.Occlusion = 0.8f;
					break;
				case "Fieldstone":
					r.ReliefMetres = 0.08f; r.Dark = Hex("#4a4843"); r.Mid = Hex("#77746c"); r.Light = Hex("#a19d93"); r.Warp = 0.2f;
					r.Motif = SurfaceMotif.Fractured; r.MotifCells = 5; r.MotifSize = 0.05f; r.MotifWeight = 0.55f; r.MotifDark = Hex("#55524c"); r.MotifLight = Hex("#8d8a82");
					r.Speckles = new[] { Hex("#9a9a7a"), Hex("#5d6347") }; r.SpeckleCount = 900; r.SpeckleSize = 0.003f;
					r.Smoothness = 0.2f; r.SmoothnessVariation = 0.15f; r.Occlusion = 0.7f;
					break;
				case "Iron":
					r.ReliefMetres = 0.004f; r.Dark = Hex("#26252a"); r.Mid = Hex("#3b3a3f"); r.Light = Hex("#5a5550"); r.Warp = 0.1f;
					r.Motif = SurfaceMotif.Granular; r.MotifWeight = 0f; r.MotifDark = Hex("#6b3d22"); r.MotifLight = Hex("#8a4f2a");
					r.Speckles = new[] { Hex("#7a4325"), Hex("#5c3520") }; r.SpeckleCount = 1600; r.SpeckleSize = 0.006f;
					r.Smoothness = 0.45f; r.SmoothnessVariation = 0.2f; r.Metallic = 0.7f; r.Occlusion = 0.3f;
					break;
				default: // Earth
					r.ReliefMetres = 0.04f; r.Dark = Hex("#3a2b1d"); r.Mid = Hex("#5c4630"); r.Light = Hex("#7d6446"); r.Warp = 0.3f;
					r.Motif = SurfaceMotif.Cobbles; r.MotifCells = 24; r.MotifSize = 0.3f; r.MotifWeight = 0.2f; r.MotifDark = Hex("#5a5248"); r.MotifLight = Hex("#7a7064");
					r.Smoothness = 0.08f; r.SmoothnessVariation = 0.1f; r.Occlusion = 0.6f;
					break;
			}
			return r;
		}

		// ── Patterns SurfaceSynth has no motif for ────────────────────

		/// <summary>Eight boards across a tile, grain along u, a butt joint in each, the odd knot.</summary>
		private static SurfaceMaps Planks(int size, int seed)
		{
			const int boards = 8;
			var height = new float[size * size];
			var colour = new Color[size * size];
			Color dark = SurfaceSynth.ToLinear(Hex("#3b2717")), mid = SurfaceSynth.ToLinear(Hex("#6e4c2d")), light = SurfaceSynth.ToLinear(Hex("#9c774f"));
			Parallel.For(0, size, y =>
			{
				float v = (y + 0.5f) / size;
				int board = Mathf.Min(boards - 1, (int)(v * boards));
				float across = v * boards - board;
				uint h = ProceduralNoise.Hash(board, 0, seed);
				float joint = ProceduralNoise.ToUnit(h);
				float tone = ProceduralNoise.ToUnit(ProceduralNoise.Mix(h ^ 0x51u));
				float knotU = ProceduralNoise.ToUnit(ProceduralNoise.Mix(h ^ 0x93u));
				for (int x = 0; x < size; x++)
				{
					float u = (x + 0.5f) / size;
					float grain = ProceduralNoise.PeriodicFbm(u, v, 3, 96, 4, 0.55f, seed + board * 31);
					float edge = Mathf.Min(across, 1f - across) * 2f; // 0 at a seam, 1 mid-board
					float groove = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(edge / 0.08f));
					float du = Mathf.Abs(u - joint);
					du = Mathf.Min(du, 1f - du) * boards;
					groove *= Mathf.SmoothStep(0f, 1f, Mathf.Clamp01(du / 0.05f));
					float knot = 0f;
					float ku = Mathf.Abs(u - knotU);
					ku = Mathf.Min(ku, 1f - ku) * boards;
					float kv = Mathf.Abs(across - 0.5f);
					float kd = Mathf.Sqrt(ku * ku * 4f + kv * kv * 16f);
					if ((h & 3u) == 0u && kd < 0.6f)
					{
						knot = 1f - kd / 0.6f;
					}
					int i = y * size + x;
					height[i] = groove * (0.75f + 0.25f * grain) - knot * 0.1f;
					float t = Mathf.Clamp01(0.5f + 0.35f * grain + (tone - 0.5f) * 0.6f);
					Color c = t < 0.5f ? Color.Lerp(dark, mid, t * 2f) : Color.Lerp(mid, light, (t - 0.5f) * 2f);
					colour[i] = Color.Lerp(c, dark, knot * 0.8f);
				}
			});
			return Compose(height, colour, size, TileMetres("Timber"), 0.006f, 0.25f, 0.1f, 0f, 0.7f);
		}

		/// <summary>Courses of dressed blocks in running bond: eight courses a tile, three or four blocks a course, mortar between.</summary>
		private static SurfaceMaps Ashlar(int size, int seed)
		{
			const int courses = 8;
			float tile = TileMetres("Ashlar");
			var height = new float[size * size];
			var colour = new Color[size * size];
			Color dark = SurfaceSynth.ToLinear(Hex("#77736b")), mid = SurfaceSynth.ToLinear(Hex("#9d988d")), light = SurfaceSynth.ToLinear(Hex("#beb8aa"));
			Color mortar = SurfaceSynth.ToLinear(Hex("#6b665c"));
			Parallel.For(0, size, y =>
			{
				float v = (y + 0.5f) / size;
				int course = Mathf.Min(courses - 1, (int)(v * courses));
				float inCourse = v * courses - course;
				uint ch = ProceduralNoise.Hash(course, 7, seed);
				int blocks = (ch & 1u) == 0u ? 3 : 4;
				float offset = ProceduralNoise.ToUnit(ProceduralNoise.Mix(ch ^ 0x2du));
				for (int x = 0; x < size; x++)
				{
					float u = (x + 0.5f) / size;
					float f = u * blocks + offset;
					int block = Mathf.FloorToInt(f);
					float inBlock = f - block;
					block = ((block % blocks) + blocks) % blocks;
					uint bh = ProceduralNoise.Hash(course, block, seed);
					// Distances to the block's edges in metres.
					float du = Mathf.Min(inBlock, 1f - inBlock) * tile / blocks;
					float dv = Mathf.Min(inCourse, 1f - inCourse) * tile / courses;
					float d = Mathf.Min(du, dv);
					float face = Mathf.SmoothStep(0f, 1f, Mathf.Clamp01((d - 0.006f) / 0.02f));
					float tool = ProceduralNoise.PeriodicFbm(u, v, 16, 4, 0.5f, seed + 5);
					float chip = ProceduralNoise.PeriodicRidged(u, v, 12, 3, 0.5f, seed + 9);
					int i = y * size + x;
					height[i] = face * (0.8f + 0.12f * tool) - (d < 0.05f ? chip * 0.15f * (1f - d / 0.05f) : 0f);
					float t = Mathf.Clamp01(0.5f + 0.25f * tool + (ProceduralNoise.ToUnit(bh) - 0.5f) * 0.7f);
					Color c = t < 0.5f ? Color.Lerp(dark, mid, t * 2f) : Color.Lerp(mid, light, (t - 0.5f) * 2f);
					colour[i] = Color.Lerp(mortar, c, face);
				}
			});
			return Compose(height, colour, size, tile, 0.02f, 0.25f, 0.12f, 0f, 0.8f);
		}

		/// <summary>
		/// One piece of weathered stone, no joints: a soft mottle in the ashlar's greys (so a statue and its masonry plinth
		/// are the same stone), a fine tooled grain, scattered pits, a few hairline cracks and pale lichen flecks.
		/// </summary>
		private static SurfaceMaps Monolith(int size, int seed)
		{
			float tile = TileMetres("Monolith");
			var height = new float[size * size];
			var colour = new Color[size * size];
			Color dark = SurfaceSynth.ToLinear(Hex("#77736b")), mid = SurfaceSynth.ToLinear(Hex("#9f9a8f")), light = SurfaceSynth.ToLinear(Hex("#c0baac"));
			Color grime = SurfaceSynth.ToLinear(Hex("#5f5b53")), lichen = SurfaceSynth.ToLinear(Hex("#b9b69c"));
			Parallel.For(0, size, y =>
			{
				float v = (y + 0.5f) / size;
				for (int x = 0; x < size; x++)
				{
					float u = (x + 0.5f) / size;
					float broad = ProceduralNoise.PeriodicFbm(u, v, 3, 5, 0.55f, seed + 1);
					float grain = ProceduralNoise.PeriodicFbm(u, v, 48, 3, 0.5f, seed + 2);
					// Pits: the deepest dips of a fine noise, sparse.
					float pit = Mathf.Clamp01((ProceduralNoise.PeriodicFbm(u, v, 40, 2, 0.5f, seed + 3) - 0.45f) * 5f);
					// Hairline cracks: the sharpest crests of a coarse ridged noise, only where a patch allows them.
					float ridge = ProceduralNoise.PeriodicRidged(u, v, 4, 3, 0.5f, seed + 4);
					float allow = Mathf.Clamp01((ProceduralNoise.PeriodicFbm(u, v, 2, 2, 0.5f, seed + 5) + 0.1f) * 3f);
					float crack = Mathf.Clamp01((ridge - 0.86f) * 12f) * allow;
					float fleck = Mathf.Clamp01((ProceduralNoise.PeriodicFbm(u, v, 24, 3, 0.5f, seed + 6) - 0.32f) * 6f);
					int i = y * size + x;
					height[i] = 0.55f + 0.3f * broad + 0.12f * grain - 0.35f * pit - 0.45f * crack;
					float t = Mathf.Clamp01(0.5f + 0.55f * broad + 0.15f * grain);
					Color c = t < 0.5f ? Color.Lerp(dark, mid, t * 2f) : Color.Lerp(mid, light, (t - 0.5f) * 2f);
					c = Color.Lerp(c, grime, Mathf.Clamp01(pit * 0.6f + crack * 0.8f));
					colour[i] = Color.Lerp(c, lichen, fleck * 0.55f);
				}
			});
			return Compose(height, colour, size, tile, 0.008f, 0.22f, 0.1f, 0f, 0.6f);
		}

		/// <summary>A plain over-under weave, near white (the materials dye it), with faint stains.</summary>
		private static SurfaceMaps Weave(int size, int seed)
		{
			const int threads = 128;
			var height = new float[size * size];
			var colour = new Color[size * size];
			Color warp = SurfaceSynth.ToLinear(Hex("#e8e2d4")), weft = SurfaceSynth.ToLinear(Hex("#d9d2c2")), stain = SurfaceSynth.ToLinear(Hex("#a59a83"));
			Parallel.For(0, size, y =>
			{
				float v = (y + 0.5f) / size;
				for (int x = 0; x < size; x++)
				{
					float u = (x + 0.5f) / size;
					float fu = u * threads, fv = v * threads;
					int cu = Mathf.FloorToInt(fu), cv = Mathf.FloorToInt(fv);
					bool over = ((cu + cv) & 1) == 0;
					float h = over ? Mathf.Sin(Mathf.PI * (fu - cu)) : Mathf.Sin(Mathf.PI * (fv - cv));
					float blot = ProceduralNoise.PeriodicFbm(u, v, 3, 4, 0.55f, seed + 3);
					int i = y * size + x;
					height[i] = 0.3f + 0.7f * h;
					Color c = over ? warp : weft;
					colour[i] = Color.Lerp(c, stain, Mathf.Clamp01((blot - 0.15f) * 1.6f));
				}
			});
			return Compose(height, colour, size, TileMetres("Cloth"), 0.0015f, 0.2f, 0.05f, 0f, 0.5f);
		}

		/// <summary>SurfaceSynth's finishing passes over a hand-built height and linear colour: normal, occlusion, albedo and mask.</summary>
		private static SurfaceMaps Compose(float[] height, Color[] linear, int size, float tileMetres, float reliefMetres, float smoothness, float smoothnessVariation, float metallic, float occlusionStrength)
		{
			SurfaceSynth.Normalise(height);
			float texel = tileMetres / size;
			Color32[] normal = SurfaceSynth.NormalsFromHeight(height, size, reliefMetres / texel);
			float[] occlusion = SurfaceSynth.OcclusionFromHeight(height, size, Mathf.Max(1, size / 64), occlusionStrength);
			var albedo = new Color32[size * size];
			var mask = new Color32[size * size];
			byte metal = Byte(metallic);
			Parallel.For(0, size, y =>
			{
				for (int x = 0; x < size; x++)
				{
					int i = y * size + x;
					float hollow = Mathf.Clamp01(1f - occlusion[i]);
					Color c = linear[i] * (1f - hollow * 0.25f);
					float smooth = Mathf.Clamp01(smoothness + (0.5f - height[i]) * smoothnessVariation);
					Color g = SurfaceSynth.ToGamma(c);
					albedo[i] = new Color32(Byte(g.r), Byte(g.g), Byte(g.b), Byte(smooth));
					mask[i] = new Color32(metal, Byte(occlusion[i]), Byte(height[i]), Byte(smooth));
				}
			});
			return new SurfaceMaps { Size = size, Height = height, Albedo = albedo, Normal = normal, Mask = mask };
		}

		private static byte Byte(float v) => (byte)Mathf.Clamp(Mathf.RoundToInt(v * 255f), 0, 255);

		// ── Finishes ──────────────────────────────────────────────────

		/// <summary>The albedo of a surface in a finish (the plain albedo for <see cref="StructureFinish.None"/>).</summary>
		public static Color32[] Finish(SurfaceMaps maps, string surface, StructureFinish finish, int seed)
		{
			if (finish == StructureFinish.None)
			{
				return maps.Albedo;
			}
			int size = maps.Size;
			int s = ProceduralNoise.SeedFor($"StructureFinish/{surface}/{finish}", seed);
			var result = new Color32[size * size];
			Color mossDark = SurfaceSynth.ToLinear(Hex("#2f3a17")), mossLight = SurfaceSynth.ToLinear(Hex("#6a7a2e"));
			Color charDark = SurfaceSynth.ToLinear(Hex("#0e0d0c")), charLight = SurfaceSynth.ToLinear(Hex("#2b2622")), ember = SurfaceSynth.ToLinear(Hex("#4a2a18"));
			Color algaeDark = SurfaceSynth.ToLinear(Hex("#1b2414")), algaeLight = SurfaceSynth.ToLinear(Hex("#3c4626"));
			Parallel.For(0, size, y =>
			{
				float v = (y + 0.5f) / size;
				for (int x = 0; x < size; x++)
				{
					float u = (x + 0.5f) / size;
					int i = y * size + x;
					Color32 a = maps.Albedo[i];
					Color c = SurfaceSynth.ToLinear(new Color(a.r / 255f, a.g / 255f, a.b / 255f));
					float occlusion = maps.Mask[i].g / 255f;
					float hollow = 1f - occlusion;
					float patch = ProceduralNoise.PeriodicFbm(u, v, 5, 4, 0.55f, s);
					float fine = ProceduralNoise.PeriodicFbm(u, v, 24, 3, 0.5f, s + 11);
					switch (finish)
					{
						case StructureFinish.Mossy:
						{
							float m = Mathf.Clamp01(patch * 1.8f + 0.2f + hollow * 2f + fine * 0.35f);
							c = Color.Lerp(c, Color.Lerp(mossDark, mossLight, 0.5f + 0.5f * fine), m * 0.9f);
							break;
						}
						case StructureFinish.Charred:
						{
							float burnt = Mathf.Clamp01(0.75f + patch * 0.6f);
							Color soot = Color.Lerp(charDark, charLight, 0.5f + 0.5f * fine);
							c = Color.Lerp(c * 0.5f, soot, burnt);
							c = Color.Lerp(c, ember, Mathf.Clamp01(hollow * 3f) * 0.5f);
							break;
						}
						default:
						{
							float streak = ProceduralNoise.PeriodicFbm(u, v, 24, 2, 3, 0.5f, s + 23);
							float film = Mathf.Clamp01(0.75f + 0.4f * streak + 0.2f * patch + hollow);
							c = Color.Lerp(c, Color.Lerp(algaeDark, algaeLight, 0.5f + 0.5f * fine), film * 0.92f);
							break;
						}
					}
					Color g = SurfaceSynth.ToGamma(c);
					result[i] = new Color32(Byte(g.r), Byte(g.g), Byte(g.b), a.a);
				}
			});
			return result;
		}
	}
}
#endif
