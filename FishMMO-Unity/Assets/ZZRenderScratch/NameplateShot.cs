using System;
using System.IO;
using System.Reflection;
using FishMMO.Client;
using FishMMO.Shared.Core;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace FishMMO.RenderScratch
{
	/// <summary>
	/// Throwaway harness: renders a set of overhead nameplates through the real
	/// <see cref="UITKNameplateLayer"/>, against a real camera, into a PNG.
	/// </summary>
	/// <remarks>
	/// Everything about the plates in the output is the shipping code: the projection, the
	/// distance-driven font size, the style resolution, the alliance blending, the row layout.
	/// The scenery behind them is painted into the panel as a background image, because a URP
	/// camera does not render on demand in edit mode.
	/// </remarks>
	public static class NameplateShot
	{
		private const string OUTPUT = "/home/jim/Dev/FishMMO-Dev/PanelRenders/Nameplates.png";
		private const string PANEL_SETTINGS_PATH = "Assets/UI Toolkit/PanelSettings.asset";
		private const string UXML_PATH = "Assets/Scripts/Client/GUI/WorldLabels/UIWorldLabels.uxml";
		private const int WIDTH = 1200;
		private const int HEIGHT = 800;
		private const int SETTLE_FRAMES = 40;

		private static RenderTexture texture;
		private static PanelSettings settings;
		private static GameObject host;
		private static GameObject cameraHost;
		private static Camera camera;
		private static UITKNameplateLayer layer;
		private static IPanel panel;
		private static int frames;

		/// <summary>
		/// Renders the Gameplay tab of the options panel, parked at the bottom where the nameplate
		/// controls live, so the section can be looked at rather than only asserted about.
		/// </summary>
		[MenuItem("FishMMO/UI Toolkit/Render Nameplate Options Page")]
		public static void RenderOptionsPage()
		{
			GameObject optionsHost = null;
			PanelSettings optionsSettings = null;
			RenderTexture optionsTexture = null;

			try
			{
				Directory.CreateDirectory(Path.GetDirectoryName(OPTIONS_OUTPUT));

				optionsTexture = new RenderTexture(OPTIONS_WIDTH, OPTIONS_HEIGHT, 24, RenderTextureFormat.ARGB32);
				optionsTexture.Create();

				optionsSettings = UnityEngine.Object.Instantiate(
					AssetDatabase.LoadAssetAtPath<PanelSettings>(PANEL_SETTINGS_PATH));
				optionsSettings.hideFlags = HideFlags.HideAndDontSave;
				optionsSettings.targetTexture = optionsTexture;
				optionsSettings.clearColor = true;
				optionsSettings.colorClearValue = new Color(0.055f, 0.059f, 0.059f, 1.0f);

				optionsHost = new GameObject("NameplateOptionsShot") { hideFlags = HideFlags.HideAndDontSave };
				UIDocument document = optionsHost.AddComponent<UIDocument>();
				document.panelSettings = optionsSettings;
				document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(OPTIONS_UXML_PATH);

				Panels.Options(optionsHost, document, "gameplay", scrollToEnd: true);
				document.rootVisualElement?.MarkDirtyRepaint();

				/* Frames rather than one pass: the tab strip and the rows carry USS transitions,
				 * and a capture taken before they settle catches them mid-flight and looks broken. */
				optionsFramesLeft = OPTIONS_SETTLE_FRAMES;
				optionsPump = () =>
				{
					if (--optionsFramesLeft > 0)
					{
						return;
					}

					EditorApplication.update -= optionsPump;
					CaptureTo(optionsTexture, OPTIONS_WIDTH, OPTIONS_HEIGHT, OPTIONS_OUTPUT);

					UnityEngine.Object.DestroyImmediate(optionsHost);
					UnityEngine.Object.DestroyImmediate(optionsSettings);
					optionsTexture.Release();
					UnityEngine.Object.DestroyImmediate(optionsTexture);

					Debug.Log($"[NameplateShot] wrote {OPTIONS_OUTPUT}");
					EditorApplication.Exit(0);
				};

				EditorApplication.update += optionsPump;
			}
			catch (Exception ex)
			{
				Debug.LogError($"[NameplateShot] options render failed: {ex}");
				EditorApplication.Exit(1);
			}
		}

		private const string OPTIONS_OUTPUT = "/home/jim/Dev/FishMMO-Dev/PanelRenders/NameplateOptions.png";
		private const string OPTIONS_UXML_PATH = "Assets/Scripts/Client/GUI/World/Options/UIOptions.uxml";
		private const int OPTIONS_WIDTH = 1000;
		private const int OPTIONS_HEIGHT = 900;
		private const int OPTIONS_SETTLE_FRAMES = 90;

		private static EditorApplication.CallbackFunction optionsPump;
		private static int optionsFramesLeft;

		[MenuItem("FishMMO/UI Toolkit/Render Nameplate Example")]
		public static void Render()
		{
			try
			{
				Build();
				EditorApplication.update -= Pump;
				EditorApplication.update += Pump;
			}
			catch (Exception ex)
			{
				Debug.LogError($"[NameplateShot] setup failed: {ex}");
				EditorApplication.Exit(1);
			}
		}

		private static void Pump()
		{
			try
			{
				Invoke(layer, "LateUpdate");
				++frames;
				if (frames < SETTLE_FRAMES)
				{
					return;
				}

				EditorApplication.update -= Pump;
				Capture();
				Teardown();
				Debug.Log($"[NameplateShot] wrote {OUTPUT}");
				EditorApplication.Exit(0);
			}
			catch (Exception ex)
			{
				EditorApplication.update -= Pump;
				Debug.LogError($"[NameplateShot] pump failed: {ex}");
				EditorApplication.Exit(1);
			}
		}

		private static void Build()
		{
			Directory.CreateDirectory(Path.GetDirectoryName(OUTPUT));

			texture = new RenderTexture(WIDTH, HEIGHT, 24, RenderTextureFormat.ARGB32);
			texture.Create();

			settings = UnityEngine.Object.Instantiate(
				AssetDatabase.LoadAssetAtPath<PanelSettings>(PANEL_SETTINGS_PATH));
			settings.hideFlags = HideFlags.HideAndDontSave;
			settings.targetTexture = texture;
			settings.clearColor = true;
			settings.colorClearValue = new Color(0.05f, 0.06f, 0.08f, 1.0f);

			cameraHost = new GameObject("ShotCamera") { hideFlags = HideFlags.HideAndDontSave };
			camera = cameraHost.AddComponent<Camera>();
			camera.fieldOfView = 55.0f;
			camera.nearClipPlane = 0.1f;
			camera.farClipPlane = 300.0f;
			camera.aspect = (float)WIDTH / HEIGHT;
			cameraHost.transform.position = new Vector3(0.0f, 1.75f, -8.0f);
			cameraHost.transform.rotation = Quaternion.Euler(3.0f, 0.0f, 0.0f);

			/* Without a target texture the camera's viewport is whatever the batch-mode game view
			 * happens to be — 1600x1000 under xvfb — and every ScreenToWorldPoint and
			 * WorldToScreenPoint below is then measured in the wrong space, which is what put the
			 * silhouettes nowhere near their plates. The panel's own surface is the right one. */
			camera.targetTexture = texture;

			host = new GameObject("NameplateShot") { hideFlags = HideFlags.HideAndDontSave };
			UIDocument document = host.AddComponent<UIDocument>();
			document.panelSettings = settings;
			document.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UXML_PATH);

			layer = host.AddComponent<UITKNameplateLayer>();
			layer.ProjectionCamera = camera;

			/* Awake is skipped: it only caches the document and claims a static singleton that a
			 * previous run in this editor session may still hold. The field is set directly, and
			 * OnEnable then adopts the plates built above. */
			typeof(UITKNameplateLayer)
				.GetField("document", BindingFlags.Instance | BindingFlags.NonPublic)
				.SetValue(layer, document);

			panel = document.rootVisualElement.panel;
			if (panel == null)
			{
				throw new InvalidOperationException("the document has no panel yet");
			}

			Subject[] subjects = Subjects();

			foreach (Subject subject in subjects)
			{
				Invoke(subject.Plate, "OnEnable");
			}
			Invoke(layer, "OnEnable");

			document.rootVisualElement.Insert(0, Backdrop(subjects));
			document.rootVisualElement.MarkDirtyRepaint();
		}

		/// <summary>One thing in the world with a plate over it.</summary>
		private sealed class Subject
		{
			public Vector3 Feet;
			public float Height;
			public float Width;
			public Nameplate Plate;
		}

		/// <summary>
		/// The cast: one of each thing the plate has to be able to say.
		/// </summary>
		private static Subject[] Subjects()
		{
			Color ally = new Color(0.24f, 0.78f, 0.32f);
			Color neutral = new Color(0.35f, 0.63f, 0.92f);
			Color enemy = new Color(0.88f, 0.24f, 0.22f);

			Subject player = Make(250.0f, 335.0f, 10.0f, 1.85f, 0.62f, ally, plate =>
			{
				plate.SetLine(NameplateSlot.Name, "Bramwell");
				plate.SetLine(NameplateSlot.GuildName, "[Ironhand Vanguard]");
			});

			Subject banker = Make(940.0f, 350.0f, 14.0f, 1.8f, 0.6f, neutral, plate =>
			{
				plate.SetLine(NameplateSlot.Name, "Aldric Stonewell");
				plate.SetLine(NameplateSlot.InteractableType, "<Banker>",
					new Color(0.13f, 0.55f, 0.13f));
			});

			Subject boss = Make(600.0f, 300.0f, 8.0f, 2.55f, 0.95f, enemy, plate =>
			{
				plate.SetStyle(NameplateStylePresets.Boss);
				plate.SetLine(NameplateSlot.Name, "Gorehowl the Ruthless");
				plate.SetLine((int)NameplateSlot.Custom, 50, "Level 42 Warchief");
				plate.SetLine(NameplateSlot.Status, "Casting: Shatterquake",
					new Color(1.0f, 0.78f, 0.28f));
			});

			Subject rival = Make(330.0f, 245.0f, 26.0f, 1.85f, 0.6f, enemy, plate =>
			{
				plate.SetLine(NameplateSlot.Name, "Zarek");
				plate.SetLine(NameplateSlot.GuildName, "[Bloodfang]");
			});

			Subject chest = Make(830.0f, 480.0f, 7.0f, 0.75f, 1.0f,
				new Color(0.45f, 0.38f, 0.26f), plate =>
			{
				plate.SetStyle(NameplateStylePresets.Plain);
				plate.SetLine(NameplateSlot.Name, "Iron Strongbox");
				plate.SetLine(NameplateSlot.InteractableType, "<Container>",
					new Color(0.13f, 0.55f, 0.13f));
			});

			return new[] { rival, banker, player, boss, chest };
		}

		/// <summary>
		/// Places a subject so its plate's anchor lands on a chosen point of the panel.
		/// </summary>
		/// <remarks>
		/// Composed in panel space and solved back into the world, rather than the other way
		/// round: what matters in a screenshot is that five plates are legible and do not overlap,
		/// and hand-picking world coordinates to achieve that is guesswork. The depth is still
		/// real, so each plate is drawn at the size its distance gives it.
		/// </remarks>
		private static Subject Make(float panelX, float panelY, float depth, float height, float width, Color tint, Action<Nameplate> author)
		{
			Vector3 head = PanelToWorld(new Vector2(panelX, panelY), depth);
			Vector3 feet = head - new Vector3(0.0f, height, 0.0f);

			GameObject go = new GameObject("Subject") { hideFlags = HideFlags.HideAndDontSave };
			go.transform.position = head;

			Nameplate plate = go.AddComponent<Nameplate>();
			plate.AllianceTint = tint;
			plate.Visible = true;
			author(plate);

			return new Subject
			{
				Feet = feet,
				Height = height,
				Width = width,
				Plate = plate,
			};
		}

		/// <summary>
		/// Paints the scenery the plates hang over.
		/// </summary>
		/// <remarks>
		/// A background image rather than a rendered camera: URP does not render an offscreen
		/// camera on demand outside play mode. The silhouettes are projected through the same
		/// camera the plates are, so each one really does stand under its own plate.
		/// </remarks>
		private static VisualElement Backdrop(Subject[] subjects)
		{
			Texture2D image = new Texture2D(WIDTH, HEIGHT, TextureFormat.RGBA32, false)
			{
				hideFlags = HideFlags.HideAndDontSave,
			};

			Color sky = new Color(0.09f, 0.12f, 0.18f);
			Color haze = new Color(0.22f, 0.26f, 0.31f);
			Color ground = new Color(0.13f, 0.13f, 0.12f);

			float horizon = ToPixels(WorldToPanel(
				camera.transform.position + (camera.transform.forward * 140.0f) - new Vector3(0.0f, camera.transform.position.y, 0.0f))).y;

			Color[] pixels = new Color[WIDTH * HEIGHT];
			for (int y = 0; y < HEIGHT; ++y)
			{
				float toHorizon = Mathf.Clamp01(Mathf.Abs(y - horizon) / (HEIGHT * 0.75f));
				Color row = y >= horizon
					? Color.Lerp(haze, sky, Mathf.Pow(toHorizon, 0.65f))
					: Color.Lerp(haze * 0.75f, ground, Mathf.Pow(toHorizon, 0.4f));

				for (int x = 0; x < WIDTH; ++x)
				{
					// A gentle vignette, so the plates read against the middle rather than the corners.
					float dx = (x / (float)WIDTH) - 0.5f;
					float dy = (y / (float)HEIGHT) - 0.5f;
					float vignette = 1.0f - (0.55f * Mathf.Clamp01((dx * dx + dy * dy) * 2.6f));
					pixels[(y * WIDTH) + x] = row * vignette;
				}
			}

			foreach (Subject subject in subjects)
			{
				PaintSilhouette(pixels, subject);
			}

			image.SetPixels(pixels);
			image.Apply();

			VisualElement backdrop = new VisualElement { pickingMode = PickingMode.Ignore };
			backdrop.style.position = Position.Absolute;
			backdrop.style.left = 0;
			backdrop.style.top = 0;
			backdrop.style.right = 0;
			backdrop.style.bottom = 0;
			backdrop.style.backgroundImage = new StyleBackground(image);
			return backdrop;
		}

		/// <summary>Draws one subject as a soft capsule between its feet and its head.</summary>
		/// <remarks>
		/// Projected through the panel, exactly as the plate above it is. Going through the
		/// camera's own screen projection instead puts the silhouettes in a different space —
		/// the camera measures in its viewport and the panel in its own points — and the two
		/// then disagree by a few hundred pixels.
		/// </remarks>
		private static void PaintSilhouette(Color[] pixels, Subject subject)
		{
			Vector2 feet = ToPixels(WorldToPanel(subject.Feet));
			Vector2 head = ToPixels(WorldToPanel(subject.Feet + new Vector3(0.0f, subject.Height, 0.0f)));
			Vector2 side = ToPixels(WorldToPanel(subject.Feet + new Vector3(subject.Width * 0.5f, 0.0f, 0.0f)));

			float radius = Mathf.Max(3.0f, Mathf.Abs(side.x - feet.x));
			float top = head.y;
			float bottom = feet.y;
			/* Neutral, and the same neutral for everyone. Tinting a stand-in body by the same
			 * standing colour its plate uses made the two read as one thing — the shape under a
			 * plate looked like part of the plate. A nameplate has no shape below it. */
			Color body = new Color(0.16f, 0.16f, 0.18f, 1.0f);

			int minX = Mathf.Max(0, Mathf.FloorToInt(feet.x - radius - 2.0f));
			int maxX = Mathf.Min(WIDTH - 1, Mathf.CeilToInt(feet.x + radius + 2.0f));
			// Both caps, not just the top: clipping the lower one left a flat-bottomed slab.
			int minY = Mathf.Max(0, Mathf.FloorToInt(bottom - radius - 2.0f));
			int maxY = Mathf.Min(HEIGHT - 1, Mathf.CeilToInt(top + radius + 2.0f));

			for (int y = minY; y <= maxY; ++y)
			{
				for (int x = minX; x <= maxX; ++x)
				{
					// Distance to the capsule's spine, which is the segment from feet to head.
					float spineY = Mathf.Clamp(y, bottom, top);
					float distance = Mathf.Sqrt(((x - feet.x) * (x - feet.x)) + ((y - spineY) * (y - spineY)));

					float coverage = Mathf.Clamp01((radius - distance) / 2.0f);
					if (coverage <= 0.0f)
					{
						continue;
					}

					int index = (y * WIDTH) + x;
					pixels[index] = Color.Lerp(pixels[index], body, coverage);
				}
			}
		}

		/// <summary>The panel point a world position projects to, the way the layer projects it.</summary>
		private static Vector2 WorldToPanel(Vector3 world)
		{
			return RuntimePanelUtils.CameraTransformWorldToPanel(panel, world, camera);
		}

		/// <summary>
		/// A panel point as a pixel in the bottom-up buffer the backdrop is painted into.
		/// </summary>
		/// <remarks>
		/// Panel Y grows downward from the top of the surface and a texture's row 0 is its bottom,
		/// so the conversion is one subtraction — and it is the whole conversion: UI Toolkit draws
		/// a background image in the texture's own orientation, so nothing is flipped afterwards.
		/// </remarks>
		private static Vector2 ToPixels(Vector2 panelPoint)
		{
			return new Vector2(panelPoint.x, (HEIGHT - 1) - panelPoint.y);
		}

		/// <summary>
		/// The world position that projects to a chosen panel point at a chosen depth.
		/// </summary>
		/// <remarks>
		/// At a fixed depth the projection is affine, so three probes — the depth's centre point
		/// and one metre along each of the camera's own axes — give the two basis vectors, and the
		/// answer is a 2x2 solve. Composed this way because what a screenshot needs is five plates
		/// that are legible and do not overlap, which is not a property of any world coordinate a
		/// person would guess.
		/// </remarks>
		private static Vector3 PanelToWorld(Vector2 target, float depth)
		{
			Transform view = camera.transform;
			Vector3 origin = view.position + (view.forward * depth);

			Vector2 p0 = WorldToPanel(origin);
			Vector2 px = WorldToPanel(origin + view.right) - p0;
			Vector2 py = WorldToPanel(origin + view.up) - p0;

			Vector2 delta = target - p0;
			float determinant = (px.x * py.y) - (px.y * py.x);
			if (Mathf.Abs(determinant) < 1e-6f)
			{
				return origin;
			}

			float a = ((delta.x * py.y) - (delta.y * py.x)) / determinant;
			float b = ((px.x * delta.y) - (px.y * delta.x)) / determinant;
			return origin + (view.right * a) + (view.up * b);
		}

		private static void Capture()
		{
			CaptureTo(texture, WIDTH, HEIGHT, OUTPUT);
		}

		/// <summary>Reads a render target back and writes it out as a PNG.</summary>
		private static void CaptureTo(RenderTexture target, int width, int height, string path)
		{
			RenderTexture previous = RenderTexture.active;
			RenderTexture.active = target;
			try
			{
				Texture2D image = new Texture2D(width, height, TextureFormat.RGBA32, false);
				image.ReadPixels(new Rect(0, 0, width, height), 0, 0);
				image.Apply();
				File.WriteAllBytes(path, image.EncodeToPNG());
				UnityEngine.Object.DestroyImmediate(image);
			}
			finally
			{
				RenderTexture.active = previous;
			}
		}

		private static void Teardown()
		{
			if (host != null) { UnityEngine.Object.DestroyImmediate(host); }
			if (cameraHost != null) { UnityEngine.Object.DestroyImmediate(cameraHost); }
			if (settings != null) { UnityEngine.Object.DestroyImmediate(settings); }
			if (texture != null) { texture.Release(); UnityEngine.Object.DestroyImmediate(texture); }
		}

		private static void Invoke(object target, string method)
		{
			MethodInfo info = target.GetType().GetMethod(method,
				BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
			info.Invoke(target, null);
		}
	}
}
