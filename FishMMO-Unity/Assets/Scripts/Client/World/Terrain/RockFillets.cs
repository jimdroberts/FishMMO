using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.Rendering;

namespace FishMMO.Client
{
	/// <summary>
	/// Rock seam fillets: where rocks meet, the seam between them is filled with a smooth surface of rock, so stacked
	/// boulders, cliff rocks and cliff sections read as one wall instead of a pile of separate pieces. Built and drawn on
	/// the GPU (FishRockFillets.compute, FishMMO/Rock Fillet) from the crevice field's distance fields and grid
	/// (<see cref="RockContactField"/>); the arithmetic is <see cref="RockFilletMath"/>'s.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Nothing about a rock changes.</b> Every rock keeps its own mesh, exactly as generated, and its own collider. The
	/// fillet is separate geometry, visual only, and stands at most <see cref="RockFilletMath.MaxOffset"/> (k/4 − inset)
	/// outside the rocks it joins, and only inside the concave crease between them.
	/// </para>
	/// <para>
	/// <b>Built once per place, not per frame.</b> Rocks never move, so a build covers a region round the camera
	/// (<see cref="WeatherRenderProfile.RockFilletDistance"/> plus <see cref="RegionMargin"/>) and is kept until the camera
	/// has moved <see cref="RebuildStep"/> from where it was built, the rocks change or the settings do. A build starts
	/// from a settled gather of the rock grid, copies its rock list (later gathers renumber it), lists the seam cells,
	/// meshes <see cref="WeatherRenderProfile.RockFilletCellsPerFrame"/> of them a frame, then groups the triangles by
	/// look into the drawn set in one frame, so what is drawn is always one whole build. Until a build finishes the last
	/// one stays drawn; it covers the camera's fade distance for <see cref="RegionMargin"/> − <see cref="RebuildStep"/>
	/// metres more of travel.
	/// </para>
	/// <para>
	/// <b>Drawn per look.</b> Each rock material (<see cref="RockContactField.RegisterLook"/>) gets a copy on the fillet
	/// shader and one Graphics.RenderPrimitivesIndirect a frame, casting and receiving shadows like the rocks; the
	/// triangles of a look are its run of the drawn set.
	/// </para>
	/// <para>
	/// Memory at the default 131,072 triangles: 2 × 8 MB of triangles, 0.9 MB of copied rock list, 0.4 MB of seam cells.
	/// A metre of seam takes about 180 triangles (the tests' wall on a ledge), the kept edge under the rocks included.
	/// Client, play mode, compute shaders only. Released with the crevice field (quit, script reload).
	/// </para>
	/// </remarks>
	public static class RockFillets
	{
		public const string ComputeAssetPath = "Assets/Prefabs/Client/Weather/Shaders/FishRockFillets.compute";
		public const string ShaderAssetPath = "Assets/Prefabs/Client/Weather/Shaders/FishRockFillet.shader";
		public const string ShaderName = "FishMMO/Rock Fillet";

		/// <summary>Looks drawn (FISH_FILLET_LOOKS): one indirect draw each. Rocks of a later look get no fillets (and a warning).</summary>
		public const int MaxLooks = 64;

		/// <summary>Seam cells one build lists; more are dropped (and warned about).</summary>
		public const int CellCapacity = 8192;

		/// <summary>Metres the camera moves from where the drawn set was built before the next build starts.</summary>
		public const float RebuildStep = 6f;

		/// <summary>Metres a build reaches past the fade distance: the drawn set covers the camera's fade sphere while it moves this less <see cref="RebuildStep"/> more, during the next build.</summary>
		public const float RegionMargin = 12f;

		private const int TriangleStride = 64;
		private const int CellStride = 48;
		private const int RockStride = 7 * 16;
		private const int Counters = 4 + 2 * MaxLooks;

		/// <summary>The build's GPU time, as a marker the profiler's GPU module and ScenePerfProbe see.</summary>
		private static readonly CustomSampler BuildSample = CustomSampler.Create("FishMMO rock fillets (build)", true);

