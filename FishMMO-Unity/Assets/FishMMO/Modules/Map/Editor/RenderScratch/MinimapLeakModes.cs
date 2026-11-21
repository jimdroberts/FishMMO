using System;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Client;
using FishMMO.Shared;

namespace FishMMO.RenderScratch
{
	/// <summary>
	/// The play-mode leak probe's minimap and marker modes (<c>minimap</c>, <c>minimap-render</c>, <c>minimap-refresh</c>,
	/// <c>minimap-standard</c>, <c>markers</c>, <c>markers-still</c>): the real minimap renderer and map view in the probe's
	/// document. Moved out of <see cref="PlayLeakProbe"/> with the Map module, 2026-11-20; registered by
	/// <see cref="MapRenderScratch"/>.
	/// </summary>
	public sealed class MinimapLeakModes : PlayLeakProbe.ILeakProbeMode
	{
		private MinimapCameraRenderer minimapRenderer;
		private UITKMapView mapView;
		private GameObject minimapCameraObject;
		private int minimapRenders;
		private int frame;

		/// <inheritdoc />
		public bool Handles(string mode) => mode.StartsWith("minimap", StringComparison.Ordinal) || mode.StartsWith("markers", StringComparison.Ordinal);

		/// <inheritdoc />
		public void Build(UIDocument document)
		{
			frame = 0;
			minimapRenders = 0;
			nextStandardTime = 0;
			markerRefreshes = 0;
			nextMarkerRefresh = 0;
			markerBuffer.Clear();
			BuildMinimap(document);
		}

		/// <inheritdoc />
		public void Step(string mode)
		{
			++frame;
			switch (mode)
			{
				case "minimap":
				case "minimap-render":
				case "minimap-refresh":
					StepMinimap(mode);
					return;

				case "markers":
				case "markers-still":
					StepMarkers(mode == "markers");
					return;

				case "minimap-standard":
					StepMinimapStandard();
					return;
			}
		}

		/// <inheritdoc />
		public string Describe()
		{
			string map = mapView == null ? "n/a" : $"{mapView.resolvedStyle.width}x{mapView.resolvedStyle.height} panel={(mapView.panel == null ? "none" : "attached")} texture={(minimapRenderer?.Texture == null ? "none" : minimapRenderer.Texture.width + "x" + minimapRenderer.Texture.height)}";
			return $"minimap {map}, minimap renders {minimapRenders}, marker refreshes {markerRefreshes}, markers laid out {mapView?.childCount}";
		}

		/// <inheritdoc />
		public void Release()
		{
			if (mapView != null) { mapView.RemoveFromHierarchy(); mapView = null; }
			minimapRenderer?.Dispose();
			minimapRenderer = null;
			if (minimapCameraObject != null) { UnityEngine.Object.Destroy(minimapCameraObject); minimapCameraObject = null; }
		}

		private const int MarkerCount = 40;
		private readonly System.Collections.Generic.List<MapMarkerSnapshot> markerBuffer = new System.Collections.Generic.List<MapMarkerSnapshot>(MarkerCount);
		private double nextMarkerRefresh;
		private int markerRefreshes;

		/// <summary>
		/// UITKMinimap.LateUpdate with NPC markers: render at the capped rate, collect and set markers
		/// ten times a second, and re-lay them out on every other frame. Positions move every frame
		/// when <paramref name="moving"/>, as wandering NPCs do; the still mode is the control.
		/// </summary>
		private void StepMarkers(bool moving)
		{
			if (minimapRenderer == null || mapView == null)
			{
				return;
			}

			MapViewTransform view = new MapViewTransform(Vector3.zero, 25.0f, 0.0f);
			mapView.View = view;
			if (minimapRenderer.Render(view))
			{
				++minimapRenders;
				mapView.MapTexture = minimapRenderer.Texture;
				mapView.RefreshSurface();
			}

			double now = Time.unscaledTimeAsDouble;
			if (now >= nextMarkerRefresh)
			{
				nextMarkerRefresh = now + 0.1;
				++markerRefreshes;
				markerBuffer.Clear();
				for (int i = 0; i < MarkerCount; ++i)
				{
					float phase = moving ? frame * 0.01f + i : i;
					markerBuffer.Add(new MapMarkerSnapshot
					{
						Source = null,
						Position = new Vector3(Mathf.Sin(phase) * 18f, 0f, Mathf.Cos(phase * 0.7f) * 18f),
						TracksSource = false,
						HasFacing = false,
						Type = MapMarkerType.NPC,
						Relationship = default,
						Icon = null,
						Tint = Color.white,
						Label = "Wandering NPC " + (i % 5),
						Tooltip = "NPC",
						Size = 12f,
						ClampToEdge = false,
						Priority = i,
					});
				}
				mapView.SetMarkers(markerBuffer);
			}
			else
			{
				if (moving)
				{
					for (int i = 0; i < markerBuffer.Count; ++i)
					{
						MapMarkerSnapshot m = markerBuffer[i];
						float phase = frame * 0.01f + i;
						m.Position = new Vector3(Mathf.Sin(phase) * 18f, 0f, Mathf.Cos(phase * 0.7f) * 18f);
						markerBuffer[i] = m;
					}
					mapView.SetMarkers(markerBuffer);
				}
				else
				{
					mapView.RelayoutMarkers();
				}
			}
		}

