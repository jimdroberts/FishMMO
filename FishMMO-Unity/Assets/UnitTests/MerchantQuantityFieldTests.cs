using System;
using System.IO;
using System.Text.RegularExpressions;
using NUnit.Framework;
using LogAssert = FishMMO.UnitTests.Harness.LogAssert;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// Proofs that the merchant's quantity field can actually draw the number it holds
	/// (issues #263, #277).
	/// </summary>
	/// <remarks>
	/// <para>
	/// The field was live and editable throughout and simply displayed nothing, which reads as a
	/// dead control rather than a blank one — and because hiding the panel disposes the visual tree
	/// it came back after every reopen, so it presented as intermittent.
	/// </para>
	/// <para>
	/// WHAT WAS NOT THE CAUSE, because it was written down here once and it was wrong: the write
	/// being guarded on <c>value != clamped</c>. <c>TextValueField&lt;T&gt;.SetValueWithoutNotify</c>
	/// re-applies that same equality test internally, so the guard was exactly its negation and
	/// removing it changed no behaviour at all. A fixture that pinned the guard's removal was
	/// pinning a no-op, and would have gone on passing after the real fault came back.
	/// </para>
	/// <para>
	/// WHAT WAS: the stylesheet. Unity's default theme pads the field's inner editable surface
	/// through a TYPE-qualified rule, and <c>.fish-input--compact</c> — the class that zeroes that
	/// padding — was written for <c>TextField</c> alone. The quantity box is an
	/// <c>IntegerField</c>, so the padding survived, and a 22px box with 10px of padding top and
	/// bottom leaves the glyph element nothing to fill. It collapsed to zero and drew no digits.
	/// </para>
	/// <para>
	/// So this fixture asserts the invariant that actually holds the fix up, and leaves the sweep
	/// to the thing that owns it: the field must wear the remedy, its TYPE must be one the height
	/// sweep reads, and the theme must name that type on the selector — which is the half a class
	/// selector alone cannot win. See <see cref="TextInputHeightTests"/>, whose
	/// <see cref="TextInputHeightTests.TheThemeNamesEveryFieldTypeThisSweepKnowsAbout"/> is what
	/// keeps the sweep and the stylesheet from drifting apart again.
	/// </para>
	/// </remarks>
	[TestFixture]
	public class MerchantQuantityFieldTests
	{
		private const string MerchantPath =
			"Assets/Scripts/Client/GUI/World/Merchant/UITKMerchant.cs";

		private const string MerchantLayoutPath =
			"Assets/Scripts/Client/GUI/World/Merchant/UIMerchant.uxml";

		/// <summary>The element name the panel queries with <c>root.Q</c> for the quantity box.</summary>
		private const string QuantityFieldName = "merchant-qty-field";

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
		public void TheQuantityFieldIsStillWrittenWithoutACondition()
		{
			/* Kept, but demoted from a proof to a guard rail: the write is not what draws the text,
			 * so this asserts only that nothing has re-introduced a branch around an assignment
			 * that has no reason to have one. */
			string body = MethodBody(ReadSource(MerchantPath),
				"private void SetQuantity(int quantity)", "private void RefreshQuantityControls");

			LogAssert.IsTrue(body.Contains("SetValueWithoutNotify(clamped)"),
				"the quantity must still be pushed into the field");

			LogAssert.IsFalse(body.Contains("quantityField.value != clamped"),
				"a comparison before the write duplicates the setter's own equality test and buys nothing");
		}

		[Test]
		public void TheQuantityFieldWearsTheCompactRemedyItsFieldTypeNeeds()
		{
			/* THE INVARIANT THE FIX RESTS ON. Three facts, all load bearing, and the fault was in
			 * the third: the box is a glyph-drawing field type the sweep reads, it wears the class
			 * that removes Unity's inner padding, and the theme names that TYPE on the selector —
			 * because the bare class loses the specificity fight and covers nothing on its own. */
			string layout = ReadSource(MerchantLayoutPath);

			Match field = Regex.Match(layout,
				"<ui:([A-Za-z][A-Za-z0-9_]*)\\b[^>]*name=\"" + Regex.Escape(QuantityFieldName) + "\"[^>]*>");
			LogAssert.IsTrue(field.Success,
				$"the merchant layout must still declare a field named {QuantityFieldName}");

			string fieldType = field.Groups[1].Value;

			LogAssert.IsTrue(Array.IndexOf(TextInputHeightTests.GlyphFieldTypes, fieldType) >= 0,
				$"{fieldType} is not a field type the height sweep reads, so this box's glyph height " +
				"is never measured — it is how the fault survived the TextField fix");

			Match classes = Regex.Match(field.Value, "class=\"([^\"]*)\"");
			LogAssert.IsTrue(classes.Success, "the quantity box must declare its classes");

			string[] worn = classes.Groups[1].Value.Split(
				new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries);

			LogAssert.IsTrue(Array.IndexOf(worn, TextInputHeightTests.CompactClass) >= 0,
				$"the quantity box must wear .{TextInputHeightTests.CompactClass}, or Unity's inner " +
				"padding starves the glyph box and the number never draws");

			LogAssert.IsTrue(TextInputHeightTests.ThemeCompactFieldTypes().Contains(fieldType),
				$"the theme's .{TextInputHeightTests.CompactClass} selector must name {fieldType}: " +
				"the bare class selector loses to Unity's type-qualified padding rule, which is why " +
				"this box stayed blank after the TextField fix");
		}

		[Test]
		public void SelectingAnEntryStillSeedsTheQuantity()
		{
			/* The write only helps if something calls it on selection. */
			string source = ReadSource(MerchantPath);

			LogAssert.IsTrue(source.Contains("SetQuantity(1)"),
				"selecting an entry must seed the quantity, or the field starts empty");
		}
	}
}
