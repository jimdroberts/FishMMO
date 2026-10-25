using UnityEditor;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>
	/// Existing Editor performance probes set these globals. Compile their deliberately duplicated
	/// shader work only while a probe is active; player builds remove the diagnostic variant.
	/// </summary>
	[InitializeOnLoad]
	public static class ShaderProbeDiagnostics
	{
		public const string Keyword = FishMMO.Shared.CustomBuildTool.ClientShaderBuildProcessor.DiagnosticsKeyword;
		private static readonly int[] ProbeIds =
		{
			Shader.PropertyToID("_FishCloudFieldSkip"), Shader.PropertyToID("_FishVegetationDiag"),
			Shader.PropertyToID("_FishVegContactAlways"), Shader.PropertyToID("_FishVegPlantConst"),
		};

		static ShaderProbeDiagnostics()
		{
			EditorApplication.update += Sync;
			Sync();
		}

		/// <summary>Also callable before a probe's immediate Camera.Render, without waiting for an Editor update.</summary>
		public static void Sync()
		{
			bool active = false;
			foreach (int id in ProbeIds) active |= Shader.GetGlobalFloat(id) != 0f;
			if (Shader.IsKeywordEnabled(Keyword) == active) return;
			if (active) Shader.EnableKeyword(Keyword);
			else Shader.DisableKeyword(Keyword);
		}
	}
}
