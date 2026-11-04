using System;
using System.Collections.Generic;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishMMO.Client
{
	/// <summary>Static mesh shadow geometry, grouped spatially so empty space in a large submesh does not keep its draw.</summary>
	public sealed class TerrainShadowHierarchy
	{
		private struct Node { public Bounds Bounds; public int Left, Right; }
		private sealed class CentreComparer : IComparer<Bounds>
		{
			private readonly int axis;
			public CentreComparer(int axis) { this.axis = axis; }
			public int Compare(Bounds a, Bounds b) => a.center[axis].CompareTo(b.center[axis]);
		}
		private static readonly IComparer<Bounds>[] comparers = { new CentreComparer(0), new CentreComparer(1), new CentreComparer(2) };
		private readonly Node[] nodes;
		public int NodeCount => nodes.Length;

		public TerrainShadowHierarchy(Bounds[] triangles)
		{
			if (triangles.Length == 0) { nodes = Array.Empty<Node>(); return; }
			var work = (Bounds[])triangles.Clone();
			var built = new List<Node>();
			Build(work, 0, work.Length, built);
			nodes = built.ToArray();
		}

		private static int Build(Bounds[] triangles, int first, int count, List<Node> nodes)
		{
			Bounds bounds = triangles[first];
			for (int i = 1; i < count; i++) bounds.Encapsulate(triangles[first + i]);
			int at = nodes.Count;
			var node = new Node { Bounds = bounds, Left = -1, Right = -1 };
			nodes.Add(node);
			if (count > 4)
			{
				Vector3 size = bounds.size;
				int axis = size.x >= size.y && size.x >= size.z ? 0 : size.y >= size.z ? 1 : 2;
				Array.Sort(triangles, first, count, comparers[axis]);
				int half = count / 2;
				node.Left = Build(triangles, first, half, nodes);
				node.Right = Build(triangles, first + half, count - half, nodes);
				nodes[at] = node;
			}
			return at;
		}

		public static TerrainShadowHierarchy FromMesh(Mesh mesh, int submesh)
		{
			if (mesh == null || !mesh.isReadable || mesh.GetTopology(submesh) != MeshTopology.Triangles) return null;
			using var data = Mesh.AcquireReadOnlyMeshData(mesh);
			using var vertices = new NativeArray<Vector3>(mesh.vertexCount, Allocator.Temp, NativeArrayOptions.UninitializedMemory);
			data[0].GetVertices(vertices);
			var part = data[0].GetSubMesh(submesh);
			var triangles = new Bounds[part.indexCount / 3];
			var wide = mesh.indexFormat == IndexFormat.UInt32 ? data[0].GetIndexData<uint>() : default;
			var narrow = mesh.indexFormat == IndexFormat.UInt16 ? data[0].GetIndexData<ushort>() : default;
			for (int i = 0; i < triangles.Length; i++)
				for (int corner = 0; corner < 3; corner++)
				{
					int at = part.indexStart + 3 * i + corner;
					int index = (mesh.indexFormat == IndexFormat.UInt32 ? (int)wide[at] : narrow[at]) + part.baseVertex;
					if (corner == 0) triangles[i] = new Bounds(vertices[index], Vector3.zero); else triangles[i].Encapsulate(vertices[index]);
				}
			return new TerrainShadowHierarchy(triangles);
		}

		public ushort SplitMask(in FishInstance instance, NativeArray<Plane> planes, NativeArray<CullingSplit> splits, ushort wanted, float padding)
		{
			return nodes.Length == 0 ? (ushort)0 : Visit(0, in instance, planes, splits, wanted, padding);
		}

		private ushort Visit(int index, in FishInstance instance, NativeArray<Plane> planes, NativeArray<CullingSplit> splits, ushort wanted, float padding)
		{
			if (wanted == 0) return 0;
			Node node = nodes[index];
			Bounds world = TerrainShadowCullingMath.WorldBounds(instance, node.Bounds);
			world.Expand(2f * padding); // world-space allowance for the known static shadow pass's normal/depth bias
			ushort mask = TerrainShadowCullingMath.BoundsMask(world, planes, splits, wanted);
			if (mask == 0 || node.Left < 0) return mask;
			ushort left = Visit(node.Left, in instance, planes, splits, mask, padding);
			return (ushort)(left | Visit(node.Right, in instance, planes, splits, (ushort)(mask & ~left), padding));
		}
	}
}