		private static ComputeShader compute;
		private static int clearKernel = -1, snapshotKernel, classifyKernel, meshKernel, finalizeKernel, scatterKernel;
		private static Shader shader;
		private static GraphicsBuffer counters, snapshot, cells, triangles, drawn, args, starts;
		private static int triangleCapacity;
		private static readonly List<Material> materials = new List<Material>();
		private static readonly CommandBuffer cmd = new CommandBuffer { name = "FishMMO rock fillets" };

		// The profile's settings.
		private static float width = 1f, share = 0.5f, inset = 0.06f, fadeDistance = 36f;
		private static int cellsPerFrame = 192, triangleBudget = 131072;

		// The build under way.
		private static bool building;
		private static int buildId;
		private static int nextCell;
		private static int knownCells = -1;
		private static Vector3 buildCentre;
		private static Bounds buildBounds;
		private static int buildKey, buildSettings;
		private static Vector4 buildGrid;
		private static Texture buildAtlas;

		// The drawn set.
		private static bool shown;
		private static Vector3 shownCentre;
		private static Bounds shownBounds;
		private static int shownKey, shownSettings;
		private static readonly uint[] lookCounts = new uint[MaxLooks];
		private static bool lookCountsKnown;
		private static bool warnedCells, warnedTriangles, warnedLooks, reported;

		/// <summary>The last finished build, for diagnostics.</summary>
		public static string LastReport { get; private set; } = "[Rock fillets] none built";

		private static readonly int CountersId = Shader.PropertyToID("_FilletCounters");
		private static readonly int RocksId = Shader.PropertyToID("_FilletRocks");
		private static readonly int RocksInId = Shader.PropertyToID("_FilletRocksIn");
		private static readonly int SlotsId = Shader.PropertyToID("_FilletSlots");
		private static readonly int CellsId = Shader.PropertyToID("_FilletCells");
		private static readonly int CellsInId = Shader.PropertyToID("_FilletCellsIn");
		private static readonly int TrisId = Shader.PropertyToID("_FilletTris");
		private static readonly int TrisInId = Shader.PropertyToID("_FilletTrisIn");
		private static readonly int DrawnId = Shader.PropertyToID("_FilletDrawn");
		private static readonly int ArgsId = Shader.PropertyToID("_FilletArgs");
		private static readonly int StartsId = Shader.PropertyToID("_FilletStarts");
		private static readonly int StartsInId = Shader.PropertyToID("_FilletStartsIn");
		private static readonly int SdfId = Shader.PropertyToID("_FilletSdf");
		private static readonly int GridId = Shader.PropertyToID("_FilletGrid");
		private static readonly int GridDimsId = Shader.PropertyToID("_FilletGridDims");
		private static readonly int RegionMinId = Shader.PropertyToID("_FilletRegionMin");
		private static readonly int RegionDimsId = Shader.PropertyToID("_FilletRegionDims");
		private static readonly int AtlasId = Shader.PropertyToID("_FilletAtlas");
		private static readonly int ShapeId = Shader.PropertyToID("_FilletShape");
		private static readonly int LimitsId = Shader.PropertyToID("_FilletLimits");

		private static readonly int DrawTrisId = Shader.PropertyToID("_FishFilletTris");
		private static readonly int DrawStartsId = Shader.PropertyToID("_FishFilletStarts");
		private static readonly int FadeId = Shader.PropertyToID("_FishFilletFade");
		private static readonly int MapsId = Shader.PropertyToID("_FishFilletMaps");
		private static readonly int ContactOnId = Shader.PropertyToID("_FishContactOn");

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			building = shown = false;
			lookCountsKnown = false;
			warnedCells = warnedTriangles = warnedLooks = reported = false;
			materials.Clear();
			compute = null;
			shader = null;
			clearKernel = -1;
		}

		/// <summary>Whether the profile asks for fillets at this quality level on this device.</summary>
		public static bool Wanted(WeatherRenderProfile profile)
		{
			return profile != null && profile.RockFillets && SystemInfo.supportsComputeShaders
				&& QualitySettings.GetQualityLevel() >= profile.RockFilletMinQuality;
		}

