using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using Debug = UnityEngine.Debug;
using Unity.Profiling;

namespace FishMMO.Client
{
	/// <summary>
	/// Which GPU renderer draws a terrain detail prototype (2026-10-27, when the CPU chunk renderer was removed): the blade
	/// grass (<see cref="GrassBladeSystem"/>) takes the prefixes of <see cref="GrassBladeSettings.PrototypePrefixes"/>, the
	/// GPU detail scatter (<see cref="DetailScatterSystem"/>) every other mesh prototype; nothing else draws a detail. A
	/// prototype neither can take (a texture detail, an empty slot) is <see cref="Route.None"/>: an error at the cut and in
	/// play, never a quiet fallback. Pure but for reading the prototype, so the routing is tested off the engine.
	/// </summary>
	public static class TerrainDetailRouting
	{
		public enum Route
		{
			/// <summary>No GPU renderer can draw it: not drawn (an error).</summary>
			None,
			/// <summary>Procedural blade grass.</summary>
			Blades,
			/// <summary>The GPU detail scatter (meshes through their procedural-instancing twins).</summary>
			Scatter,
		}

		/// <summary>The route of a prefab by its name: <paramref name="mesh"/> false (a texture detail) is never drawn.</summary>
		public static Route RouteOf(string prefabName, bool mesh, GrassBladeSettings grass)
		{
			if (!mesh || string.IsNullOrEmpty(prefabName))
			{
				return Route.None;
			}
			return grass != null && grass.IsBladePrototype(prefabName) ? Route.Blades : Route.Scatter;
		}

		/// <summary>The route of a terrain's detail prototype.</summary>
		public static Route RouteOf(DetailPrototype prototype, GrassBladeSettings grass)
		{
			GameObject prefab = prototype?.prototype;
			return RouteOf(prefab != null ? prefab.name : null, prototype != null && prototype.usePrototypeMesh && prefab != null, grass);
		}

		/// <summary>Why a prototype has no route (for the error).</summary>
		public static string WhyNot(DetailPrototype prototype)
		{
			if (prototype == null)
			{
				return "an empty prototype";
			}
			return !prototype.usePrototypeMesh ? "a texture (billboard grass) detail: give it a mesh prefab" : "no prefab";
		}

		/// <summary>A prototype's name for a message: its prefab's, its texture's, or its index.</summary>
		public static string NameOf(DetailPrototype prototype, int index)
		{
			if (prototype?.prototype != null)
			{
				return prototype.prototype.name;
			}
			return prototype?.prototypeTexture != null ? prototype.prototypeTexture.name : $"prototype {index}";
		}

		/// <summary>
		/// Which of a terrain's detail prototypes are painted anywhere on it (<see cref="TerrainData.GetSupportedLayers(int,int,int,int)"/>:
		/// a layer any detail patch holds). Cuts before 2026-10-27 listed the whole scene's prototypes on every tile; only these are read.
		/// </summary>
		public static bool[] Painted(TerrainData data)
		{
			var painted = new bool[data != null ? data.detailPrototypes.Length : 0];
			if (painted.Length == 0 || data.detailWidth <= 0 || data.detailHeight <= 0)
			{
				return painted;
			}
			foreach (int p in data.GetSupportedLayers(0, 0, data.detailWidth, data.detailHeight))
			{
				if (p >= 0 && p < painted.Length)
				{
					painted[p] = true;
				}
			}
			return painted;
		}
	}

	/// <summary>
	/// Takes every terrain's details off Unity's own detail drawing in play mode, for the GPU renderers that draw them: the
	/// blade grass (<see cref="GrassBladeSystem"/>) and the GPU detail scatter (<see cref="DetailScatterSystem"/>), which
	/// generate their instances every frame on the GPU (<see cref="TerrainDetailRouting"/> says which takes what).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>GPU only (2026-10-27).</b> This was the CPU chunk renderer: it built ~32 m chunks of instances on the main thread
	/// (TerrainDetailField, TerrainDetailModel) and drew every prototype the GPU renderers did not take. It is gone; what is
	/// left is the driver both GPU renderers wait on: a terrain they may draw is one taken here.
	/// </para>
	/// <para>
	/// <b>Play mode only, client only.</b> Each terrain taken has its <see cref="Terrain.detailObjectDistance"/> set to 0 (its
	/// own remembered, put back when it is let go, at play-mode exit and at quit; followed if something else sets it). Edit
	/// mode is Unity's, so painting previews as before. Details have no colliders; the server strips them anyway.
	/// </para>
	/// <para>
	/// <b>Not taken, left to Unity:</b> a terrain whose quality settings override the detail distance, and a terrain with no
	/// details or a zero distance. A prototype no GPU renderer can draw is NOT handed back to Unity: it is not drawn, with an
	/// error (the scatter's and the grass's selection say which and why).
	/// </para>
	/// </remarks>
	public static class TerrainDetailInstancing
	{
		private sealed class Driven
		{
			public Terrain Terrain;
			public TerrainData Data;
			/// <summary>The distance mesh details are drawn to: the player's grass distance, capped (<see cref="EffectiveDistance"/>).</summary>
			public float Distance;
			/// <summary>The terrain's own detail distance, as the scene authored it: put back on release.</summary>
			public float Authored;
		}

