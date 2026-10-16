using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using FishMMO.Shared.Biomes;
using FishMMO.Shared.NameGeneration;
using FishMMO.Shared.WorldDesign;
using NUnit.Framework;

namespace FishMMO.UnitTests.WorldDesign
{
	/// <summary>
	/// Phase I5b of the biome suite (2026-10-16): template LISTS carry an authoring mark per entry (BiomeEntryMarks), so a list
	/// edited by hand still takes the entries a later spec adds while every entry edited, made or deleted by hand stays as
	/// left; a list holding the old whole-list mark converts; hand-made point-of-interest templates that name a biome reach its
	/// site variants; and a biome's moat water and street lights moved onto the template with exactly what the code decided.
	/// </summary>
	public class BiomeTemplateEntryMarksTests
	{
		private const string WorldFolder = "Assets/Scripts/Shared/Implementation/Tools/Extensions/Unity/Editor/World";
		private const string BiomeFolder = "Assets/Templates/Entity/Biomes";

		private static string Id(BiomeSiteVariant v) => BiomeArtAuthoring.SiteVariantId(v);
		private static string Print(BiomeSiteVariant v) => v.ToString();
		private static BiomeSiteVariant V(POIType kind, string variant, float weight) => new BiomeSiteVariant(kind, variant, weight);
		private static List<string> Prints(List<BiomeSiteVariant> list) => list.ConvertAll(Print);

		private static readonly BiomeSiteVariant StoneVillage = V(POIType.Village, "Stone", 2f);
		private static readonly BiomeSiteVariant TimberVillage = V(POIType.Village, "Timber", 0.1f);
		private static readonly BiomeSiteVariant HideCamp = V(POIType.Camp, "Hide Tents", 3f);

		// ── The per-entry algebra ─────────────────────────────────────

		/// <summary>
		/// From empty, the spec's list in its order, every entry marked with what was written; again, nothing; the tool's untouched
		/// entries follow the spec (updated, or pruned when the spec drops them); a new spec entry goes in after its predecessor.
		/// </summary>
		[Test]
		public void Merge_AddsUpdatesAndPrunesTheToolsOwnEntries()
		{
			var list = new List<BiomeSiteVariant>();
			var marks = new Dictionary<string, string>();
			var spec = new List<BiomeSiteVariant> { StoneVillage, TimberVillage, HideCamp };
			BiomeEntryMarks.Outcome first = BiomeEntryMarks.Merge(list, spec, Id, Print, marks);
			Assert.That(Prints(list), Is.EqualTo(Prints(spec)), "authored from empty: the spec's list, in its order");
			Assert.That(first.Ids(BiomeEntryMarks.Step.Added).Count, Is.EqualTo(3));
			Assert.That(marks[Id(StoneVillage)], Is.EqualTo(Print(StoneVillage)));
			Assert.That(marks.Count, Is.EqualTo(3));

			BiomeEntryMarks.Outcome again = BiomeEntryMarks.Merge(list, spec, Id, Print, marks);
			Assert.That(again.Changes, Is.Empty, "a second run changes nothing");
			Assert.That(again.Wrote || again.Kept, Is.False);

			// The spec moves the stone village's weight, drops the timber village and adds a fortress variant after the stone village.
			BiomeSiteVariant stone3 = V(POIType.Village, "Stone", 3f), fort = V(POIType.Village, "Fortified", 1.5f);
			var next = new List<BiomeSiteVariant> { stone3, fort, HideCamp };
			BiomeEntryMarks.Outcome moved = BiomeEntryMarks.Merge(list, next, Id, Print, marks);
			Assert.That(Prints(list), Is.EqualTo(Prints(next)));
			Assert.That(moved.Ids(BiomeEntryMarks.Step.Updated), Is.EqualTo(new[] { Id(stone3) }));
			Assert.That(moved.Ids(BiomeEntryMarks.Step.Pruned), Is.EqualTo(new[] { Id(TimberVillage) }));
			Assert.That(moved.Ids(BiomeEntryMarks.Step.Added), Is.EqualTo(new[] { Id(fort) }));
			Assert.That(marks.ContainsKey(Id(TimberVillage)), Is.False, "a pruned entry's mark goes with it (a gap again)");
			Assert.That(marks[Id(stone3)], Is.EqualTo(Print(stone3)));
		}

