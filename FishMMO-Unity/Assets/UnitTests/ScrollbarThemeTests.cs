using System;
using System.Collections;
using System.IO;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using UnityEngine.UIElements;
using UnityEditor;
using FishMMO.Client;
using FishMMO.Shared;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Scrollbars take their look from the theme, and the player can recolour them.
	/// </summary>
	/// <remarks>
	/// Reported as "scrollbars and the thumb are white or grey instead of matching the theme".
	/// The stylesheet had rules for them that addressed the wrong depth of Unity's scroller
	/// hierarchy and matched nothing, so the default runtime theme showed through. These mount a
	/// real panel with a real ScrollView, give it more rows than fit, and read what the scroller
	/// actually resolves to.
	/// </remarks>
	[TestFixture]
	public class ScrollbarThemeTests
	{
		private const string PanelSettingsPath = "Assets/UI Toolkit/PanelSettings.asset";
		private const string UxmlPath = "Assets/Scripts/Client/GUI/World/Faction/UIFactions.uxml";

		/// <summary>--brand-500 in FishMMO-Theme.uss: the thumb at rest.</summary>
		private static readonly Color32 Brand500 = new Color32(0, 115, 192, 255);

		/// <summary>--abyss-700 in FishMMO-Theme.uss: the rail.</summary>
		private static readonly Color32 Abyss700 = new Color32(0, 28, 40, 255);

		private GameObject host;
		private UIDocument document;
		private Configuration previousSettings;
		private bool settingsReplaced;

		[SetUp]
		public void SetUp()
		{
			PanelSettings settings = AssetDatabase.LoadAssetAtPath<PanelSettings>(PanelSettingsPath);
			VisualTreeAsset uxml = AssetDatabase.LoadAssetAtPath<VisualTreeAsset>(UxmlPath);
			LogAssert.IsNotNull(settings, $"panel settings must exist at {PanelSettingsPath}");
			LogAssert.IsNotNull(uxml, $"the factions UXML must exist at {UxmlPath}");

			host = new GameObject("ScrollbarThemeTest");
			document = host.AddComponent<UIDocument>();
			document.panelSettings = UnityEngine.Object.Instantiate(settings);
			document.visualTreeAsset = uxml;
		}

		[TearDown]
		public void TearDown()
		{
			UnityEngine.TestTools.LogAssert.ignoreFailingMessages = false;
			if (settingsReplaced)
			{
				/* Cleared BEFORE the restore, not after. Configuration.SetGlobalSettings refuses null
				 * by design — it is meant to be called once at startup — and in EditMode there is no
				 * global configuration to have saved, so previousSettings is null and the restore
				 * threw. That left the flag set and this fixture's temporary config installed
				 * globally, so every later test in the fixture threw in TearDown as well and the
				 * next one ran against a stale config. One failure became three. */
				settingsReplaced = false;
				RestoreGlobalSettings(previousSettings);
				UITKThemeManager.Reload();
			}
			if (host != null)
			{
				UnityEngine.Object.DestroyImmediate(host);
			}
		}

		/// <summary>
		/// Puts back exactly what was there, including nothing.
		/// </summary>
		/// <remarks>
		/// <c>SetGlobalSettings</c> takes the public path when there was a configuration to restore.
		/// When there was not — the EditMode case — the field is written directly, because the
		/// public setter rejects null on purpose and a harness that cannot express "there was none"
		/// would have to leave its own temporary configuration installed for every fixture that runs
		/// after it.
		/// </remarks>
		/// <param name="previous">The configuration that was global before this test replaced it.</param>
		private static void RestoreGlobalSettings(Configuration previous)
		{
			if (previous != null)
			{
				Configuration.SetGlobalSettings(previous);
				return;
			}

			System.Reflection.FieldInfo field = typeof(Configuration).GetField("globalSettings",
				System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic);
			LogAssert.IsNotNull(field, "Configuration.globalSettings must exist for the harness to clear it.");
			field.SetValue(null, null);
		}

		/// <summary>Fills the panel's scroll view past its height so a vertical scroller appears.</summary>
		private ScrollView Overfill()
		{
			ScrollView scroll = document.rootVisualElement.Q<ScrollView>("faction-scroll");
			LogAssert.IsNotNull(scroll, "UIFactions.uxml must still carry faction-scroll");
			for (int i = 0; i < 120; ++i)
			{
				scroll.Add(new Label($"row {i}") { style = { height = 24 } });
			}
			return scroll;
		}

		private static VisualElement Thumb(Scroller scroller)
		{
			return scroller.Q(className: "unity-base-slider__dragger");
		}

		private static VisualElement Track(Scroller scroller)
		{
			return scroller.Q(className: "unity-base-slider__tracker");
		}

		private static bool Near(float a, float b, float tolerance = 0.51f)
		{
			return Mathf.Abs(a - b) <= tolerance;
		}

		private static bool SameColour(Color32 expected, Color actual)
		{
			Color32 c = actual;
			return c.r == expected.r && c.g == expected.g && c.b == expected.b && c.a == expected.a;
		}

		[UnityTest]
		public IEnumerator TheStylesheet_PaintsTheScrollerFromTheTheme()
		{
			ScrollView scroll = Overfill();
			for (int i = 0; i < 6; ++i)
			{
				yield return null;
			}

			Scroller scroller = scroll.verticalScroller;
			LogAssert.IsNotNull(scroller, "a ScrollView has a vertical scroller");
			LogAssert.IsTrue(scroller.resolvedStyle.display == DisplayStyle.Flex, "and with more rows than fit, it is shown");

			VisualElement thumb = Thumb(scroller);
			VisualElement track = Track(scroller);
			LogAssert.IsNotNull(thumb, "the scroller has a thumb");
			LogAssert.IsNotNull(track, "and a track");

			LogAssert.IsTrue(SameColour(Brand500, thumb.resolvedStyle.backgroundColor),
				$"the thumb rests on --brand-500, got {thumb.resolvedStyle.backgroundColor}");
			LogAssert.IsTrue(SameColour(Abyss700, track.resolvedStyle.backgroundColor),
				$"the track is --abyss-700, got {track.resolvedStyle.backgroundColor}");

			LogAssert.IsTrue(scroller.lowButton.resolvedStyle.display == DisplayStyle.None, "no arrow button at the top");
			LogAssert.IsTrue(scroller.highButton.resolvedStyle.display == DisplayStyle.None, "nor at the bottom");

			LogAssert.IsTrue(Near(scroller.resolvedStyle.width, 8f), $"the rail is 8px wide, got {scroller.resolvedStyle.width}");
			LogAssert.IsTrue(Near(thumb.resolvedStyle.width, scroller.resolvedStyle.width),
				$"the thumb fills the rail's width: {thumb.resolvedStyle.width} of {scroller.resolvedStyle.width}");
			LogAssert.IsTrue(Near(track.resolvedStyle.height, scroller.resolvedStyle.height),
				$"with no buttons the track runs the scroller's full height: {track.resolvedStyle.height} of {scroller.resolvedStyle.height}");
			LogAssert.IsTrue(thumb.resolvedStyle.height > 0f && thumb.resolvedStyle.height < track.resolvedStyle.height,
				$"the thumb is shorter than the track: {thumb.resolvedStyle.height} of {track.resolvedStyle.height}");
		}

		[Test]
		public void TheThemeNamesBothScrollbarColours()
		{
			LogAssert.IsTrue(Array.IndexOf(UITKTheme.ColorNames, "ScrollTrack") >= 0, "ScrollTrack is a theme colour");
			LogAssert.IsTrue(Array.IndexOf(UITKTheme.ColorNames, "ScrollThumb") >= 0, "ScrollThumb is a theme colour");
		}

		[Test]
		public void AnOverride_ReachesTheScrollerAndNothingElse()
		{
			string dir = Path.Combine(Path.GetTempPath(), "fishmmo-scrollbar-theme-test");
			Directory.CreateDirectory(dir);
			Configuration config = new Configuration(dir);
			Color thumbColour = new Color(1.0f, 0.5f, 0.0f, 1.0f);
			Color trackColour = new Color(0.1f, 0.2f, 0.3f, 1.0f);
			UITKTheme.Write(config, "ScrollThumb", thumbColour);
			UITKTheme.Write(config, "ScrollTrack", trackColour);

			previousSettings = Configuration.GlobalSettings;
			settingsReplaced = true;
			Configuration.SetGlobalSettings(config);
			UITKThemeManager.Reload();

			VisualElement root = document.rootVisualElement;
			ScrollView scroll = Overfill();

			// An option-style slider in the same tree must keep its stylesheet look.
			Slider optionSlider = new Slider();
			optionSlider.AddToClassList("fish-slider");
			root.Add(optionSlider);

			UITKThemeManager.Apply(root);

			Scroller scroller = scroll.verticalScroller;
			VisualElement thumb = Thumb(scroller);
			VisualElement track = Track(scroller);
			/* Compared as bytes, not as floats.
			 *
			 * A theme colour is STORED as four bytes — UITKTheme.Write casts to Color32 and writes
			 * the channels — so a colour that goes into configuration as 0.5 comes back out as
			 * 128/255, which is 0.50196. Unity's Color equality is a squared-distance test with a
			 * 1e-10 epsilon, far tighter than that, so an exact comparison against the float the
			 * test wrote can never succeed for any channel that is not already a whole number of
			 * 255ths. The colour reaching the thumb was right the entire time.
			 *
			 * SameColour is the comparison the rest of this fixture already uses against the
			 * stylesheet's own values, for the same reason. */
			LogAssert.IsTrue(thumb.style.backgroundColor.keyword == StyleKeyword.Undefined &&
					SameColour(thumbColour, thumb.style.backgroundColor.value),
				$"the thumb carries the override inline, got {thumb.style.backgroundColor.value}");
			LogAssert.IsTrue(track.style.backgroundColor.keyword == StyleKeyword.Undefined &&
					SameColour(trackColour, track.style.backgroundColor.value),
				$"and so does the track, got {track.style.backgroundColor.value}");

			VisualElement sliderThumb = optionSlider.Q(className: "unity-base-slider__dragger");
			LogAssert.IsTrue(sliderThumb.style.backgroundColor.keyword == StyleKeyword.Null,
				"an option slider's dragger is not a scrollbar and is left alone");

			// Clearing the override hands the colour back to the stylesheet.
			UITKTheme.Clear(config, "ScrollThumb");
			UITKTheme.Clear(config, "ScrollTrack");
			UITKThemeManager.Reload();
			UITKThemeManager.Apply(root);
			LogAssert.IsTrue(thumb.style.backgroundColor.keyword == StyleKeyword.Null, "cleared thumb");
			LogAssert.IsTrue(track.style.backgroundColor.keyword == StyleKeyword.Null, "cleared track");
		}
	}
}
