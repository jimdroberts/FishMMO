using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Experimental.Rendering;
using UnityEngine.Rendering;
using UnityEngine.Rendering.RenderGraphModule;
using UnityEngine.Rendering.Universal;
using FishMMO.Shared;

namespace FishMMO.Client
{
	/// <summary>
	/// Draws <see cref="TargetOutline"/>'s target: a ring of the outline colour and width round the
	/// object's visible silhouette.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>Three passes, after post-processing</b>, so the colour lands exactly as chosen (no tonemapping,
	/// grading or bloom) and before the UI:
	/// </para>
	/// <list type="number">
	/// <item><b>Mask</b> (RGBA8): each outlined renderer drawn flat, rgb its colour and alpha its width.
	/// Fragments behind the scene's depth are dropped, so only the visible parts are outlined.</item>
	/// <item><b>Nearest in the row</b> (R8): for each pixel, the signed column offset to the nearest mask
	/// pixel in its row within the outline radius, or none.</item>
	/// <item><b>Ring</b>: for each pixel outside the mask, the nearest mask pixel over the column (offset²
	/// + row²: the exact Euclidean nearest within the radius), blended onto the frame with an
	/// antialiased edge at the width that pixel carries.</item>
	/// </list>
	/// <para>
	/// The two full-screen passes do their work only inside the screen rectangle the outlined renderers'
	/// bounds cover, widened by the radius; everywhere else they return at once. With nothing outlined, no
	/// pass is added at all; with the target off screen, none is recorded.
	/// </para>
	/// <para>
	/// Only the main camera (tagged MainCamera) and the scene view draw outlines: the minimap and other
	/// utility cameras do not.
	/// </para>
	/// <para>
	/// Every texture is RGBA8 or R8 (Firefox's WebGPU cannot sample R16), and every read is a texel load
	/// at the pixel's own coordinate, so nothing depends on samplers or on which way up a target is stored.
	/// </para>
	/// </remarks>
	public class TargetOutlineFeature : ScriptableRendererFeature
	{
		public const string ShaderName = "Hidden/FishMMO/TargetOutline";

		/// <summary>The width the mask's alpha holds at 1, in pixels: room for <see cref="TargetOutline.MaxWidth"/> at a render scale above 1.</summary>
		public const float EncodedWidth = 16f;

		[Tooltip("The outline shader (Hidden/FishMMO/TargetOutline). Referenced here so a build carries it: Shader.Find " +
			"only finds a shader something in the build references. Left empty, it is found by name, which works in the editor alone.")]
		public Shader OutlineShader;

		[System.NonSerialized] private Material created;

		private OutlinePass pass;

		public override void Create()
		{
			pass = new OutlinePass
			{
				renderPassEvent = RenderPassEvent.AfterRenderingPostProcessing,
				// Post-processing would otherwise write straight to the back buffer, which a pass cannot draw over.
				requiresIntermediateTexture = true,
			};
			pass.ConfigureInput(ScriptableRenderPassInput.Depth);
		}

		public override void AddRenderPasses(ScriptableRenderer renderer, ref RenderingData renderingData)
		{
			if (!TargetOutline.HasTarget)
			{
				return;
			}
			CameraType type = renderingData.cameraData.cameraType;
			if (type != CameraType.Game && type != CameraType.SceneView)
			{
				return;
			}
			if (renderingData.cameraData.renderType == CameraRenderType.Overlay)
			{
				return;
			}
			if (type == CameraType.Game && !renderingData.cameraData.camera.CompareTag("MainCamera"))
			{
				return;
			}
			Material material = Resolve();
			if (material == null)
			{
				return;
			}
			pass.Setup(material);
			renderer.EnqueuePass(pass);
		}

		private Material Resolve()
		{
			if (created != null)
			{
				return created;
			}
			Shader shader = OutlineShader != null ? OutlineShader : ShaderLookup.Find(ShaderName);
			if (shader == null || !shader.isSupported)
			{
				return null;
			}
			created = CoreUtils.CreateEngineMaterial(shader);
			return created;
		}

		protected override void Dispose(bool disposing)
		{
			if (created != null)
			{
				CoreUtils.Destroy(created);
				created = null;
			}
			pass = null;
		}

		private sealed class OutlinePass : ScriptableRenderPass
		{
			private const int MaskPass = 0;
			private const int NearestPass = 1;
			private const int RingPass = 2;

