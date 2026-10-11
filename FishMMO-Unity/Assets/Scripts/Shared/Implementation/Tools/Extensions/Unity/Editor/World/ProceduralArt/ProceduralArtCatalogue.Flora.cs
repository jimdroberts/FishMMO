#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The land flora added by the vegetation expansion (2026-10-10): its detail plants (built by
	/// <see cref="FloraMeshes"/>), the traits of the detail kinds it owns, and the names and paths of the deadwood
	/// props (<see cref="DeadwoodMeshes"/>, written by BiomeArtGenerator.Deadwood.cs).
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>One package's file.</b> The flora package adds its details, kinds' traits and deadwood here and nowhere
	/// shared: <see cref="Details"/> appends <see cref="FloraDetails"/> after the legacy list, the generator reads a
	/// kind's shadows, sink, bark, sway and tint patches through <see cref="Traits"/>, and the payload, wrapper and
	/// prefab lists take the deadwood's paths from the three methods at the bottom.
	/// </para>
	/// <para>
	/// <b>No static fields read during initialisation.</b> <see cref="FloraDetails"/> is a method, not a field, and
	/// <see cref="FloraKindTraits"/> a pure switch: the partial class's field initialisers run file by file in an
	/// unspecified order (see <see cref="Details"/>), so a field here built with <c>D(…)</c> could see the tints as
	/// zero. Constants (<see cref="Ground"/>, <see cref="Bark"/>) are always safe. The deadwood table is built on first
	/// use for the same reason.
	/// </para>
	/// <para>
	/// <b>Names decide the renderer.</b> A prototype named <c>Detail_Grass*</c>, <c>Detail_Reeds*</c> or
	/// <c>Detail_Flowers*</c> is drawn by the client's blade grass (its mesh here is only the WebGL2 fallback and the
	/// source of the blade colours: the stems' root and tip colours and up to four head colours, read from vertices
	/// under alpha 0.5); <c>Detail_Debris*</c> by the GPU detail scatter; <c>Detail_Bush_*</c> is a shrub; everything
	/// else by the detail renderer. So sedges, rushes and cottongrass are <c>Grass…</c>, the common reed
	/// <c>ReedsPlume</c>, and the new flower sets <c>Flowers…</c>.
	/// </para>
	/// </remarks>
	public static partial class ProceduralArtCatalogue
	{
		/// <summary>
		/// The land flora's detail plants: new grasses, sedges and rushes (as <c>Detail_Grass*</c> / <c>Detail_Reeds*</c>
		/// so the blade renderer takes them), flowers (<c>Detail_Flowers*</c>), herbs, ferns, succulents and cacti,
		/// cushions, lichens, creepers, root knobs, litter and mushrooms — every item of the design's §2c–§2e and §2g that
		/// is a detail, named as the design names it.
		/// </summary>
		/// <remarks>
		/// Each entry is a <c>D(…)</c> line as in CoreDetails, with its own <c>groupMetres</c>, <c>groupSize</c> and
		/// <c>sink</c> (the spec reads them only for names it has no legacy value for). Colours are sRGB picks from
		/// photographs of the living plant; a cactus or root knob's colours are a tint on its bark (white = the bark).
		/// </remarks>
		private static DetailSpec[] FloraDetails()
		{
			// The dry grasses' tints (as GrassDry): a golden grass browns less than a green one.
			var paleHealthy = new Color(1f, 0.97f, 0.9f);
			var paleDry = new Color(0.85f, 0.78f, 0.62f);
			Color[] A(params string[] hex)
			{
				var colours = new Color[hex.Length];
				for (int i = 0; i < hex.Length; i++)
				{
					colours[i] = H(hex[i]);
				}
				return colours;
			}
			return new DetailSpec[]
			{
				// ── Grasses, sedges, rushes, reeds (§2c): blade types; meshes are the fallback and the colour source ──
				// Sedge (Carex): a tussock of arching keeled blue-green blades.
				D("GrassSedge", DetailKind.Grass, 16, 0.5f, 0.03f, 0.3f, "#5e7a4a", "#8a9a62", lean: 0.6f,
					groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.02f, 0.04f)),
				// Cottongrass (Eriophorum): olive sedge leaves under white cotton tufts.
				D("GrassCotton", DetailKind.Grass, 10, 0.4f, 0.02f, 0.2f, "#6a7a44", "#8a8a52", lean: 0.4f, accents: A("#f4f4ee"),
					variant: FloraVariant.GrassCotton, groupMetres: 5f, groupSize: 10f, sink: new Vector2(0.02f, 0.04f)),
				// Marram / cordgrass / Stipagrostis: stiff rolled grey-green blades on dunes.
				D("GrassDune", DetailKind.Grass, 18, 0.9f, 0.022f, 0.3f, "#8a9a6e", "#a8b088", lean: 0.3f, healthy: paleHealthy, dry: paleDry,
					groupMetres: 4f, groupSize: 6f, sink: new Vector2(0.03f, 0.06f)),
				// Feather grass (Stipa): a fine grey-green tuft with silver-straw silky awns.
				D("GrassFeather", DetailKind.Grass, 10, 0.8f, 0.012f, 0.22f, "#9a9a6a", "#b8b08a", lean: 0.35f, accents: A("#e8e2cc"),
					healthy: paleHealthy, dry: paleDry, variant: FloraVariant.GrassPlume, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.02f, 0.04f)),
				// Savanna red-oat / elephant grass: tall golden swathes.
				D("GrassSavanna", DetailKind.Grass, 18, 1.8f, 0.035f, 0.45f, "#b8963c", "#d8bc6a", lean: 0.45f, snowBury: 0.3f,
					healthy: paleHealthy, dry: paleDry, groupMetres: 6f, groupSize: 12f, sink: new Vector2(0.03f, 0.06f)),
				// Common reed (Phragmites): 2.5 m culms with strap leaves and a purple-brown plume (the legacy Reeds stays the cattail).
				D("ReedsPlume", DetailKind.Reeds, 8, 2.5f, 0.03f, 0.3f, "#5e7a3a", "#86985a", lean: 0.1f, accents: A("#6a4a48"), snowBury: 0.2f,
					variant: FloraVariant.ReedsPlume, groupMetres: 6f, groupSize: 20f, sink: new Vector2(0.04f, 0.08f)),
				// Rush (Juncus): stiff dark round stems with a brown side tuft.
				D("GrassRush", DetailKind.Grass, 22, 0.8f, 0.007f, 0.18f, "#3e5a2a", "#56703a", lean: 0.12f, segments: 2, accents: A("#7a5a34"),
					variant: FloraVariant.GrassRush, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.03f, 0.06f)),
				// Wheat: golden stems with ears (hand-masked fields).
				D("GrassWheat", DetailKind.Grass, 10, 1f, 0.02f, 0.2f, "#b89a48", "#d4b866", lean: 0.15f, accents: A("#d8b45a"), snowBury: 0.4f,
					healthy: paleHealthy, dry: paleDry, variant: FloraVariant.GrassEar, groupMetres: 8f, groupSize: 20f, sink: new Vector2(0.02f, 0.04f)),

				// ── Flowers (§2d): sprinkled blade flowers, four head colours each ──
				// Gentian blue, white, magenta (moss campion, saxifrage), yellow (alpine buttercup, avens).
				D("FlowersAlpine", DetailKind.Flowers, 7, 0.12f, 0.045f, 0.25f, "#4c7a2c", "#669036", accents: A("#3050c0", "#f4f2ea", "#c0408a", "#f0c830"),
					snowBury: 1f, groupMetres: 3f, groupSize: 10f, sink: new Vector2(0.01f, 0.02f)),
				// Purple loosestrife, yellow flag iris, meadowsweet cream, marsh marigold.
				D("FlowersWet", DetailKind.Flowers, 6, 0.8f, 0.08f, 0.35f, "#3e6a2a", "#5a8034", accents: A("#b8408a", "#e8c832", "#f2ead0", "#f2a81c"),
					snowBury: 0.4f, groupMetres: 3f, groupSize: 10f, sink: new Vector2(0.01f, 0.03f)),
				// Bluebell, wood anemone, ramsons (wild garlic), primrose.
				D("FlowersWoodland", DetailKind.Flowers, 8, 0.25f, 0.05f, 0.35f, "#3e6a28", "#557f30", accents: A("#5a5ac8", "#f4f4f0", "#e8ece0", "#f0e890"),
					groupMetres: 4f, groupSize: 14f, sink: new Vector2(0.01f, 0.02f)),
				// Fireweed magenta, goldenrod / sunflower yellow, lupin violet, umbellifer white.
				D("FlowersTall", DetailKind.Flowers, 6, 1.2f, 0.1f, 0.4f, "#4a6e2c", "#64843a", accents: A("#c0408c", "#e8b820", "#7a5ac0", "#f0eee0"),
					snowBury: 0.3f, groupMetres: 4f, groupSize: 10f, sink: new Vector2(0.01f, 0.03f)),
				// A desert superbloom: poppy orange, lupine blue, desert marigold yellow, sand verbena pink.
				D("FlowersDesert", DetailKind.Flowers, 7, 0.25f, 0.06f, 0.3f, "#6a7a3a", "#8a9450", accents: A("#f08020", "#5060c8", "#f0d030", "#d070b0"),
					snowBury: 0.9f, healthy: paleHealthy, dry: paleDry, groupMetres: 5f, groupSize: 16f, sink: new Vector2(0.01f, 0.02f)),

				// ── Ferns and herbs (§2d) ──
				D("FernBracken", DetailKind.Fern, 5, 1.2f, 0.6f, 0.3f, "#4a7a2c", "#5e8a34", segments: 4, snowBury: 0.3f,
					variant: FloraVariant.FernBracken, groupMetres: 6f, groupSize: 10f, sink: new Vector2(0.04f, 0.08f)),
				D("FernHartstongue", DetailKind.Fern, 10, 0.4f, 0.06f, 0.03f, "#3a7a2a", "#4e8e34", segments: 4,
					variant: FloraVariant.FernHartstongue, groupMetres: 3f, groupSize: 5f, sink: new Vector2(0.03f, 0.06f)),
				// Monstera / elephant-ear on the jungle floor.
				D("BroadHerb", DetailKind.BroadHerb, 6, 1.2f, 0.6f, 0.15f, "#2a5a22", "#3e7a2c", segments: 3, snowBury: 0.2f,
					groupMetres: 5f, groupSize: 6f, sink: new Vector2(0.03f, 0.06f)),
				// Nettle and dock.
				D("HerbRuderal", DetailKind.Shrub, 6, 0.8f, 0.16f, 0.3f, "#2e4a22", "#3e5e2a", snowBury: 0.4f,
					variant: FloraVariant.ShrubHerb, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.03f, 0.06f)),
				// Samphire (Salicornia): green succulent stems reddening to the tips.
				D("ShrubSamphire", DetailKind.Shrub, 8, 0.2f, 0.008f, 0.15f, "#5a8a3a", "#6e9a40", accents: A("#a8403a"), snowBury: 0.9f,
					variant: FloraVariant.ShrubSucculent, groupMetres: 5f, groupSize: 12f, sink: new Vector2(0.02f, 0.04f)),
				// Tumbleweed (Salsola), dried to a tan ball.
				D("ShrubTumbleweed", DetailKind.DryShrub, 24, 0.8f, 0.4f, 0.4f, "#b09868", "#cdb888", snowBury: 0.3f,
					variant: FloraVariant.DryShrubBall, groupMetres: 8f, groupSize: 3f, sink: new Vector2(0.01f, 0.04f)),
				// Horsetail (Equisetum), a later form (§2d P3) for wet taiga and peat.
				D("Horsetail", DetailKind.Shrub, 9, 0.5f, 0.12f, 0.25f, "#4a7a2a", "#5e8e34", snowBury: 0.6f,
					variant: FloraVariant.ShrubHorsetail, groupMetres: 4f, groupSize: 10f, sink: new Vector2(0.02f, 0.05f)),

				// ── Succulents and cacti (§2d): cacti wear the cactus bark, their colours a tint on it ──
				// Prickly pear: the accent is its magenta fruit.
				D("PadCactus", DetailKind.PadCactus, 1, 1.2f, 0.22f, 0.3f, "#ffffff", "#ffffff", accents: A("#9a2a4a"), snowBury: 0f,
					groupMetres: 6f, groupSize: 4f, sink: new Vector2(0.04f, 0.08f)),
				// Cholla: straw-gold living joints over a dark trunk of dead ones.
				D("Cholla", DetailKind.Cholla, 3, 1.5f, 0.04f, 0.2f, "#ffffff", "#ffffff", accents: A("#f2e6a8", "#6a5a48"), snowBury: 0f,
					groupMetres: 8f, groupSize: 5f, sink: new Vector2(0.04f, 0.08f)),
				D("OrganPipe", DetailKind.ColumnCluster, 9, 3f, 0.08f, 0.35f, "#ffffff", "#ffffff", accents: A("#ffffff", "#e0ead8"), snowBury: 0f,
					variant: FloraVariant.OrganPipe, groupMetres: 10f, groupSize: 3f, sink: new Vector2(0.04f, 0.08f)),
				D("Euphorbia", DetailKind.ColumnCluster, 6, 2.6f, 0.07f, 0.3f, "#ffffff", "#ffffff", accents: A("#dde6cc", "#c8d4b4"), snowBury: 0f,
					variant: FloraVariant.Euphorbia, groupMetres: 10f, groupSize: 3f, sink: new Vector2(0.04f, 0.08f)),
				// Agave: thick blue-grey channelled leaves with dark terminal spines.
				D("RosetteAgave", DetailKind.Rosette, 20, 0.9f, 0.14f, 0.12f, "#7a9a96", "#94b0a8", segments: 3, snowBury: 0.2f,
					variant: FloraVariant.RosetteAgave, groupMetres: 6f, groupSize: 4f, sink: new Vector2(0.03f, 0.06f)),
				// Yucca: dark green daggers and a cream-belled flower stalk.
				D("RosetteYucca", DetailKind.Rosette, 26, 0.7f, 0.035f, 0.06f, "#4a6a3a", "#62804a", segments: 3, accents: A("#f0ead0"), snowBury: 0.2f,
					variant: FloraVariant.RosetteYucca, groupMetres: 6f, groupSize: 4f, sink: new Vector2(0.03f, 0.06f)),
				// Bromeliad: glossy recurved straps round a red heart.
				D("RosetteBromeliad", DetailKind.Rosette, 16, 0.4f, 0.07f, 0.05f, "#3a6a2a", "#4e7e34", segments: 3, accents: A("#c02a3a", "#d8384a"), snowBury: 0.2f,
					variant: FloraVariant.RosetteBromeliad, groupMetres: 4f, groupSize: 6f, sink: new Vector2(0.02f, 0.05f)),
				// Tower of jewels (Echium wildpretii), a later form (§2d P3) for dry volcanic slopes.
				D("EchiumSpire", DetailKind.Rosette, 18, 0.4f, 0.06f, 0.35f, "#a8b0a0", "#c0c6b8", segments: 3, accents: A("#c03048", "#9a1e34"), snowBury: 0.2f,
					variant: FloraVariant.RosetteEchium, groupMetres: 8f, groupSize: 3f, sink: new Vector2(0.03f, 0.06f)),
				// Pitcher plant (Sarracenia), a later form (§2d P3) for peat bogs.
				D("PitcherPlant", DetailKind.Rosette, 8, 0.5f, 0.06f, 0.15f, "#6a8a3a", "#7e9a44", accents: A("#a0302a"), snowBury: 0.6f,
					variant: FloraVariant.RosettePitcher, groupMetres: 4f, groupSize: 6f, sink: new Vector2(0.02f, 0.05f)),

				// ── Cushions, lichens, creepers, root knobs (§2e) ──
				D("CushionMossCampion", DetailKind.Cushion, 64, 0.08f, 0.075f, 0.2f, "#4a7a2a", "#5e8a34", accents: A("#d878a8", "#e090b8"), snowBury: 1f,
					variant: FloraVariant.CushionMossCampion, groupMetres: 5f, groupSize: 8f, sink: new Vector2(0.01f, 0.02f)),
				D("CushionThrift", DetailKind.Cushion, 30, 0.08f, 0.07f, 0.15f, "#5a7a3a", "#6e8a44", accents: A("#e0a0c0", "#d888b0"), snowBury: 1f,
					variant: FloraVariant.CushionThrift, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.01f, 0.02f)),
				// Astragalus / Acantholimon hedgehog cushions.
				D("CushionSpiny", DetailKind.Cushion, 70, 0.4f, 0.15f, 0.35f, "#7a8a6a", "#9aa488", accents: A("#d8c8e0"), snowBury: 0.8f,
					variant: FloraVariant.CushionSpiny, groupMetres: 6f, groupSize: 5f, sink: new Vector2(0.02f, 0.04f)),
				// Red on the crown, green down the sides, ochre in patches (the accent).
				D("CushionSphagnum", DetailKind.Cushion, 80, 0.3f, 0.15f, 0.5f, "#8a3a2a", "#6a8a34", accents: A("#b0903a"), snowBury: 1f,
					variant: FloraVariant.CushionSphagnum, groupMetres: 6f, groupSize: 10f, sink: new Vector2(0.02f, 0.05f)),
				D("CushionMoss", DetailKind.Cushion, 72, 0.06f, 0.09f, 0.25f, "#4a6a22", "#6a8a2a", snowBury: 1f,
					variant: FloraVariant.CushionMoss, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.01f, 0.02f)),
				// Reindeer lichen (Cladonia).
				D("LichenClump", DetailKind.Lichen, 2, 0.08f, 0.006f, 0.15f, "#c8ccb0", "#d8dcc4", accents: A("#b8bea0"), snowBury: 1f,
					variant: FloraVariant.LichenClump, groupMetres: 6f, groupSize: 12f, sink: new Vector2(0.005f, 0.015f)),
				// Lava lichen (Stereocaulon vesuvianum).
				D("LichenLava", DetailKind.Lichen, 2, 0.05f, 0.005f, 0.15f, "#b8bab0", "#d0d2c8", accents: A("#a0a49a"), snowBury: 1f,
					variant: FloraVariant.LichenLava, groupMetres: 6f, groupSize: 12f, sink: new Vector2(0.005f, 0.015f)),
				// Ivy (Hedera helix): dark glossy leaves; the accent is its stems.
				D("Ivy", DetailKind.Creeper, 100, 0.05f, 0.13f, 0.7f, "#1e3a18", "#2e5224", accents: A("#3a3424"), snowBury: 1f,
					variant: FloraVariant.CreeperIvy, groupMetres: 5f, groupSize: 10f, sink: new Vector2(0.005f, 0.015f)),
				// Beach morning-glory (Ipomoea pes-caprae): pink-purple flowers, reddish runners.
				D("BeachVine", DetailKind.Creeper, 30, 0.1f, 0.09f, 1.2f, "#3e6a2a", "#548034", accents: A("#c0489a", "#7a4a30"), snowBury: 0.9f,
					variant: FloraVariant.CreeperBeachVine, groupMetres: 6f, groupSize: 8f, sink: new Vector2(0.005f, 0.015f)),
				// Black mangrove pneumatophores: mud-stained at the foot, grey-brown above (a tint on the fibrous bark).
				D("Pneumatophores", DetailKind.RootKnobs, 30, 0.3f, 0.013f, 0.7f, "#6e6454", "#b0a690", snowBury: 0f,
					variant: FloraVariant.Pneumatophores, groupMetres: 6f, groupSize: 14f, sink: new Vector2(0.03f, 0.06f)),
				// Bald cypress knees.
				D("CypressKnees", DetailKind.RootKnobs, 5, 1f, 0f, 0.9f, "#8a7a6a", "#b8a898", snowBury: 0f,
					variant: FloraVariant.CypressKnees, groupMetres: 5f, groupSize: 5f, sink: new Vector2(0.03f, 0.06f)),

				// ── Litter and fungi (§2g) ──
				D("DebrisNeedle", DetailKind.DebrisNeedle, 14, 0f, 0.25f, 0.55f, "#7a5230", "#a06a3c", accents: A("#4a3c2c", "#5a3a22", "#7a5230"), snowBury: 1f,
					groupMetres: 5f, groupSize: 10f, sink: new Vector2(0f, 0.01f)),
				D("DebrisPalm", DetailKind.DebrisPalm, 2, 3f, 0.8f, 1f, "#8a6e40", "#a88a58", accents: A("#5a3e24", "#7a5634"), snowBury: 1f,
					groupMetres: 6f, groupSize: 4f, sink: new Vector2(0f, 0.01f)),
				D("DebrisWrack", DetailKind.DebrisWrack, 34, 0.35f, 0.035f, 1f, "#2e2618", "#4a3c22", accents: A("#8a7a58", "#6a5e4c"), snowBury: 1f,
					groupMetres: 8f, groupSize: 10f, sink: new Vector2(0f, 0.01f)),
				D("DebrisBranch", DetailKind.DebrisBranch, 3, 1.6f, 0.035f, 0.6f, "#ffffff", "#ffffff", snowBury: 1f,
					groupMetres: 6f, groupSize: 5f, sink: new Vector2(0f, 0.02f)),
				D("DebrisBamboo", DetailKind.DebrisBamboo, 3, 2.6f, 0.03f, 0.8f, "#c4b078", "#b0a070", accents: A("#8a7a50", "#a07a48", "#b8a464"), snowBury: 1f,
					groupMetres: 6f, groupSize: 6f, sink: new Vector2(0f, 0.01f)),
				// Fly agaric (scarlet), its white gills and stem, and penny-bun boletes (brown cap, pale stem).
				D("MushroomForest", DetailKind.Mushroom, 5, 0.15f, 0.06f, 0.35f, "#c8241c", "#d8401c", accents: A("#f2eee2", "#f0ece4", "#7a4a24", "#d8ccb0"), snowBury: 1f,
					variant: FloraVariant.MushroomForest, groupMetres: 4f, groupSize: 4f, sink: new Vector2(0.005f, 0.015f)),
				// Honey fungus: honey-brown caps, cream gills, buff stems.
				D("MushroomCluster", DetailKind.Mushroom, 10, 0.1f, 0.035f, 0.15f, "#b88a3c", "#a0742c", accents: A("#e0d0a8", "#c8b48a"), snowBury: 1f,
					variant: FloraVariant.MushroomCluster, groupMetres: 4f, groupSize: 3f, sink: new Vector2(0.005f, 0.015f)),
				// Shaggy ink caps (white, the rim inking black) and turkey-tail brackets on a stick.
				D("MushroomSwamp", DetailKind.Mushroom, 3, 0.2f, 0.035f, 0.3f, "#ece8e0", "#e0dcd2", accents: A("#d8d4cc", "#f0ece4", "#2a2626", "#7a6a52"), snowBury: 1f,
					variant: FloraVariant.MushroomSwamp, groupMetres: 4f, groupSize: 4f, sink: new Vector2(0.005f, 0.015f)),
				// Pale cave bonnets.
				D("MushroomCave", DetailKind.Mushroom, 9, 0.12f, 0.022f, 0.25f, "#e8e4d4", "#d8d2bc", accents: A("#f0ecdc", "#f2eee4"), snowBury: 1f,
					variant: FloraVariant.MushroomCave, groupMetres: 4f, groupSize: 5f, sink: new Vector2(0.005f, 0.015f)),
			};
		}

		/// <summary>
		/// The traits of the detail kinds the flora package owns (<see cref="DetailKindTraits"/>): BroadHerb … DebrisBamboo
		/// in <see cref="DetailKind"/>. Sets nothing for any other kind (the legacy kinds' variants keep their kind's
		/// legacy traits; a variant's own sink comes from its spec's <see cref="DetailSpec.Sink"/>).
		/// </summary>
		static partial void FloraKindTraits(DetailKind kind, ref DetailKindTraits traits)
		{
			switch (kind)
			{
				case DetailKind.BroadHerb:
					// A metre of great leaves: a readable shadow; they flap in the wind and brown in broad stands.
					traits.CastsShadows = true; traits.Sink = 0.05f; traits.Sway = 0.25f; traits.TintPatchMetres = 18f;
					break;
				case DetailKind.PadCactus:
				case DetailKind.Cholla:
					traits.CastsShadows = true; traits.Sink = 0.05f; traits.BarkFamily = Bark.Cactus;
					break;
				case DetailKind.ColumnCluster:
					traits.CastsShadows = true; traits.Sink = 0.06f; traits.BarkFamily = Bark.Cactus;
					break;
				case DetailKind.Rosette:
					// Stiff leaves: barely any sway.
					traits.CastsShadows = true; traits.Sink = 0.04f; traits.Sway = 0.05f; traits.TintPatchMetres = 18f;
					break;
				case DetailKind.Cushion:
					traits.Sink = 0.02f; traits.Sway = 0.02f; traits.TintPatchMetres = 10f;
					break;
				case DetailKind.Lichen:
					traits.Sink = 0.012f; traits.Sway = 0.01f;
					break;
				case DetailKind.Creeper:
					traits.Sink = 0.012f; traits.Sway = 0.05f; traits.TintPatchMetres = 10f;
					break;
				case DetailKind.RootKnobs:
					// Woody: they wear a bark (cypress knees and mangrove roots are both fibrous-barked).
					traits.CastsShadows = true; traits.Sink = 0.05f; traits.BarkFamily = Bark.Fibrous;
					break;
				case DetailKind.Mushroom:
					traits.Sink = 0.012f; traits.Sway = 0.01f;
					break;
				case DetailKind.DebrisNeedle:
				case DetailKind.DebrisPalm:
				case DetailKind.DebrisWrack:
				case DetailKind.DebrisBamboo:
					traits.Sink = 0.012f; traits.Sway = 0.01f;
					break;
				case DetailKind.DebrisBranch:
					// Dead sticks: bark, no leaves.
					traits.Sink = 0.02f; traits.BarkFamily = Bark.Brown;
					break;
				default:
					break;
			}
		}

		// ── Deadwood (DeadwoodMeshes, BiomeArtGenerator.Deadwood.cs) ──────────

		/// <summary>Every deadwood prefab, mesh and material starts with this.</summary>
		public const string DeadwoodPrefix = "Deadwood_";

		/// <summary>A deadwood prop's prefab name (a tree-channel prop: bedded, a baked collider).</summary>
		public static string DeadwoodPrefab(string name) => DeadwoodPrefix + name;

		/// <summary>A deadwood prop's mesh at a level of detail.</summary>
		public static string DeadwoodMesh(string name, int lod) => $"{DeadwoodPrefab(name)}_LOD{lod}";

		/// <summary>The material a deadwood prop's bark (sub-mesh 0) wears: its bark family's, held still; a petrified log's stone.</summary>
		public static string DeadwoodBarkMaterial(in DeadwoodSpecies species) =>
			species.Form == DeadwoodForm.PetrifiedLog ? DeadwoodPrefix + "Petrified" : DeadwoodPrefix + "Bark_" + species.BarkFamily;

		/// <summary>The material every deadwood prop's exposed wood (sub-mesh 1: broken ends, cut faces, soil, fungi) wears: vertex colours on the atlas's solid cell.</summary>
		public const string DeadwoodWoodMaterial = DeadwoodPrefix + "Wood";

		private static DeadwoodSpecies[] deadwood;

		/// <summary>
		/// Every deadwood prop (the design's §2g, tree-channel items): fallen logs in three barks, stumps snapped, sawn
		/// and shelved with brackets, driftwood, a petrified log and a windthrow root plate, at their real sizes.
		/// </summary>
		/// <remarks>
		/// <list type="bullet">
		/// <item><b>FallenLog_Oak</b>: 10 m of oak (brown bark) 0.4 m in radius, snapped at both ends, mossed along its top,
		/// with branch stubs.</item>
		/// <item><b>FallenLog_Conifer</b>: 12 m of spruce or pine (pine bark), 0.32 m, the most common log of the taiga.</item>
		/// <item><b>FallenLog_Birch</b>: 7 m of birch, 0.2 m: birch rots inside its bark, so its ends crumble.</item>
		/// <item><b>Stump_Broken</b>: a snapped stump 0.9 m tall with root buttresses, splintered at the top.</item>
		/// <item><b>Stump_Cut</b>: a sawn conifer stump, its flat top ringed pale and dark.</item>
		/// <item><b>Stump_Bracket</b>: an old rotting stump shelved with bracket fungi (the design's P3 bracket-fungus stump).</item>
		/// <item><b>Driftwood</b>: 4 m of sea- and river-worn wood, bark gone, bleached silver, smooth and twisted, a forked
		/// branch and root knobs at its foot.</item>
		/// <item><b>PetrifiedLog</b>: 7 m of silicified trunk broken into cordwood segments, red-brown and ochre outside, its
		/// broken faces banded in agate reds, ambers and greys.</item>
		/// <item><b>RootPlate</b>: a windthrown tree's root plate (the design's P3), a 3.2 m disc of soil and roots stood on
		/// edge, the snapped trunk lying from it.</item>
		/// </list>
		/// Built on first use, like <see cref="Details"/>.
		/// </remarks>
		public static DeadwoodSpecies[] Deadwood
		{
			get
			{
				if (deadwood == null)
				{
					Color moss = H("#4a6a24");
					deadwood = new[]
					{
						new DeadwoodSpecies { Name = "FallenLog_Oak", Form = DeadwoodForm.FallenLog, Length = 10f, Radius = 0.4f, BarkFamily = Bark.Brown,
							BarkTint = H("#e8e0d8"), WoodA = H("#c8a878"), WoodB = H("#8a6844"), Moss = 0.65f, MossColour = moss, Stubs = 3 },
						new DeadwoodSpecies { Name = "FallenLog_Conifer", Form = DeadwoodForm.FallenLog, Length = 12f, Radius = 0.32f, BarkFamily = Bark.Pine,
							BarkTint = H("#e0d8d0"), WoodA = H("#d8b888"), WoodB = H("#a07a50"), Moss = 0.5f, MossColour = H("#56742a"), Stubs = 4 },
						new DeadwoodSpecies { Name = "FallenLog_Birch", Form = DeadwoodForm.FallenLog, Length = 7f, Radius = 0.2f, BarkFamily = Bark.Birch,
							BarkTint = H("#f0ece6"), WoodA = H("#b89a70"), WoodB = H("#7a5e40"), Moss = 0.35f, MossColour = moss, Stubs = 2 },
						new DeadwoodSpecies { Name = "Stump_Broken", Form = DeadwoodForm.Stump, Length = 0.9f, Radius = 0.35f, BarkFamily = Bark.Brown,
							BarkTint = H("#e8e0d8"), WoodA = H("#c8a878"), WoodB = H("#7a5a38"), Moss = 0.45f, MossColour = moss },
						new DeadwoodSpecies { Name = "Stump_Cut", Form = DeadwoodForm.Stump, Length = 0.45f, Radius = 0.3f, BarkFamily = Bark.Pine,
							BarkTint = H("#e8e0d8"), WoodA = H("#e0c498"), WoodB = H("#b08a5a"), Moss = 0.2f, MossColour = moss, Sawn = true },
						new DeadwoodSpecies { Name = "Stump_Bracket", Form = DeadwoodForm.Stump, Length = 0.7f, Radius = 0.38f, BarkFamily = Bark.Brown,
							BarkTint = H("#d8d0c8"), WoodA = H("#a88a62"), WoodB = H("#6a4e30"), Moss = 0.6f, MossColour = moss, Brackets = 5,
							Bands = new[] { H("#e8dcc0"), H("#a07a50"), H("#6a5038") } },
						new DeadwoodSpecies { Name = "Driftwood", Form = DeadwoodForm.Driftwood, Length = 4f, Radius = 0.16f, BarkFamily = Bark.Dead,
							BarkTint = H("#f4f0ea"), WoodA = H("#c8c2b6"), WoodB = H("#a8a296"), Stubs = 3 },
						new DeadwoodSpecies { Name = "PetrifiedLog", Form = DeadwoodForm.PetrifiedLog, Length = 7f, Radius = 0.42f, BarkFamily = Bark.Dead,
							BarkTint = H("#c89a7a"), WoodA = H("#6a4434"), WoodB = H("#9a7a64"),
							Bands = new[] { H("#5a3a2e"), H("#b0482e"), H("#d89a4a"), H("#e8c890"), H("#8a5a6a"), H("#c8beb0"), H("#a0302a") } },
						new DeadwoodSpecies { Name = "RootPlate", Form = DeadwoodForm.RootPlate, Length = 3.2f, Radius = 0.35f, BarkFamily = Bark.Brown,
							BarkTint = H("#9a8a7a"), WoodA = H("#4a3a28"), WoodB = H("#7a6448"), Moss = 0.2f, MossColour = moss, Stubs = 12,
							Bands = new[] { H("#8a8a84"), H("#6a6a64") } },
					};
				}
				return deadwood;
			}
		}

		/// <summary>The deadwood prop with this name (without the prefix).</summary>
		public static bool TryDeadwood(string name, out DeadwoodSpecies species)
		{
			foreach (DeadwoodSpecies d in Deadwood)
			{
				if (d.Name == name)
				{
					species = d;
					return true;
				}
			}
			species = default;
			return false;
		}

		/// <summary>The distinct bark materials the deadwood wears, in table order.</summary>
		private static IEnumerable<string> DeadwoodBarkMaterials()
		{
			var seen = new HashSet<string>();
			foreach (DeadwoodSpecies d in Deadwood)
			{
				DeadwoodSpecies species = d;
				string material = DeadwoodBarkMaterial(in species);
				if (seen.Add(material))
				{
					yield return material;
				}
			}
		}

		/// <summary>Every deadwood payload file: three meshes a prop.</summary>
		public static IEnumerable<string> DeadwoodPayloadPaths()
		{
			foreach (DeadwoodSpecies d in Deadwood)
			{
				for (int lod = 0; lod < DeadwoodMeshes.Levels; lod++)
				{
					yield return MeshPath(DeadwoodMesh(d.Name, lod));
				}
			}
		}

		/// <summary>Every deadwood wrapper besides its prefabs: the bark materials and the wood material.</summary>
		public static IEnumerable<string> DeadwoodWrapperPaths()
		{
			foreach (string material in DeadwoodBarkMaterials())
			{
				yield return MaterialPath(material);
			}
			yield return MaterialPath(DeadwoodWoodMaterial);
		}

		/// <summary>Every deadwood prefab's name, for the spec table's checks.</summary>
		public static IEnumerable<string> DeadwoodPrefabNames()
		{
			foreach (DeadwoodSpecies d in Deadwood)
			{
				yield return DeadwoodPrefab(d.Name);
			}
		}
	}
}
#endif
