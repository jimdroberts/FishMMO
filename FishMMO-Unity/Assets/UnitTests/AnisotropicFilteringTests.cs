using System;
using System.Collections.Generic;
using System.IO;
using FishMMO.Client;
using NUnit.Framework;
using UnityEngine;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs for the texture filtering option.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Anisotropic filtering is authored per quality level -- the three shipped levels use Disable,
	/// Enable and ForceEnable respectively -- so a player's choice is overwritten every time the
	/// quality level is set. VSync already had that problem and already solves it by re-applying
	/// after the level change; this has to do the same or the setting silently reverts the next
	/// time anyone touches the quality dropdown.
	/// </para>
	/// <para>
	/// The value also lives in a project asset, which is why the editor safeguard that restores
	/// the authored quality settings after play mode has to cover it too. Without that, running the
	/// client once leaves a source-control change describing whichever filtering mode the last
	/// person to press Play happened to prefer.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class AnisotropicFilteringTests
	{
		private AnisotropicFiltering original;

		[SetUp]
		public void SetUp()
		{
			original = QualitySettings.anisotropicFiltering;
		}

		[TearDown]
		public void TearDown()
		{
			QualitySettings.anisotropicFiltering = original;
		}

		[Test]
		public void EachOption_SelectsItsOwnMode()
		{
			var expected = new Dictionary<ClientDisplaySettings.AnisotropicOption, AnisotropicFiltering>
			{
				{ ClientDisplaySettings.AnisotropicOption.Off, AnisotropicFiltering.Disable },
				{ ClientDisplaySettings.AnisotropicOption.PerTexture, AnisotropicFiltering.Enable },
				{ ClientDisplaySettings.AnisotropicOption.Forced, AnisotropicFiltering.ForceEnable },
			};

			foreach (var pair in expected)
			{
				ClientDisplaySettings.ApplyAnisotropicFiltering(pair.Key);

				LogAssert.AreEqual(pair.Value, QualitySettings.anisotropicFiltering,
					$"{pair.Key} must select {pair.Value}");
			}
		}

		[Test]
		public void EveryOption_MapsToADistinctMode()
		{
			/* Two options resolving to the same mode would leave the player a choice that does
			 * nothing -- which is indistinguishable, from their side, from the setting being
			 * broken. */
			var seen = new HashSet<AnisotropicFiltering>();

			foreach (ClientDisplaySettings.AnisotropicOption option in
				Enum.GetValues(typeof(ClientDisplaySettings.AnisotropicOption)))
			{
				ClientDisplaySettings.ApplyAnisotropicFiltering(option);

				LogAssert.IsTrue(seen.Add(QualitySettings.anisotropicFiltering),
					$"{option} selects a mode another option already selects");
			}
		}

		[Test]
		public void TheStoredOrdinals_AreFixed()
		{
			/* The save file holds these numbers. Reordering the enum would silently turn an
			 * existing player's choice into a different one. */
			LogAssert.AreEqual(0, (int)ClientDisplaySettings.AnisotropicOption.Off);
			LogAssert.AreEqual(1, (int)ClientDisplaySettings.AnisotropicOption.PerTexture);
			LogAssert.AreEqual(2, (int)ClientDisplaySettings.AnisotropicOption.Forced);
		}

		[Test]
		public void TheDefault_RespectsHowTheArtWasImported()
		{
			/* Per-texture rather than Forced. Forcing costs little on a modern GPU, but it is still
			 * a decision about someone else's art, so it is offered rather than imposed. */
			LogAssert.AreEqual(
				ClientDisplaySettings.AnisotropicOption.PerTexture,
				ClientDisplaySettings.DefaultAnisotropicFiltering,
				"the default must not override what each texture was imported with");
		}

		[Test]
		public void AQualityLevelChange_ReAppliesTheChoice()
		{
			/* The failure this exists to prevent. Every quality level authors its own anisotropic
			 * mode, so without the re-apply the player's choice survives only until they touch the
			 * quality dropdown -- and then reverts with nothing to explain it.
			 *
			 * Pinned in source: driving it for real would mean switching quality level inside a
			 * test, which reloads textures and render targets and writes to the project asset. */
			string source = File.ReadAllText(Path.Combine(
				Directory.GetCurrentDirectory(),
				"Assets/Scripts/Client/Settings/ClientDisplaySettings.cs"));

			int apply = source.IndexOf(
				"public static void ApplyQualityLevel(", StringComparison.Ordinal);
			LogAssert.IsTrue(apply >= 0, "ApplyQualityLevel must still exist");

			// Bounded to that method, so the declaration further down cannot satisfy this.
			int end = source.IndexOf("#if UNITY_EDITOR", apply, StringComparison.Ordinal);
			LogAssert.IsTrue(end > apply, "the end of ApplyQualityLevel must be locatable");

			string body = source.Substring(apply, end - apply);

			LogAssert.IsTrue(body.Contains("ApplySavedAnisotropicFiltering()"),
				"a quality change must re-apply the player's filtering choice, as it does for VSync");
		}

		[Test]
		public void TheEditorSafeguard_RestoresTheAuthoredMode()
		{
			/* QualitySettings is a project asset. The class already captures and restores the
			 * authored quality level and VSync count so a play-mode session cannot leave a
			 * source-control change behind; anisotropic filtering is written the same way and needs
			 * the same treatment, or this feature reintroduces exactly that trap. */
			string source = File.ReadAllText(Path.Combine(
				Directory.GetCurrentDirectory(),
				"Assets/Scripts/Client/Settings/ClientDisplaySettings.cs"));

			LogAssert.IsTrue(source.Contains("authoredAnisotropicFiltering = QualitySettings.anisotropicFiltering"),
				"the authored filtering mode must be captured before anything overwrites it");

			LogAssert.IsTrue(source.Contains("QualitySettings.anisotropicFiltering = authoredAnisotropicFiltering"),
				"the authored filtering mode must be put back");
		}

		[Test]
		public void TheDropdownLabels_CoverEveryOption()
		{
			/* The dropdown stores the selected index, so labels and enum must stay the same length
			 * and order or a click maps onto a different mode. */
			string source = File.ReadAllText(Path.Combine(
				Directory.GetCurrentDirectory(),
				"Assets/Scripts/Client/GUI/World/Options/UITKOptions.cs"));

			int labels = source.IndexOf("AnisotropicLabels =", StringComparison.Ordinal);
			LogAssert.IsTrue(labels >= 0, "the dropdown must still declare its labels");

			int end = source.IndexOf("};", labels, StringComparison.Ordinal);
			string block = source.Substring(labels, end - labels);

			int optionCount = Enum.GetValues(typeof(ClientDisplaySettings.AnisotropicOption)).Length;
			int labelCount = block.Split('"').Length / 2;

			LogAssert.AreEqual(optionCount, labelCount,
				"there must be exactly one dropdown label per option");
		}
	}
}