			private static readonly int DepthId = Shader.PropertyToID("_FishOutlineDepth");
			private static readonly int MaskId = Shader.PropertyToID("_FishOutlineMask");
			private static readonly int NearestId = Shader.PropertyToID("_FishOutlineNearest");
			private static readonly int MaskColorId = Shader.PropertyToID("_FishOutlineMaskColor");
			private static readonly int MaskParamsId = Shader.PropertyToID("_FishOutlineMaskParams");
			private static readonly int RectId = Shader.PropertyToID("_FishOutlineRect");
			private static readonly int SizeId = Shader.PropertyToID("_FishOutlineSize");

			private static readonly Vector3[] Corners = new Vector3[8];

			private Material material;

			// What this camera draws, gathered while recording and read while executing. URP records and
			// executes one camera before the next, so one set of lists serves every camera.
			private readonly List<Renderer> drawRenderers = new List<Renderer>();
			private readonly List<int> drawSubmeshes = new List<int>();
			private readonly List<int> drawOutline = new List<int>();
			private readonly List<Vector4> outlineColors = new List<Vector4>();
			private readonly List<Vector4> outlineParams = new List<Vector4>();

			public void Setup(Material outline)
			{
				material = outline;
			}

			private class MaskData
			{
				public Material Material;
				public TextureHandle Depth;
				public TextureHandle Mask;
				public Matrix4x4 View;
				public Matrix4x4 Projection;
				public OutlinePass Owner;
			}

			private class ScreenData
			{
				public Material Material;
				public TextureHandle Mask;
				public TextureHandle Nearest;
				public Rect Bounds;
				public Vector4 Size;
				public int Pass;
			}

			public override void RecordRenderGraph(RenderGraph renderGraph, ContextContainer frameData)
			{
				if (material == null)
				{
					return;
				}
				UniversalCameraData cameraData = frameData.Get<UniversalCameraData>();
				UniversalResourceData resourceData = frameData.Get<UniversalResourceData>();
				if (resourceData.isActiveTargetBackBuffer || !resourceData.cameraDepthTexture.IsValid())
				{
					return;
				}

				int width = Mathf.Max(1, cameraData.cameraTargetDescriptor.width);
				int height = Mathf.Max(1, cameraData.cameraTargetDescriptor.height);
				Matrix4x4 view = cameraData.GetViewMatrix();
				Matrix4x4 projection = cameraData.GetProjectionMatrix();
				// Widths are in screen pixels; the frame here is drawn at the render scale.
				float scale = Mathf.Max(0.01f, cameraData.renderScale);
				if (!Gather(projection * view, width, height, scale, out Rect bounds, out float widest))
				{
					return;
				}

				int radius = Mathf.CeilToInt(widest + 0.5f);
				bounds = Widen(bounds, radius, width, height);
				if (bounds.width <= 0f || bounds.height <= 0f)
				{
					return;
				}
				var size = new Vector4(radius, EncodedWidth, width, height);

				TextureDesc desc = renderGraph.GetTextureDesc(resourceData.activeColorTexture);
				desc.msaaSamples = MSAASamples.None;
				desc.bindTextureMS = false;
				desc.filterMode = FilterMode.Point;
				desc.wrapMode = TextureWrapMode.Clamp;
				desc.clearBuffer = true;
				desc.clearColor = Color.clear;

				desc.name = "_FishOutlineMask";
				desc.format = GraphicsFormat.R8G8B8A8_UNorm;
				TextureHandle mask = renderGraph.CreateTexture(desc);

				desc.name = "_FishOutlineNearest";
				desc.format = GraphicsFormat.R8_UNorm;
				TextureHandle nearest = renderGraph.CreateTexture(desc);

				using (var builder = renderGraph.AddRasterRenderPass<MaskData>("Fish Target Outline Mask", out MaskData data))
				{
					data.Material = material;
					data.Depth = resourceData.cameraDepthTexture;
					data.Mask = mask;
					data.View = view;
					data.Projection = projection;
					data.Owner = this;
					builder.SetRenderAttachment(mask, 0);
					builder.UseTexture(resourceData.cameraDepthTexture, AccessFlags.Read);
					builder.AllowGlobalStateModification(true);
					builder.SetRenderFunc((MaskData d, RasterGraphContext context) => d.Owner.DrawMask(d, context));
				}

				using (var builder = renderGraph.AddRasterRenderPass<ScreenData>("Fish Target Outline Nearest", out ScreenData data))
				{
					data.Material = material;
					data.Mask = mask;
					data.Nearest = nearest;
					data.Bounds = bounds;
					data.Size = size;
					data.Pass = NearestPass;
					builder.SetRenderAttachment(nearest, 0);
					builder.UseTexture(mask, AccessFlags.Read);
					builder.AllowGlobalStateModification(true);
					builder.SetRenderFunc((ScreenData d, RasterGraphContext context) => DrawScreen(d, context));
				}

				using (var builder = renderGraph.AddRasterRenderPass<ScreenData>("Fish Target Outline", out ScreenData data))
				{
					data.Material = material;
					data.Mask = mask;
					data.Nearest = nearest;
					data.Bounds = bounds;
					data.Size = size;
					data.Pass = RingPass;
					builder.SetRenderAttachment(resourceData.activeColorTexture, 0);
					builder.UseTexture(mask, AccessFlags.Read);
					builder.UseTexture(nearest, AccessFlags.Read);
					builder.AllowGlobalStateModification(true);
					builder.AllowPassCulling(false);
					builder.SetRenderFunc((ScreenData d, RasterGraphContext context) => DrawScreen(d, context));
				}
			}

