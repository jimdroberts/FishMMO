using System.Collections.Generic;
using FishMMO.Client;
using FishMMO.Shared.Biomes;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// How the Ambient Life module reads a biome template's habitat (AmbientGating.HabitatOfTemplate): an authored habitat
	/// with the Alien bit read live from the template's air, an unauthored one from the biome's name. (The World Design
	/// module's BiomeAuthoringHabitatTests check what its authoring writes; split there, Phase 4, 2026-11-18.)
	/// </summary>
	[TestFixture]
	public class AmbientHabitatOfTemplateTests
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

		private BiomeTemplate MakeBiome(string name)
		{
			var biome = ScriptableObject.CreateInstance<BiomeTemplate>();
			biome.name = name;
			created.Add(biome);
			return biome;
		}

		[Test]
		public void AnUnauthoredTemplate_ReadsItsName_AndAnAuthoredOne_ItsHabitat()
		{
			BiomeTemplate forest = MakeBiome("Forest");
			AmbientHabitat byName = AmbientGating.HabitatOfBiome("Forest", "Forest", forest.Atmosphere);
			Assert.That(AmbientGating.HabitatOfTemplate(forest), Is.EqualTo(byName), "unauthored: read from its name");
			forest.Habitat = BiomeHabitat.Desert;
			Assert.That(AmbientGating.HabitatOfTemplate(forest), Is.EqualTo(AmbientHabitat.Desert), "the template's own habitat wins");
		}

		[Test]
		public void TheAlienBit_IsTheTemplatesAir_ReadLive()
		{
			BiomeTemplate geysers = MakeBiome("Ice Geyser Field");
			geysers.Atmosphere = BiomeAtmosphereRequirement.Airless | BiomeAtmosphereRequirement.Thin;
			geysers.Requires = BiomeWorldRequirement.Cryovolcanic;
			Assert.That(AmbientGating.HabitatOfTemplate(geysers), Is.EqualTo(AmbientHabitat.Ice | AmbientHabitat.Alien), "unauthored: its name and its air");

			// A bit stored by an old authoring (with its mark) is masked on read; the air adds it back.
			var so = new SerializedObject(geysers);
			so.FindProperty("habitat").intValue = (int)(BiomeHabitat.Ice | BiomeHabitat.Alien);
			so.ApplyModifiedPropertiesWithoutUndo();
			geysers.SetAuthoredFingerprint(BiomeTemplate.Authored.Habitat, "Ice, Alien");
			Assert.That(AmbientGating.HabitatOfTemplate(geysers), Is.EqualTo(AmbientHabitat.Ice | AmbientHabitat.Alien));

			// The air moves the creatures: given home air, the same template is ice alone.
			geysers.Atmosphere = BiomeAtmosphereRequirement.Any;
			geysers.Requires = BiomeWorldRequirement.None;
			Assert.That(AmbientGating.HabitatOfTemplate(geysers), Is.EqualTo(AmbientHabitat.Ice));

			// Alien ground alone: stored None, marked, read as Alien.
			BiomeTemplate dust = MakeBiome("Dust Sea");
			dust.Atmosphere = BiomeAtmosphereRequirement.Airless | BiomeAtmosphereRequirement.Thin;
			dust.Habitat = BiomeHabitat.None;
			dust.SetAuthoredFingerprint(BiomeTemplate.Authored.Habitat, "None");
			Assert.That(AmbientGating.HabitatOfTemplate(dust), Is.EqualTo(AmbientHabitat.Alien));
		}
	}
}
