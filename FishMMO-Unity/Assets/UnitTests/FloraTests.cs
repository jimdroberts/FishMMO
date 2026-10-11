using System;
using System.Collections.Generic;
using FishMMO.Client;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;
using UnityEngine;

namespace FishMMO.UnitTests
{
	/// <summary>
	/// The land flora of the vegetation expansion (2026-10-10): its detail plants (FloraMeshes, ProceduralArtCatalogue.Flora),
	/// and the deadwood props (DeadwoodMeshes, BiomeArtGenerator.Deadwood). Every item the design names is in the catalogue
	/// under that name; the blade types carry the colours the blade renderer reads from them; the newer kinds are land
	/// kinds with their materials' traits; and the deadwood is valid, bedded, lighter by level and listed.
	/// </summary>
	/// <remarks>
	/// The budgets, validity and determinism of every detail (these included) are BiomeProceduralArtTests'
	/// DetailPlants_AreValidDeterministicSingleSubmesh_AndSmall; the shrubs' are BushTests'.
	/// </remarks>
	[TestFixture]
	public class FloraTests
	{
		private const int Seed = 1234;

		/// <summary>The design's detail names (§2c–§2e, §2g), plus the later forms built with them (horsetail, echium, pitcher plant).</summary>
		private static readonly string[] DesignDetails =
		{
			"GrassSedge", "GrassCotton", "GrassDune", "GrassFeather", "GrassSavanna", "ReedsPlume", "GrassRush", "GrassWheat",
			"FlowersAlpine", "FlowersWet", "FlowersWoodland", "FlowersTall", "FlowersDesert",
			"FernBracken", "FernHartstongue", "BroadHerb", "HerbRuderal", "ShrubSamphire", "ShrubTumbleweed",
			"PadCactus", "Cholla", "OrganPipe", "Euphorbia", "RosetteAgave", "RosetteYucca", "RosetteBromeliad",
			"CushionMossCampion", "CushionThrift", "CushionSpiny", "CushionSphagnum", "CushionMoss",
			"LichenClump", "LichenLava", "Ivy", "BeachVine", "Pneumatophores", "CypressKnees",
			"DebrisNeedle", "DebrisPalm", "DebrisWrack", "DebrisBranch", "DebrisBamboo",
			"MushroomForest", "MushroomCluster", "MushroomSwamp", "MushroomCave",
			"Horsetail", "EchiumSpire", "PitcherPlant",
		};

		/// <summary>The design's deadwood props (§2g's tree-channel items).</summary>
		private static readonly string[] DesignDeadwood =
		{
			"FallenLog_Oak", "FallenLog_Conifer", "FallenLog_Birch", "Stump_Broken", "Stump_Cut", "Stump_Bracket", "Driftwood", "PetrifiedLog", "RootPlate",
		};

		/// <summary>Detail names the blade grass takes (their meshes are its WebGL2 fallback and colour source).</summary>
		private static readonly string[] BladeDetails =
		{
			"GrassSedge", "GrassCotton", "GrassDune", "GrassFeather", "GrassSavanna", "ReedsPlume", "GrassRush", "GrassWheat",
			"FlowersAlpine", "FlowersWet", "FlowersWoodland", "FlowersTall", "FlowersDesert",
		};

		/// <summary>Of those, the ones whose meshes carry a head colour (seed head, plume, tuft, ear or flower).</summary>
		private static readonly string[] HeadedDetails =
		{
			"GrassCotton", "GrassFeather", "ReedsPlume", "GrassRush", "GrassWheat",
			"FlowersAlpine", "FlowersWet", "FlowersWoodland", "FlowersTall", "FlowersDesert",
		};

		[Test]
		public void EveryDesignItem_IsInTheCatalogue_UnderItsName()
		{
			foreach (string name in DesignDetails)
			{
				Assert.That(ProceduralArtCatalogue.TryDetail(name, out _), $"detail '{name}' is missing");
			}
			foreach (string name in DesignDeadwood)
			{
				Assert.That(ProceduralArtCatalogue.TryDeadwood(name, out _), $"deadwood '{name}' is missing");
			}
			var names = new HashSet<string>();
			foreach (string prefab in ProceduralArtCatalogue.AllPrefabNames())
			{
				Assert.That(names.Add(prefab), $"prefab name '{prefab}' is used twice");
			}
		}

