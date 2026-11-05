using System.Collections.Generic;
using FishMMO.Client;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The detail scatter's batched draws (2026-11-04, DetailScatterRenderer.BatchDraws): parts of one material merged into a
	/// pooled mesh, a submesh a part, each command's args reading its part's submesh. The pool must hand every command
	/// exactly its part's triangles, and the draw path must keep each call to a run of commands with work.
	/// </summary>
	public class DetailScatterBatchingTests
	{
		private static Mesh Part(int seed, int quads, IndexFormat format)
		{
			var random = new System.Random(seed);
			var vertices = new List<Vector3>();
			var normals = new List<Vector3>();
			var uvs = new List<Vector2>();
			var triangles = new List<int>();
			for (int q = 0; q < quads; q++)
			{
				int at = vertices.Count;
				for (int k = 0; k < 4; k++)
				{
					vertices.Add(new Vector3((float)random.NextDouble(), (float)random.NextDouble() + q, (float)random.NextDouble()));
					normals.Add(Vector3.up);
					uvs.Add(new Vector2(k & 1, k >> 1));
				}
				triangles.AddRange(new[] { at, at + 1, at + 2, at + 2, at + 1, at + 3 });
			}
			var mesh = new Mesh { indexFormat = format };
			mesh.SetVertices(vertices);
			mesh.SetNormals(normals);
			mesh.SetUVs(0, uvs);
			mesh.SetTriangles(triangles, 0);
			return mesh;
		}

		[Test]
		public void APooledSubmesh_DrawsExactlyItsPartsTriangles()
		{
			var parts = new List<Mesh> { Part(1, 3, IndexFormat.UInt16), Part(2, 17, IndexFormat.UInt16), Part(3, 1, IndexFormat.UInt32), Part(4, 9, IndexFormat.UInt16) };
			Mesh pool = DetailScatterRenderer.PoolMesh(parts);
			try
			{
				Assert.That(pool.subMeshCount, Is.EqualTo(parts.Count), "a submesh a part");
				Vector3[] pooled = pool.vertices;
				// The index buffer as the GPU reads it: raw, base vertices not applied.
				using Mesh.MeshDataArray data = Mesh.AcquireReadOnlyMeshData(pool);
				uint[] indices = data[0].GetIndexData<uint>().ToArray();
				for (int i = 0; i < parts.Count; i++)
				{
					// As the indirect args read it: indexCount from startIndex, each index offset by baseVertex.
					int count = (int)pool.GetIndexCount(i), start = (int)pool.GetIndexStart(i), baseVertex = (int)pool.GetBaseVertex(i);
					int[] own = parts[i].triangles;
					Vector3[] ownVertices = parts[i].vertices;
					Assert.That(count, Is.EqualTo(own.Length), "part " + i + "'s index count");
					for (int k = 0; k < count; k++)
					{
						Vector3 drawn = pooled[(int)indices[start + k] + baseVertex];
						Assert.That(drawn, Is.EqualTo(ownVertices[own[k]]), $"part {i} index {k}");
					}
				}
			}
			finally
			{
				Object.DestroyImmediate(pool);
				foreach (Mesh m in parts) Object.DestroyImmediate(m);
			}
		}

		[Test]
		public void Wiring_BatchedCallsCoverRunsOfCommandsWithWork()
		{
			string code = SourceScanPins.ReadCode("Assets/Scripts/Client/World/Terrain/Scatter/DetailScatterRenderer.cs");
			// Batched only where one call can draw many commands (the instance index carries each command's run start), and not
			// through the batch group, which submits every command itself.
			StringAssert.Contains("batchedState = !brgState && BatchDraws && TerrainGpuRenderer.InstanceIdCarriesBase;", code);
			// A switch flipped at run time re-lays out.
			StringAssert.Contains("batchedState != (!brgState && BatchDraws && TerrainGpuRenderer.InstanceIdCarriesBase)", code);
			StringAssert.Contains("brgState != BrgFor(instanceBuffer != null ? instanceBuffer.count : 0)", code);
			// A call never spans a command whose slot has no work (each would still be a GPU draw).
			Assert.That(SourceScanPins.InOrder(code, "while (c < end && need[commands[c].Slot] > 0)", "Graphics.RenderMeshIndirect(rp, batch.Mesh, argsBuffer, c - first, first);"), Is.Null);
			// The args are rebuilt in the commands' final order, so argument i, slot i and command base i stay one command's.
			Assert.That(SourceScanPins.InOrder(code, "GroupCommands();", "args.Add(ArgsOf(command));", "argSlots.Add((uint)command.Slot);", "commandBases.Add((uint)bases[command.Slot]);"), Is.Null);
		}
	}
}