		private static readonly Dictionary<Terrain, Driven> driven = new Dictionary<Terrain, Driven>();
		private static readonly List<Driven> drivenList = new List<Driven>();
		/// <summary>
		/// Per terrain, the prototypes each GPU renderer draws, per owner (<see cref="SetSkipped"/>): the claims the diagnostics
		/// read. One mask per owner, so neither owner's release gives back the other's.
		/// </summary>
		private static readonly Dictionary<Terrain, Dictionary<object, bool[]>> skipped = new Dictionary<Terrain, Dictionary<object, bool[]>>();
		private static readonly List<Terrain> active = new List<Terrain>();
		private static bool hooked;
		private static bool enabled = true;
		private static int syncedFrame = -1;

		/// <summary>On by default; off gives every terrain back to Unity's own detail drawing (an A/B only: the GPU renderers then draw nothing).</summary>
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
				syncedFrame = -1;
			}
		}

		public static int DrivenTerrainCount => drivenList.Count;

		/// <summary>
		/// The detail distance a terrain stands for: the mesh details' distance when it is taken (whose own
		/// <see cref="Terrain.detailObjectDistance"/> is then 0), the terrain's own otherwise — always in edit mode. The
		/// vegetation fade and the GPU detail scatter read this in place of the terrain's field.
		/// </summary>
		public static float DetailDistanceOf(Terrain terrain)
		{
			if (terrain == null)
			{
				return 0f;
			}
			return driven.TryGetValue(terrain, out Driven d) ? d.Distance : terrain.detailObjectDistance;
		}

		public static bool IsDriving(Terrain terrain) => terrain != null && driven.ContainsKey(terrain);

		/// <summary>
		/// Records that <paramref name="owner"/> (the blade grass or the GPU detail scatter) draws the prototypes set in
		/// <paramref name="mask"/> (by index into the terrain's detail prototypes) on <paramref name="terrain"/>; null gives that
		/// owner's claim back. A prototype is claimed while any owner claims it (<see cref="IsSkipped"/>; the name is the chunk
		/// renderer's, which skipped them).
		/// </summary>
		public static void SetSkipped(object owner, Terrain terrain, bool[] mask)
		{
			if (terrain == null || owner == null)
			{
				return;
			}
			skipped.TryGetValue(terrain, out Dictionary<object, bool[]> owners);
			if (mask == null)
			{
				if (owners == null || !owners.Remove(owner))
				{
					return;
				}
				if (owners.Count == 0)
				{
					skipped.Remove(terrain);
				}
				return;
			}
			if (owners == null)
			{
				owners = new Dictionary<object, bool[]>();
				skipped[terrain] = owners;
			}
			owners[owner] = (bool[])mask.Clone();
		}

		/// <summary>Whether a prototype of a terrain is claimed by a GPU renderer (any owner's).</summary>
		public static bool IsSkipped(Terrain terrain, int prototype)
		{
			if (terrain == null || prototype < 0 || !skipped.TryGetValue(terrain, out Dictionary<object, bool[]> owners))
			{
				return false;
			}
			foreach (bool[] mask in owners.Values)
			{
				if (mask != null && prototype < mask.Length && mask[prototype])
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>The player's grass distance (<see cref="ClientGrassSettings"/>), cached: re-read on its change.</summary>
		private static float grassDistance = ClientGrassSettings.DefaultDistance;

		/// <summary>The distance a taken terrain's mesh details are drawn to: the player's grass distance, never past <see cref="MaxChunkDistance"/>.</summary>
		private static float EffectiveDistance() => Mathf.Min(grassDistance, MaxChunkDistance);

		/// <summary>
		/// The farthest the mesh details (the GPU detail scatter's kinds but bushes, whose reach is their own) are drawn, metres,
		/// whatever the grass distance: the reach the chunk renderer had, kept when it went so the scatter's GPU work (instances
		/// generated per frame grow with the square of it) is what it was. The blade grass carries on to the full grass distance.
		/// </summary>
		public const float MaxChunkDistance = 250f;

		private static void OnGrassSettingsChanged() => grassDistance = ClientGrassSettings.Distance;

		/// <summary>The density scale Unity would use for a terrain: the quality override where it applies, else the terrain's own.</summary>
		public static float DensityScaleOf(Terrain terrain)
		{
			bool overridden = (QualitySettings.terrainQualityOverrides & TerrainQualityOverrides.DetailDensity) != 0 && !terrain.ignoreQualitySettings;
			return overridden ? QualitySettings.terrainDetailDensityScale : terrain.detailObjectDensity;
		}

		// ── Hook ──────────────────────────────────────────────────────

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void ResetStatics()
		{
			ReleaseAll();
			Unhook();
			skipped.Clear();
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
			enabled = UnityEditor.SessionState.GetBool(TerrainTreeInstancing.EnabledKey, true);
#endif
			RenderPipelineManager.beginContextRendering += OnBeginContextRendering;
			Application.quitting += Shutdown;
			grassDistance = ClientGrassSettings.Distance;
			ClientGrassSettings.OnChanged += OnGrassSettingsChanged;
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
			Application.quitting -= Shutdown;
			ClientGrassSettings.OnChanged -= OnGrassSettingsChanged;
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

		/// <summary>This system's once-a-frame work before the cameras render, named in the profiler (StartupTimeline reads it).</summary>
		private static readonly ProfilerMarker ContextMarker = new ProfilerMarker("TerrainDetailInstancing.BeginContext");

		private static void OnBeginContextRendering(ScriptableRenderContext context, List<Camera> cameras)
		{
			using var contextScope = ContextMarker.Auto();
			Sync();
		}

		// ── Following the loaded terrains ─────────────────────────────

		private static void Sync()
		{
			if (!Application.isPlaying || syncedFrame == Time.frameCount)
			{
				return;
			}
			syncedFrame = Time.frameCount;
			if (!enabled)
			{
				return;
			}
			Terrain.GetActiveTerrains(active);
			bool qualityOverride = (QualitySettings.terrainQualityOverrides & TerrainQualityOverrides.DetailDistance) != 0;
			for (int i = drivenList.Count - 1; i >= 0; i--)
			{
				Driven d = drivenList[i];
				Terrain t = d.Terrain;
				bool keep = t != null && active.Contains(t) && t.terrainData == d.Data && d.Data != null && !(qualityOverride && !t.ignoreQualitySettings);
				if (!keep)
				{
					Release(d);
					continue;
				}
				if (t.detailObjectDistance != 0f)
				{
					d.Authored = t.detailObjectDistance;
					t.detailObjectDistance = 0f;
				}
				// The player's grass distance can change at any time: the GPU renderers follow through DetailDistanceOf.
				d.Distance = EffectiveDistance();
			}
			int taken = 0;
			for (int i = 0; i < active.Count; i++)
			{
				Terrain t = active[i];
				if (t == null || driven.ContainsKey(t) || (qualityOverride && !t.ignoreQualitySettings))
				{
					continue;
				}
				taken += TryDrive(t) ? 1 : 0;
			}
			if (taken > 0)
			{
				Debug.Log($"[Terrain details] Taking the detail layers of {drivenList.Count} terrain(s) for the GPU renderers (blade grass and detail scatter); Unity's detail drawing is off on those terrains for this play session.");
			}
		}

		private static bool TryDrive(Terrain terrain)
		{
			TerrainData data = terrain.terrainData;
			if (data == null || terrain.detailObjectDistance <= 0f || data.detailPrototypes.Length == 0)
			{
				return false;
			}
			var d = new Driven
			{
				Terrain = terrain,
				Data = data,
				Distance = EffectiveDistance(),
				Authored = terrain.detailObjectDistance,
			};
			driven.Add(terrain, d);
			drivenList.Add(d);
			terrain.detailObjectDistance = 0f;
			return true;
		}

		private static void Release(Driven d)
		{
			if (d.Terrain != null && d.Terrain.detailObjectDistance == 0f)
			{
				d.Terrain.detailObjectDistance = d.Authored;
			}
			driven.Remove(d.Terrain);
			drivenList.Remove(d);
		}

		private static void ReleaseAll()
		{
			for (int i = drivenList.Count - 1; i >= 0; i--)
			{
				Release(drivenList[i]);
			}
			driven.Clear();
		}
	}
}
