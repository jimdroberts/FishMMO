using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// What the run-time cave cover reads of a cave's rock (2026-11-03), baked at the cut beside its pieces in the scene's caves
	/// asset and named on its <see cref="CaveCoverSite"/>: per piece, its vertices cut down to the four attributes
	/// FishCaveCover.compute reads (position, normal, colour, TEXCOORD3, each in the piece's own format, so the cover lands bit
	/// for bit where the mesh would put it) and its first submesh's indices.
	/// </summary>
	/// <remarks>
	/// The stored pieces are unreadable (CaveShaper): a player uploads them straight to the GPU with no CPU copy, and there
	/// re-targeting a piece's index buffer raw for the compute fails ("index buffer is null"), so no cover ever grew in a
	/// player. The cover reads this instead, in buffers of its own. About half the render vertex (36 of 68 bytes); the pieces
	/// stay unreadable, and a server never loads it (the Visual is client-only).
	/// </remarks>
	public sealed class CaveCoverBake : ScriptableObject
	{
		/// <summary>One piece as the compute reads it. Offsets are -1 for an attribute the piece has not; formats are FishCaveCover.compute's (VertexAttributeFormat | dimension &lt;&lt; 8).</summary>
		[Serializable]
		public sealed class Piece
		{
			public Mesh Mesh;
			/// <summary>VertexCount × Stride bytes, each attribute 4-byte aligned.</summary>
			public byte[] Vertices;
			/// <summary>The first submesh's indices in the mesh's index format, padded to a multiple of 4 bytes.</summary>
			public byte[] Indices;
			public int VertexCount, IndexCount, Stride, BaseVertex;
			public bool Wide;
			public int PositionOffset = -1, NormalOffset = -1, ColourOffset = -1, Uv3Offset = -1;
			public uint PositionFormat, NormalFormat, ColourFormat, Uv3Format;

			/// <summary>Whether it still describes <paramref name="mesh"/> (a re-cut writes the pieces over in place).</summary>
			public bool Matches(Mesh mesh) => mesh != null && Mesh == mesh && VertexCount == mesh.vertexCount && Vertices != null && Indices != null
				&& PositionOffset >= 0 && NormalOffset >= 0 && Vertices.Length == VertexCount * Stride && Indices.Length >= IndexCount * (Wide ? 4 : 2);
		}

		public List<Piece> Pieces = new List<Piece>();

		[NonSerialized] private Dictionary<Mesh, Piece> byMesh;

		/// <summary>The baked piece of a mesh, or null when it has none or no longer matches it.</summary>
		public Piece For(Mesh mesh)
		{
			if (mesh == null)
			{
				return null;
			}
			if (byMesh == null)
			{
				byMesh = new Dictionary<Mesh, Piece>();
				foreach (Piece piece in Pieces)
				{
					if (piece != null && piece.Mesh != null)
					{
						byMesh[piece.Mesh] = piece;
					}
				}
			}
			return byMesh.TryGetValue(mesh, out Piece found) && found.Matches(mesh) ? found : null;
		}

		/// <summary>Forgets the lookup (the cut refilled <see cref="Pieces"/>).</summary>
		public void Refreshed() => byMesh = null;
	}
}