		/// <summary>
		/// An entry edited by hand and one made by hand are kept whatever the spec says; the rest of the list still follows the
		/// spec, and a new spec entry still arrives: one hand edit no longer freezes the list (the I5 whole-list mark did).
		/// </summary>
		[Test]
		public void Merge_KeepsHandEditsAndHandMadeEntries_AndStillTakesNewOnes()
		{
			var list = new List<BiomeSiteVariant>();
			var marks = new Dictionary<string, string>();
			BiomeEntryMarks.Merge(list, new List<BiomeSiteVariant> { StoneVillage, TimberVillage }, Id, Print, marks);
			list[0] = V(POIType.Village, "Stone", 9f);
			BiomeSiteVariant mine = V(POIType.Village, "Elven Treehouses", 4f);
			list.Add(mine);

			BiomeSiteVariant timber2 = V(POIType.Village, "Timber", 0.2f), yurts = V(POIType.Camp, "Yurts", 2f);
			var spec = new List<BiomeSiteVariant> { V(POIType.Village, "Stone", 2.5f), timber2, yurts };
			BiomeEntryMarks.Outcome outcome = BiomeEntryMarks.Merge(list, spec, Id, Print, marks);
			Assert.That(Prints(list), Is.EqualTo(new[] { "Village/Stone×9", "Village/Timber×0.2", "Camp/Yurts×2", Print(mine) }));
			Assert.That(outcome.Ids(BiomeEntryMarks.Step.KeptEdited), Is.EqualTo(new[] { Id(StoneVillage) }));
			Assert.That(outcome.Ids(BiomeEntryMarks.Step.Updated), Is.EqualTo(new[] { Id(timber2) }));
			Assert.That(outcome.Ids(BiomeEntryMarks.Step.Added), Is.EqualTo(new[] { Id(yurts) }));
			Assert.That(outcome.Kept && outcome.Wrote, Is.True);
			Assert.That(marks.ContainsKey(Id(mine)), Is.False, "a hand-made entry is never marked");

			// A hand-made entry the spec does not have is never pruned; the hand-edited one is never brought back.
			BiomeEntryMarks.Outcome later = BiomeEntryMarks.Merge(list, new List<BiomeSiteVariant> { yurts }, Id, Print, marks);
			Assert.That(Prints(list), Is.EqualTo(new[] { "Village/Stone×9", "Camp/Yurts×2", Print(mine) }), "the tool's timber entry pruned, the rest kept");
			Assert.That(later.Ids(BiomeEntryMarks.Step.Pruned), Is.EqualTo(new[] { Id(timber2) }));
		}

		/// <summary>
		/// An entry the tool wrote and somebody deleted is remembered (a tombstone) and never added again, run after run; put back
		/// by hand it is the developer's, or the tool's again when it equals the spec's.
		/// </summary>
		[Test]
		public void Merge_RemembersADeletion()
		{
			var list = new List<BiomeSiteVariant>();
			var marks = new Dictionary<string, string>();
			var spec = new List<BiomeSiteVariant> { StoneVillage, TimberVillage, HideCamp };
			BiomeEntryMarks.Merge(list, spec, Id, Print, marks);
			list.RemoveAt(1);

			BiomeEntryMarks.Outcome deleted = BiomeEntryMarks.Merge(list, spec, Id, Print, marks);
			Assert.That(Prints(list), Is.EqualTo(new[] { Print(StoneVillage), Print(HideCamp) }), "a deleted entry was added again");
			Assert.That(marks[Id(TimberVillage)], Is.EqualTo(BiomeEntryMarks.Removed));
			Assert.That(deleted.Ids(BiomeEntryMarks.Step.Tombstoned), Is.EqualTo(new[] { Id(TimberVillage) }));
			Assert.That(deleted.Ids(BiomeEntryMarks.Step.KeptDeleted), Is.EqualTo(new[] { Id(TimberVillage) }));
			Assert.That(deleted.Wrote, Is.True, "the tombstone is written");

			BiomeEntryMarks.Outcome again = BiomeEntryMarks.Merge(list, spec, Id, Print, marks);
			Assert.That(again.Wrote, Is.False);
			Assert.That(again.Ids(BiomeEntryMarks.Step.KeptDeleted), Is.EqualTo(new[] { Id(TimberVillage) }), "reported as kept every run");
			Assert.That(list.Count, Is.EqualTo(2));

			// A hand edit deleted later is a deletion too.
			list[0] = V(POIType.Village, "Stone", 7f);
			BiomeEntryMarks.Merge(list, spec, Id, Print, marks);
			list.RemoveAt(0);
			BiomeEntryMarks.Merge(list, spec, Id, Print, marks);
			Assert.That(Prints(list), Is.EqualTo(new[] { Print(HideCamp) }));
			Assert.That(marks[Id(StoneVillage)], Is.EqualTo(BiomeEntryMarks.Removed));

			// Put back by hand: someone else's weight is kept; the spec's own weight makes it the tool's again.
			list.Add(V(POIType.Village, "Timber", 5f));
			BiomeEntryMarks.Outcome back = BiomeEntryMarks.Merge(list, spec, Id, Print, marks);
			Assert.That(back.Ids(BiomeEntryMarks.Step.KeptEdited), Is.EqualTo(new[] { Id(TimberVillage) }));
			Assert.That(list.Exists(v => v.Weight == 5f), Is.True);
			list.Add(StoneVillage);
			BiomeEntryMarks.Merge(list, spec, Id, Print, marks);
			Assert.That(marks[Id(StoneVillage)], Is.EqualTo(Print(StoneVillage)), "equal to the spec's: the tool's again");
		}

