using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System;
using System.Text.RegularExpressions;
using FishMMO.Client;
using NUnit.Framework;
using Unity.Collections;
using UnityEditor;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;
using Object = UnityEngine.Object;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The instanced terrain detail renderer and the GPU-driven path shared with the trees: deterministic
	/// placement, count calibration against Unity's own detail transforms, the chunk lifecycle, the GPU
	/// buffer layouts, slot layout and overflow guard, the CPU mirror of the cull kernel, the range
	/// allocator, the path choice, and the detail-distance seam.
	/// </summary>
	public class TerrainDetailInstancingTests
	{
		private const string CovViaductTile = "Assets/Scenes/WorldScene/Arthis/Cov Viaduct Terrain/Cov Viaduct 1_0.asset";

		private static void Near(float expected, float actual, float tolerance, string message)
		{
			LogAssert.IsTrue(Mathf.Abs(expected - actual) <= tolerance, $"{message}: expected {expected}, got {actual}");
		}

		// ── Deterministic placement ───────────────────────────────────

		[Test]
		public void Hash_IsAFunctionOfWorldCellPrototypeSeedAndInstance()
		{
			uint a = TerrainDetailMath.Hash(120, -37, 3, 991, 2);
			LogAssert.AreEqual(a, TerrainDetailMath.Hash(120, -37, 3, 991, 2), "same inputs, same hash");
			LogAssert.AreNotEqual(a, TerrainDetailMath.Hash(121, -37, 3, 991, 2), "the next cell differs");
			LogAssert.AreNotEqual(a, TerrainDetailMath.Hash(120, -37, 4, 991, 2), "another prototype differs");
			LogAssert.AreNotEqual(a, TerrainDetailMath.Hash(120, -37, 3, 992, 2), "another seed differs");
			LogAssert.AreNotEqual(a, TerrainDetailMath.Hash(120, -37, 3, 991, 3), "the next instance differs");
			// The world cell, not the tile's: two tiles' views of one world point agree.
			LogAssert.AreEqual(TerrainDetailMath.WorldCell(1083.3335f + 0.5f, 1f), TerrainDetailMath.WorldCell(1083.8335f, 1f), "world cell index");
			LogAssert.AreEqual(-1, TerrainDetailMath.WorldCell(-0.25f, 1f), "negative coordinates floor");
		}

		[Test]
		public void Placement_StaysInItsCell_AndSizesFollowTheNoise()
		{
			for (int i = 0; i < 2000; i++)
			{
				uint h = TerrainDetailMath.Hash(i, i * 7, 1, 5, 1);
				Vector2 p = TerrainDetailMath.InCell(h, 1f);
				LogAssert.IsTrue(p.x >= 0f && p.x < 1f && p.y >= 0f && p.y < 1f, $"inside the cell: {p}");
				float yaw = TerrainDetailMath.Yaw(h);
				LogAssert.IsTrue(yaw >= 0f && yaw < 360f, $"yaw {yaw}");
			}
			Vector2 centre = TerrainDetailMath.InCell(TerrainDetailMath.Hash(1, 2, 3, 4, 5), 0f);
			Near(0.5f, centre.x, 1e-6f, "no jitter: the centre");
			Near(0.5f, centre.y, 1e-6f, "no jitter: the centre");
			float n0 = TerrainDetailMath.Noise(10.2f, 4.1f, 9), n1 = TerrainDetailMath.Noise(10.21f, 4.1f, 9);
			LogAssert.IsTrue(n0 >= 0f && n0 <= 1f, "noise in [0, 1]");
			LogAssert.IsTrue(Mathf.Abs(n0 - n1) < 0.05f, "noise is smooth: neighbours share a size");
			Vector2 size = TerrainDetailMath.Size(0.5f, 0.8f, 1.2f, 0.5f, 1.5f);
			Near(1f, size.x, 1e-6f, "width halfway");
			Near(1f, size.y, 1e-6f, "height halfway");
		}

		[Test]
		public void Counts_AreUnitysCoverageFormula_WithAnExactExpectedValue()
		{
			// Coverage mode: value/255 × coverage-per-m² × area × density.
			Near(2f * 1f * 0.5f, TerrainDetailMath.ExpectedInCell(255, true, 2f, 1f, 0.5f), 1e-5f, "full cover, half density");
			Near(128f / 255f * 4f, TerrainDetailMath.ExpectedInCell(128, true, 4f, 1f, 1f), 1e-5f, "half cover");
			Near(3f * 0.5f, TerrainDetailMath.ExpectedInCell(3, false, 99f, 1f, 0.5f), 1e-5f, "instance-count mode: the value");
			LogAssert.AreEqual(0f, TerrainDetailMath.ExpectedInCell(0, true, 4f, 1f, 1f), "empty cell");
			LogAssert.AreEqual((float)TerrainDetailMath.MaxPerCell, TerrainDetailMath.ExpectedInCell(255, false, 0f, 1f, 1f), "clamped per cell");

			// The coin on the fraction makes the mean exact.
			const float expected = 2.3f;
			long total = 0;
			const int cells = 20000;
			for (int c = 0; c < cells; c++)
			{
				int n = TerrainDetailMath.CountInCell(expected, TerrainDetailMath.Hash(c, -c, 0, 1, 0));
				LogAssert.IsTrue(n == 2 || n == 3, $"cell {c}: {n}");
				total += n;
			}
			Near(expected, total / (float)cells, 0.02f, "mean count per cell");
		}

		// ── Against Unity's own detail data (Cov Viaduct) ─────────────

		private static TerrainData LoadTile()
		{
			var data = AssetDatabase.LoadAssetAtPath<TerrainData>(CovViaductTile);
			if (data == null || data.detailPrototypes.Length == 0)
			{
				Assert.Ignore($"{CovViaductTile} is not here or has no details.");
			}
			return data;
		}

		private static TerrainDetailModel[] BuildModels(TerrainData data)
		{
			DetailPrototype[] prototypes = data.detailPrototypes;
			var models = new TerrainDetailModel[prototypes.Length];
			for (int i = 0; i < prototypes.Length; i++)
			{
				models[i] = TerrainDetailModel.Build(prototypes[i], 0, out string reason);
				if (models[i] == null)
				{
					foreach (TerrainDetailModel m in models)
					{
						m?.Dispose();
					}
					Assert.Ignore($"prototype {i} cannot be drawn here ({reason}); the generated art may not be on this machine.");
				}
			}
			return models;
		}

		[Test]
		public void Calibration_CountsAreNearUnitysOwnDetailTransforms()
		{
			TerrainData data = LoadTile();
			GameObject go = Terrain.CreateTerrainGameObject(data);
			TerrainDetailModel[] models = BuildModels(data);
			TerrainDetailField field = null;
			try
			{
				Terrain terrain = go.GetComponent<Terrain>();
				field = TerrainDetailField.Create(terrain, models, 1f);
				field.SetPrototypes(data.detailPrototypes);
				int perPatch = data.detailResolutionPerPatch;
				// A window of whole patches that is also whole chunks (64 × 64 cells), where the most prototypes grow.
				int cells = Mathf.Min(64, data.detailResolution);
				int patches = cells / perPatch;
				int bestX = 0, bestZ = 0, bestLayers = -1;
				for (int z = 0; z + cells <= data.detailResolution; z += cells)
				{
					for (int x = 0; x + cells <= data.detailResolution; x += cells)
					{
						int layers = data.GetSupportedLayers(x, z, cells, cells).Length;
						if (layers > bestLayers)
						{
							bestLayers = layers;
							bestX = x;
							bestZ = z;
						}
					}
				}
				int patchX0 = bestX / perPatch, patchZ0 = bestZ / perPatch;
				long unity = 0;
				var unityPer = new long[models.Length];
				for (int p = 0; p < models.Length; p++)
				{
					for (int py = patchZ0; py < patchZ0 + patches; py++)
					{
						for (int px = patchX0; px < patchX0 + patches; px++)
						{
							int n = data.ComputeDetailInstanceTransforms(px, py, p, 1f, out Bounds _).Length;
							unityPer[p] += n;
							unity += n;
						}
					}
				}
				long ours = 0;
				int chunkSpan = Mathf.Max(1, cells / field.ChunkCells);
				int chunkX0 = bestX / field.ChunkCells, chunkZ0 = bestZ / field.ChunkCells;
				for (int cz = chunkZ0; cz < chunkZ0 + chunkSpan; cz++)
				{
					for (int cx = chunkX0; cx < chunkX0 + chunkSpan; cx++)
					{
						ours += field.Build(cz * field.ChunksX + cx);
					}
				}
				string per = string.Join(", ", Enumerable.Range(0, models.Length).Where(p => unityPer[p] > 0).Select(p => $"{models[p].Prefab.name}: Unity {unityPer[p]}"));
				Debug.Log($"[Detail calibration] {CovViaductTile} {cells}² cells at ({bestX}, {bestZ}), {bestLayers} prototypes: Unity {unity}, ours {ours} (ratio {(unity > 0 ? ours / (double)unity : 0):F3}); {per}");
				if (unity == 0)
				{
					Assert.Ignore("no details in the calibration window");
				}
				double ratio = ours / (double)unity;
				LogAssert.IsTrue(ratio > 0.67 && ratio < 1.5, $"our count is within ×1.5 of Unity's: {ours} vs {unity} (ratio {ratio:F3})");
			}
			finally
			{
				field?.Dispose();
				TerrainDetailField.DisposePool();
				foreach (TerrainDetailModel m in models)
				{
					m.Dispose();
				}
				Object.DestroyImmediate(go);
			}
		}

		[Test]
		public void ChunkLifecycle_WantedChunksAreQueuedNearestFirst_BuiltIdentically_AndReleasedWhenStale()
		{
			TerrainData data = LoadTile();
			GameObject go = Terrain.CreateTerrainGameObject(data);
			TerrainDetailModel[] models = BuildModels(data);
			TerrainDetailField field = null;
			try
			{
				Terrain terrain = go.GetComponent<Terrain>();
				field = TerrainDetailField.Create(terrain, models, 1f);
				field.SetPrototypes(data.detailPrototypes);
				Vector3 centre = terrain.GetPosition() + new Vector3(data.size.x * 0.5f, 50f, data.size.z * 0.5f);
				Matrix4x4 worldToCamera = Matrix4x4.Scale(new Vector3(1f, 1f, -1f)) * Matrix4x4.TRS(centre, Quaternion.identity, Vector3.one).inverse;
				var view = new TerrainTreeField.View
				{
					Position = centre,
					Planes = GeometryUtility.CalculateFrustumPlanes(Matrix4x4.Perspective(60f, 1.5f, 0.3f, 2000f) * worldToCamera),
					ScreenFactor = TerrainTreeMath.ScreenFactor(60f, false, 0f, 1f),
				};
				var missing = new List<TerrainDetailField.Missing>();
				LogAssert.AreEqual(0, field.Gather(in view, 120f, 1, missing), "nothing built yet: nothing gathered");
				LogAssert.IsTrue(missing.Count > 0, "the chunks in reach are wanted");
				float reachChunks = Mathf.Ceil(240f / (field.ChunkCells * data.size.x / data.detailResolution)) + 1;
				LogAssert.IsTrue(missing.Count <= reachChunks * reachChunks, $"only chunks within the detail distance ({missing.Count})");
				int again = missing.Count;
				field.Gather(in view, 120f, 1, missing);
				LogAssert.AreEqual(again, missing.Count, "queued once per frame, however many cameras ask");

				foreach (TerrainDetailField.Missing m in missing)
				{
					field.Build(m.Index);
				}
				LogAssert.AreEqual(missing.Count, field.BuiltCount, "every wanted chunk built");

				// Determinism: release and rebuild one chunk; the same instances.
				int index = missing.OrderByDescending(m => field.ChunkAt(m.Index).Count).First().Index;
				TerrainDetailField.Chunk chunk = field.ChunkAt(index);
				FishInstance[] first = chunk.Instances.GetSubArray(0, chunk.Count).ToArray();
				if (first.Length == 0)
				{
					Assert.Ignore("no plants within reach of the tile's centre to compare");
				}
				field.ReleaseStale(int.MaxValue);
				LogAssert.AreEqual(0, field.BuiltCount, "stale chunks go back to the pool");
				field.Build(index);
				TerrainDetailField.Chunk rebuilt = field.ChunkAt(index);
				LogAssert.AreEqual(first.Length, rebuilt.Count, "the same count after a rebuild");
				for (int i = 0; i < first.Length; i++)
				{
					LogAssert.IsTrue(first[i].Row0 == rebuilt.Instances[i].Row0 && first[i].Row1 == rebuilt.Instances[i].Row1 && first[i].Row2 == rebuilt.Instances[i].Row2,
						$"instance {i} identical after a rebuild");
				}
				// Every instance stands on the ground.
				for (int i = 0; i < rebuilt.Count; i += Mathf.Max(1, rebuilt.Count / 50))
				{
					Vector3 p = rebuilt.Instances[i].Position;
					Near(terrain.SampleHeight(p) + terrain.GetPosition().y, p.y, 0.25f, $"instance {i} on the ground");
				}

				// Gathered into the CPU buckets once built (the camera looks down +z from the centre).
				missing.Clear();
				int gathered = field.Gather(in view, 120f, 2, missing);
				LogAssert.IsTrue(gathered >= 0, "gathers");
				LogAssert.AreEqual(gathered, models.Sum(m => m.Count), "every gathered instance is in a bucket");
				field.ReleaseStale(3);
				LogAssert.AreEqual(0, field.BuiltCount, "released when no camera wanted them since");
			}
			finally
			{
				field?.Dispose();
				TerrainDetailField.DisposePool();
				foreach (TerrainDetailModel m in models)
				{
					m.Dispose();
				}
				Object.DestroyImmediate(go);
			}
		}

		// ── The GPU path ──────────────────────────────────────────────

		[Test]
		public void BufferLayouts_MatchTheShaders()
		{
			LogAssert.AreEqual(48, Marshal.SizeOf<FishInstance>(), "FishInstance: 3 × float4");
			LogAssert.AreEqual(FishInstance.Stride, Marshal.SizeOf<FishInstance>(), "its stride constant");
			LogAssert.AreEqual(8, Marshal.SizeOf<FishVisible>(), "FishVisible: uint + float");
			LogAssert.AreEqual(FishVisible.Stride, Marshal.SizeOf<FishVisible>(), "its stride constant");
			LogAssert.AreEqual(FishModelData.Stride, Marshal.SizeOf<FishModelData>(), "FishModel");
			LogAssert.AreEqual(FishWork.Stride, Marshal.SizeOf<FishWork>(), "FishWork");
			LogAssert.AreEqual(20, GraphicsBuffer.IndirectDrawIndexedArgs.size, "indexed args: 5 uints");

			string compute = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Assets/Resources/FishTerrainInstancing.compute"));
			LogAssert.IsTrue(compute.Contains("struct FishInstance { float4 row0; float4 row1; float4 row2; };"), "the kernel's FishInstance");
			LogAssert.IsTrue(compute.Contains("struct FishVisible { uint index; float lodFade; };"), "the kernel's FishVisible");
			LogAssert.IsTrue(compute.Contains("(id.x * 5 + 1) * 4"), "the finaliser writes instanceCount, the second uint of each command");
			LogAssert.IsFalse(Regex.IsMatch(compute, @"\bWave[A-Z]"), "no wave intrinsics (WebGPU)");
			LogAssert.IsTrue(compute.Contains("if (at < s.y)"), "the append is guarded by the slot capacity");
			LogAssert.IsTrue(compute.Contains("min(count, _FishSlots[slot].y)"), "the command count is clamped to the capacity");

			var m = Matrix4x4.TRS(new Vector3(3f, 4f, 5f), Quaternion.Euler(0f, 37f, 0f), new Vector3(1.5f, 2f, 1.5f));
			Matrix4x4 back = FishInstance.From(m).ToMatrix();
			for (int i = 0; i < 16; i++)
			{
				Near(m[i], back[i], 1e-6f, $"round trip element {i}");
			}
		}

		[Test]
		public void Slots_AreLaidOutByPrefixSum_AndNeverOverflow()
		{
			var capacities = new[] { 10, 0, 7, 3 };
			var bases = new int[4];
			LogAssert.AreEqual(20, TerrainGpuMath.LayoutSlots(capacities, bases), "total");
			LogAssert.AreEqual("0,10,10,17", string.Join(",", bases), "bases");
			LogAssert.AreEqual(7, TerrainGpuMath.Slot(4, 1, 1), "slot = base + level × 2 + view");

			uint counter = 0;
			var written = new List<int>();
			for (int i = 0; i < 5; i++)
			{
				int at = TerrainGpuMath.Append(ref counter, 3);
				if (at >= 0)
				{
					written.Add(at);
				}
			}
			LogAssert.AreEqual("0,1,2", string.Join(",", written), "appends past the capacity are dropped");
			LogAssert.AreEqual(5u, counter, "the counter still counts them");
			LogAssert.AreEqual(3u, TerrainGpuMath.Finalize(counter, 3), "the command count is clamped to the capacity");
			LogAssert.AreEqual(2u, TerrainGpuMath.Finalize(2, 3), "below the capacity it is the count");
		}

		[Test]
		public void PathChoice_FallsBackWithoutComputeOrVertexBuffers()
		{
			LogAssert.IsTrue(TerrainGpuMath.ChooseGpu(true, 4, true), "Vulkan/D3D/Metal/WebGPU: GPU-driven");
			LogAssert.IsFalse(TerrainGpuMath.ChooseGpu(false, 0, true), "WebGL2: no compute → fallback");
			LogAssert.IsFalse(TerrainGpuMath.ChooseGpu(true, 0, true), "no vertex-stage structured buffers → fallback");
			LogAssert.IsFalse(TerrainGpuMath.ChooseGpu(true, 8, false), "compute asset missing → fallback");
			LogAssert.IsNotNull(UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(TerrainGpuRenderer.ComputeAssetPath), "the compute exists where the render profile points");
			// Client-only: referenced by WeatherRenderProfile, never from a Resources folder (which ships to servers).
			LogAssert.IsNull(Resources.Load<ComputeShader>("FishTerrainInstancing"), "the compute must not be in Resources");
		}

		private static Plane[] ForwardPlanes()
		{
			Matrix4x4 worldToCamera = Matrix4x4.Scale(new Vector3(1f, 1f, -1f));
			return GeometryUtility.CalculateFrustumPlanes(Matrix4x4.Perspective(60f, 1f, 0.3f, 1000f) * worldToCamera);
		}

		private static FishModelData TreeModel()
		{
			float[] t = { 0.25f, 0.08f, 0.002f };
			var data = new FishModelData
			{
				LocalReference = new Vector3(0f, 2f, 0f),
				Size = 4f,
				SphereCentre = new Vector3(0f, 2f, 0f),
				SphereRadius = 2.5f,
				LevelCount = 3,
				ShadowMask = 0b011,
			};
			for (int l = 0; l < 3; l++)
			{
				data.Transitions[l] = t[l];
				data.FadeWidths[l] = TerrainTreeMath.FadeWidth(t, l, 0f);
			}
			return data;
		}

		[Test]
		public void CullMirror_PicksLevelsAndFadesLikeTheCpu_AndSeparatesTheShadowView()
		{
			FishModelData model = TreeModel();
			Plane[] planes = ForwardPlanes();
			var emits = new List<TerrainGpuMath.Emit>();
			var lod = new Vector3(TerrainTreeMath.ScreenFactor(60f, false, 0f, 1f), 0f, 0f);
			var eye = new Vector3(0f, 2f, 0f);
			var light = new Vector4(0f, -0.5f, 0.866f, 50f);

			// Near and on screen: LOD0 in the camera view and the shadow view.
			TerrainGpuMath.Cull(Matrix4x4.Translate(new Vector3(0f, 0f, 10f)), in model, 1000f, 1f, eye, planes, light, lod, emits);
			LogAssert.AreEqual(2, emits.Count, "camera + shadow");
			LogAssert.IsTrue(emits[0].Level == 0 && emits[0].View == 0 && emits[0].Fade == 0f, "LOD0 whole in the camera view");
			LogAssert.IsTrue(emits[1].Level == 0 && emits[1].View == 1, "and in the shadow view");

			// In the LOD0→1 band (rh 0.2625): both levels, complementary, in both views.
			float d = 4f * lod.x / 0.2625f;
			TerrainGpuMath.Cull(Matrix4x4.Translate(new Vector3(0f, 0f, d)), in model, 1000f, 1f, eye, planes, light, lod, emits);
			LogAssert.AreEqual(4, emits.Count, "two levels × two views");
			LogAssert.IsTrue(emits[0].Fade < 0f && emits[1].Fade == -emits[0].Fade && emits[0].Level == 0 && emits[1].Level == 1, "camera view: −f / +f");
			LogAssert.IsTrue(emits[2].View == 1 && emits[3].View == 1 && emits[3].Fade == -emits[2].Fade, "shadow view: the same partition");

			// Far: the billboard, which does not cast, so the camera view only.
			TerrainGpuMath.Cull(Matrix4x4.Translate(new Vector3(0f, 0f, 400f)), in model, 1000f, 1f, eye, planes, light, lod, emits);
			LogAssert.AreEqual(1, emits.Count, "billboard: camera only");
			LogAssert.AreEqual(2, emits[0].Level, "the billboard");

			// Behind the camera with the sun behind it: its shadow falls ahead, so the shadow view only.
			TerrainGpuMath.Cull(Matrix4x4.Translate(new Vector3(0f, 0f, -3f)), in model, 1000f, 1f, eye, planes, light, lod, emits);
			LogAssert.IsTrue(emits.All(e => e.View == 1), "off screen: shadow casters only");
			LogAssert.IsTrue(emits.Count > 0, "but still a caster");

			// Past the draw distance: nothing.
			TerrainGpuMath.Cull(Matrix4x4.Translate(new Vector3(0f, 0f, 300f)), in model, 200f, 1f, eye, planes, light, lod, emits);
			LogAssert.AreEqual(0, emits.Count, "beyond the draw distance");
		}

		[Test]
		public void LevelMask_CoversEveryLevelARunCanReach()
		{
			float[] t = { 0.25f, 0.08f, 0.002f };
			float[] w = { TerrainTreeMath.FadeWidth(t, 0, 0f), TerrainTreeMath.FadeWidth(t, 1, 0f), TerrainTreeMath.FadeWidth(t, 2, 0f) };
			LogAssert.AreEqual(0b001, TerrainGpuMath.LevelMask(0.5f, 0.3f, t, w, 0), "all LOD0");
			LogAssert.AreEqual(0b011, TerrainGpuMath.LevelMask(0.5f, 0.26f, t, w, 0), "reaching into LOD0's band: LOD1 too");
			LogAssert.AreEqual(0b111, TerrainGpuMath.LevelMask(0.5f, 0.001f, t, w, 0), "near to culled: every level");
			LogAssert.AreEqual(0, TerrainGpuMath.LevelMask(0.001f, 0.0005f, t, w, 0), "wholly culled");
			LogAssert.AreEqual(1, TerrainGpuMath.WorkItems(64), "64 per work item");
			LogAssert.AreEqual(2, TerrainGpuMath.WorkItems(65), "65 → two");
			LogAssert.AreEqual(new Vector2Int(65535, 2), TerrainGpuMath.CullGroups(70000), "groups wrap into y past 65535");
		}

		[Test]
		public void CullGroups_DecodeEveryWorkItemExactlyOnce()
		{
			foreach (int items in new[] { 1, 63, 64, 65534, 65535, 65536, 140000 })
			{
				Vector2Int groups = TerrainGpuMath.CullGroups(items);
				var seen = new bool[items];
				int covered = 0;
				for (int y = 0; y < groups.y; y++)
				{
					for (int x = 0; x < groups.x; x++)
					{
						int item = x + y * 65535; // FishCull: group.x + group.y * 65535
						if (item >= items)
						{
							continue; // the kernel returns for these
						}
						LogAssert.IsFalse(seen[item], $"{items} items: item {item} decoded twice");
						seen[item] = true;
						covered++;
					}
				}
				LogAssert.AreEqual(items, covered, $"{items} items: every work item decoded");
				LogAssert.IsTrue(groups.x <= 65535 && groups.y <= 65535, "within the dispatch limits");
			}
		}

		[Test]
		public void IndirectArgs_InstanceCountIsTheSecondUintOfEachCommand()
		{
			// GraphicsBuffer.IndirectDrawIndexedArgs: indexCountPerInstance, instanceCount, startIndex, baseVertexIndex, startInstance.
			var args = new GraphicsBuffer.IndirectDrawIndexedArgs[] { new GraphicsBuffer.IndirectDrawIndexedArgs { instanceCount = 7 }, new GraphicsBuffer.IndirectDrawIndexedArgs { instanceCount = 9 } };
			uint[] raw = MemoryMarshal.Cast<GraphicsBuffer.IndirectDrawIndexedArgs, uint>(args).ToArray();
			LogAssert.AreEqual(10, raw.Length, "5 uints per command");
			LogAssert.AreEqual(7u, raw[0 * 5 + 1], "command 0's instanceCount at uint 1 (byte (0·5+1)·4)");
			LogAssert.AreEqual(9u, raw[1 * 5 + 1], "command 1's instanceCount at uint 6");
		}

		[Test]
		public void VisibleBase_IsFedFloatBacked_LikeUrpsOwnUintUniforms()
		{
			string code = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Assets/Scripts/Client/World/Terrain/TerrainGpuRenderer.cs"));
			LogAssert.IsTrue(code.Contains("SetFloat(VisibleBaseId"), "the per-draw base is set float-backed");
			LogAssert.IsFalse(code.Contains("SetInteger(VisibleBaseId"), "not as a real-int property (not known to reach a uint parameter on GLCore)");
			LogAssert.IsTrue((float)(1 << 24) == (float)((1 << 24) - 1) + 1f, "floats hold every base up to 2^24 exactly");
		}

		[Test]
		public void GpuCulling_IsRecordedPerCamera_OnItsOwnComputeInstance_AndReflectionProbesTakeTheCpuPath()
		{
			string root = Directory.GetCurrentDirectory();
			string renderer = File.ReadAllText(Path.Combine(root, "Assets/Scripts/Client/World/Terrain/TerrainGpuRenderer.cs"));
			LogAssert.IsTrue(renderer.Contains("Object.Instantiate(cs)"), "each renderer culls with its own ComputeShader instance (one parameter sheet each)");
			LogAssert.IsFalse(Regex.IsMatch(renderer, @"compute\.(Dispatch|SetVector|SetInt|SetBuffer|SetVectorArray)\("), "no immediate-mode compute parameters or dispatches");
			LogAssert.IsTrue(renderer.Contains("cmd.DispatchCompute(compute, cullKernel"), "the cull is recorded into a command buffer");
			LogAssert.IsTrue(renderer.Contains("context.ExecuteCommandBuffer(cmd)"), "executed on the camera's own render context");
			LogAssert.IsTrue(renderer.Contains("cmd.SetBufferData(workBuffer"), "the work list is uploaded in the same command stream");
			LogAssert.IsFalse(renderer.Contains("unity_BaseInstanceID"), "Unity's built-in base instance is left to Unity");
			foreach (string system in new[] { "TerrainTreeInstancing.cs", "TerrainDetailInstancing.cs" })
			{
				string code = File.ReadAllText(Path.Combine(root, "Assets/Scripts/Client/World/Terrain", system));
				LogAssert.IsTrue(code.Contains("camera.cameraType == CameraType.Reflection ? null : gpu"), $"{system}: reflection-probe faces take the CPU path");
			}
		}

		[Test]
		public void CommandBases_MapEachCommandIndexToItsSlotsBase()
		{
			// Slots laid out by capacity; commands (one per draw call, index = startCommand) each draw one slot.
			var capacities = new[] { 10, 4, 0, 7 };
			var bases = new int[4];
			TerrainGpuMath.LayoutSlots(capacities, bases);
			var commandSlots = new List<uint> { 0, 0, 1, 3, 3, 2 }; // bark+leaves share slot 0, …
			uint[] commandBases = TerrainGpuMath.CommandBases(commandSlots, bases);
			LogAssert.AreEqual("0,0,10,14,14,14", string.Join(",", commandBases), "command c → base of the slot it draws");
			LogAssert.AreEqual(1, TerrainGpuMath.CommandBases(new List<uint>(), bases).Length, "never an empty buffer");
			LogAssert.AreEqual("_FishCommandBases", TerrainGpuRenderer.CommandBasesName, "the name the indirect shaders read");
		}

		[Test]
		public void RangeAllocator_NeverHandsOutOverlappingRanges_UnderRandomAllocFreeGrow()
		{
			var random = new System.Random(11);
			var a = new RangeAllocator(0);
			var live = new List<Vector2Int>(); // x offset, y count
			for (int step = 0; step < 5000; step++)
			{
				if (live.Count > 0 && random.NextDouble() < 0.4)
				{
					int k = random.Next(live.Count);
					a.Free(live[k].x, live[k].y);
					live.RemoveAt(k);
					continue;
				}
				int count = 1 + random.Next(3000);
				int offset = a.Allocate(count);
				if (offset < 0)
				{
					// The renderer's growth: double, or enough for this one.
					a.Grow(Mathf.Max(a.Capacity * 2, a.Used + count + 1024));
					offset = a.Allocate(count);
				}
				LogAssert.IsTrue(offset >= 0 && offset + count <= a.Capacity, $"step {step}: inside the buffer");
				foreach (Vector2Int r in live)
				{
					LogAssert.IsFalse(offset < r.x + r.y && r.x < offset + count, $"step {step}: [{offset}, {offset + count}) overlaps live [{r.x}, {r.x + r.y})");
				}
				live.Add(new Vector2Int(offset, count));
				LogAssert.AreEqual(live.Sum(r => r.y), a.Used, $"step {step}: used count");
			}
		}

		[Test]
		public void InstanceUploads_GoThroughTheCpuMirror_AndReachTheGpuInTheCamerasCommandBuffer()
		{
			string code = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Assets/Scripts/Client/World/Terrain/TerrainGpuRenderer.cs"));
			LogAssert.IsFalse(code.Contains("instances.SetData("), "no immediate writes into the instance buffer");
			LogAssert.IsTrue(code.Contains("NativeArray<FishInstance>.Copy(source, start, mirror, offset, count)"), "every upload writes the CPU mirror");
			LogAssert.IsFalse(code.Contains("into.SetBufferData(instances,"), "never written by the CPU while in use (an OpenGL stall of 50-90 ms)");
			LogAssert.IsTrue(code.Contains("into.SetBufferData(upload, mirror, lo, packed, hi - lo)"), "changed spans go into a fresh upload buffer");
			LogAssert.IsTrue(code.Contains("into.DispatchCompute(compute, copyKernel,"), "and are copied into the instance buffer on the GPU");
			LogAssert.IsTrue(code.Contains("Flush(cmd);"), "flushed in the camera's command buffer, ahead of the culling");
			LogAssert.IsFalse(code.Contains("MarkDirty(0, mirror.Length)"), "a recreated buffer does not upload the whole mirror (free space included)");
			LogAssert.IsTrue(code.Contains("MarkDirty(r.Key, r.Value.Count)"), "a recreated buffer receives every resident range");
			LogAssert.IsTrue(code.Contains("BindBlocks();\n\t\t\tdirty.Clear();") || code.Replace("\r\n", "\n").Contains("BindBlocks();\n\t\t\tdirty.Clear();"), "created and bound before anything is written into it");
		}

		[Test]
		public void SlotCapacity_NeverBelowTheWorkSubmitted_SoNoWholeChunkIsDropped()
		{
			string code = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "Assets/Scripts/Client/World/Terrain/TerrainGpuRenderer.cs"));
			LogAssert.IsTrue(code.Contains("m.WorkInstances += count;"), "AddWork counts the instances it submits per model");
			LogAssert.IsTrue(code.Contains("if (m.WorkInstances > m.Capacity)"), "Execute grows a slot that the work could overflow");
			int grow = code.IndexOf("if (m.WorkInstances > m.Capacity)");
			int relayout = code.IndexOf("Relayout();", grow);
			int cull = code.IndexOf("DispatchCompute(compute, cullKernel", grow);
			LogAssert.IsTrue(grow >= 0 && relayout > grow && cull > relayout, "grown and laid out before the culling runs");
		}

		[Test]
		public void DirtySpans_MergeOnlyNearNeighbours()
		{
			var spans = new List<Vector2Int>();
			TerrainGpuMath.AddDirty(spans, 0, 100, 512);
			TerrainGpuMath.AddDirty(spans, 40000, 40100, 512);
			LogAssert.AreEqual(2, spans.Count, "far-apart chunks stay two uploads, not one of everything between");
			TerrainGpuMath.AddDirty(spans, 300, 400, 512);
			LogAssert.AreEqual(2, spans.Count, "a near neighbour merges");
			LogAssert.AreEqual(new Vector2Int(0, 400), spans[0], "merged span");
			TerrainGpuMath.AddDirty(spans, 20000, 20010, 512);
			LogAssert.AreEqual(3, spans.Count, "a third far span");
			LogAssert.IsTrue(spans[0].x < spans[1].x && spans[1].x < spans[2].x, "kept sorted");
			TerrainGpuMath.AddDirty(spans, 50, 40050, 512);
			LogAssert.AreEqual(1, spans.Count, "a span covering all of them swallows them");
			LogAssert.AreEqual(new Vector2Int(0, 40100), spans[0], "the union");
			TerrainGpuMath.AddDirty(spans, 5, 5, 512);
			LogAssert.AreEqual(1, spans.Count, "an empty span changes nothing");
		}

		[Test]
		public void DiscBound_IsAnUpperBoundOnInstancesWithinTheDetailDistance()
		{
			// 1 m cells, 2.3 instances per m² at full cover: every cell holds at most 3.
			long bound = TerrainDetailMath.DiscBound(120f, 1f, 1f, 2.3f, 1f);
			double cells = Math.PI * Math.Pow(120 + 2 * Math.Sqrt(2), 2);
			LogAssert.IsTrue(bound >= (long)(cells * 3) && bound <= (long)(cells * 3) + 3, $"cells in the widened disc × 3 per cell ({bound})");
			// Brute force: every cell within reach fully covered, counted with the real per-cell coin.
			long brute = 0;
			for (int z = -130; z <= 130; z++)
			{
				for (int x = -130; x <= 130; x++)
				{
					if (new Vector2(x + 0.5f, z + 0.5f).magnitude > 120f + 1.5f)
					{
						continue;
					}
					brute += TerrainDetailMath.CountInCell(TerrainDetailMath.ExpectedInCell(255, true, 2.3f, 1f, 1f), TerrainDetailMath.Hash(x, z, 0, 1, 0));
				}
			}
			LogAssert.IsTrue(brute <= bound, $"no camera can see more than the bound ({brute} ≤ {bound})");
			LogAssert.AreEqual(0L, TerrainDetailMath.DiscBound(0f, 1f, 1f, 2f, 1f), "no distance, no instances");
		}

		[Test]
		public void RangeAllocator_AllocatesFirstFit_CoalescesFrees_AndGrows()
		{
			var a = new RangeAllocator(100);
			int x = a.Allocate(30), y = a.Allocate(30), z = a.Allocate(30);
			LogAssert.AreEqual("0,30,60", $"{x},{y},{z}", "first fit, in order");
			LogAssert.AreEqual(-1, a.Allocate(20), "only 10 left");
			a.Free(y, 30);
			LogAssert.AreEqual(30, a.Allocate(20), "reuses the hole");
			a.Free(30, 20);
			a.Free(x, 30);
			LogAssert.AreEqual(60, a.LargestFree(), "freed neighbours merge: 0..60");
			a.Grow(200);
			a.Free(z, 30);
			LogAssert.AreEqual(200, a.LargestFree(), "everything free merges with the grown tail");
			LogAssert.AreEqual(0, a.Used, "nothing used");
		}

		// ── The seam ──────────────────────────────────────────────────

		[Test]
		public void DetailDistanceOf_AnUndrivenTerrain_IsItsOwnDistance()
		{
			var go = new GameObject("Terrain_Test");
			try
			{
				var terrain = go.AddComponent<Terrain>();
				terrain.detailObjectDistance = 87f;
				LogAssert.IsFalse(TerrainDetailInstancing.IsDriving(terrain), "edit mode drives nothing");
				Near(87f, TerrainDetailInstancing.DetailDistanceOf(terrain), 1e-4f, "the terrain's own distance");
				LogAssert.AreEqual(0f, TerrainDetailInstancing.DetailDistanceOf(null), "null");
			}
			finally
			{
				Object.DestroyImmediate(go);
			}
		}
	}
}