		private static void ReadProfile()
		{
			WeatherRenderProfile p = WeatherRenderProfile.Active;
			if (p == null)
			{
				return;
			}
			width = Mathf.Clamp(p.RockFilletWidth, 0.05f, RockFilletMath.MaxWidth);
			share = Mathf.Clamp(p.RockFilletSizeShare, 0.05f, 1f);
			inset = Mathf.Clamp(p.RockFilletInset, 0.005f, width * 0.25f);
			fadeDistance = Mathf.Max(4f, p.RockFilletDistance);
			cellsPerFrame = Mathf.Max(1, p.RockFilletCellsPerFrame);
			triangleBudget = Mathf.Max(1024, p.RockFilletTriangles);
		}

		/// <summary>A key of every setting a build depends on: a change rebuilds.</summary>
		private static int SettingsKey()
		{
			unchecked
			{
				int h = 17;
				h = h * 31 + width.GetHashCode();
				h = h * 31 + share.GetHashCode();
				h = h * 31 + inset.GetHashCode();
				h = h * 31 + fadeDistance.GetHashCode();
				h = h * 31 + triangleBudget;
				return h;
			}
		}

		private static bool EnsureResources()
		{
			if (compute == null)
			{
				WeatherRenderProfile profile = WeatherRenderProfile.Active;
				ComputeShader cs = profile != null ? profile.RockFilletsCompute : null;
#if UNITY_EDITOR
				if (cs == null)
				{
					cs = UnityEditor.AssetDatabase.LoadAssetAtPath<ComputeShader>(ComputeAssetPath);
				}
#endif
				if (cs == null)
				{
					return false;
				}
				compute = Object.Instantiate(cs);
				compute.hideFlags = HideFlags.DontSave;
				clearKernel = compute.FindKernel("FishFilletClear");
				snapshotKernel = compute.FindKernel("FishFilletSnapshot");
				classifyKernel = compute.FindKernel("FishFilletClassify");
				meshKernel = compute.FindKernel("FishFilletMesh");
				finalizeKernel = compute.FindKernel("FishFilletFinalize");
				scatterKernel = compute.FindKernel("FishFilletScatter");
			}
			if (shader == null)
			{
				WeatherRenderProfile profile = WeatherRenderProfile.Active;
				Shader s = profile != null ? profile.RockFilletShader : null;
				if (s == null)
				{
					s = Shader.Find(ShaderName);
				}
				if (s == null || !s.isSupported)
				{
					return false;
				}
				shader = s;
			}
			if (counters == null)
			{
				counters = new GraphicsBuffer(GraphicsBuffer.Target.Structured, Counters, sizeof(uint));
				snapshot = new GraphicsBuffer(GraphicsBuffer.Target.Structured, RockContactField.ListCapacity, RockStride);
				cells = new GraphicsBuffer(GraphicsBuffer.Target.Structured, CellCapacity, CellStride);
				args = new GraphicsBuffer(GraphicsBuffer.Target.IndirectArguments | GraphicsBuffer.Target.Raw, MaxLooks, GraphicsBuffer.IndirectDrawArgs.size);
				starts = new GraphicsBuffer(GraphicsBuffer.Target.Structured, MaxLooks, sizeof(uint));
			}
			if (triangles == null || triangleCapacity != triangleBudget)
			{
				triangles?.Release();
				drawn?.Release();
				triangleCapacity = triangleBudget;
				triangles = new GraphicsBuffer(GraphicsBuffer.Target.Structured, triangleCapacity, TriangleStride);
				drawn = new GraphicsBuffer(GraphicsBuffer.Target.Structured, triangleCapacity, TriangleStride);
				building = shown = false;
			}
			return true;
		}

