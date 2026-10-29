#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEditor.Build;
using UnityEditor.Rendering;
using UnityEngine;
using UnityEngine.Rendering;

namespace FishMMO.Shared.CustomBuildTool
{
	/// <summary>Remove Editor profiling variants and unused WebGPU paths of the dedicated indirect twins.</summary>
	public sealed class ClientShaderBuildProcessor : IPreprocessShaders
	{
		public const string DiagnosticsKeyword = "FISHMMO_SHADER_DIAGNOSTICS";
		// Apply after URP stripping and before the optional client shader audit samples its input.
		public int callbackOrder => int.MaxValue - 1;

		public void OnProcessShader(Shader shader, ShaderSnippetData snippet, IList<ShaderCompilerData> variants)
		{
			bool indirect = shader.name == "FishMMO/Vegetation Indirect" || shader.name == "FishMMO/Weather Lit Indirect";
			bool hasDiagnostics = shader.keywordSpace.FindKeyword(DiagnosticsKeyword).isValid;
			if (!indirect && !hasDiagnostics) return;
			var procedural = indirect ? new ShaderKeyword(shader, "PROCEDURAL_INSTANCING_ON") : default;
			var dots = indirect ? new ShaderKeyword(shader, "DOTS_INSTANCING_ON") : default;
			var lodGroupFade = indirect ? new ShaderKeyword(shader, "LOD_FADE_CROSSFADE") : default;
			var diagnostics = hasDiagnostics ? new ShaderKeyword(shader, DiagnosticsKeyword) : default;
			for (int i = variants.Count - 1; i >= 0; i--)
			{
				// These probes deliberately repeat whole density/light calculations and are Editor tools only.
				bool editorProbe = hasDiagnostics && variants[i].shaderKeywordSet.IsEnabled(diagnostics);
				// TerrainGpuRenderer's runtime clones draw with RenderMeshIndirect (procedural variants) or through its
				// BatchRendererGroup (DOTS variants, TerrainGpuRenderer.Brg.cs). The plain and INSTANCING_ON paths remain
				// on the original shaders for MeshRenderers and the GPU Resident Drawer.
				// Neither path supplies unity_LODFade through the keyword: transitions use FishLodFadeClip (the
				// procedural fade, or the DOTS visible index's packed byte), so the LODGroup keyword is unused as well.
				// WebGPU combines vertex/fragment compilation; leave other compilers' stage variants alone.
				bool usedIndirect = variants[i].shaderKeywordSet.IsEnabled(procedural) || variants[i].shaderKeywordSet.IsEnabled(dots);
				bool unusedIndirectPath = indirect && variants[i].shaderCompilerPlatform == ShaderCompilerPlatform.WebGPU &&
					(!usedIndirect || variants[i].shaderKeywordSet.IsEnabled(lodGroupFade));
				if (editorProbe || unusedIndirectPath) variants.RemoveAt(i);
			}
		}
	}
}
#endif
