using System.Reflection;
using System.Runtime.CompilerServices;
using UnityEditor;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.Rendering.Universal.Internal;

namespace FishMMO.Client
{
	/// <summary>
	/// Corrects URP's editor-only "Final Depth Copy" so it reads a hardware-resolved MSAA depth attachment as the plain
	/// texture it is. Without this, the Game view logs "A non-multisampled texture being bound to a multisampled sampler…
	/// enable the bindMS flag" every frame while Gizmos are on.
	/// </summary>
	/// <remarks>
	/// <para>
	/// With the Game view's Gizmos on, <c>UniversalRenderer</c> copies the camera depth attachment into the backbuffer
	/// depth so gizmos depth-test against the scene (<c>Handles.ShouldRenderGizmos()</c>). The pass doing it,
	/// <c>m_FinalDepthCopyPass</c>, is built with <c>copyResolvedDepth: false</c> and nothing ever updates it, while the
	/// attachment it reads is allocated with <c>bindTextureMS = hasMSAA &amp;&amp; !hardwareResolve</c>. On Vulkan the depth
	/// resolves in hardware, so the attachment is not bindable as multisampled; the copy then takes its sample count from
	/// the texture (MSAA 4 on High Fidelity), turns on <c>_DEPTH_MSAA_4</c>, and CopyDepth declares a <c>Texture2DMS</c>
	/// that Unity refuses to bind — the copy reads nothing and the gizmos lose their depth test as well. The main depth copy
	/// does not hit this because URP sets its flag from the attachment each frame; the final copy was missed.
	/// </para>
	/// <para>
	/// A URP defect in a package we do not embed (the same family as UUM-134993, still open on 6000.x), and editor-only:
	/// the pass is compiled only under <c>UNITY_EDITOR</c>, so players never run it. The flag is a device constant — URP's
	/// own rule for the attachment reduces to "hardware depth resolve, or GLES" — so each renderer is set once. Not the
	/// GPU Resident Drawer occlusion bindMS error, which <see cref="ClientDisplaySettings.MatchOcclusionToMsaa"/> handles.
	/// Delete this once URP sets <c>m_FinalDepthCopyPass.m_CopyResolvedDepth</c> itself.
	/// </para>
	/// </remarks>
	[InitializeOnLoad]
	internal static class FinalDepthCopyMsaaFix
	{
		private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.NonPublic;
		private const BindingFlags Static = BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public;

		private static readonly FieldInfo FinalDepthCopyPass = typeof(UniversalRenderer).GetField("m_FinalDepthCopyPass", Instance);
		private static readonly FieldInfo CopyResolvedDepth = typeof(CopyDepthPass).GetField("m_CopyResolvedDepth", Instance);
		private static readonly MethodInfo MultisampleDepthResolveSupported = typeof(RenderingUtils).GetMethod("MultisampleDepthResolveSupported", Static);

		/// <summary>Renderers already corrected. URP rebuilds its renderers when a pipeline asset changes, and new ones are corrected on their first camera.</summary>
		private static readonly ConditionalWeakTable<UniversalRenderer, object> corrected = new ConditionalWeakTable<UniversalRenderer, object>();

		static FinalDepthCopyMsaaFix()
		{
			if (FinalDepthCopyPass == null || CopyResolvedDepth == null || MultisampleDepthResolveSupported == null)
			{
				Debug.LogWarning("[Display] URP's final depth copy changed shape (m_FinalDepthCopyPass / m_CopyResolvedDepth / RenderingUtils.MultisampleDepthResolveSupported): FinalDepthCopyMsaaFix is inactive. If the Game view no longer logs the bindMS error with Gizmos on under MSAA, delete it.");
				return;
			}
			RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;
			RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
		}

		private static void OnBeginCameraRendering(ScriptableRenderContext context, Camera camera)
		{
			// TryGetComponent, not GetUniversalAdditionalCameraData: that adds the component to cameras that have none.
			ScriptableRenderer renderer = camera.TryGetComponent(out UniversalAdditionalCameraData cameraData)
				? cameraData.scriptableRenderer
				: UniversalRenderPipeline.asset != null ? UniversalRenderPipeline.asset.scriptableRenderer : null;
			if (!(renderer is UniversalRenderer universal) || corrected.TryGetValue(universal, out _))
			{
				return;
			}
			if (FinalDepthCopyPass.GetValue(universal) is CopyDepthPass pass)
			{
				// UniversalRenderer.CreateIntermediateCameraDepthAttachment: bindTextureMS = hasMSAA && !resolveDepth, and
				// never on GLES; resolveDepth also needs native render passes, which URP's render graph never turns off.
				// The copy reads resolved depth exactly when the attachment is not multisampled-bindable.
				bool resolved = (bool)MultisampleDepthResolveSupported.Invoke(null, null)
					|| SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3;
				CopyResolvedDepth.SetValue(pass, resolved);
			}
			corrected.Add(universal, null);
		}
	}
}