			/// <summary>
			/// Lists what this camera outlines and the screen rectangle it covers, in pixels from the bottom
			/// left. False when nothing enabled is on screen.
			/// </summary>
			private bool Gather(Matrix4x4 viewProjection, int width, int height, float scale, out Rect bounds, out float widest)
			{
				drawRenderers.Clear();
				drawSubmeshes.Clear();
				drawOutline.Clear();
				outlineColors.Clear();
				outlineParams.Clear();
				widest = 0f;
				float xMin = float.MaxValue, yMin = float.MaxValue, xMax = float.MinValue, yMax = float.MinValue;
				bool linear = QualitySettings.activeColorSpace == ColorSpace.Linear;

				float pixels = Mathf.Min(TargetOutline.Width * scale, EncodedWidth);
				if (pixels <= 0f || !TargetOutline.HasTarget)
				{
					bounds = default;
					return false;
				}

				IReadOnlyList<Renderer> renderers = TargetOutline.Renderers;
				for (int r = 0; r < renderers.Count; r++)
				{
					Renderer renderer = renderers[r];
					if (renderer == null || !renderer.enabled || renderer.forceRenderingOff || !renderer.isVisible ||
						!renderer.gameObject.activeInHierarchy)
					{
						continue;
					}
					int submeshes = SubmeshCount(renderer);
					if (submeshes == 0)
					{
						continue;
					}
					if (!Project(renderer.bounds, viewProjection, width, height, ref xMin, ref yMin, ref xMax, ref yMax))
					{
						continue;
					}
					drawRenderers.Add(renderer);
					drawSubmeshes.Add(submeshes);
					drawOutline.Add(0);
				}

				if (drawRenderers.Count > 0)
				{
					Color c = linear ? TargetOutline.Color.linear : TargetOutline.Color;
					// The alpha carries the width; never quite 0, which reads as "no mask here".
					outlineColors.Add(new Vector4(c.r, c.g, c.b, Mathf.Max(1f / 255f, pixels / EncodedWidth)));
					// x: 1 would draw the whole silhouette over whatever is in front; the aim outline never does.
					outlineParams.Add(Vector4.zero);
					widest = pixels;
				}

				bounds = drawRenderers.Count > 0 ? Rect.MinMaxRect(xMin, yMin, xMax, yMax) : default;
				return drawRenderers.Count > 0;
			}

			private static int SubmeshCount(Renderer renderer)
			{
				Mesh mesh = null;
				if (renderer is SkinnedMeshRenderer skinned)
				{
					mesh = skinned.sharedMesh;
				}
				else if (renderer.TryGetComponent(out MeshFilter filter))
				{
					mesh = filter.sharedMesh;
				}
				return mesh != null ? mesh.subMeshCount : 0;
			}

