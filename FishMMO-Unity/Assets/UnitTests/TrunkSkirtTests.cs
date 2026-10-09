using System.Collections.Generic;
using FishMMO.Client;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The GPU tree path's trunk skirt (TrunkSkirt): a ring hung off a trunk's open bark foot, copied from it and flagged
	/// for the vegetation shader to drop onto the terrain (FishVegetationPasses.hlsl VegSkirtFoot).
	/// </summary>
	public class TrunkSkirtTests
	{
		private const int Sides = 8;
		private const int Ring = Sides + 1;   // the seam's column twice, as PlantParts.Tube builds it

		private readonly List<Object> made = new List<Object>();

		[TearDown]
		public void TearDown()
		{
			foreach (Object o in made)
			{
				Object.DestroyImmediate(o);
			}
			made.Clear();
		}

		/// <summary>An open bark tube from y 0 to 1 (bark v = y) in submesh 0, and an open leaf card at the ground in submesh 1.</summary>
		private Mesh Tree()
		{
			var positions = new List<Vector3>();
			var colours = new List<Color32>();
			var uv = new List<Vector2>();
			var wind = new List<Vector2>();
			var pivots = new List<Vector4>();
			for (int r = 0; r < 2; r++)
			{
				for (int s = 0; s < Ring; s++)
				{
					float a = s * Mathf.PI * 2f / Sides;
					positions.Add(new Vector3(Mathf.Cos(a) * 0.4f, r, Mathf.Sin(a) * 0.4f));
					colours.Add(new Color32(255, 255, 255, 0));
					uv.Add(new Vector2((float)s / Sides, r));
					wind.Add(new Vector2(0.1f * r, 0f));
					pivots.Add(new Vector4(0f, r, 0f, 0.5f));
				}
			}
			var bark = new List<int>();
			for (int s = 0; s < Sides; s++)
			{
				int b0 = s, b1 = s + 1, t0 = Ring + s, t1 = Ring + s + 1;
				bark.Add(b0); bark.Add(t0); bark.Add(b1);
				bark.Add(b1); bark.Add(t0); bark.Add(t1);
			}
			int leaf = positions.Count;
			foreach (Vector3 p in new[] { new Vector3(2f, 0f, 0f), new Vector3(3f, 0f, 0f), new Vector3(2f, 0f, 1f) })
			{
				positions.Add(p);
				colours.Add(new Color32(255, 255, 255, 255));
				uv.Add(Vector2.zero);
				wind.Add(new Vector2(0f, 1f));
				pivots.Add(Vector4.zero);
			}
			var mesh = new Mesh { name = "tree" };
			made.Add(mesh);
			mesh.SetVertices(positions);
			mesh.SetColors(colours);
			mesh.SetUVs(0, uv);
			mesh.SetUVs(1, wind);
			mesh.SetUVs(2, pivots);
			mesh.subMeshCount = 2;
			mesh.SetTriangles(bark, 0);
			mesh.SetTriangles(new List<int> { leaf, leaf + 1, leaf + 2 }, 1);
			mesh.RecalculateNormals();
			return mesh;
		}

		private Mesh Skirt(Mesh source, params bool[] allowed)
		{
			Mesh mesh = TrunkSkirt.Build(source, allowed);
			if (mesh != null)
			{
				made.Add(mesh);
			}
			return mesh;
		}

		private static float Outward(Vector3 a, Vector3 b, Vector3 c)
		{
			Vector3 n = Vector3.Cross(b - a, c - a);
			Vector3 centre = (a + b + c) / 3f;
			return n.x * centre.x + n.z * centre.z;
		}

		[Test]
		public void FootRing_IsCopiedFromTheTrunksOpenBottom_InTheBarksSubmesh()
		{
			Mesh source = Tree();
			Mesh skirted = Skirt(source, true, true);
			Assert.IsNotNull(skirted, "an open bark foot grows a skirt");
			Assert.AreEqual(source.vertexCount + Ring, skirted.vertexCount, "one foot per bottom ring vertex (the seam's two included), none for the leaf or the top");
			Assert.AreEqual(2, skirted.GetVertexAttributeDimension(VertexAttribute.TexCoord1), "the wind channel keeps its layout");
			Assert.AreEqual(4, skirted.GetVertexAttributeDimension(VertexAttribute.TexCoord2), "the part pivot channel keeps its layout");
			Assert.AreEqual(source.GetIndexCount(0) + Sides * 6, skirted.GetIndexCount(0), "two triangles under every bark foot edge, in the bark's own submesh");
			Assert.AreEqual(source.GetIndexCount(1), skirted.GetIndexCount(1), "the leaf card grows nothing");
			Assert.IsFalse(skirted.isReadable, "the copy is uploaded and its CPU data freed");
		}

		[Test]
		public void FootVertices_CarryTheRingsAttributes_AndTheBarksVPerMetre()
		{
			Mesh source = Tree();
			var sourcePositions = new List<Vector3>();
			source.GetVertices(sourcePositions);
			Mesh skirted = TrunkSkirt.Build(source, new[] { true, true }, keepReadable: true);
			Assert.IsNotNull(skirted);
			made.Add(skirted);
			var positions = new List<Vector3>();
			skirted.GetVertices(positions);
			var wind = new List<Vector2>();
			skirted.GetUVs(1, wind);
			var pivots = new List<Vector4>();
			skirted.GetUVs(2, pivots);
			for (int i = source.vertexCount; i < skirted.vertexCount; i++)
			{
				Assert.AreEqual(0f, positions[i].y, 1e-5f, "a foot starts on the ring it hangs from");
				Assert.IsTrue(sourcePositions.GetRange(0, Ring).Contains(positions[i]), "a foot is a copy of a bottom ring vertex");
				Assert.AreEqual(-2f, wind[i].y, 1e-4f, "flagged with -(1 + v per metre): the tube's bark runs 1 v per metre");
				Assert.AreEqual(0f, wind[i].x, 1e-5f, "the ring's own sway (none at the foot)");
				Assert.AreEqual(0.5f, pivots[i].w, 1e-5f, "the part data comes with it, so the shader moves it as it moves the ring");
			}
			// Every skirt triangle faces the way the bark above it does, once the shader has dropped its feet (in the mesh
			// they sit on the ring: no area until then).
			for (int i = source.vertexCount; i < positions.Count; i++)
			{
				positions[i] += Vector3.down;
			}
			var bark = new List<int>();
			skirted.GetTriangles(bark, 0);
			int sourceIndices = (int)source.GetIndexCount(0);
			for (int t = 0; t < bark.Count; t += 3)
			{
				float outward = Outward(positions[bark[t]], positions[bark[t + 1]], positions[bark[t + 2]]);
				Assert.Greater(outward, 0f, t < sourceIndices ? $"bark triangle {t / 3} faces out (fixture)" : $"skirt triangle {(t - sourceIndices) / 3} faces out, as the bark does");
			}
		}

		[Test]
		public void NoSkirt_WithoutOpenBarkAtTheFoot_OrWhereNotAllowed()
		{
			Assert.IsNull(Skirt(Tree(), false, false), "a submesh not drawn by the vegetation shader (or a billboard) grows none");
			Assert.IsNull(Skirt(Tree(), false, true), "the leaf card is not bark");

			// A closed foot: the same tube with a cap under it.
			Mesh capped = Tree();
			var bark = new List<int>();
			capped.GetTriangles(bark, 0);
			// A fan from vertex 0, wound against the bark's foot edges; the seam's twin (vertex Sides) is vertex 0's place.
			for (int s = 1; s < Sides - 1; s++)
			{
				bark.Add(0); bark.Add(s); bark.Add(s + 1);
			}
			capped.SetTriangles(bark, 0);
			Assert.IsNull(Skirt(capped, true, true), "a capped foot has no open edge to hang a skirt from");
		}

	}
}