		/// <summary>
		/// The blade renderer takes a prototype by its name's prefix: the new grasses, sedges, rushes, reeds and flower sets
		/// must be named into it, and nothing else of the flora's may be (a cushion drawn as blades would be a lawn).
		/// </summary>
		[Test]
		public void BladeTypes_AreNamedIntoTheBladeRenderer_AndNothingElseIs()
		{
			string[] prefixes = new GrassBladeSettings().PrototypePrefixes;
			bool IsBlade(string prefab) => Array.Exists(prefixes, p => prefab.StartsWith(p, StringComparison.Ordinal));
			foreach (string name in DesignDetails)
			{
				string prefab = ProceduralArtCatalogue.DetailPrefab(name);
				bool blade = Array.IndexOf(BladeDetails, name) >= 0;
				Assert.That(IsBlade(prefab), Is.EqualTo(blade), blade ? $"{prefab} must be drawn as blades" : $"{prefab} must not be drawn as blades");
			}
		}

		/// <summary>
		/// What the blade renderer reads from a blade type's mesh: stem colours (vertices at alpha 0.5 and over) at its root
		/// and tip, and for a headed type at most four head colours (vertices under 0.5). A head colour lost to a stem's
		/// alpha, or a fifth colour, would be dropped there.
		/// </summary>
		[Test]
		public void BladeTypes_CarryStemColours_AndTheirHeadColours()
		{
			foreach (string name in BladeDetails)
			{
				Assert.That(ProceduralArtCatalogue.TryDetail(name, out DetailSpec spec), name);
				DetailPlant plant = spec.Plant;
				MeshBuilder mesh = VegetationMeshes.Build(in plant, Seed);
				int stems = 0;
				var heads = new HashSet<int>();
				foreach (Color32 c in mesh.Colors)
				{
					if (c.a >= 128)
					{
						stems++;
					}
					else
					{
						heads.Add(c.r << 16 | c.g << 8 | c.b);
					}
				}
				Assert.That(stems, Is.GreaterThan(0), $"{name}: no stem colours");
				bool headed = Array.IndexOf(HeadedDetails, name) >= 0;
				if (headed)
				{
					Assert.That(heads.Count, Is.GreaterThan(0), $"{name}: no head colours");
				}
				else
				{
					Assert.That(heads, Is.Empty, $"{name}: a plain grass has no heads");
				}
				// Flower sets take exactly their accents (≤ 4); a grass's head colours vary in brightness, so its count is
				// not checked, only that it has accents of its own.
				if (plant.Kind == DetailKind.Flowers)
				{
					Assert.That(plant.Accents, Is.Not.Null.And.Length.InRange(1, 4), $"{name}: a flower set has one to four head colours");
					Assert.That(heads.Count, Is.LessThanOrEqualTo(4), $"{name}: {heads.Count} head colours");
				}
			}
		}

		/// <summary>
		/// The flora's kinds are land plants (never the sea's surge or keep-under clamp), and the woody or succulent ones
		/// wear a bark that exists; a kind's sink is within what the shader's sink can hide.
		/// </summary>
		[Test]
		public void FloraKinds_AreLandKinds_WithTheirMaterialsTraits()
		{
			var barks = new HashSet<string>();
			foreach (SurfaceRecipe r in SurfaceCatalogue.BarkRecipes)
			{
				barks.Add(r.Name);
			}
			for (DetailKind kind = DetailKind.BroadHerb; kind <= DetailKind.DebrisBamboo; kind++)
			{
				Assert.That(SeaFloorMeshes.IsAquatic(kind), Is.False, $"{kind} is a land kind");
				DetailKindTraits traits = ProceduralArtCatalogue.Traits(kind);
				Assert.That(traits.Sink, Is.InRange(0.005f, 0.1f), $"{kind} sink");
				if (traits.BarkFamily != null)
				{
					Assert.That(barks, Does.Contain(traits.BarkFamily), $"{kind} wears a bark that is not made");
				}
			}
			Assert.That(ProceduralArtCatalogue.Traits(DetailKind.PadCactus).BarkFamily, Is.EqualTo(Bark.Cactus));
			Assert.That(ProceduralArtCatalogue.Traits(DetailKind.Cholla).BarkFamily, Is.EqualTo(Bark.Cactus));
			Assert.That(ProceduralArtCatalogue.Traits(DetailKind.ColumnCluster).BarkFamily, Is.EqualTo(Bark.Cactus));
			Assert.That(ProceduralArtCatalogue.Traits(DetailKind.RootKnobs).BarkFamily, Is.Not.Null, "root knobs are woody");
		}

