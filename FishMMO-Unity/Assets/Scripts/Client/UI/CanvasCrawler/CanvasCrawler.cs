using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using FishMMO.Shared;
using TMPro;

namespace FishMMO.Client
{
	/// <summary>
	/// Crawls a Canvas hierarchy and applies a unified UI theme from a configuration file to all supported UI components.
	/// Theme settings (colors, layout, scroll, font, transitions) are parsed once into a <see cref="UITheme"/>
	/// and reused across all handlers. Supports runtime re-crawling when theme settings change.
	/// </summary>
	public class CanvasCrawler : MonoBehaviour
	{
		/// <summary>
		/// Maps UI component types to their canvas settings handlers.
		/// </summary>
		private static readonly Dictionary<Type, BaseCanvasTypeSettings> CanvasSettingsMap = new Dictionary<Type, BaseCanvasTypeSettings>()
		{
			{ typeof(Button), new SelectableCanvasTypeSettings(applySelectedColor: true) },
			{ typeof(Toggle), new SelectableCanvasTypeSettings() },
			{ typeof(Slider), new SelectableCanvasTypeSettings() },
			{ typeof(Scrollbar), new SelectableCanvasTypeSettings() },
			{ typeof(Dropdown), new SelectableCanvasTypeSettings() },
			{ typeof(InputField), new SelectableCanvasTypeSettings() },
			{ typeof(TMP_InputField), new SelectableCanvasTypeSettings(applySelectedColor: true) },
			{ typeof(TMP_Dropdown), new SelectableCanvasTypeSettings(applySelectedColor: true) },
			{ typeof(Image), new ImageCanvasTypeSettings() },
			{ typeof(Text), new TextGraphicCanvasTypeSettings() },
			{ typeof(TMP_Text), new TextGraphicCanvasTypeSettings() },
			{ typeof(VerticalLayoutGroup), new LayoutGroupCanvasTypeSettings() },
			{ typeof(HorizontalLayoutGroup), new LayoutGroupCanvasTypeSettings() },
			{ typeof(GridLayoutGroup), new GridLayoutGroupCanvasTypeSettings() },
			{ typeof(ScrollRect), new ScrollRectCanvasTypeSettings() },
		};

		/// <summary>
		/// Cached theme parsed from configuration. Null until first Awake.
		/// </summary>
		private static UITheme theme;

		/// <summary>
		/// The canvas component to crawl and apply settings to.
		/// </summary>
		[SerializeField]
		private Canvas canvas;

		/// <summary>
		/// Initializes the canvas reference and loads or creates the UI configuration.
		/// </summary>
		private void Awake()
		{
			if (canvas == null)
			{
				canvas = GetComponent<Canvas>();
				if (canvas == null)
				{
					throw new UnityException("Unable to find a Canvas on the gameObject.");
				}
			}

			if (theme == null)
			{
				LoadTheme();
			}
		}

		/// <summary>
		/// Begins crawling the canvas to apply the theme to all child UI components.
		/// </summary>
		private void Start()
		{
			Crawl(canvas, theme);
		}

		/// <summary>
		/// Loads or reloads the UI configuration and rebuilds the theme.
		/// Call this at runtime to apply updated configuration values.
		/// </summary>
		public static void LoadTheme()
		{
			Configuration configuration = new Configuration(Constants.GetWorkingDirectory());
			if (!configuration.Load("UIConfiguration"))
			{
				InitializeDefaultConfiguration(configuration);
#if !UNITY_EDITOR
				configuration.Save();
#endif
			}
			theme = new UITheme(configuration);
		}

		/// <summary>
		/// Returns the currently loaded theme, or null if not yet initialized.
		/// </summary>
		public static UITheme GetTheme()
		{
			return theme;
		}

		/// <summary>
		/// Reloads the configuration from disk and re-crawls this canvas.
		/// Useful for applying settings changes from an options menu at runtime.
		/// </summary>
		public void Recrawl()
		{
			LoadTheme();
			Crawl(canvas, theme);
		}

		/// <summary>
		/// Crawls all child game objects of the given canvas and applies settings from registered handlers.
		/// Objects with "Ignore" in their name are skipped.
		/// </summary>
		/// <param name="targetCanvas">The canvas to crawl.</param>
		/// <param name="uiTheme">The pre-parsed UI theme to apply.</param>
		public static void Crawl(Canvas targetCanvas, UITheme uiTheme)
		{
			List<GameObject> gameObjects = targetCanvas.transform.FindAllChildGameObjects();

			foreach (GameObject go in gameObjects)
			{
				if (go.name.Contains("Ignore", StringComparison.Ordinal))
				{
					continue;
				}

				foreach (KeyValuePair<Type, BaseCanvasTypeSettings> pair in CanvasSettingsMap)
				{
					Component component = go.GetComponent(pair.Key);
					if (component == null) continue;

					pair.Value.ApplySettings(component, uiTheme);
				}
			}
		}

		/// <summary>
		/// Populates configuration with default values for all theme settings.
		/// </summary>
		/// <param name="config">The configuration to populate.</param>
		private static void InitializeDefaultConfiguration(Configuration config)
		{
			SetColorConfig(config, "Primary", 44, 44, 44, 235);
			SetColorConfig(config, "Secondary", 80, 80, 80, 235);
			SetColorConfig(config, "Highlight", 128, 128, 128, 250);
			SetColorConfig(config, "Background", 128, 128, 128, 235);
			SetColorConfig(config, "Text", 186, 186, 186, 255);
			SetColorConfig(config, "Health", 207, 76, 76, 255);
			SetColorConfig(config, "Mana", 87, 119, 222, 255);
			SetColorConfig(config, "Stamina", 83, 176, 59, 255);
			SetColorConfig(config, "Crosshair", 255, 255, 255, 255);

			config.Set("LayoutSpacing", "4");
			config.Set("PaddingLeft", "4");
			config.Set("PaddingRight", "4");
			config.Set("PaddingTop", "4");
			config.Set("PaddingBottom", "4");

			config.Set("GridSpacingX", "4");
			config.Set("GridSpacingY", "4");
			config.Set("GridPaddingLeft", "4");
			config.Set("GridPaddingRight", "4");
			config.Set("GridPaddingTop", "4");
			config.Set("GridPaddingBottom", "4");

			config.Set("ScrollSensitivity", "10");
			config.Set("ScrollMovementType", "2");
			config.Set("ScrollElasticity", "0.1");
			config.Set("ScrollInertia", "true");
			config.Set("ScrollDecelerationRate", "0.135");

			config.Set("SelectableFadeDuration", "0.1");

			config.Set("FontSize", "0");
			config.Set("FontSizeMin", "0");
			config.Set("FontSizeMax", "0");
		}

		/// <summary>
		/// Writes a single color's RGBA components to configuration.
		/// </summary>
		/// <param name="config">The configuration to write to.</param>
		/// <param name="name">The color name prefix.</param>
		/// <param name="r">Red channel (0-255).</param>
		/// <param name="g">Green channel (0-255).</param>
		/// <param name="b">Blue channel (0-255).</param>
		/// <param name="a">Alpha channel (0-255).</param>
		private static void SetColorConfig(Configuration config, string name, byte r, byte g, byte b, byte a)
		{
			config.Set($"{name}ColorR", r.ToString());
			config.Set($"{name}ColorG", g.ToString());
			config.Set($"{name}ColorB", b.ToString());
			config.Set($"{name}ColorA", a.ToString());
		}
	}
}