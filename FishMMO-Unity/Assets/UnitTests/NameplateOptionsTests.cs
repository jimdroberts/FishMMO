using System.Globalization;
using System.IO;
using System.Reflection;
using System.Text.RegularExpressions;
using FishMMO.Client;
using FishMMO.Shared;
using FishMMO.Shared.Core;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs for the nameplate appearance settings the Gameplay tab exposes: size, opacity,
	/// background strength, which optional rows are drawn, and how many plates may be on screen.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Three contracts. The settings themselves round-trip through the configuration store with the
	/// meaning the panel gives them. They are stored SEPARATELY from the world label settings,
	/// which is the entire reason the second class exists — a shared key would make the nameplate
	/// sliders confusing aliases of the label ones. And the panel actually declares a control for
	/// each of them, authored with the bounds the settings offer, since a slider whose bounds
	/// disagree with its setting silently clamps the ends of its own travel.
	/// </para>
	/// <para>
	/// Every test runs against a scratch <see cref="Configuration"/> swapped in as the global store,
	/// so nothing here reads or writes the developer's own Configuration.cfg.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class NameplateOptionsTests
	{
		private const string OptionsUxml = "Assets/Scripts/Client/GUI/World/Options/UIOptions.uxml";

		private Configuration previous;
		private int raised;

		[SetUp]
		public void SetUp()
		{
			previous = Configuration.GlobalSettings;
			Configuration.SetGlobalSettings(new Configuration(
				Path.Combine(Path.GetTempPath(), "FishMMO-NameplateOptionsTests")));

			raised = 0;
			ClientNameplateSettings.OnChanged += CountRaise;
		}

		[TearDown]
		public void TearDown()
		{
			ClientNameplateSettings.OnChanged -= CountRaise;
			RestoreGlobalSettings(previous);
		}

		private void CountRaise() => ++raised;

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

		// --- Defaults -------------------------------------------------------------------------

		[Test]
		public void AFreshInstall_LooksExactlyAsItDidBeforeTheseControlsExisted()
		{
			/* Every default here is what the layer draws with no configuration at all. A default
			 * that disagreed would change how the game looks for everyone the moment the controls
			 * were added, which is not what adding a control is for. */
			LogAssert.AreEqual(1.0f, ClientNameplateSettings.Opacity, "plates start fully opaque");
			LogAssert.AreEqual(1.0f, ClientNameplateSettings.Scale, "plates start at their authored size");
			LogAssert.AreEqual(1.0f, ClientNameplateSettings.BackgroundOpacity,
				"the background starts at the strength each style asks for");
			LogAssert.IsTrue(ClientNameplateSettings.ShowGuild, "guild rows start visible");
			LogAssert.IsTrue(ClientNameplateSettings.ShowTitles, "title rows start visible");
		}

		[Test]
		public void PlateOpacity_CannotBeDraggedToInvisible()
		{
			/* Turning nameplates OFF is what the two ranges and the own-name toggle are for. A
			 * slider that reached zero would be a second, much less discoverable way to do it —
			 * and the one place a player cannot see what they are doing while they drag. */
			LogAssert.IsTrue(ClientNameplateSettings.MinimumOpacity > 0.0f,
				"the faintest offered plate must still be visible");
		}

		[Test]
		public void BackgroundOpacity_ReachesZeroBecauseThatIsARealLook()
		{
			/* Unlike the plate opacity: at zero the rows are still drawn, straight over the world,
			 * which is a look people ask for by name. Nothing disappears at the end of the travel. */
			LogAssert.AreEqual(0.0f, ClientNameplateSettings.MinimumBackgroundOpacity,
				"no background must be reachable");

			ClientNameplateSettings.SetBackgroundOpacity(0.0f);
			LogAssert.AreEqual(0.0f, ClientNameplateSettings.BackgroundOpacity,
				"zero must read back as zero rather than as a missing value");
		}

		// --- Round trips ----------------------------------------------------------------------

		[Test]
		public void SetOpacity_ClampsAndRoundTrips()
		{
			ClientNameplateSettings.SetOpacity(0.5f);
			LogAssert.AreEqual(0.5f, ClientNameplateSettings.Opacity, "a value in range must round-trip");

			ClientNameplateSettings.SetOpacity(10.0f);
			LogAssert.AreEqual(ClientNameplateSettings.MaximumOpacity, ClientNameplateSettings.Opacity,
				"a value above the ceiling must be stored at the ceiling");

			ClientNameplateSettings.SetOpacity(-1.0f);
			LogAssert.AreEqual(ClientNameplateSettings.MinimumOpacity, ClientNameplateSettings.Opacity,
				"a value below the floor must be stored at the floor");

			ClientNameplateSettings.SetOpacity(float.NaN);
			LogAssert.AreEqual(ClientNameplateSettings.DefaultOpacity, ClientNameplateSettings.Opacity,
				"NaN through the setter must store the default");
		}

		[Test]
		public void NonFiniteValues_AreRejectedOnBothPaths()
		{
			/* A hand-edited Configuration.cfg is a real source of these, and the read path is the
			 * one that has to survive it — the setter never ran. */
			ClientSettings.Set(ClientSettings.NameplateScaleKey, float.PositiveInfinity);
			LogAssert.AreEqual(ClientNameplateSettings.DefaultScale, ClientNameplateSettings.Scale,
				"infinity already in the file must read as the default");

			ClientNameplateSettings.SetScale(float.NaN);
			LogAssert.AreEqual(ClientNameplateSettings.DefaultScale, ClientNameplateSettings.Scale,
				"NaN through the setter must store the default");
		}

		[Test]
		public void SetMaxVisible_ClampsToTheOfferedRange()
		{
			ClientNameplateSettings.SetMaxVisible(100000);
			LogAssert.AreEqual(ClientNameplateSettings.MaximumMaxVisible, ClientNameplateSettings.MaxVisible,
				"a budget above the ceiling must be stored at the ceiling");

			ClientNameplateSettings.SetMaxVisible(0);
			LogAssert.AreEqual(ClientNameplateSettings.MinimumMaxVisible, ClientNameplateSettings.MaxVisible,
				"a budget below the floor must be stored at the floor");
		}

		[Test]
		public void EverySetter_TellsTheLayerToReReadTheSettings()
		{
			/* The layer caches all of these and re-reads on the event. A setter that forgot to
			 * raise it would leave the panel and the screen disagreeing until something else
			 * changed a setting. */
			raised = 0;

			ClientNameplateSettings.SetOpacity(0.6f);
			ClientNameplateSettings.SetScale(1.5f);
			ClientNameplateSettings.SetBackgroundOpacity(0.25f);
			ClientNameplateSettings.SetMaxVisible(32);
			ClientNameplateSettings.SetShowGuild(false);
			ClientNameplateSettings.SetShowTitles(false);

			LogAssert.AreEqual(6, raised, "every nameplate setter must raise OnChanged exactly once");
		}

		// --- Independence from the world labels -------------------------------------------------

		[Test]
		public void NameplateAndWorldLabelSettings_AreStoredIndependently()
		{
			/* The whole reason for a second settings class. Damage numbers are feedback read for a
			 * second and a nameplate is furniture looked past all day; wanting one faint and the
			 * other loud is an ordinary preference a shared key could not express. */
			ClientNameplateSettings.SetOpacity(0.4f);
			ClientWorldLabelSettings.SetOpacity(1.0f);

			LogAssert.AreEqual(0.4f, ClientNameplateSettings.Opacity, "the plate opacity must keep its own value");
			LogAssert.AreEqual(1.0f, ClientWorldLabelSettings.Opacity, "the label opacity must keep its own value");

			ClientNameplateSettings.SetScale(2.0f);
			ClientWorldLabelSettings.SetScale(0.5f);

			LogAssert.AreEqual(2.0f, ClientNameplateSettings.Scale, "the plate scale must keep its own value");
			LogAssert.AreEqual(0.5f, ClientWorldLabelSettings.Scale, "the label scale must keep its own value");
		}

		[Test]
		public void TheTwoBudgets_AreCountedSeparately()
		{
			/* Separate budgets are what stop a burst of damage numbers pushing the plate the player
			 * is aiming at off the screen, and stop a crowded hub starving the combat feedback. */
			ClientNameplateSettings.SetMaxVisible(16);
			ClientWorldLabelSettings.SetMaxVisible(128);

			LogAssert.AreEqual(16, ClientNameplateSettings.MaxVisible, "the plate budget must keep its own value");
			LogAssert.AreEqual(128, ClientWorldLabelSettings.MaxVisible, "the label budget must keep its own value");
		}

		// --- Row visibility ---------------------------------------------------------------------

		[Test]
		public void TheRowToggles_HideOnlyTheirOwnRow()
		{
			ClientNameplateSettings.SetShowGuild(false);
			ClientNameplateSettings.SetShowTitles(true);

			LogAssert.IsFalse(ClientNameplateSettings.IsRowVisible(NameplateSlot.GuildName),
				"the guild row must follow its toggle");
			LogAssert.IsTrue(ClientNameplateSettings.IsRowVisible(NameplateSlot.InteractableType),
				"the title row must not follow the guild toggle");

			ClientNameplateSettings.SetShowTitles(false);
			LogAssert.IsFalse(ClientNameplateSettings.IsRowVisible(NameplateSlot.InteractableType),
				"the title row must follow its own toggle");
		}

		[Test]
		public void TheNameRow_IsNeverRefusable()
		{
			/* A plate with its name turned off is not a nameplate. Neither toggle may reach it,
			 * and a status row is transient enough that hiding it would mostly hide nothing. */
			ClientNameplateSettings.SetShowGuild(false);
			ClientNameplateSettings.SetShowTitles(false);

			LogAssert.IsTrue(ClientNameplateSettings.IsRowVisible(NameplateSlot.Name),
				"the name row must always be drawn");
			LogAssert.IsTrue(ClientNameplateSettings.IsRowVisible(NameplateSlot.Status),
				"the status row must always be drawn");
			LogAssert.IsTrue(ClientNameplateSettings.IsRowVisible(NameplateSlot.Custom),
				"a developer's own row must always be drawn");
		}

		[Test]
		public void TheCachedOverload_AnswersTheSameAsTheReadingOne()
		{
			/* The renderer caches the two flags and calls the overload rather than reading the
			 * configuration store per row per plate per frame. The two must not drift apart. */
			ClientNameplateSettings.SetShowGuild(false);
			ClientNameplateSettings.SetShowTitles(true);

			foreach (NameplateSlot slot in new[]
			{
				NameplateSlot.Name, NameplateSlot.GuildName,
				NameplateSlot.InteractableType, NameplateSlot.Status,
			})
			{
				LogAssert.AreEqual(
					ClientNameplateSettings.IsRowVisible(slot),
					ClientNameplateSettings.IsRowVisible(slot,
						ClientNameplateSettings.ShowGuild, ClientNameplateSettings.ShowTitles),
					$"the two overloads must agree about {slot}");
			}
		}

		// --- The panel actually offers them ------------------------------------------------------

		[Test]
		public void EveryNameplateControl_IsDeclaredInTheOptionsPanel()
		{
			/* The settings are useless if nothing writes them, and a renamed element resolves to
			 * null and binds nothing — silently, because every binding here is null-guarded. */
			string uxml = ReadSource(OptionsUxml);

			foreach (string name in new[]
			{
				"nameplate-scale-slider",
				"nameplate-opacity-slider",
				"nameplate-background-slider",
				"nameplate-max-slider",
				"nameplate-show-guild-toggle",
				"nameplate-show-titles-toggle",
			})
			{
				LogAssert.IsTrue(uxml.Contains($"name=\"{name}\""),
					$"UIOptions.uxml must declare {name}");
			}
		}

		[Test]
		public void TheAuthoredSliderBounds_MatchTheSettings()
		{
			/* The bindings set lowValue/highValue at runtime, so a disagreement is invisible while
			 * playing — but the authored values are what the panel renders with before the binding
			 * runs, and what a designer reads when tuning the page. */
			string uxml = ReadSource(OptionsUxml);

			AssertSliderBounds(uxml, "nameplate-scale-slider",
				ClientNameplateSettings.MinimumScale, ClientNameplateSettings.MaximumScale);
			AssertSliderBounds(uxml, "nameplate-opacity-slider",
				ClientNameplateSettings.MinimumOpacity, ClientNameplateSettings.MaximumOpacity);
			AssertSliderBounds(uxml, "nameplate-background-slider",
				ClientNameplateSettings.MinimumBackgroundOpacity, ClientNameplateSettings.MaximumBackgroundOpacity);
			AssertSliderBounds(uxml, "nameplate-max-slider",
				ClientNameplateSettings.MinimumMaxVisible, ClientNameplateSettings.MaximumMaxVisible);
		}

		private static void AssertSliderBounds(string uxml, string name, float low, float high)
		{
			Match slider = Regex.Match(uxml,
				$"<ui:Slider name=\"{name}\"[^>]*low-value=\"([^\"]+)\"[^>]*high-value=\"([^\"]+)\"");

			LogAssert.IsTrue(slider.Success, $"{name} must author low-value and high-value");
			LogAssert.AreEqual(low, float.Parse(slider.Groups[1].Value, CultureInfo.InvariantCulture),
				$"{name} low-value must equal the minimum");
			LogAssert.AreEqual(high, float.Parse(slider.Groups[2].Value, CultureInfo.InvariantCulture),
				$"{name} high-value must equal the maximum");
		}

		private static string ReadSource(string projectRelativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), projectRelativePath);
			LogAssert.IsTrue(File.Exists(path), $"{projectRelativePath} must exist");
			return File.ReadAllText(path);
		}
	}
}