		/// <summary>
		/// One frame, after the crevice field's gather (RockContactField): carries the build under way on by a slice, or
		/// starts one when the drawn set no longer fits (only from a settled grid), and draws the drawn set.
		/// </summary>
		internal static void Frame(Camera camera, Vector3 gridMin, int rockKey, bool settled)
		{
			ReadProfile();
			if (!EnsureResources() || RockContactField.ListBuffer == null || RockContactField.Atlas == null)
			{
				Hide();
				return;
			}
			Vector3 eye = camera.transform.position;
			int settings = SettingsKey();
			if (building)
			{
				Continue();
			}
			else if (settled && (!shown || rockKey != shownKey || settings != shownSettings || (eye - shownCentre).sqrMagnitude > RebuildStep * RebuildStep))
			{
				Start(eye, gridMin, rockKey, settings);
			}
			if (shown)
			{
				Draw(eye);
			}
		}

		/// <summary>Draws nothing until the next build (the grid is off, or the fillets are).</summary>
		internal static void Hide()
		{
			shown = false;
			building = false;
		}

		private static void Start(Vector3 eye, Vector3 gridMin, int rockKey, int settings)
		{
			Vector3Int dims = RockContactField.GridCells;
			float cell = RockContactField.CellSize;
			// The region: the fade distance and the margin round the camera, in the grid's cells (clamped to it).
			float reach = fadeDistance + RegionMargin;
			Vector3Int lo = Vector3Int.FloorToInt((eye - Vector3.one * reach - gridMin) / cell);
			Vector3Int hi = Vector3Int.FloorToInt((eye + Vector3.one * reach - gridMin) / cell);
			lo = Vector3Int.Max(lo, Vector3Int.zero);
			hi = Vector3Int.Min(hi, dims - Vector3Int.one);
			Vector3Int size = hi - lo + Vector3Int.one;
			if (size.x <= 0 || size.y <= 0 || size.z <= 0)
			{
				return;
			}
			int regionCells = size.x * size.y * size.z;

			building = true;
			buildId++;
			nextCell = 0;
			knownCells = -1;
			buildCentre = eye;
			buildKey = rockKey;
			buildSettings = settings;
			buildGrid = new Vector4(gridMin.x, gridMin.y, gridMin.z, cell);
			buildAtlas = RockContactField.Atlas;
			Vector3 worldLo = gridMin + (Vector3)lo * cell, worldHi = gridMin + (Vector3)(hi + Vector3Int.one) * cell;
			buildBounds = new Bounds((worldLo + worldHi) * 0.5f, worldHi - worldLo);

			cmd.Clear();
			cmd.BeginSample(BuildSample);
			SetCommon();
			cmd.SetComputeIntParams(compute, RegionMinId, lo.x, lo.y, lo.z, 0);
			cmd.SetComputeIntParams(compute, RegionDimsId, size.x, size.y, size.z, regionCells);
			cmd.SetComputeBufferParam(compute, clearKernel, CountersId, counters);
			cmd.DispatchCompute(compute, clearKernel, 1, 1, 1);
			cmd.SetComputeBufferParam(compute, snapshotKernel, RocksId, snapshot);
			cmd.SetComputeBufferParam(compute, snapshotKernel, RocksInId, RockContactField.ListBuffer);
			cmd.DispatchCompute(compute, snapshotKernel, (RockContactField.ListCapacity + 63) / 64, 1, 1);
			cmd.SetComputeBufferParam(compute, classifyKernel, CountersId, counters);
			cmd.SetComputeBufferParam(compute, classifyKernel, RocksInId, RockContactField.ListBuffer);
			cmd.SetComputeBufferParam(compute, classifyKernel, SlotsId, RockContactField.SlotsBuffer);
			cmd.SetComputeBufferParam(compute, classifyKernel, CellsId, cells);
			cmd.SetComputeTextureParam(compute, classifyKernel, SdfId, buildAtlas);
			cmd.DispatchCompute(compute, classifyKernel, (regionCells + 63) / 64, 1, 1);
			cmd.EndSample(BuildSample);
			Graphics.ExecuteCommandBuffer(cmd);
			cmd.Clear();

			// How many seam cells there are, so the build stops at the last one rather than at the capacity.
			int id = buildId;
			AsyncGPUReadback.Request(counters, 4 * sizeof(uint), 0, request =>
			{
				if (id != buildId || request.hasError)
				{
					return;
				}
				var data = request.GetData<uint>();
				knownCells = (int)Mathf.Min(data[0], CellCapacity);
				if (data[0] > CellCapacity && !warnedCells)
				{
					warnedCells = true;
					Debug.LogWarning($"[Rock fillets] {data[0]} seam cells round the camera, over the {CellCapacity} a build holds: the farthest-found are left without fillets.");
				}
			});
		}

