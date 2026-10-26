using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.WorldDesign;

namespace FishMMO.UnitTests
{
	public partial class BiomeProceduralArtTests
	{
		/// <summary>
		/// I2i (2026-10-18): one authoring run marks a template's floating plants (none) and boardwalks (None) as the tool's, a second
		/// changes nothing, and a hand-made list or setting is kept. Unity-only (it makes a BiomeTemplate).
		/// </summary>
		[Test]
		public void Authoring_WritesNoFloatingPlantsAndNoBoardwalks_OnceAndKeepsHandEdits()
		{
			BiomeTemplate swamp = MakeBiome("Swamp");
			BiomeArtSpec.Entry entry = BiomeArtSpec.For("Swamp");
			var source = new FakeSource(created);
			Assert.That(swamp.FloatingPlants, Is.Empty);
			Assert.That(swamp.Boardwalks, Is.EqualTo(BiomeBoardwalks.None));

			var first = new BiomeArtAuthoring.Report();
			BiomeArtAuthoring.Author(swamp, entry, source, first, true);
			Assert.That(swamp.IsAuthored(BiomeTemplate.Authored.FloatingPlants), Is.True);
			Assert.That(swamp.IsAuthored(BiomeTemplate.Authored.Boardwalks), Is.True);
			Assert.That(BiomeTemplateArt.FloatingPlants(swamp), Is.Empty);
			Assert.That(BiomeTemplateArt.Boardwalks(swamp), Is.EqualTo(BiomeBoardwalks.None));

			var second = new BiomeArtAuthoring.Report();
			Assert.IsFalse(BiomeArtAuthoring.Author(swamp, entry, source, second, true), "a second run changed something:\n" + second);

			swamp.FloatingPlants.Add(new BiomeFloatingPlant(BiomeFloatingPlantKind.WaterLily, 2f));
			swamp.Boardwalks = BiomeBoardwalks.Roads | BiomeBoardwalks.Streets;
			var third = new BiomeArtAuthoring.Report();
			BiomeArtAuthoring.Author(swamp, entry, source, third, true);
			Assert.That(swamp.FloatingPlants, Is.EqualTo(new List<BiomeFloatingPlant> { new BiomeFloatingPlant(BiomeFloatingPlantKind.WaterLily, 2f) }),
				"a hand-made plant was removed:\n" + third);
			Assert.That(swamp.Boardwalks, Is.EqualTo(BiomeBoardwalks.Roads | BiomeBoardwalks.Streets), "a hand-set boardwalk setting was overwritten:\n" + third);
		}

		/// <summary>I2i: the floating plants' details are sound detail meshes that lie on the water (Unity-only: it builds meshes).</summary>
		[Test]
		public void FloatingPlants_LieOnTheWater()
		{
			foreach (DetailSpec spec in ProceduralArtCatalogue.Details)
			{
				DetailPlant plant = spec.Plant;
				if (!FloatingPlantMeshes.IsFloating(plant.Kind))
				{
					continue;
				}
				MeshBuilder mesh = VegetationMeshes.Build(in plant, Seed);
				Assert.That(mesh.Validate(false), Is.Empty, spec.Name);
				Assert.That(mesh.TriangleCount, Is.InRange(1, 400), spec.Name);
				Bounds b = mesh.Bounds;
				// I2i-b: a kind's Reach says how far under the water line it may go (none for the first four) and how far over it.
				Vector2 reach = FloatingPlantMeshes.Reach(in plant);
				Assert.That(b.min.y, Is.GreaterThanOrEqualTo(-reach.x - 1e-4f), $"{spec.Name}: nothing further under the water line than its kind reaches");
				Assert.That(b.min.y, Is.LessThan(0.03f), $"{spec.Name}: lying on the water, not over it");
				Assert.That(b.max.y, Is.LessThan(reach.y), $"{spec.Name} stands no higher than its kind: {b.max.y}");
				foreach (Vector2 w in mesh.Wind)
				{
					Assert.That(w, Is.EqualTo(Vector2.zero), $"{spec.Name}: held still");
				}
			}
		}
	}
}