		/// <summary>Only the first entry with an identity is the entry; a second one is somebody's and is never touched. Identities ignore a variant name's case.</summary>
		[Test]
		public void Merge_LeavesADuplicateAlone_AndMatchesVariantNamesIgnoringCase()
		{
			var list = new List<BiomeSiteVariant> { V(POIType.Village, "stone ", 2f), V(POIType.Village, "Stone", 8f) };
			var marks = new Dictionary<string, string>();
			BiomeEntryMarks.Outcome outcome = BiomeEntryMarks.Merge(list, new List<BiomeSiteVariant> { V(POIType.Village, "Stone", 2f) }, Id, Print, marks);
			Assert.That(Id(list[0]), Is.EqualTo(Id(StoneVillage)));
			Assert.That(list.Count, Is.EqualTo(2), "nothing added: the first entry is the stone village");
			Assert.That(list[1].Weight, Is.EqualTo(8f));
			Assert.That(outcome.Ids(BiomeEntryMarks.Step.KeptEdited), Is.EqualTo(new[] { Id(StoneVillage) }), "\"stone \" prints differently: a hand edit");
			Assert.That(outcome.Changes.Count, Is.EqualTo(1));
		}

		/// <summary>A prefab-style list (identity = print) works the same, and an entry with no identity (a missing prefab) is never touched.</summary>
		[Test]
		public void Merge_NameLists_AndEntriesWithNoIdentity()
		{
			var list = new List<string> { null, "Detail_FlowersMeadow" };
			var marks = new Dictionary<string, string>();
			BiomeEntryMarks.Outcome outcome = BiomeEntryMarks.Merge(list, new List<string> { "Detail_FlowersAlpine", "Detail_FlowersMeadow" },
				BiomeArtAuthoring.NameId, n => n, marks);
			Assert.That(list, Is.EqualTo(new[] { null, "Detail_FlowersAlpine", "Detail_FlowersMeadow" }), "added before the meadow flowers, its successor");
			Assert.That(outcome.Ids(BiomeEntryMarks.Step.Marked), Is.EqualTo(new[] { "detail_flowersmeadow" }));
		}

		// ── Migration from one mark for the whole list ───────────────

		private static string Whole(IReadOnlyList<BiomeSiteVariant> list) => BiomeArtAuthoring.Mark(new List<BiomeSiteVariant>(list));