		/// <summary>The constants every kernel of a build reads, for this build.</summary>
		private static void SetCommon()
		{
			Vector3Int dims = RockContactField.GridCells;
			cmd.SetComputeVectorParam(compute, GridId, buildGrid);
			cmd.SetComputeIntParams(compute, GridDimsId, dims.x, dims.y, dims.z, 0);
			cmd.SetComputeVectorParam(compute, AtlasId, RockContactField.AtlasInfo);
			// The tuck: half the inset, so the kept surface's edge is between half and all of it under the rocks.
			cmd.SetComputeVectorParam(compute, ShapeId, new Vector4(width, share, inset, inset * 0.5f));
			cmd.SetComputeIntParams(compute, LimitsId, CellCapacity, triangleCapacity, RockContactField.ListCapacity, nextCell);
		}

		private static void Continue()
		{
			// The atlas was rebuilt under the build (a shape registered late): its fields are being drawn again.
			if (RockContactField.Atlas != buildAtlas)
			{
				building = false;
				return;
			}
			int total = knownCells >= 0 ? knownCells : CellCapacity;
			int groups = Mathf.Min(cellsPerFrame, total - nextCell);
			cmd.Clear();
			cmd.BeginSample(BuildSample);
			if (groups > 0)
			{
				SetCommon();
				cmd.SetComputeBufferParam(compute, meshKernel, CountersId, counters);
				cmd.SetComputeBufferParam(compute, meshKernel, RocksId, snapshot);
				cmd.SetComputeBufferParam(compute, meshKernel, CellsInId, cells);
				cmd.SetComputeBufferParam(compute, meshKernel, TrisId, triangles);
				cmd.SetComputeTextureParam(compute, meshKernel, SdfId, buildAtlas);
				cmd.DispatchCompute(compute, meshKernel, groups, 1, 1);
				nextCell += groups;
			}
			bool done = nextCell >= total;
			if (done)
			{
				// The whole build becomes the drawn set at once.
				cmd.SetComputeBufferParam(compute, finalizeKernel, CountersId, counters);
				cmd.SetComputeBufferParam(compute, finalizeKernel, ArgsId, args);
				cmd.SetComputeBufferParam(compute, finalizeKernel, StartsId, starts);
				cmd.DispatchCompute(compute, finalizeKernel, 1, 1, 1);
				cmd.SetComputeIntParams(compute, LimitsId, CellCapacity, triangleCapacity, RockContactField.ListCapacity, 0);
				cmd.SetComputeBufferParam(compute, scatterKernel, CountersId, counters);
				cmd.SetComputeBufferParam(compute, scatterKernel, TrisInId, triangles);
				cmd.SetComputeBufferParam(compute, scatterKernel, DrawnId, drawn);
				cmd.SetComputeBufferParam(compute, scatterKernel, StartsInId, starts);
				cmd.DispatchCompute(compute, scatterKernel, (triangleCapacity + 63) / 64, 1, 1);
			}
			cmd.EndSample(BuildSample);
			Graphics.ExecuteCommandBuffer(cmd);
			cmd.Clear();
			if (done)
			{
				Finish();
			}
		}