		/// <summary>
		/// Each newer detail carries its own group and sink (the spec reads them for names it has no legacy value for), and
		/// every legacy kind's variant is one of the flora's own (variant 0 is the legacy builder, kept for the legacy details).
		/// </summary>
		[Test]
		public void FloraDetails_CarryTheirOwnGroupsAndSinks()
		{
			foreach (string name in DesignDetails)
			{
				Assert.That(ProceduralArtCatalogue.TryDetail(name, out DetailSpec spec), name);
				Assert.That(spec.GroupMetres, Is.GreaterThan(0f), $"{name} group metres");
				Assert.That(spec.GroupSize, Is.GreaterThan(0f), $"{name} group size");
				Assert.That(spec.Sink.y, Is.InRange(0.01f, 0.1f), $"{name} sink");
				Assert.That(spec.Sink.x, Is.InRange(0f, spec.Sink.y), $"{name} sink range");
			}
		}

		/// <summary>The flora's details stand on the ground: rooted at or a little under it, rising above it.</summary>
		[Test]
		public void FloraDetails_StandOnTheGround()
		{
			foreach (string name in DesignDetails)
			{
				Assert.That(ProceduralArtCatalogue.TryDetail(name, out DetailSpec spec), name);
				DetailPlant plant = spec.Plant;
				Bounds b = VegetationMeshes.Build(in plant, Seed).Bounds;
				Assert.That(b.min.y, Is.InRange(-0.15f, 0.02f), $"{name} is rooted at {b.min.y:F3}");
				Assert.That(b.max.y, Is.GreaterThan(0.02f), $"{name} rises to {b.max.y:F3}");
			}
		}

		// ── Deadwood ──────────────────────────────────────────────────

		[Test]
		public void Deadwood_LevelsAreValid_TwoSubmeshes_Lighter_AndWithinBudget()
		{
			foreach (DeadwoodSpecies species in ProceduralArtCatalogue.Deadwood)
			{
				DeadwoodSpecies sp = species;
				int previous = int.MaxValue;
				for (int lod = 0; lod < DeadwoodMeshes.Levels; lod++)
				{
					MeshBuilder mesh = DeadwoodMeshes.Build(in sp, lod, Seed);
					List<string> problems = mesh.Validate(false);
					Assert.That(problems, Is.Empty, $"{sp.Name} LOD{lod}: {string.Join("; ", problems)}");
					Assert.That(mesh.Submeshes.Count, Is.EqualTo(2), $"{sp.Name}: bark and wood");
					Assert.That(mesh.Submeshes[DeadwoodMeshes.BarkSubmesh].Count, Is.GreaterThan(0), $"{sp.Name} LOD{lod} has no bark");
					Assert.That(mesh.Submeshes[DeadwoodMeshes.WoodSubmesh].Count, Is.GreaterThan(0), $"{sp.Name} LOD{lod} has no exposed wood (its second material would draw nothing)");
					Assert.That(mesh.TriangleCount, Is.InRange(1, DeadwoodMeshes.Budget[lod]), $"{sp.Name} LOD{lod}");
					Assert.That(mesh.TriangleCount, Is.LessThan(previous), $"{sp.Name} LOD{lod} is no lighter than the level before");
					previous = mesh.TriangleCount;
					MeshBuilder again = DeadwoodMeshes.Build(in sp, lod, Seed);
					Assert.That(again.VertexCount, Is.EqualTo(mesh.VertexCount), $"{sp.Name} LOD{lod} is not deterministic");
					for (int i = 0; i < mesh.VertexCount; i++)
					{
						Assert.That(again.Positions[i], Is.EqualTo(mesh.Positions[i]), $"{sp.Name} LOD{lod} is not deterministic");
					}
				}
			}
		}