		/// <summary>A list still as the tool wrote it: every entry the tool's, so the next merge brings the whole list to today's spec.</summary>
		[Test]
		public void Migrate_AnUntouchedList_MarksEveryEntry()
		{
			var written = new List<BiomeSiteVariant> { StoneVillage, TimberVillage };
			var spec = new List<BiomeSiteVariant> { V(POIType.Village, "Stone", 3f), HideCamp };
			Dictionary<string, string> marks = BiomeEntryMarks.Migrate(written, spec, Id, Print, Whole(written), Whole);
			Assert.That(marks, Is.EquivalentTo(new Dictionary<string, string> { { Id(StoneVillage), Print(StoneVillage) }, { Id(TimberVillage), Print(TimberVillage) } }));
			var list = new List<BiomeSiteVariant>(written);
			BiomeEntryMarks.Merge(list, spec, Id, Print, marks);
			Assert.That(Prints(list), Is.EqualTo(Prints(spec)), "untouched: the list becomes today's spec");

			// An untouched EMPTY list (the spec was empty when it was written) takes today's entries.
			Dictionary<string, string> none = BiomeEntryMarks.Migrate(new List<BiomeSiteVariant>(), spec, Id, Print, Whole(new List<BiomeSiteVariant>()), Whole);
			Assert.That(none, Is.Empty);
		}

		/// <summary>
		/// A list edited by hand whose mark is the hash of today's spec list (the spec has not changed since): what was written is
		/// known, so the edited entry reads as a hand edit, the deleted one as deleted, a hand-made one as hand-made, and the list
		/// stays exactly as left.
		/// </summary>
		[Test]
		public void Migrate_AnEditedList_WhenTheSpecIsUnchanged_IsExact()
		{
			var spec = new List<BiomeSiteVariant> { StoneVillage, TimberVillage, HideCamp };
			BiomeSiteVariant mine = V(POIType.Village, "Elven Treehouses", 4f);
			var list = new List<BiomeSiteVariant> { V(POIType.Village, "Stone", 9f), HideCamp, mine };
			Dictionary<string, string> marks = BiomeEntryMarks.Migrate(list, spec, Id, Print, Whole(spec), Whole);
			Assert.That(marks[Id(StoneVillage)], Is.EqualTo(Print(StoneVillage)), "marked with what was written: the edit shows as a mismatch");
			Assert.That(marks[Id(TimberVillage)], Is.EqualTo(BiomeEntryMarks.Removed), "deleted by hand");
			Assert.That(marks[Id(HideCamp)], Is.EqualTo(Print(HideCamp)));
			Assert.That(marks.ContainsKey(Id(mine)), Is.False);

			var before = Prints(list);
			BiomeEntryMarks.Outcome outcome = BiomeEntryMarks.Merge(list, spec, Id, Print, marks);
			Assert.That(Prints(list), Is.EqualTo(before), "the edited list stays as left");
			Assert.That(outcome.Ids(BiomeEntryMarks.Step.KeptEdited), Is.EqualTo(new[] { Id(StoneVillage) }));
			Assert.That(outcome.Ids(BiomeEntryMarks.Step.KeptDeleted), Is.EqualTo(new[] { Id(TimberVillage) }));

			// A later edit of the stone village back to the spec's weight makes it the tool's again; later spec entries arrive.
			BiomeSiteVariant yurts = V(POIType.Camp, "Yurts", 2f);
			spec.Add(yurts);
			BiomeEntryMarks.Merge(list, spec, Id, Print, marks);
			Assert.That(list.Exists(v => Id(v) == Id(yurts)), Is.True, "a new spec entry reaches the hand-edited list");
		}

		/// <summary>
		/// A list edited by hand when the spec has changed since (the old mark matches nothing): only entries equal to today's spec
		/// are the tool's, the rest hand-made, and a spec entry the list lacks is taken as deleted by hand (the list stays as the
		/// developer left it, as the whole-list mark kept it).
		/// </summary>
		[Test]
		public void Migrate_AnEditedList_WhenTheSpecHasChanged_MarksOnlyWhatEqualsTheSpec()
		{
			var spec = new List<BiomeSiteVariant> { V(POIType.Village, "Stone", 3f), TimberVillage, HideCamp };
			var list = new List<BiomeSiteVariant> { V(POIType.Village, "Stone", 9f), TimberVillage };
			Dictionary<string, string> marks = BiomeEntryMarks.Migrate(list, spec, Id, Print, "an old hash nobody can read", Whole);
			Assert.That(marks.ContainsKey(Id(StoneVillage)), Is.False, "not equal to the spec's: hand-made");
			Assert.That(marks[Id(TimberVillage)], Is.EqualTo(Print(TimberVillage)));
			Assert.That(marks[Id(HideCamp)], Is.EqualTo(BiomeEntryMarks.Removed));
			var before = Prints(list);
			BiomeEntryMarks.Merge(list, spec, Id, Print, marks);
			Assert.That(Prints(list), Is.EqualTo(before));
		}

