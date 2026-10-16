using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests
{
	public partial class BiomeProceduralArtTests
	{
		/// <summary>
		/// I2e (2026-10-16): a template made before the water look existed reads today's look; one authoring run writes and
		/// marks it, a second changes nothing, and a look edited by hand (a peat bog's black water) is kept. The crops it writes
		/// lie flat, marked as before. Unity-only (it makes a BiomeTemplate).
		/// </summary>
		[Test]
		public void Authoring_WritesTheWaterLookAndFlatCrops_OnceAndKeepsAHandEditedLook()
		{
			BiomeTemplate forest = MakeBiome("Forest");
			BiomeArtSpec.Entry entry = BiomeArtSpec.For("Forest");
			var source = new FakeSource(created);
			Assert.That(forest.WaterLook.Signature(), Is.EqualTo(BiomeWaterLooks.Today.Signature()), "the field's default is today's look");
			Assert.That(forest.IsAuthored(BiomeTemplate.Authored.WaterLook), Is.False);
			Assert.That(BiomeTemplateArt.WaterLook(forest).Signature(), Is.EqualTo(BiomeWaterLooks.Today.Signature()));

			var first = new BiomeArtAuthoring.Report();
			BiomeArtAuthoring.Author(forest, entry, source, first, true);
			Assert.That(forest.IsAuthored(BiomeTemplate.Authored.WaterLook), Is.True);
			Assert.That(forest.WaterLook.Signature(), Is.EqualTo(BiomeWaterLooks.Today.Signature()));
			Assert.That(BiomeWaterLooks.Of(forest)?.Signature(), Is.EqualTo(BiomeWaterLooks.Today.Signature()));
			foreach (BiomeFieldCrop crop in forest.FieldCrops)
			{
				Assert.That(crop.Surface, Is.EqualTo(BiomeFieldSurface.Flat));
			}
			Assert.That(forest.FieldCrops.ConvertAll(c => c.ToString()), Is.EqualTo(new[] { "Detail_GrassWheat×1" }));

			var second = new BiomeArtAuthoring.Report();
			Assert.IsFalse(BiomeArtAuthoring.Author(forest, entry, source, second, true), "a second run changed something:\n" + second);

			var peat = new BiomeWaterLook(new Color(0.35f, 0.2f, 0.08f, 1f), new Color(0.03f, 0.015f, 0.005f, 1f), new Vector3(0.9f, 1.8f, 3.2f), 0.2f,
				new Color(0.25f, 0.15f, 0.07f, 1f), new Color(0.85f, 0.78f, 0.62f, 1f));
			forest.WaterLook = peat;
			var third = new BiomeArtAuthoring.Report();
			BiomeArtAuthoring.Author(forest, entry, source, third, true);
			Assert.That(forest.WaterLook.Signature(), Is.EqualTo(peat.Signature()), "a hand-edited look was overwritten:\n" + third);
			Assert.That(third.ValuesKept, Is.GreaterThanOrEqualTo(1));

			forest.WaterLook = default;
			Assert.That(BiomeTemplateArt.WaterLook(forest).Signature(), Is.EqualTo(BiomeWaterLooks.Today.Signature()), "a zeroed look reads today's");
		}
	}
}
