using System.Collections.Generic;
using NUnit.Framework;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests
{
	public partial class BiomeProceduralArtTests
	{
		/// <summary>
		/// One authoring run writes a template's linear features (the woodland's laid hedges since its round G1) and
		/// marks them as the tool's, a second changes nothing, and a hand-made style is kept beside them. Unity-only (it makes a BiomeTemplate).
		/// </summary>
		[Test]
		public void Authoring_WritesTheSpecsLinearFeatures_OnceAndKeepsHandEdits()
		{
			BiomeTemplate woodland = MakeBiome("Woodland");
			BiomeArtSpec.Entry entry = BiomeArtSpec.For("Woodland");
			var source = new FakeSource(created);
			Assert.That(woodland.LinearFeatures, Is.Empty);

			var first = new BiomeArtAuthoring.Report();
			BiomeArtAuthoring.Author(woodland, entry, source, first, true);
			Assert.That(woodland.IsAuthored(BiomeTemplate.Authored.LinearFeatures), Is.True);
			int authored = woodland.LinearFeatures.Count;
			Assert.That(authored, Is.GreaterThan(0), "the woodland's hedges:\n" + first);
			Assert.That(BiomeTemplateArt.LinearFeatures(woodland), Is.EqualTo(new List<BiomeLinearStyle>(woodland.LinearFeatures)));

			var second = new BiomeArtAuthoring.Report();
			Assert.IsFalse(BiomeArtAuthoring.Author(woodland, entry, source, second, true), "a second run changed something:\n" + second);

			var hedge = new BiomeLinearStyle(BiomeLinearForm.Laid, BiomeLinearPlace.Lanes, 0.7f);
			woodland.LinearFeatures.Add(hedge);
			var third = new BiomeArtAuthoring.Report();
			BiomeArtAuthoring.Author(woodland, entry, source, third, true);
			Assert.That(woodland.LinearFeatures.Count, Is.EqualTo(authored + 1), "a hand-made style was removed:\n" + third);
			Assert.That(woodland.LinearFeatures.Exists(f => f.ToString() == hedge.ToString()), Is.True, "the hand-made style is kept");
			Assert.That(BiomeTemplateArt.LinearFeatures(woodland), Is.EqualTo(new List<BiomeLinearStyle>(woodland.LinearFeatures)));
		}
	}
}