		/// <summary>
		/// The vines' old whole-value mark spells out what was written (densities, then each species and weight): read back exactly,
		/// and each species' print is the segment its signature has.
		/// </summary>
		[Test]
		public void VineMarks_ReadBackFromTheOldSignature()
		{
			var vines = new BiomeVines { Caves = 0.6f, Ruins = 0.35f, Cliffs = 0.2f, Trees = 0.125f };
			vines.Species.Add(new BiomeVineSpecies("Ivy", 3f));
			vines.Species.Add(new BiomeVineSpecies("Creeper", 1.3333f));
			List<(string Id, string Print)> written = BiomeArtAuthoring.VineSpeciesWritten(vines.Signature(), out string densities);
			Assert.That(densities, Is.EqualTo(BiomeArtAuthoring.VineDensities(vines)));
			Assert.That(written.ConvertAll(w => w.Id), Is.EqualTo(new[] { "ivy", "creeper" }));
			Assert.That(written.ConvertAll(w => w.Print), Is.EqualTo(vines.Species.ConvertAll(BiomeArtAuthoring.VineSpeciesPrint)));
			Assert.That(BiomeArtAuthoring.VineSpeciesWritten(new BiomeVines().Signature(), out string none), Is.Empty);
			Assert.That(none, Is.EqualTo("0,0,0,0"));

			// Converted: the hand-deleted creeper is remembered.
			var now = new List<BiomeVineSpecies> { new BiomeVineSpecies("Ivy", 3f) };
			Dictionary<string, string> marks = BiomeEntryMarks.Migrate(now, vines.Species, s => BiomeArtAuthoring.NameId(s.Species), BiomeArtAuthoring.VineSpeciesPrint,
				vines.Signature(), _ => "edited", written);
			Assert.That(marks["ivy"], Is.EqualTo(BiomeArtAuthoring.VineSpeciesPrint(now[0])));
			Assert.That(marks["creeper"], Is.EqualTo(BiomeEntryMarks.Removed));
		}

		// ── Hand-made point-of-interest templates ─────────────────────

		/// <summary>
		/// A template the catalogue does not write, whose own biome list names a biome, reaches that biome's site variants at the
		/// weight its list gives (not stranded at its weight elsewhere); one of a variant the catalogue writes is skipped; the
		/// first of two assets of one variant counts; a biome's own Prefers still wins.
		/// </summary>
		[Test]
		public void HandMadeTemplates_NamingABiome_JoinItsSiteVariants()
		{
			var treehouses = new PointOfInterestTemplateSpec { Kind = POIType.Village, Variant = "Elven Treehouses" }.Elsewhere(0.2f).In(3f, "Forest", "Jungle");
			var again = new PointOfInterestTemplateSpec { Kind = POIType.Village, Variant = "elven treehouses" }.Elsewhere(0.2f).In(9f, "Forest");
			PointOfInterestTemplateSpec catalogue = PointOfInterestTemplateSpecs.All().First(s => s.BiomeWeights.Count > 0);
			var shadow = new PointOfInterestTemplateSpec { Kind = catalogue.Kind, Variant = catalogue.Variant }.Elsewhere(0f).In(42f, "Forest", "Plains");
			var handMade = new List<PointOfInterestTemplateSpec> { treehouses, again, shadow };

			List<BiomeSiteVariant> forest = BiomeArtSpec.For("Forest").SiteVariants(handMade);
			List<BiomeSiteVariant> forestCatalogue = BiomeArtSpec.For("Forest").SiteVariants();
			Assert.That(BiomeSiteVariant.TryWeight(forest, POIType.Village, "Elven Treehouses", out float w) ? w : -1f, Is.EqualTo(3f));
			Assert.That(forest.Count(v => v.Kind == POIType.Village && v.Variant.Equals("Elven Treehouses", StringComparison.OrdinalIgnoreCase)), Is.EqualTo(1));
			Assert.That(Prints(forest.Where(v => !(v.Kind == POIType.Village && v.Variant == "Elven Treehouses")).ToList()), Is.EqualTo(Prints(forestCatalogue)),
				"the catalogue's own weights stand: the shadowing asset is skipped");

			List<BiomeSiteVariant> plains = BiomeArtSpec.For("Plains").SiteVariants(handMade);
			Assert.That(plains.Any(v => v.Variant == "Elven Treehouses"), Is.False, "a biome the list does not name: its weight elsewhere, so not listed");
			Assert.That(Prints(plains), Is.EqualTo(Prints(BiomeArtSpec.For("Plains").SiteVariants())));

			// The pick is what the asset's own list gave by the biome's name (the old reading).
			foreach (string biome in new[] { "Forest", "Jungle", "Plains", "Desert" })
			{
				List<BiomeSiteVariant> list = BiomeArtSpec.For(biome).SiteVariants(handMade);
				float weight = BiomeTemplateArt.SiteVariantWeight(list, null, POIType.Village, "Elven Treehouses", null, treehouses.DefaultBiomeWeight);
				Assert.That(weight, Is.EqualTo(treehouses.BiomeWeightByName(biome)), biome);
			}

			// A hand-edited list still takes the adopted variant (per-entry marks).
			var marks = new Dictionary<string, string>();
			var edited = new List<BiomeSiteVariant>();
			BiomeEntryMarks.Merge(edited, forestCatalogue, Id, Print, marks);
			if (edited.Count > 0)
			{
				edited[0] = V(edited[0].Kind, edited[0].Variant, edited[0].Weight + 1f);
			}
			BiomeEntryMarks.Merge(edited, forest, Id, Print, marks);
			Assert.That(BiomeSiteVariant.TryWeight(edited, POIType.Village, "Elven Treehouses", out float adopted) ? adopted : -1f, Is.EqualTo(3f));
		}

