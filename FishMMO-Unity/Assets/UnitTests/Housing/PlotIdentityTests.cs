using FishMMO.Shared;
using NUnit.Framework;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Tests for <see cref="PlotIdentity"/>.
	/// </summary>
	/// <remarks>
	/// A plot row is found by scene name and key, and the unique index on that pair is what stops
	/// one foundation becoming several rows with different owners. These tests cover the
	/// canonicalisation that index depends on: two designers writing the same key in different
	/// casing must land on the same plot, not on two.
	/// </remarks>
	[TestFixture]
	public class PlotIdentityTests
	{
		[Test]
		public void Default_IsNotValid()
		{
			Assert.IsFalse(default(PlotIdentity).IsValid);
		}

		[Test]
		public void TryCreate_KeepsTheSceneAndKey()
		{
			Assert.IsTrue(PlotIdentity.TryCreate("StartScene", "riverside_01", out PlotIdentity identity));
			Assert.IsTrue(identity.IsValid);
			Assert.AreEqual("StartScene", identity.SceneName);
			Assert.AreEqual("riverside_01", identity.PlotKey);
		}

		/// <summary>
		/// The whole point of canonicalising. Without it these are two rows that read as one plot
		/// wherever the key is displayed, each with its own owner.
		/// </summary>
		[Test]
		public void KeysDifferingOnlyInCase_AreTheSamePlot()
		{
			Assert.IsTrue(PlotIdentity.TryCreate("StartScene", "Riverside_01", out PlotIdentity upper));
			Assert.IsTrue(PlotIdentity.TryCreate("StartScene", "riverside_01", out PlotIdentity lower));

			Assert.AreEqual(lower, upper);
			Assert.IsTrue(lower == upper);
			Assert.AreEqual(lower.GetHashCode(), upper.GetHashCode());
		}

		[Test]
		public void SurroundingWhitespace_IsTrimmedFromBothParts()
		{
			Assert.IsTrue(PlotIdentity.TryCreate("  StartScene  ", "  riverside_01  ", out PlotIdentity identity));

			Assert.AreEqual("StartScene", identity.SceneName);
			Assert.AreEqual("riverside_01", identity.PlotKey);
		}

		/// <summary>
		/// Scene names are not canonicalised the way keys are: they name a Unity asset, so their
		/// casing is not the designer's to vary.
		/// </summary>
		[Test]
		public void SceneNames_KeepTheirCasing()
		{
			Assert.IsTrue(PlotIdentity.TryCreate("StartScene", "plot", out PlotIdentity identity));
			Assert.AreEqual("StartScene", identity.SceneName);
		}

		[TestCase(null, "plot")]
		[TestCase("", "plot")]
		[TestCase("   ", "plot")]
		[TestCase("StartScene", null)]
		[TestCase("StartScene", "")]
		[TestCase("StartScene", "   ")]
		public void TryCreate_RejectsMissingParts(string sceneName, string plotKey)
		{
			Assert.IsFalse(PlotIdentity.TryCreate(sceneName, plotKey, out PlotIdentity identity));
			Assert.IsFalse(identity.IsValid);
		}

		/// <summary>
		/// Rejected here, against the same limit the column uses, so an over-long key fails while a
		/// designer still has the scene open rather than as a truncation or an insert error later.
		/// </summary>
		[Test]
		public void TryCreate_RejectsAKeyTooLongForItsColumn()
		{
			string key = new string('a', PlotIdentity.MaxPlotKeyLength + 1);

			Assert.IsFalse(PlotIdentity.TryCreate("StartScene", key, out _));
		}

		[Test]
		public void TryCreate_AcceptsAKeyExactlyAtTheLimit()
		{
			string key = new string('a', PlotIdentity.MaxPlotKeyLength);

			Assert.IsTrue(PlotIdentity.TryCreate("StartScene", key, out PlotIdentity identity));
			Assert.AreEqual(PlotIdentity.MaxPlotKeyLength, identity.PlotKey.Length);
		}

		[Test]
		public void TryCreate_RejectsASceneNameTooLongForItsColumn()
		{
			string sceneName = new string('a', PlotIdentity.MaxSceneNameLength + 1);

			Assert.IsFalse(PlotIdentity.TryCreate(sceneName, "plot", out _));
		}

		/// <summary>
		/// Length is measured after trimming, so padding alone must not push a valid key over.
		/// </summary>
		[Test]
		public void TryCreate_MeasuresLengthAfterTrimming()
		{
			string key = "  " + new string('a', PlotIdentity.MaxPlotKeyLength) + "  ";

			Assert.IsTrue(PlotIdentity.TryCreate("StartScene", key, out _));
		}

		[Test]
		public void Normalize_HandlesNull()
		{
			Assert.AreEqual(string.Empty, PlotIdentity.Normalize(null));
		}

		[Test]
		public void PlotsInDifferentScenes_AreDifferentPlots()
		{
			Assert.IsTrue(PlotIdentity.TryCreate("SceneA", "plot", out PlotIdentity a));
			Assert.IsTrue(PlotIdentity.TryCreate("SceneB", "plot", out PlotIdentity b));

			Assert.AreNotEqual(a, b);
			Assert.IsTrue(a != b);
		}

		[Test]
		public void ToString_NamesTheSceneAndKey()
		{
			Assert.IsTrue(PlotIdentity.TryCreate("StartScene", "Riverside_01", out PlotIdentity identity));
			Assert.AreEqual("StartScene/riverside_01", identity.ToString());
		}
	}
}
