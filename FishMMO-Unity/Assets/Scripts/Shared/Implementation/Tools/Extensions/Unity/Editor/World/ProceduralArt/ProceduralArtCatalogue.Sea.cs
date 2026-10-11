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
			};
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