		// ── Moat water ────────────────────────────────────────────────

		/// <summary>
		/// Every biome's spec moat water is what the moat shaper decided from its template's climate envelope (a literal copy of
		/// the old code, over the asset's own climate): dry, frozen or wet, for all 83.
		/// </summary>
		[Test]
		public void SpecMoatWater_IsWhatTheClimateGaveTheMoat_ForEveryBiome()
		{
			int checkedBiomes = 0, dry = 0, frozen = 0;
			foreach (string path in Directory.GetFiles(BiomeFolder, "*.asset"))
			{
				string text = File.ReadAllText(path);
				if (!Regex.IsMatch(text, "^  MinTemperature:", RegexOptions.Multiline))
				{
					continue;
				}
				string name = Field(text, "m_Name");
				BiomeArtSpec.Entry entry = BiomeArtSpec.For(name);
				Assert.That(entry, Is.Not.Null, name);
				float minH = Number(text, "MinHumidity"), maxH = Number(text, "MaxHumidity"), minT = Number(text, "MinTemperature"), maxT = Number(text, "MaxTemperature");
				BiomeMoatWater old = OldMoat(minH, maxH, minT, maxT);
				Assert.That(entry.MoatWater, Is.EqualTo(old), name);
				Assert.That(BiomeMoatWaters.FromClimate(minH, maxH, minT, maxT), Is.EqualTo(old), name + " (the fallback rule)");
				checkedBiomes++;
				dry += old == BiomeMoatWater.Dry ? 1 : 0;
				frozen += old == BiomeMoatWater.Frozen ? 1 : 0;
			}
			Assert.That(checkedBiomes, Is.EqualTo(83));
			Assert.That(dry, Is.GreaterThan(5), "the arid biomes dig dry ditches");
			Assert.That(frozen, Is.GreaterThan(5), "the cold biomes freeze theirs");
			Assert.That(BiomeArtSpec.For("Desert").MoatWater, Is.EqualTo(BiomeMoatWater.Dry));
			Assert.That(BiomeArtSpec.For("Snow").MoatWater, Is.EqualTo(BiomeMoatWater.Frozen));
			Assert.That(BiomeArtSpec.For("Forest").MoatWater, Is.EqualTo(BiomeMoatWater.Wet));
		}