			/// <summary>
			/// Grows the rectangle by a world box's projection. A box reaching behind the camera covers the
			/// whole screen; one wholly off screen adds nothing and returns false.
			/// </summary>
			private static bool Project(Bounds box, Matrix4x4 viewProjection, int width, int height,
				ref float xMin, ref float yMin, ref float xMax, ref float yMax)
			{
				Vector3 c = box.center;
				Vector3 e = box.extents;
				int n = 0;
				for (int sx = -1; sx <= 1; sx += 2)
				{
					for (int sy = -1; sy <= 1; sy += 2)
					{
						for (int sz = -1; sz <= 1; sz += 2)
						{
							Corners[n++] = new Vector3(c.x + sx * e.x, c.y + sy * e.y, c.z + sz * e.z);
						}
					}
				}

				float bxMin = float.MaxValue, byMin = float.MaxValue, bxMax = float.MinValue, byMax = float.MinValue;
				for (int i = 0; i < 8; i++)
				{
					Vector4 clip = viewProjection * new Vector4(Corners[i].x, Corners[i].y, Corners[i].z, 1f);
					if (clip.w <= 1e-4f)
					{
						// Part of the box is behind the eye: its projection is unbounded.
						xMin = 0f;
						yMin = 0f;
						xMax = Mathf.Max(xMax, width);
						yMax = Mathf.Max(yMax, height);
						return true;
					}
					float x = (clip.x / clip.w * 0.5f + 0.5f) * width;
					float y = (clip.y / clip.w * 0.5f + 0.5f) * height;
					bxMin = Mathf.Min(bxMin, x);
					byMin = Mathf.Min(byMin, y);
					bxMax = Mathf.Max(bxMax, x);
					byMax = Mathf.Max(byMax, y);
				}
				if (bxMax < 0f || byMax < 0f || bxMin > width || byMin > height)
				{
					return false;
				}
				xMin = Mathf.Min(xMin, bxMin);
				yMin = Mathf.Min(yMin, byMin);
				xMax = Mathf.Max(xMax, bxMax);
				yMax = Mathf.Max(yMax, byMax);
				return true;
			}

			private static Rect Widen(Rect rect, int radius, int width, int height)
			{
				float xMin = Mathf.Clamp(Mathf.Floor(rect.xMin) - radius - 1, 0, width);
				float yMin = Mathf.Clamp(Mathf.Floor(rect.yMin) - radius - 1, 0, height);
				float xMax = Mathf.Clamp(Mathf.Ceil(rect.xMax) + radius + 1, 0, width);
				float yMax = Mathf.Clamp(Mathf.Ceil(rect.yMax) + radius + 1, 0, height);
				return Rect.MinMaxRect(xMin, yMin, xMax, yMax);
			}

			private void DrawMask(MaskData d, RasterGraphContext context)
			{
				RasterCommandBuffer cmd = context.cmd;
				// Drawn the way up URP draws into this kind of target, which is the way the depth texture was drawn.
				bool flip = context.GetTextureUVOrigin(d.Mask) == TextureUVOrigin.BottomLeft;
				RenderingUtils.SetViewAndProjectionMatrices(cmd, d.View, GL.GetGPUProjectionMatrix(d.Projection, flip), false);
				cmd.SetGlobalTexture(DepthId, d.Depth);
				int current = -1;
				for (int i = 0; i < drawRenderers.Count; i++)
				{
					Renderer renderer = drawRenderers[i];
					if (renderer == null)
					{
						continue;
					}
					int outline = drawOutline[i];
					if (outline != current)
					{
						current = outline;
						cmd.SetGlobalVector(MaskColorId, outlineColors[outline]);
						cmd.SetGlobalVector(MaskParamsId, outlineParams[outline]);
					}
					int submeshes = drawSubmeshes[i];
					for (int s = 0; s < submeshes; s++)
					{
						cmd.DrawRenderer(renderer, d.Material, s, MaskPass);
					}
				}
			}

			private static void DrawScreen(ScreenData d, RasterGraphContext context)
			{
				RasterCommandBuffer cmd = context.cmd;
				// The rectangle is measured from the bottom left; a target stored top row first counts rows from the
				// top. The mask, the row texture and the frame are all intermediates and are stored the same way up.
				Rect b = d.Bounds;
				float height = d.Size.w;
				Vector4 rect = context.GetTextureUVOrigin(d.Mask) == TextureUVOrigin.BottomLeft
					? new Vector4(b.xMin, b.yMin, b.xMax, b.yMax)
					: new Vector4(b.xMin, height - b.yMax, b.xMax, height - b.yMin);
				cmd.SetGlobalVector(RectId, rect);
				cmd.SetGlobalVector(SizeId, d.Size);
				cmd.SetGlobalTexture(MaskId, d.Mask);
				if (d.Pass == RingPass)
				{
					cmd.SetGlobalTexture(NearestId, d.Nearest);
				}
				Blitter.BlitTexture(cmd, new Vector4(1f, 1f, 0f, 0f), d.Material, d.Pass);
			}
		}
	}
}
