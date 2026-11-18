#if UNITY_EDITOR
using System;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// A tree's last level of detail: one quad carrying a picture of the tree, rendered here in
	/// software from its own mesh, which the vegetation shader turns to face the camera.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why one turning quad, not crossed ones (2026-10-02).</b> Two fixed quads crossing on the
	/// trunk read as a box from every angle but the four they face: at 45° both show foreshortened,
	/// and the picture's transparent margins make the planes' edges visible as a thin cross; with the
	/// textures missing they drew as grey slabs. A quad turned about the trunk toward the camera
	/// (cylindrical billboarding, <c>_FacingCamera</c> in FishVegetationPasses.hlsl — what Unity's own
	/// terrain billboards do) always shows the whole silhouette at full width, costs two triangles, and
	/// at the billboard's distance (past ~160 m at High, <see cref="ProceduralArtCatalogue.TreeLodHeights"/>) the
	/// one side it shows is indistinguishable from the others for a tree. More crossed planes would
	/// only add slivers; octahedral impostors would be truer from above, which a ground-level game
	/// at that range does not need. The mesh's bounds are widened to the cylinder it sweeps.
	/// </para>
	/// <para>
	/// <b>Why a billboard is made at all.</b> Unity draws automatic billboards only for SpeedTree
	/// and Tree Creator trees; a tree prototype that is a LODGroup prefab just keeps drawing its
	/// last mesh to the horizon. A cross-quad is four triangles and keeps the silhouette, which is
	/// what a tree a kilometre off is.
	/// </para>
	/// <para>
	/// <b>Why in software.</b> A camera render in the editor depends on the open scene, the
	/// pipeline asset, the lighting and the colour space, and none of that is the tree. An
	/// orthographic rasteriser over a few thousand triangles takes milliseconds, needs no GPU, gives
	/// the same bytes every time, and runs in a test.
	/// </para>
	/// <para>
	/// <b>Unlit, with its normals (2026-11-06).</b> The picture is the surface's own colour, and a normal map
	/// beside it holds the front-most surface's normal; the vegetation shader lights the turned quad by it
	/// (VegNormal) as it lights the meshes. It used to be lit here (a fixed sun, the back of the crown
	/// darkened) and lit again in the scene: about 0.7 of the meshes' colour, so trees went duller and
	/// browner where they changed to their billboards, and nearer that change could not come.
	/// </para>
	/// </remarks>
	public static class BillboardImpostor
	{
		/// <summary>
		/// A tree's fullness range: x the fewest of its spare limbs a plant keeps, y the fewest of its spare leaf cards; each plant
		/// keeps a share between that and all of them, from its own hash. The trees' vegetation materials' <c>_VaryFullness</c>
		/// (BiomeArtGenerator's Vegetation): keep the two the same.
		/// </summary>
		public static readonly Vector2 TreeFullness = new Vector2(0.15f, 0.55f);

		/// <summary>What the median tree keeps of its spares (<see cref="TreeFullness"/> halfway to all): the parts a billboard is pictured with.</summary>
		public static Vector2 MedianTreeKeep => new Vector2(Mathf.Lerp(TreeFullness.x, 1f, 0.5f), Mathf.Lerp(TreeFullness.y, 1f, 0.5f));

		public sealed class Result
		{
			public Color32[] Pixels;
			public int Width, Height;
			/// <summary>
			/// The picture's normals at half its size (<see cref="NormalWidth"/> × <see cref="NormalHeight"/>), tangent space of the
			/// quad as the shader turns it (x across, y up, z toward the viewer), packed 0..1 as a normal map's colour.
			/// </summary>
			public Color32[] Normals;
			public int NormalWidth, NormalHeight;
			public MeshBuilder Mesh;
		}

		/// <summary>The colour and coverage of a point on the tree: (sub-mesh, uv, vertex colour) → rgba.</summary>
		public delegate Color Shade(int submesh, Vector2 uv, Color vertexColour);

		/// <param name="height">Texture height in pixels; the width follows the tree's proportions, a power of two.</param>
		/// <param name="keep">
		/// The spares pictured (x limbs, y leaf cards: a part ranked above is left out, as the vegetation shader's VegVary drops it
		/// from a plant that keeps no more); <see cref="Vector2.zero"/> pictures every part. Unset: the median tree's
		/// (<see cref="MedianTreeKeep"/>). The billboard replaces trees that drop theirs, and a picture of every spare drew each
		/// one denser than the tree it stood for (2026-11-06).
		/// </param>
		public static Result Render(MeshBuilder tree, Shade shade, int height, float swayHeight, float tintable, Vector2? keep = null)
		{
			Vector2 kept = keep ?? MedianTreeKeep;
			Bounds bounds = tree.Bounds;
			float reach = 0.01f;
			foreach (Vector3 p in tree.Positions)
			{
				reach = Mathf.Max(reach, Mathf.Abs(p.x), Mathf.Abs(p.z));
			}
			float bottom = Mathf.Min(0f, bounds.min.y), top = bounds.max.y;
			float tall = Mathf.Max(0.01f, top - bottom);
			int width = Mathf.Clamp(Mathf.NextPowerOfTwo(Mathf.RoundToInt(height * 2f * reach / tall)), 32, height);

			// Rendered at twice the size and filtered down: the cutout edge is antialiased in alpha.
			int sw = width * 2, sh = height * 2;
			var colour = new Color[sw * sh];
			var normal = new Vector3[sw * sh];
			var depth = new float[sw * sh];
			for (int i = 0; i < depth.Length; i++)
			{
				depth[i] = float.MaxValue;
			}

			for (int s = 0; s < tree.Submeshes.Count; s++)
			{
				var list = tree.Submeshes[s];
				for (int t = 0; t + 2 < list.Count; t += 3)
				{
					int ia = list[t], ib = list[t + 1], ic = list[t + 2];
					if (kept != Vector2.zero && tree.HasParts && Dropped(tree.PartData[ia], kept))
					{
						continue;
					}
					Vector3 pa = tree.Positions[ia], pb = tree.Positions[ib], pc = tree.Positions[ic];
					// View from -Z looking down +Z: x right, y up, z away.
					Vector2 sa = new Vector2((pa.x + reach) / (2f * reach) * sw, (pa.y - bottom) / tall * sh);
					Vector2 sb = new Vector2((pb.x + reach) / (2f * reach) * sw, (pb.y - bottom) / tall * sh);
					Vector2 sc = new Vector2((pc.x + reach) / (2f * reach) * sw, (pc.y - bottom) / tall * sh);
					float area = Edge(sa, sb, sc);
					if (Mathf.Abs(area) < 1e-6f)
					{
						continue;
					}
					int minX = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(sa.x, Mathf.Min(sb.x, sc.x))));
					int maxX = Mathf.Min(sw - 1, Mathf.CeilToInt(Mathf.Max(sa.x, Mathf.Max(sb.x, sc.x))));
					int minY = Mathf.Max(0, Mathf.FloorToInt(Mathf.Min(sa.y, Mathf.Min(sb.y, sc.y))));
					int maxY = Mathf.Min(sh - 1, Mathf.CeilToInt(Mathf.Max(sa.y, Mathf.Max(sb.y, sc.y))));
					for (int y = minY; y <= maxY; y++)
					{
						for (int x = minX; x <= maxX; x++)
						{
							var q = new Vector2(x + 0.5f, y + 0.5f);
							float wa = Edge(sb, sc, q) / area, wb = Edge(sc, sa, q) / area, wc = Edge(sa, sb, q) / area;
							if (wa < 0f || wb < 0f || wc < 0f)
							{
								continue;
							}
							float z = pa.z * wa + pb.z * wb + pc.z * wc;
							int i = y * sw + x;
							if (z >= depth[i])
							{
								continue;
							}
							Vector2 uv = tree.UVs[ia] * wa + tree.UVs[ib] * wb + tree.UVs[ic] * wc;
							Color vc = (Color)tree.Colors[ia] * wa + (Color)tree.Colors[ib] * wb + (Color)tree.Colors[ic] * wc;
							Color c = shade(s, uv, vc);
							if (c.a < 0.5f)
							{
								continue;
							}
							// The surface's own colour and normal, unlit: the shader lights the picture by its normals as it lights
							// the meshes (2026-11-06). A picture lit once here (a fixed sun, the back of the crown darkened) and
							// again in the scene came out at about 0.7 of the meshes' colour: duller and browner than the tree it
							// took over from.
							Vector3 n = (tree.Normals[ia] * wa + tree.Normals[ib] * wb + tree.Normals[ic] * wc).normalized;
							depth[i] = z;
							normal[i] = n;
							colour[i] = new Color(c.r, c.g, c.b, 1f);
						}
					}
				}
			}

			var pixels = new Color32[width * height];
			for (int y = 0; y < height; y++)
			{
				for (int x = 0; x < width; x++)
				{
					Color sum = Color.clear;
					float a = 0f;
					for (int k = 0; k < 4; k++)
					{
						Color c = colour[(y * 2 + (k >> 1)) * sw + x * 2 + (k & 1)];
						sum += c * c.a;
						a += c.a;
					}
					Color avg = a > 0f ? sum / a : Color.clear;
					pixels[y * width + x] = new Color32(
						(byte)Mathf.Clamp(Mathf.RoundToInt(avg.r * 255f), 0, 255),
						(byte)Mathf.Clamp(Mathf.RoundToInt(avg.g * 255f), 0, 255),
						(byte)Mathf.Clamp(Mathf.RoundToInt(avg.b * 255f), 0, 255),
						(byte)Mathf.Clamp(Mathf.RoundToInt(a / 4f * 255f), 0, 255));
				}
			}

			// Normals at half the picture's size: each texel the coverage-weighted mean of its 4×4 supersamples, in the quad's
			// tangent space (object x across, object y up, object −z toward the viewer). Where nothing is drawn, straight out.
			int nw = Mathf.Max(1, width / 2), nh = Mathf.Max(1, height / 2);
			var normals = new Color32[nw * nh];
			for (int y = 0; y < nh; y++)
			{
				for (int x = 0; x < nw; x++)
				{
					Vector3 sum = Vector3.zero;
					for (int k = 0; k < 16; k++)
					{
						int i = (y * 4 + (k >> 2)) * sw + x * 4 + (k & 3);
						if (colour[i].a > 0f)
						{
							sum += normal[i];
						}
					}
					Vector3 t = sum.sqrMagnitude > 1e-8f ? TangentNormal(sum.normalized) : Vector3.forward;
					normals[y * nw + x] = PackNormal(t);
				}
			}

			return new Result
			{
				Pixels = pixels, Width = width, Height = height, Normals = normals, NormalWidth = nw, NormalHeight = nh,
				Mesh = FacingQuad(reach, bottom, top, swayHeight, tintable),
			};
		}

		/// <summary>
		/// An object-space normal as the billboard's tangent space holds it: x across (object x), y up (object y), z toward the
		/// viewer (object −z, the side the picture is taken from). A normal map keeps only the viewer's hemisphere (its z is
		/// rebuilt from x and y), so one facing away is mirrored into it: the front-most surface faces the viewer nearly always.
		/// </summary>
		public static Vector3 TangentNormal(Vector3 objectNormal)
		{
			var t = new Vector3(objectNormal.x, objectNormal.y, Mathf.Abs(objectNormal.z));
			return t.sqrMagnitude > 1e-8f ? t.normalized : Vector3.forward;
		}

		/// <summary>A tangent-space normal as a normal map's texel (−1..1 to 0..255, z kept for the importer to drop).</summary>
		public static Color32 PackNormal(Vector3 t) => new Color32(
			(byte)Mathf.Clamp(Mathf.RoundToInt((t.x * 0.5f + 0.5f) * 255f), 0, 255),
			(byte)Mathf.Clamp(Mathf.RoundToInt((t.y * 0.5f + 0.5f) * 255f), 0, 255),
			(byte)Mathf.Clamp(Mathf.RoundToInt((t.z * 0.5f + 0.5f) * 255f), 0, 255),
			255);

		/// <summary>
		/// Whether a plant that keeps <paramref name="keep"/> of its spares (x limbs, y leaf cards) leaves this part out: exactly
		/// VegVary's test (FishVegetationPasses.hlsl), on a part's data (<see cref="MeshBuilder.PartData"/>: y its keep rank,
		/// z what it is, w a card's keep rank).
		/// </summary>
		public static bool Dropped(Vector4 part, Vector2 keep)
		{
			float kind = part.z;
			if (kind <= 0.5f)
			{
				return false;
			}
			bool leafCard = kind > 2.5f && kind < 3.5f;
			return part.y > keep.x + 1e-4f || (leafCard && part.w > keep.y + 1e-4f);
		}

		private static float Edge(Vector2 a, Vector2 b, Vector2 c) => (b.x - a.x) * (c.y - a.y) - (b.y - a.y) * (c.x - a.x);

		/// <summary>
		/// The picture on one quad in the object's x–y plane, facing −z, centred on the trunk: the
		/// shader turns object x and −z toward the camera about the trunk's axis.
		/// </summary>
		public static MeshBuilder FacingQuad(float reach, float bottom, float top, float swayHeight, float tintable)
		{
			var mesh = new MeshBuilder(1);
			var colour = PlantParts.C32(Color.white, tintable);
			float height = Mathf.Max(0.1f, top - bottom);
			int[] corners = new int[4];
			for (int k = 0; k < 4; k++)
			{
				float u = (k == 1 || k == 2) ? 1f : 0f;
				float v = k >= 2 ? 1f : 0f;
				var p = new Vector3(Mathf.Lerp(-reach, reach, u), Mathf.Lerp(bottom, top, v), 0f);
				// Normals round like the crown's, up and out toward the viewer, so the picture is not lit as a flat wall.
				Vector3 n = (Vector3.right * Mathf.Lerp(-0.6f, 0.6f, u) + Vector3.up * 0.8f + Vector3.back * 0.5f).normalized;
				float f = Mathf.Clamp01((p.y - bottom) / height);
				corners[k] = mesh.AddVertex(p, n, new Vector2(u, v), colour, new Vector2(f * f * swayHeight / 10f, 0.2f * f));
			}
			mesh.AddQuad(0, corners[0], corners[1], corners[2], corners[3], Vector3.back);
			mesh.RecalculateTangents();
			return mesh;
		}

		/// <summary>Two quads crossing on the trunk's axis, both showing the whole picture. Superseded by <see cref="FacingQuad"/>; kept for tools that draw a static impostor.</summary>
		public static MeshBuilder CrossQuads(float reach, float bottom, float top, float swayHeight, float tintable)
		{
			var mesh = new MeshBuilder(1);
			var colour = PlantParts.C32(Color.white, tintable);
			float height = Mathf.Max(0.1f, top - bottom);
			Vector2 Wind(float y)
			{
				float f = Mathf.Clamp01((y - bottom) / height);
				return new Vector2(f * f * swayHeight / 10f, 0.2f * f);
			}
			for (int q = 0; q < 2; q++)
			{
				Vector3 across = q == 0 ? Vector3.right : Vector3.forward;
				Vector3 facing = q == 0 ? Vector3.back : Vector3.right;
				int[] corners = new int[4];
				for (int k = 0; k < 4; k++)
				{
					float u = (k == 1 || k == 2) ? 1f : 0f;
					float v = k >= 2 ? 1f : 0f;
					Vector3 p = across * Mathf.Lerp(-reach, reach, u) + Vector3.up * Mathf.Lerp(bottom, top, v);
					// Normals round like the crown's, up and out, so a billboard is not lit as a flat wall.
					Vector3 n = (across * Mathf.Lerp(-0.6f, 0.6f, u) + Vector3.up * 0.8f).normalized;
					corners[k] = mesh.AddVertex(p, n, new Vector2(u, v), colour, Wind(p.y));
				}
				mesh.AddQuad(0, corners[0], corners[1], corners[2], corners[3], facing);
			}
			mesh.RecalculateTangents();
			return mesh;
		}

		/// <summary>Bilinear sample from a texture with wrap (bark) or clamp (atlas).</summary>
		public static Color Sample(Color32[] pixels, int size, Vector2 uv, bool wrap) => FoliageAtlas.Sample(pixels, size, uv, wrap);
	}
}
#endif
