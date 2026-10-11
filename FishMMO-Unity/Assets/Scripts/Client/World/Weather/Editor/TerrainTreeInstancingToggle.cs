using System;
using FishMMO.Shared;
using UnityEditor;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// A/B buttons and a self-check for the instanced terrain renderers (<see cref="TerrainTreeInstancing"/>,
	/// <see cref="TerrainDetailInstancing"/>). The two switches also work in edit mode: they pre-set what play
	/// mode starts with (kept in SessionState, which outlives the domain reload of entering play mode).
	/// </summary>
	public static class TerrainTreeInstancingToggle
	{
		private const int SettleFrames = 5;

		[DashboardTool(DashboardToolAttribute.Biomes, "Toggle instanced terrain trees and details (play mode A/B)", Section = "Diagnostics", Order = 101, AllowInPlayMode = true,
			Tooltip = "Switches the tree channel and the detail layers between the client's instanced renderers (default) and Unity's own terrain drawing. In edit mode it sets what the next play session starts with. Nothing is saved to any scene.")]
		public static void Toggle()
		{
			bool on = !SessionState.GetBool(TerrainTreeInstancing.EnabledKey, true);
			if (Application.isPlaying)
			{
				on = !TerrainTreeInstancing.Enabled;
			}
			SessionState.SetBool(TerrainTreeInstancing.EnabledKey, on);
			if (!Application.isPlaying)
			{
				Debug.Log($"[Terrain instancing] When play starts: {(on ? "instanced trees and details ON" : "OFF — Unity draws trees and details")} ({PathWhenPlaying()}).");
				return;
			}
			TerrainTreeInstancing.Enabled = on;
			TerrainDetailInstancing.Enabled = on;
			SceneView.RepaintAll();
			AfterFrames(() => Debug.Log($"[Terrain instancing] {State()}"));
		}

		[DashboardTool(DashboardToolAttribute.Biomes, "Toggle GPU-driven terrain instancing (play mode A/B)", Section = "Diagnostics", Order = 102, AllowInPlayMode = true,
			Tooltip = "Switches the instanced trees and details between the GPU-driven path (compute culling + indirect draws) and the RenderMeshInstanced fallback that WebGL2 uses. In edit mode it sets what the next play session starts with.")]
		public static void ToggleGpu()
		{
			bool prefer = !SessionState.GetBool(TerrainTreeInstancing.PreferGpuKey, true);
			if (Application.isPlaying)
			{
				prefer = !TerrainTreeInstancing.PreferGpu;
			}
			SessionState.SetBool(TerrainTreeInstancing.PreferGpuKey, prefer);
			if (!Application.isPlaying)
			{
				Debug.Log($"[Terrain instancing] When play starts: {PathWhenPlaying()}.");
				return;
			}
			TerrainTreeInstancing.PreferGpu = prefer;
			AfterFrames(() => Debug.Log($"[Terrain instancing] {State()}"));
		}

		[DashboardTool(DashboardToolAttribute.Biomes, "Diagnose terrain instancing (play mode)", Section = "Diagnostics", Order = 103, AllowInPlayMode = true,
			Tooltip = "Play mode, GPU path. Reads the game camera's culling counts and indirect arguments back from the GPU (AsyncGPUReadback, no stall), recomputes them with the CPU mirror of the kernel, and logs every prototype and level with any mismatch flagged.")]
		public static void Diagnose()
		{
			if (!Application.isPlaying)
			{
				Debug.Log($"[Terrain instancing] The diagnostic reads a live frame: enter play mode first. {PathWhenPlaying()}.");
				return;
			}
			Debug.Log($"[Terrain instancing] {State()}");
			Camera camera = Camera.main;
			if (camera == null)
			{
				Debug.LogWarning("[Terrain instancing] No main camera to diagnose.");
				return;
			}
			bool trees = TerrainTreeInstancing.RequestGpuDiagnostics(camera, r => Log("Trees", r));
			bool details = TerrainDetailInstancing.RequestGpuDiagnostics(camera, r => Log("Details", r));
			if (!trees && !details)
			{
				Debug.Log("[Terrain instancing] Neither renderer is on the GPU path; nothing to read back.");
			}
		}

		[DashboardTool(DashboardToolAttribute.Biomes, "Toggle terrain GPU upload log", Section = "Diagnostics", Order = 104, AllowInPlayMode = true,
			Tooltip = "Logs every upload, grow, free and flush of the GPU instance buffers (offset, count, owner, buffer, frame). Set it in edit mode to catch the first uploads of a play session.")]
		public static void ToggleUploadLog()
		{
			bool on = !SessionState.GetBool(TerrainTreeInstancing.LogUploadsKey, false);
			SessionState.SetBool(TerrainTreeInstancing.LogUploadsKey, on);
			TerrainGpuRenderer.LogUploads = on;
			Debug.Log($"[Terrain instancing] GPU upload log {(on ? "ON" : "OFF")}{(Application.isPlaying ? "" : " (from the next play session's first upload)")}.");
		}

		[DashboardTool(DashboardToolAttribute.Biomes, "Toggle terrain spike probe", Section = "Diagnostics", Order = 105, AllowInPlayMode = true,
			Tooltip = "Logs one [Terrain probe] line per frame over 33 ms, or with a relayout or instance-buffer grow: sync, chunk builds, uploads, flushes, compute record, draw submit and Gen-0 GCs. Leave the upload log OFF while measuring; its own lines cause spikes.")]
		public static void ToggleSpikeProbe()
		{
			bool on = !SessionState.GetBool(TerrainInstancingProbe.EnabledKey, false);
			SessionState.SetBool(TerrainInstancingProbe.EnabledKey, on);
			TerrainInstancingProbe.Enabled = on;
			Debug.Log($"[Terrain instancing] Spike probe {(on ? "ON" : "OFF")}; upload log {(TerrainGpuRenderer.LogUploads ? "ON (turn it off while measuring)" : "off")}.");
		}

		[DashboardTool(DashboardToolAttribute.Biomes, "Cycle ground colour debug view", Section = "Diagnostics", Order = 106, AllowInPlayMode = true,
			Tooltip = "Play mode, cycles: OFF → GROUND (every grass/leaf wholly the ground colour its root reads; magenta where it reads nothing) → WEIGHT (grey by how much ground colour the normal blend gives: white all, black none) → OFF. Logs the shader globals each press.")]
		public static void ToggleGroundColourDebug()
		{
			GroundColourMap.DebugMode = (GroundColourMap.DebugMode + 1) % 3;
			string mode = GroundColourMap.DebugMode == 1 ? "GROUND: plants show the ground colour they read, magenta where they read nothing"
				: GroundColourMap.DebugMode == 2 ? "WEIGHT: plants grey by the normal blend's weight (white all, black none)" : "OFF";
			Debug.Log($"[Ground colour] Debug view {mode}; {GroundColourMap.Describe()}.");
		}

		[DashboardTool(DashboardToolAttribute.Biomes, "Toggle path debug view", Section = "Diagnostics", Order = 108, AllowInPlayMode = true,
			Tooltip = "The terrain draws every baked path flat MAGENTA where it has its earth/gravel/cobble slices, CYAN where they are missing (repaint and save the scene). Nothing drawn = the path field is not bound. Logs what is bound each press.")]
		public static void TogglePathDebug()
		{
			ScenePathSurfaceBinder.DebugView = !ScenePathSurfaceBinder.DebugView;
			Debug.Log($"[Paths] Debug view {(ScenePathSurfaceBinder.DebugView ? "ON" : "OFF")}; {ScenePathSurfaceBinder.Describe()}.");
		}

		[DashboardTool(DashboardToolAttribute.Biomes, "Toggle old mesh grass (play mode A/B)", Section = "Diagnostics", Order = 107, AllowInPlayMode = true,
			Tooltip = "Procedural blade grass is the default wherever compute runs. This opts the grass detail prototypes (Detail_Grass*) back into the old instanced clump meshes, and a second press returns to the blades. Remembered for the editor session; in edit mode it sets what the next play session starts with. Each press in play mode also logs the blade system's state and, a second later, the blades it appended (async readback).")]
		public static void ToggleProceduralGrass()
		{
			bool meshGrass = !SessionState.GetBool(GrassBladeSystem.MeshGrassKey, false);
			if (Application.isPlaying)
			{
				meshGrass = GrassBladeSystem.Enabled;
			}
			SessionState.SetBool(GrassBladeSystem.MeshGrassKey, meshGrass);
			bool on = !meshGrass;
			if (!Application.isPlaying)
			{
				Debug.Log($"[Grass] When play starts: {(on ? "procedural blade grass (default)" : "OLD MESH GRASS (clumps); press again for the blades")}.");
				return;
			}
			GrassBladeSystem.Enabled = on;
			GrassBladeRenderer.StatsEnabled = on;
			SceneView.RepaintAll();
			AfterFrames(() => Debug.Log($"[Grass] {GrassBladeSystem.Describe()}"));
			if (on)
			{
				int until = Time.frameCount + 2 * GrassBladeRenderer.StatsInterval + SettleFrames;
				void Later()
				{
					if (Application.isPlaying && Time.frameCount < until)
					{
						return;
					}
					EditorApplication.update -= Later;
					if (Application.isPlaying)
					{
						Debug.Log($"[Grass] {GrassBladeSystem.Describe()}");
					}
				}
				EditorApplication.update += Later;
			}
		}

		[DashboardTool(DashboardToolAttribute.Biomes, "Highlight procedural grass (play mode)", Section = "Diagnostics", Order = 108, AllowInPlayMode = true,
			Tooltip = "Draws every procedural grass blade bright magenta (press again to stop): whatever grass stays green or straw-coloured is the old mesh grass. Logs the blade system's state.")]
		public static void ToggleGrassHighlight()
		{
			GrassBladeRenderer.Highlight = !GrassBladeRenderer.Highlight;
			GrassBladeRenderer.StatsEnabled = true;
			Debug.Log($"[Grass] Highlight {(GrassBladeRenderer.Highlight ? "ON: procedural blades are MAGENTA" : "OFF")}; {GrassBladeSystem.Describe()}");
			AfterFrames(() => Debug.Log($"[Grass] {GrassBladeSystem.Describe()}"));
		}

		[DashboardTool(DashboardToolAttribute.Biomes, "Freeze procedural grass (play mode)", Section = "Diagnostics", Order = 109, AllowInPlayMode = true,
			Tooltip = "Stops regenerating the blades and keeps drawing the last set generated (press again to resume). A flicker that goes on while frozen is in the drawing or lighting; one that stops is in the generation or culling. Wind still moves the frozen blades.")]
		public static void ToggleGrassFreeze()
		{
			GrassBladeRenderer.Freeze = !GrassBladeRenderer.Freeze;
			Debug.Log($"[Grass] Generation {(GrassBladeRenderer.Freeze ? "FROZEN: drawing the last blades generated" : "running")}.");
		}

		[DashboardTool(DashboardToolAttribute.Biomes, "Skip grass terrain colour read (play mode A/B)", Section = "Diagnostics", Order = 110, AllowInPlayMode = true,
			Tooltip = "Switches the grass compute to its variant without the terrain-colour textures (press again to resume): it declares and binds none of the 8 control maps or the albedo array, exactly the bindings it had before the tint. The blades fall back to the ground colour map.")]
		public static void ToggleGrassTerrainColour()
		{
			GrassBladeRenderer.SkipTerrainColour = !GrassBladeRenderer.SkipTerrainColour;
			Debug.Log($"[Grass] Terrain colour read {(GrassBladeRenderer.SkipTerrainColour ? "SKIPPED (ground colour map fallback)" : "on")}.");
		}

		[DashboardTool(DashboardToolAttribute.Biomes, "Constant grass terrain colour (play mode A/B)", Section = "Diagnostics", Order = 111, AllowInPlayMode = true,
			Tooltip = "The grass compute keeps its full kernel, bindings and vertex terrain-colour branch, but writes one constant colour without reading any colour data (press again to resume). Separates the colour reads from everything else the Skip switch changes.")]
		public static void ToggleGrassConstantColour()
		{
			GrassBladeRenderer.ConstantTerrainColour = !GrassBladeRenderer.ConstantTerrainColour;
			Debug.Log($"[Grass] Constant terrain colour {(GrassBladeRenderer.ConstantTerrainColour ? "ON (no colour data read)" : "off")}.");
		}

		[DashboardTool(DashboardToolAttribute.Biomes, "Reverse grass dispatch order (play mode A/B)", Section = "Diagnostics", Order = 112, AllowInPlayMode = true,
			Tooltip = "Generates the grass terrains last to first (press again to restore). Normally the terrain underfoot is built first and so is the first dispatch of every frame: if a flicker follows the reversal to another terrain, it belongs to the dispatch's position, not to the terrain.")]
		public static void ToggleGrassReverseDispatch()
		{
			GrassBladeRenderer.ReverseDispatchOrder = !GrassBladeRenderer.ReverseDispatchOrder;
			Debug.Log($"[Grass] Dispatch order {(GrassBladeRenderer.ReverseDispatchOrder ? "REVERSED (terrain underfoot last)" : "normal (terrain underfoot first)")}.");
		}

		[DashboardTool(DashboardToolAttribute.Biomes, "Toggle GPU detail scatter (play mode A/B)", Section = "Diagnostics", Order = 115, AllowInPlayMode = true,
			Tooltip = "The GPU detail scatter (pebbles, small rocks, shells, litter generated on the GPU every frame) is the default wherever compute runs. This opts those prototypes back into the CPU-built detail chunks, and a second press returns to the scatter. Remembered for the editor session; in edit mode it sets what the next play session starts with. Each press in play mode logs the scatter's state.")]
		public static void ToggleDetailScatter()
		{
			bool chunks = !SessionState.GetBool(DetailScatterSystem.ChunkDetailsKey, false);
			if (Application.isPlaying)
			{
				chunks = DetailScatterSystem.Enabled;
			}
			SessionState.SetBool(DetailScatterSystem.ChunkDetailsKey, chunks);
			if (!Application.isPlaying)
			{
				Debug.Log($"[Detail scatter] When play starts: {(chunks ? "CPU DETAIL CHUNKS for every mesh detail; press again for the GPU scatter" : "GPU detail scatter (default)")}.");
				return;
			}
			DetailScatterSystem.Enabled = !chunks;
			SceneView.RepaintAll();
			AfterFrames(() => Debug.Log($"[Detail scatter] {DetailScatterSystem.Describe()}"));
		}

		[DashboardTool(DashboardToolAttribute.Biomes, "Describe GPU detail scatter (play mode)", Section = "Diagnostics", Order = 116, AllowInPlayMode = true,
			Tooltip = "Logs the GPU detail scatter's state: terrains taken, types, the game camera's work items and upper bound, and each type's appended instances against its slot capacity (read back every 30 frames in the editor).")]
		public static void DescribeDetailScatter()
		{
			Debug.Log($"[Detail scatter] {DetailScatterSystem.Describe()}");
		}

		[DashboardTool(DashboardToolAttribute.Biomes, "Freeze GPU tree/rock/detail culling (play mode)", Section = "Diagnostics", Order = 113, AllowInPlayMode = true,
			Tooltip = "Stops TerrainGpuRenderer's culling (trees, details, cliff rocks) and keeps drawing the last results (press again to resume). A flicker that goes on while frozen is in the drawing; one that stops is in the culling.")]
		public static void ToggleGpuCullFreeze()
		{
			TerrainGpuRenderer.Freeze = !TerrainGpuRenderer.Freeze;
			Debug.Log($"[Terrain GPU] Culling {(TerrainGpuRenderer.Freeze ? "FROZEN: drawing the last results" : "running")}.");
		}

		[DashboardTool(DashboardToolAttribute.Biomes, "Skip GPU tree/rock/detail uploads (play mode A/B)", Section = "Diagnostics", Order = 114, AllowInPlayMode = true,
			Tooltip = "TerrainGpuRenderer keeps culling every frame but stops copying newly streamed instances into its instance buffer (press again to resume; new chunks stay missing meanwhile). If a movement flicker stops, it is in the uploads; if not, in the culling.")]
		public static void ToggleGpuSkipUploads()
		{
			TerrainGpuRenderer.SkipUploads = !TerrainGpuRenderer.SkipUploads;
			Debug.Log($"[Terrain GPU] Instance uploads {(TerrainGpuRenderer.SkipUploads ? "SKIPPED (new chunks stay missing)" : "running")}.");
		}

		private static void Log(string which, string report)
		{
			string text = $"[{which}] {report}";
			if (report.Contains("MISMATCH"))
			{
				Debug.LogWarning(text);
			}
			else
			{
				Debug.Log(text);
			}
		}

		private static string PathWhenPlaying()
		{
			bool enabled = SessionState.GetBool(TerrainTreeInstancing.EnabledKey, true);
			bool prefer = SessionState.GetBool(TerrainTreeInstancing.PreferGpuKey, true);
			if (!enabled)
			{
				return "Unity's own drawing";
			}
			return prefer
				? (SystemInfo.supportsComputeShaders ? "GPU-driven where the platform allows" : "CPU fallback (no compute on this device)")
				: "CPU fallback (forced)";
		}

		/// <summary>The live state, e.g. "GPU-driven: ON (trees 61/79 models on GPU, details 12/12)".</summary>
		private static string State()
		{
			if (!TerrainTreeInstancing.Enabled)
			{
				return "Instanced terrain renderers OFF: Unity draws trees and details.";
			}
			string trees = $"trees {TerrainTreeInstancing.GpuModelCount}/{TerrainTreeInstancing.ModelCount} models on GPU on {TerrainTreeInstancing.DrivenTerrainCount} terrains";
			string details = $"details {TerrainDetailInstancing.GpuModelCount}/{TerrainDetailInstancing.ModelCount}, {TerrainDetailInstancing.ResidentChunks} chunks";
			if (TerrainTreeInstancing.GpuActive || TerrainDetailInstancing.GpuActive)
			{
				return $"GPU-driven: ON ({trees}, {details}).";
			}
			return $"CPU fallback ({(TerrainTreeInstancing.PreferGpu ? "no compute or no shaders" : "forced")}; {trees.Replace(" on GPU", "")}, {details}).";
		}

		/// <summary>Runs <paramref name="action"/> once a few frames have passed (the switch takes the terrains back on the next frame).</summary>
		private static void AfterFrames(Action action)
		{
			int until = Time.frameCount + SettleFrames;
			void Tick()
			{
				if (Application.isPlaying && Time.frameCount < until)
				{
					return;
				}
				EditorApplication.update -= Tick;
				if (Application.isPlaying)
				{
					action();
				}
			}
			EditorApplication.update += Tick;
		}
	}
}
