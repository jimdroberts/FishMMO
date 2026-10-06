#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The pieces plants are assembled from: tubes for stems, trunks and branches; strips for
	/// blades and fronds; cards for leaf clusters. Shared by the detail and tree generators.
	/// </summary>
	public static class PlantParts
	{
		/// <remarks>
		/// The colour is authored in sRGB (the catalogue's hex codes, picked by eye) and stored LINEAR: a vertex
		/// colour reaches the shader as it is written, with no decoding, and the shader multiplies it into a
		/// linear albedo. Stored as authored, every plant's colour came out two to four times too bright in its
		/// darker channels and washed toward white — part of the glow on every crown and flower (the grass blades
		/// were linearised for the same reason, GrassBladeRenderer). The alpha is a mask, not a colour.
		/// </remarks>
		public static Color32 C32(Color c, float alpha)
		{
			Color linear = c.linear;
			return new Color32(
				(byte)Mathf.Clamp(Mathf.RoundToInt(linear.r * 255f), 0, 255),
				(byte)Mathf.Clamp(Mathf.RoundToInt(linear.g * 255f), 0, 255),
				(byte)Mathf.Clamp(Mathf.RoundToInt(linear.b * 255f), 0, 255),
				(byte)Mathf.Clamp(Mathf.RoundToInt(alpha * 255f), 0, 255));
		}

		/// <summary>Any unit vector perpendicular to <paramref name="v"/>.</summary>
		public static Vector3 Perpendicular(Vector3 v)
		{
			Vector3 other = Mathf.Abs(v.y) < 0.95f ? Vector3.up : Vector3.right;
			return Vector3.Cross(v, other).normalized;
		}

		/// <summary>
		/// A tube along a path: rings of <paramref name="sides"/> vertices (plus a seam column for the
		/// UVs), framed by parallel transport so it does not twist, closed with a point when its last
		/// radius is zero.
		/// </summary>
		/// <param name="uvMetres">Metres of bark per texture tile, round and along.</param>
		/// <param name="ribs">Ridges round the tube (a cactus); 0 for round.</param>
		/// <param name="uvMap">Overrides the bark mapping: (u round 0..1, v metres along) to a UV.</param>
		/// <param name="analyticNormals">Radial normals now; false leaves them for <see cref="MeshBuilder.RecalculateNormals"/>.</param>
		public static void Tube(MeshBuilder mesh, int submesh, IList<Vector3> path, IList<float> radii, int sides, float uvMetres,
			IList<Color32> colours, IList<Vector2> wind, int ribs = 0, float ribDepth = 0f, Func<float, float, Vector2> uvMap = null, bool analyticNormals = true)
		{
			int count = path.Count;
			if (count < 2)
			{
				return;
			}
			sides = Mathf.Max(3, sides);
			Vector3 tangent = (path[1] - path[0]).normalized;
			Vector3 normal = Perpendicular(tangent);
			float along = 0f;
			int previousRing = -1;
			Vector3 previousCentre = path[0];
			float meanRadius = 0f;
			for (int i = 0; i < count; i++)
			{
				meanRadius += radii[i];
			}
			meanRadius /= count;
			float circumferenceTiles = Mathf.Max(1f, Mathf.Round(2f * Mathf.PI * meanRadius / Mathf.Max(0.01f, uvMetres)));

			for (int i = 0; i < count; i++)
			{
				Vector3 next = i < count - 1 ? (path[i + 1] - path[i]).normalized : tangent;
				Vector3 t = i == 0 ? next : ((tangent + next) * 0.5f).normalized;
				if (i > 0)
				{
					along += Vector3.Distance(path[i], path[i - 1]);
					normal = (Quaternion.FromToRotation(tangent, t) * normal).normalized;
				}
				tangent = t;
				Vector3 binormal = Vector3.Cross(tangent, normal);
				float radius = radii[i];

				if (radius <= 1e-4f && i == count - 1 && previousRing >= 0)
				{
					Vector2 tipUV = uvMap != null ? uvMap(0.5f, along) : new Vector2(circumferenceTiles * 0.5f, along / uvMetres);
					int tip = mesh.AddVertex(path[i], analyticNormals ? tangent : Vector3.zero, tipUV, colours[i], wind[i]);
					for (int s = 0; s < sides; s++)
					{
						int a = previousRing + s, b = previousRing + s + 1;
						Vector3 faceCentre = (mesh.Positions[a] + mesh.Positions[b] + path[i]) / 3f;
						mesh.AddTriangle(submesh, a, b, tip, faceCentre - previousCentre);
					}
					return;
				}

				int ring = mesh.VertexCount;
				for (int s = 0; s <= sides; s++)
				{
					float u = (float)s / sides;
					float angle = u * Mathf.PI * 2f;
					Vector3 radial = normal * Mathf.Cos(angle) + binormal * Mathf.Sin(angle);
					float r = radius * (ribs > 0 ? 1f - ribDepth * (0.5f - 0.5f * Mathf.Cos(angle * ribs)) : 1f);
					Vector2 uv = uvMap != null ? uvMap(u, along) : new Vector2(u * circumferenceTiles, along / uvMetres);
					mesh.AddVertex(path[i] + radial * r, analyticNormals ? radial : Vector3.zero, uv, colours[i], wind[i]);
				}
				if (previousRing >= 0)
				{
					for (int s = 0; s < sides; s++)
					{
						int a = previousRing + s, b = previousRing + s + 1, c = ring + s + 1, d = ring + s;
						Vector3 centre = (mesh.Positions[a] + mesh.Positions[c]) * 0.5f;
						Vector3 axis = (previousCentre + path[i]) * 0.5f;
						mesh.AddQuad(submesh, a, b, c, d, centre - axis);
					}
				}
				previousRing = ring;
				previousCentre = path[i];
			}
		}

		/// <summary>
		/// A flat strip along a spine (blade, frond, reed), its width across <paramref name="side"/>,
		/// mapped onto an atlas cell; pointed when its last width is zero.
		/// </summary>
		/// <param name="lightNormal">Normal written for lighting (bent toward the sky or the crown); zero to use the face's own.</param>
		public static void Strip(MeshBuilder mesh, int submesh, IList<Vector3> spine, IList<float> widths, Vector3 side, FoliageCell cell,
			IList<Color32> colours, IList<Vector2> wind, Vector3 lightNormal, float uMin = 0f, float uMax = 1f)
		{
			int count = spine.Count;
			if (count < 2)
			{
				return;
			}
			side = side.normalized;
			Vector3 axis = (spine[count - 1] - spine[0]).normalized;
			Vector3 face = Vector3.Cross(side, axis);
			if (face.sqrMagnitude < 1e-8f)
			{
				face = Perpendicular(axis);
			}
			face.Normalize();
			Vector3 n = lightNormal.sqrMagnitude > 1e-8f ? lightNormal.normalized : face;
			int previousLeft = -1, previousRight = -1;
			for (int i = 0; i < count; i++)
			{
				float v = (float)i / (count - 1);
				float half = widths[i] * 0.5f;
				if (half <= 1e-5f && i == count - 1 && previousLeft >= 0)
				{
					int tip = mesh.AddVertex(spine[i], n, FoliageAtlas.CellUV(cell, (uMin + uMax) * 0.5f, v), colours[i], wind[i]);
					mesh.AddTriangle(submesh, previousLeft, previousRight, tip, face);
					return;
				}
				int left = mesh.AddVertex(spine[i] - side * half, n, FoliageAtlas.CellUV(cell, uMin, v), colours[i], wind[i]);
				int right = mesh.AddVertex(spine[i] + side * half, n, FoliageAtlas.CellUV(cell, uMax, v), colours[i], wind[i]);
				if (previousLeft >= 0)
				{
					mesh.AddQuad(submesh, previousLeft, previousRight, right, left, face);
				}
				previousLeft = left;
				previousRight = right;
			}
		}

		/// <summary>A single quad card centred at <paramref name="centre"/>, spanning <paramref name="right"/> and <paramref name="up"/> (full extents).</summary>
		public static void Card(MeshBuilder mesh, int submesh, Vector3 centre, Vector3 right, Vector3 up, FoliageCell cell, Color32 colour,
			Vector2 windBottom, Vector2 windTop, Vector3 lightNormal)
		{
			Vector3 face = Vector3.Cross(right, up).normalized;
			Vector3 n = lightNormal.sqrMagnitude > 1e-8f ? lightNormal.normalized : face;
			int a = mesh.AddVertex(centre - right * 0.5f - up * 0.5f, n, FoliageAtlas.CellUV(cell, 0f, 0f), colour, windBottom);
			int b = mesh.AddVertex(centre + right * 0.5f - up * 0.5f, n, FoliageAtlas.CellUV(cell, 1f, 0f), colour, windBottom);
			int c = mesh.AddVertex(centre + right * 0.5f + up * 0.5f, n, FoliageAtlas.CellUV(cell, 1f, 1f), colour, windTop);
			int d = mesh.AddVertex(centre - right * 0.5f + up * 0.5f, n, FoliageAtlas.CellUV(cell, 0f, 1f), colour, windTop);
			mesh.AddQuad(submesh, a, b, c, d, face);
		}

		/// <summary>A card hanging from <paramref name="root"/> along <paramref name="direction"/> (a leaf spray on a branch).</summary>
		public static void SprayCard(MeshBuilder mesh, int submesh, Vector3 root, Vector3 direction, float length, float width, float roll,
			FoliageCell cell, Color32 colour, Vector2 windRoot, Vector2 windTip, Vector3 crownCentre, float bend)
		{
			Vector3 dir = direction.normalized;
			Vector3 side = Quaternion.AngleAxis(roll, dir) * Perpendicular(dir);
			Vector3 face = Vector3.Cross(side, dir).normalized;
			Vector3 mid = root + dir * length * 0.5f;
			Vector3 outward = (mid - crownCentre).sqrMagnitude > 1e-6f ? (mid - crownCentre).normalized : Vector3.up;
			// Bent toward the outside of the crown: a crown lit as one soft mass, not as a heap of flat cards.
			Vector3 n = Vector3.Slerp(Vector3.Dot(face, outward) >= 0f ? face : -face, outward, bend);
			Card(mesh, submesh, mid, side * width, dir * length, cell, colour, windRoot, windTip, n);
		}
	}
}
#endif
