#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishMMO.Shared.CustomBuildTool
{
	/// <summary>Remove Editor profiling variants and the paths the dedicated indirect twins are never drawn with.</summary>
	public sealed class ClientShaderBuildProcessor : IPreprocessShaders
	{
		public const string DiagnosticsKeyword = "FISHMMO_SHADER_DIAGNOSTICS";
		public const string DotsKeyword = "DOTS_INSTANCING_ON";
		public const string FirstInstanceKeyword = "FISH_INDIRECT_FIRST_INSTANCE";
		// Apply after URP stripping and before the optional client shader audit samples its input.
		public int callbackOrder => int.MaxValue - 1;

		/// <summary>The shaders TerrainGpuRenderer's BatchRendererGroup draws with: the only DOTS variants a WebGPU build keeps.</summary>
		public static bool IsBrgTwin(string shaderName) =>
			shaderName == "FishMMO/Vegetation Indirect" || shaderName == "FishMMO/Weather Lit Indirect" || shaderName == "FishMMO/Vegetation Prime";

		/// <summary>
		/// Whether a twin's variant is one TerrainGpuRenderer never draws. Its runtime clones draw with RenderMeshIndirect
		/// (procedural variants) or through its BatchRendererGroup (DOTS variants, TerrainGpuRenderer.Brg.cs); the plain and
		/// INSTANCING_ON paths remain on the original shaders for MeshRenderers and the GPU Resident Drawer. Neither path
		/// supplies unity_LODFade through the keyword: transitions use FishLodFadeClip (the procedural fade, or the DOTS visible
		/// index's packed byte, BatchDrawCommandFlags.LODCrossFadeValuePacked without LODCrossFadeKeyword), so the LODGroup
		/// keyword is unused as well. FISH_INDIRECT_FIRST_INSTANCE is a WebGPU device feature: every other player keeps it
		/// globally off (WebGpuDrawCapabilities), and FishIndirectInstancing.hlsl reads it under SHADER_API_WEBGPU only, so off
		/// WebGPU its two states are the same program and the enabled one is never requested.
		/// On WebGPU the rule covers the two indirect shaders as before; natively (2026-11-03, a strict-variant-matching
		/// player, Docs/Rendering/2026-11-03/shader-compile-cost) it covers all three twins.
		/// </summary>
		public static bool IsUnusedTwinVariant(bool webGpu, bool indirect, bool twin, bool procedural, bool dots, bool lodGroupFade, bool firstInstance)
		{
			bool usedIndirect = procedural || dots;
			if (webGpu) return indirect && (!usedIndirect || lodGroupFade);
			return twin && (!usedIndirect || lodGroupFade || firstInstance);
		}

		public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> variants)
		{
			bool indirect = shader.name == "FishMMO/Vegetation Indirect" || shader.name == "FishMMO/Weather Lit Indirect";
			bool twin = IsBrgTwin(shader.name);
			bool hasDiagnostics = shader.keywordSpace.FindKeyword(DiagnosticsKeyword).isValid;
			// WebGPU builds keep BatchRendererGroup variants (WebGLRenderPipelineScope.BrowserBrgStripping) for the twins only:
			// the GPU Resident Drawer is off in the browser, and only the twins' DOTS variants fit its 12 uniform buffers a stage
			// (FishWeatherLitInput.hlsl / FishIndirectInstancing.hlsl), so every other shader's are removed there as StripAll did.
			bool foreignDots = !IsBrgTwin(shader.name) && shader.keywordSpace.FindKeyword(DotsKeyword).isValid;
			if (!twin && !hasDiagnostics && !foreignDots) return;
			var procedural = twin ? new ShaderKeyword(shader, "PROCEDURAL_INSTANCING_ON") : default;
			var dots = twin || foreignDots ? new ShaderKeyword(shader, DotsKeyword) : default;
			var lodGroupFade = twin ? new ShaderKeyword(shader, "LOD_FADE_CROSSFADE") : default;
			var firstInstance = twin ? new ShaderKeyword(shader, FirstInstanceKeyword) : default;
			var diagnostics = hasDiagnostics ? new ShaderKeyword(shader, DiagnosticsKeyword) : default;
			for (int i = variants.Count - 1; i >= 0; i--)
			{
				// These probes deliberately repeat whole density/light calculations and are Editor tools only.
				bool editorProbe = hasDiagnostics && variants[i].shaderKeywordSet.IsEnabled(diagnostics);
				bool webGpu = variants[i].shaderCompilerPlatform == ShaderCompilerPlatform.WebGPU;
				// Every keyword tested here is a multi_compile of all stages, so a stage-split compiler (Vulkan) removes the
				// same variant from each of its stages.
				bool unusedIndirectPath = twin && IsUnusedTwinVariant(webGpu, indirect, twin,
					variants[i].shaderKeywordSet.IsEnabled(procedural), variants[i].shaderKeywordSet.IsEnabled(dots),
					variants[i].shaderKeywordSet.IsEnabled(lodGroupFade), variants[i].shaderKeywordSet.IsEnabled(firstInstance));
				bool unusedDots = foreignDots && webGpu && variants[i].shaderKeywordSet.IsEnabled(dots);
				if (editorProbe || unusedIndirectPath || unusedDots) variants.RemoveAt(i);
			}
		}
	}
}
#endif
