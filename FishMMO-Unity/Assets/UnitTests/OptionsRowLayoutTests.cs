using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs that each options row carries one setting.
	/// </summary>
	/// <remarks>
	/// <para>
	/// A row lays its children out across the panel's width, so two settings sharing one row do not
	/// wrap — they squeeze, and the second control runs off the right edge of the panel. That is
	/// what happened when the antialiasing and texture filtering settings were merged: both were
	/// added by separate changes in the same place, the rows collapsed into one, and the resulting
	/// panel showed a truncated dropdown and grew a horizontal scrollbar.
	/// </para>
	/// <para>
	/// It is a merge hazard rather than a typo — the two additions were individually correct — so it
	/// is worth a test rather than care. Nothing about the markup makes an overloaded row look wrong
	/// on the page.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class OptionsRowLayoutTests
	{
		private const string OptionsUxml =
			"Assets/Scripts/Client/GUI/World/Options/UIOptions.uxml";

		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path);
		}

		/// <summary>The inner markup of every options row.</summary>
		private static MatchCollection Rows()
		{
			return Regex.Matches(
				ReadSource(OptionsUxml),
				"<ui:VisualElement class=\"options-row\">(.*?)</ui:VisualElement>",
				RegexOptions.Singleline);
		}

		[Test]
		public void ThePanelStillHasRows()
		{
			/* Guards the two tests below from passing on an empty match set, which is what they
			 * would do if the class name or the markup shape ever changed. */
			LogAssert.IsTrue(Rows().Count > 10,
				"the options panel must still be built from options-row elements");
		}

		[Test]
		public void NoRowCarriesTwoControls()
		{
			/* The failure: a row does not wrap, so the second control is pushed off the panel. */
			foreach (Match row in Rows())
			{
				int controls = Regex.Matches(
					row.Groups[1].Value,
					"<ui:(DropdownField|Slider|Toggle|TextField)\\b").Count;

				LogAssert.IsTrue(controls <= 1,
					$"an options row carries {controls} controls; each row holds one setting: {Caption(row)}");
			}
		}

		[Test]
		public void NoRowCarriesTwoCaptions()
		{
			/* Two captions is the same fault seen from the other side, and catches the case where
			 * the second setting's control has not been added yet. */
			foreach (Match row in Rows())
			{
				int captions = Regex.Matches(
					row.Groups[1].Value,
					"<ui:Label[^>]*class=\"fish-label").Count;

				LogAssert.IsTrue(captions <= 1,
					$"an options row carries {captions} captions: {Caption(row)}");
			}
		}

		/// <summary>First caption in a row, to name it in a failure.</summary>
		private static string Caption(Match row)
		{
			Match text = Regex.Match(row.Groups[1].Value, "text=\"([^\"]+)\"");
			return text.Success ? text.Groups[1].Value : "(unnamed row)";
		}
	}
}