		private static void Finish()
		{
			building = false;
			shown = true;
			shownCentre = buildCentre;
			shownBounds = buildBounds;
			shownKey = buildKey;
			shownSettings = buildSettings;
			lookCountsKnown = false;
			int id = buildId;
			AsyncGPUReadback.Request(counters, Counters * sizeof(uint), 0, request =>
			{
				if (id != buildId || request.hasError)
				{
					return;
				}
				var data = request.GetData<uint>();
				uint total = 0;
				int used = 0;
				for (int l = 0; l < MaxLooks; l++)
				{
					lookCounts[l] = data[4 + l];
					total += lookCounts[l];
					used += lookCounts[l] > 0 ? 1 : 0;
				}
				lookCountsKnown = true;
				LastReport = $"[Rock fillets] {Mathf.Min(data[0], CellCapacity)} seam cell(s), {total} triangle(s) in {used} look(s) round ({buildCentre.x:F0}, {buildCentre.y:F0}, {buildCentre.z:F0}); width {width:F2} m, inset {inset:F2} m, stands at most {RockFilletMath.MaxOffset(width, inset):F2} m out of the rocks";
				if ((data[2] & 2u) != 0 && !warnedTriangles)
				{
					warnedTriangles = true;
					Debug.LogWarning($"[Rock fillets] {data[1]} triangles round the camera, over the {triangleCapacity} held (profile: Rock Fillet Triangles): some seams are left open.");
				}
				if (!reported)
				{
					reported = true;
					Debug.Log(LastReport);
				}
			});
		}

		/// <summary>A look's material: a copy of the rock's own on the fillet shader, the maps its keywords had on noted (_FishFilletMaps).</summary>
		private static Material MaterialFor(int look)
		{
			IReadOnlyList<Material> looks = RockContactField.Looks;
			while (materials.Count <= look && materials.Count < looks.Count)
			{
				Material source = looks[materials.Count];
				Material m = null;
				if (source != null)
				{
					m = new Material(shader) { name = $"{source.name} (rock fillet)", hideFlags = HideFlags.DontSave };
					m.CopyPropertiesFromMaterial(source);
					m.SetVector(MapsId, new Vector4(
						source.IsKeywordEnabled("_METALLICSPECGLOSSMAP") ? 1f : 0f,
						source.IsKeywordEnabled("_OCCLUSIONMAP") ? 1f : 0f,
						source.IsKeywordEnabled("_NORMALMAP") ? 1f : 0f,
						0f));
					// The rocks' base band and crevices (FishGroundColour.hlsl), as the rock renderers set it.
					m.SetFloat(ContactOnId, 1f);
				}
				materials.Add(m);
			}
			return look < materials.Count ? materials[look] : null;
		}

		private static void Draw(Vector3 eye)
		{
			Shader.SetGlobalBuffer(DrawTrisId, drawn);
			Shader.SetGlobalBuffer(DrawStartsId, starts);
			Shader.SetGlobalVector(FadeId, new Vector4(eye.x, eye.y, eye.z, fadeDistance));
			int count = RockContactField.Looks.Count;
			if (count > MaxLooks && !warnedLooks)
			{
				warnedLooks = true;
				Debug.LogWarning($"[Rock fillets] {count} rock materials, over the {MaxLooks} drawn: rocks of the later ones get no fillets.");
			}
			count = Mathf.Min(count, MaxLooks);
			for (int look = 0; look < count; look++)
			{
				if (lookCountsKnown && lookCounts[look] == 0)
				{
					continue;
				}
				Material material = MaterialFor(look);
				if (material == null)
				{
					continue;
				}
				var rp = new RenderParams(material)
				{
					layer = RockContactField.RockLayer,
					worldBounds = shownBounds,
					shadowCastingMode = ShadowCastingMode.On,
					receiveShadows = true,
					// As the rocks: Unity hands a draw with probes off the scene's ambient probe.
					lightProbeUsage = LightProbeUsage.Off,
					reflectionProbeUsage = ReflectionProbeUsage.BlendProbes,
				};
				Graphics.RenderPrimitivesIndirect(rp, MeshTopology.Triangles, args, 1, look);
			}
		}

		/// <summary>Lets every GPU buffer and material go (RockContactField.Release: quit, script reload).</summary>
		internal static void Release()
		{
			building = shown = false;
			counters?.Release();
			snapshot?.Release();
			cells?.Release();
			triangles?.Release();
			drawn?.Release();
			args?.Release();
			starts?.Release();
			counters = snapshot = cells = triangles = drawn = args = starts = null;
			foreach (Material m in materials)
			{
				if (m != null)
				{
					Object.Destroy(m);
				}
			}
			materials.Clear();
			if (compute != null)
			{
				Object.Destroy(compute);
				compute = null;
			}
		}
	}
}
