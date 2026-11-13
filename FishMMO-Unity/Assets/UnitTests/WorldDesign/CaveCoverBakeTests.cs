using System;
using System.IO;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// The cave cover's bake (2026-11-03, CaveCoverBake): players logged "index buffer is null" from CaveCoverSystem.TakeOn
	/// for every stored piece, because an unreadable mesh's index buffer cannot be re-targeted raw where there is no CPU copy,
	/// so no cover ever grew in a player. The cut now bakes what FishCaveCover.compute reads; these pin that the bake is that
	/// data byte for byte (so the cover lands exactly as from the mesh), that it reads an unreadable piece, and the wiring.
	/// </summary>
	[TestFixture]
	public class CaveCoverBakeTests
	{
		/// <summary>A piece laid out as the mesher writes one: position, normal, tangent, colour, two UVs and the habitat's TEXCOORD3, 16-bit indices from a non-zero start.</summary>
		private static Mesh Piece(int vertices = 40)
		{
			var mesh = new Mesh { name = "Cave 7 P0" };
			mesh.SetVertexBufferParams(vertices,
				new VertexAttributeDescriptor(VertexAttribute.Position, VertexAttributeFormat.Float32, 3),
				new VertexAttributeDescriptor(VertexAttribute.Normal, VertexAttributeFormat.Float32, 3),
				new VertexAttributeDescriptor(VertexAttribute.Tangent, VertexAttributeFormat.Float32, 4),
				new VertexAttributeDescriptor(VertexAttribute.Color, VertexAttributeFormat.UNorm8, 4),
				new VertexAttributeDescriptor(VertexAttribute.TexCoord0, VertexAttributeFormat.Float32, 2),
				new VertexAttributeDescriptor(VertexAttribute.TexCoord1, VertexAttributeFormat.Float32, 2),
				new VertexAttributeDescriptor(VertexAttribute.TexCoord3, VertexAttributeFormat.Float32, 2));
			var positions = new Vector3[vertices];
			var normals = new Vector3[vertices];
			var colours = new Color32[vertices];
			var uv3 = new Vector2[vertices];
			var rng = new System.Random(5);
			for (int i = 0; i < vertices; i++)
			{
				positions[i] = new Vector3((float)rng.NextDouble() * 32f, (float)rng.NextDouble() * 32f - 7.3f, (float)rng.NextDouble() * 32f);
				normals[i] = new Vector3((float)rng.NextDouble() - 0.5f, 1f, (float)rng.NextDouble() - 0.5f).normalized;
				colours[i] = new Color32((byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256), (byte)rng.Next(256));
				uv3[i] = new Vector2((float)rng.NextDouble(), (float)rng.NextDouble());
			}
			mesh.SetVertices(positions);
			mesh.SetNormals(normals);
			mesh.SetColors(colours);
			mesh.SetUVs(3, uv3);
			var indices = new int[(vertices - 2) * 3];
			for (int t = 0; t < vertices - 2; t++)
			{
				indices[t * 3] = 0;
				indices[t * 3 + 1] = t + 1;
				indices[t * 3 + 2] = t + 2;
			}
			mesh.SetIndexBufferParams(indices.Length + 5, IndexFormat.UInt16);
			var padded = new ushort[indices.Length + 5];
			for (int i = 0; i < indices.Length; i++)
			{
				padded[i + 5] = (ushort)indices[i];
			}
			mesh.SetIndexBufferData(padded, 0, 0, padded.Length);
			mesh.subMeshCount = 1;
			mesh.SetSubMesh(0, new SubMeshDescriptor(5, indices.Length));
			mesh.RecalculateBounds();
			return mesh;
		}

		private static float F(byte[] b, int at) => BitConverter.ToSingle(b, at);

		[Test]
		public void BakedPiece_IsWhatTheComputeReads_ByteForByte()
		{
			Mesh mesh = Piece();
			try
			{
				CaveCoverBake.Piece p = CaveShaper.CoverPieceOf(mesh);
				Assert.That(p, Is.Not.Null);
				Assert.That(p.Stride, Is.EqualTo(12 + 12 + 4 + 8), "position, normal, colour and TEXCOORD3 only: 36 of the 68 bytes");
				Assert.That(p.PositionFormat, Is.EqualTo((uint)VertexAttributeFormat.Float32 | 3u << 8));
				Assert.That(p.ColourFormat, Is.EqualTo((uint)VertexAttributeFormat.UNorm8 | 4u << 8));
				Assert.That(p.Uv3Format, Is.EqualTo((uint)VertexAttributeFormat.Float32 | 2u << 8));
				Vector3[] positions = mesh.vertices;
				Vector3[] normals = mesh.normals;
				Color32[] colours = mesh.colors32;
				var uv3 = new System.Collections.Generic.List<Vector2>();
				mesh.GetUVs(3, uv3);
				for (int v = 0; v < mesh.vertexCount; v++)
				{
					int at = v * p.Stride;
					Assert.That(new Vector3(F(p.Vertices, at + p.PositionOffset), F(p.Vertices, at + p.PositionOffset + 4), F(p.Vertices, at + p.PositionOffset + 8)), Is.EqualTo(positions[v]), "the triangle keys hash the positions: bit for bit");
					Assert.That(new Vector3(F(p.Vertices, at + p.NormalOffset), F(p.Vertices, at + p.NormalOffset + 4), F(p.Vertices, at + p.NormalOffset + 8)), Is.EqualTo(normals[v]));
					Assert.That(new Color32(p.Vertices[at + p.ColourOffset], p.Vertices[at + p.ColourOffset + 1], p.Vertices[at + p.ColourOffset + 2], p.Vertices[at + p.ColourOffset + 3]), Is.EqualTo(colours[v]));
					Assert.That(new Vector2(F(p.Vertices, at + p.Uv3Offset), F(p.Vertices, at + p.Uv3Offset + 4)), Is.EqualTo(uv3[v]));
				}
				int[] triangles = mesh.GetIndices(0);
				Assert.That(p.IndexCount, Is.EqualTo(triangles.Length));
				Assert.That(p.Wide, Is.False);
				Assert.That(p.Indices.Length % 4, Is.EqualTo(0), "a raw buffer is whole words");
				for (int i = 0; i < triangles.Length; i++)
				{
					Assert.That((int)BitConverter.ToUInt16(p.Indices, i * 2), Is.EqualTo(triangles[i]), "the submesh's own range, from its start");
				}
				Assert.That(p.Matches(mesh), Is.True);
			}
			finally
			{
				Object.DestroyImmediate(mesh);
			}
		}

		[Test]
		public void AnUnreadablePiece_IsBakedInTheEditor()
		{
			Mesh mesh = Piece();
			try
			{
				Vector3[] positions = mesh.vertices;
				using (var so = new SerializedObject(mesh))
				{
					so.FindProperty("m_IsReadable").boolValue = false;
					so.ApplyModifiedPropertiesWithoutUndo();
				}
				Assert.That(mesh.isReadable, Is.False, "stored as the cut stores its pieces");
				CaveCoverBake.Piece p = CaveShaper.CoverPieceOf(mesh);
				Assert.That(p, Is.Not.Null);
				Assert.That(F(p.Vertices, (mesh.vertexCount - 1) * p.Stride + p.PositionOffset), Is.EqualTo(positions[mesh.vertexCount - 1].x));
			}
			finally
			{
				Object.DestroyImmediate(mesh);
			}
		}

		[Test]
		public void TheRow_PointsTheComputeAtTheBakedBytes()
		{
			Mesh mesh = Piece();
			try
			{
				CaveCoverBake.Piece p = CaveShaper.CoverPieceOf(mesh);
				var choice = CaveCoverSite.Default;
				CaveCoverRenderer.PieceData row = CaveCoverSystem.RowOf(3, in choice, p, Matrix4x4.Translate(new Vector3(1f, 2f, 3f)));
				Assert.That(row.VertexStride, Is.EqualTo((uint)p.Stride));
				Assert.That(row.FirstIndex, Is.EqualTo(0u), "its own index buffer starts at the submesh");
				Assert.That(row.Triangles, Is.EqualTo((uint)(p.IndexCount / 3)));
				Assert.That(row.PositionOffset, Is.EqualTo((uint)p.PositionOffset));
				Assert.That(row.Uv3Offset, Is.EqualTo((uint)p.Uv3Offset));
				Assert.That(row.CaveSlot, Is.EqualTo(3u));
				Assert.That(row.TypeMask, Is.EqualTo(choice.Types));
				Assert.That(row.Row0.w, Is.EqualTo(1f));
			}
			finally
			{
				Object.DestroyImmediate(mesh);
			}
		}

		[Test]
		public void ABake_NoLongerMatchesAPieceWrittenOver()
		{
			Mesh mesh = Piece(40);
			try
			{
				var bake = ScriptableObject.CreateInstance<CaveCoverBake>();
				bake.Pieces.Add(CaveShaper.CoverPieceOf(mesh));
				Assert.That(bake.For(mesh), Is.Not.Null);
				Mesh other = Piece(30);
				mesh.Clear();
				mesh.SetVertices(other.vertices);
				Object.DestroyImmediate(other);
				bake.Refreshed();
				Assert.That(bake.For(mesh), Is.Null, "a re-cut writes pieces over in place: a stale bake is not read");
				Object.DestroyImmediate(bake);
			}
			finally
			{
				Object.DestroyImmediate(mesh);
			}
		}

		[Test]
		public void Wiring_TheCutBakesAndThePlayerNeverRetargetsAnUnreadablePiece()
		{
			string shaper = ProjectFiles.ReadAllText("Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/PointsOfInterest/Shapers/CaveShaper.cs");
			StringAssert.Contains("CaveCoverBake cover = CoverBakeFor(assetPath, site.Id, meshes, rebuilt, live);", shaper);
			string species = ProjectFiles.ReadAllText("Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World/PointsOfInterest/Shapers/CaveCoverSpecies.cs");
			StringAssert.Contains("site.SetBake(bake);", species);
			string system = ProjectFiles.ReadAllText("Assets/Scripts/Client/World/Caves/Cover/CaveCoverSystem.cs");
			int bake = system.IndexOf("cave.Bake.For(mesh)", StringComparison.Ordinal);
			int guard = system.IndexOf("if (!mesh.isReadable && !Application.isEditor)", StringComparison.Ordinal);
			int retarget = system.IndexOf("mesh.indexBufferTarget |= GraphicsBuffer.Target.Raw;", StringComparison.Ordinal);
			Assert.That(bake, Is.GreaterThan(0));
			Assert.That(guard, Is.GreaterThan(bake), "the bake first");
			Assert.That(retarget, Is.GreaterThan(guard), "an unreadable piece in a player is never re-targeted");
		}
	}
}
