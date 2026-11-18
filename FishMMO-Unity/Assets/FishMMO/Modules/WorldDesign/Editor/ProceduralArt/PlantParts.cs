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

		/// <summary>
		/// A surface of revolution: <paramref name="profile"/> (x the radius, y the height) turned about the up
		/// axis through <paramref name="centre"/>. Faces turn outward by the profile's direction — up the outside
		/// faces out, across the top faces up, down an inside wall faces in. Normals are left to
		/// <see cref="MeshBuilder.RecalculateNormals"/>.
		/// </summary>
		/// <remarks>
		/// The sea floor's domes, cups and barrels were built with this first (it lived in SeaFloorMeshes); it moved
		/// here unchanged so mushrooms, cushions, sea cucumbers, clams and stumps can turn their profiles too. Every
		/// existing call passes neither <paramref name="submesh"/> nor <paramref name="uvMap"/>, so their meshes are
		/// the same bytes as before the move.
		/// </remarks>
		/// <param name="radiusScale">Scales the radius at (profile share 0..1, round 0..1); null for round.</param>
		/// <param name="colour">The colour at (profile share, round).</param>
		/// <param name="wind">Sway and flutter weights at a profile share; null for none.</param>
		/// <param name="submesh">The sub-mesh the faces go in (a detail has only 0; a tree's bark is <see cref="TreeMeshes.BarkSubmesh"/>).</param>
		/// <param name="uvMap">(round 0..1, profile share 0..1) to a UV; null for the atlas's <see cref="FoliageCell.Solid"/> cell.</param>
		public static void Lathe(MeshBuilder mesh, Vector3 centre, IList<Vector2> profile, int sides,
			Func<float, float, float> radiusScale, Func<float, float, Color32> colour, Func<float, Vector2> wind = null, float yaw = 0f,
			int submesh = 0, Func<float, float, Vector2> uvMap = null)
		{
			int count = profile.Count;
			if (count < 2)
			{
				return;
			}
			sides = Mathf.Max(3, sides);
			int first = mesh.VertexCount;
			for (int i = 0; i < count; i++)
			{
				float s = (float)i / (count - 1);
				for (int k = 0; k <= sides; k++)
				{
					float u = (float)k / sides;
					float angle = yaw + u * Mathf.PI * 2f;
					var radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
					float r = Mathf.Max(1e-4f, profile[i].x) * (radiusScale != null ? radiusScale(s, u) : 1f);
					Vector2 uv = uvMap != null ? uvMap(u, s) : FoliageAtlas.CellUV(FoliageCell.Solid, Mathf.Clamp01(u), Mathf.Repeat(s, 1f));
					mesh.AddVertex(centre + radial * r + Vector3.up * profile[i].y, Vector3.zero, uv, colour(s, u),
						wind != null ? wind(s) : Vector2.zero);
				}
			}
			int row = sides + 1;
			for (int i = 0; i + 1 < count; i++)
			{
				Vector2 along = profile[i + 1] - profile[i];
				if (along.sqrMagnitude < 1e-12f)
				{
					continue;
				}
				// Outward in the profile's plane: right of the direction the profile runs (x radius, y up).
				var outward = new Vector2(along.y, -along.x);
				for (int k = 0; k < sides; k++)
				{
					float angle = yaw + (k + 0.5f) / sides * Mathf.PI * 2f;
					var radial = new Vector3(Mathf.Cos(angle), 0f, Mathf.Sin(angle));
					Vector3 facing = radial * outward.x + Vector3.up * outward.y;
					int a = first + i * row + k, b = a + 1, c = a + row + 1, d = a + row;
					mesh.AddQuad(submesh, a, b, c, d, facing);
				}
			}
		}

		/// <summary>
		/// The centre line of a wandering limb from <paramref name="a"/> to <paramref name="c"/>, appended to
		/// <paramref name="into"/>: an arc lifted at its middle by <paramref name="lift"/> of its length and bent off it by
		/// two waves of their own phase and pitch, <paramref name="wander"/> of its length at most and nothing at either
		/// end. The same curve the tree generator's crooked limbs follow (TreeMeshes' Grower.Crooked, which keeps its own
		/// copy so no tree changes), here for fallen logs, driftwood and branches (DeadwoodMeshes, FloraMeshes).
		/// Draws three numbers from <paramref name="rng"/>.
		/// </summary>
		public static void CrookedPath(Vector3 a, Vector3 c, int segments, float wander, float lift, DeterministicRNG rng, List<Vector3> into)
		{
			Vector3 axis = c - a;
			float length = Mathf.Max(0.01f, axis.magnitude);
			Vector3 along = axis / length;
			Vector3 side = Perpendicular(along).normalized;
			Vector3 other = Vector3.Cross(along, side).normalized;
			float phaseA = rng.NextFloat() * Mathf.PI * 2f, phaseB = rng.NextFloat() * Mathf.PI * 2f;
			float waves = rng.Range(1.2f, 2.6f);
			segments = Mathf.Max(1, segments);
			for (int s = 0; s <= segments; s++)
			{
				float t = (float)s / segments;
				float envelope = Mathf.Sin(Mathf.PI * t);
				into.Add(Vector3.Lerp(a, c, t) + Vector3.up * (lift * length * 4f * t * (1f - t))
					+ (side * Mathf.Sin(waves * Mathf.PI * t + phaseA) + other * Mathf.Sin((waves + 0.7f) * Mathf.PI * t + phaseB)) * (wander * length * envelope));
			}
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