		/// <summary>Builds the minimap exactly as UITKMinimap does: a view-aligned map view fed by the shared renderer.</summary>
		private void BuildMinimap(UIDocument document)
		{
			minimapCameraObject = new GameObject("LeakProbeMinimapCamera");
			Camera camera = minimapCameraObject.AddComponent<Camera>();
			camera.enabled = false;

			minimapRenderer = new MinimapCameraRenderer();
			minimapRenderer.Configure(camera, 0, Cartography.MinimapResolution);
			minimapRenderer.FramesPerSecond = 30.0f;

			mapView = new UITKMapView();
			mapView.MapTextureIsViewAligned = true;
			mapView.style.position = Position.Absolute;
			mapView.style.left = 20;
			mapView.style.top = 20;
			mapView.style.width = 256;
			mapView.style.height = 256;
			if (document == null || document.rootVisualElement == null || document.rootVisualElement.panel == null)
			{
				throw new InvalidOperationException("the document has no root attached to a panel, so the minimap would never repaint; the measurement would be meaningless");
			}
			document.rootVisualElement.Add(mapView);
		}

		/// <summary>UITKMinimap.LateUpdate's render path, optionally split into its render and surface-refresh halves.</summary>
		private void StepMinimap(string mode)
		{
			if (minimapRenderer == null || mapView == null)
			{
				return;
			}

			MapViewTransform view = new MapViewTransform(new Vector3(Mathf.Sin(frame * 0.01f) * 5f, 0f, 0f), 25.0f, 0.0f);
			mapView.View = view;

			if (mode == "minimap-refresh")
			{
				// Refresh half only: one render to have a texture, then reassign and repaint at the render rate.
				if (minimapRenderer.Texture == null || minimapRenders == 0)
				{
					if (minimapRenderer.Render(view, true)) { ++minimapRenders; mapView.MapTexture = minimapRenderer.Texture; }
					return;
				}
				if (frame % 2 == 0)
				{
					mapView.MapTexture = minimapRenderer.Texture;
					mapView.RefreshSurface();
				}
				return;
			}

			if (minimapRenderer.Render(view))
			{
				++minimapRenders;
				if (mode == "minimap")
				{
					mapView.MapTexture = minimapRenderer.Texture;
					mapView.RefreshSurface();
				}
			}
		}

		private double nextStandardTime;

		/// <summary>
		/// The minimap path exactly as the shipped client ran it before the working-tree change: the
		/// renderer's texture and settings, but a RenderPipeline.StandardRequest at 30 per second,
		/// then the surface refresh.
		/// </summary>
		private void StepMinimapStandard()
		{
			if (minimapRenderer == null || mapView == null)
			{
				return;
			}

			MapViewTransform view = new MapViewTransform(new Vector3(Mathf.Sin(frame * 0.01f) * 5f, 0f, 0f), 25.0f, 0.0f);
			mapView.View = view;

			if (minimapRenderer.Texture == null)
			{
				// One ordinary render creates the texture and applies the camera settings.
				if (minimapRenderer.Render(view, true)) { ++minimapRenders; mapView.MapTexture = minimapRenderer.Texture; }
				return;
			}

			double now = Time.unscaledTimeAsDouble;
			if (now < nextStandardTime)
			{
				return;
			}
			nextStandardTime = now + (1.0 / 30.0);

			Camera camera = minimapCameraObject.GetComponent<Camera>();
			UnityEngine.Rendering.RenderPipeline.StandardRequest request = new UnityEngine.Rendering.RenderPipeline.StandardRequest { destination = minimapRenderer.Texture };
			if (UnityEngine.Rendering.RenderPipeline.SupportsRenderRequest(camera, request))
			{
				camera.SubmitRenderRequest(request);
				++minimapRenders;
				mapView.MapTexture = minimapRenderer.Texture;
				mapView.RefreshSurface();
			}
		}
	}
}
