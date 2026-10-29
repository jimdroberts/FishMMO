using System.Collections.Generic;
using FishMMO.Shared;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishMMO.Client
{
	/// <summary>
	/// The run-time cave cover (2026-10-20): the cave's counterpart of the surface's blade grass and detail scatter. Carved caves'
	/// rock is a mesh, not a heightmap, and the terrain is holed under them, so nothing small and dense grew inside; this
	/// scatters it — moss and glow moss, gravel and grit, mushrooms and glowcaps, bones, guano, pearls, mud, root litter, frost,
	/// basalt grit, sulphur, ember glints, lichen, glowing specks, bracket fungi, mineral glints, seeps, soda straws, roots,
	/// glow-worm threads; in a mine timber, iron, ore crumbs and candle stubs — on the rock around each camera every frame
	/// (<see cref="CaveCoverRenderer"/>, FishCaveCover.compute), where the cave's habitat channels (baked into its mesh by the
	/// cut: wet, daylight, sediment, trodden, organic, glow) say each grows.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>What it follows.</b> Every cave alive (<see cref="CaveVisibility.Live"/>, on the cave's client-only Visual): its pieces'
	/// meshes, taken on as a camera nears them (their GPU buffers marked raw and bound to the compute as bytes, a few pieces a
	/// frame, main thread), and its <see cref="CaveCoverSite"/> (the types and glow colours the cut chose; a cave cut before has
	/// none and grows <see cref="CaveCoverTypes.Natural"/>). A cave gone from the list is let go.
	/// </para>
	/// <para>
	/// <b>When.</b> Play mode, client, compute-capable devices (not WebGL2), the profile's Cave cover enabled; the dashboard's
	/// A/B key (SessionState <see cref="OffKey"/>) turns it off per editor session. Reflection cameras and the minimap draw none.
	/// </para>
	/// </remarks>
	public static class CaveCoverSystem
	{
		/// <summary>SessionState key (editor): true turns the cave cover off (A/B).</summary>
		public const string OffKey = "FishMMO.CaveCover.Off";

		/// <summary>Pieces taken on a frame at most (each marks its mesh's buffers raw, which re-creates them).</summary>
		public const int TakeOnPerFrame = 6;

		/// <summary>Metres past the cover's reach a piece is taken on, so it is ready by the time it is in reach.</summary>
		public const float TakeOnMargin = 24f;

		private sealed class Cave
		{
			public CaveVisibility Visibility;
			public MeshFilter[] Filters;
			/// <summary>Each piece's world box, read once (the rock never moves).</summary>
			public Bounds[] Bounds;
			public CaveCoverRenderer.Piece[] Pieces;
			public bool[] Tried;
			public CaveCoverSite.Choice Choice;
			public int Slot = -1;
			public int Layer;
		}

		private static readonly Dictionary<CaveVisibility, Cave> caves = new Dictionary<CaveVisibility, Cave>();
		private static readonly List<CaveCoverRenderer.Piece> pieces = new List<CaveCoverRenderer.Piece>();
		private static readonly List<CaveVisibility> gone = new List<CaveVisibility>();
		private static readonly bool[] slotsUsed = new bool[CaveCoverRenderer.MaxCaves];
		private static readonly CaveCoverSettings defaults = new CaveCoverSettings();
		private static readonly List<Vector3> eyes = new List<Vector3>();
		private static readonly HashSet<string> warned = new HashSet<string>();
		private static CaveCoverRenderer renderer;
		private static bool rendererTried, hooked;
		private static bool enabled = true;
		private static int syncedFrame = -1, nextId = 1;
		private static readonly ProfilerMarker ContextMarker = new ProfilerMarker("CaveCoverSystem.BeginContext");

		/// <summary>The renderer, for diagnostics.</summary>
		public static CaveCoverRenderer Renderer => renderer;

		/// <summary>The caves followed and the pieces taken on.</summary>
		public static int CaveCount => caves.Count;
		public static int PieceCount => pieces.Count;
		public static int LastDrawCount { get; private set; }

		/// <summary>The session's switch (A/B).</summary>
		public static bool Enabled
		{
			get => enabled;
			set
			{
				if (enabled == value)
				{
					return;
				}
				enabled = value;
				if (!value)
				{
					ReleaseAll();
				}
			}
		}

		private static CaveCoverSettings Settings => WeatherRenderProfile.Active?.CaveCover ?? defaults;

		/// <summary>A line for the dashboard.</summary>
		public static string Describe()
		{
			if (!enabled || !Settings.Enabled)
			{
				return "Cave cover OFF.";
			}
			if (renderer == null)
			{
				return rendererTried ? "Cave cover unavailable on this device or without its assets (see the log)." : "Cave cover not started (enter play mode near a cave).";
			}
			var s = new System.Text.StringBuilder();
			s.Append($"Cave cover ON: {caves.Count} cave(s), {pieces.Count} piece(s) taken on; last game camera {renderer.LastPieces} piece(s), {renderer.LastTriangles} triangles; GPU buffers {renderer.GpuBytes / (1024f * 1024f):F1} MiB; appended:");
			for (int t = 0; t < CaveCoverRenderer.Types; t++)
			{
				int n = 0;
				for (int level = 0; level < CaveCoverRenderer.Levels; level++)
				{
					n += renderer.LastCounts[CaveCoverRenderer.SlotOf(t, level)];
				}
				if (n > 0)
				{
					s.Append($" {(CaveCoverType)t} {n}");
				}
			}
			return s.Append('.').ToString();
		}

		// ── Hook ──────────────────────────────────────────────────────

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			ReleaseAll();
			Unhook();
			warned.Clear();
			enabled = true;
		}

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
		private static void Hook()
		{
			if (hooked || !Application.isPlaying)
			{
				return;
			}
			hooked = true;
			syncedFrame = -1;
#if UNITY_EDITOR
			enabled = !UnityEditor.SessionState.GetBool(OffKey, false);
#endif
			RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
			RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
			Application.quitting += Shutdown;
#if UNITY_EDITOR
			UnityEditor.EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
#endif
		}

		private static void Unhook()
		{
			if (!hooked)
			{
				return;
			}
			hooked = false;
			RenderPipelineManager.beginContextRendering -= OnBeginContextRendering;
			RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
			Application.quitting -= Shutdown;
#if UNITY_EDITOR
			UnityEditor.EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
#endif
		}

#if UNITY_EDITOR
		private static void OnPlayModeStateChanged(UnityEditor.PlayModeStateChange change)
		{
			if (change == UnityEditor.PlayModeStateChange.ExitingPlayMode)
			{
				Shutdown();
			}
		}
#endif

		private static void Shutdown()
		{
			ReleaseAll();
			Unhook();
		}

		private static void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
		{
			using (ContextMarker.Auto())
			{
				eyes.Clear();
				if (cameras != null)
				{
					foreach (Camera camera in cameras)
					{
						if (camera != null && TerrainInstancingShared.DrawsDetails(camera) && camera.cameraType != CameraType.Reflection)
						{
							eyes.Add(camera.transform.position);
						}
					}
				}
				Sync();
			}
		}

		/// <summary>This hook's own time in the profiler (render-pipeline callbacks otherwise show only as
		/// RenderPipeline.BeginCameraRendering's self time). In a class of its own so the marker, an engine object, is made
		/// when the hook first runs, not when the type is first touched (tests outside Unity use these types' pure parts).</summary>
		private static class HookProfiling
		{
			public static readonly Unity.Profiling.ProfilerMarker Marker = new Unity.Profiling.ProfilerMarker("CaveCoverSystem.BeginCamera");
		}

		private static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
		{
			using var hookMarkerScope = HookProfiling.Marker.Auto();
			if (!Application.isPlaying || renderer == null || pieces.Count == 0 || !TerrainInstancingShared.DrawsDetails(camera) || camera.cameraType == CameraType.Reflection)
			{
				return;
			}
			CaveCoverSettings s = Settings;
			if (!enabled || !s.Enabled)
			{
				return;
			}
			TerrainTreeField.View view = TerrainInstancingShared.ViewOf(camera);
			LastDrawCount = renderer.Execute(camera, context, in view, pieces, s);
		}

		// ── Following the caves ───────────────────────────────────────

		private static void Sync()
		{
			if (!Application.isPlaying || syncedFrame == Time.frameCount)
			{
				return;
			}
			syncedFrame = Time.frameCount;
			CaveCoverSettings s = Settings;
			if (!enabled || !s.Enabled)
			{
				if (caves.Count > 0)
				{
					ReleaseAll();
				}
				return;
			}
			IReadOnlyList<CaveVisibility> live = CaveVisibility.Live;
			if (live.Count == 0 && caves.Count == 0)
			{
				return;
			}
			if (renderer == null && !rendererTried)
			{
				rendererTried = true;
				renderer = CaveCoverRenderer.TryCreate(out string reason);
				if (renderer == null)
				{
					WarnOnce("renderer", $"[Cave cover] Off: {reason}.");
				}
			}
			if (renderer == null)
			{
				return;
			}

			// Caves gone (unloaded, disabled) are let go; new ones followed.
			gone.Clear();
			foreach (KeyValuePair<CaveVisibility, Cave> kv in caves)
			{
				if (kv.Key == null || !kv.Key.isActiveAndEnabled)
				{
					gone.Add(kv.Key);
				}
			}
			foreach (CaveVisibility v in gone)
			{
				Release(v);
			}
			for (int i = 0; i < live.Count; i++)
			{
				CaveVisibility v = live[i];
				if (v != null && !caves.ContainsKey(v))
				{
					Follow(v);
				}
			}

			// Pieces near a camera taken on, a few a frame, nearest first by the order met.
			float reach = s.Reach(LongestReach()) + TakeOnMargin;
			float reach2 = reach * reach;
			int budget = TakeOnPerFrame;
			foreach (Cave cave in caves.Values)
			{
				for (int p = 0; p < cave.Filters.Length && budget > 0; p++)
				{
					if (cave.Tried[p] || cave.Filters[p] == null)
					{
						continue;
					}
					Bounds bounds = cave.Bounds[p];
					bool near = false;
					foreach (Vector3 eye in eyes)
					{
						near |= bounds.SqrDistance(eye) <= reach2;
					}
					if (!near)
					{
						continue;
					}
					cave.Tried[p] = true;
					budget--;
					CaveCoverRenderer.Piece piece = TakeOn(cave, cave.Filters[p], bounds);
					if (piece != null)
					{
						cave.Pieces[p] = piece;
						pieces.Add(piece);
					}
				}
			}
		}

		private static float LongestReach()
		{
			float longest = 0.05f;
			foreach (CaveCoverTypeInfo t in CaveCoverTypes.All)
			{
				longest = Mathf.Max(longest, t.Reach);
			}
			return longest;
		}

		private static void Follow(CaveVisibility visibility)
		{
			int slot = System.Array.IndexOf(slotsUsed, false);
			if (slot < 0)
			{
				WarnOnce("slots", $"[Cave cover] More than {CaveCoverRenderer.MaxCaves} caves alive at once: '{visibility.name}' and later ones grow no cover until one unloads.");
				return;
			}
			slotsUsed[slot] = true;
			CaveCoverSite site = visibility.GetComponent<CaveCoverSite>();
			MeshFilter[] filters = visibility.GetComponentsInChildren<MeshFilter>(true);
			var boxes = new Bounds[filters.Length];
			for (int i = 0; i < filters.Length; i++)
			{
				Renderer r = filters[i] != null ? filters[i].GetComponent<Renderer>() : null;
				boxes[i] = r != null ? r.bounds : default;
			}
			var cave = new Cave
			{
				Visibility = visibility,
				Filters = filters,
				Bounds = boxes,
				Pieces = new CaveCoverRenderer.Piece[filters.Length],
				Tried = new bool[filters.Length],
				Choice = site != null ? site.Current : CaveCoverSite.Default,
				Slot = slot,
				Layer = visibility.gameObject.layer,
			};
			renderer.SetPalette(cave.Slot, cave.Choice.Fungus, cave.Choice.Accent, cave.Choice.Crystal);
			caves.Add(visibility, cave);
		}

		/// <summary>
		/// A piece's mesh bound for the compute: its vertex and index buffers marked raw (a one-off re-creation of them on the GPU),
		/// its layout read from the mesh (one stream holding position, normal, colour and, when written, TEXCOORD3; formats
		/// read as they are). Null for a mesh the compute cannot read.
		/// </summary>
		private static CaveCoverRenderer.Piece TakeOn(Cave cave, MeshFilter filter, Bounds bounds)
		{
			Mesh mesh = filter.sharedMesh;
			if (mesh == null || mesh.subMeshCount < 1 || mesh.vertexCount == 0)
			{
				return null;
			}
			if (!mesh.HasVertexAttribute(VertexAttribute.Position) || !mesh.HasVertexAttribute(VertexAttribute.Normal))
			{
				WarnOnce("layout:" + mesh.name, $"[Cave cover] '{mesh.name}' has no positions or normals: no cover on it.");
				return null;
			}
			int stream = mesh.GetVertexAttributeStream(VertexAttribute.Position);
			bool colour = mesh.HasVertexAttribute(VertexAttribute.Color);
			bool uv3 = mesh.HasVertexAttribute(VertexAttribute.TexCoord3);
			if (mesh.GetVertexAttributeStream(VertexAttribute.Normal) != stream
				|| (colour && mesh.GetVertexAttributeStream(VertexAttribute.Color) != stream)
				|| (uv3 && mesh.GetVertexAttributeStream(VertexAttribute.TexCoord3) != stream))
			{
				WarnOnce("streams:" + mesh.name, $"[Cave cover] '{mesh.name}' keeps its attributes in several vertex streams: no cover on it (the compute reads one).");
				return null;
			}
			mesh.vertexBufferTarget |= GraphicsBuffer.Target.Raw;
			mesh.indexBufferTarget |= GraphicsBuffer.Target.Raw;
			if ((mesh.vertexBufferTarget & GraphicsBuffer.Target.Raw) == 0 || (mesh.indexBufferTarget & GraphicsBuffer.Target.Raw) == 0)
			{
				WarnOnce("raw:" + mesh.name, $"[Cave cover] '{mesh.name}' would not take raw GPU buffers: no cover on it.");
				return null;
			}
			GraphicsBuffer vertices = mesh.GetVertexBuffer(stream);
			GraphicsBuffer indices = mesh.GetIndexBuffer();
			if (vertices == null || indices == null)
			{
				vertices?.Dispose();
				indices?.Dispose();
				return null;
			}
			SubMeshDescriptor sub = mesh.GetSubMesh(0);
			Matrix4x4 toWorld = filter.transform.localToWorldMatrix;
			CaveCoverSite.Choice choice = cave.Choice;
			return new CaveCoverRenderer.Piece
			{
				Id = nextId++,
				Bounds = bounds,
				Vertices = vertices,
				Indices = indices,
				Layer = cave.Layer,
				Data = new CaveCoverRenderer.PieceData
				{
					Row0 = toWorld.GetRow(0),
					Row1 = toWorld.GetRow(1),
					Row2 = toWorld.GetRow(2),
					PositionOffset = (uint)mesh.GetVertexAttributeOffset(VertexAttribute.Position),
					NormalOffset = (uint)mesh.GetVertexAttributeOffset(VertexAttribute.Normal),
					ColourOffset = colour ? (uint)mesh.GetVertexAttributeOffset(VertexAttribute.Color) : 0xFFFFFFFFu,
					Uv3Offset = uv3 ? (uint)mesh.GetVertexAttributeOffset(VertexAttribute.TexCoord3) : 0xFFFFFFFFu,
					PositionFormat = Format(mesh, VertexAttribute.Position),
					NormalFormat = Format(mesh, VertexAttribute.Normal),
					ColourFormat = colour ? Format(mesh, VertexAttribute.Color) : 0u,
					Uv3Format = uv3 ? Format(mesh, VertexAttribute.TexCoord3) : 0u,
					VertexStride = (uint)mesh.GetVertexBufferStride(stream),
					Triangles = (uint)(sub.indexCount / 3),
					Wide = mesh.indexFormat == IndexFormat.UInt32 ? 1u : 0u,
					FirstIndex = (uint)sub.indexStart,
					BaseVertex = (uint)Mathf.Max(0, sub.baseVertex),
					CaveSlot = (uint)cave.Slot,
					TypeMask = choice.Types,
					Density = choice.Density,
					Glow = choice.Glow,
				},
			};
		}

		/// <summary>An attribute's format for the compute: VertexAttributeFormat | dimension &lt;&lt; 8.</summary>
		private static uint Format(Mesh mesh, VertexAttribute attribute)
			=> (uint)mesh.GetVertexAttributeFormat(attribute) | ((uint)mesh.GetVertexAttributeDimension(attribute) << 8);

		private static void Release(CaveVisibility visibility)
		{
			if (!caves.TryGetValue(visibility, out Cave cave))
			{
				return;
			}
			caves.Remove(visibility);
			foreach (CaveCoverRenderer.Piece piece in cave.Pieces)
			{
				if (piece != null)
				{
					pieces.Remove(piece);
					piece.Vertices?.Dispose();
					piece.Indices?.Dispose();
				}
			}
			if (cave.Slot >= 0)
			{
				slotsUsed[cave.Slot] = false;
			}
		}

		private static void ReleaseAll()
		{
			var all = new List<CaveVisibility>(caves.Keys);
			foreach (CaveVisibility v in all)
			{
				Release(v);
			}
			caves.Clear();
			pieces.Clear();
			System.Array.Clear(slotsUsed, 0, slotsUsed.Length);
			renderer?.Dispose();
			renderer = null;
			rendererTried = false;
		}

		private static void WarnOnce(string key, string message)
		{
			if (warned.Add(key))
			{
				Debug.LogWarning(message);
			}
		}
	}
}
