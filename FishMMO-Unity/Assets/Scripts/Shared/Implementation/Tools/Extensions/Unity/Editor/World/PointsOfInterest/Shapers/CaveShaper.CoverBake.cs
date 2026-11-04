#if UNITY_EDITOR
using System.Collections.Generic;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The cave cover's bake (2026-11-03, <see cref="CaveCoverBake"/>): what FishCaveCover.compute reads of each piece, written
	/// beside the pieces as <c>Cave &lt;id&gt; Cover</c>. A player cannot read the stored pieces (they are unreadable), so
	/// without it no cover grew there.
	/// </summary>
	public sealed partial class CaveShaper
	{
		/// <summary>A cave's cover bake's name in the caves asset.</summary>
		public static string CoverBakeName(int siteId) => $"Cave {siteId} Cover";

		/// <summary>The attributes the compute reads, in its piece row's order.</summary>
		private static readonly VertexAttribute[] CoverAttributes = { VertexAttribute.Position, VertexAttribute.Normal, VertexAttribute.Color, VertexAttribute.TexCoord3 };

		/// <summary>
		/// The cave's cover bake: the asset's own, kept when it still matches <paramref name="meshes"/> and they were not
		/// written again (<paramref name="rebuilt"/>), else filled from them; added to the asset when it had none. Bakes of
		/// caves not in <paramref name="live"/> are removed. In memory only when the path is null (tests, renders).
		/// </summary>
		private static CaveCoverBake CoverBakeFor(string assetPath, int siteId, Mesh[] meshes, bool rebuilt, ICollection<int> live)
		{
			if (meshes == null || meshes.Length == 0)
			{
				return null;
			}
			CaveCoverBake bake = null;
			if (assetPath != null)
			{
				bool removed = false;
				foreach (Object asset in AssetDatabase.LoadAllAssetsAtPath(assetPath))
				{
					if (!(asset is CaveCoverBake found) || !CaveIdOf(found.name, out int id))
					{
						continue;
					}
					if (id == siteId)
					{
						bake = found;
					}
					else if (live != null && !live.Contains(id))
					{
						AssetDatabase.RemoveObjectFromAsset(found);
						Object.DestroyImmediate(found, true);
						removed = true;
					}
				}
				if (removed)
				{
					unsaved.Add(assetPath);
				}
			}
			if (bake != null && !rebuilt && CoverBakeMatches(bake, meshes))
			{
				return bake;
			}
			bool fresh = bake == null;
			if (fresh)
			{
				bake = ScriptableObject.CreateInstance<CaveCoverBake>();
				bake.name = CoverBakeName(siteId);
			}
			bake.Pieces.Clear();
			foreach (Mesh mesh in meshes)
			{
				CaveCoverBake.Piece piece = CoverPieceOf(mesh);
				if (piece != null)
				{
					bake.Pieces.Add(piece);
				}
			}
			bake.Refreshed();
			if (assetPath != null)
			{
				if (fresh)
				{
					AssetDatabase.AddObjectToAsset(bake, assetPath);
				}
				EditorUtility.SetDirty(bake);
				unsaved.Add(assetPath);
			}
			return bake;
		}

		/// <summary>Whether a bake still describes every piece (the same meshes, their vertex counts unchanged).</summary>
		private static bool CoverBakeMatches(CaveCoverBake bake, Mesh[] meshes)
		{
			if (bake.Pieces.Count != meshes.Length)
			{
				return false;
			}
			foreach (Mesh mesh in meshes)
			{
				if (bake.For(mesh) == null)
				{
					return false;
				}
			}
			return true;
		}

		/// <summary>
		/// One piece as the cover reads it: position, normal, colour and TEXCOORD3 copied byte for byte in their own formats
		/// (each 4-byte aligned, from whichever stream holds it), and its first submesh's indices. Read through
		/// <see cref="MeshUtility.AcquireReadOnlyMeshData(Mesh)"/>, which the editor allows on an unreadable mesh. Null for a
		/// mesh with no positions, normals or triangles.
		/// </summary>
		public static CaveCoverBake.Piece CoverPieceOf(Mesh mesh)
		{
			if (mesh == null || mesh.vertexCount == 0 || mesh.subMeshCount < 1
				|| !mesh.HasVertexAttribute(VertexAttribute.Position) || !mesh.HasVertexAttribute(VertexAttribute.Normal))
			{
				return null;
			}
			using (Mesh.MeshDataArray all = MeshUtility.AcquireReadOnlyMeshData(mesh))
			{
				Mesh.MeshData data = all[0];
				SubMeshDescriptor sub = data.GetSubMesh(0);
				if (sub.indexCount < 3)
				{
					return null;
				}
				var piece = new CaveCoverBake.Piece { Mesh = mesh, VertexCount = data.vertexCount, IndexCount = sub.indexCount, BaseVertex = Mathf.Max(0, sub.baseVertex), Wide = data.indexFormat == IndexFormat.UInt32 };
				var offsets = new int[CoverAttributes.Length];
				var sizes = new int[CoverAttributes.Length];
				var formats = new uint[CoverAttributes.Length];
				int stride = 0;
				for (int a = 0; a < CoverAttributes.Length; a++)
				{
					offsets[a] = -1;
					if (!data.HasVertexAttribute(CoverAttributes[a]))
					{
						continue;
					}
					VertexAttributeFormat format = data.GetVertexAttributeFormat(CoverAttributes[a]);
					int dimension = data.GetVertexAttributeDimension(CoverAttributes[a]);
					sizes[a] = FormatSize(format) * dimension;
					formats[a] = (uint)format | ((uint)dimension << 8);
					offsets[a] = stride;
					stride += (sizes[a] + 3) & ~3;
				}
				piece.Stride = stride;
				piece.Vertices = new byte[data.vertexCount * stride];
				for (int a = 0; a < CoverAttributes.Length; a++)
				{
					if (offsets[a] < 0)
					{
						continue;
					}
					int stream = data.GetVertexAttributeStream(CoverAttributes[a]);
					int from = data.GetVertexAttributeOffset(CoverAttributes[a]);
					int sourceStride = data.GetVertexBufferStride(stream);
					NativeArray<byte> source = data.GetVertexData<byte>(stream);
					for (int v = 0; v < data.vertexCount; v++)
					{
						NativeArray<byte>.Copy(source, v * sourceStride + from, piece.Vertices, v * stride + offsets[a], sizes[a]);
					}
				}
				piece.PositionOffset = offsets[0];
				piece.NormalOffset = offsets[1];
				piece.ColourOffset = offsets[2];
				piece.Uv3Offset = offsets[3];
				piece.PositionFormat = formats[0];
				piece.NormalFormat = formats[1];
				piece.ColourFormat = formats[2];
				piece.Uv3Format = formats[3];

				int width = piece.Wide ? 4 : 2;
				int bytes = sub.indexCount * width;
				piece.Indices = new byte[(bytes + 3) & ~3];
				NativeArray<byte>.Copy(data.GetIndexData<byte>(), sub.indexStart * width, piece.Indices, 0, bytes);
				return piece;
			}
		}

		/// <summary>The bytes of one component of a vertex attribute format.</summary>
		private static int FormatSize(VertexAttributeFormat format)
		{
			switch (format)
			{
				case VertexAttributeFormat.Float32:
				case VertexAttributeFormat.UInt32:
				case VertexAttributeFormat.SInt32:
					return 4;
				case VertexAttributeFormat.Float16:
				case VertexAttributeFormat.UNorm16:
				case VertexAttributeFormat.SNorm16:
				case VertexAttributeFormat.UInt16:
				case VertexAttributeFormat.SInt16:
					return 2;
				default:
					return 1;
			}
		}
	}
}
#endif
