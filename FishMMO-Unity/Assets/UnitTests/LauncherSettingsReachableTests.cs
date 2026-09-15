using System.Collections;
using System.Text;
using UnityEngine;
using UnityEngine.UIElements;
using UnityEngine.TestTools;
using UnityEditor;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Every launcher setting can be reached, at every common window shape.
	/// </summary>
	/// <remarks>
	/// <para>The shared panel settings scale with screen size, matched on width, so the launcher
	/// is always 1200 units wide and its height is 1200 × height ÷ width: a wide or short window
	/// is a short panel. The banner keeps its aspect ratio from the image, which puts it at about
	/// 377 units tall at any size. What is left between the banner and the footer was all the
	/// settings overlay had, capped at 38% of it — about 66 units at 16:9 and nothing at 21:9, so
	/// the list was cut off exactly at the resolutions most players run.</para>
	/// <para>Measured against the real markup and the shared panel settings at each window size,
	/// with the banner sized the way <c>UITKClientLauncher</c> sizes it at runtime. Each size is
	/// a separate target texture so the panel genuinely lays out at that shape.</para>
	/// </remarks>
	[TestFixture]
	public class LauncherSettingsReachableTests
	{
		private const string PanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";
		private const string LauncherUxmlPath = "Assets/Scripts/Client/GUI/Launcher/UILauncher.uxml";
		private const string ClosedClass = "launcher-settings--closed";
		private const float BannerFallbackAspect = 3840f / 1207f;

		/// <summary>The minimum usable settings viewport, in panel units: several rows at once.</summary>
		private const float MinimumUsableHeight = 150f;

		private static readonly Vector2Int[] WindowSizes =
		{
			new Vector2Int(480, 360),   // the launcher's minimum window, 4:3
			new Vector2Int(1024, 768),  // the launcher's default window, 4:3
			new Vector2Int(1280, 1024), // 5:4
			new Vector2Int(1280, 800),  // 16:10
			new Vector2Int(1366, 768),  // ~16:9
			new Vector2Int(1920, 1080), // 16:9
			new Vector2Int(2560, 1080), // 21:9
			new Vector2Int(3440, 1440), // 21:9
		};

		private PanelSettings settings;
		private RenderTexture texture;
		private GameObject host;

		[TearDown]
		public void TearDown()
		{
			Release();
		}

		private void Release()
		{
			if (host != null)
			{
				Object.DestroyImmediate(host);
			}
			if (settings != null)
			{
				Object.DestroyImmediate(settings);
			}
			if (texture != null)
			{
				texture.Release();
				Object.DestroyImmediate(texture);
			}
			host = null;
			settings = null;
			texture = null;
		}

		private static IEnumerator Frames(int count)
		{
			for (int frame = 0; frame < count; ++frame)
			{
				yield return null;
			}
		}

		[UnityTest]
		public IEnumerator EverySettingCanBeScrolledIntoViewAtEveryWindowShape()
		{
			PanelSettings asset = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
			VisualTreeAsset uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(LauncherUxmlPath);
			LogAssert.IsNotNull(asset, $"panel settings must exist at {PanelSettingsPath}");
			LogAssert.IsNotNull(uxml, $"the launcher UXML must exist at {LauncherUxmlPath}");

			StringBuilder table = new StringBuilder("window     panel h  banner  settings  viewport  content  last-row-visible\n");
			StringBuilder failures = new StringBuilder();

			foreach (Vector2Int size in WindowSizes)
			{
				Release();

				texture = new RenderTexture(size.x, size.y, 24, RenderTextureFormat.ARGB32);
				texture.Create();
				settings = Object.Instantiate(asset);
				settings.hideFlags = HideFlags.HideAndDontSave;
				settings.targetTexture = texture;

				host = new GameObject("LauncherSettingsReachableTests");
				UIDocument document = host.AddComponent<UIDocument>();
				document.panelSettings = settings;
				document.visualTreeAsset = uxml;

				VisualElement root = document.rootVisualElement;
				VisualElement panel = root.Q("launcher-panel");
				VisualElement header = root.Q("launcher-header");
				VisualElement hero = root.Q("launcher-hero");
				VisualElement footer = root.Q("launcher-footer");
				ScrollView settingsPanel = root.Q<ScrollView>("launcher-settings");
				VisualElement lastRow = root.Q("launcher-patchdir-row");
				LogAssert.IsTrue(panel != null && header != null && hero != null && footer != null && settingsPanel != null && lastRow != null,
					"the launcher markup must contain the panel, header, banner, footer, settings and the patch folder row");

				yield return Frames(10);

				// The banner height UITKClientLauncher.OnHeroGeometryChanged writes at runtime.
				Background background = hero.resolvedStyle.backgroundImage;
				float aspect = background.texture != null && background.texture.height > 0
					? (float)background.texture.width / background.texture.height
					: BannerFallbackAspect;
				hero.style.height = hero.resolvedStyle.width / aspect;

				// Exactly what UITKClientLauncher.ToggleSettings does, then scrolled as far down as it goes.
				settingsPanel.RemoveFromClassList(ClosedClass);
				yield return Frames(10);
				settingsPanel.scrollOffset = new Vector2(0f, 100000f);
				yield return Frames(10);

				Rect panelRect = panel.worldBound;
				Rect settingsRect = settingsPanel.worldBound;
				Rect viewport = settingsPanel.contentViewport.worldBound;
				Rect row = lastRow.worldBound;
				float content = settingsPanel.contentContainer.layout.height;
				bool rowVisible = row.height > 1f && row.yMin >= viewport.yMin - 1f && row.yMax <= viewport.yMax + 1f;

				table.AppendLine($"{size.x}x{size.y,-6} {panelRect.height,7:0}  {hero.worldBound.height,6:0}  {settingsRect.height,8:0}  {viewport.height,8:0}  {content,7:0}  {rowVisible}");

				string label = $"{size.x}x{size.y}";
				if (settingsRect.yMin < header.worldBound.yMax - 1f || settingsRect.yMax > footer.worldBound.yMin + 1f)
				{
					failures.AppendLine($"{label}: settings ({settingsRect.yMin:0}-{settingsRect.yMax:0}) must lie between the header ({header.worldBound.yMax:0}) and the footer ({footer.worldBound.yMin:0}), where nothing clips it");
				}
				if (viewport.height + 1f < Mathf.Min(content, MinimumUsableHeight))
				{
					failures.AppendLine($"{label}: the settings viewport is {viewport.height:0} tall for {content:0} of content; it needs at least {Mathf.Min(content, MinimumUsableHeight):0} to be usable");
				}
				if (!rowVisible)
				{
					failures.AppendLine($"{label}: the last settings row ({row.yMin:0}-{row.yMax:0}, {row.height:0} tall) cannot be scrolled into the viewport ({viewport.yMin:0}-{viewport.yMax:0})");
				}
			}

			Debug.Log("[LauncherSettingsReachableTests]\n" + table);
			LogAssert.IsTrue(failures.Length == 0, "launcher settings are cut off:\n" + failures + "\n" + table);
		}
	}
}