		// The moat shaper's code before 2026-10-16 (MoatShaper.AddWater), literally.
		private static BiomeMoatWater OldMoat(float minHumidity, float maxHumidity, float minTemperature, float maxTemperature)
		{
			if (0.5f * (minHumidity + maxHumidity) < -0.3f)
			{
				return BiomeMoatWater.Dry;
			}
			bool frozen = 0.5f * (minTemperature + maxTemperature) < -0.5f;
			return frozen ? BiomeMoatWater.Frozen : BiomeMoatWater.Wet;
		}

		/// <summary>Every biome's spec file names its moat water, and the enum is stored by value (append-only).</summary>
		[Test]
		public void SpecFiles_NameTheirMoatWater()
		{
			string folder = Path.Combine(WorldFolder, "ProceduralArt/BiomeArtSpec");
			string[] files = Directory.GetFiles(folder, "BiomeArtSpec.*.cs");
			Assert.That(files.Length, Is.EqualTo(83));
			foreach (string file in files)
			{
				Assert.That(Regex.Matches(File.ReadAllText(file), @"\.Moat\(BiomeMoatWater\.\w+\)").Count, Is.EqualTo(1), Path.GetFileName(file));
			}
			Assert.That((int)BiomeMoatWater.Wet, Is.EqualTo(0));
			Assert.That((int)BiomeMoatWater.Dry, Is.EqualTo(1));
			Assert.That((int)BiomeMoatWater.Frozen, Is.EqualTo(2));
		}

		// ── Way lights ────────────────────────────────────────────────

		/// <summary>
		/// Every biome's spec street lights light every kind exactly as the props did by kind alone (a literal copy of the old
		/// code): iron lanterns every 13 m in towns, cities, capitals and ports, torches every 20 m in villages, nothing elsewhere.
		/// </summary>
		[Test]
		public void SpecWayLights_LightEveryKindAsThePropsDid_ForEveryBiome()
		{
			var kinds = (POIType[])Enum.GetValues(typeof(POIType));
			int biomes = 0;
			foreach (BiomeArtSpec.Entry entry in BiomeArtSpec.Entries)
			{
				List<BiomeWayLight> lights = entry.WayLights();
				Assert.That(lights.Select(l => l.Kind).Distinct().Count(), Is.EqualTo(lights.Count), entry.Biome);
				foreach (POIType kind in kinds)
				{
					bool lanterns = kind == POIType.Town || kind == POIType.City || kind == POIType.Capital || kind == POIType.Port;
					bool torches = kind == POIType.Village;
					bool lit = BiomeWayLights.TryFor(lights, kind, out BiomeWayLight light);
					Assert.That(lit, Is.EqualTo(lanterns || torches), $"{entry.Biome}: {kind}");
					if (lit)
					{
						Assert.That(light.PieceQuery, Is.EqualTo(lanterns ? "=LanternPost" : "=TorchPost"), $"{entry.Biome}: {kind}");
						Assert.That(light.Spacing, Is.EqualTo(lanterns ? 13f : 20f), $"{entry.Biome}: {kind}");
					}
				}
				biomes++;
			}
			Assert.That(biomes, Is.EqualTo(83));
		}

		/// <summary>Lights by the first entry for a kind; a spacing under the least is read as the least; the enum is stored by value.</summary>
		[Test]
		public void WayLights_ReadTheFirstEntry_AndAreStoredByValue()
		{
			var lights = new List<BiomeWayLight>
			{
				new BiomeWayLight(POIType.Village, BiomeWayLightKind.Brazier, 1f),
				new BiomeWayLight(POIType.Village, BiomeWayLightKind.TorchPost, 20f),
			};
			Assert.That(BiomeWayLights.TryFor(lights, POIType.Village, out BiomeWayLight light), Is.True);
			Assert.That(light.Light, Is.EqualTo(BiomeWayLightKind.Brazier));
			Assert.That(light.Spacing, Is.EqualTo(BiomeWayLights.MinSpacing));
			Assert.That(BiomeWayLights.TryFor(lights, POIType.Town, out _), Is.False);
			Assert.That(BiomeWayLights.TryFor(null, POIType.Town, out _), Is.False);
			Assert.That(light.PieceQuery, Is.EqualTo("=Brazier"));
			Assert.That((int)BiomeWayLightKind.LanternPost, Is.EqualTo(0));
			Assert.That((int)BiomeWayLightKind.TorchPost, Is.EqualTo(1));
			Assert.That((int)BiomeWayLightKind.Brazier, Is.EqualTo(2));
			Assert.That(new BiomeWayLight(POIType.Town, BiomeWayLightKind.LanternPost, 13f).ToString(), Is.EqualTo("Town=LanternPost@13"));
		}

