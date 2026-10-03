#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Plain vertex and index lists a procedural mesh is assembled in, with the derived data
	/// (normals, tangents, bounds) worked out here rather than by the engine, and one call that
	/// turns the result into a <see cref="Mesh"/>.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Winding by intent, never by habit.</b> WorldEditor's convex hull lost each horizon edge's
	/// direction (its <c>Edge</c> struct sorted the two indices) and so wound roughly half the faces
	/// it added inward, and every primitive there trusted a hand-picked index order. Here every
	/// face is added with the direction it should face (<see cref="AddTriangle(int,int,int,int,Vector3)"/>),
	/// and the builder orders the indices so Unity's clockwise-front rule agrees with it. A face
	/// that faces the wrong way cannot be written.
	/// </para>
	/// <para>
	/// <b>Smooth across seams.</b> <see cref="RecalculateNormals"/> accumulates face normals per
	/// welded position, not per vertex, so a UV seam — where a vertex is split to carry two texture
	/// coordinates — does not become a shading seam. Unity's <c>Mesh.RecalculateNormals</c> treats
	/// split vertices as unrelated, which is what put a visible line down every WorldEditor cylinder
	/// at u = 0.
	/// </para>
	/// <para>
	/// <b>Tangents.</b> Per-vertex tangents from the UV derivatives (Lengyel's method), made
	/// orthogonal to the normal with the bitangent's handedness in w. That agrees with MikkTSpace
	/// wherever a mesh has no mirrored or degenerate UVs, which is everything built here; and the
	/// normal maps these meshes use are tiling detail maps, not maps baked against the mesh, so
	/// there is no baker whose basis they would have to match exactly.
	/// </para>
	/// <para>
	/// <b>MeshData, not managed arrays.</b> <see cref="ToMesh"/> writes one interleaved vertex
	/// stream through <see cref="Mesh.AllocateWritableMeshData(int)"/> — half-precision normals and
	/// tangents are not used because these meshes are small and the precision costs nothing.
	/// </para>
	/// </remarks>
	public sealed class MeshBuilder
	{
		public readonly List<Vector3> Positions = new List<Vector3>();
		public readonly List<Vector3> Normals = new List<Vector3>();
		public readonly List<Vector4> Tangents = new List<Vector4>();
		public readonly List<Vector2> UVs = new List<Vector2>();
		public readonly List<Color32> Colors = new List<Color32>();
		/// <summary>TEXCOORD1: x sway (moves with the whole plant), y flutter (trembles on its own). Zero for rocks.</summary>
		public readonly List<Vector2> Wind = new List<Vector2>();
		public readonly List<List<int>> Submeshes = new List<List<int>>();

		public MeshBuilder(int submeshCount = 1)
		{
			for (int i = 0; i < Mathf.Max(1, submeshCount); i++)
			{
				Submeshes.Add(new List<int>());
			}
		}

		public int VertexCount => Positions.Count;

		public int TriangleCount
		{
			get
			{
				int n = 0;
				foreach (List<int> s in Submeshes)
				{
					n += s.Count / 3;
				}
				return n;
			}
		}

		/// <summary>Adds a vertex; the normal may be zero and filled by <see cref="RecalculateNormals"/>.</summary>
		public int AddVertex(Vector3 position, Vector3 normal, Vector2 uv, Color32 color, Vector2 wind = default)
		{
			Positions.Add(position);
			Normals.Add(normal);
			UVs.Add(uv);
			Colors.Add(color);
			Wind.Add(wind);
			Tangents.Add(Vector4.zero);
			return Positions.Count - 1;
		}

		/// <summary>A triangle whose front faces <paramref name="facing"/>; the index order is chosen to make it so.</summary>
		public void AddTriangle(int submesh, int a, int b, int c, Vector3 facing)
		{
			Vector3 n = Vector3.Cross(Positions[b] - Positions[a], Positions[c] - Positions[a]);
			List<int> list = Submeshes[submesh];
			if (Vector3.Dot(n, facing) >= 0f)
			{
				list.Add(a); list.Add(b); list.Add(c);
			}
			else
			{
				list.Add(a); list.Add(c); list.Add(b);
			}
		}

		/// <summary>A quad a-b-c-d (in order round its edge) whose front faces <paramref name="facing"/>.</summary>
		public void AddQuad(int submesh, int a, int b, int c, int d, Vector3 facing)
		{
			AddTriangle(submesh, a, b, c, facing);
			AddTriangle(submesh, a, c, d, facing);
		}

		/// <summary>Appends another builder's geometry, offset into this one's submeshes.</summary>
		public void Append(MeshBuilder other, int submeshOffset = 0)
		{
			int baseIndex = Positions.Count;
			Positions.AddRange(other.Positions);
			Normals.AddRange(other.Normals);
			Tangents.AddRange(other.Tangents);
			UVs.AddRange(other.UVs);
			Colors.AddRange(other.Colors);
			Wind.AddRange(other.Wind);
			for (int s = 0; s < other.Submeshes.Count; s++)
			{
				int target = Mathf.Min(Submeshes.Count - 1, s + submeshOffset);
				foreach (int i in other.Submeshes[s])
				{
					Submeshes[target].Add(i + baseIndex);
				}
			}
		}

		/// <summary>Transforms positions and directions of vertices from <paramref name="first"/> on.</summary>
		public void Transform(Matrix4x4 matrix, int first = 0)
		{
			Matrix4x4 normalMatrix = matrix.inverse.transpose;
			for (int i = first; i < Positions.Count; i++)
			{
				Positions[i] = matrix.MultiplyPoint3x4(Positions[i]);
				Vector3 n = normalMatrix.MultiplyVector(Normals[i]);
				Normals[i] = n.sqrMagnitude > 1e-12f ? n.normalized : n;
			}
		}

		private static (long, long, long) PositionKey(Vector3 p)
		{
			// 0.1 mm cells, a full long per axis: a 21-bit packing wrapped at ±104 m, which a 200 m
			// iceberg with a 157 m keel crosses, merging the normals of unrelated vertices.
			return ((long)Mathf.Round(p.x * 10000f), (long)Mathf.Round(p.y * 10000f), (long)Mathf.Round(p.z * 10000f));
		}

		/// <summary>
		/// Area-weighted face normals accumulated per vertex — per welded position when
		/// <paramref name="smoothAcrossSeams"/>, so split seam vertices shade as one.
		/// </summary>
		/// <param name="onlyMissing">Leave vertices that already have a normal alone (analytic normals win).</param>
		public void RecalculateNormals(bool smoothAcrossSeams = true, bool onlyMissing = false)
		{
			var accumulated = new Vector3[Positions.Count];
			Dictionary<(long, long, long), Vector3> byPosition = smoothAcrossSeams ? new Dictionary<(long, long, long), Vector3>() : null;
			foreach (List<int> list in Submeshes)
			{
				for (int t = 0; t + 2 < list.Count; t += 3)
				{
					int a = list[t], b = list[t + 1], c = list[t + 2];
					// Unnormalised: its length is twice the area, which is the weight wanted.
					Vector3 n = Vector3.Cross(Positions[b] - Positions[a], Positions[c] - Positions[a]);
					accumulated[a] += n;
					accumulated[b] += n;
					accumulated[c] += n;
				}
			}
			if (byPosition != null)
			{
				for (int i = 0; i < Positions.Count; i++)
				{
					(long, long, long) key = PositionKey(Positions[i]);
					byPosition.TryGetValue(key, out Vector3 sum);
					byPosition[key] = sum + accumulated[i];
				}
			}
			for (int i = 0; i < Positions.Count; i++)
			{
				if (onlyMissing && Normals[i].sqrMagnitude > 1e-8f)
				{
					continue;
				}
				Vector3 n = byPosition != null ? byPosition[PositionKey(Positions[i])] : accumulated[i];
				Normals[i] = n.sqrMagnitude > 1e-20f ? n.normalized : Vector3.up;
			}
		}

		/// <summary>Per-vertex tangents from UV derivatives, orthogonalised against the normal, handedness in w.</summary>
		public void RecalculateTangents()
		{
			int count = Positions.Count;
			var tan = new Vector3[count];
			var bit = new Vector3[count];
			foreach (List<int> list in Submeshes)
			{
				for (int t = 0; t + 2 < list.Count; t += 3)
				{
					int a = list[t], b = list[t + 1], c = list[t + 2];
					Vector3 e1 = Positions[b] - Positions[a], e2 = Positions[c] - Positions[a];
					Vector2 d1 = UVs[b] - UVs[a], d2 = UVs[c] - UVs[a];
					float det = d1.x * d2.y - d2.x * d1.y;
					if (Mathf.Abs(det) < 1e-12f)
					{
						continue;
					}
					float r = 1f / det;
					Vector3 sdir = (e1 * d2.y - e2 * d1.y) * r;
					Vector3 tdir = (e2 * d1.x - e1 * d2.x) * r;
					tan[a] += sdir; tan[b] += sdir; tan[c] += sdir;
					bit[a] += tdir; bit[b] += tdir; bit[c] += tdir;
				}
			}
			for (int i = 0; i < count; i++)
			{
				Vector3 n = Normals[i];
				Vector3 t = tan[i] - n * Vector3.Dot(n, tan[i]);
				if (t.sqrMagnitude < 1e-12f)
				{
					// No UV gradient here: any direction perpendicular to the normal will do.
					t = Vector3.Cross(n, Mathf.Abs(n.y) < 0.99f ? Vector3.up : Vector3.right);
				}
				t.Normalize();
				float w = Vector3.Dot(Vector3.Cross(n, t), bit[i]) < 0f ? -1f : 1f;
				Tangents[i] = new Vector4(t.x, t.y, t.z, w);
			}
		}

		public Bounds Bounds
		{
			get
			{
				if (Positions.Count == 0)
				{
					return new Bounds();
				}
				Vector3 min = Positions[0], max = Positions[0];
				foreach (Vector3 p in Positions)
				{
					min = Vector3.Min(min, p);
					max = Vector3.Max(max, p);
				}
				var b = new Bounds();
				b.SetMinMax(min, max);
				return b;
			}
		}

		/// <summary>What is wrong with the mesh, one line each; empty when it is sound.</summary>
		/// <param name="requireOutwardWinding">
		/// Also check every face agrees with its vertices' normals. True for closed shapes; false
		/// for foliage whose normals are deliberately bent toward the crown.
		/// </param>
		public List<string> Validate(bool requireOutwardWinding)
		{
			var problems = new List<string>();
			for (int i = 0; i < Positions.Count; i++)
			{
				Vector3 p = Positions[i], n = Normals[i];
				if (float.IsNaN(p.x) || float.IsNaN(p.y) || float.IsNaN(p.z) || float.IsInfinity(p.x) || float.IsInfinity(p.y) || float.IsInfinity(p.z))
				{
					problems.Add($"vertex {i} position is not finite");
				}
				if (float.IsNaN(n.x) || Mathf.Abs(n.magnitude - 1f) > 0.01f)
				{
					problems.Add($"vertex {i} normal is not unit length ({n})");
				}
				Vector4 t = Tangents[i];
				if (Mathf.Abs(Mathf.Abs(t.w) - 1f) > 1e-4f)
				{
					problems.Add($"vertex {i} tangent handedness is {t.w}");
				}
			}
			for (int s = 0; s < Submeshes.Count; s++)
			{
				List<int> list = Submeshes[s];
				if (list.Count % 3 != 0)
				{
					problems.Add($"submesh {s} index count {list.Count} is not a multiple of 3");
					continue;
				}
				for (int t = 0; t < list.Count; t += 3)
				{
					int a = list[t], b = list[t + 1], c = list[t + 2];
					if (a < 0 || b < 0 || c < 0 || a >= Positions.Count || b >= Positions.Count || c >= Positions.Count)
					{
						problems.Add($"submesh {s} triangle {t / 3} indexes out of range");
						continue;
					}
					if (a == b || b == c || a == c)
					{
						problems.Add($"submesh {s} triangle {t / 3} repeats a vertex");
						continue;
					}
					Vector3 face = Vector3.Cross(Positions[b] - Positions[a], Positions[c] - Positions[a]);
					if (face.sqrMagnitude < 1e-14f)
					{
						problems.Add($"submesh {s} triangle {t / 3} has no area");
						continue;
					}
					if (requireOutwardWinding)
					{
						Vector3 vn = Normals[a] + Normals[b] + Normals[c];
						if (Vector3.Dot(face, vn) <= 0f)
						{
							problems.Add($"submesh {s} triangle {t / 3} faces against its normals");
						}
					}
				}
			}
			return problems;
		}

		/// <summary>The geometry as a Unity mesh, written through the MeshData API.</summary>
		public Mesh ToMesh(string name)
		{
			int vertexCount = Positions.Count;
			int indexCount = 0;
			foreach (List<int> s in Submeshes)
			{
				indexCount += s.Count;
			}
			bool wide = vertexCount > 65535;

			Mesh.MeshDataArray dataArray = Mesh.AllocateWritableMeshData(1);
			Mesh.MeshData data = dataArray[0];
			data.SetVertexBufferParams(vertexCount,
				new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
				new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
				new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.Float32, 4),
				new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
				new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
				new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 2));
			NativeArray<Vertex> vertices = data.GetVertexData<Vertex>();
			for (int i = 0; i < vertexCount; i++)
			{
				vertices[i] = new Vertex
				{
					Position = Positions[i],
					Normal = Normals[i],
					Tangent = Tangents[i],
					Color = Colors[i],
					UV = UVs[i],
					Wind = Wind[i],
				};
			}

			data.SetIndexBufferParams(indexCount, wide ? IndexFormat.UInt32 : IndexFormat.UInt16);
			int start = 0;
			if (wide)
			{
				NativeArray<uint> indices = data.GetIndexData<uint>();
				foreach (List<int> s in Submeshes)
				{
					for (int i = 0; i < s.Count; i++)
					{
						indices[start + i] = (uint)s[i];
					}
					start += s.Count;
				}
			}
			else
			{
				NativeArray<ushort> indices = data.GetIndexData<ushort>();
				foreach (List<int> s in Submeshes)
				{
					for (int i = 0; i < s.Count; i++)
					{
						indices[start + i] = (ushort)s[i];
					}
					start += s.Count;
				}
			}

			Bounds bounds = Bounds;
			data.subMeshCount = Submeshes.Count;
			start = 0;
			const MeshUpdateFlags quiet = MeshUpdateFlags.DontRecalculateBounds | MeshUpdateFlags.DontValidateIndices;
			for (int s = 0; s < Submeshes.Count; s++)
			{
				data.SetSubMesh(s, new SubMeshDescriptor(start, Submeshes[s].Count) { bounds = bounds, vertexCount = vertexCount }, quiet);
				start += Submeshes[s].Count;
			}

			var mesh = new Mesh { name = name };
			Mesh.ApplyAndDisposeWritableMeshData(dataArray, mesh, quiet);
			mesh.bounds = bounds;
			return mesh;
		}

		[System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
		private struct Vertex
		{
			public Vector3 Position;
			public Vector3 Normal;
			public Vector4 Tangent;
			public Color32 Color;
			public Vector2 UV;
			public Vector2 Wind;
		}
	}
}
#endif
