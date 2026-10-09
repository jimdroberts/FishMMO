using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FishMMO.Client;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.Rendering;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The client's instanced terrain tree renderer (TerrainTreeInstancing): placement, Unity's LOD rule,
	/// chunk bucketing and culling, batch splitting, the prototype reader, the gather, the vegetation-fade
	/// seam, and the guards that keep it out of the server and away from the colliders.
	/// </summary>
	public class TerrainTreeInstancingTests
	{
		private const string RendererFolder = "Assets/Scripts/Client/World/Terrain";
		private static readonly string[] RendererTypes =
		{
			"TerrainTreeInstancing", "TerrainTreeModel", "TerrainTreeField", "TerrainTreeMath",
			"TerrainDetailInstancing", "TerrainDetailModel", "TerrainDetailField", "TerrainDetailMath",
			"TerrainGpuRenderer", "TerrainGpuMath", "TerrainInstancingShared", "FishInstance", "FishVisible", "RangeAllocator", "TrunkSkirt",
		};
		private static readonly float[] TreeLods = { 0.25f, 0.08f, 0.002f };

		private readonly List<Object> made = new List<Object>();

		[TearDown]
		public void TearDown()
		{
			foreach (Object o in made)
			{
				if (o != null)
				{
					Object.DestroyImmediate(o);
				}
			}
			made.Clear();
		}

		private static void Near(float expected, float actual, float tolerance, string message)
		{
			LogAssert.IsTrue(Mathf.Abs(expected - actual) <= tolerance, $"{message}: expected {expected}, got {actual}");
		}

		private static void Near(Vector3 expected, Vector3 actual, float tolerance, string message)
		{
			LogAssert.IsTrue((expected - actual).magnitude <= tolerance, $"{message}: expected {expected}, got {actual}");
		}

		// ── Placement ─────────────────────────────────────────────────

		[Test]
		public void InstanceMatrix_StandsWhereTheTerrainPutsTheTree()
		{
			var origin = new Vector3(100f, -5f, 200f);
			var size = new Vector3(1000f, 600f, 1000f);
			var normalized = new Vector3(0.25f, 0.5f, 0.75f);
			const float rotation = 1.2f, width = 1.5f, height = 2f;

			Matrix4x4 m = TerrainTreeMath.InstanceMatrix(origin, size, normalized, rotation, width, height);

			// Height included: y is the stored normalised height times the terrain's height.
			Near(new Vector3(350f, 295f, 950f), m.GetColumn(3), 1e-3f, "translation = corner + position scaled by size");
			Near(new Vector3(350f, 295f, 950f), TerrainTreeMath.WorldPosition(origin, size, normalized), 1e-3f, "world position");

			// Unity's tree: Euler(0, rotation in degrees, 0), scale (width, height, width).
			Matrix4x4 unity = Matrix4x4.TRS(new Vector3(350f, 295f, 950f), Quaternion.Euler(0f, rotation * Mathf.Rad2Deg, 0f), new Vector3(width, height, width));
			for (int i = 0; i < 16; i++)
			{
				Near(unity[i], m[i], 1e-3f, $"element {i} matches TRS(position, Euler(0, rotation°, 0), (w, h, w))");
			}

			// Up stays up, scaled by the height; the turn is about y only.
			Near(new Vector3(350f, 297f, 950f), m.MultiplyPoint3x4(Vector3.up), 1e-3f, "local up is world up × heightScale");
			Vector3 side = m.MultiplyPoint3x4(Vector3.right) - (Vector3)m.GetColumn(3);
			Near(0f, side.y, 1e-4f, "local right stays horizontal");
			Near(width, side.magnitude, 1e-4f, "local right is scaled by widthScale");
			Near(Mathf.Cos(rotation) * width, side.x, 1e-4f, "rotation is in radians, about y");
		}

		[Test]
		public void TransformBounds_HoldsEveryTransformedCorner()
		{
			var local = new Bounds(new Vector3(0f, 3f, 0f), new Vector3(2f, 6f, 1f));
			Matrix4x4 m = TerrainTreeMath.InstanceMatrix(new Vector3(10f, 0f, 10f), new Vector3(100f, 100f, 100f), new Vector3(0.5f, 0.1f, 0.5f), 0.7f, 1.3f, 0.8f);
			Bounds world = TerrainTreeMath.TransformBounds(m, local);
			world.Expand(1e-3f);
			for (int i = 0; i < 8; i++)
			{
				Vector3 corner = local.center + Vector3.Scale(local.extents, new Vector3((i & 1) == 0 ? -1 : 1, (i & 2) == 0 ? -1 : 1, (i & 4) == 0 ? -1 : 1));
				LogAssert.IsTrue(world.Contains(m.MultiplyPoint3x4(corner)), $"corner {i} inside");
			}
			Near(TerrainTreeMath.WorldSize(8f, 1.3f, 0.8f), 8f * 1.3f, 1e-5f, "a LODGroup measures by the larger scale");
		}

		// ── Levels of detail ──────────────────────────────────────────

		[Test]
		public void RelativeHeight_IsUnitysLodGroupRule()
		{
			const float size = 8.49f, distance = 100f, fov = 60f;
			float factor = TerrainTreeMath.ScreenFactor(fov, false, 0f, 1f);
			// UnityEditor.LODUtility.DistanceToRelativeHeight: size * 0.5 / (distance * tan(fov / 2)).
			float unity = size * 0.5f / (distance * Mathf.Tan(Mathf.Deg2Rad * fov * 0.5f));
			Near(unity, TerrainTreeMath.RelativeHeight(size, distance, factor, false), 1e-6f, "relative height");
			float biased = TerrainTreeMath.RelativeHeight(size, distance, TerrainTreeMath.ScreenFactor(fov, false, 0f, 2f), false);
			Near(unity * 2f, biased, 1e-6f, "the LOD bias multiplies the relative height");
			// Orthographic: the distance does not matter.
			float ortho = TerrainTreeMath.ScreenFactor(fov, true, 50f, 1f);
			Near(size / 100f, TerrainTreeMath.RelativeHeight(size, 10f, ortho, true), 1e-6f, "orthographic near");
			Near(size / 100f, TerrainTreeMath.RelativeHeight(size, 1000f, ortho, true), 1e-6f, "orthographic far");
		}

		[Test]
		public void SelectLod_PicksTheFirstLevelReachedAndCullsBelowTheLast()
		{
			LogAssert.AreEqual(0, TerrainTreeMath.SelectLod(0.5f, TreeLods, 0), "big on screen: LOD0");
			LogAssert.AreEqual(0, TerrainTreeMath.SelectLod(0.25f, TreeLods, 0), "at the transition: the finer level");
			LogAssert.AreEqual(1, TerrainTreeMath.SelectLod(0.1f, TreeLods, 0), "LOD1");
			LogAssert.AreEqual(2, TerrainTreeMath.SelectLod(0.01f, TreeLods, 0), "the billboard");
			LogAssert.AreEqual(-1, TerrainTreeMath.SelectLod(0.001f, TreeLods, 0), "below the last: culled");
			LogAssert.AreEqual(1, TerrainTreeMath.SelectLod(0.5f, TreeLods, 1), "maximumLODLevel 1 never draws LOD0");
			LogAssert.AreEqual(2, TerrainTreeMath.SelectLod(0.5f, TreeLods, 5), "a maximum past the last level draws the last");
			LogAssert.AreEqual(-1, TerrainTreeMath.SelectLod(0.001f, TreeLods, 1), "the maximum does not keep a culled tree");
			LogAssert.AreEqual(0, TerrainTreeMath.SelectLod(0f, new[] { 0f }, 0), "no LODGroup: always drawn");

			// An 8.49 m acacia at 60°, bias 1: LOD0 to ~29 m, LOD1 to ~92 m, billboard to ~3.7 km.
			float factor = TerrainTreeMath.ScreenFactor(60f, false, 0f, 1f);
			int At(float d) => TerrainTreeMath.SelectLod(TerrainTreeMath.RelativeHeight(8.49f, d, factor, false), TreeLods, 0);
			LogAssert.AreEqual(0, At(25f), "25 m");
			LogAssert.AreEqual(1, At(60f), "60 m");
			LogAssert.AreEqual(2, At(500f), "500 m");
			LogAssert.AreEqual(-1, At(4000f), "4 km");
		}

		// ── Chunks ────────────────────────────────────────────────────

		[Test]
		public void Chunks_CutATerrainIntoSquaresNoLongerThanTheChunk()
		{
			LogAssert.AreEqual(17, TerrainTreeMath.ChunkCount(1083.3333f, 64f), "Cov Viaduct tile");
			LogAssert.AreEqual(2, TerrainTreeMath.ChunkCount(128f, 64f), "an exact multiple does not add a sliver");
			LogAssert.AreEqual(1, TerrainTreeMath.ChunkCount(10f, 64f), "a small terrain is one chunk");
			LogAssert.AreEqual(1, TerrainTreeMath.ChunkCount(0f, 64f), "degenerate");
			LogAssert.AreEqual(0, TerrainTreeMath.ChunkIndex(0f, 0f, 17, 17), "corner");
			LogAssert.AreEqual(17 * 17 - 1, TerrainTreeMath.ChunkIndex(1f, 1f, 17, 17), "the far edge belongs to the last chunk");
			LogAssert.AreEqual(17 + 1, TerrainTreeMath.ChunkIndex(1.5f / 17f, 1.5f / 17f, 17, 17), "row-major");
		}

		[Test]
		public void SortByChunkThenPrototype_IsAStablePermutationWithChunkBoundaries()
		{
			var random = new System.Random(7);
			const int n = 500, chunks = 9, prototypes = 4;
			var chunkOf = new int[n];
			var prototypeOf = new int[n];
			for (int i = 0; i < n; i++)
			{
				chunkOf[i] = random.Next(chunks);
				prototypeOf[i] = random.Next(prototypes);
			}
			var order = new int[n];
			var chunkStart = new int[chunks + 1];
			TerrainTreeMath.SortByChunkThenPrototype(chunkOf, prototypeOf, chunks, prototypes, order, chunkStart);

			LogAssert.AreEqual(n, order.Distinct().Count(), "every instance exactly once");
			for (int k = 1; k < n; k++)
			{
				int a = order[k - 1], b = order[k];
				int keyA = chunkOf[a] * prototypes + prototypeOf[a], keyB = chunkOf[b] * prototypes + prototypeOf[b];
				LogAssert.IsTrue(keyA < keyB || (keyA == keyB && a < b), $"sorted by (chunk, prototype), stable, at {k}");
			}
			LogAssert.AreEqual(0, chunkStart[0], "first chunk starts at 0");
			LogAssert.AreEqual(n, chunkStart[chunks], "last boundary is the count");
			for (int c = 0; c < chunks; c++)
			{
				for (int k = chunkStart[c]; k < chunkStart[c + 1]; k++)
				{
					LogAssert.AreEqual(c, chunkOf[order[k]], $"instance at {k} is in chunk {c}");
				}
			}
		}

		// ── Culling ───────────────────────────────────────────────────

		/// <summary>Planes of a camera at the origin looking down +z (60°, square, 0.3 – 1000 m).</summary>
		private static Plane[] ForwardPlanes()
		{
			Matrix4x4 worldToCamera = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * Matrix4x4.TRS(Vector3.zero, Quaternion.identity, Vector3.one).inverse;
			return GeometryUtility.CalculateFrustumPlanes(Matrix4x4.Perspective(60f, 1f, 0.3f, 1000f) * worldToCamera);
		}

		[Test]
		public void IntersectsFrustum_KeepsWhatIsAheadAndDropsTheRest()
		{
			Plane[] planes = ForwardPlanes();
			LogAssert.IsTrue(TerrainTreeMath.IntersectsFrustum(planes, new Bounds(new Vector3(0f, 0f, 50f), Vector3.one * 2f)), "ahead");
			LogAssert.IsFalse(TerrainTreeMath.IntersectsFrustum(planes, new Bounds(new Vector3(0f, 0f, -50f), Vector3.one * 2f)), "behind");
			LogAssert.IsFalse(TerrainTreeMath.IntersectsFrustum(planes, new Bounds(new Vector3(0f, 0f, 2000f), Vector3.one * 2f)), "past the far plane");
			LogAssert.IsFalse(TerrainTreeMath.IntersectsFrustum(planes, new Bounds(new Vector3(200f, 0f, 50f), Vector3.one * 2f)), "off to the side");
			LogAssert.IsTrue(TerrainTreeMath.IntersectsFrustum(planes, new Bounds(new Vector3(40f, 0f, 50f), new Vector3(30f, 2f, 2f))), "straddling the side plane");
			// Agrees with Unity's own test.
			var box = new Bounds(new Vector3(28f, 3f, 50f), new Vector3(4f, 4f, 4f));
			LogAssert.AreEqual(GeometryUtility.TestPlanesAABB(planes, box), TerrainTreeMath.IntersectsFrustum(planes, box), "matches GeometryUtility.TestPlanesAABB");
		}

		[Test]
		public void ShadowSweep_KeepsAnOffscreenChunkWhoseShadowFallsOnScreen()
		{
			Plane[] planes = ForwardPlanes();
			// A 30 m tall chunk 20 m behind the camera: not visible itself.
			var chunk = new Bounds(new Vector3(0f, 15f, -20f), new Vector3(10f, 30f, 10f));
			LogAssert.IsFalse(TerrainTreeMath.IntersectsFrustum(planes, chunk), "the chunk is behind the camera");

			Vector3 forwardSun = new Vector3(0f, -0.5f, 1f).normalized;
			LogAssert.IsTrue(TerrainTreeMath.IntersectsFrustum(planes, TerrainTreeMath.ShadowSweep(chunk, forwardSun, 150f)),
				"a low sun behind it throws its shadow ahead: kept");
			Vector3 backSun = new Vector3(0f, -0.5f, -1f).normalized;
			LogAssert.IsFalse(TerrainTreeMath.IntersectsFrustum(planes, TerrainTreeMath.ShadowSweep(chunk, backSun, 150f)),
				"a sun ahead throws it further behind: dropped");
			Bounds capped = TerrainTreeMath.ShadowSweep(chunk, forwardSun, 5f);
			LogAssert.IsTrue(capped.max.z <= chunk.max.z + 5f + 1e-3f, "the sweep is capped at the shadow distance");
		}

		[Test]
		public void BoxDistances_AreNearestAndFarthest()
		{
			var box = new Bounds(new Vector3(10f, 0f, 0f), new Vector3(2f, 2f, 2f));
			Near(9f, TerrainTreeMath.MinDistance(box, Vector3.zero), 1e-5f, "nearest face");
			Near(new Vector3(11f, 1f, 1f).magnitude, TerrainTreeMath.MaxDistance(box, Vector3.zero), 1e-5f, "farthest corner");
			Near(0f, TerrainTreeMath.MinDistance(box, new Vector3(10f, 0f, 0f)), 1e-5f, "inside");
		}

		// ── Batches ───────────────────────────────────────────────────

		[Test]
		public void Batches_SplitAt1023()
		{
			LogAssert.AreEqual(1023, TerrainTreeMath.MaxInstancesPerBatch, "the batch size");
			LogAssert.AreEqual(0, TerrainTreeMath.BatchCount(0), "none");
			LogAssert.AreEqual(1, TerrainTreeMath.BatchCount(1), "one");
			LogAssert.AreEqual(1, TerrainTreeMath.BatchCount(1023), "exactly one batch");
			LogAssert.AreEqual(2, TerrainTreeMath.BatchCount(1024), "one over");
			LogAssert.AreEqual(3, TerrainTreeMath.BatchCount(2500), "2500");
			int total = 0, expectedStart = 0;
			for (int b = 0; b < TerrainTreeMath.BatchCount(2500); b++)
			{
				TerrainTreeMath.Batch(2500, b, out int start, out int size);
				LogAssert.AreEqual(expectedStart, start, $"batch {b} starts where the last ended");
				LogAssert.IsTrue(size > 0 && size <= 1023, $"batch {b} size {size}");
				expectedStart += size;
				total += size;
			}
			LogAssert.AreEqual(2500, total, "every instance drawn once");
			TerrainTreeMath.Batch(2500, 2, out int lastStart, out int lastSize);
			LogAssert.AreEqual(2046, lastStart, "last start");
			LogAssert.AreEqual(454, lastSize, "last size");
		}

		// ── The prototype reader ──────────────────────────────────────

		private Material InstancedMaterial(bool instancing)
		{
			Shader shader = Shader.Find("Universal Render Pipeline/Lit");
			Assert.IsNotNull(shader, "URP Lit shader");
			var material = new Material(shader) { enableInstancing = instancing };
			made.Add(material);
			return material;
		}

		private Mesh Box()
		{
			var mesh = new Mesh
			{
				vertices = new[] { new Vector3(-1f, 0f, -1f), new Vector3(1f, 0f, -1f), new Vector3(0f, 4f, 1f) },
				triangles = new[] { 0, 2, 1 },
			};
			mesh.RecalculateBounds();
			made.Add(mesh);
			return mesh;
		}

		private GameObject Part(Transform parent, string name, Mesh mesh, Material material, ShadowCastingMode shadows)
		{
			var go = new GameObject(name);
			go.transform.SetParent(parent, false);
			go.AddComponent<MeshFilter>().sharedMesh = mesh;
			var renderer = go.AddComponent<MeshRenderer>();
			renderer.sharedMaterials = new[] { material };
			renderer.shadowCastingMode = shadows;
			return go;
		}

		private GameObject LodPrefab(Material material)
		{
			var root = new GameObject("Tree_Test");
			made.Add(root);
			Mesh mesh = Box();
			var lods = new LOD[3];
			for (int i = 0; i < 3; i++)
			{
				GameObject part = Part(root.transform, $"LOD{i}", mesh, material, i < 2 ? ShadowCastingMode.On : ShadowCastingMode.Off);
				lods[i] = new LOD(TreeLods[i], new Renderer[] { part.GetComponent<MeshRenderer>() });
			}
			var group = root.AddComponent<LODGroup>();
			group.SetLODs(lods);
			group.RecalculateBounds();
			return root;
		}

		[Test]
		public void ModelBuild_ReadsTheLodGroup()
		{
			GameObject prefab = LodPrefab(InstancedMaterial(true));
			TerrainTreeModel model = TerrainTreeModel.Build(prefab, 7, out string reason);
			try
			{
				LogAssert.IsNotNull(model, $"built ({reason})");
				LogAssert.IsTrue(model.HasLodGroup, "has a LODGroup");
				LogAssert.AreEqual(3, model.Levels.Length, "three levels");
				LogAssert.AreEqual(string.Join(",", TreeLods), string.Join(",", model.Transitions), "transition heights");
				Near(0.025f, model.FadeWidths[0], 1e-6f, "LOD0's band: the default share (the prefab sets no fadeTransitionWidth)");
				Near(0.0002f, model.FadeWidths[2], 1e-7f, "the last level's cull band");
				Near(prefab.GetComponent<LODGroup>().size, model.Size, 1e-5f, "the group's size");
				TerrainTreeModel.Part part = model.Levels[0].Parts[0];
				LogAssert.IsTrue(part.Identity, "a child at the root's origin needs no extra matrix");
				LogAssert.AreEqual(7, part.Params[0].layer, "drawn on the terrain's layer");
				LogAssert.AreEqual(ShadowCastingMode.On, part.Params[0].shadowCastingMode, "LOD0 casts");
				LogAssert.AreEqual(ShadowCastingMode.Off, model.Levels[2].Parts[0].Params[0].shadowCastingMode, "the last level does not");
				LogAssert.IsTrue(part.UsesProbes, "the renderer blends probes (default)");
			}
			finally
			{
				model?.Dispose();
			}
		}

		[Test]
		public void ModelBuild_RefusesWhatTheInstancedPathCannotDraw()
		{
			LogAssert.IsNull(TerrainTreeModel.Build(LodPrefab(InstancedMaterial(false)), 0, out string noInstancing), "a material without instancing");
			LogAssert.IsTrue(noInstancing.Contains("instancing"), noInstancing);

			GameObject billboard = LodPrefab(InstancedMaterial(true));
			new GameObject("Billboard", typeof(BillboardRenderer)).transform.SetParent(billboard.transform, false);
			LogAssert.IsNull(TerrainTreeModel.Build(billboard, 0, out string hasBillboard), "a SpeedTree-style billboard renderer");
			LogAssert.IsTrue(hasBillboard.Contains("BillboardRenderer"), hasBillboard);

			var empty = new GameObject("Empty");
			made.Add(empty);
			LogAssert.IsNull(TerrainTreeModel.Build(empty, 0, out string nothing), "nothing to draw");
			LogAssert.IsNull(TerrainTreeModel.Build(null, 0, out _), "no prefab");
		}

		[Test]
		public void ModelBuild_WithoutLodGroup_DrawsEveryRendererAtOneLevel_AndKeepsChildOffsets()
		{
			var root = new GameObject("Rock_Test");
			made.Add(root);
			GameObject part = Part(root.transform, "Mesh", Box(), InstancedMaterial(true), ShadowCastingMode.On);
			part.transform.localPosition = new Vector3(0f, 1f, 0f);
			part.layer = 3;
			TerrainTreeModel model = TerrainTreeModel.Build(root, -1, out string reason);
			try
			{
				LogAssert.IsNotNull(model, $"built ({reason})");
				LogAssert.IsFalse(model.HasLodGroup, "no LODGroup");
				LogAssert.AreEqual(1, model.Levels.Length, "one level");
				LogAssert.AreEqual(0f, model.Transitions[0], "drawn at any size within the draw distance");
				TerrainTreeModel.Part p = model.Levels[0].Parts[0];
				LogAssert.IsFalse(p.Identity, "an offset child");
				Near(new Vector3(0f, 1f, 0f), p.Local.GetColumn(3), 1e-5f, "its offset under the root");
				LogAssert.AreEqual(3, p.Params[0].layer, "preserveTreePrototypeLayers keeps the renderer's layer");
			}
			finally
			{
				model?.Dispose();
			}
		}

		// ── Gathering ─────────────────────────────────────────────────

		private static TreeInstance Tree(float x, float z, float scale = 1f)
		{
			return new TreeInstance { position = new Vector3(x, 0f, z), widthScale = scale, heightScale = scale, rotation = 0f, prototypeIndex = 0 };
		}

		[Test]
		public void Gather_BucketsVisibleInstancesByLevel_AndDropsTheRest()
		{
			TerrainTreeModel model = TerrainTreeModel.Build(LodPrefab(InstancedMaterial(true)), 0, out string reason);
			Assert.IsNotNull(model, reason);
			// A 2 km square terrain centred on the camera, which looks down +z.
			var origin = new Vector3(-1000f, 0f, -1000f);
			var size = new Vector3(2000f, 100f, 2000f);
			Vector2 N(float worldX, float worldZ) => new Vector2((worldX - origin.x) / size.x, (worldZ - origin.z) / size.z);
			Vector2 near = N(0f, 10f), mid = N(0f, 60f), far = N(0f, 500f), behind = N(0f, -200f), beyond = N(0f, 900f);
			var instances = new[]
			{
				Tree(near.x, near.y), Tree(mid.x, mid.y), Tree(far.x, far.y), Tree(behind.x, behind.y), Tree(beyond.x, beyond.y),
			};
			TerrainTreeField field = TerrainTreeField.Build(origin, size, instances, new[] { model });
			try
			{
				model.EnsureCapacity(field.PrototypeCounts[0]);
				LogAssert.AreEqual(5, field.InstanceCount, "every instance laid out");

				var view = new TerrainTreeField.View
				{
					Position = new Vector3(0f, 2f, 0f),
					Planes = ForwardPlanes(),
					ScreenFactor = TerrainTreeMath.ScreenFactor(60f, false, 0f, 1f),
					MaximumLodLevel = 0,
					Shadows = false,
				};
				model.Clear();
				int gathered = field.Gather(in view, 800f, 1f);

				// The test tree's group is 4 m: LOD0 at 10 m; the billboard level at 60 m and 500 m.
				float factor = view.ScreenFactor;
				int Expected(float d) => TerrainTreeMath.SelectLod(TerrainTreeMath.RelativeHeight(model.Size, d, factor, false), TreeLods, 0);
				var expected = new int[3];
				foreach (float d in new[] { 10f, 60f, 500f })
				{
					int lod = Expected(d);
					if (lod >= 0)
					{
						expected[lod]++;
					}
				}
				int expectedTotal = expected.Sum();
				LogAssert.AreEqual(expectedTotal, gathered, "the three ahead within 800 m are gathered unless culled by size; behind and beyond are not");
				for (int l = 0; l < 3; l++)
				{
					LogAssert.AreEqual(expected[l], model.Levels[l].Count, $"level {l} bucket");
				}
				LogAssert.AreEqual(1, model.Levels[0].Count, "the near tree is LOD0");
				for (int l = 0; l < 3; l++)
				{
					LogAssert.AreEqual(0, model.Levels[l].FadedCount, $"level {l}: nothing in a band, nothing faded");
					for (int i = 0; i < model.Levels[l].Count; i++)
					{
						LogAssert.AreEqual(0f, model.Levels[l].Fades[i], $"level {l} entry {i} drawn whole");
					}
				}

				// Turned around, only the tree behind is seen.
				Matrix4x4 worldToCamera = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * Matrix4x4.TRS(view.Position, Quaternion.Euler(0f, 180f, 0f), Vector3.one).inverse;
				view.Planes = GeometryUtility.CalculateFrustumPlanes(Matrix4x4.Perspective(60f, 1f, 0.3f, 1000f) * worldToCamera);
				model.Clear();
				int behindCount = field.Gather(in view, 800f, 1f);
				LogAssert.AreEqual(Expected(200f) >= 0 ? 1 : 0, behindCount, "the tree behind, when the camera turns");
			}
			finally
			{
				field.Dispose();
				model.Dispose();
			}
		}

		// ── Cross-fade bands ──────────────────────────────────────────

		private static float[] TreeWidths()
		{
			var widths = new float[TreeLods.Length];
			for (int i = 0; i < widths.Length; i++)
			{
				widths[i] = TerrainTreeMath.FadeWidth(TreeLods, i, 0f);
			}
			return widths;
		}

		[Test]
		public void FadeWidth_IsTheDefaultShare_OrUnitysTransitionWidth_NeverPastTheLevelAbove()
		{
			LogAssert.AreEqual(0.1f, TerrainTreeMath.DefaultFadeBandShare, "default band: 10% of the transition height");
			Near(0.025f, TerrainTreeMath.FadeWidth(TreeLods, 0, 0f), 1e-6f, "LOD0→1: 10% of 0.25");
			Near(0.008f, TerrainTreeMath.FadeWidth(TreeLods, 1, 0f), 1e-6f, "LOD1→2: 10% of 0.08");
			Near(0.0002f, TerrainTreeMath.FadeWidth(TreeLods, 2, 0f), 1e-7f, "last→culled: 10% of 0.002");
			// Unity's fadeTransitionWidth: a share of the level's own range (LOD1: 0.08 .. 0.25).
			Near(0.5f * (0.25f - 0.08f), TerrainTreeMath.FadeWidth(TreeLods, 1, 0.5f), 1e-6f, "fadeTransitionWidth 0.5 of LOD1's range");
			Near(0.5f * (1f - 0.25f), TerrainTreeMath.FadeWidth(TreeLods, 0, 0.5f), 1e-6f, "LOD0's range runs to 1");
			// Clamped to the range: a huge share cannot reach the level above.
			var tight = new[] { 0.25f, 0.24f };
			Near(0.01f, TerrainTreeMath.FadeWidth(tight, 1, 0f, 0.5f), 1e-6f, "clamped to the level's own range");
			LogAssert.AreEqual(0f, TerrainTreeMath.FadeWidth(new[] { 0f }, 0, 0f), "no LODGroup: no band");
		}

		[Test]
		public void SelectLodFaded_InsideABand_DrawsBothLevelsWithExactlyComplementaryFades()
		{
			float[] widths = TreeWidths();
			// LOD0→LOD1 band: [0.25, 0.275).
			foreach (float rh in new[] { 0.2501f, 0.255f, 0.2625f, 0.27f, 0.2749f })
			{
				int lod = TerrainTreeMath.SelectLodFaded(rh, TreeLods, widths, 0, out float fade, out int partner, out float partnerFade);
				LogAssert.AreEqual(0, lod, $"rh {rh}: the outgoing level is LOD0");
				LogAssert.AreEqual(1, partner, $"rh {rh}: the incoming level is LOD1");
				LogAssert.IsTrue(fade < 0f, $"rh {rh}: outgoing fades out (−f), got {fade}");
				LogAssert.IsTrue(partnerFade > 0f && partnerFade < 1f, $"rh {rh}: incoming fades in (+f), got {partnerFade}");
				LogAssert.IsTrue(fade == -partnerFade, $"rh {rh}: magnitudes exactly equal ({fade} vs {partnerFade})");
				Near((0.275f - rh) / 0.025f, partnerFade, 1e-4f, $"rh {rh}: f is the incoming share across the band");
			}
			// Deeper into the band (smaller on screen) the incoming level takes more of the pattern.
			TerrainTreeMath.SelectLodFaded(0.27f, TreeLods, widths, 0, out _, out _, out float early);
			TerrainTreeMath.SelectLodFaded(0.255f, TreeLods, widths, 0, out _, out _, out float late);
			LogAssert.IsTrue(late > early, "the incoming share grows as the tree shrinks on screen");
		}

		[Test]
		public void SelectLodFaded_BandEdges_AreOneLevelDrawnWhole()
		{
			float[] widths = TreeWidths();
			int lod = TerrainTreeMath.SelectLodFaded(0.3f, TreeLods, widths, 0, out float fade, out int partner, out _);
			LogAssert.AreEqual(0, lod, "above the band: LOD0");
			LogAssert.AreEqual(0f, fade, "drawn whole");
			LogAssert.AreEqual(-1, partner, "no partner");

			float top = TreeLods[0] + widths[0];
			lod = TerrainTreeMath.SelectLodFaded(top, TreeLods, widths, 0, out fade, out partner, out _);
			LogAssert.AreEqual(0, lod, "the band's top edge (exclusive): LOD0 whole");
			LogAssert.AreEqual(0f, fade, "no fade at the top edge");
			LogAssert.AreEqual(-1, partner, "no incoming draw at the top edge (a 0 there would draw it whole)");

			lod = TerrainTreeMath.SelectLodFaded(0.25f, TreeLods, widths, 0, out fade, out partner, out _);
			LogAssert.AreEqual(1, lod, "exactly at the transition: wholly the next level");
			LogAssert.AreEqual(0f, fade, "drawn whole");
			LogAssert.AreEqual(-1, partner, "the outgoing level is skipped, not sent a 0");

			lod = TerrainTreeMath.SelectLodFaded(0.2f, TreeLods, widths, 0, out fade, out partner, out _);
			LogAssert.AreEqual(1, lod, "below the transition, above LOD1's own band: LOD1 whole");
			LogAssert.AreEqual(0f, fade, "drawn whole");
			LogAssert.AreEqual(-1, partner, "no partner");

			// maximumLODLevel 1: the LOD0 transition is not a transition, so no band there.
			lod = TerrainTreeMath.SelectLodFaded(0.26f, TreeLods, widths, 1, out fade, out partner, out _);
			LogAssert.AreEqual(1, lod, "maximumLODLevel 1 draws LOD1");
			LogAssert.AreEqual(0f, fade, "with no fade where LOD0 would have handed over");
			LogAssert.AreEqual(-1, partner, "and no partner");
		}

		[Test]
		public void SelectLodFaded_TheLastLevelFadesOutAloneBeforeTheCull()
		{
			float[] widths = TreeWidths();
			// The billboard's cull band: [0.002, 0.0022).
			float previous = 0f;
			foreach (float rh in new[] { 0.00219f, 0.0021f, 0.00205f, 0.002001f })
			{
				int lod = TerrainTreeMath.SelectLodFaded(rh, TreeLods, widths, 0, out float fade, out int partner, out float partnerFade);
				LogAssert.AreEqual(2, lod, $"rh {rh}: still the last level");
				LogAssert.AreEqual(-1, partner, $"rh {rh}: no partner draw before the cull");
				LogAssert.AreEqual(0f, partnerFade, $"rh {rh}: no partner fade");
				LogAssert.IsTrue(fade < 0f && fade > -1f, $"rh {rh}: fading out, never 0 (got {fade})");
				LogAssert.IsTrue(-fade > previous, $"rh {rh}: more of it gone the smaller it is");
				previous = -fade;
			}
			LogAssert.AreEqual(-1, TerrainTreeMath.SelectLodFaded(0.002f, TreeLods, widths, 0, out _, out _, out _), "at the cull: nothing drawn");
			LogAssert.AreEqual(-1, TerrainTreeMath.SelectLodFaded(0.001f, TreeLods, widths, 0, out _, out _, out _), "below it: nothing drawn");
			int whole = TerrainTreeMath.SelectLodFaded(0.0023f, TreeLods, widths, 0, out float wholeFade, out _, out _);
			LogAssert.AreEqual(2, whole, "above the cull band: the last level");
			LogAssert.AreEqual(0f, wholeFade, "drawn whole");
		}

		[Test]
		public void SelectLodFaded_AgreesWithSelectLod_OutsideTheBands_AndNeverSendsZeroToAHiddenLevel()
		{
			float[] widths = TreeWidths();
			for (int k = 0; k <= 4000; k++)
			{
				float rh = Mathf.Pow(10f, -3.5f + k * (3.5f / 4000f));
				int plain = TerrainTreeMath.SelectLod(rh, TreeLods, 0);
				int lod = TerrainTreeMath.SelectLodFaded(rh, TreeLods, widths, 0, out float fade, out int partner, out float partnerFade);
				if (fade == 0f)
				{
					LogAssert.AreEqual(-1, partner, $"rh {rh}: a whole level has no partner");
					if (lod != plain)
					{
						// Only exactly at a transition does the faded pick hand over early.
						LogAssert.AreEqual(plain + 1 < TreeLods.Length ? plain + 1 : -1, lod, $"rh {rh}");
					}
					continue;
				}
				LogAssert.AreEqual(plain, lod, $"rh {rh}: in a band the outgoing level is the plain pick");
				LogAssert.IsTrue(fade < 0f, $"rh {rh}: the primary of a band fades out");
				if (partner >= 0)
				{
					LogAssert.IsTrue(partnerFade > 0f && partnerFade == -fade, $"rh {rh}: complementary partner");
				}
			}
		}

		[Test]
		public void Gather_PutsAnInstanceInABand_IntoBothLevels_WithComplementaryFades()
		{
			TerrainTreeModel model = TerrainTreeModel.Build(LodPrefab(InstancedMaterial(true)), 0, out string reason);
			Assert.IsNotNull(model, reason);
			var origin = new Vector3(-1000f, 0f, -1000f);
			var size = new Vector3(2000f, 100f, 2000f);
			float factor = TerrainTreeMath.ScreenFactor(60f, false, 0f, 1f);
			// The distance at which this model sits in the middle of its LOD0→1 band (rh = 0.2625).
			float d = model.Size * factor / 0.2625f;
			var instances = new[] { Tree(0.5f, (d - origin.z) / size.z) };
			TerrainTreeField field = TerrainTreeField.Build(origin, size, instances, new[] { model });
			try
			{
				model.EnsureCapacity(field.PrototypeCounts[0]);
				var view = new TerrainTreeField.View
				{
					// The reference point (the group's centre) is 2 m up: the eye at its height.
					Position = new Vector3(0f, model.LocalReference.y, 0f),
					Planes = ForwardPlanes(),
					ScreenFactor = factor,
					MaximumLodLevel = 0,
				};
				model.Clear();
				int entries = field.Gather(in view, 800f, 1f);
				LogAssert.AreEqual(2, entries, "one instance, two levels");
				LogAssert.AreEqual(1, model.Levels[0].Count, "outgoing LOD0");
				LogAssert.AreEqual(1, model.Levels[1].Count, "incoming LOD1");
				LogAssert.AreEqual(0, model.Levels[2].Count, "nothing in the billboard");
				float outgoing = model.Levels[0].Fades[0], incoming = model.Levels[1].Fades[0];
				LogAssert.IsTrue(outgoing < 0f && incoming > 0f && outgoing == -incoming, $"complementary: {outgoing} / {incoming}");
				Near(0.5f, incoming, 0.01f, "mid-band: half each");
				LogAssert.AreEqual(1, model.Levels[0].FadedCount, "the outgoing level carries a fade");
				LogAssert.AreEqual(1, model.Levels[1].FadedCount, "and the incoming one");
			}
			finally
			{
				field.Dispose();
				model.Dispose();
			}
		}

		// ── The vegetation fade seam ──────────────────────────────────

		[Test]
		public void TreeDistanceOf_AnUndrivenTerrain_IsItsOwnDistance()
		{
			var go = new GameObject("Terrain_Test");
			made.Add(go);
			var terrain = go.AddComponent<Terrain>();
			terrain.treeDistance = 1234f;
			LogAssert.IsFalse(TerrainTreeInstancing.IsDriving(terrain), "edit mode drives nothing");
			Near(1234f, TerrainTreeInstancing.TreeDistanceOf(terrain), 1e-3f, "the terrain's own distance");
			LogAssert.AreEqual(0f, TerrainTreeInstancing.TreeDistanceOf(null), "null");
		}

		[Test]
		public void VegetationDistanceFade_ReadsTheTreeDistanceThroughTheSeam()
		{
			string code = CodeOnly(Read("Assets/Scripts/Client/World/Weather/Presentation/VegetationDistanceFade.cs"));
			LogAssert.AreEqual(2, Regex.Matches(code, @"TerrainTreeInstancing\.TreeDistanceOf\(t\)").Count,
				"the signature and the band both read the renderer's remembered distance");
			LogAssert.IsFalse(Regex.IsMatch(code, @"\bt\.treeDistance\b"), "nothing reads the zeroed terrain field directly");
			LogAssert.IsTrue(code.Contains("QualitySettings.terrainTreeDistance"), "the quality override is still honoured");
			LogAssert.AreEqual(2, Regex.Matches(code, @"TerrainDetailInstancing\.DetailDistanceOf\(t\)").Count,
				"the signature and the detail band both read the detail renderer's remembered distance");
			LogAssert.IsFalse(Regex.IsMatch(code, @"\bt\.detailObjectDistance\b"), "nothing reads the zeroed detail distance directly");
			LogAssert.IsTrue(code.Contains("QualitySettings.terrainDetailDistance"), "the detail quality override is still honoured");
		}

		// ── Guards ────────────────────────────────────────────────────

		private static string Root => Directory.GetCurrentDirectory();

		private static string Read(string relative) => File.ReadAllText(Path.Combine(Root, relative)).Replace("\r\n", "\n");

		private static string CodeOnly(string source)
		{
			return string.Join("\n", source.Split('\n').Where(line =>
			{
				string t = line.TrimStart();
				return !(t.StartsWith("//") || t.StartsWith("/*") || t.StartsWith("*"));
			}));
		}

		private static IEnumerable<string> RendererSources()
		{
			return Directory.GetFiles(Path.Combine(Root, RendererFolder), "*.cs", SearchOption.AllDirectories);
		}

		[Serializable]
		private class Asmdef
		{
			public string name;
			public string[] excludePlatforms;
			public string[] defineConstraints;
		}

		[Test]
		public void Renderer_LivesOnlyInTheClientAssembly_WhichTheServerNeverCompiles()
		{
			string[] sources = RendererSources().ToArray();
			LogAssert.IsTrue(sources.Length >= 4, $"renderer sources found in {RendererFolder}");
			foreach (string source in sources)
			{
				// The nearest asmdef above each file is FishMMO.Client's.
				string dir = Path.GetDirectoryName(source);
				string asmdef = null;
				while (dir != null && dir.Length >= Root.Length)
				{
					string[] found = Directory.GetFiles(dir, "*.asmdef").Concat(Directory.GetFiles(dir, "*.asmref")).ToArray();
					if (found.Length > 0)
					{
						asmdef = found[0];
						break;
					}
					dir = Path.GetDirectoryName(dir);
				}
				LogAssert.IsNotNull(asmdef, $"{source} is in an assembly");
				LogAssert.AreEqual("FishMMO.Client.asmdef", Path.GetFileName(asmdef), $"{Path.GetFileName(source)} compiles into FishMMO.Client");
			}

			var client = JsonUtility.FromJson<Asmdef>(Read("Assets/Scripts/Client/FishMMO.Client.asmdef"));
			LogAssert.IsTrue(client.defineConstraints.Contains("!UNITY_SERVER"), "FishMMO.Client is not compiled under UNITY_SERVER");
			LogAssert.IsTrue(client.excludePlatforms.Contains("LinuxStandalone64Server"), "nor for the Linux server");
			LogAssert.IsTrue(client.excludePlatforms.Contains("WindowsStandalone64Server"), "nor for the Windows server");

			// Nothing the server compiles names the renderer.
			foreach (string tier in new[] { "Assets/Scripts/Shared", "Assets/Scripts/Server" })
			{
				foreach (string file in Directory.GetFiles(Path.Combine(Root, tier), "*.cs", SearchOption.AllDirectories))
				{
					string code = CodeOnly(File.ReadAllText(file));
					foreach (string type in RendererTypes)
					{
						LogAssert.IsFalse(Regex.IsMatch(code, $@"\b{type}\b"), $"{file} names {type}");
					}
				}
			}
		}

		[Test]
		public void Renderer_OnlyDraws_NeverWritesTreesOrColliders_AndOnlyInPlayMode()
		{
			foreach (string file in RendererSources())
			{
				string code = CodeOnly(File.ReadAllText(file));
				string name = Path.GetFileName(file);
				LogAssert.IsFalse(code.Contains("SetTreeInstances"), $"{name} never writes tree instances");
				LogAssert.IsFalse(Regex.IsMatch(code, @"\.treeInstances\s*="), $"{name} never assigns tree instances");
				LogAssert.IsFalse(Regex.IsMatch(code, @"\.treePrototypes\s*="), $"{name} never assigns tree prototypes");
				LogAssert.IsFalse(code.Contains("SetDetailLayer") || code.Contains("SetDetailResolution") || Regex.IsMatch(code, @"\.detailPrototypes\s*="),
					$"{name} never writes detail layers or prototypes");
				LogAssert.IsFalse(code.Contains("TerrainCollider"), $"{name} never touches the terrain collider");
				LogAssert.IsFalse(code.Contains("enableInstancing ="), $"{name} never edits a shared material");
				LogAssert.IsFalse(code.Contains("[ExecuteAlways]") || code.Contains("[ExecuteInEditMode]") || Regex.IsMatch(code, @"(?<!Runtime)InitializeOnLoad"),
					$"{name} never runs in edit mode");
			}
			string system = CodeOnly(Read(RendererFolder + "/TerrainTreeInstancing.cs"));
			LogAssert.IsTrue(system.Contains("RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)"), "hooked at runtime only");
			LogAssert.IsTrue(Regex.Matches(system, @"!Application\.isPlaying").Count >= 3, "the hook, the sync and the camera callback check play mode");
			LogAssert.IsTrue(system.Contains("ExitingPlayMode"), "every terrain's distance is put back when play mode ends");
			string details = CodeOnly(Read(RendererFolder + "/TerrainDetailInstancing.cs"));
			LogAssert.IsTrue(details.Contains("RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)"), "details hooked at runtime only");
			LogAssert.IsTrue(Regex.Matches(details, @"!Application\.isPlaying").Count >= 3, "details: the hook, the sync and the camera callback check play mode");
			LogAssert.IsTrue(details.Contains("ExitingPlayMode"), "every terrain's detail distance is put back when play mode ends");
			LogAssert.IsTrue(details.Contains("detailObjectDistance = d.Distance"), "the detail distance is restored on release");
		}
	}
}