		/// <summary>Every light the template can name is a piece the structure kit builds.</summary>
		[Test]
		public void WayLightKinds_AreKitPieces()
		{
			foreach (BiomeWayLightKind light in (BiomeWayLightKind[])Enum.GetValues(typeof(BiomeWayLightKind)))
			{
				Assert.That(StructureKitPieceSource.Resolves(BiomeWayLights.PieceQuery(light)), Is.True, light.ToString());
			}
		}

		// ── Consumers ─────────────────────────────────────────────────

		/// <summary>
		/// The moat shaper reads the template's moat water (no climate threshold of its own) and the props read the template's
		/// street lights (no lantern/torch rule by kind of their own); the authoring writes every list the per-entry way.
		/// </summary>
		[Test]
		public void Consumers_ReadMoatAndLightsFromTheTemplate_AndListsAreMarkedPerEntry()
		{
			string moat = CodeOnly(File.ReadAllText(Path.Combine(WorldFolder, "PointsOfInterest/Shapers/MoatShaper.cs")));
			Assert.That(moat, Does.Contain("BiomeTemplateArt.MoatWater("));
			Assert.That(moat, Does.Not.Contain("MinHumidity"));
			Assert.That(moat, Does.Not.Contain("MinTemperature"));

			string props = CodeOnly(File.ReadAllText(Path.Combine(WorldFolder, "PointsOfInterest/PropsFeature.cs")));
			Assert.That(props, Does.Contain("BiomeTemplateArt.WayLight(siteBiome, record.Kind"));
			Assert.That(props, Does.Not.Contain("\"=TorchPost\""), "the torch post comes from the template");
			Assert.That(Regex.IsMatch(props, @"lanterns\s*\?\s*13f"), Is.False);

			string authoring = CodeOnly(File.ReadAllText(Path.Combine(WorldFolder, "ProceduralArt/BiomeArtAuthoring.cs")));
			foreach (string field in new[] { "AcceptedRocks", "CaveShells", "SeaLife", "GatheringResources", "SiteKinds", "SiteVariants", "SiteStyles", "FieldCrops", "StructurePalette", "WayLights" })
			{
				Assert.That(Regex.IsMatch(authoring, @"AuthorEntries\([^;]*BiomeTemplate\.Authored\." + field + @"\b"), Is.True, field + " is authored entry by entry");
				Assert.That(Regex.IsMatch(authoring, @"AuthorValue\([^;]*BiomeTemplate\.Authored\." + field + @"\b"), Is.False, field + " still has one mark for the whole list");
			}
			Assert.That(Regex.IsMatch(authoring, @"AuthorEntries\([^;]*BiomeTemplate\.Authored\.Vines\b"), Is.True);
			Assert.That(Regex.IsMatch(authoring, @"AuthorValue\([^;]*BiomeTemplate\.Authored\.MoatWater\b"), Is.True, "a single value keeps one mark");
		}

		// ── Helpers ───────────────────────────────────────────────────

		private static string Field(string yaml, string key)
		{
			Match m = Regex.Match(yaml, "^  " + key + ": ?(.*)$", RegexOptions.Multiline);
			return m.Success ? m.Groups[1].Value.Trim().Trim('\r') : null;
		}

		private static float Number(string yaml, string key) => float.Parse(Field(yaml, key), NumberStyles.Float, CultureInfo.InvariantCulture);

		private static string CodeOnly(string source)
		{
			var code = new System.Text.StringBuilder(source.Length);
			foreach (string line in source.Replace("\r\n", "\n").Split('\n'))
			{
				string trimmed = line.TrimStart();
				if (trimmed.StartsWith("//", StringComparison.Ordinal) || trimmed.StartsWith("/*", StringComparison.Ordinal) || trimmed.StartsWith("*", StringComparison.Ordinal))
				{
					continue;
				}
				code.Append(line).Append('\n');
			}
			return code.ToString();
		}
	}
}
