using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests
{
	public partial class BiomeProceduralArtTests
	{
		/// <summary>
		/// I2h (2026-10-18): a template made before the sea look existed reads today's look; one authoring run writes and marks
		/// it, a second changes nothing, and a look edited by hand (a bog coast's peat-brown sea) is kept. Unity-only (it makes a
		/// BiomeTemplate).
		/// </summary>
		[Test]
		public void Authoring_WritesTheSeaLook_OnceAndKeepsAHandEditedLook()
		{
			BiomeTemplate forest = MakeBiome("Forest");
			BiomeArtSpec.Entry entry = BiomeArtSpec.For("Forest");
			var source = new FakeSource(created);
			Assert.That(forest.SeaLook.Signature(), Is.EqualTo(BiomeSeaLooks.Today.Signature()), "the field's default is today's look");
			Assert.That(forest.IsAuthored(BiomeTemplate.Authored.SeaLook), Is.False);

			var first = new BiomeArtAuthoring.Report();
			BiomeArtAuthoring.Author(forest, entry, source, first, true);
			Assert.That(forest.IsAuthored(BiomeTemplate.Authored.SeaLook), Is.True);
			Assert.That(forest.SeaLook.Signature(), Is.EqualTo(BiomeSeaLooks.Today.Signature()));
			Assert.That(BiomeSeaLooks.Of(forest)?.Signature(), Is.EqualTo(BiomeSeaLooks.Today.Signature()));

			var second = new BiomeArtAuthoring.Report();
			Assert.IsFalse(BiomeArtAuthoring.Author(forest, entry, source, second, true), "a second run changed something:\n" + second);

			var peat = new BiomeSeaLook(new Color(0.18f, 0.12f, 0.05f, 1f), 6f, 1.6f);
			forest.SeaLook = peat;
			var third = new BiomeArtAuthoring.Report();
			BiomeArtAuthoring.Author(forest, entry, source, third, true);
			Assert.That(forest.SeaLook.Signature(), Is.EqualTo(peat.Signature()), "a hand-edited look was overwritten:\n" + third);
			Assert.That(third.ValuesKept, Is.GreaterThanOrEqualTo(1));

			forest.SeaLook = default;
			Assert.That(BiomeSeaLooks.Of(forest)?.Signature(), Is.EqualTo(BiomeSeaLooks.Today.Signature()), "a zeroed look reads today's");
		}
	}
}
