using System;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using FishMMO.Client;
using FishMMO.Shared;

namespace FishMMO.RenderScratch
{
	/// <summary>
	/// Asserts that a configuration carrying pre-UITK colour keys leaves this skin's surfaces
	/// alone, and that colours this skin was actually given still apply.
	/// </summary>
	public static class ThemeGuardTest
	{
		private static int failures;

		[MenuItem("FishMMO/UI Toolkit/Test Theme Guard")]
		public static void Run()
		{
			try
			{
				LegacyConfigDoesNotPaintSurfaces();
				LegacyConfigStillPaintsGameColours();
				StampedConfigPaintsEverything();
				WriteStampsTheVersion();
				PanelBackgroundStaysOnTheStylesheet();
			}
			catch (Exception ex)
			{
				Debug.LogError("[Guard] threw: " + ex);
				++failures;
			}

			Debug.Log(failures == 0 ? "[Guard] ALL PASS" : $"[Guard] {failures} FAILURE(S)");
			EditorApplication.Exit(failures == 0 ? 0 : 1);
		}

		// ── Cases ───────────────────────────────────────────────────

		/// <summary>A Canvas-era file: colour keys, no version stamp.</summary>
		private static Configuration Legacy()
		{
			Configuration config = Fresh();
			Raw(config, "Primary", 60, 60, 60);
			Raw(config, "Secondary", 45, 45, 45);
			Raw(config, "Highlight", 120, 120, 120);
			Raw(config, "Background", 32, 32, 32);
			Raw(config, "Text", 200, 200, 200);
			Raw(config, "Health", 190, 40, 40);
			return config;
		}

		private static void LegacyConfigDoesNotPaintSurfaces()
		{
			UITKTheme theme = new UITKTheme(Legacy());
			Check("legacy Background is not honoured", !theme.HasOverride("Background"));
			Check("legacy Primary is not honoured", !theme.HasOverride("Primary"));
			Check("legacy Secondary is not honoured", !theme.HasOverride("Secondary"));
			Check("legacy Highlight is not honoured", !theme.HasOverride("Highlight"));
		}

		private static void LegacyConfigStillPaintsGameColours()
		{
			UITKTheme theme = new UITKTheme(Legacy());
			Check("legacy Health is honoured", theme.HasOverride("Health"));
			Check("legacy Text is honoured", theme.HasOverride("Text"));
			Check("legacy theme still counts as overridden", theme.IsOverridden);
		}

		private static void StampedConfigPaintsEverything()
		{
			Configuration config = Legacy();
			config.Set(UITKTheme.VersionKey, UITKTheme.Version);

			UITKTheme theme = new UITKTheme(config);
			Check("stamped Background is honoured", theme.HasOverride("Background"));
			Check("stamped Background keeps its value",
				((Color32)theme.Background).r == 32, ((Color32)theme.Background).ToString());
		}

		private static void WriteStampsTheVersion()
		{
			Configuration config = Fresh();
			UITKTheme.Write(config, "Health", new Color32(1, 2, 3, 255));

			bool stamped = config.TryGetInt(UITKTheme.VersionKey, out int version, 0);
			Check("Write stamps the version", stamped && version == UITKTheme.Version, "v=" + version);

			// And a surface colour set afterwards must take effect on the next parse.
			UITKTheme.Write(config, "Background", new Color32(9, 9, 9, 255));
			Check("a surface colour set in this skin is honoured",
				new UITKTheme(config).HasOverride("Background"));
		}

		/// <summary>The outcome that matters: the panel keeps its stylesheet background.</summary>
		private static void PanelBackgroundStaysOnTheStylesheet()
		{
			Configuration previous = Configuration.GlobalSettings;
			GameObject host = new GameObject("GuardPanel") { hideFlags = HideFlags.HideAndDontSave };
			try
			{
				Configuration.SetGlobalSettings(Legacy());

				PanelSettings settings = UnityEngine.Object.Instantiate(
					AssetDatabase.LoadAssetAtPath<PanelSettings>("Assets/UI Toolkit/PanelSettings.asset"));
				settings.hideFlags = HideFlags.HideAndDontSave;

				UIDocument doc = host.AddComponent<UIDocument>();
				doc.panelSettings = settings;
				doc.visualTreeAsset = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(
					"Assets/Scripts/Client/GUI/World/Options/UIOptions.uxml");

				UITKThemeManager.Reload();
				VisualElement root = doc.rootVisualElement;
				UITKThemeManager.Register(root);

				VisualElement panel = root.Q(className: "fish-panel");
				Check("a .fish-panel exists to test", panel != null);
				if (panel != null)
				{
					Check("panel background left to the stylesheet",
						panel.style.backgroundColor.keyword == StyleKeyword.Null,
						panel.style.backgroundColor.ToString());
				}

				UITKThemeManager.Unregister(root);
				UnityEngine.Object.DestroyImmediate(settings);
			}
			finally
			{
				UnityEngine.Object.DestroyImmediate(host);
				if (previous != null) { Configuration.SetGlobalSettings(previous); }
			}
		}

		// ── Helpers ─────────────────────────────────────────────────

		private static Configuration Fresh()
		{
			return new Configuration(Path.GetTempPath());
		}

		/// <summary>Writes a colour group directly, without the version stamp Write adds.</summary>
		private static void Raw(Configuration config, string name, byte r, byte g, byte b)
		{
			config.Set($"{name}ColorR", r);
			config.Set($"{name}ColorG", g);
			config.Set($"{name}ColorB", b);
			config.Set($"{name}ColorA", (byte)255);
		}

		private static void Check(string what, bool ok, string got = "")
		{
			Debug.Log((ok ? "PASS  " : "FAIL  ") + what + (string.IsNullOrEmpty(got) ? "" : "   " + got));
			if (!ok) { ++failures; }
		}
	}
}
