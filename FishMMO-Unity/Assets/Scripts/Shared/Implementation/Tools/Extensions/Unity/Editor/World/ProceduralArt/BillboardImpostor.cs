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
	/// at the billboard's distance (past ~160 m, <see cref="ProceduralArtCatalogue.TreeLodHeights"/>) the
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
	/// the same bytes every time, and runs in a test. The picture is lit only softly — the billboard
	/// is lit again by the scene at runtime — and darkened toward the back of the crown so it keeps
	/// some depth.
	/// </para>
	/// </remarks>
	public static class BillboardImpostor
	{
		public sealed class Result
		{
			public Color32[] Pixels;
			public int Width, Height;
			public MeshBuilder Mesh;
		}

		/// <summary>The colour and coverage of a point on the tree: (sub-mesh, uv, vertex colour) → rgba.</summary>
		public delegate Color Shade(int submesh, Vector2 uv, Color vertexColour);

		/// <param name="height">Texture height in pixels; the width follows the tree's proportions, a power of two.</param>
		public static Result Render(MeshBuilder tree, Shade shade, int height, float swayHeight, float tintable)
		{
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
			var depth = new float[sw * sh];
			for (int i = 0; i < depth.Length; i++)
			{
				depth[i] = float.MaxValue;
			}
			float zMin = bounds.min.z, zRange = Mathf.Max(0.01f, bounds.size.z);
			var light = new Vector3(0.35f, 0.75f, -0.55f).normalized;

			for (int s = 0; s < tree.Submeshes.Count; s++)
			{
				var list = tree.Submeshes[s];
				for (int t = 0; t + 2 < list.Count; t += 3)
				{
					int ia = list[t], ib = list[t + 1], ic = list[t + 2];
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
							Vector3 n = (tree.Normals[ia] * wa + tree.Normals[ib] * wb + tree.Normals[ic] * wc).normalized;
							float lit = 0.6f + 0.4f * Mathf.Max(0f, Vector3.Dot(n, light));
							float back = Mathf.Lerp(1f, 0.72f, (z - zMin) / zRange);
							depth[i] = z;
							colour[i] = new Color(c.r * lit * back, c.g * lit * back, c.b * lit * back, 1f);
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

			return new Result { Pixels = pixels, Width = width, Height = height, Mesh = FacingQuad(reach, bottom, top, swayHeight, tintable) };
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