		/// <summary>Deadwood lies bedded in the ground (part of it below the pivot, most above) at its species' size.</summary>
		[Test]
		public void Deadwood_IsBedded_AtItsSize()
		{
			foreach (DeadwoodSpecies species in ProceduralArtCatalogue.Deadwood)
			{
				DeadwoodSpecies sp = species;
				Bounds b = DeadwoodMeshes.Build(in sp, 0, Seed).Bounds;
				Assert.That(b.min.y, Is.LessThan(0f), $"{sp.Name} floats");
				Assert.That(b.max.y, Is.GreaterThan(-b.min.y), $"{sp.Name} is mostly buried");
				switch (sp.Form)
				{
					case DeadwoodForm.FallenLog:
					case DeadwoodForm.Driftwood:
					case DeadwoodForm.PetrifiedLog:
						Assert.That(b.size.x, Is.InRange(sp.Length * 0.9f, sp.Length * 1.25f), $"{sp.Name} length");
						Assert.That(b.max.y, Is.LessThan(sp.Radius * 3f), $"{sp.Name} lies on the ground");
						break;
					case DeadwoodForm.Stump:
						Assert.That(b.max.y, Is.InRange(sp.Length * 0.9f, sp.Length * 1.8f), $"{sp.Name} height");
						break;
					case DeadwoodForm.RootPlate:
						Assert.That(b.max.y, Is.InRange(sp.Length * 0.6f, sp.Length * 1.3f), $"{sp.Name} stands on edge");
						break;
				}
			}
		}

		/// <summary>
		/// Every deadwood file is listed where the payload, wrappers and spec checks look (or the art reads as stale, or
		/// never as stale), and every bark it wears is made.
		/// </summary>
		[Test]
		public void Deadwood_IsListed_AndWearsBarksThatAreMade()
		{
			var payload = new HashSet<string>(ProceduralArtCatalogue.PayloadPaths());
			var wrappers = new HashSet<string>(ProceduralArtCatalogue.WrapperPaths());
			var prefabs = new HashSet<string>(ProceduralArtCatalogue.AllPrefabNames());
			var barks = new HashSet<string>();
			foreach (SurfaceRecipe r in SurfaceCatalogue.BarkRecipes)
			{
				barks.Add(r.Name);
			}
			foreach (DeadwoodSpecies species in ProceduralArtCatalogue.Deadwood)
			{
				DeadwoodSpecies sp = species;
				Assert.That(barks, Does.Contain(sp.BarkFamily), $"{sp.Name}: bark {sp.BarkFamily} is not made");
				Assert.That(prefabs, Does.Contain(ProceduralArtCatalogue.DeadwoodPrefab(sp.Name)), sp.Name);
				Assert.That(wrappers, Does.Contain(ProceduralArtCatalogue.PrefabPath(ProceduralArtCatalogue.DeadwoodPrefab(sp.Name))), sp.Name);
				Assert.That(wrappers, Does.Contain(ProceduralArtCatalogue.MaterialPath(ProceduralArtCatalogue.DeadwoodBarkMaterial(in sp))), sp.Name);
				for (int lod = 0; lod < DeadwoodMeshes.Levels; lod++)
				{
					Assert.That(payload, Does.Contain(ProceduralArtCatalogue.MeshPath(ProceduralArtCatalogue.DeadwoodMesh(sp.Name, lod))), $"{sp.Name} LOD{lod}");
				}
			}
			Assert.That(wrappers, Does.Contain(ProceduralArtCatalogue.MaterialPath(ProceduralArtCatalogue.DeadwoodWoodMaterial)));
		}
	}
}
