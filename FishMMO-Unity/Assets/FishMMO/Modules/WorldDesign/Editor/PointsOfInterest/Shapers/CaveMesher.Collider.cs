#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// A piece's collider as its own mesh (2026-10-20): the piece's own triangles, nothing thinner, on positions alone.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Why not the render mesh.</b> A render vertex is 68 bytes (position, normal, tangent, colour, two UVs and the
	/// habitat's UV), and the piece copies each net vertex once per projection axis its triangles use. A collider needs
	/// positions only, each once. A server loads a cave's meshes through its colliders alone (the Visual is client-only and
	/// stripped), so it held the whole render layout for nothing; measured on Moron 2 (seven caves, 811k triangles): 42 MB of
	/// render mesh in memory against 10 MB welded. The render pieces are then stored unreadable (CaveShaper), so a client
	/// drops their CPU copy too.
	/// </para>
	/// <para>
	/// <b>Why not thinner.</b> Measured the same day: query cost against the caves is flat in their triangle count (rays,
	/// capsule casts and overlaps all about 1 µs, the same at half the triangles); the colliders are cooked when the player
	/// is built (bakeCollisionMeshes), in 0.2 s a scene; and the cooked memory a half-thinned collider saves (24 → 14 MB on
	/// Moron 2) comes with a surface off the drawn rock by more than the rock's own thinning, which the camera's sphere cast
	/// would follow behind a wall. The same triangles keep every floor, step and closed wall exactly as drawn.
	/// </para>
	/// </remarks>
	public static partial class CaveMesher
	{
		/// <summary>
		/// The collider of a piece: its vertices welded where their positions are exactly equal (the piece's per-axis copies of
		/// one net vertex), first come first numbered, and its triangles in order on them, wound as they were. A triangle whose
		/// corners weld together (no area) is left out; collider cooking would drop it anyway.
		/// </summary>
		public static void ColliderOf(IReadOnlyList<Vector3> positions, IReadOnlyList<int> triangles, List<Vector3> welded, List<int> weldedTriangles)
		{
			welded.Clear();
			weldedTriangles.Clear();
			var index = new Dictionary<Vector3, int>(positions.Count);
			var map = new int[positions.Count];
			for (int i = 0; i < positions.Count; i++)
			{
				Vector3 p = positions[i];
				if (!index.TryGetValue(p, out int id))
				{
					index[p] = id = welded.Count;
					welded.Add(p);
				}
				map[i] = id;
			}
			for (int t = 0; t + 2 < triangles.Count; t += 3)
			{
				int a = map[triangles[t]], b = map[triangles[t + 1]], c = map[triangles[t + 2]];
				if (a == b || b == c || c == a)
				{
					continue;
				}
				weldedTriangles.Add(a);
				weldedTriangles.Add(b);
				weldedTriangles.Add(c);
			}
		}
	}
}
#endif
