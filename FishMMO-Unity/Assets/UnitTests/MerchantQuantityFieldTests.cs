using System;
using System.IO;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs that the merchant's quantity field shows the number it holds (issues #263, #277).
	/// </summary>
	/// <remarks>
	/// <para>
	/// A UI Toolkit <c>IntegerField</c> renders its text AS A RESULT of being written to. Guarding
	/// that write on "only if the number changed" therefore skips the one case that matters: the
	/// UXML authors the field as <c>value="1"</c>, so the <c>SetQuantity(1)</c> that runs when an
	/// entry is selected found the values equal, wrote nothing, and left the inner text element
	/// empty.
	/// </para>
	/// <para>
	/// The field was live and editable throughout — it simply displayed nothing, which reads as a
	/// dead control rather than a blank one. Hiding the panel disposes the visual tree, so the
	/// re-queried field returns to the authored 1 with no text and the fault recurs, which is why
	/// it presented as intermittent.
	/// </para>
	/// <para>
	/// The rule this pins: a control whose visible text is produced by the write must be written
	/// unconditionally. Comparing before assigning is an optimisation that costs correctness here.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class MerchantQuantityFieldTests
	{
		private const string MerchantPath =
			"Assets/Scripts/Client/GUI/World/Merchant/UITKMerchant.cs";

		/// <summary>Source text with line endings normalised, so bounds do not depend on checkout.</summary>
		private static string ReadSource(string relativePath)
		{
			string path = Path.Combine(Directory.GetCurrentDirectory(), relativePath);
			LogAssert.IsTrue(File.Exists(path), $"{relativePath} not found at {path}.");
			return File.ReadAllText(path).Replace("\r\n", "\n");
		}

		/// <summary>The body of a named method, bounded by the next member's signature.</summary>
		private static string MethodBody(string source, string signature, string nextSymbol)
		{
			int start = source.IndexOf(signature, StringComparison.Ordinal);
			LogAssert.IsTrue(start >= 0, $"the source must still declare {signature}");

			int end = source.IndexOf(nextSymbol, start, StringComparison.Ordinal);
			LogAssert.IsTrue(end > start, $"the end of {signature} must be locatable");

			return source.Substring(start, end - start);
		}

		[Test]
		public void TheQuantityFieldIsWrittenEvenWhenTheNumberIsUnchanged()
		{
			string body = MethodBody(ReadSource(MerchantPath),
				"private void SetQuantity(int quantity)", "private void RefreshQuantityControls");

			LogAssert.IsTrue(body.Contains("SetValueWithoutNotify(clamped)"),
				"the quantity must still be pushed into the field");

			/* The defect in one line. Any comparison of the field's current value against the value
			 * being written re-introduces the skip that leaves the text blank. */
			LogAssert.IsFalse(body.Contains("quantityField.value != clamped"),
				"guarding the write on a value comparison leaves the field's text unrendered");
		}

		[Test]
		public void SelectingAnEntryStillSeedsTheQuantity()
		{
			/* The write only helps if something calls it on selection. This is the call the guard
			 * was swallowing, so it is the one worth pinning. */
			string source = ReadSource(MerchantPath);

			LogAssert.IsTrue(source.Contains("SetQuantity(1)"),
				"selecting an entry must seed the quantity, or the field starts empty");
		}
	}
}
