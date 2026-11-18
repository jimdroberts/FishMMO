using NUnit.Framework;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests
{
	public partial class BiomeProceduralArtTests
	{
		/// <summary>
		/// I2j-b (2026-10-18): one authoring run marks a template's orchards (none), field layout (anywhere), old terraces (none) and
		/// channel margin (none) as the tool's, a second changes nothing, and hand edits to each are kept. Unity-only (it makes a
		/// BiomeTemplate).
		/// </summary>
		[Test]
		public void Authoring_WritesNoOrchardsLayoutOldTerracesOrMargin_OnceAndKeepsHandEdits()
		{
			// The Valley (unauthored): no orchards, layout, old terraces or margin (the Hills round, 2026-10-26, gave Hills all but the margin).
			BiomeTemplate hills = MakeBiome("Valley");
			BiomeArtSpec.Entry entry = BiomeArtSpec.For("Valley");
			var source = new FakeSource(created);

			var first = new BiomeArtAuthoring.Report();
			BiomeArtAuthoring.Author(hills, entry, source, first, true);
			foreach (string field in new[] { BiomeTemplate.Authored.Orchards, BiomeTemplate.Authored.FieldLayout, BiomeTemplate.Authored.AbandonedTerraces, BiomeTemplate.Authored.ChannelMargin })
			{
				Assert.That(hills.IsAuthored(field), Is.True, field);
			}
			Assert.That(BiomeTemplateArt.Orchards(hills), Is.Empty);
			Assert.That(BiomeTemplateArt.FieldLayout(hills).Any, Is.False);
			Assert.That(BiomeTemplateArt.AbandonedTerraces(hills).Any, Is.False);
			Assert.That(BiomeTemplateArt.ChannelMargin(hills).Defined, Is.False);
			foreach (BiomeFieldCrop crop in hills.FieldCrops)
			{
				Assert.That(crop.Riser, Is.EqualTo(BiomeTerraceRiser.None));
				Assert.That(crop.Rows, Is.EqualTo(0f));
			}

			var second = new BiomeArtAuthoring.Report();
			Assert.IsFalse(BiomeArtAuthoring.Author(hills, entry, source, second, true), "a second run changed something:\n" + second);

			hills.FieldLayout = new BiomeFieldLayout(150f, 0f);
			hills.AbandonedTerraces = new BiomeAbandonedTerraces(2);
			hills.ChannelMargin = new BiomeChannelMargin(1, 4f);
			var third = new BiomeArtAuthoring.Report();
			BiomeArtAuthoring.Author(hills, entry, source, third, true);
			Assert.That(hills.FieldLayout.Bottomland, Is.EqualTo(150f), "a hand-set layout was undone:\n" + third);
			Assert.That(hills.AbandonedTerraces.Flights, Is.EqualTo(2));
			Assert.That(hills.ChannelMargin.DetailLayer, Is.EqualTo(1));
			Assert.That(BiomeTemplateArt.FieldLayout(hills).Bottomland, Is.EqualTo(150f));
		}
	}
}
