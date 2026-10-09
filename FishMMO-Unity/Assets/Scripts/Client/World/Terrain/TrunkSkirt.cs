using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishMMO.Client
{
	/// <summary>
	/// A tree mesh with a skirt hung off its trunk's open bottom: the GPU tree path's way of making a trunk meet the
	/// ground wherever the ground is, with no art regenerated.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A generated trunk is a tube that ends, open, at the tree's pivot. The scatter sinks the tree a little, more on a
	/// slope, but the ground under one trunk is no plane: its downhill side, a dip, a heightmap texel's fold fall away
	/// below the ring and leave a gap under the bark (Jim, 2026-10-08).
	/// </para>
	/// <para>
	/// The skirt is a second ring under every open bark edge near the bottom of the mesh: each of its vertices a copy of
	/// the ring vertex above it — position, normal, colour, the plant's part data — so the shader moves it exactly as it
	/// moves the ring (the plant's own girth and lean, its burial, the wind), and the skirt's top is the trunk's own
	/// vertices, so nothing can open between them. Its foot is flagged in the flutter channel (TEXCOORD1.y = −(1 + the
	/// bark's v per metre), a value the generators never write), and the vegetation shader drops it onto the terrain
	/// under it per instance and per vertex (FishVegetationPasses.hlsl VegSkirtFoot), reading the same heightmap copy as
	/// the contact blend. Where the ground is higher than the ring the foot stays on it: a sliver of no area.
	/// </para>
	/// <para>
	/// Built once per mesh when a tree model registers with a GPU renderer that takes the contact blend; the CPU fallback
	/// and every other renderer draw the mesh as authored. Bark is told by its vertex colour's alpha (0: the tint mask,
	/// TreeMeshes' Bark), so leaf cards and billboards, which are open everywhere, never grow one.
	/// </para>
	/// </remarks>
	public static class TrunkSkirt
	{
		/// <summary>How far above the bark's lowest vertex an open edge still counts as the trunk's foot (m).</summary>
		public const float FootBand = 0.05f;

		/// <summary>Vertices closer than this are one point when finding open edges (a tube's seam doubles its vertices).</summary>
		private const float WeldMetres = 0.001f;

		/// <summary>Highest bark alpha (of 255): the generators write 0 for bark and 255 for what the tint applies to.</summary>
		private const byte BarkAlpha = 8;

		/// <summary>
		/// A copy of <paramref name="source"/> with skirts under its trunks' feet, or null when it has none to give (no
		/// open bark at its bottom, not readable, no colours or no wind channel to carry the flag).
		/// <paramref name="skirtSubmesh"/> says which submeshes may grow one (drawn with the vegetation shader, not a
		/// camera-facing billboard). The copy's CPU data is freed once uploaded unless <paramref name="keepReadable"/>.
		/// </summary>
		public static Mesh Build(Mesh source, IReadOnlyList<bool> skirtSubmesh, bool keepReadable = false)
		{
			if (source == null || !source.isReadable || !source.HasVertexAttribute(VertexAttribute.Color) || !source.HasVertexAttribute(VertexAttribute.TexCoord1))
			{
				return null;
			}
			var positions = new List<Vector3>();
			source.GetVertices(positions);
			var colours = new List<Color32>();
			source.GetColors(colours);
			int n = positions.Count;
			if (n == 0 || colours.Count != n)
			{
				return null;
			}

			// The bark triangles, and the lowest bark vertex.
			var bark = new List<int>();
			var triangles = new List<int>();
			float minY = float.MaxValue;
			for (int s = 0; s < source.subMeshCount; s++)
			{
				if (s >= skirtSubmesh.Count || !skirtSubmesh[s] || source.GetTopology(s) != MeshTopology.Triangles)
				{
					continue;
				}
				source.GetTriangles(triangles, s);
				for (int t = 0; t + 2 < triangles.Count; t += 3)
				{
					int a = triangles[t], b = triangles[t + 1], c = triangles[t + 2];
					if (colours[a].a > BarkAlpha || colours[b].a > BarkAlpha || colours[c].a > BarkAlpha)
					{
						continue;
					}
					bark.Add(s);
					bark.Add(a);
					bark.Add(b);
					bark.Add(c);
					minY = Mathf.Min(minY, Mathf.Min(positions[a].y, Mathf.Min(positions[b].y, positions[c].y)));
				}
			}
			if (bark.Count == 0)
			{
				return null;
			}

			// Open edges by position, not index: a tube's seam is two columns of vertices at one place, and its edges
			// would otherwise all read as open.
			var welded = new Dictionary<Vector3Int, int>();
			var weld = new int[n];
			for (int i = 0; i < n; i++)
			{
				Vector3 p = positions[i] / WeldMetres;
				var key = new Vector3Int(Mathf.RoundToInt(p.x), Mathf.RoundToInt(p.y), Mathf.RoundToInt(p.z));
				if (!welded.TryGetValue(key, out int id))
				{
					welded.Add(key, id = welded.Count);
				}
				weld[i] = id;
			}
			var edges = new HashSet<long>();
			for (int k = 0; k < bark.Count; k += 4)
			{
				int a = weld[bark[k + 1]], b = weld[bark[k + 2]], c = weld[bark[k + 3]];
				edges.Add(Edge(a, b));
				edges.Add(Edge(b, c));
				edges.Add(Edge(c, a));
			}

			var uv0 = new List<Vector2>();
			source.GetUVs(0, uv0);
			var wind = new List<Vector4>();
			source.GetUVs(1, wind);
			bool hasUv = uv0.Count == n;

			// Each open edge a → b at the foot (in its triangle's winding) gets the quad b, a, a', b' below it: the
			// triangles (b, a, a') and (b, a', b') face the way the bark above them does.
			var feet = new Dictionary<int, int>();
			var footSource = new List<int>();
			var footVPerMetre = new List<float>();
			var added = new List<int>[source.subMeshCount];
			float top = minY + FootBand;
			for (int k = 0; k < bark.Count; k += 4)
			{
				int s = bark[k];
				for (int e = 0; e < 3; e++)
				{
					int a = bark[k + 1 + e], b = bark[k + 1 + (e + 1) % 3], c = bark[k + 1 + (e + 2) % 3];
					if (positions[a].y > top || positions[b].y > top || edges.Contains(Edge(weld[b], weld[a])))
					{
						continue;
					}
					// The bark's v per metre up the trunk, from the triangle's third vertex (the ring above).
					float rise = positions[c].y - positions[a].y;
					float vPerMetre = hasUv && rise > 0.01f ? Mathf.Clamp((uv0[c].y - uv0[a].y) / rise, 0f, 100f) : 0f;
					int footA = Foot(a, vPerMetre, n, feet, footSource, footVPerMetre);
					int footB = Foot(b, vPerMetre, n, feet, footSource, footVPerMetre);
					List<int> list = added[s] ??= new List<int>();
					list.Add(b); list.Add(a); list.Add(footA);
					list.Add(b); list.Add(footA); list.Add(footB);
				}
			}
			if (footSource.Count == 0)
			{
				return null;
			}

			var mesh = new Mesh
			{
				name = source.name + " (trunk skirt)",
				hideFlags = HideFlags.DontSave,
				indexFormat = n + footSource.Count > 65535 ? IndexFormat.UInt32 : source.indexFormat,
			};
			// Every vertex as it was, then the feet: copies of their ring vertices, flagged.
			foreach (int i in footSource)
			{
				positions.Add(positions[i]);
				colours.Add(colours[i]);
			}
			mesh.SetVertices(positions);
			mesh.SetColors(colours);
			if (source.HasVertexAttribute(VertexAttribute.Normal))
			{
				var normals = new List<Vector3>();
				source.GetNormals(normals);
				foreach (int i in footSource) normals.Add(normals[i]);
				mesh.SetNormals(normals);
			}
			if (source.HasVertexAttribute(VertexAttribute.Tangent))
			{
				var tangents = new List<Vector4>();
				source.GetTangents(tangents);
				foreach (int i in footSource) tangents.Add(tangents[i]);
				mesh.SetTangents(tangents);
			}
			for (int channel = 0; channel < 8; channel++)
			{
				if (!source.HasVertexAttribute(VertexAttribute.TexCoord0 + channel))
				{
					continue;
				}
				var uvs = new List<Vector4>();
				source.GetUVs(channel, uvs);
				for (int f = 0; f < footSource.Count; f++)
				{
					Vector4 v = uvs[footSource[f]];
					if (channel == 1)
					{
						v.y = -(1f + footVPerMetre[f]);
					}
					uvs.Add(v);
				}
				SetUVs(mesh, channel, uvs, source.GetVertexAttributeDimension(VertexAttribute.TexCoord0 + channel));
			}
			mesh.subMeshCount = source.subMeshCount;
			for (int s = 0; s < source.subMeshCount; s++)
			{
				if (source.GetTopology(s) != MeshTopology.Triangles)
				{
					var indices = new List<int>();
					source.GetIndices(indices, s);
					mesh.SetIndices(indices, source.GetTopology(s), s, false);
					continue;
				}
				source.GetTriangles(triangles, s);
				if (added[s] != null)
				{
					triangles.AddRange(added[s]);
				}
				mesh.SetTriangles(triangles, s, false);
			}
			// The feet go down to wherever the ground is (at most 3 m: FishVegetationPasses.hlsl FISH_SKIRT_MAX_DROP).
			Bounds bounds = source.bounds;
			bounds.Encapsulate(bounds.min - new Vector3(0f, 3.2f, 0f));
			mesh.bounds = bounds;
			mesh.UploadMeshData(!keepReadable);
			return mesh;
		}

		private static long Edge(int from, int to) => ((long)from << 32) | (uint)to;

		private static int Foot(int ring, float vPerMetre, int vertexCount, Dictionary<int, int> feet, List<int> footSource, List<float> footVPerMetre)
		{
			if (!feet.TryGetValue(ring, out int foot))
			{
				foot = vertexCount + footSource.Count;
				feet.Add(ring, foot);
				footSource.Add(ring);
				footVPerMetre.Add(vPerMetre);
			}
			return foot;
		}

		/// <summary>Sets a UV channel at the source's own dimension, so the vertex layout the shaders read is unchanged.</summary>
		private static void SetUVs(Mesh mesh, int channel, List<Vector4> uvs, int dimension)
		{
			switch (dimension)
			{
				case 1:
				case 2:
					var two = new List<Vector2>(uvs.Count);
					foreach (Vector4 v in uvs) two.Add(v);
					mesh.SetUVs(channel, two);
					break;
				case 3:
					var three = new List<Vector3>(uvs.Count);
					foreach (Vector4 v in uvs) three.Add(v);
					mesh.SetUVs(channel, three);
					break;
				default:
					mesh.SetUVs(channel, uvs);
					break;
			}
		}
	}
}
