using UnityEngine.Rendering;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The pass type Unity gives a shader pass with a LightMode tag, as a shader variant collection keys it (see
	/// ProceduralArtVariants' remarks for where the mapping comes from). Core editor (Phase 4, 2026-11-18): the shader prewarm
	/// reads it too.
	/// </summary>
	public static class ShaderPassTypes
	{
		/// <summary>The pass type for a LightMode tag, or null for one a collection leaves out (Meta).</summary>
		public static PassType? Of(string lightMode)
		{
			if (string.IsNullOrEmpty(lightMode) || lightMode == "SRPDefaultUnlit")
			{
				return PassType.ScriptableRenderPipelineDefaultUnlit;
			}
			switch (lightMode)
			{
				case "ShadowCaster":
					return PassType.ShadowCaster;
				case "MotionVectors":
					return PassType.MotionVectors;
				case "Meta":
					return null;
				default:
					return PassType.ScriptableRenderPipeline;
			}
		}
	}
}
