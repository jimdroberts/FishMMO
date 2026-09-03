using System;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using FishMMO.Client;
using FishMMO.Shared;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs for the custom crosshair options (issue #188): that every choice the options panel
	/// offers is stored, read back with the same meaning, and kept inside the range the crosshair
	/// can actually draw.
	/// </summary>
	/// <remarks>
	/// <para>
	/// The controls themselves are four rows of UXML bound in <c>UITKOptions</c>, and the crosshair
	/// repaints from <see cref="ClientCrosshairSettings.OnChanged"/>. What can rot underneath them
	/// without anything looking wrong is the contract those two ends share: the style ordinal in
	/// the saved file, the label list the dropdown builds from, the class list the crosshair
	/// clears, and the bounds the sliders are clamped to. Each of those is a list or a number that
	/// has to agree with another one somewhere else, so each gets a test.
	/// </para>
	/// <para>
	/// Every test runs against a scratch <see cref="Configuration"/> swapped in as the global
	/// store, so nothing here reads or writes the developer's own Configuration.cfg.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class CrosshairSettingsTests
	{
		private const string OptionsUxml = "Assets/Scripts/Client/GUI/World/Options/UIOptions.uxml";
		private const string CrosshairUss = "Assets/Scripts/Client/GUI/World/Crosshair/UICrosshair.uss";

		private Configuration previous;
		private int raised;

		[SetUp]
		public void SetUp()
		{
			previous = Configuration.GlobalSettings;

			/* The directory is only recorded, never touched: nothing in these tests saves, and
			 * ClientSettings.Flush is editor-guarded besides. */
			Configuration.SetGlobalSettings(new Configuration(
				Path.Combine(Path.GetTempPath(), "FishMMO-CrosshairSettingsTests")));

			raised = 0;
			ClientCrosshairSettings.OnChanged += CountRaise;
		}

		[TearDown]
		public void TearDown()
		{
			ClientCrosshairSettings.OnChanged -= CountRaise;
			RestoreGlobalSettings(previous);
		}

		private void CountRaise() => ++raised;

		/// <summary>
		/// Puts the global store back exactly as it was, including "there was none".
		/// </summary>
		/// <remarks>
		/// <see cref="Configuration.SetGlobalSettings"/> refuses null, and leaving this fixture's
		/// scratch store behind would hand every later test in the run a configuration rooted in
		/// a temp directory. Reflection reaches the field directly; if the field is ever renamed
		/// the fallback keeps the swap-in at least pointing at a live store.
		/// </remarks>
		private static void RestoreGlobalSettings(Configuration value)
		{
			FieldInfo field = typeof(Configuration).GetField(
				"globalSettings", BindingFlags.NonPublic | BindingFlags.Static);

			if (field != null)
			{
				field.SetValue(null, value);
			}
			else if (value != null)
			{
				Configuration.SetGlobalSettings(value);
			}
		}

		// --- The stored shape keeps its meaning --------------------------------------------------

		[Test]
		public void TheStoredOrdinals_AreFixed()
		{
			/* The saved file holds these numbers. Reordering the enum silently changes what an
			 * existing player's crosshair is; they would log in to a different shape with nothing
			 * to explain it. */
			LogAssert.AreEqual(0, (int)ClientCrosshairSettings.CrosshairStyle.Cross);
			LogAssert.AreEqual(1, (int)ClientCrosshairSettings.CrosshairStyle.Dot);
			LogAssert.AreEqual(2, (int)ClientCrosshairSettings.CrosshairStyle.Circle);
		}

		[Test]
		public void TheLabelsAndClasses_CoverEveryStyleInOrder()
		{
			/* The dropdown stores its INDEX and the crosshair indexes the class list by the same
			 * ordinal, so both lists must be exactly as long as the enum. A style added to the
			 * enum without a label is unselectable; without a class it throws on selection. */
			int styles = Enum.GetValues(typeof(ClientCrosshairSettings.CrosshairStyle)).Length;

			LogAssert.AreEqual(styles, ClientCrosshairSettings.StyleLabels.Length,
				"there must be exactly one dropdown label per style");
			LogAssert.AreEqual(styles, ClientCrosshairSettings.StyleClasses.Length,
				"there must be exactly one USS class per style");
		}

		[Test]
		public void EveryStyleClass_IsDeclaredInTheStylesheet()
		{
			/* A class the crosshair adds but the stylesheet never declares draws nothing: the
			 * element sits there transparent, which is exactly how the crosshair was lost once
			 * before, when the port dropped its sprite reference. */
			string uss = ReadSource(CrosshairUss);

			foreach (string styleClass in ClientCrosshairSettings.StyleClasses)
			{
				LogAssert.IsTrue(uss.Contains("." + styleClass),
					$"UICrosshair.uss must declare .{styleClass}");
			}
		}

		[Test]
		public void EachStyle_RoundTrips()
		{
			foreach (ClientCrosshairSettings.CrosshairStyle style in
				Enum.GetValues(typeof(ClientCrosshairSettings.CrosshairStyle)))
			{
				ClientCrosshairSettings.SetStyle(style);

				LogAssert.AreEqual(style, ClientCrosshairSettings.Style,
					$"{style} must read back as itself");
			}
		}

		[Test]
		public void AStoredOrdinalThisBuildDoesNotKnow_FallsBackToTheDefault()
		{
			/* A file written by a newer build, or edited by hand. Reading it must not index past
			 * the class list on the crosshair's next repaint. */
			ClientSettings.Set(ClientSettings.CrosshairStyleKey, 99);
			LogAssert.AreEqual(ClientCrosshairSettings.DefaultStyle, ClientCrosshairSettings.Style,
				"an ordinal above the range must read as the default");

			ClientSettings.Set(ClientSettings.CrosshairStyleKey, -1);
			LogAssert.AreEqual(ClientCrosshairSettings.DefaultStyle, ClientCrosshairSettings.Style,
				"a negative ordinal must read as the default");
		}

		// --- Size and opacity stay drawable ------------------------------------------------------

		[Test]
		public void TheDefaultSize_IsWhatTheStylesheetAuthors()
		{
			/* The slider starts at the settings default while an untouched crosshair is drawn at
			 * the stylesheet's width, so the two must be the same number. If they drift, the
			 * options panel shows a size the crosshair is not, and the first nudge of the slider
			 * visibly resizes a crosshair the player never asked to change. */
			Match authored = Regex.Match(
				ReadSource(CrosshairUss),
				@"\.crosshair-icon\s*\{[^}]*?width:\s*(\d+)px",
				RegexOptions.Singleline);

			LogAssert.IsTrue(authored.Success, "UICrosshair.uss must author a width on .crosshair-icon");
			LogAssert.AreEqual(float.Parse(authored.Groups[1].Value), ClientCrosshairSettings.DefaultSize,
				"the settings default must equal the stylesheet's authored size");
		}

		[Test]
		public void TheDefaults_LieInsideTheOfferedRanges()
		{
			/* A default outside its own slider is a control that opens already clamped, showing a
			 * value the crosshair is not using. */
			LogAssert.IsTrue(
				ClientCrosshairSettings.DefaultSize >= ClientCrosshairSettings.MinimumSize &&
				ClientCrosshairSettings.DefaultSize <= ClientCrosshairSettings.MaximumSize,
				"the default size must be within the slider's range");
			LogAssert.IsTrue(
				ClientCrosshairSettings.DefaultOpacity >= ClientCrosshairSettings.MinimumOpacity &&
				ClientCrosshairSettings.DefaultOpacity <= ClientCrosshairSettings.MaximumOpacity,
				"the default opacity must be within the slider's range");
		}

		[Test]
		public void TheFaintestOpacity_IsStillVisible()
		{
			/* Zero would be a second "off" the enable toggle cannot see. A player who slid to
			 * zero would find the toggle on and no crosshair, with nothing to explain it. */
			LogAssert.IsTrue(ClientCrosshairSettings.MinimumOpacity > 0.0f,
				"the opacity slider must not be able to hide the crosshair");
		}

		[Test]
		public void SetSize_ClampsToTheRangeTheCrosshairCanDraw()
		{
			ClientCrosshairSettings.SetSize(1000.0f);
			LogAssert.AreEqual(ClientCrosshairSettings.MaximumSize, ClientCrosshairSettings.Size,
				"a size above the ceiling must be stored at the ceiling");

			ClientCrosshairSettings.SetSize(-5.0f);
			LogAssert.AreEqual(ClientCrosshairSettings.MinimumSize, ClientCrosshairSettings.Size,
				"a size below the floor must be stored at the floor");
		}

		[Test]
		public void SetOpacity_ClampsToTheRange()
		{
			ClientCrosshairSettings.SetOpacity(2.0f);
			LogAssert.AreEqual(ClientCrosshairSettings.MaximumOpacity, ClientCrosshairSettings.Opacity);

			ClientCrosshairSettings.SetOpacity(0.0f);
			LogAssert.AreEqual(ClientCrosshairSettings.MinimumOpacity, ClientCrosshairSettings.Opacity,
				"zero must clamp up to the faintest visible opacity, not become an invisible crosshair");
		}

		[Test]
		public void NonFiniteValues_AreRejectedOnBothPaths()
		{
			/* NaN compares false against every bound, so a clamp alone passes it through, and a
			 * NaN width takes the crosshair out of layout entirely. It can arrive through the
			 * setter or already be in the file, so both readers are checked. */
			ClientCrosshairSettings.SetSize(float.NaN);
			LogAssert.AreEqual(ClientCrosshairSettings.DefaultSize, ClientCrosshairSettings.Size,
				"NaN through the setter must store the default");

			ClientCrosshairSettings.SetOpacity(float.PositiveInfinity);
			LogAssert.AreEqual(ClientCrosshairSettings.DefaultOpacity, ClientCrosshairSettings.Opacity,
				"infinity through the setter must store the default");

			ClientSettings.Set(ClientSettings.CrosshairSizeKey, float.NaN);
			LogAssert.AreEqual(ClientCrosshairSettings.DefaultSize, ClientCrosshairSettings.Size,
				"NaN already in the file must read as the default");
		}

		// --- Visibility and change notification --------------------------------------------------

		[Test]
		public void TheCrosshair_IsOnByDefault()
		{
			/* A fresh install with no crosshair would read as the bug this feature revived. */
			LogAssert.IsTrue(ClientCrosshairSettings.Enabled, "a fresh install must draw a crosshair");
		}

		[Test]
		public void SetEnabled_RoundTrips()
		{
			ClientCrosshairSettings.SetEnabled(false);
			LogAssert.IsFalse(ClientCrosshairSettings.Enabled);

			ClientCrosshairSettings.SetEnabled(true);
			LogAssert.IsTrue(ClientCrosshairSettings.Enabled);
		}

		[Test]
		public void EverySetter_RaisesOnChangedOnce()
		{
			/* The crosshair is a separate document that only repaints on this event. A setter that
			 * wrote the key without raising would take effect at the next scene load, which is
			 * the failure the settings class exists to prevent. */
			ClientCrosshairSettings.SetEnabled(false);
			LogAssert.AreEqual(1, raised, "SetEnabled must notify");

			ClientCrosshairSettings.SetStyle(ClientCrosshairSettings.CrosshairStyle.Dot);
			LogAssert.AreEqual(2, raised, "SetStyle must notify");

			ClientCrosshairSettings.SetSize(12.0f);
			LogAssert.AreEqual(3, raised, "SetSize must notify");

			ClientCrosshairSettings.SetOpacity(0.5f);
			LogAssert.AreEqual(4, raised, "SetOpacity must notify");
		}

		// --- The options panel offers every one of them ------------------------------------------

		[Test]
		public void TheOptionsPanel_DeclaresAllFourControls()
		{
			/* The panel binds by element name and tolerates a missing one silently, so a renamed
			 * or deleted row leaves a setting with no control and no error. */
			string uxml = ReadSource(OptionsUxml);

			foreach (string name in new[]
			{
				"crosshair-enabled-toggle",
				"crosshair-style-dropdown",
				"crosshair-size-slider",
				"crosshair-opacity-slider",
			})
			{
				LogAssert.IsTrue(uxml.Contains($"name=\"{name}\""),
					$"UIOptions.uxml must declare {name}");
			}
		}

		[Test]
		public void TheAuthoredSliderBounds_MatchTheSettings()
		{
			/* The panel rewrites the bounds at bind time, so a drift here is invisible at runtime
			 * and only misleads whoever edits the UXML next. Kept equal so the markup tells the
			 * truth about the range it is showing. */
			string uxml = ReadSource(OptionsUxml);

			AssertSliderBounds(uxml, "crosshair-size-slider",
				ClientCrosshairSettings.MinimumSize, ClientCrosshairSettings.MaximumSize);
			AssertSliderBounds(uxml, "crosshair-opacity-slider",
				ClientCrosshairSettings.MinimumOpacity, ClientCrosshairSettings.MaximumOpacity);
		}

		[Test]
		public void TheCrosshairColour_IsAThemedColour()
		{
			/* Colour is deliberately not a crosshair row: the UI tab's colour list edits it as a
			 * theme colour. That only holds while the theme still names it. */
			LogAssert.IsTrue(Array.IndexOf(UITKTheme.ColorNames, "Crosshair") >= 0,
				"UITKTheme.ColorNames must carry Crosshair, or the colour has no control at all");
		}

		private static void AssertSliderBounds(string uxml, string name, float low, float high)
		{
			Match slider = Regex.Match(uxml,
				$"<ui:Slider name=\"{name}\"[^>]*low-value=\"([^\"]+)\"[^>]*high-value=\"([^\"]+)\"");

			LogAssert.IsTrue(slider.Success, $"{name} must author low-value and high-value");
			LogAssert.AreEqual(low, float.Parse(slider.Groups[1].Value,
				System.Globalization.CultureInfo.InvariantCulture), $"{name} low-value");
			LogAssert.AreEqual(high, float.Parse(slider.Groups[2].Value,
				System.Globalization.CultureInfo.InvariantCulture), $"{name} high-value");
		}

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path);
		}
	}
}
