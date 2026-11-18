#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The sea floor added by the vegetation expansion (2026-10-10): its detail plants and animals (built by
	/// <see cref="SeaFloorMeshes"/>), the traits of the detail kinds it owns, and its small-rock materials.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>One package's file.</b> The sea package adds its details, kinds' traits and rock materials here and nowhere
	/// shared: <see cref="Details"/> appends <see cref="SeaDetails"/> after the legacy and flora lists, the generator
	/// reads a kind's shadows, sink, sway and tint patches through <see cref="Traits"/> (whether a kind is aquatic,
	/// and how it moves with the surge, stay in <see cref="SeaFloorMeshes.IsAquatic"/> and <see cref="SeaFloorMeshes.Sway"/>),
	/// and <see cref="AllRockMaterials"/> takes <see cref="SeaRockMaterials"/>.
	/// </para>
	/// <para>
	/// <b>No static fields read during initialisation</b>, as in ProceduralArtCatalogue.Flora.cs: <see cref="SeaDetails"/>
	/// is a method and <see cref="SeaKindTraits"/> a pure switch. <see cref="SeaRockMaterials"/> may stay a field: it
	/// holds only constants, and is read (through <see cref="AllRockMaterials"/>) after the type is initialised.
	/// </para>
	/// </remarks>
	public static partial class ProceduralArtCatalogue
	{
		/// <summary>
		/// The sea floor's newer details: mussel, oyster and vent-clam beds, barnacled stones, sea lettuce, sand dollars,
		/// sea cucumbers, brittle stars, sea pens, crinoids, glass sponges (on rock and stalked in mud), cold-water and soft
		/// corals, giant clams, xenophyophores and bacterial mats, and the mats' land cousin, a cave's slime mould. No
		/// seagrass (removed 2026-10-05: it read as land grass).
		/// </summary>
		/// <remarks>
		/// <para>
		/// Each entry is a <c>D(…)</c> line as in CoreDetails. Sea life passes <c>healthy: SeaTint, dry: SeaTint</c> and
		/// <c>snowBury: 0f</c>, and carries its own <c>groupMetres</c>, <c>groupSize</c> and <c>sink</c>, which the spec's
		/// helpers read because these names are not in their legacy tables: beds and carpets (mussels, sand dollars,
		/// brittle stars, mats) gather in many-celled patches, big solitary things (clams, cucumbers) in twos and threes.
		/// </para>
		/// <para>
		/// <b>Sizes are the animals'.</b> Height, Width and Radius mean what each builder in <see cref="SeaFloorMeshes"/>
		/// says: a mussel bed's Height is one shell's length, a sea cucumber's Width its body radius and Radius half its
		/// length, a sand dollar's Width its disc radius. Sinks are bedded to the thing: a clam half buried, a sand
		/// dollar or a brittle star (under 4 cm thick) a fifth to a third of its own thickness.
		/// </para>
		/// <para>
		/// <b>Variants.</b> A bed of oysters and a seep's white clams are the mussel bed's kind (<see cref="DetailKind.MusselBed"/>,
		/// variants 1 and 2) under names of their own, as are the stalked glass sponges of the mud (GlassSpongeStalked, variant 1).
		/// </para>
		/// </remarks>
		private static DetailSpec[] SeaDetails()
		{
			return new DetailSpec[]
			{
				// Beds: blue mussels on rock and pebbles in the shallows; oysters in estuaries and on mangrove roots;
				// Calyptogena clams round a vent or a seep.
				D("MusselBed", DetailKind.MusselBed, 15, 0.065f, 0f, 0.14f, "#1c2030", "#2a2638",
					accents: new[] { H("#1e2230"), H("#262a3c"), H("#2c2434"), H("#3a3226") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 4f, groupSize: 14f, sink: new Vector2(0.005f, 0.012f)),
				D("MusselBedOyster", DetailKind.MusselBed, 10, 0.1f, 0f, 0.18f, "#6e685c", "#857c6a",
					accents: new[] { H("#6e685c"), H("#857c6a"), H("#5e5652"), H("#948a76") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					variant: 1, groupMetres: 4f, groupSize: 12f, sink: new Vector2(0.006f, 0.015f)),
				D("MusselBedVentClam", DetailKind.MusselBed, 7, 0.2f, 0f, 0.3f, "#e6e2d6", "#d4cdb8",
					accents: new[] { H("#ece8dc"), H("#ddd6c4"), H("#d0c8b0") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					variant: 2, groupMetres: 3f, groupSize: 10f, sink: new Vector2(0.02f, 0.05f)),
				D("Barnacles", DetailKind.Barnacles, 30, 0.12f, 0.011f, 0.17f, "#4e4c46", "#64605a",
					accents: new[] { H("#e6e2d4"), H("#d6d0be"), H("#ece6d8") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 3f, groupSize: 6f, sink: new Vector2(0.02f, 0.04f)),
				D("SeaLettuce", DetailKind.SeaLettuce, 7, 0.22f, 0.2f, 0.12f, "#3e8c2a", "#6ab83a", snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 3f, groupSize: 8f, sink: new Vector2(0.01f, 0.02f)),
				// Lying animals.
				D("SandDollar", DetailKind.SandDollar, 4, 0.009f, 0.04f, 0.25f, "#4a3a4a", "#5e4c52",
					accents: new[] { H("#4a3a48"), H("#5a4850"), H("#6a5a58"), H("#e2ddcc") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 4f, groupSize: 16f, sink: new Vector2(0.001f, 0.003f)),
				D("SeaCucumber", DetailKind.SeaCucumber, 2, 0.07f, 0.045f, 0.16f, "#3a2a20", "#5a4030",
					accents: new[] { H("#3a2a1e"), H("#262220"), H("#7a5a2c"), H("#8a4a42"), H("#a07a8a") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 6f, groupSize: 3f, sink: new Vector2(0.01f, 0.02f)),
				D("BrittleStar", DetailKind.BrittleStar, 4, 0.008f, 0.014f, 0.22f, "#8a7a6a", "#6a5a5a",
					accents: new[] { H("#8a7a6a"), H("#b0a090"), H("#7a5a6a"), H("#c0a8a0") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 3f, groupSize: 20f, sink: new Vector2(0.0005f, 0.002f)),
				// Filter feeders of the deep: in the mud and on the rock.
				D("SeaPen", DetailKind.SeaPen, 4, 0.4f, 0.007f, 0.18f, "#c0503a", "#d07a8a",
					accents: new[] { H("#c0503a"), H("#d07a8a"), H("#e2d4b8"), H("#a03a4a") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 5f, groupSize: 8f, sink: new Vector2(0.03f, 0.06f)),
				D("Crinoid", DetailKind.Crinoid, 2, 0.7f, 0.006f, 0.2f, "#c8bca8", "#b0a490",
					accents: new[] { H("#d8c070"), H("#d07a30"), H("#e8e0d0"), H("#7a4a8a") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 4f, groupSize: 5f, sink: new Vector2(0.02f, 0.04f)),
				D("GlassSponge", DetailKind.GlassSponge, 2, 0.45f, 0.05f, 0.2f, "#e6e0c8", "#d6cfb6",
					accents: new[] { H("#ebe6d2"), H("#ddd6be"), H("#e4dcc8") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 6f, groupSize: 4f, sink: new Vector2(0.03f, 0.06f)),
				D("GlassSpongeStalked", DetailKind.GlassSponge, 3, 0.35f, 0.035f, 0.2f, "#e4dcc6", "#d8e0e0",
					accents: new[] { H("#e8e2cc"), H("#dcd4bc"), H("#e2dcd0") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					variant: 1, groupMetres: 6f, groupSize: 5f, sink: new Vector2(0.04f, 0.08f)),
				// Corals beyond the reef builders: Lophelia's cold-water thickets, and the reef's soft corals.
				D("ColdCoral", DetailKind.ColdCoral, 4, 0.7f, 0.008f, 0.3f, "#e8e4dc", "#e8c8c0",
					accents: new[] { H("#ece8e0"), H("#f0d8d0"), H("#e8b890") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 10f, groupSize: 8f, sink: new Vector2(0.04f, 0.08f)),
				D("SoftCoral", DetailKind.SoftCoral, 2, 0.25f, 0.013f, 0.3f, "#a89468", "#c08a90",
					accents: new[] { H("#c8a878"), H("#d890a8"), H("#a8b070"), H("#d8c0d8"), H("#e08a60") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 8f, groupSize: 6f, sink: new Vector2(0.03f, 0.06f)),
				D("GiantClam", DetailKind.GiantClam, 1, 0.3f, 0f, 0.35f, "#d8d0c0", "#b8b0a0",
					accents: new[] { H("#2a6ac0"), H("#2aa0a8"), H("#6a3a9a"), H("#7a8a3a") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 8f, groupSize: 2f, sink: new Vector2(0.04f, 0.08f)),
				D("Xenophyophore", DetailKind.Xenophyophore, 3, 0.15f, 0f, 0.25f, "#8a8270", "#a09886",
					accents: new[] { H("#8e8674"), H("#a49c8a"), H("#7e7666") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 6f, groupSize: 4f, sink: new Vector2(0.01f, 0.02f)),
				// Microbial mats: a seep's and a vent's under the sea; a cave's slime mould on land (not aquatic).
				D("BacterialMat", DetailKind.BacterialMat, 4, 0.012f, 0f, 0.6f, "#e8e4d8", "#e0c060",
					accents: new[] { H("#f2f0e8"), H("#ece4c8"), H("#e8c870"), H("#e0a050") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 5f, groupSize: 10f, sink: new Vector2(0.001f, 0.003f)),
				D("SlimeMat", DetailKind.SlimeMat, 3, 0.012f, 0f, 0.5f, "#d07a20", "#e0a030",
					accents: new[] { H("#e8b030"), H("#d88a20"), H("#c86a1a") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 4f, groupSize: 6f, sink: new Vector2(0.001f, 0.003f)),

				// ── Coastal Water (biome round, 2026-10-26): the Celtic Atlantic shelf. Each is an existing kind in the draft's colours
				// and sizes under a name of its own (decision 12). LATER (I9b P30/P31): Rainbow wrack's iridescent sheen, the
				// Sabellaria hummock as a kind of its own (a brain-coral mound stands in), the kelps' and anemones' own forms.
				// The kelp forest: stiff-stemmed cuvie with red-weed sleeves, the warty furbelows, cold sugar kelp, warm golden kelp.
				D("KelpCuvie", DetailKind.Kelp, 3, 2.4f, 0.12f, 0.35f, "#5a4a1a", "#8a7228", lean: 0.15f, segments: 8,
					accents: new[] { H("#7a2430") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint, groupMetres: 12f, groupSize: 20f, sink: new Vector2(0.05f, 0.1f)),
				D("KelpFurbelows", DetailKind.Kelp, 1, 3f, 0.3f, 0.4f, "#6a5a2a", "#7a6a2a", lean: 0.35f, segments: 10,
					snowBury: 0f, healthy: SeaTint, dry: SeaTint, groupMetres: 8f, groupSize: 2f, sink: new Vector2(0.05f, 0.1f)),
				D("KelpSugar", DetailKind.Kelp, 4, 2.5f, 0.18f, 0.3f, "#8a7a30", "#b09a40", lean: 0.5f, segments: 10,
					snowBury: 0f, healthy: SeaTint, dry: SeaTint, groupMetres: 6f, groupSize: 12f, sink: new Vector2(0.04f, 0.08f)),
				D("KelpGolden", DetailKind.Kelp, 3, 2.2f, 0.14f, 0.35f, "#c8a830", "#8a6a1a", lean: 0.2f, segments: 8,
					snowBury: 0f, healthy: SeaTint, dry: SeaTint, groupMetres: 12f, groupSize: 18f, sink: new Vector2(0.05f, 0.1f)),
				// The shallow fringe's thongweed straps and the warm band's rainbow wrack.
				D("Thongweed", DetailKind.Seaweed, 8, 2f, 0.02f, 0.2f, "#6a6a2a", "#6a5a1e", lean: 0.6f, segments: 8,
					snowBury: 0f, healthy: SeaTint, dry: SeaTint, groupMetres: 4f, groupSize: 10f, sink: new Vector2(0.01f, 0.03f)),
				D("RainbowWrack", DetailKind.Seaweed, 6, 0.4f, 0.05f, 0.15f, "#3a8a7a", "#7ad0c0", lean: 0.3f, segments: 6,
					snowBury: 0f, healthy: SeaTint, dry: SeaTint, groupMetres: 3f, groupSize: 8f, sink: new Vector2(0.01f, 0.03f)),
				// Red weeds of the understorey, Irish moss and dulse.
				D("RedWeedTurf", DetailKind.SeaLettuce, 9, 0.15f, 0.15f, 0.12f, "#a02838", "#d04a5a", snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 3f, groupSize: 10f, sink: new Vector2(0.01f, 0.02f)),
				D("Carrageen", DetailKind.SeaLettuce, 8, 0.1f, 0.1f, 0.1f, "#6a2a4a", "#7a7ab8", snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 3f, groupSize: 8f, sink: new Vector2(0.01f, 0.02f)),
				D("Dulse", DetailKind.SeaLettuce, 6, 0.3f, 0.2f, 0.12f, "#8a3a3a", "#b0585a", snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 3f, groupSize: 8f, sink: new Vector2(0.01f, 0.02f)),
				// The walls and cobbles: pink sea fans set across the current, Ross coral, dead man's fingers.
				D("PinkSeaFan", DetailKind.SeaFan, 1, 0.4f, 0.01f, 0.35f, "#e89aa8", "#f0e0e0",
					accents: new[] { H("#e89aa8"), H("#f0d8dc"), H("#f4f0ec") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 6f, groupSize: 4f, sink: new Vector2(0.03f, 0.06f)),
				D("RossCoral", DetailKind.Coral, 4, 0.35f, 0.05f, 0.25f, "#e09070", "#f0b090",
					accents: new[] { H("#e09070"), H("#f0b090"), H("#d88060") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 6f, groupSize: 3f, sink: new Vector2(0.03f, 0.06f)),
				D("DeadMansFingers", DetailKind.SoftCoral, 2, 0.15f, 0.015f, 0.15f, "#f0ece0", "#e8904a",
					accents: new[] { H("#f0ece0"), H("#e8904a"), H("#f4d8b8") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 4f, groupSize: 4f, sink: new Vector2(0.02f, 0.04f)),
				// Anemones: jewel-anemone carpets in five colours, plumose columns on wrecks, the squat dahlia on the gravel.
				D("JewelAnemone", DetailKind.Anemone, 1, 0.03f, 0.004f, 0.25f, "#e070a0", "#60c080",
					accents: new[] { H("#e070a0"), H("#60c080"), H("#e08040"), H("#9060c0"), H("#f0ece8") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 2f, groupSize: 12f, sink: new Vector2(0.005f, 0.01f)),
				D("PlumoseAnemone", DetailKind.Anemone, 1, 0.3f, 0.02f, 0.12f, "#f0ece8", "#e89050",
					accents: new[] { H("#f4f0ec"), H("#e89050") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 3f, groupSize: 6f, sink: new Vector2(0.01f, 0.025f)),
				D("DahliaAnemone", DetailKind.Anemone, 1, 0.08f, 0.008f, 0.12f, "#a02a3a", "#c0b0a0",
					accents: new[] { H("#a02a3a"), H("#c0b0a0") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 4f, groupSize: 3f, sink: new Vector2(0.01f, 0.025f)),
				// Grazers and stars: the edible urchin, the barrens' purple urchins, sun stars and cushion stars.
				D("EdibleUrchin", DetailKind.Urchin, 1, 0.14f, 0.006f, 0.12f, "#b05a7a", "#e8d0d8", snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 4f, groupSize: 4f, sink: new Vector2(0.01f, 0.025f)),
				D("PurpleUrchin", DetailKind.Urchin, 2, 0.06f, 0.004f, 0.2f, "#4a2a5a", "#6a3a7a", snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 3f, groupSize: 14f, sink: new Vector2(0.01f, 0.02f)),
				D("SunStar", DetailKind.Starfish, 1, 0.02f, 0f, 0.12f, "#d84a2a", "#f0d0b0", snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 6f, groupSize: 2f, sink: new Vector2(0f, 0.006f)),
				D("CushionStar", DetailKind.Starfish, 2, 0.015f, 0f, 0.08f, "#d8a040", "#c08030", snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 4f, groupSize: 3f, sink: new Vector2(0f, 0.005f)),
				D("BrittleStarBed", DetailKind.BrittleStar, 5, 0.01f, 0.012f, 0.3f, "#8a5a6a", "#c0a0a8",
					accents: new[] { H("#8a5a6a"), H("#c0a0a8"), H("#6a4a5a") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 10f, groupSize: 30f, sink: new Vector2(0.0005f, 0.002f)),
				// Beds: scallop grounds (the pilgrim's shell), flat-oyster reefs, horse-mussel clumps; barnacles on the dolerite.
				D("MusselBedScallop", DetailKind.MusselBed, 6, 0.12f, 0f, 0.25f, "#8a4a2a", "#c86a4a",
					accents: new[] { H("#8a4a2a"), H("#c86a4a"), H("#a85a3a"), H("#e8d8c0") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					variant: 1, groupMetres: 8f, groupSize: 10f, sink: new Vector2(0.01f, 0.03f)),
				D("MusselBedFlatOyster", DetailKind.MusselBed, 8, 0.1f, 0f, 0.2f, "#8a8078", "#b0a098",
					accents: new[] { H("#8a8078"), H("#b0a098"), H("#9a8a90") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					variant: 1, groupMetres: 5f, groupSize: 12f, sink: new Vector2(0.006f, 0.015f)),
				D("MusselBedHorse", DetailKind.MusselBed, 8, 0.12f, 0f, 0.25f, "#3a2a1a", "#5a4028",
					accents: new[] { H("#3a2a1a"), H("#5a4028"), H("#d8b030") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.008f, 0.02f)),
				D("BarnaclesCeltic", DetailKind.Barnacles, 30, 0.12f, 0.011f, 0.17f, "#2a3430", "#4a5650",
					accents: new[] { H("#e6e2d4"), H("#d6d0be"), H("#c8c4b8") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 3f, groupSize: 6f, sink: new Vector2(0.02f, 0.04f)),
				D("SabellariaHummock", DetailKind.BrainCoral, 1, 0.5f, 0f, 0.7f, "#8a765a", "#a89878",
					accents: new[] { H("#8a765a"), H("#a89878"), H("#6e5e48") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 6f, groupSize: 4f, sink: new Vector2(0.05f, 0.1f)),
				// Litter: sea-potato tests (no sand dollars in these seas), Atlantic shells, storm-torn kelp heaped in the gullies.
				D("SeaPotato", DetailKind.SandDollar, 3, 0.03f, 0.05f, 0.25f, "#ece8e0", "#d8d0c4", snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 4f, groupSize: 6f, sink: new Vector2(0.003f, 0.008f)),
				D("ShellsAtlantic", DetailKind.Shells, 4, 0.03f, 0f, 0.35f, "#c8b090", "#c08a8a",
					accents: new[] { H("#d8ccb0"), H("#8a4a2a"), H("#e8dcc0"), H("#c08a8a") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 3f, groupSize: 8f, sink: new Vector2(0f, 0.008f)),
				D("KelpDrift", DetailKind.Seaweed, 5, 1.2f, 0.08f, 0.5f, "#4a3e14", "#6a5a1e", lean: 0.95f, segments: 6,
					snowBury: 0f, healthy: SeaTint, dry: SeaTint, groupMetres: 4f, groupSize: 10f, sink: new Vector2(0f, 0.02f)),

				// ── Ocean (biome round, 2026-10-26): the shelf edge and the continental slope. LATER (I9b P31): the Nephrops burrow as
				// a crater set (a xenophyophore mound in the mud's colours stands in).
				// Lophelia thickets (white and salmon live tips over a grey dead framework) and Madrepora's brittle zig-zag fans.
				D("Lophelia", DetailKind.ColdCoral, 5, 1f, 0.009f, 0.4f, "#f0ece4", "#e89070",
					accents: new[] { H("#f0ece4"), H("#e89070"), H("#8a8478") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 10f, groupSize: 8f, sink: new Vector2(0.04f, 0.08f)),
				D("Madrepora", DetailKind.ColdCoral, 3, 0.4f, 0.006f, 0.25f, "#f0dcd4", "#e8c8c0",
					accents: new[] { H("#f0dcd4"), H("#e8c8c0") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 6f, groupSize: 4f, sink: new Vector2(0.03f, 0.06f)),
				D("BubblegumCoral", DetailKind.SeaFan, 1, 1.8f, 0.03f, 0.9f, "#d04050", "#f0e8e0",
					accents: new[] { H("#d04050"), H("#f0e8e0"), H("#e06070") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 12f, groupSize: 2f, sink: new Vector2(0.05f, 0.1f)),
				D("FeatherStar", DetailKind.Crinoid, 3, 0.2f, 0.005f, 0.12f, "#c84a2a", "#e07a4a",
					accents: new[] { H("#c84a2a"), H("#e07a4a") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 8f, groupSize: 20f, sink: new Vector2(0.005f, 0.015f)),
				// The phosphorescent sea pen: crimson feathers that glow faintly green in the dark slope (decision 36 (d)).
				Glowing(D("PhosphorPen", DetailKind.SeaPen, 3, 0.3f, 0.008f, 0.18f, "#8a1a24", "#f0ece4",
					accents: new[] { H("#8a1a24"), H("#f0ece4"), H("#a02a34") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 8f, groupSize: 8f, sink: new Vector2(0.03f, 0.06f)), new Color(0.06f, 0.3f, 0.14f)),
				D("Geodia", DetailKind.Sponge, 1, 0.7f, 0.3f, 0.4f, "#d8d4c8", "#6a6a62", snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 8f, groupSize: 4f, sink: new Vector2(0.05f, 0.1f)),
				D("EarSponge", DetailKind.Sponge, 2, 0.3f, 0.12f, 0.2f, "#d8a030", "#c08020", snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 6f, groupSize: 3f, sink: new Vector2(0.03f, 0.06f)),
				D("PencilUrchin", DetailKind.Urchin, 1, 0.1f, 0.008f, 0.1f, "#b8a890", "#d89090", snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 4f, groupSize: 3f, sink: new Vector2(0.01f, 0.02f)),
				D("PlatedCushionStar", DetailKind.Starfish, 1, 0.03f, 0f, 0.1f, "#c0402a", "#e07050", snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 6f, groupSize: 2f, sink: new Vector2(0f, 0.008f)),
				D("Bolocera", DetailKind.Anemone, 1, 0.25f, 0.02f, 0.15f, "#b03a2a", "#e8907a", snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 4f, groupSize: 3f, sink: new Vector2(0.01f, 0.025f)),
				D("NephropsBurrow", DetailKind.Xenophyophore, 3, 0.12f, 0f, 0.5f, "#6e6252", "#24201a",
					accents: new[] { H("#6e6252"), H("#5e5446"), H("#24201a") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 8f, groupSize: 6f, sink: new Vector2(0.02f, 0.05f)),
				// ── Coral Reef (biome round, 2026-10-26): the Maldivian atoll shelf. Each is an existing kind in the draft's colours and
				// sizes under a name of its own (decision 12). LATER (I9b P30): the kinds' own forms (Pocillopora cauliflowers, Porites
				// microatoll tops with worm dots, Turbinaria scrolls, Fungia septa, Antipathes bushes, Dendronephthya trees, Sarcophyton
				// caps, Heteractis skirts, Diadema needles, Tridacna mantles, the garden eels' arched bodies, the ghost-shrimp craters).
				// Acropora antler thickets (tan, pale blue-cream tips) and the bleached white state of the hot band.
				D("StaghornCoral", DetailKind.Coral, 6, 0.9f, 0.05f, 0.45f, "#a8885a", "#c8d4e0",
					accents: new[] { H("#a8885a"), H("#b89868"), H("#c8d4e0") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 10f, groupSize: 14f, sink: new Vector2(0.02f, 0.05f)),
				D("StaghornBleached", DetailKind.Coral, 6, 0.9f, 0.05f, 0.45f, "#f4f2ea", "#e8e4d8",
					accents: new[] { H("#f4f2ea"), H("#e8e4d8"), H("#8a8a5a") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 10f, groupSize: 14f, sink: new Vector2(0.02f, 0.05f)),
				D("FingerCoral", DetailKind.Coral, 6, 0.28f, 0.04f, 0.2f, "#d8a0a0", "#8aa070",
					accents: new[] { H("#d8a0a0"), H("#8aa070"), H("#e8d0b0") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 6f, groupSize: 8f, sink: new Vector2(0.02f, 0.04f)),
				// Platygyra meander domes, the masons' Porites boulders, stalked table Acropora and the slope's plate corals.
				D("BrainCoralReef", DetailKind.BrainCoral, 1, 0.6f, 0f, 0.6f, "#7a8a4a", "#d8ccaa",
					accents: new[] { H("#7a8a4a"), H("#d8ccaa"), H("#9a7a5a") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 8f, groupSize: 3f, sink: new Vector2(0.04f, 0.08f)),
				D("PoritesBoulder", DetailKind.BrainCoral, 1, 1.6f, 0f, 1.2f, "#b8a070", "#8c7a4a",
					accents: new[] { H("#b8a070"), H("#a89060"), H("#c03a3a"), H("#3a6ac0") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 14f, groupSize: 1f, sink: new Vector2(0.05f, 0.1f)),
				D("TableAcropora", DetailKind.TableCoral, 1, 0.9f, 0f, 1f, "#8aa8a0", "#a080c0",
					accents: new[] { H("#8aa8a0"), H("#9ab0b0"), H("#a080c0") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 10f, groupSize: 2f, sink: new Vector2(0.05f, 0.1f)),
				D("PlateCoral", DetailKind.TableCoral, 1, 0.5f, 0f, 0.5f, "#b8a040", "#8a7a30",
					accents: new[] { H("#b8a040"), H("#a8a050"), H("#8a9a50") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 8f, groupSize: 3f, sink: new Vector2(0.03f, 0.06f)),
				// Free-living mushroom corals lying on the sand: cream and green discs.
				D("MushroomCoral", DetailKind.SandDollar, 3, 0.012f, 0.1f, 0.25f, "#d8d0b0", "#8aa070",
					accents: new[] { H("#d8d0b0"), H("#8aa070"), H("#c8c0a0") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 5f, groupSize: 5f, sink: new Vector2(0.004f, 0.01f)),
				// The drop-off's gorgonians and whips set across the current, and the prized black coral.
				D("GiantGorgonian", DetailKind.SeaFan, 1, 2.2f, 0.02f, 0.9f, "#c03a2a", "#e8704a",
					accents: new[] { H("#c03a2a"), H("#e8704a"), H("#d8a040") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 14f, groupSize: 3f, sink: new Vector2(0.05f, 0.1f)),
				D("WhipCoral", DetailKind.TubeWorms, 3, 1.5f, 0.01f, 0.1f, "#d03a2a", "#e0c040",
					accents: new[] { H("#d03a2a"), H("#e0c040") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 8f, groupSize: 4f, sink: new Vector2(0.03f, 0.06f)),
				D("BlackCoral", DetailKind.SeaFan, 1, 1.5f, 0.02f, 0.45f, "#1a1612", "#7a4a2a",
					accents: new[] { H("#1a1612"), H("#7a4a2a") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 16f, groupSize: 2f, sink: new Vector2(0.05f, 0.1f)),
				// Soft corals: Dendronephthya trees (pink, violet, red) and Sarcophyton leather caps.
				D("TreeSoftCoral", DetailKind.SoftCoral, 3, 0.55f, 0.02f, 0.35f, "#e06aa0", "#c040a0",
					accents: new[] { H("#e06aa0"), H("#c040a0"), H("#d03a3a"), H("#f0b0c0") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 8f, groupSize: 6f, sink: new Vector2(0.03f, 0.06f)),
				D("LeatherCoral", DetailKind.SoftCoral, 1, 0.5f, 0.02f, 0.5f, "#a89860", "#8a8a50",
					accents: new[] { H("#a89860"), H("#8a8a50"), H("#b8a878") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 6f, groupSize: 4f, sink: new Vector2(0.03f, 0.06f)),
				D("BarrelSponge", DetailKind.Sponge, 1, 1f, 0.25f, 0.5f, "#8a4a2a", "#6a3a22", snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 12f, groupSize: 2f, sink: new Vector2(0.05f, 0.1f)),
				D("CarpetAnemone", DetailKind.Anemone, 1, 0.1f, 0.01f, 0.6f, "#e8e0c8", "#8040a0",
					accents: new[] { H("#e8e0c8"), H("#8040a0") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 8f, groupSize: 2f, sink: new Vector2(0.01f, 0.025f)),
				// Grazers and stars: long-spined black urchins, cobalt Linckia, the crown-of-thorns on its own white feeding scar.
				D("LongSpineUrchin", DetailKind.Urchin, 1, 0.22f, 0.004f, 0.12f, "#14121a", "#2a3a6a", snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 6f, groupSize: 6f, sink: new Vector2(0.01f, 0.02f)),
				D("BlueLinckia", DetailKind.Starfish, 1, 0.03f, 0f, 0.22f, "#2a4ac0", "#3a5ad0", snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 8f, groupSize: 2f, sink: new Vector2(0f, 0.006f)),
				D("CrownOfThorns", DetailKind.Starfish, 1, 0.05f, 0f, 0.4f, "#6a5a7a", "#8a7a9a",
					accents: new[] { H("#6a5a7a"), H("#8a7a9a"), H("#f0ece0") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 8f, groupSize: 2f, sink: new Vector2(0f, 0.02f)),
				// Litter: cowries, cone shells and spider conchs; the live money cowrie of the reef flat; the reef's giant clams.
				D("ShellsReef", DetailKind.Shells, 4, 0.03f, 0f, 0.35f, "#e8d8a0", "#c88a3a",
					accents: new[] { H("#e8d8a0"), H("#c88a3a"), H("#e0b080"), H("#d8c8b0") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 3f, groupSize: 8f, sink: new Vector2(0f, 0.008f)),
				D("MoneyCowrie", DetailKind.Shells, 4, 0.03f, 0f, 0.35f, "#f0e4b0", "#e8d8a0",
					accents: new[] { H("#f0e4b0"), H("#e8d8a0"), H("#f4ecc8") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 20f, groupSize: 30f, sink: new Vector2(0f, 0.004f)),
				D("GiantClamReef", DetailKind.GiantClam, 1, 0.5f, 0f, 0.5f, "#d8d0c0", "#b8b0a0",
					accents: new[] { H("#2a6ab0"), H("#3aa080"), H("#7a4ab0") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 10f, groupSize: 2f, sink: new Vector2(0.05f, 0.1f)),
				D("LeopardCucumber", DetailKind.SeaCucumber, 1, 0.12f, 0.08f, 0.25f, "#e8dcc0", "#2a2420",
					accents: new[] { H("#e8dcc0"), H("#2a2420"), H("#d8c8a8") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 8f, groupSize: 3f, sink: new Vector2(0.02f, 0.04f)),
				// Halimeda, the calcareous green alga that makes the sand; the garden eels' colony; the ghost-shrimp mounds.
				D("Halimeda", DetailKind.Seaweed, 8, 0.22f, 0.03f, 0.15f, "#6a9a4a", "#a8c880", lean: 0.3f, segments: 6,
					snowBury: 0f, healthy: SeaTint, dry: SeaTint, groupMetres: 6f, groupSize: 10f, sink: new Vector2(0.005f, 0.015f)),
				D("GardenEels", DetailKind.TubeWorms, 12, 0.4f, 0.01f, 0.3f, "#d8d8d0", "#b0b0a8", snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 30f, groupSize: 40f, sink: new Vector2(0.03f, 0.06f)),
				D("ShrimpMounds", DetailKind.Xenophyophore, 2, 0.15f, 0f, 0.25f, "#c8c4b0", "#6a6656",
					accents: new[] { H("#c8c4b0"), H("#b8b4a0"), H("#6a6656") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 6f, groupSize: 8f, sink: new Vector2(0.01f, 0.02f)),

				// ── Beach (biome round, 2026-10-26): the surf and the swash. ──
				// Baltic cockle and pink tellin; the warm band's olive shells, augers and coquina fans; the sand-grey sand dollar.
				D("ShellsCockle", DetailKind.Shells, 4, 0.03f, 0f, 0.35f, "#e8e0d0", "#e8b8b0",
					accents: new[] { H("#e8e0d0"), H("#e8b8b0"), H("#d8c8b0") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 3f, groupSize: 8f, sink: new Vector2(0f, 0.01f)),
				D("ShellsOlive", DetailKind.Shells, 4, 0.03f, 0f, 0.35f, "#e8c8a0", "#8a6a9a",
					accents: new[] { H("#e8c8a0"), H("#8a6a9a"), H("#c8a878"), H("#f0e4d0") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 3f, groupSize: 8f, sink: new Vector2(0f, 0.01f)),
				D("SandDollarStrand", DetailKind.SandDollar, 3, 0.009f, 0.04f, 0.25f, "#a89a88", "#8a7c6a", snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 4f, groupSize: 10f, sink: new Vector2(0.001f, 0.003f)),
				// Loose blackened eelgrass ribbons that sway in the surge.
				D("EelgrassDrift", DetailKind.Seaweed, 5, 0.8f, 0.03f, 0.3f, "#2a2a1e", "#3a3a24", lean: 0.9f, segments: 6,
					snowBury: 0f, healthy: SeaTint, dry: SeaTint, groupMetres: 5f, groupSize: 8f, sink: new Vector2(0f, 0.01f)),

				// ── Abyss (biome round, 2026-10-26): the hadal trench and its cold seeps. The sun never reaches it; the sea details are the
				// animals' and mats' own colours, and three glow (the lure polyps, the umbellula's ring, the stalked beacons).
				// LATER (I9b P31 / SEA-LIFE): the sea pig's five leg-pairs, the tripod fish standing on fin-ray stilts, the harp sponge's vanes,
				// the methane bubble streams; the kit's stalks and cushions stand in.
				D("SeaPig", DetailKind.SeaCucumber, 2, 0.05f, 0.05f, 0.09f, "#e8b0b0", "#f4d0c8",
					accents: new[] { H("#e8b0b0"), H("#f4d0c8"), H("#d8a0a8") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 12f, groupSize: 40f, sink: new Vector2(0.005f, 0.012f)),
				D("TripodFish", DetailKind.SeaPen, 1, 0.35f, 0.006f, 0.12f, "#2a2a2e", "#3a3a40", snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 20f, groupSize: 1f, sink: new Vector2(0.01f, 0.02f)),
				D("HarpSponge", DetailKind.Sponge, 1, 0.35f, 0.08f, 0.2f, "#e8e0cc", "#d8d0bc", snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 8f, groupSize: 3f, sink: new Vector2(0.02f, 0.04f)),
				D("SeaLily", DetailKind.Crinoid, 3, 0.8f, 0.005f, 0.2f, "#d8c8b8", "#c8b8a8",
					accents: new[] { H("#d8c8b8"), H("#e0d4c4") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 6f, groupSize: 6f, sink: new Vector2(0.02f, 0.04f)),
				Glowing(D("Umbellula", DetailKind.SeaPen, 1, 2f, 0.01f, 0.2f, "#f0e0d0", "#e8d8c8",
					accents: new[] { H("#f0e0d0"), H("#e8d8c8") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 30f, groupSize: 1f, sink: new Vector2(0.03f, 0.06f)), new Color(0.05f, 0.2f, 0.55f)),
				Glowing(D("LurePolyp", DetailKind.Anemone, 1, 0.1f, 0.008f, 0.1f, "#3a6ac0", "#a8d8ff",
					accents: new[] { H("#3a6ac0"), H("#a8d8ff") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 4f, groupSize: 4f, sink: new Vector2(0.005f, 0.012f)), new Color(0.1f, 0.35f, 1f)),
				D("Lamellibrachia", DetailKind.TubeWorms, 12, 1.5f, 0.012f, 0.3f, "#d8ccb0", "#6a5a42",
					accents: new[] { H("#a02020"), H("#d8ccb0"), H("#6a5a42") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 5f, groupSize: 5f, sink: new Vector2(0.03f, 0.06f)),
				D("SeepMat", DetailKind.BacterialMat, 4, 0.012f, 0f, 0.6f, "#f0ece0", "#e08a30",
					accents: new[] { H("#f0ece0"), H("#e08a30"), H("#7a4a8a") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 5f, groupSize: 10f, sink: new Vector2(0.001f, 0.003f)),
				D("SeepMussel", DetailKind.MusselBed, 8, 0.09f, 0f, 0.35f, "#5a4a32", "#8a7448",
					accents: new[] { H("#5a4a32"), H("#8a7448"), H("#463e34") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 4f, groupSize: 10f, sink: new Vector2(0.006f, 0.015f)),
				D("HydrateMound", DetailKind.Xenophyophore, 2, 0.6f, 0f, 0.6f, "#ece4c4", "#c87a7a",
					accents: new[] { H("#ece4c4"), H("#c87a7a"), H("#463e34") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 8f, groupSize: 3f, sink: new Vector2(0.04f, 0.08f)),
				D("XenophyophoreHadal", DetailKind.Xenophyophore, 3, 0.15f, 0f, 0.25f, "#5a5446", "#6e6858",
					accents: new[] { H("#5a5446"), H("#6e6858"), H("#4a4438") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 8f, groupSize: 3f, sink: new Vector2(0.01f, 0.02f)),

				// ── Estuary (biome round, 2026-10-26): the Wadden's mussel banks and the flats' green gutweed ──
				// Blue-black Mytilus banks, hummocky, fringed with bladderwrack and barnacles.
				D("MusselBank", DetailKind.MusselBed, 12, 0.09f, 0f, 0.2f, "#1e2228", "#3a4a5a",
					accents: new[] { H("#1e2228"), H("#3a4a5a"), H("#6a5a2a"), H("#a89e88") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					variant: 1, groupMetres: 4f, groupSize: 14f, sink: new Vector2(0.006f, 0.015f)),
				// Gutweed (Ulva intestinalis): bright tube-hair mats lying on the mud, with sea-lettuce sheets.
				D("Gutweed", DetailKind.SeaLettuce, 8, 0.1f, 0.15f, 0.12f, "#5aaa3a", "#8ad04a", snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 4f, groupSize: 10f, sink: new Vector2(0.01f, 0.02f)),

				// ── Mangrove (biome round, 2026-10-26): rock oysters on the roots, the lagoon's upside-down jellies and sandfish ──
				// Saccostrea rock-oyster clumps and barnacles on root feet and stones.
				D("RootOysters", DetailKind.MusselBed, 10, 0.09f, 0f, 0.3f, "#a89e88", "#6e685c",
					accents: new[] { H("#a89e88"), H("#6e685c"), H("#8a8270"), H("#c8c0a8") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					variant: 1, groupMetres: 4f, groupSize: 12f, sink: new Vector2(0.006f, 0.015f)),
				// Cassiopea: pale discs lying bell-down on lagoon mud. LATER (SEA-LIFE): the slow pulse on the shared clock.
				D("UpsideDownJelly", DetailKind.Starfish, 2, 0.02f, 0f, 0.3f, "#c8d0c0", "#a8b8a0", snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 10f, groupSize: 4f, sink: new Vector2(0f, 0.006f)),
				// Holothuria scabra: grey-banded sea cucumbers half-buried in the lagoon.
				D("Sandfish", DetailKind.SeaCucumber, 1, 0.08f, 0.05f, 0.2f, "#6a6458", "#3a3430",
					snowBury: 0f, healthy: SeaTint, dry: SeaTint, groupMetres: 6f, groupSize: 3f, sink: new Vector2(0.01f, 0.025f)),
				// ── Lake and River (biome round, 2026-10-26): the clear lake's shelf and the river's channel, on the existing aquatic kinds. LATER
				// (I9b): the lobelia's bells above the surface, quillwort's own rosette, the pondweed forest and the ball's velvet as kinds of their own. ──
				D("WaterLobelia", DetailKind.Seaweed, 5, 0.5f, 0.02f, 0.2f, "#2e5a3a", "#4a7a4a", lean: 0.1f, segments: 4,
					snowBury: 0f, healthy: SeaTint, dry: SeaTint, groupMetres: 5f, groupSize: 6f, sink: new Vector2(0.01f, 0.03f)),
				D("Quillwort", DetailKind.Seaweed, 10, 0.15f, 0.008f, 0.3f, "#1e3a2a", "#3a5a3a", lean: 0.05f, segments: 3,
					snowBury: 0f, healthy: SeaTint, dry: SeaTint, groupMetres: 6f, groupSize: 20f, sink: new Vector2(0.01f, 0.025f)),
				D("Pondweed", DetailKind.Kelp, 3, 2f, 0.06f, 0.3f, "#4a7a2a", "#8aa83a", lean: 0.2f, segments: 6,
					snowBury: 0f, healthy: SeaTint, dry: SeaTint, groupMetres: 6f, groupSize: 8f, sink: new Vector2(0.03f, 0.06f)),
				D("Milfoil", DetailKind.Seaweed, 6, 0.8f, 0.05f, 0.2f, "#3a5a2a", "#5a7a3a", lean: 0.3f, segments: 6,
					snowBury: 0f, healthy: SeaTint, dry: SeaTint, groupMetres: 5f, groupSize: 8f, sink: new Vector2(0.01f, 0.03f)),
				D("WillowMoss", DetailKind.Seaweed, 8, 0.3f, 0.03f, 0.15f, "#1a3a1e", "#2e5a2a", lean: 0.9f, segments: 4,
					snowBury: 0f, healthy: SeaTint, dry: SeaTint, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.01f, 0.03f)),
				D("LakeBall", DetailKind.BrainCoral, 1, 0.08f, 0f, 0.25f, "#3a6a2a", "#5a8a3a", snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 4f, groupSize: 6f, sink: new Vector2(0.01f, 0.03f)),
				D("LakeSponge", DetailKind.Sponge, 2, 0.1f, 0.02f, 0.15f, "#5a8a3a", "#8aa860", snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 5f, groupSize: 3f, sink: new Vector2(0.01f, 0.03f)),
				D("NetSinkers", DetailKind.Shells, 3, 0.03f, 0f, 0.3f, "#8a8478", "#5a5a52",
					accents: new[] { H("#8a8478"), H("#a07a5a"), H("#6a6a62") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 6f, groupSize: 5f, sink: new Vector2(0f, 0.008f)),
				D("FennelWeed", DetailKind.Seaweed, 8, 1.4f, 0.02f, 0.25f, "#3e4a22", "#6a7a3a", lean: 0.8f, segments: 8,
					snowBury: 0f, healthy: SeaTint, dry: SeaTint, groupMetres: 8f, groupSize: 10f, sink: new Vector2(0.01f, 0.03f)),
				D("RibbonWeed", DetailKind.Seaweed, 6, 1.8f, 0.04f, 0.25f, "#4a6a2a", "#8aa84a", lean: 0.85f, segments: 8,
					snowBury: 0f, healthy: SeaTint, dry: SeaTint, groupMetres: 6f, groupSize: 8f, sink: new Vector2(0.01f, 0.03f)),
				D("Lemanea", DetailKind.SeaLettuce, 10, 0.1f, 0.1f, 0.1f, "#1e2418", "#3a4228", snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 3f, groupSize: 10f, sink: new Vector2(0.01f, 0.02f)),
				D("Starwort", DetailKind.Seaweed, 5, 0.6f, 0.03f, 0.2f, "#6a9a2a", "#a8c84a", lean: 0.3f, segments: 5,
					snowBury: 0f, healthy: SeaTint, dry: SeaTint, groupMetres: 4f, groupSize: 6f, sink: new Vector2(0.01f, 0.03f)),
				D("EelBaskets", DetailKind.Shells, 1, 0.35f, 0f, 0.3f, "#8a6a3a", "#6a4a2a",
					accents: new[] { H("#8a6a3a"), H("#6a4a2a"), H("#8a8478") }, snowBury: 0f, healthy: SeaTint, dry: SeaTint,
					groupMetres: 8f, groupSize: 2f, sink: new Vector2(0.005f, 0.015f)),
			};
		}

		/// <summary>A sea detail with its own light (<see cref="DetailSpec.Glow"/>, HDR): drawn on the cave glow path.</summary>
		private static DetailSpec Glowing(DetailSpec spec, Color glow)
		{
			spec.Glow = glow;
			return spec;
		}

		/// <summary>
		/// The traits of the detail kinds the sea package owns (<see cref="DetailKindTraits"/>): MusselBed … SlimeMat in
		/// <see cref="DetailKind"/>. Sets nothing for any other kind.
		/// </summary>
		static partial void SeaKindTraits(DetailKind kind, ref DetailKindTraits traits)
		{
			switch (kind)
			{
				// The few solid enough to throw a readable shadow on the bed, as the brain and table corals and the
				// sponges do: a clam's heavy valves, a Lophelia thicket, a glass sponge's basket, a leather coral's cap.
				// Everything else here is flat, thin or lying, and its shade is lost in the sediment's own texture.
				case DetailKind.GiantClam:
				case DetailKind.ColdCoral:
				case DetailKind.GlassSponge:
				case DetailKind.SoftCoral:
					traits.CastsShadows = true;
					break;
				default:
					break;
			}
		}

		/// <summary>
		/// The sea's small-rock materials (boulders, <c>Detail_Rocks_*</c>, <c>Detail_Pebbles_*</c>): manganese nodules
		/// carpeting the abyssal plain, coral rubble on a reef.
		/// </summary>
		/// <remarks>
		/// <para>
		/// A small-rock material is a ground family's textures on the shared rock and pebble meshes, so each wears the
		/// family that reads most like the real thing. <b>Nodules</b> wear <see cref="Ground.Ash"/>: a manganese nodule
		/// is a matte, knobbly, brown-black potato of oxide crusts, which the ash's warm dark grains and low smoothness
		/// match, where basalt (the placeholder) is a cooler, glossier, fractured rock. <b>Coral rubble</b> wears
		/// <see cref="Ground.Coral"/>: bleached cream fragments flecked with the rose of coralline algae.
		/// </para>
		/// <para>
		/// The shapes are the shared small-rock and pebble meshes (BiomeArtGenerator.WriteRocks): rounded, which suits
		/// nodules and worn rubble alike. Branch-shaped rubble would need meshes of its own per material.
		/// </para>
		/// </remarks>
		public static readonly RockMaterialSpec[] SeaRockMaterials =
		{
			new RockMaterialSpec { Name = "Nodule", GroundFamily = Ground.Ash },
			new RockMaterialSpec { Name = "Coral", GroundFamily = Ground.Coral },
		};
	}
}
#endif
