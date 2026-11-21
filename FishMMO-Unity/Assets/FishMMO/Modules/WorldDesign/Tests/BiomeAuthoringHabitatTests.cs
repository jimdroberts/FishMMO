using FishMMO.Client;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;
using FishMMO.Shared;
using NUnit.Framework;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The biome authoring writes the habitat, sea life, vines, gathering and herbs onto the template, and the habitat's Alien bit is read live (split from BiomeProceduralArtTests, Phase 4, 2026-11-17). How the Ambient Life module reads a template is its own test.
	/// </summary>
	[TestFixture]
	public class BiomeAuthoringHabitatTests
	{
		private readonly List<Object> created = new List<Object>();

		[TearDown]
		public void DestroyCreated()
		{
			foreach (Object o in created)
			{
				if (o != null)
				{
					Object.DestroyImmediate(o);
				}
			}
			created.Clear();
		}

		/// <summary>The biome art source the authoring reads (BiomeProceduralArtTests' fake).</summary>
		private sealed class FakeSource : IBiomeArtSource
		{
			private readonly Dictionary<string, TerrainLayer> layers = new Dictionary<string, TerrainLayer>();
			private readonly Dictionary<string, GameObject> prefabs = new Dictionary<string, GameObject>();
			private readonly List<Object> created;

			public FakeSource(List<Object> created) => this.created = created;

			public TerrainLayer Layer(string family)
			{
				if (!layers.TryGetValue(family, out TerrainLayer layer))
				{
					layer = new TerrainLayer { name = "Ground_" + family };
					created.Add(layer);
					layers[family] = layer;
				}
				return layer;
			}

			public GameObject Prefab(string name)
			{
				if (!prefabs.TryGetValue(name, out GameObject prefab))
				{
					prefab = new GameObject(name) { hideFlags = HideFlags.HideAndDontSave };
					created.Add(prefab);
					prefabs[name] = prefab;
				}
				return prefab;
			}
		}

		private BiomeTemplate MakeBiome(string name)
		{
			var biome = ScriptableObject.CreateInstance<BiomeTemplate>();
			biome.name = name;
			created.Add(biome);
			return biome;
		}

		/// <summary>
		/// The template carries its wildlife, sea life, vines, gathering resources and herb-grove herbs (2026-10-16): one run
		/// writes what the biome's name gave (the client and the cut then read those), a second changes nothing, and values
		/// edited or cleared by hand are kept. A template never authored still reads its name.
		/// </summary>
		[Test]
		public void Authoring_WritesWildlifeVinesGatheringAndHerbs_OnceAndKeepsHandEdits()
		{
			BiomeTemplate forest = MakeBiome("Forest");
			BiomeArtSpec.Entry entry = BiomeArtSpec.For("Forest");
			var source = new FakeSource(created);
			Assert.That(BiomeTemplateArt.Vines(forest).Species, Is.EqualTo(VineBiomes.For("Forest").Species), "unauthored: the vine table's row");

			var first = new BiomeArtAuthoring.Report();
			Assert.IsTrue(BiomeArtAuthoring.Author(forest, entry, source, first, true));
			// The spec's habitat: the forest's own and, since its round, the streams' FreshWater (dippers, newts, dragonflies).
			Assert.That(entry.Habitat & BiomeHabitat.Forest, Is.EqualTo(BiomeHabitat.Forest));
			Assert.That(forest.LiveHabitat, Is.EqualTo(entry.Habitat));
			Assert.That(forest.Habitat, Is.EqualTo(entry.Habitat));
			Assert.That(forest.SeaLife, Is.Empty);
			VineBiomes.Habitat vines = BiomeTemplateArt.Vines(forest);
			// The file's own vines (the Forest round's honeysuckle among them), as the authoring writes them.
			BiomeVines own = entry.VineGrowth();
			Assert.That(vines.Species, Is.EqualTo(own.Species.ConvertAll(v => (v.Species, v.Weight)).ToArray()));
			Assert.That(vines.Caves, Is.EqualTo(own.Caves));
			Assert.That(BiomeTemplateArt.Gathering(forest), Is.EqualTo(entry.GatheringResources()));
			Assert.That(forest.HerbGroveHerbs.ConvertAll(p => p.name), Is.EqualTo(entry.GroveHerbs()));

			var second = new BiomeArtAuthoring.Report();
			Assert.IsFalse(BiomeArtAuthoring.Author(forest, entry, source, second, true), "a second run changed something:\n" + second);

			forest.Habitat = BiomeHabitat.Desert;
			forest.Vines = new BiomeVines();
			forest.GatheringResources.Clear();
			forest.GatheringResources.Add("Iron Vein");
			var third = new BiomeArtAuthoring.Report();
			BiomeArtAuthoring.Author(forest, entry, source, third, true);
			Assert.That(forest.Habitat, Is.EqualTo(BiomeHabitat.Desert), "a hand-set habitat was overwritten:\n" + third);
			Assert.That(BiomeTemplateArt.Vines(forest), Is.Null, "vines cleared by hand grew again");
			Assert.That(BiomeTemplateArt.Gathering(forest), Is.EqualTo(new[] { "Iron Vein" }));
			// The habitat, the vines' densities and their species (one mark each since 2026-10-16, I5b), the gathering list.
			Assert.That(third.ValuesKept, Is.EqualTo(4));

			// A sea biome's sea life; a dry one's empty lists are decisions, not gaps.
			BiomeTemplate reef = MakeBiome("Coral Reef");
			BiomeArtAuthoring.Author(reef, BiomeArtSpec.For("Coral Reef"), source, new BiomeArtAuthoring.Report(), true);
			// Its file's own (the reef's round names its sea life since), kind and weight.
			List<BiomeSeaLife> reefLife = BiomeArtSpec.For("Coral Reef").SeaLife();
			Assert.That(reefLife, Is.Not.Empty);
			Assert.That(reef.SeaLife.ConvertAll(e => e.Kind), Is.EqualTo(reefLife.ConvertAll(e => e.Kind)));
			Assert.That(reef.SeaLife.ConvertAll(e => e.Weight), Is.EqualTo(reefLife.ConvertAll(e => e.Weight)));
			BiomeTemplate desert = MakeBiome("Desert");
			BiomeArtAuthoring.Author(desert, BiomeArtSpec.For("Desert"), source, new BiomeArtAuthoring.Report(), true);
			Assert.That(desert.IsAuthored(BiomeTemplate.Authored.Vines), Is.True);
			Assert.That(desert.IsAuthored(BiomeTemplate.Authored.SeaLife), Is.True);
			Assert.That(desert.SeaLife, Is.Empty);
			// The desert's own vines since its round (Pergularia on its ruins and trees).
			Assert.That(BiomeTemplateArt.Vines(desert).Species, Is.EqualTo(BiomeArtSpec.For("Desert").VineGrowth().Species.ConvertAll(v => (v.Species, v.Weight)).ToArray()));
		}

		/// <summary>
		/// I4b (2026-10-16): the habitat's Alien bit is the template's air, read live, never stored. A bit stored by I4's authoring
		/// (with its mark) is masked on read, dropped by the next authoring, and the habitat stays the tool's to rewrite; a
		/// hand-set Alien bit is dropped too; an unauthored template takes it from its air as well.
		/// </summary>
		[Test]
		public void Habitat_ReadsAlienLive_AndAuthoringDropsAStoredBit()
		{
			BiomeTemplate geysers = MakeBiome("Ice Geyser Field");
			geysers.Atmosphere = BiomeAtmosphereRequirement.Airless | BiomeAtmosphereRequirement.Thin;
			geysers.Requires = BiomeWorldRequirement.Cryovolcanic;

			// As I4's authoring left it: the bit stored, and in the mark.
			var so = new SerializedObject(geysers);
			so.FindProperty("habitat").intValue = (int)(BiomeHabitat.Ice | BiomeHabitat.Alien);
			so.ApplyModifiedPropertiesWithoutUndo();
			geysers.SetAuthoredFingerprint(BiomeTemplate.Authored.Habitat, "Ice, Alien");
			Assert.That(geysers.Habitat, Is.EqualTo(BiomeHabitat.Ice), "masked on read");
			Assert.That(geysers.LiveHabitat, Is.EqualTo(BiomeHabitat.Ice | BiomeHabitat.Alien));
			var source = new FakeSource(created);
			var report = new BiomeArtAuthoring.Report();
			Assert.IsTrue(BiomeArtAuthoring.Author(geysers, BiomeArtSpec.For("Ice Geyser Field"), source, report, true));
			Assert.That(new SerializedObject(geysers).FindProperty("habitat").intValue, Is.EqualTo((int)BiomeHabitat.Ice), "the stored bit is dropped:\n" + report);
			Assert.That(geysers.AuthoredFingerprint(BiomeTemplate.Authored.Habitat), Is.EqualTo("Ice"));
			Assert.That(report.ValuesKept, Is.Zero, "the tool's own habitat is not a hand edit:\n" + report);
			Assert.That(geysers.LiveHabitat, Is.EqualTo(BiomeHabitat.Ice | BiomeHabitat.Alien));

			// The air moves the creatures: given home air, the same template is ice alone.
			geysers.Atmosphere = BiomeAtmosphereRequirement.Any;
			geysers.Requires = BiomeWorldRequirement.None;
			Assert.That(geysers.LiveHabitat, Is.EqualTo(BiomeHabitat.Ice));
			geysers.Habitat = BiomeHabitat.Ice | BiomeHabitat.Alien;
			Assert.That(geysers.Habitat, Is.EqualTo(BiomeHabitat.Ice), "setting the bit does nothing");

			// Alien ground alone: stored None, marked, read as Alien.
			BiomeTemplate dust = MakeBiome("Dust Sea");
			dust.Atmosphere = BiomeAtmosphereRequirement.Airless | BiomeAtmosphereRequirement.Thin;
			BiomeArtAuthoring.Author(dust, BiomeArtSpec.For("Dust Sea"), source, new BiomeArtAuthoring.Report(), true);
			Assert.That(dust.Habitat, Is.EqualTo(BiomeHabitat.None));
			Assert.That(dust.IsAuthored(BiomeTemplate.Authored.Habitat), Is.True);
			Assert.That(dust.LiveHabitat, Is.EqualTo(BiomeHabitat.Alien));
		}
	}
}
