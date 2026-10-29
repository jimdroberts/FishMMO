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

				// ── Forest (biome round, 2026-10-26): the primeval beech-and-fir forest's own plants, on the existing kinds ──
				// Great wood-rush (Luzula sylvatica): glossy shade tussocks, chestnut flower heads.
				D("GrassWoodrush", DetailKind.Grass, 14, 0.35f, 0.02f, 0.25f, "#2a4418", "#4e7028", lean: 0.5f, accents: A("#6a4a2a"),
					variant: FloraVariant.GrassPlume, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.02f, 0.04f)),
				// Wood melick (Melica uniflora): fine blades, nodding purple-brown spikelets in the light gaps.
				D("GrassWoodMelick", DetailKind.Grass, 12, 0.4f, 0.01f, 0.25f, "#3a5a24", "#6a8a3a", lean: 0.5f, accents: A("#7a4a5a"),
					variant: FloraVariant.GrassPlume, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.02f, 0.04f)),
				// Pendulous sedge (Carex pendula): arching metre-long blades and drooping catkins by the streams.
				D("GrassSedgePendulous", DetailKind.Grass, 14, 1.1f, 0.03f, 0.4f, "#3a5a2a", "#6a7e44", lean: 0.7f, accents: A("#5a4a2a"),
					variant: FloraVariant.GrassPlume, groupMetres: 5f, groupSize: 8f, sink: new Vector2(0.03f, 0.06f)),
				// The beechwood's spring floor: wood anemone, lesser celandine, wood sorrel, sweet woodruff.
				D("FlowersBeechwood", DetailKind.Flowers, 8, 0.15f, 0.04f, 0.35f, "#3e6a28", "#557f30", accents: A("#f6f4f0", "#f2d020", "#f4ecf0", "#f8f8f4"),
					groupMetres: 4f, groupSize: 14f, sink: new Vector2(0.01f, 0.02f)),
				// Ramsons (Allium ursinum): broad strap leaves under white star umbels, in dense carpets.
				D("FlowersRamsons", DetailKind.Flowers, 10, 0.35f, 0.05f, 0.35f, "#3a6a22", "#5a8a30", accents: A("#f6f6f0"),
					groupMetres: 6f, groupSize: 20f, sink: new Vector2(0.01f, 0.02f)),
				// Male and lady fern shuttlecocks, bigger and darker than the generic fern.
				D("FernShuttlecock", DetailKind.Fern, 7, 1f, 0.4f, 0.03f, "#2a5418", "#40702a", segments: 4, snowBury: 0.4f,
					groupMetres: 4f, groupSize: 6f, sink: new Vector2(0.04f, 0.08f)),
				// Dog's mercury: the ancient-woodland carpet of upright leafy stems.
				D("DogsMercury", DetailKind.Shrub, 6, 0.3f, 0.12f, 0.25f, "#2e5a20", "#3e6a28", snowBury: 0.6f,
					variant: FloraVariant.ShrubHerb, groupMetres: 6f, groupSize: 12f, sink: new Vector2(0.02f, 0.05f)),
				// Pincushion moss (Leucobryum): pale grey-green domes on roots and boulders.
				D("CushionMossPincushion", DetailKind.Cushion, 60, 0.08f, 0.09f, 0.25f, "#7a9a50", "#a0b868", snowBury: 1f,
					variant: FloraVariant.CushionMoss, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.01f, 0.02f)),
				// Forest ivy carpeting the floor under beech, darker than the hedgerow's.
				D("IvyForest", DetailKind.Creeper, 100, 0.05f, 0.13f, 0.7f, "#142e12", "#22401a", accents: A("#3a3424"), snowBury: 1f,
					variant: FloraVariant.CreeperIvy, groupMetres: 5f, groupSize: 10f, sink: new Vector2(0.005f, 0.015f)),
				// Serpent roots: grey surface roots of the old beeches, mossed on top. LATER (I9b): serpentine 2–4 m roots of their
				// own form; low root knobs stand in (a tint on the knobs' bark).
				D("SerpentRoots", DetailKind.RootKnobs, 4, 0.18f, 0f, 1.5f, "#8a8478", "#b0aa9a", snowBury: 0.6f,
					variant: FloraVariant.CypressKnees, groupMetres: 5f, groupSize: 4f, sink: new Vector2(0.03f, 0.06f)),
				// Beech mast: copper beech and tan oak leaves, husks and acorns (Woodland keeps the plain oak litter).
				D("DebrisBeechMast", DetailKind.Debris, 10, 0f, 0.25f, 0.6f, "#7a4220", "#8a6a30", snowBury: 1f,
					groupMetres: 6f, groupSize: 12f, sink: new Vector2(0f, 0.01f)),
				// Moss-sleeved fallen branches (a tint on the branch's bark).
				D("DebrisBranchMossy", DetailKind.DebrisBranch, 3, 1.6f, 0.035f, 0.6f, "#b8c8a0", "#d0d8b8", snowBury: 1f,
					groupMetres: 6f, groupSize: 5f, sink: new Vector2(0f, 0.02f)),
				// Ceps and tawny grisettes with a few amethyst deceivers (not Taiga's fly agaric).
				D("MushroomCep", DetailKind.Mushroom, 5, 0.15f, 0.06f, 0.35f, "#7a4a24", "#b08a50", accents: A("#e8dcc0", "#d8ccb0", "#7a4a8a", "#c8b490"), snowBury: 1f,
					variant: FloraVariant.MushroomForest, groupMetres: 4f, groupSize: 4f, sink: new Vector2(0.005f, 0.015f)),
				// Porcelain fungus: glossy translucent white caps on beech logs.
				D("MushroomPorcelain", DetailKind.Mushroom, 9, 0.06f, 0.03f, 0.2f, "#f2f0ea", "#e8e6de", accents: A("#f6f4ee", "#ece8e0"), snowBury: 1f,
					variant: FloraVariant.MushroomCave, groupMetres: 3f, groupSize: 4f, sink: new Vector2(0.005f, 0.015f)),
				// Foxfire: honey fungus on rotten wood, its mycelium glowing at night. LATER (I9b): night emission on a land detail;
				// pale greenish honey-fungus clusters stand in.
				D("MushroomFoxfire", DetailKind.Mushroom, 10, 0.08f, 0.03f, 0.15f, "#a89a5a", "#9aa860", accents: A("#c8e0a0", "#a8c890"), snowBury: 1f,
					variant: FloraVariant.MushroomCluster, groupMetres: 3f, groupSize: 4f, sink: new Vector2(0.005f, 0.015f)),
				// Butterbur: round leaves 0.7 m across along the streams. LATER (I9b): its pink flower spikes.
				D("Butterbur", DetailKind.BroadHerb, 6, 1f, 0.5f, 0.15f, "#3a6a2a", "#5a8a34", segments: 3, snowBury: 0.2f,
					groupMetres: 5f, groupSize: 6f, sink: new Vector2(0.03f, 0.06f)),
				// Wood betony, a herb grove's: leafy stems under purple flower spikes.
				D("HerbWoodBetony", DetailKind.Shrub, 6, 0.5f, 0.12f, 0.25f, "#3e6a28", "#557f30", accents: A("#9a4a8a"), snowBury: 0.4f,
					variant: FloraVariant.ShrubHerb, groupMetres: 4f, groupSize: 6f, sink: new Vector2(0.02f, 0.05f)),
				// The assarts' crops: oats in open drooping panicles, bearded spelt, field beans in white and black flower.
				D("GrassOats", DetailKind.Grass, 10, 1f, 0.02f, 0.2f, "#7a9a6a", "#c8b070", lean: 0.2f, accents: A("#d8c890"), snowBury: 0.4f,
					healthy: paleHealthy, dry: paleDry, variant: FloraVariant.GrassPlume, groupMetres: 8f, groupSize: 20f, sink: new Vector2(0.02f, 0.04f)),
				D("GrassSpelt", DetailKind.Grass, 10, 1.2f, 0.02f, 0.2f, "#a08a48", "#b88a48", lean: 0.15f, accents: A("#c09050"), snowBury: 0.4f,
					healthy: paleHealthy, dry: paleDry, variant: FloraVariant.GrassEar, groupMetres: 8f, groupSize: 20f, sink: new Vector2(0.02f, 0.04f)),
				D("FlowersFieldBean", DetailKind.Flowers, 6, 0.9f, 0.06f, 0.3f, "#5a7a5a", "#7a9a70", accents: A("#f2f0ea", "#2a2626"), snowBury: 0.4f,
					groupMetres: 8f, groupSize: 20f, sink: new Vector2(0.01f, 0.03f)),

				// ── Woodland (biome round, 2026-10-26): the managed lowland wood's own plants ──
				// Grazed red fescue and common bent: short downland turf, purple-brown panicles.
				D("GrassPasture", DetailKind.Grass, 16, 0.12f, 0.02f, 0.4f, "#3e5a24", "#7a9a44", lean: 0.3f,
					groupMetres: 4f, groupSize: 10f, sink: new Vector2(0.02f, 0.04f)),
				// Yorkshire fog (Holcus lanatus): soft grey-green velvet, pink-grey heads.
				D("GrassYorkshireFog", DetailKind.Grass, 14, 0.5f, 0.025f, 0.3f, "#6a7a5a", "#9aa880", lean: 0.4f, accents: A("#c8a0a8"),
					variant: FloraVariant.GrassPlume, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.02f, 0.04f)),
				// False brome (Brachypodium sylvaticum), the coppice grass: drooping yellow-green blades, nodding spikes.
				D("GrassFalseBrome", DetailKind.Grass, 14, 0.45f, 0.025f, 0.3f, "#5a7a2a", "#a0b044", lean: 0.6f, accents: A("#8a9a4a"),
					variant: FloraVariant.GrassEar, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.02f, 0.04f)),
				// Tufted hair-grass (Deschampsia cespitosa): damp rides' tussocks under a silver-purple haze.
				D("GrassHairgrass", DetailKind.Grass, 16, 0.6f, 0.012f, 0.3f, "#3e5a24", "#7a8a4a", lean: 0.4f, accents: A("#a89aa8"),
					variant: FloraVariant.GrassPlume, groupMetres: 5f, groupSize: 8f, sink: new Vector2(0.02f, 0.04f)),
				// Woodland's own rye (Taiga grows its own look of rye).
				D("GrassRye", DetailKind.Grass, 10, 1.4f, 0.02f, 0.2f, "#8a9a5a", "#c8b878", lean: 0.15f, accents: A("#c8b070"), snowBury: 0.4f,
					healthy: paleHealthy, dry: paleDry, variant: FloraVariant.GrassEar, groupMetres: 8f, groupSize: 20f, sink: new Vector2(0.02f, 0.04f)),
				// Bluebells (Hyacinthoides non-scripta): one-sided nodding blue-violet bells, the odd white.
				D("FlowersBluebell", DetailKind.Flowers, 10, 0.35f, 0.03f, 0.35f, "#2e5a22", "#4a7a2c", accents: A("#4a4ac0", "#5a5ad0", "#6a5ac8", "#f0f0f4"),
					groupMetres: 6f, groupSize: 20f, sink: new Vector2(0.01f, 0.02f)),
				// The spring coppice set: primrose, dog violet, early-purple orchid, stitchwort.
				D("FlowersPrimrose", DetailKind.Flowers, 7, 0.12f, 0.04f, 0.3f, "#3e6a28", "#557f30", accents: A("#f2e890", "#8a70c8", "#a04a9a", "#f6f6f2"),
					groupMetres: 3f, groupSize: 10f, sink: new Vector2(0.01f, 0.02f)),
				// Foxgloves, following the cut.
				D("FlowersFoxglove", DetailKind.Flowers, 4, 1.4f, 0.08f, 0.35f, "#3e6a2a", "#5a8034", accents: A("#c0508a", "#d070a0", "#e8c0d8", "#f4f0ee"), snowBury: 0.3f,
					groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.01f, 0.03f)),
				// The glades: red campion, bugle, St John's wort, ox-eye.
				D("FlowersGlade", DetailKind.Flowers, 6, 0.45f, 0.05f, 0.35f, "#43622a", "#6a8a3a", accents: A("#d04a7a", "#4a5ac0", "#f0c020", "#f6f4ee"),
					groupMetres: 4f, groupSize: 10f, sink: new Vector2(0.01f, 0.02f)),
				// Hedgerow lace: cow parsley, hogweed, white dead-nettle, wild marjoram.
				D("FlowersHedgerow", DetailKind.Flowers, 5, 1f, 0.1f, 0.4f, "#4a6e2c", "#64843a", accents: A("#f4f2e8", "#f0ece0", "#f2f2ec", "#c080a8"), snowBury: 0.3f,
					groupMetres: 4f, groupSize: 10f, sink: new Vector2(0.01f, 0.03f)),
				// The assarts' crops: buckwheat, woad (the Picardy dye crop), field peas.
				D("FlowersBuckwheat", DetailKind.Flowers, 6, 0.7f, 0.06f, 0.3f, "#4a7a2c", "#a04a3a", accents: A("#f0dce0", "#e8c8d0"), snowBury: 0.4f,
					groupMetres: 8f, groupSize: 20f, sink: new Vector2(0.01f, 0.03f)),
				D("FlowersWoad", DetailKind.Flowers, 5, 1f, 0.08f, 0.35f, "#5a7a6a", "#6a8a7a", accents: A("#f0d830", "#e8c820"), snowBury: 0.4f,
					groupMetres: 8f, groupSize: 20f, sink: new Vector2(0.01f, 0.03f)),
				D("FlowersFieldPea", DetailKind.Flowers, 6, 0.6f, 0.06f, 0.3f, "#6a8a5a", "#7a9a6a", accents: A("#e8e0f0", "#8a4a9a"), snowBury: 0.4f,
					groupMetres: 8f, groupSize: 20f, sink: new Vector2(0.01f, 0.03f)),
				// Woodland bracken: taller, russet-tipped.
				D("FernBrackenWoodland", DetailKind.Fern, 5, 1.6f, 0.6f, 0.3f, "#4a7a2c", "#6a8a34", segments: 4, snowBury: 0.3f,
					variant: FloraVariant.FernBracken, groupMetres: 6f, groupSize: 10f, sink: new Vector2(0.04f, 0.08f)),
				// Nettle beds and burdock at settlements, warrens and coupes.
				D("HerbNettle", DetailKind.Shrub, 6, 0.8f, 0.16f, 0.3f, "#2e4a22", "#3e5e2a", snowBury: 0.4f,
					variant: FloraVariant.ShrubHerb, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.03f, 0.06f)),
				// Plain oak, yellow hazel and chestnut leaf litter, spiny husks and acorn cups.
				D("DebrisOakLeaf", DetailKind.Debris, 10, 0f, 0.25f, 0.6f, "#8a6a30", "#c0a048", snowBury: 1f,
					groupMetres: 6f, groupSize: 12f, sink: new Vector2(0f, 0.01f)),
				// Coppice brash: cut rods and pale chips (a pale tint on the branch's bark).
				D("DebrisCoppice", DetailKind.DebrisBranch, 4, 1.2f, 0.025f, 0.7f, "#e8d8b8", "#f0e4c8", snowBury: 1f,
					groupMetres: 5f, groupSize: 6f, sink: new Vector2(0f, 0.02f)),
				// Parasol mushrooms: shaggy flat caps on snakeskin stems.
				D("MushroomParasol", DetailKind.Mushroom, 3, 0.3f, 0.1f, 0.3f, "#d8c8a8", "#c8b898", accents: A("#7a5a3a", "#e8dcc8"), snowBury: 1f,
					variant: FloraVariant.MushroomForest, groupMetres: 5f, groupSize: 3f, sink: new Vector2(0.005f, 0.015f)),
				// Chanterelles: golden trumpets in little clusters.
				D("MushroomChanterelle", DetailKind.Mushroom, 6, 0.07f, 0.03f, 0.15f, "#e8b030", "#d8a028", accents: A("#f0c048", "#e0a830"), snowBury: 1f,
					variant: FloraVariant.MushroomCluster, groupMetres: 4f, groupSize: 4f, sink: new Vector2(0.005f, 0.015f)),
				// Wood spurge: dark leaves under lime-green bracts in the sunlit coppice.
				D("WoodSpurge", DetailKind.Shrub, 6, 0.5f, 0.12f, 0.25f, "#3a5a2a", "#4a6a32", accents: A("#c8d850"), snowBury: 0.4f,
					variant: FloraVariant.ShrubHerb, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.02f, 0.05f)),
				// Lords-and-ladies (Arum): arrow leaves at the hedge feet. LATER (I9b): its red berry spike.
				D("LordsAndLadies", DetailKind.BroadHerb, 5, 0.3f, 0.2f, 0.1f, "#2e5a22", "#3e6a2a", segments: 3, snowBury: 0.5f,
					groupMetres: 3f, groupSize: 4f, sink: new Vector2(0.02f, 0.04f)),
				// Herb grove herbs: agrimony's slender yellow spikes, wild marjoram's pink-purple heads on the chalk.
				D("HerbAgrimony", DetailKind.Shrub, 6, 0.6f, 0.1f, 0.25f, "#3e6a28", "#4e7a30", accents: A("#e8c020"), snowBury: 0.4f,
					variant: FloraVariant.ShrubHerb, groupMetres: 4f, groupSize: 6f, sink: new Vector2(0.02f, 0.05f)),
				D("HerbWildMarjoram", DetailKind.Shrub, 7, 0.4f, 0.1f, 0.25f, "#4a6a30", "#5a7a38", accents: A("#c080a8"), snowBury: 0.4f,
					variant: FloraVariant.ShrubHerb, groupMetres: 4f, groupSize: 6f, sink: new Vector2(0.02f, 0.05f)),
				// ── Tundra (biome round, 2026-10-26): the treeless arctic's own plants, on the existing kinds ──
				// Pendant grass (Arctophila fulva): orange-red stems and nodding plumes in the polygon troughs and thaw-lake shallows.
				D("GrassPendant", DetailKind.Grass, 12, 0.6f, 0.008f, 0.2f, "#4a6a3a", "#c06a2a", lean: 0.5f, accents: A("#a05a30"),
					variant: FloraVariant.GrassPlume, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.02f, 0.04f)),
				// Arctic wood-rush (Luzula confusa / arctica): low bronze tufts, black-brown clustered heads on the fell-field.
				D("GrassWoodrushArctic", DetailKind.Grass, 10, 0.15f, 0.005f, 0.12f, "#4a4a2a", "#7a6a3a", lean: 0.3f, accents: A("#2a1e18"),
					variant: FloraVariant.GrassRush, groupMetres: 3f, groupSize: 6f, sink: new Vector2(0.01f, 0.03f)),
				// Arctic poppy, mountain avens, purple saxifrage, woolly lousewort: tiny heads on short stiff stems.
				D("FlowersArctic", DetailKind.Flowers, 7, 0.14f, 0.04f, 0.25f, "#4a6a2c", "#5e7a34", accents: A("#f4e04a", "#f6f4ec", "#b04a9a", "#e08ab0"),
					snowBury: 1f, groupMetres: 3f, groupSize: 10f, sink: new Vector2(0.01f, 0.02f)),
				// River beauty (Chamerion latifolium, dwarf fireweed): big magenta flowers over glaucous leaves on the river gravels.
				D("FlowersRiverBeauty", DetailKind.Flowers, 6, 0.3f, 0.08f, 0.3f, "#6a8a7a", "#7a9a8a", accents: A("#c83a8a", "#b02a78"),
					snowBury: 0.8f, groupMetres: 3f, groupSize: 10f, sink: new Vector2(0.01f, 0.03f)),
				// Mountain avens (Dryas octopetala): crinkled dark leaf mats under white eight-petalled flowers and twisted seed plumes.
				D("CushionDryas", DetailKind.Cushion, 60, 0.06f, 0.07f, 0.25f, "#2e4a26", "#4a6a34", accents: A("#f6f4ec", "#e0dcd0"), snowBury: 1f,
					variant: FloraVariant.CushionMossCampion, groupMetres: 5f, groupSize: 8f, sink: new Vector2(0.01f, 0.02f)),
				// Purple saxifrage (Saxifraga oppositifolia): a tight green cushion, magenta flowers on most of it.
				D("CushionSaxifrage", DetailKind.Cushion, 64, 0.04f, 0.06f, 0.15f, "#3a5a34", "#4a6a3e", accents: A("#b04a9a", "#c060a8"), snowBury: 1f,
					variant: FloraVariant.CushionMossCampion, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.01f, 0.02f)),
				// Hair-cap moss (Polytrichum): dark star shoots on the frost-heaved rims, rusty capsules on red setae.
				D("CushionHairCap", DetailKind.Cushion, 72, 0.06f, 0.08f, 0.2f, "#2e4a1e", "#3e5a26", accents: A("#8a4a24"), snowBury: 1f,
					variant: FloraVariant.CushionThrift, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.01f, 0.02f)),
				// Snow lichen and Iceland moss (Flavocetraria): crisp cream-yellow lobes, brown at the base (not Taiga's reindeer lichen).
				D("LichenSnow", DetailKind.Lichen, 2, 0.06f, 0.006f, 0.15f, "#d8d098", "#e4dcaa", accents: A("#8a6a3a"), snowBury: 1f,
					variant: FloraVariant.LichenClump, groupMetres: 6f, groupSize: 12f, sink: new Vector2(0.005f, 0.015f)),
				// Rock tripe (Umbilicaria): leathery black-brown lobes lying flat on the stone.
				D("LichenRockTripe", DetailKind.Lichen, 2, 0.02f, 0.006f, 0.12f, "#2a2420", "#4a3a30", accents: A("#3a302a"), snowBury: 1f,
					variant: FloraVariant.LichenLava, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.005f, 0.015f)),
				// Roseroot (Rhodiola rosea): a glaucous succulent rosette with a yellow-and-red flower head, at the outcrop's foot.
				D("Roseroot", DetailKind.Rosette, 14, 0.25f, 0.05f, 0.08f, "#6a8a7a", "#7e9a8a", segments: 3, accents: A("#e0c040", "#a0302a"), snowBury: 0.6f,
					variant: FloraVariant.RosetteBromeliad, groupMetres: 3f, groupSize: 4f, sink: new Vector2(0.02f, 0.04f)),
				// Rough-stalk boletes (Leccinum) standing above the dwarf-birch mat: brown caps, scaly grey-white stems.
				D("MushroomRoughStalk", DetailKind.Mushroom, 4, 0.12f, 0.05f, 0.25f, "#8a5a30", "#a06a3a", accents: A("#e0dcd0", "#c8c4bc", "#3a3028", "#d8ccb0"),
					snowBury: 1f, variant: FloraVariant.MushroomForest, groupMetres: 4f, groupSize: 3f, sink: new Vector2(0.005f, 0.015f)),
				// Shed caribou antlers and bleached bones (a bone tint on the branch's bark). LATER (I9b): antler, rib and skull meshes of their own.
				D("DebrisAntler", DetailKind.DebrisBranch, 3, 1f, 0.03f, 0.5f, "#e8e2d4", "#c8bca8", snowBury: 1f,
					groupMetres: 6f, groupSize: 3f, sink: new Vector2(0f, 0.02f)),

				// ── Ice Sheet (biome round, 2026-10-26): the nunataks' lichens and the petrels' stone-oil, the only life on the cap ──
				// Stone-oil (mumiyo): black-amber waxy crusts and dribbles laid down by ages of petrels, white feathers in it.
				D("LichenStoneOil", DetailKind.Lichen, 2, 0.05f, 0.01f, 0.15f, "#1e140c", "#4a2a10", accents: A("#ece8dc"), snowBury: 1f,
					variant: FloraVariant.LichenLava, groupMetres: 3f, groupSize: 8f, sink: new Vector2(0.005f, 0.015f)),
				// Buellia frigida: grey-white crust rosettes with black margins and fruit dots.
				D("LichenFrigid", DetailKind.Lichen, 2, 0.02f, 0.005f, 0.1f, "#c8c8c0", "#dcdcd4", accents: A("#1a1a1a"), snowBury: 1f,
					variant: FloraVariant.LichenLava, groupMetres: 4f, groupSize: 10f, sink: new Vector2(0.005f, 0.015f)),
				// Usnea sphacelata: yellow-grey shrublets a few centimetres high, black-tipped, on the windward faces.
				D("LichenBlacktuft", DetailKind.Lichen, 2, 0.04f, 0.005f, 0.1f, "#b8b89a", "#c8c8a8", accents: A("#141414"), snowBury: 1f,
					variant: FloraVariant.LichenClump, groupMetres: 3f, groupSize: 8f, sink: new Vector2(0.005f, 0.015f)),
				// Pleopsidium chlorophanum: chartreuse crusts in the crevices and under the perches, the cap's only colour.
				D("LichenSulphur", DetailKind.Lichen, 2, 0.015f, 0.005f, 0.1f, "#c8d020", "#d8e030", accents: A("#a8b018"), snowBury: 1f,
					variant: FloraVariant.LichenLava, groupMetres: 3f, groupSize: 6f, sink: new Vector2(0.005f, 0.015f)),
				// Bryum seep moss: green-black cushions with silver tips, fed by guano on sun-warmed rock.
				D("CushionMossSeep", DetailKind.Cushion, 72, 0.08f, 0.09f, 0.2f, "#3a4a2a", "#4a5a32", accents: A("#b8c0b0"), snowBury: 1f,
					variant: FloraVariant.CushionMoss, groupMetres: 3f, groupSize: 6f, sink: new Vector2(0.01f, 0.02f)),

				// ── Glacier (biome round, 2026-10-26): the forefield's pioneers, the moraine herbs and the ice's own life ──
				// Spike trisetum (Trisetum spicatum): stiff tufts with shining silver-purple spikes, the moraine's first grass.
				D("GrassTrisetum", DetailKind.Grass, 12, 0.25f, 0.004f, 0.15f, "#4a5a3a", "#8a9a6a", lean: 0.25f, accents: A("#9a8aa8"),
					variant: FloraVariant.GrassPlume, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.02f, 0.04f)),
				// Black-headed snowmelt sedge: low tufts with chestnut-black spikes beside the meltwater.
				D("GrassMeltSedge", DetailKind.Grass, 14, 0.15f, 0.004f, 0.15f, "#3e4a2a", "#6a7a42", lean: 0.4f, accents: A("#3a2a22"),
					variant: FloraVariant.GrassRush, groupMetres: 3f, groupSize: 8f, sink: new Vector2(0.02f, 0.04f)),
				// Snowmelt primulas (Primula rosea) and marsh marigold: vivid rose and gold cups beside the melt.
				D("FlowersSnowmeltPrimula", DetailKind.Flowers, 7, 0.12f, 0.05f, 0.25f, "#3a5a2a", "#4e6e34", accents: A("#e04a8a", "#f0c020"),
					snowBury: 1f, groupMetres: 3f, groupSize: 10f, sink: new Vector2(0.01f, 0.02f)),
				// Himalayan rhubarb (Rheum webbianum): crinkled round leaves on red stalks, a cream panicle. LATER (I9b): the panicle.
				D("RhubarbHimalayan", DetailKind.BroadHerb, 5, 0.8f, 0.45f, 0.2f, "#4a6a2a", "#5e7e34", segments: 3, accents: A("#a02a2a"), snowBury: 0.4f,
					groupMetres: 5f, groupSize: 4f, sink: new Vector2(0.03f, 0.06f)),
				// Dhoop (Jurinea): a flat silver rosette with a stemless purple thistle head, the incense root.
				D("HerbDhoop", DetailKind.Rosette, 14, 0.1f, 0.06f, 0.1f, "#a8b0a0", "#c0c6b8", segments: 3, accents: A("#8a3a8a"), snowBury: 0.8f,
					variant: FloraVariant.RosetteBromeliad, groupMetres: 3f, groupSize: 4f, sink: new Vector2(0.01f, 0.03f)),
				// Bergenia, the stone-breaker: leathery glossy round leaves, green and wine, pink heads in the boulders' cracks.
				D("BergeniaStone", DetailKind.Rosette, 10, 0.3f, 0.09f, 0.1f, "#3a5a2a", "#6a1a2a", segments: 3, accents: A("#e8a0b8"), snowBury: 0.6f,
					variant: FloraVariant.RosetteYucca, groupMetres: 3f, groupSize: 4f, sink: new Vector2(0.02f, 0.04f)),
				// Glacier mice: loose ovoid moss balls that roll on the debris-covered ice.
				D("CushionGlacierMice", DetailKind.Cushion, 40, 0.08f, 0.08f, 0.3f, "#5a5a2a", "#7a6a3a", snowBury: 1f,
					variant: FloraVariant.CushionMoss, groupMetres: 4f, groupSize: 6f, sink: new Vector2(0.005f, 0.01f)),
				// Map lichen (Rhizocarpon): chartreuse areolae bounded by black lines, the lichen that dates the moraines.
				D("LichenMap", DetailKind.Lichen, 2, 0.015f, 0.005f, 0.12f, "#b8c838", "#c8d448", accents: A("#1a1a1a"), snowBury: 1f,
					variant: FloraVariant.LichenLava, groupMetres: 4f, groupSize: 10f, sink: new Vector2(0.005f, 0.015f)),
				// Cryoconite holes: black-brown sludge under azure water in the bare ice. LATER (I9): a flat colliderless decal of its own;
				// a flat dark crust with an azure accent stands in.
				D("LichenCryoconite", DetailKind.Lichen, 2, 0.01f, 0.01f, 0.2f, "#2a2420", "#3a3028", accents: A("#5ab0e0"), snowBury: 1f,
					variant: FloraVariant.LichenLava, groupMetres: 5f, groupSize: 10f, sink: new Vector2(0.005f, 0.015f)),
				// The ice's litter: rock-flour flakes, striated chips, sheep bone, the odd decayed marigold garland near a shrine.
				D("DebrisGlacier", DetailKind.DebrisNeedle, 12, 0f, 0.2f, 0.5f, "#8a8a84", "#c4ccc0", accents: A("#d8d0c0", "#c87a1a", "#3a3a3a"), snowBury: 1f,
					groupMetres: 5f, groupSize: 8f, sink: new Vector2(0f, 0.01f)),

				// ── Alpine Meadow (biome round, 2026-10-26): the Carpathian high pastures' own plants, on the existing kinds ──
				// Common and rock bent (Agrostis): fine blades under an airy purple cloud of panicles, the polonyna's mauve haze.
				D("GrassBent", DetailKind.Grass, 20, 0.25f, 0.0025f, 0.25f, "#4a5a2a", "#8a9a50", lean: 0.35f, accents: A("#8a5a7a"),
					healthy: H("#e8ecd8"), dry: H("#c8b48a"), variant: FloraVariant.GrassPlume, groupMetres: 4f, groupSize: 10f, sink: new Vector2(0.02f, 0.04f)),
				// Alpine sweet vernal grass: dense gold-brown spikes, the smell of the hay.
				D("GrassSweetVernal", DetailKind.Grass, 14, 0.3f, 0.004f, 0.25f, "#5a6a30", "#a8a050", lean: 0.3f, accents: A("#b8903a"),
					healthy: H("#f0ead0"), dry: H("#d8c080"), variant: FloraVariant.GrassEar, groupMetres: 4f, groupSize: 10f, sink: new Vector2(0.02f, 0.04f)),
				// Three-leaved rush (Juncus trifidus): wiry rust-brown tufts of the wind ridges, tiny dark heads.
				D("GrassRushTrifid", DetailKind.Grass, 12, 0.18f, 0.0015f, 0.15f, "#5a4a2a", "#a86a3a", lean: 0.2f, accents: A("#3a2a1e"),
					dry: H("#9a5a30"), variant: FloraVariant.GrassRush, groupMetres: 3f, groupSize: 8f, sink: new Vector2(0.02f, 0.04f)),
				// Blue moor-grass (Sesleria): glaucous tufts with ovoid blue-violet heads on the limestone.
				D("GrassBlueMoor", DetailKind.Grass, 14, 0.3f, 0.004f, 0.2f, "#4a5a50", "#8a9a90", lean: 0.3f, accents: A("#5a5a8a"),
					healthy: H("#e0e8e8"), dry: H("#c0b8a0"), variant: FloraVariant.GrassEar, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.02f, 0.04f)),
				// The polonyna's flowers: arnica gold, bellflower blue, rampion violet, white spignel umbels.
				D("FlowersPolonyna", DetailKind.Flowers, 8, 0.3f, 0.05f, 0.35f, "#4a6a2a", "#66843a", accents: A("#f0a020", "#5a6ad0", "#6a3aa0", "#f4f2e8"),
					groupMetres: 4f, groupSize: 14f, sink: new Vector2(0.01f, 0.02f)),
				// Crocus at the snow's edge, violet and white, with fringed snowbells and least primrose.
				D("FlowersMeltCrocus", DetailKind.Flowers, 10, 0.1f, 0.04f, 0.3f, "#4a6a2a", "#5e7e34", accents: A("#7a4ac0", "#f4f0f8", "#9a80d8", "#e070a8"),
					snowBury: 1f, groupMetres: 4f, groupSize: 16f, sink: new Vector2(0.01f, 0.02f)),
				// Tall herbs of the springs and streams: martagon lily, monkshood, willow gentian, golden telekia.
				D("FlowersTallHerb", DetailKind.Flowers, 6, 1.1f, 0.08f, 0.35f, "#3e6a28", "#5a8a34", accents: A("#c06a8a", "#3a3a9a", "#2a4ab0", "#f0b020"),
					snowBury: 0.3f, groupMetres: 5f, groupSize: 10f, sink: new Vector2(0.01f, 0.03f)),
				// The crag flowers of the limestone: edelweiss felt stars, pinks, whitlow-grass, white pasque.
				D("FlowersCrag", DetailKind.Flowers, 8, 0.12f, 0.04f, 0.25f, "#5a6a4a", "#8a9a7a", accents: A("#ecece4", "#e05a90", "#f0d030", "#f4f2ea"),
					groupMetres: 3f, groupSize: 10f, sink: new Vector2(0.01f, 0.02f)),
				// The village fields: mountain oats with nodding spikelets, blue flax, uncut hay meadow.
				D("GrassMountainOats", DetailKind.Grass, 10, 0.9f, 0.006f, 0.2f, "#5a7a5a", "#c8c090", lean: 0.2f, accents: A("#d8c890"), snowBury: 0.4f,
					healthy: paleHealthy, dry: paleDry, variant: FloraVariant.GrassPlume, groupMetres: 8f, groupSize: 20f, sink: new Vector2(0.02f, 0.04f)),
				D("FlowersFlax", DetailKind.Flowers, 8, 0.7f, 0.03f, 0.25f, "#5a7a4a", "#7a9a5a", accents: A("#6a8ae0", "#b89a5a"), snowBury: 0.4f,
					groupMetres: 8f, groupSize: 20f, sink: new Vector2(0.01f, 0.03f)),
				D("GrassHayMeadow", DetailKind.Grass, 16, 0.7f, 0.005f, 0.25f, "#4e6a2c", "#a8a860", lean: 0.35f, accents: A("#a88a5a"), snowBury: 0.4f,
					variant: FloraVariant.GrassPlume, groupMetres: 8f, groupSize: 20f, sink: new Vector2(0.02f, 0.04f)),
				// White false hellebore (Veratrum): pleated broad leaves spiralled up a tall stem under a greenish-white panicle, the
				// pastures' ungrazed (poisonous) signature.
				D("Veratrum", DetailKind.Shrub, 5, 1.2f, 0.2f, 0.2f, "#4a7a3a", "#6a9a4a", accents: A("#d8e0b8"), snowBury: 0.3f,
					variant: FloraVariant.ShrubHerb, groupMetres: 5f, groupSize: 6f, sink: new Vector2(0.03f, 0.06f)),
				// Alpine dock: heart leaves half a metre across under rust-red seed spikes, the ring round every fold.
				D("DockAlpine", DetailKind.BroadHerb, 5, 0.9f, 0.5f, 0.2f, "#3e6a2a", "#4e7a34", segments: 3, accents: A("#8a3a24"), snowBury: 0.3f,
					groupMetres: 6f, groupSize: 10f, sink: new Vector2(0.03f, 0.06f)),
				// Yellow gentian: tall stems, paired glaucous ribbed leaves, whorls of gold stars.
				D("GentianYellow", DetailKind.Shrub, 3, 1.2f, 0.18f, 0.15f, "#5a8a5a", "#6a9a6a", accents: A("#e8c020"), snowBury: 0.3f,
					variant: FloraVariant.ShrubHerb, groupMetres: 4f, groupSize: 4f, sink: new Vector2(0.03f, 0.06f)),
				// Carline thistle: a stemless silver star round a gold-brown disc on a spiny rosette, the weather charm on the doors.
				D("CarlineThistle", DetailKind.Rosette, 16, 0.12f, 0.05f, 0.12f, "#4a5a3a", "#5a6a44", segments: 3, accents: A("#e8e4d8", "#b08a40"), snowBury: 0.8f,
					variant: FloraVariant.RosetteBromeliad, groupMetres: 4f, groupSize: 4f, sink: new Vector2(0.01f, 0.03f)),
				// Arnica: rosettes under orange-gold heads (a herb grove's and a gatherer's).
				D("HerbArnica", DetailKind.Shrub, 6, 0.4f, 0.1f, 0.15f, "#4a6a2a", "#5e7a34", accents: A("#f0a020"), snowBury: 0.5f,
					variant: FloraVariant.ShrubHerb, groupMetres: 4f, groupSize: 6f, sink: new Vector2(0.02f, 0.04f)),
				// Spignel (Meum): feathery aromatic tufts under white umbels, the sheep pasture's herb.
				D("HerbMeum", DetailKind.Shrub, 7, 0.3f, 0.1f, 0.2f, "#4a7a3a", "#5e8a44", accents: A("#f4f2e8"), snowBury: 0.5f,
					variant: FloraVariant.ShrubHerb, groupMetres: 4f, groupSize: 6f, sink: new Vector2(0.02f, 0.04f)),
				// Iceland moss (Cetraria): olive-brown curled ribbons with white margins, famine bread and cough medicine.
				D("LichenIceland", DetailKind.Lichen, 2, 0.06f, 0.006f, 0.15f, "#7a6a3a", "#a89a6a", accents: A("#e8e4d8"), snowBury: 1f,
					variant: FloraVariant.LichenClump, groupMetres: 5f, groupSize: 10f, sink: new Vector2(0.005f, 0.015f)),
				// The springs' bright gold-green moss cushions.
				D("MossFlush", DetailKind.Cushion, 60, 0.08f, 0.09f, 0.3f, "#8ab83a", "#6a8a2a", snowBury: 1f,
					variant: FloraVariant.CushionMoss, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.01f, 0.02f)),
				// Fairy-ring champignons: tan caps on a dark-green ring band (rings from the FairyRings scatter mode).
				D("FairyRing", DetailKind.Mushroom, 8, 0.06f, 0.03f, 0.2f, "#c8a878", "#b8986a", accents: A("#e8dcc0", "#2e4a1a"), snowBury: 1f,
					variant: FloraVariant.MushroomCluster, groupMetres: 3f, groupSize: 6f, sink: new Vector2(0.005f, 0.015f)),
				// A marmot burrow's spoil: grey stone fan and flattened turf. LATER (I9b): a burrow-mouth mound of its own; flat spoil stands in.
				D("DebrisMarmotSpoil", DetailKind.DebrisNeedle, 10, 0f, 0.3f, 0.6f, "#8a8a80", "#6a6c5e", accents: A("#9a9a90", "#4a5a2a"), snowBury: 1f,
					groupMetres: 5f, groupSize: 4f, sink: new Vector2(0f, 0.01f)),
				// The litter of a grazed summer: wool on twigs, sheep and cow dung, hay wisps, chewed stone-pine cone scales.
				D("DebrisPasture", DetailKind.DebrisNeedle, 12, 0f, 0.2f, 0.5f, "#e8e0d0", "#2a2218", accents: A("#3a3020", "#c8b878", "#6a3a24"), snowBury: 1f,
					groupMetres: 6f, groupSize: 10f, sink: new Vector2(0f, 0.01f)),

				// ── Mountain Slope (biome round, 2026-10-26): the Afromontane cloud forest's own plants, on the existing kinds ──
				// Kikuyu grass: the highland pasture's dense low mat.
				D("GrassKikuyu", DetailKind.Grass, 18, 0.12f, 0.005f, 0.3f, "#3a5a24", "#6a9a3a", lean: 0.3f, healthy: H("#e8f0d8"), dry: H("#c8c08a"),
					groupMetres: 4f, groupSize: 10f, sink: new Vector2(0.02f, 0.04f)),
				// Basket grass of the forest floor: broad soft shade blades.
				D("GrassBasket", DetailKind.Grass, 12, 0.18f, 0.009f, 0.25f, "#2e4a1e", "#5a8a32", lean: 0.5f,
					groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.02f, 0.04f)),
				// Frost-moor tussock fescue: rolled straw blades pulled into tussocks, sparse panicles.
				D("GrassAfroTussock", DetailKind.Grass, 18, 0.7f, 0.003f, 0.25f, "#5a5a32", "#b0a070", lean: 0.4f, accents: A("#a89a7a"),
					healthy: H("#e8e4cc"), dry: H("#c8b48a"), variant: FloraVariant.GrassPlume, groupMetres: 5f, groupSize: 6f, sink: new Vector2(0.02f, 0.05f)),
				// Meskel daisies (Bidens): the gold carpet after the rains.
				D("FlowersMeskel", DetailKind.Flowers, 8, 0.7f, 0.05f, 0.35f, "#4a6a2a", "#66843a", accents: A("#f0c018", "#5a3a1a"),
					groupMetres: 5f, groupSize: 14f, sink: new Vector2(0.01f, 0.03f)),
				// Busy lizzies of the wet banks: scarlet, pink and white on succulent stems.
				D("FlowersImpatiens", DetailKind.Flowers, 6, 0.4f, 0.06f, 0.3f, "#4a7a3a", "#5a8a44", accents: A("#d8302a", "#e870a8", "#f4f0f0"),
					groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.01f, 0.03f)),
				// Everlastings (Helichrysum): silver-felt leaves, papery white, rose and straw-gold heads.
				D("FlowersEverlasting", DetailKind.Flowers, 7, 0.35f, 0.05f, 0.3f, "#8a9a88", "#a8b0a0", accents: A("#f4f2ec", "#e0a0b0", "#d8b860"),
					snowBury: 0.6f, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.01f, 0.02f)),
				// Red-hot pokers: strap clumps under metre-tall spikes, scarlet to yellow at the tips.
				D("FlowersTorchLily", DetailKind.Flowers, 4, 1f, 0.1f, 0.25f, "#4a6a3a", "#5a7a44", accents: A("#d8401c", "#f0c020"),
					groupMetres: 4f, groupSize: 5f, sink: new Vector2(0.02f, 0.04f)),
				// The terraces' crops: teff in airy red-brown or ivory clouds, dark-bearded emmer.
				D("GrassTeff", DetailKind.Grass, 14, 0.8f, 0.003f, 0.2f, "#6a8a3a", "#c8b080", lean: 0.3f, accents: A("#a85a3a", "#e8dcc0"), snowBury: 0.4f,
					healthy: paleHealthy, dry: paleDry, variant: FloraVariant.GrassPlume, groupMetres: 8f, groupSize: 20f, sink: new Vector2(0.02f, 0.04f)),
				D("GrassEmmer", DetailKind.Grass, 10, 0.9f, 0.006f, 0.2f, "#5a7a5a", "#a88a5a", lean: 0.15f, accents: A("#4a3a2a"), snowBury: 0.4f,
					healthy: paleHealthy, dry: paleDry, variant: FloraVariant.GrassEar, groupMetres: 8f, groupSize: 20f, sink: new Vector2(0.02f, 0.04f)),
				// Enset, the false banana of the homestead gardens: a fat pseudostem under huge paddle leaves with red midribs.
				D("Enset", DetailKind.BroadHerb, 6, 4f, 1.2f, 0.4f, "#3a6a2a", "#4a7a34", segments: 3, accents: A("#8a2a2a"), snowBury: 0.1f,
					groupMetres: 6f, groupSize: 6f, sink: new Vector2(0.04f, 0.08f)),
				// Giant lobelia: a rosette of straps under a felted spike of blue bracts and grey hair. LATER (I9b): the 3–5 m spike;
				// the tower-of-jewels rosette stands in.
				D("GiantLobelia", DetailKind.Rosette, 18, 1.2f, 0.08f, 0.5f, "#4a6a4a", "#6a8a6a", segments: 3, accents: A("#4a5aa8", "#b8bcc8"), snowBury: 0.2f,
					variant: FloraVariant.RosetteEchium, groupMetres: 8f, groupSize: 3f, sink: new Vector2(0.03f, 0.06f)),
				// Montane shuttlecock ferns of the wet understory.
				D("FernMontane", DetailKind.Fern, 7, 1.4f, 0.45f, 0.03f, "#3e7a2a", "#5a9a38", segments: 4, snowBury: 0.3f,
					groupMetres: 4f, groupSize: 6f, sink: new Vector2(0.04f, 0.08f)),
				// Cloud moss: deep sleeves on logs and boulders, a liverwort accent.
				D("CushionCloudMoss", DetailKind.Cushion, 60, 0.15f, 0.12f, 0.3f, "#2e5a1a", "#6a9a2a", accents: A("#4a7a3a"), snowBury: 1f,
					variant: FloraVariant.CushionMoss, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.01f, 0.02f)),
				// Creeping alchemilla: a low mat of round pleated leaves over the glades and the moor.
				D("HerbAlchemilla", DetailKind.Creeper, 60, 0.08f, 0.1f, 0.5f, "#4a7a3a", "#6a9a4a", snowBury: 1f,
					variant: FloraVariant.CreeperIvy, groupMetres: 5f, groupSize: 10f, sink: new Vector2(0.005f, 0.015f)),
				// Koseret, the butter herb: aromatic grey-green tufts under white flower heads.
				D("HerbKoseret", DetailKind.Shrub, 6, 0.5f, 0.14f, 0.25f, "#6a7a5a", "#7a8a6a", accents: A("#f0ece0"), snowBury: 0.4f,
					variant: FloraVariant.ShrubHerb, groupMetres: 4f, groupSize: 6f, sink: new Vector2(0.02f, 0.05f)),
				// Termite mushrooms: tall pale parasols with dark pointed umbos.
				D("MushroomTermitomyces", DetailKind.Mushroom, 5, 0.25f, 0.08f, 0.35f, "#b0a088", "#c0b098", accents: A("#4a3a2a", "#ece6d8"), snowBury: 1f,
					variant: FloraVariant.MushroomForest, groupMetres: 4f, groupSize: 5f, sink: new Vector2(0.005f, 0.015f)),
				// The cloud forest's litter: podocarp needles, kosso leaflets, fallen beard lichen, mossy twigs, hypericum petals.
				D("DebrisCloudForest", DetailKind.DebrisNeedle, 12, 0f, 0.2f, 0.5f, "#6a4a2a", "#7a5a2e", accents: A("#b8c4a0", "#4a5a2a", "#e8c030"), snowBury: 1f,
					groupMetres: 5f, groupSize: 10f, sink: new Vector2(0f, 0.01f)),
				// The coffee forest's litter: leathery fig leaves, fallen figs, coffee cherries.
				D("DebrisFig", DetailKind.DebrisNeedle, 10, 0f, 0.25f, 0.6f, "#6a5030", "#8a6a40", accents: A("#8a4a3a", "#a8201a"), snowBury: 1f,
					groupMetres: 6f, groupSize: 10f, sink: new Vector2(0f, 0.01f)),

				// ── Scree (biome round, 2026-10-26): the talus's own sparse plants, lichens and litter ──
				// Ridge reed (Calamagrostis): feathery purple-tan panicles in the fines.
				D("GrassRidgeReed", DetailKind.Grass, 14, 0.6f, 0.008f, 0.25f, "#4a5a2a", "#a8a060", lean: 0.4f, accents: A("#9a7a6a"),
					variant: FloraVariant.GrassPlume, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.02f, 0.04f)),
				// A rock sedge's low tussocks.
				D("GrassRockSedge", DetailKind.Grass, 14, 0.15f, 0.006f, 0.2f, "#3a4a24", "#8a9a5a", lean: 0.4f,
					groupMetres: 3f, groupSize: 8f, sink: new Vector2(0.02f, 0.04f)),
				// Diamond bellflowers: nodding pale violet and white tubes on arching stems on the shaded talus.
				D("FlowersDiamondBell", DetailKind.Flowers, 6, 0.3f, 0.04f, 0.3f, "#4a6a3a", "#5a7a44", accents: A("#9a8ad8", "#ece8f8"),
					groupMetres: 3f, groupSize: 8f, sink: new Vector2(0.01f, 0.02f)),
				// Stonecrop: fleshy rosettes with flat gold star clusters on the sunned blocks.
				D("FlowersStonecrop", DetailKind.Flowers, 10, 0.1f, 0.04f, 0.2f, "#4a6a2a", "#6a7a34", accents: A("#f0c830"),
					groupMetres: 3f, groupSize: 8f, sink: new Vector2(0.01f, 0.02f)),
				// The nets' cushion: a dark mat with white five-petal stars.
				D("CushionNetwort", DetailKind.Cushion, 50, 0.06f, 0.07f, 0.2f, "#2e4a2a", "#6a6a3a", accents: A("#f4f2ec"), snowBury: 1f,
					variant: FloraVariant.CushionMossCampion, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.01f, 0.02f)),
				// Coral lichen (Stereocaulon): snow-white coral tufts on the blocks.
				D("LichenCoral", DetailKind.Lichen, 2, 0.06f, 0.006f, 0.12f, "#e8ecea", "#d8dcd8", accents: A("#c8ccc8"), snowBury: 1f,
					variant: FloraVariant.LichenClump, groupMetres: 4f, groupSize: 10f, sink: new Vector2(0.005f, 0.015f)),
				// Black-dot lichen (Lecidea): a white crust with black fruit dots on the old stable blocks.
				D("LichenBlackdot", DetailKind.Lichen, 2, 0.01f, 0.005f, 0.12f, "#d8d8d0", "#c8c8c0", accents: A("#1a1a1a"), snowBury: 1f,
					variant: FloraVariant.LichenLava, groupMetres: 4f, groupSize: 10f, sink: new Vector2(0.005f, 0.015f)),
				// Parsley fern: crisp bright-green tufts in the block gaps.
				D("FernParsley", DetailKind.Fern, 9, 0.2f, 0.1f, 0.03f, "#5a9a3a", "#7ab040", segments: 3, snowBury: 0.6f,
					groupMetres: 3f, groupSize: 6f, sink: new Vector2(0.02f, 0.05f)),
				// Fir clubmoss: upright green bottle-brushes in the wet gaps.
				D("ClubmossFir", DetailKind.Shrub, 8, 0.12f, 0.05f, 0.12f, "#3a6a2a", "#4e7e34", snowBury: 0.8f,
					variant: FloraVariant.ShrubHorsetail, groupMetres: 3f, groupSize: 8f, sink: new Vector2(0.01f, 0.03f)),
				// Mountain root: whorls of five leaflets under a red berry cluster, the ginseng of the fir edges.
				D("HerbMountainRoot", DetailKind.Shrub, 4, 0.4f, 0.12f, 0.15f, "#3a6a2a", "#4e7e34", accents: A("#c02a2a"), snowBury: 0.5f,
					variant: FloraVariant.ShrubHerb, groupMetres: 3f, groupSize: 3f, sink: new Vector2(0.02f, 0.04f)),
				// Perilla of the fire-fields: square stems, broad toothed leaves green above and purple below, white flower spikes.
				D("CropPerilla", DetailKind.Shrub, 6, 0.8f, 0.15f, 0.25f, "#4a6a2a", "#5a2a4a", accents: A("#f4f2ec"), snowBury: 0.4f,
					variant: FloraVariant.ShrubHerb, groupMetres: 8f, groupSize: 20f, sink: new Vector2(0.02f, 0.05f)),
				// Fresh rockfall: white unlichened chips and split stones at the chutes' runouts.
				D("DebrisRockfallScar", DetailKind.DebrisNeedle, 10, 0f, 0.15f, 0.5f, "#e4e0d8", "#c8c4bc", accents: A("#f0ece4", "#8a8880"), snowBury: 1f,
					groupMetres: 5f, groupSize: 8f, sink: new Vector2(0f, 0.01f)),
				// The cone toes' fir litter: dead twigs and purple cone scales.
				D("DebrisFirDrift", DetailKind.DebrisNeedle, 12, 0f, 0.2f, 0.5f, "#4a3a2a", "#5a4a34", accents: A("#4a2a4a", "#6a5a40"), snowBury: 1f,
					groupMetres: 5f, groupSize: 10f, sink: new Vector2(0f, 0.01f)),

				// ── Scrubland (biome round, 2026-10-26): the Sonoran bajadas' own plants, on the existing kinds ──
				// Bush muhly (Muhlenbergia porteri): wiry grey tufts growing up inside the shrubs, an airy purple-pink cloud.
				D("GrassBushMuhly", DetailKind.Grass, 16, 0.6f, 0.008f, 0.3f, "#6a6a4a", "#a8a080", lean: 0.4f, accents: A("#c8a0a8"),
					healthy: paleHealthy, dry: paleDry, variant: FloraVariant.GrassPlume, groupMetres: 3f, groupSize: 6f, sink: new Vector2(0.02f, 0.04f)),
				// Purple three-awn (Aristida purpurea): tufts of fine blades under spreading purple awns.
				D("GrassThreeAwn", DetailKind.Grass, 14, 0.45f, 0.01f, 0.22f, "#6a5a4a", "#c0a888", lean: 0.35f, accents: A("#a07a8a"),
					healthy: paleHealthy, dry: paleDry, variant: FloraVariant.GrassPlume, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.02f, 0.04f)),
				// Arizona cottontop (Digitaria californica): silky white cottony heads over grey-green tufts.
				D("GrassCottontop", DetailKind.Grass, 12, 0.7f, 0.014f, 0.25f, "#5a6a48", "#a8a888", lean: 0.35f, accents: A("#f0ece4"),
					healthy: paleHealthy, dry: paleDry, variant: FloraVariant.GrassCotton, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.02f, 0.04f)),
				// Six-weeks grama (Bouteloua barbata): an ephemeral carpet of short stems with eyelash flags in the swales.
				D("GrassSixWeeks", DetailKind.Grass, 18, 0.12f, 0.012f, 0.2f, "#6a7a4a", "#b8b088", lean: 0.3f, accents: A("#b8a888"),
					healthy: paleHealthy, dry: paleDry, variant: FloraVariant.GrassEar, groupMetres: 6f, groupSize: 14f, sink: new Vector2(0.01f, 0.03f)),
				// Carrizo: the common reed of the rivers and canal banks, grey-green blades under purple-brown plumes.
				D("ReedsCarrizo", DetailKind.Reeds, 8, 3f, 0.03f, 0.3f, "#6a7a5a", "#8a9a6a", lean: 0.1f, accents: A("#8a6a6a"), snowBury: 0.2f,
					variant: FloraVariant.ReedsPlume, groupMetres: 6f, groupSize: 20f, sink: new Vector2(0.04f, 0.08f)),
				// Mexican gold poppy: satin orange-gold cups over ferny blue-green leaves, in spring carpets.
				D("FlowersGoldPoppy", DetailKind.Flowers, 7, 0.3f, 0.06f, 0.3f, "#6a8a7a", "#7a9a8a", accents: A("#f0a020", "#f8c838", "#e88a18", "#f4b830"),
					snowBury: 0.9f, healthy: paleHealthy, dry: paleDry, groupMetres: 8f, groupSize: 24f, sink: new Vector2(0.01f, 0.02f)),
				// Arizona lupine: violet-blue spikes over palmate leaves, inside the poppy carpets.
				D("FlowersLupine", DetailKind.Flowers, 6, 0.35f, 0.06f, 0.3f, "#5a7a4a", "#6a8a5a", accents: A("#5a5ad0", "#8a7ae0", "#6a6ad8", "#7a5ac8"),
					snowBury: 0.9f, healthy: paleHealthy, dry: paleDry, groupMetres: 4f, groupSize: 10f, sink: new Vector2(0.01f, 0.02f)),
				// Desert marigold (Baileya): woolly grey mounds under lemon daisies on 30 cm stalks, at roadsides and canal banks.
				D("FlowersDesertMarigold", DetailKind.Flowers, 6, 0.45f, 0.06f, 0.3f, "#a8b0a0", "#c0c6b8", accents: A("#f0d838", "#e8cc30"),
					snowBury: 0.9f, healthy: paleHealthy, dry: paleDry, groupMetres: 3f, groupSize: 8f, sink: new Vector2(0.01f, 0.02f)),
				// Sand verbena (Abronia): sprawling sticky stems under pink-purple balls on the flats and wash sand.
				D("FlowersSandVerbena", DetailKind.Flowers, 6, 0.15f, 0.06f, 0.35f, "#5a7a4a", "#6a8a52", accents: A("#c060b0", "#d070c0"),
					snowBury: 0.9f, healthy: paleHealthy, dry: paleDry, groupMetres: 4f, groupSize: 10f, sink: new Vector2(0.01f, 0.02f)),
				// Teddybear cholla: dense straw-silver spines that glow against the light over a black dead trunk, fallen joints round it.
				D("ChollaTeddybear", DetailKind.Cholla, 3, 1.5f, 0.05f, 0.25f, "#ffffff", "#ffffff", accents: A("#e8e0b0", "#3a3028"), snowBury: 0f,
					groupMetres: 20f, groupSize: 6f, sink: new Vector2(0.04f, 0.08f)),
				// Fishhook barrel: a 1.2 m barrel leaning south, red hooked spines, a crown of yellow fruit.
				D("CactusFishhook", DetailKind.BarrelCactus, 1, 1.2f, 0f, 0.3f, "#4a6a3a", "#6a8a4a", accents: A("#a8302a", "#e8c838"), snowBury: 0f,
					groupMetres: 8f, groupSize: 3f, sink: new Vector2(0.04f, 0.08f)),
				// Engelmann prickly pear: round blue-green pads, yellow cups, purple tunas (a tint on the cactus bark).
				D("PadCactusEngelmann", DetailKind.PadCactus, 1, 1.1f, 0.24f, 0.35f, "#b8ccc0", "#c8d8cc", accents: A("#6a2050"), snowBury: 0f,
					groupMetres: 6f, groupSize: 4f, sink: new Vector2(0.04f, 0.08f)),
				// The canal builders' agave: green-grey 0.7 m rosettes. LATER (I9b): the 3 m bulbil stalk on one in six.
				D("AgaveHohokam", DetailKind.Rosette, 18, 0.7f, 0.12f, 0.1f, "#6a8a7a", "#8aa098", segments: 3, snowBury: 0.2f,
					variant: FloraVariant.RosetteAgave, groupMetres: 6f, groupSize: 4f, sink: new Vector2(0.03f, 0.06f)),
				// Organ pipe of the frost-free reach: grey-green columns, pink night flowers, red fruit (a tint on the cactus bark).
				D("OrganPipeSonoran", DetailKind.ColumnCluster, 9, 3.5f, 0.09f, 0.4f, "#d0dcc8", "#e0e8d8", accents: A("#e8a0b8", "#c83a2a"), snowBury: 0f,
					variant: FloraVariant.OrganPipe, groupMetres: 12f, groupSize: 3f, sink: new Vector2(0.04f, 0.08f)),
				// Pincushion (Mammillaria): a 10 cm globe of white radials under a ring of pink cups, in the shade of a shrub.
				D("CactusPincushion", DetailKind.BarrelCactus, 1, 0.12f, 0f, 0.06f, "#7a9a6a", "#90aa80", accents: A("#e080b0"), snowBury: 0f,
					groupMetres: 3f, groupSize: 4f, sink: new Vector2(0.01f, 0.02f)),
				// Queen of the night (Peniocereus): grey four-angled dead-looking sticks inside the shrubs. LATER (I9b P29): the opened
				// state, its 15 cm white flowers on the one bloom night of the shared clock's year.
				D("QueenOfTheNight", DetailKind.Shrub, 5, 0.5f, 0.02f, 0.12f, "#7a7468", "#8a8478", accents: A("#f8f6f0"), snowBury: 0.2f,
					variant: FloraVariant.ShrubHorsetail, groupMetres: 3f, groupSize: 3f, sink: new Vector2(0.02f, 0.05f)),
				// Spikemoss (Selaginella): grey-brown curled resurrection rosettes in the north-facing crevices. LATER (I9b P29): green after rain.
				D("Spikemoss", DetailKind.Cushion, 60, 0.04f, 0.06f, 0.15f, "#6a5a44", "#7a6a4a", snowBury: 1f,
					variant: FloraVariant.CushionMoss, groupMetres: 3f, groupSize: 8f, sink: new Vector2(0.01f, 0.02f)),
				// Chia (Salvia columbariae): stacked blue-purple whorls on grey-green stems, a seed herb of the roadsides.
				D("HerbChia", DetailKind.Shrub, 6, 0.4f, 0.1f, 0.2f, "#6a7a5a", "#7a8a6a", accents: A("#6a5ad0"), snowBury: 0.4f,
					variant: FloraVariant.ShrubHerb, groupMetres: 4f, groupSize: 6f, sink: new Vector2(0.02f, 0.05f)),
				// Desert tobacco: sticky grey-green leaves under slim white trumpets, in rock crevices and ruins.
				D("HerbDesertTobacco", DetailKind.Shrub, 6, 0.6f, 0.14f, 0.25f, "#6a7a5a", "#8a9a78", accents: A("#ece8dc"), snowBury: 0.4f,
					variant: FloraVariant.ShrubHerb, groupMetres: 3f, groupSize: 4f, sink: new Vector2(0.02f, 0.05f)),
				// A fallen saguaro's skeleton: wooden ribs and the woodpeckers' boots (a pale tint on the branch's bark).
				D("DebrisSaguaroRibs", DetailKind.DebrisBranch, 6, 2.5f, 0.025f, 0.8f, "#d8c8a8", "#c0a888", snowBury: 1f,
					groupMetres: 6f, groupSize: 3f, sink: new Vector2(0f, 0.02f)),
				// A packrat's midden: a metre mound of sticks and cholla joints with a dark glazed foot (dark sticks on the branch's bark).
				D("DebrisPackratMidden", DetailKind.DebrisBranch, 14, 0.8f, 0.02f, 0.5f, "#7a6a58", "#5a4a3a", snowBury: 1f,
					groupMetres: 4f, groupSize: 2f, sink: new Vector2(0f, 0.02f)),
				// Red-on-buff sherds: buff pot sherds with red scrolls, shell-bracelet bits and broken metates near the villages.
				D("DebrisSherdsRedBuff", DetailKind.DebrisNeedle, 10, 0f, 0.15f, 0.5f, "#d8b890", "#c8a880", accents: A("#9a3a24", "#f0e8dc", "#8a8078"), snowBury: 1f,
					groupMetres: 5f, groupSize: 8f, sink: new Vector2(0f, 0.01f)),
				// The bosque's floor: tan curled mesquite pods and leaflets.
				D("DebrisMesquitePods", DetailKind.Debris, 10, 0f, 0.2f, 0.6f, "#c8a870", "#6a7a3a", snowBury: 1f,
					groupMetres: 6f, groupSize: 12f, sink: new Vector2(0f, 0.01f)),
				// The canal fields' crops: flood-irrigated maize with tassels, Sonoran cotton with white bolls, little barley's gold heads.
				D("GrassMaizeCanal", DetailKind.Grass, 6, 2f, 0.05f, 0.25f, "#4a6a2a", "#8a9a4a", lean: 0.1f, accents: A("#d8c070"), snowBury: 0.4f,
					variant: FloraVariant.GrassEar, groupMetres: 8f, groupSize: 20f, sink: new Vector2(0.03f, 0.06f)),
				D("FlowersCottonCanal", DetailKind.Flowers, 6, 1.2f, 0.08f, 0.35f, "#4a6a3a", "#5a7a44", accents: A("#f4f2ec", "#ece8e0"), snowBury: 0.4f,
					groupMetres: 8f, groupSize: 20f, sink: new Vector2(0.01f, 0.03f)),
				D("GrassLittleBarley", DetailKind.Grass, 12, 0.4f, 0.012f, 0.2f, "#8a9a5a", "#d0b870", lean: 0.2f, accents: A("#d8b860"), snowBury: 0.4f,
					healthy: paleHealthy, dry: paleDry, variant: FloraVariant.GrassEar, groupMetres: 8f, groupSize: 20f, sink: new Vector2(0.02f, 0.04f)),

				// ── Savanna (biome round, 2026-10-26): the Zimbabwe plateau's veld, kopje and termite-island plants ──
				// Thatching grass (Hyparrhenia filipendula): head-high swathes, loose drooping rust-gold panicles.
				D("GrassThatch", DetailKind.Grass, 18, 2f, 0.03f, 0.45f, "#5a5a2a", "#c8a858", lean: 0.45f, accents: A("#b0703a"), snowBury: 0.3f,
					healthy: paleHealthy, dry: paleDry, variant: FloraVariant.GrassPlume, groupMetres: 6f, groupSize: 12f, sink: new Vector2(0.03f, 0.06f)),
				// Red-oat grass (Themeda triandra): arching tufts, hanging copper-red spikelet clusters.
				D("GrassRedOat", DetailKind.Grass, 16, 0.9f, 0.02f, 0.3f, "#5a5a2e", "#a0602e", lean: 0.45f, accents: A("#8a3a1e"),
					healthy: paleHealthy, dry: paleDry, variant: FloraVariant.GrassPlume, groupMetres: 5f, groupSize: 10f, sink: new Vector2(0.02f, 0.05f)),
				// Wire grass (Aristida): wiry silver-purple three-awned tufts of the poor soils and kraal margins.
				D("GrassWire", DetailKind.Grass, 14, 0.5f, 0.008f, 0.2f, "#6a5a4a", "#c8b8b8", lean: 0.2f, accents: A("#d8c8d0"),
					healthy: paleHealthy, dry: paleDry, variant: FloraVariant.GrassPlume, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.02f, 0.04f)),
				// Guinea grass (Megathyrsus maximus): broad lush blades in tree shade, open airy panicles.
				D("GrassGuinea", DetailKind.Grass, 14, 1.5f, 0.04f, 0.35f, "#3a5a2a", "#7a9a3a", lean: 0.5f, accents: A("#8a6a4a"),
					variant: FloraVariant.GrassPlume, groupMetres: 5f, groupSize: 10f, sink: new Vector2(0.03f, 0.05f)),
				// Star grass (Cynodon dactylon): the short grazing lawn by the water, purple finger heads.
				D("GrassStar", DetailKind.Grass, 20, 0.12f, 0.012f, 0.3f, "#3e5a2a", "#6a8a3a", lean: 0.3f, accents: A("#6a4a5a"),
					variant: FloraVariant.GrassEar, groupMetres: 6f, groupSize: 14f, sink: new Vector2(0.01f, 0.03f)),
				// Elephant grass (Pennisetum purpureum): 3.5 m canes on the sand-river banks and vlei edges, bristly golden spikes.
				D("ReedsElephant", DetailKind.Reeds, 8, 3.5f, 0.04f, 0.4f, "#4a6a2a", "#8aa04a", lean: 0.12f, accents: A("#c8a050"), snowBury: 0.2f,
					variant: FloraVariant.ReedsPlume, groupMetres: 6f, groupSize: 20f, sink: new Vector2(0.04f, 0.08f)),
				// The wet season's veld flowers: ironweed lilac, wild hibiscus yellow, morning-glory pink, Aspilia gold.
				D("FlowersVeld", DetailKind.Flowers, 6, 0.6f, 0.07f, 0.35f, "#4a6a2e", "#6a8a3a", accents: A("#9a7ac8", "#e8c830", "#d878a8", "#e0a020"),
					snowBury: 0.4f, groupMetres: 4f, groupSize: 10f, sink: new Vector2(0.01f, 0.03f)),
				// Fire lilies: scarlet trumpets, pink candelabra balls and white crinums, only on the burnt veld after the fire passed.
				D("FlowersFireLily", DetailKind.Flowers, 6, 0.35f, 0.06f, 0.3f, "#3a5a2a", "#5a7a34", accents: A("#d0281e", "#e070a0", "#f2ece0"),
					snowBury: 0.6f, groupMetres: 5f, groupSize: 14f, sink: new Vector2(0.01f, 0.02f)),
				// Flame lily (Gloriosa): red-and-yellow recurved flames scrambling in the thickets.
				D("FlowersFlameLily", DetailKind.Flowers, 5, 0.9f, 0.08f, 0.35f, "#3a5a2a", "#5a7a30", accents: A("#c8281a", "#f0c020"),
					snowBury: 0.4f, groupMetres: 3f, groupSize: 6f, sink: new Vector2(0.01f, 0.03f)),
				// The homestead fields' crops: pearl millet's candle spikes and Bambara groundnut's low trifoliate clumps.
				D("GrassPearlMillet", DetailKind.Grass, 8, 2f, 0.04f, 0.25f, "#5a7a3a", "#8a8a50", lean: 0.1f, accents: A("#8a7a5a"), snowBury: 0.4f,
					healthy: paleHealthy, dry: paleDry, variant: FloraVariant.GrassEar, groupMetres: 8f, groupSize: 20f, sink: new Vector2(0.03f, 0.06f)),
				D("Bambara", DetailKind.Shrub, 6, 0.25f, 0.1f, 0.25f, "#4a7a2e", "#5a8a34", snowBury: 0.4f,
					variant: FloraVariant.ShrubHerb, groupMetres: 8f, groupSize: 20f, sink: new Vector2(0.02f, 0.04f)),
				// Resurrection bush (Myrothamnus): 0.4 m grey-brown dead-looking fans in the dwala cracks. LATER (I9b P29): green after rain.
				D("Myrothamnus", DetailKind.Shrub, 7, 0.4f, 0.1f, 0.2f, "#6a5a48", "#7a6a54", snowBury: 0.4f,
					variant: FloraVariant.ShrubHerb, groupMetres: 3f, groupSize: 6f, sink: new Vector2(0.02f, 0.05f)),
				// Monkey's tail (Xerophyta): 0.8 m black fibrous stems with grass-leaf tufts and mauve stars on the granite domes.
				D("Xerophyta", DetailKind.Rosette, 18, 0.8f, 0.02f, 0.1f, "#2a2622", "#5a6a4a", segments: 3, accents: A("#b8a0d0"), snowBury: 0.2f,
					variant: FloraVariant.RosetteYucca, groupMetres: 4f, groupSize: 5f, sink: new Vector2(0.03f, 0.06f)),
				// Bowstring hemp (Sansevieria): banded grey-green sword leaves under the thickets and on the termite islands.
				D("Sansevieria", DetailKind.Rosette, 10, 0.7f, 0.05f, 0.08f, "#4a6a4a", "#8a9a7a", segments: 3, snowBury: 0.2f,
					variant: FloraVariant.RosetteYucca, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.03f, 0.06f)),
				// Fever-tea (Lippia): a metre of aromatic grey-green shrublet under cream flower heads.
				D("HerbWildSage", DetailKind.Shrub, 6, 1f, 0.14f, 0.3f, "#6a7a5a", "#8a9a78", accents: A("#ece4c8"), snowBury: 0.4f,
					variant: FloraVariant.ShrubHerb, groupMetres: 4f, groupSize: 5f, sink: new Vector2(0.02f, 0.05f)),
				// Desert shaggy mane (Podaxis): a 15 cm white scaly club turning black, on the termite islands after rain.
				D("MushroomPodaxis", DetailKind.Mushroom, 3, 0.15f, 0.03f, 0.2f, "#ece8e0", "#d8d0c4", accents: A("#e0dcd2", "#f0ece4", "#2a2626", "#6a5a4a"), snowBury: 1f,
					variant: FloraVariant.MushroomSwamp, groupMetres: 4f, groupSize: 3f, sink: new Vector2(0.005f, 0.015f)),
				// The veld's litter: mopane pod pairs, marula stones, porcupine quills, guineafowl feathers, dung balls.
				D("DebrisVeld", DetailKind.DebrisNeedle, 12, 0f, 0.2f, 0.5f, "#8a6a3a", "#a08048", accents: A("#c8b070", "#e8e4dc", "#3a2a1a"), snowBury: 1f,
					groupMetres: 6f, groupSize: 10f, sink: new Vector2(0f, 0.01f)),
				// ── Grassland (biome round, 2026-10-26): the Pannonian puszta's own grasses, forbs, crops and litter, on the existing kinds ──
				// Upright and smooth brome (Bromus erectus / inermis), the base sward: nodding straw panicles.
				D("GrassBrome", DetailKind.Grass, 14, 0.6f, 0.008f, 0.2f, "#4a6a2a", "#8aa048", lean: 0.3f, accents: A("#8a7a4a"),
					variant: FloraVariant.GrassPlume, groupMetres: 6f, groupSize: 12f, sink: new Vector2(0.02f, 0.04f)),
				// Goldbeard (Chrysopogon gryllus), the puszta's signature: tall leaning tussocks under bronze-purple panicles with gold hairs.
				D("GrassGoldbeard", DetailKind.Grass, 12, 1.3f, 0.006f, 0.25f, "#5a6a3a", "#a89a5a", lean: 0.35f, accents: A("#7a4a5a", "#c8a050"), snowBury: 0.3f,
					healthy: paleHealthy, dry: paleDry, variant: FloraVariant.GrassPlume, groupMetres: 8f, groupSize: 16f, sink: new Vector2(0.03f, 0.06f)),
				// Red beard-grass (Bothriochloa ischaemum): fine blades with copper-red tips, red digitate heads. LATER (I9b): a digitate head.
				D("GrassRedBeard", DetailKind.Grass, 12, 0.5f, 0.004f, 0.18f, "#6a6a3a", "#a0603a", lean: 0.3f, accents: A("#8a3a3a"),
					variant: FloraVariant.GrassPlume, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.02f, 0.04f)),
				// Sand fescue (Festuca vaginata): tight glaucous tussocks with bare sand between, sparse straw heads.
				D("GrassBlueFescue", DetailKind.Grass, 18, 0.3f, 0.003f, 0.12f, "#6a8a8a", "#a8b8b0", lean: 0.2f, accents: A("#c8b88a"),
					variant: FloraVariant.GrassPlume, groupMetres: 3f, groupSize: 6f, sink: new Vector2(0.01f, 0.03f)),
				// Alkali grass (Puccinellia limosa) with pepperwort: low tufts with purple-grey panicles ringing the soda pans.
				D("GrassAlkali", DetailKind.Grass, 14, 0.25f, 0.004f, 0.15f, "#6a7a5a", "#9aa480", lean: 0.3f, accents: A("#8a7a8a"),
					variant: FloraVariant.GrassPlume, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.01f, 0.03f)),
				// Thatching reed: the soda-lake fringe's winter-dry Phragmites, pale straw culms under grey-mauve plumes (Wetlands keeps the green reed).
				D("ReedsThatch", DetailKind.Reeds, 8, 2.6f, 0.025f, 0.35f, "#c8b078", "#e0cc98", lean: 0.1f, accents: A("#9a8a8a"), snowBury: 0.2f,
					healthy: paleHealthy, dry: paleDry, variant: FloraVariant.ReedsPlume, groupMetres: 6f, groupSize: 20f, sink: new Vector2(0.04f, 0.08f)),
				// The main forb set: violet sage, yellow inula, Carthusian pink, sky-blue Austrian flax.
				D("FlowersPannonia", DetailKind.Flowers, 7, 0.5f, 0.06f, 0.3f, "#4a6a34", "#5a7a3a", accents: A("#6a4ac0", "#f0c020", "#c83a7a", "#7aa0e0"),
					snowBury: 0.5f, groupMetres: 4f, groupSize: 12f, sink: new Vector2(0.01f, 0.03f)),
				// Spring steppe: pheasant's-eye gold, pasque flower purple with silver hairs, dwarf iris, the wild yellow tulip.
				D("FlowersSpringSteppe", DetailKind.Flowers, 7, 0.2f, 0.05f, 0.25f, "#4a6a3a", "#5a7a42", accents: A("#f2d020", "#7a4aa8", "#e8d040", "#e0dce8"),
					snowBury: 0.8f, groupMetres: 3f, groupSize: 10f, sink: new Vector2(0.01f, 0.02f)),
				// Fern-leaf peony (Paeonia tenuifolia): feathery dissected leaves under single crimson cups with a gold boss, on the kurgans.
				D("FlowersPeony", DetailKind.Flowers, 5, 0.4f, 0.08f, 0.25f, "#3e6a2a", "#4e7a32", accents: A("#c01828", "#e0b020"),
					snowBury: 0.5f, groupMetres: 3f, groupSize: 6f, sink: new Vector2(0.01f, 0.03f)),
				// The szik edges: statice's purple haze, sea aster lilac with a gold disc, wild chamomile white.
				D("FlowersSzik", DetailKind.Flowers, 7, 0.45f, 0.08f, 0.3f, "#5a6a4a", "#6a7a54", accents: A("#8a6ac0", "#b0a0e0", "#f6f6f0", "#e0c030"),
					snowBury: 0.5f, groupMetres: 4f, groupSize: 12f, sink: new Vector2(0.01f, 0.03f)),
				// Tall Tatar forbs: crimson Russian bugloss spikes, purple mullein, the white cloud of Tatar sea-kale.
				D("FlowersTatar", DetailKind.Flowers, 5, 0.9f, 0.1f, 0.25f, "#4a6a34", "#5a7a3a", accents: A("#a01830", "#7a3a8a", "#f4f2ea"),
					snowBury: 0.3f, groupMetres: 4f, groupSize: 6f, sink: new Vector2(0.02f, 0.04f)),
				// Broomcorn millet, the medieval staple: drooping golden panicles over broad leaves.
				D("GrassMillet", DetailKind.Grass, 10, 1f, 0.02f, 0.2f, "#6a8a3a", "#a8a058", lean: 0.3f, accents: A("#d8b860"), snowBury: 0.4f,
					healthy: paleHealthy, dry: paleDry, variant: FloraVariant.GrassEar, groupMetres: 8f, groupSize: 20f, sink: new Vector2(0.02f, 0.04f)),
				// Hemp: 2 m palmate fibre crop, retting pools beside its fields.
				D("CropHemp", DetailKind.Shrub, 6, 2f, 0.25f, 0.3f, "#4a7a2c", "#5a8a34", accents: A("#8a9a5a"), snowBury: 0.3f,
					variant: FloraVariant.ShrubHerb, groupMetres: 8f, groupSize: 20f, sink: new Vector2(0.03f, 0.06f)),
				// A melon patch: sprawling broad leaves, yellow flowers, striped melons. LATER (I9b): the fruit on the runners.
				D("CropMelon", DetailKind.BroadHerb, 5, 0.3f, 0.22f, 0.35f, "#3a5a2a", "#4e6e32", segments: 3, accents: A("#9ab060", "#f0d040"), snowBury: 0.5f,
					groupMetres: 8f, groupSize: 20f, sink: new Vector2(0.01f, 0.03f)),
				// Sea wormwood (Artemisia santonicum): low silver-grey feathery cushions on the alkali puszta, faint gold sprays.
				D("ArtemisiaSalt", DetailKind.Shrub, 6, 0.45f, 0.25f, 0.25f, "#8a9a8a", "#c0c8b8", accents: A("#d8c070"), snowBury: 0.5f,
					groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.02f, 0.05f)),
				// Musk thistle (Carduus nutans): a spiny rosette and tall stems with nodding crimson-purple heads, the overgrazed pasture's mark.
				D("ThistleMusk", DetailKind.Shrub, 5, 1.2f, 0.15f, 0.2f, "#5a7a5a", "#7a8a6a", accents: A("#a03a8a", "#f0ece4"), snowBury: 0.3f,
					variant: FloraVariant.ShrubHerb, groupMetres: 4f, groupSize: 5f, sink: new Vector2(0.03f, 0.06f)),
				// Field eryngo (Eryngium campestre): a pale grey-green spiny candelabra with white-green globes, drying to straw.
				D("Eryngo", DetailKind.Shrub, 5, 0.5f, 0.1f, 0.25f, "#9aaa90", "#b8c4a8", accents: A("#d8e0c8"), snowBury: 0.4f,
					variant: FloraVariant.ShrubHerb, groupMetres: 4f, groupSize: 5f, sink: new Vector2(0.02f, 0.05f)),
				// Mullein (Verbascum): a woolly silver rosette under a 1.6 m yellow spike, the open ground's vertical.
				D("Mullein", DetailKind.Rosette, 12, 0.3f, 0.08f, 0.22f, "#9aa890", "#b0bca4", segments: 3, accents: A("#f0d040"), snowBury: 0.4f,
					variant: FloraVariant.RosetteEchium, groupMetres: 5f, groupSize: 3f, sink: new Vector2(0.03f, 0.06f)),
				// Pannonian thyme: a creeping aromatic cushion with pink-mauve flowers.
				D("ThymeMat", DetailKind.Cushion, 48, 0.06f, 0.08f, 0.25f, "#5a6a3a", "#6a7a44", accents: A("#c088b0"), snowBury: 0.8f,
					variant: FloraVariant.CushionThrift, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.01f, 0.02f)),
				// Field mushrooms and fairy-ring champignons: white and tan caps in arcs (the rings from the FairyRings scatter mode).
				D("MushroomField", DetailKind.Mushroom, 6, 0.08f, 0.06f, 0.2f, "#f0ece4", "#c8a878", accents: A("#e8dcc8", "#6a4a3a"), snowBury: 1f,
					variant: FloraVariant.MushroomCluster, groupMetres: 3f, groupSize: 6f, sink: new Vector2(0.005f, 0.015f)),
				// The litter of a grass sea: wind-laid straw culms, blown seed-heads, white banded snail shells, a bleached rib now and then.
				D("DebrisSteppe", DetailKind.DebrisNeedle, 12, 0f, 0.2f, 0.5f, "#c8b078", "#a89060", accents: A("#ece4d4", "#d8c088", "#e8e2d4"), snowBury: 1f,
					groupMetres: 6f, groupSize: 10f, sink: new Vector2(0f, 0.01f)),
				// A suslik colony's spoil: fans of yellow loess on black earth round the burrow mouths. LATER (I9b): a burrow-mouth mound.
				D("DebrisSuslikSpoil", DetailKind.DebrisNeedle, 10, 0f, 0.3f, 0.6f, "#b8a070", "#a08a5a", accents: A("#d8c08a", "#1e1a14"), snowBury: 1f,
					groupMetres: 5f, groupSize: 4f, sink: new Vector2(0f, 0.01f)),
				// Puszta chamomile (Matricaria): feathery leaves, white rays round gold domes; the herb grove's.
				D("HerbChamomile", DetailKind.Shrub, 6, 0.3f, 0.1f, 0.25f, "#5a8a3a", "#6a9a44", accents: A("#f6f6f0", "#f0c020"), snowBury: 0.5f,
					variant: FloraVariant.ShrubHerb, groupMetres: 4f, groupSize: 6f, sink: new Vector2(0.02f, 0.04f)),
				// ── Plains (biome round, 2026-10-26): the High Plains prairie's own grasses, forbs, crops and litter, on the existing kinds ──
				// Western wheatgrass (Pascopyrum smithii), the blue-grey base: stiff glaucous blades, slim spikes.
				D("GrassWheatgrass", DetailKind.Grass, 14, 0.6f, 0.006f, 0.2f, "#5a6a5a", "#9aaa9e", lean: 0.2f, accents: A("#8a8a6a"),
					variant: FloraVariant.GrassPlume, groupMetres: 6f, groupSize: 12f, sink: new Vector2(0.02f, 0.04f)),
				// Little bluestem: blue-green bunches turning copper-mahogany, fluffy silver seed tufts.
				D("GrassLittleBluestem", DetailKind.Grass, 14, 0.7f, 0.004f, 0.18f, "#4a6a5a", "#a0603a", lean: 0.3f, accents: A("#e0dccc"),
					variant: FloraVariant.GrassPlume, groupMetres: 5f, groupSize: 10f, sink: new Vector2(0.02f, 0.04f)),
				// Blue grama: curly basal leaves under one-sided eyelash flags, purple going straw. LATER (I9b): the one-sided comb head.
				D("GrassGrama", DetailKind.Grass, 14, 0.35f, 0.003f, 0.15f, "#5a6a4a", "#8a9070", lean: 0.3f, accents: A("#7a4a6a"),
					variant: FloraVariant.GrassPlume, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.01f, 0.03f)),
				// Buffalograss: a dense curly grey-green stolon sod, no heads.
				D("GrassBuffalo", DetailKind.Grass, 18, 0.1f, 0.003f, 0.15f, "#5a6a48", "#8a9068", lean: 0.4f,
					groupMetres: 4f, groupSize: 10f, sink: new Vector2(0.005f, 0.015f)),
				// Big bluestem (turkey-foot): tall blue stems turning bronze-red under wine-purple three-branched heads, gold Indiangrass among it.
				// LATER (I9b): the digitate head.
				D("GrassBigBluestem", DetailKind.Grass, 10, 2f, 0.01f, 0.3f, "#3e5a3a", "#7a6a4a", lean: 0.2f, accents: A("#6a2a4a", "#c89a3a"), snowBury: 0.2f,
					variant: FloraVariant.GrassPlume, groupMetres: 8f, groupSize: 16f, sink: new Vector2(0.03f, 0.06f)),
				// Switchgrass: an airy red-purple panicle haze.
				D("GrassSwitchgrass", DetailKind.Grass, 12, 1.4f, 0.008f, 0.25f, "#4a6a3a", "#7a8a5a", lean: 0.25f, accents: A("#8a4a5a"), snowBury: 0.3f,
					variant: FloraVariant.GrassPlume, groupMetres: 6f, groupSize: 12f, sink: new Vector2(0.03f, 0.05f)),
				// Prairie sandreed and sand bluestem: straw-gold rhizomatous grass of the sandhills, open panicles.
				D("GrassSandReed", DetailKind.Grass, 12, 1f, 0.006f, 0.25f, "#7a7a5a", "#c8b07a", lean: 0.3f, accents: A("#d8c088"), snowBury: 0.3f,
					healthy: paleHealthy, dry: paleDry, variant: FloraVariant.GrassPlume, groupMetres: 6f, groupSize: 12f, sink: new Vector2(0.02f, 0.05f)),
				// Coneflowers and blanketflower: mauve drooping rays on an orange-brown cone, prairie coneflower yellow, red-gold gaillardia.
				D("FlowersConeflower", DetailKind.Flowers, 7, 0.6f, 0.06f, 0.3f, "#4a6a34", "#5a7a3a", accents: A("#c87ab0", "#8a4a1a", "#f0c020", "#c8301a"),
					snowBury: 0.4f, groupMetres: 4f, groupSize: 12f, sink: new Vector2(0.01f, 0.03f)),
				// Wild and Maximilian sunflowers: 1.8 m stems, many gold heads with dark discs, on disturbed ground.
				D("FlowersSunflower", DetailKind.Flowers, 5, 1.8f, 0.12f, 0.3f, "#4a6a2a", "#5a7a34", accents: A("#f0b820", "#3a2a1a"),
					snowBury: 0.2f, groupMetres: 5f, groupSize: 8f, sink: new Vector2(0.02f, 0.05f)),
				// Blazing star and prairie clovers: purple spikes, white and purple clover heads, late goldenrod.
				D("FlowersBlazingStar", DetailKind.Flowers, 6, 0.8f, 0.05f, 0.25f, "#4a6a3a", "#5a7a42", accents: A("#8a3ab0", "#f2f0e6", "#8a3a8a", "#e8c020"),
					snowBury: 0.3f, groupMetres: 4f, groupSize: 10f, sink: new Vector2(0.01f, 0.03f)),
				// Prairie smoke (Geum triflorum): nodding red sepals going to feathery pink plumes, the north's spring.
				D("FlowersPrairieSmoke", DetailKind.Flowers, 7, 0.25f, 0.04f, 0.25f, "#4a6a3a", "#5a7a42", accents: A("#b04a5a", "#e0a0b8"),
					snowBury: 0.8f, groupMetres: 3f, groupSize: 10f, sink: new Vector2(0.01f, 0.02f)),
				// The sandhills' hoary puccoon orange, spiderwort blue-violet, white evening primrose.
				D("FlowersSandhill", DetailKind.Flowers, 7, 0.35f, 0.05f, 0.25f, "#5a6a4a", "#6a7a54", accents: A("#f08a20", "#5a5ad8", "#f6f4ea"),
					snowBury: 0.6f, groupMetres: 3f, groupSize: 10f, sink: new Vector2(0.01f, 0.02f)),
				// The playa rims: smartweed pink spikes, coreopsis gold, barnyard grass.
				D("FlowersPlaya", DetailKind.Flowers, 7, 0.5f, 0.05f, 0.3f, "#5a7a3a", "#6a7a3a", accents: A("#d87a9a", "#f0c020"),
					snowBury: 0.5f, groupMetres: 4f, groupSize: 12f, sink: new Vector2(0.01f, 0.03f)),
				// Flint corn of the Three-Sisters hills: tall stems, gold tassels, ears red, blue and yellow, beans twining.
				D("GrassFlintCorn", DetailKind.Grass, 6, 1.8f, 0.05f, 0.25f, "#3e6a2a", "#8a9a4a", lean: 0.1f, accents: A("#d8b860"), snowBury: 0.4f,
					variant: FloraVariant.GrassEar, groupMetres: 8f, groupSize: 20f, sink: new Vector2(0.03f, 0.06f)),
				// Squash sprawling between the corn hills: broad leaves, gold trumpets, orange and striped squash. LATER (I9b): the fruit.
				D("CropSquash", DetailKind.BroadHerb, 5, 0.4f, 0.35f, 0.35f, "#3a5a2a", "#4e6e32", segments: 3, accents: A("#f0b020", "#d87a20"), snowBury: 0.5f,
					groupMetres: 8f, groupSize: 20f, sink: new Vector2(0.01f, 0.03f)),
				// The field sunflower: one great head on a 2.5 m stalk.
				D("FlowersFieldSunflower", DetailKind.Flowers, 4, 2.5f, 0.15f, 0.25f, "#4a6a2a", "#5a7a34", accents: A("#f0b820", "#3a2a1a"),
					snowBury: 0.2f, groupMetres: 8f, groupSize: 20f, sink: new Vector2(0.03f, 0.06f)),
				// Soapweed yucca (Yucca glauca): a grey-green spiky ball under a cream bell spike.
				D("YuccaSoapweed", DetailKind.Rosette, 26, 0.6f, 0.03f, 0.06f, "#7a8a7a", "#8a9a8a", segments: 3, accents: A("#f0ecd8"), snowBury: 0.4f,
					variant: FloraVariant.RosetteYucca, groupMetres: 6f, groupSize: 4f, sink: new Vector2(0.03f, 0.06f)),
				// Plains prickly pear (Opuntia polyacantha): a low sprawl of small spiny pads, lemon cups, red fruit.
				D("PricklyPearPlains", DetailKind.PadCactus, 1, 0.35f, 0.14f, 0.4f, "#a8b8a0", "#b8c8b0", accents: A("#f0e060"), snowBury: 0.6f,
					groupMetres: 5f, groupSize: 4f, sink: new Vector2(0.03f, 0.06f)),
				// Compass plant (Silphium): deeply cut basal leaves standing north-south under a tall stalk of yellow heads.
				D("CompassPlant", DetailKind.Shrub, 5, 2.4f, 0.2f, 0.25f, "#4a6a3a", "#5a7a42", accents: A("#f0c020"), snowBury: 0.2f,
					variant: FloraVariant.ShrubHerb, groupMetres: 6f, groupSize: 4f, sink: new Vector2(0.03f, 0.06f)),
				// Showy milkweed: grey felted leaves, pink star umbels, split pods spilling silk.
				D("Milkweed", DetailKind.Shrub, 6, 0.9f, 0.15f, 0.25f, "#7a8a6a", "#8a9a7a", accents: A("#d890b0", "#f6f4ee"), snowBury: 0.3f,
					variant: FloraVariant.ShrubHerb, groupMetres: 4f, groupSize: 6f, sink: new Vector2(0.02f, 0.05f)),
				// Wild bergamot: lavender pompoms, the grove herb.
				D("HerbBergamot", DetailKind.Shrub, 6, 0.7f, 0.1f, 0.25f, "#4a6a3a", "#5a7a42", accents: A("#b8a0d8"), snowBury: 0.4f,
					variant: FloraVariant.ShrubHerb, groupMetres: 4f, groupSize: 6f, sink: new Vector2(0.02f, 0.04f)),
				// Prairie turnip (Pediomelum): hairy palmate leaves, blue-violet spikes over the gathered tuber.
				D("HerbPrairieTurnip", DetailKind.Shrub, 6, 0.3f, 0.1f, 0.25f, "#6a7a5a", "#7a8a6a", accents: A("#6a5ab8"), snowBury: 0.5f,
					variant: FloraVariant.ShrubHerb, groupMetres: 4f, groupSize: 6f, sink: new Vector2(0.02f, 0.04f)),
				// Giant puffballs: white spheres on the prairie, old ones brown and split. LATER (I9b): a sphere fungus form.
				D("MushroomPuffball", DetailKind.Mushroom, 3, 0.2f, 0.2f, 0.3f, "#f2f0e8", "#e8e4d8", accents: A("#f2f0e8", "#a08a6a"), snowBury: 1f,
					variant: FloraVariant.MushroomCluster, groupMetres: 6f, groupSize: 3f, sink: new Vector2(0.03f, 0.06f)),
				// The herd's passage: buffalo chips, shed winter wool, a sun-bleached skull now and then.
				D("DebrisBison", DetailKind.DebrisNeedle, 12, 0f, 0.25f, 0.5f, "#4a3a28", "#8a6a4a", accents: A("#3a2e20", "#a8906a", "#e8e2d4"), snowBury: 1f,
					groupMetres: 6f, groupSize: 10f, sink: new Vector2(0f, 0.01f)),
				// The galleries' floor: triangular gold cottonwood leaves, drifts of cotton fluff, twigs.
				D("DebrisCottonwood", DetailKind.DebrisNeedle, 12, 0f, 0.25f, 0.5f, "#d8b030", "#a88a40", accents: A("#f2f0ea", "#6a5a3a"), snowBury: 1f,
					groupMetres: 6f, groupSize: 10f, sink: new Vector2(0f, 0.01f)),
				// A prairie-dog crater cone: a rim of packed earth round the hole (colonies from the ClusteredFields scatter mode).
				// LATER (I9b): a crater-cone mound of its own; flat spoil stands in.
				D("DebrisDogtownMound", DetailKind.DebrisNeedle, 10, 0f, 0.3f, 0.6f, "#6e5a42", "#8a745a", accents: A("#5a4632", "#1e1a14"), snowBury: 1f,
					groupMetres: 5f, groupSize: 4f, sink: new Vector2(0f, 0.01f)),

				// ── Desert (biome round, 2026-10-26): the Saharan erg and reg's own sparse plants and the caravans' litter ──
				// Drinn (Stipagrostis pungens): stiff rolled glaucous tussocks under feathery silver plumes, the dune grass that builds nebkha.
				D("GrassDrinn", DetailKind.Grass, 14, 1.2f, 0.008f, 0.4f, "#6a7a68", "#c8bc94", lean: 0.25f, accents: A("#ece6d6"),
					healthy: paleHealthy, dry: paleDry, variant: FloraVariant.GrassPlume, groupMetres: 5f, groupSize: 6f, sink: new Vector2(0.03f, 0.06f)),
				// Taman (Panicum turgidum): cane-like jointed blue-green stems in bushy clumps, loose purple-tinged panicles.
				D("GrassTaman", DetailKind.Grass, 12, 0.9f, 0.01f, 0.3f, "#5a6a5a", "#8aa08a", lean: 0.2f, accents: A("#9a7a8a"),
					healthy: paleHealthy, dry: paleDry, variant: FloraVariant.GrassPlume, groupMetres: 5f, groupSize: 6f, sink: new Vector2(0.03f, 0.06f)),
				// Silver-awn (Stipagrostis plumosa): fine tufts under long feathered silver awns that blaze against the low sun.
				D("GrassSilverAwn", DetailKind.Grass, 14, 0.35f, 0.004f, 0.15f, "#8a8a6a", "#c8c09a", lean: 0.35f, accents: A("#f2ece0"),
					healthy: paleHealthy, dry: paleDry, variant: FloraVariant.GrassPlume, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.01f, 0.03f)),
				// Schouwia's acheb carpets after rain: fleshy blue-green leaves, crowded violet-purple heads.
				D("FlowersSchouwia", DetailKind.Flowers, 7, 0.3f, 0.06f, 0.3f, "#5a7a6a", "#6a8a78", accents: A("#8a3aa8", "#b070c8", "#9a4ab8", "#a860c0"),
					snowBury: 0.9f, healthy: paleHealthy, dry: paleDry, groupMetres: 8f, groupSize: 24f, sink: new Vector2(0.01f, 0.02f)),
				// Fagonia: wiry spiny green mats, pink-violet five-petalled stars.
				D("FlowersFagonia", DetailKind.Flowers, 6, 0.15f, 0.05f, 0.3f, "#4a6a3a", "#5a7a44", accents: A("#c060a8", "#d070b8"),
					snowBury: 0.9f, healthy: paleHealthy, dry: paleDry, groupMetres: 3f, groupSize: 6f, sink: new Vector2(0.01f, 0.02f)),
				// Sea-daffodil of the dune feet (Pancratium): white frilled star-lilies over grey-green straps.
				D("FlowersSandLily", DetailKind.Flowers, 5, 0.3f, 0.07f, 0.2f, "#7a8a78", "#8a9a88", accents: A("#f6f2ea", "#ece6d8"),
					snowBury: 0.9f, healthy: paleHealthy, dry: paleDry, groupMetres: 3f, groupSize: 4f, sink: new Vector2(0.01f, 0.02f)),
				// The desert hyacinth (Cistanche): leafless 0.5 m club spikes packed with bright yellow flowers, brown-purple tips.
				D("Cistanche", DetailKind.Rosette, 12, 0.5f, 0.07f, 0.15f, "#8a6a4a", "#a08060", segments: 3, accents: A("#f0c020", "#6a3a4a"), snowBury: 0.2f,
					variant: FloraVariant.RosetteEchium, groupMetres: 3f, groupSize: 3f, sink: new Vector2(0.03f, 0.06f)),
				// Had (Cornulaca): pale green spiny cushion-shrublets, straw spines; the camels' favourite.
				D("Had", DetailKind.Cushion, 60, 0.5f, 0.18f, 0.35f, "#8a9a6a", "#a0aa7a", accents: A("#d8c890"), snowBury: 0.8f,
					variant: FloraVariant.CushionSpiny, groupMetres: 6f, groupSize: 5f, sink: new Vector2(0.02f, 0.04f)),
				// Zilla: rounded intricate spiny grey-green tangles with lilac crucifer flowers; silver dead skeletons among them.
				D("Zilla", DetailKind.DryShrub, 12, 0.6f, 0.5f, 0.4f, "#8a9478", "#c8c0b0", accents: A("#b89ac8"), snowBury: 0.3f,
					groupMetres: 6f, groupSize: 4f, sink: new Vector2(0.02f, 0.05f)),
				// Colocynth: runners over the sand, deeply lobed grey-green leaves, yellow bells, striped green-and-gold gourds.
				D("Colocynth", DetailKind.Creeper, 30, 0.1f, 0.09f, 1f, "#6a8a5a", "#7a9a68", accents: A("#4a7a3a", "#d8b040"), snowBury: 0.9f,
					variant: FloraVariant.CreeperBeachVine, groupMetres: 6f, groupSize: 5f, sink: new Vector2(0.005f, 0.015f)),
				// Rose of Jericho: grey fist-size curled dry balls lodged against the stones. LATER (I9b P29): its opened green state in the
				// rain cells.
				D("RoseOfJericho", DetailKind.DryShrub, 14, 0.12f, 0.14f, 0.1f, "#9a9080", "#aaa090", snowBury: 0.6f,
					variant: FloraVariant.DryShrubBall, groupMetres: 3f, groupSize: 4f, sink: new Vector2(0.005f, 0.02f)),
				// Camel litter at the wells and along the pistes: dung pellets, pad prints, tufts of shed wool.
				D("DebrisCamel", DetailKind.DebrisNeedle, 10, 0f, 0.2f, 0.5f, "#5a4430", "#6a5038", accents: A("#a8845a", "#4a3828"), snowBury: 1f,
					groupMetres: 5f, groupSize: 8f, sink: new Vector2(0f, 0.01f)),
				// The toll of the crossing: a bleached camel's ribs and vertebrae, sand-scoured (pale bone on the branch's bark).
				// LATER (I9b): a skull and rib mesh of its own (the Bone family's meshes); pale sticks stand in.
				D("DebrisBones", DetailKind.DebrisBranch, 5, 0.9f, 0.03f, 0.5f, "#ece4d4", "#d8cebc", snowBury: 1f,
					groupMetres: 5f, groupSize: 3f, sink: new Vector2(0f, 0.02f)),
				// The green Sahara's surface scatter: quern slabs and handstones, polished axes, comb-marked sherds, ostrich-shell beads.
				D("DebrisNeolithic", DetailKind.DebrisNeedle, 10, 0f, 0.18f, 0.5f, "#8a5a3a", "#9a7a5a", accents: A("#4a4a42", "#f0e8d6", "#b8a888"), snowBury: 1f,
					groupMetres: 6f, groupSize: 6f, sink: new Vector2(0f, 0.01f)),

				// ── Badlands (biome round, 2026-10-26): the Cappadocian plateau's own steppe grasses, flowers, cushions, crops and litter ──
				// Bulbous meadow-grass: fine spring-green tufts that brown by midsummer, purple viviparous bulbil heads.
				D("GrassBulbousPoa", DetailKind.Grass, 16, 0.2f, 0.004f, 0.15f, "#5a6a48", "#9aa070", lean: 0.3f, accents: A("#7a5a6a"),
					healthy: paleHealthy, dry: paleDry, variant: FloraVariant.GrassEar, groupMetres: 4f, groupSize: 10f, sink: new Vector2(0.01f, 0.03f)),
				// Goatgrass (Aegilops): stiff beaded cylindrical spikes with long awns, ripening red-gold.
				D("GrassGoatgrass", DetailKind.Grass, 14, 0.45f, 0.006f, 0.18f, "#6a6a4a", "#a89a62", lean: 0.2f, accents: A("#b07a3a"),
					healthy: paleHealthy, dry: paleDry, variant: FloraVariant.GrassEar, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.02f, 0.04f)),
				// Wild barley (Hordeum bulbosum): tall silky-awned nodding heads, silver at dawn.
				D("GrassWildBarley", DetailKind.Grass, 12, 0.8f, 0.012f, 0.25f, "#4e5e3a", "#a8a070", lean: 0.35f, accents: A("#d8c890"),
					healthy: paleHealthy, dry: paleDry, variant: FloraVariant.GrassEar, groupMetres: 5f, groupSize: 10f, sink: new Vector2(0.02f, 0.04f)),
				// Ravenna grass: 2.5 m clumps under pink-silver plumes in the washes and gorges.
				D("GrassRavenna", DetailKind.Grass, 14, 2.5f, 0.025f, 0.45f, "#4a5a3a", "#8a8a62", lean: 0.3f, accents: A("#d8b8b0"),
					healthy: paleHealthy, dry: paleDry, variant: FloraVariant.GrassPlume, groupMetres: 6f, groupSize: 8f, sink: new Vector2(0.04f, 0.08f)),
				// Gypsophila (chöven): airy white baby's-breath clouds on grey stems, the plateau's signature.
				D("FlowersGypsophila", DetailKind.Flowers, 7, 0.6f, 0.5f, 0.3f, "#8a9078", "#9aa088", accents: A("#f4f2ee", "#ece8e0"),
					snowBury: 0.8f, healthy: paleHealthy, dry: paleDry, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.01f, 0.02f)),
				// The spring flush on the ash: grape-hyacinth, Ixiolirion, Anatolian crocus, star-of-Bethlehem.
				D("FlowersSpringAsh", DetailKind.Flowers, 7, 0.15f, 0.05f, 0.3f, "#4a6a3a", "#5a7a44", accents: A("#3a3a9a", "#6a6ad0", "#f0c020", "#f6f4ee"),
					snowBury: 0.9f, healthy: paleHealthy, dry: paleDry, groupMetres: 6f, groupSize: 16f, sink: new Vector2(0.01f, 0.02f)),
				// Golden-drops (Onosma): nodding yellow tubular bells on silver bristly cushions, on bare tuff.
				D("FlowersOnosma", DetailKind.Flowers, 6, 0.25f, 0.06f, 0.25f, "#9a9e88", "#aab09a", accents: A("#f0d050", "#e8c440"),
					snowBury: 0.9f, healthy: paleHealthy, dry: paleDry, groupMetres: 3f, groupSize: 5f, sink: new Vector2(0.01f, 0.02f)),
				// Foxtail lily (Eremurus): 1.5 m spires of cream-pink and apricot stars, the plateau's vertical accent.
				D("FlowersEremurus", DetailKind.Flowers, 4, 1.5f, 0.15f, 0.2f, "#4a6a3a", "#5a7a44", accents: A("#f0d8c8", "#f0a060"),
					snowBury: 0.5f, healthy: paleHealthy, dry: paleDry, groupMetres: 4f, groupSize: 4f, sink: new Vector2(0.02f, 0.04f)),
				// Wild hollyhock (Alcea): 2 m spikes, pink, white and wine, by the houses and wash edges.
				D("FlowersHollyhock", DetailKind.Flowers, 4, 2f, 0.2f, 0.2f, "#4a6a34", "#5a7a3e", accents: A("#d87a9a", "#f2ece4", "#6a1a3a"),
					snowBury: 0.4f, healthy: paleHealthy, dry: paleDry, groupMetres: 3f, groupSize: 4f, sink: new Vector2(0.02f, 0.05f)),
				// Sternbergia: crocus-gold goblets after the first autumn rain, glossy strap leaves.
				D("FlowersSternbergia", DetailKind.Flowers, 7, 0.15f, 0.05f, 0.25f, "#3e6a2a", "#4e7a34", accents: A("#f0c020", "#e8b818"),
					snowBury: 0.9f, healthy: paleHealthy, dry: paleDry, groupMetres: 4f, groupSize: 10f, sink: new Vector2(0.01f, 0.02f)),
				// Prickly thrift (Acantholimon): hard grey-green needle cushions under pink star sprays on the bare crests.
				D("Acantholimon", DetailKind.Cushion, 60, 0.3f, 0.15f, 0.3f, "#7a8a78", "#8a9a88", accents: A("#e0a0b0"), snowBury: 0.8f,
					variant: FloraVariant.CushionSpiny, groupMetres: 4f, groupSize: 6f, sink: new Vector2(0.02f, 0.04f)),
				// Noaea: spiny grey chenopod domes, straw-pink winged fruits in autumn.
				D("Noaea", DetailKind.Cushion, 60, 0.4f, 0.16f, 0.3f, "#9a9a84", "#aaaa94", accents: A("#d8b0a0"), snowBury: 0.8f,
					variant: FloraVariant.CushionSpiny, groupMetres: 5f, groupSize: 5f, sink: new Vector2(0.02f, 0.04f)),
				// Syrian rue (harmal): bright green ferny clumps, white flowers, green capsules strung against the evil eye.
				D("HerbHarmal", DetailKind.Shrub, 6, 0.5f, 0.14f, 0.25f, "#4a7a3a", "#5a8a44", accents: A("#f4f2e8"), snowBury: 0.4f,
					variant: FloraVariant.ShrubHerb, groupMetres: 4f, groupSize: 6f, sink: new Vector2(0.02f, 0.05f)),
				// Salep: the steppe orchid, spotted leaves under purple spikes; its tubers are ground for salep.
				D("HerbSalep", DetailKind.Shrub, 5, 0.35f, 0.1f, 0.15f, "#4a6a3a", "#5a7a44", accents: A("#8a3a8a"), snowBury: 0.4f,
					variant: FloraVariant.ShrubHerb, groupMetres: 3f, groupSize: 3f, sink: new Vector2(0.02f, 0.04f)),
				// Kenger (Gundelia): spiny grey thistle rosettes under wine-purple flower balls; its gum is gathered.
				D("HerbKenger", DetailKind.Rosette, 14, 0.4f, 0.08f, 0.3f, "#8a9a8a", "#a0aaa0", segments: 3, accents: A("#6a2a4a"), snowBury: 0.3f,
					variant: FloraVariant.RosetteAgave, groupMetres: 4f, groupSize: 4f, sink: new Vector2(0.02f, 0.05f)),
				// Orange and lemon crustose lichen on the caps and ledges.
				D("LichenTuff", DetailKind.Lichen, 2, 0.015f, 0.005f, 0.15f, "#d8781a", "#d8c030", accents: A("#2a2622"), snowBury: 1f,
					variant: FloraVariant.LichenLava, groupMetres: 4f, groupSize: 10f, sink: new Vector2(0.005f, 0.015f)),
				// Moss in the planes' shade by the water, the plateau's only moss.
				D("MossPlaneShade", DetailKind.Cushion, 60, 0.06f, 0.09f, 0.2f, "#4a5a2a", "#5a6a3a", snowBury: 1f,
					variant: FloraVariant.CushionMoss, groupMetres: 3f, groupSize: 6f, sink: new Vector2(0.01f, 0.02f)),
				// White pumice lapilli and fist-size floats, rose tuff spalls.
				D("DebrisPumice", DetailKind.DebrisNeedle, 12, 0f, 0.2f, 0.5f, "#f0ece4", "#e0dad0", accents: A("#c8a090", "#4a443c"), snowBury: 1f,
					groupMetres: 5f, groupSize: 10f, sink: new Vector2(0f, 0.01f)),
				// Terracotta potsherds and red-slip, lamp fragments: the potters' country.
				D("DebrisSherdsTerracotta", DetailKind.DebrisNeedle, 10, 0f, 0.15f, 0.5f, "#b0603a", "#a05a34", accents: A("#8a2a1a", "#d8b090", "#6a4a3a"), snowBury: 1f,
					groupMetres: 5f, groupSize: 8f, sink: new Vector2(0f, 0.01f)),
				// Guano spatter, grey feathers, cast down under the dovecote rows.
				D("DebrisPigeon", DetailKind.DebrisNeedle, 12, 0f, 0.2f, 0.5f, "#f0ece4", "#d8d4cc", accents: A("#8a8a8a", "#6a6a6a"), snowBury: 1f,
					groupMetres: 4f, groupSize: 8f, sink: new Vector2(0f, 0.01f)),
				// Vine prunings bundled and dropped along the plots (dark sticks on the branch's bark).
				D("DebrisVinePrunings", DetailKind.DebrisBranch, 8, 0.8f, 0.025f, 0.4f, "#4a3424", "#5a4030", snowBury: 1f,
					groupMetres: 4f, groupSize: 4f, sink: new Vector2(0f, 0.02f)),
				// The wash plots' crops: ungrafted ground-trained bush vines in low bowls, sprawling pumpkins, bushy blue-green chickpeas.
				// LATER (I9b): the vines' autumn wine-red and purple-black clusters, the pumpkins' gourds; leaves and accents stand in.
				D("CropBushVine", DetailKind.Shrub, 7, 0.6f, 0.4f, 0.4f, "#4a6a2a", "#5a7a34", accents: A("#2a1a3a", "#8a2a2a"), snowBury: 0.4f,
					variant: FloraVariant.ShrubHerb, groupMetres: 8f, groupSize: 20f, sink: new Vector2(0.03f, 0.06f)),
				D("CropPumpkin", DetailKind.Creeper, 30, 0.4f, 0.2f, 0.9f, "#4a6a2a", "#5a7a34", accents: A("#f0c020", "#e0802a"), snowBury: 0.9f,
					variant: FloraVariant.CreeperBeachVine, groupMetres: 8f, groupSize: 20f, sink: new Vector2(0.005f, 0.015f)),
				D("CropChickpea", DetailKind.Shrub, 6, 0.5f, 0.12f, 0.25f, "#6a8a7a", "#7a9a88", accents: A("#d8c8a0"), snowBury: 0.4f,
					variant: FloraVariant.ShrubHerb, groupMetres: 8f, groupSize: 20f, sink: new Vector2(0.02f, 0.04f)),

				// ── Salt Flat (biome round, 2026-10-26): the brine edge's and lava rim's few plants, the hoppers and the trade's litter ──
				// Odyssea: stiff spiny glaucous salt-flat grass, mat-forming.
				D("GrassOdyssea", DetailKind.Grass, 14, 0.35f, 0.006f, 0.2f, "#5e5c48", "#c4bc98", lean: 0.15f, accents: A("#a89670"),
					healthy: paleHealthy, dry: paleDry, variant: FloraVariant.GrassEar, groupMetres: 4f, groupSize: 10f, sink: new Vector2(0.01f, 0.03f)),
				// The brine sedge (Cyperus laevigatus): leafless round green stems, dark brown head knots.
				D("GrassBrineSedge", DetailKind.Grass, 16, 0.3f, 0.004f, 0.15f, "#3e4a2a", "#7a9a3a", lean: 0.2f, accents: A("#4a3420"),
					variant: FloraVariant.GrassRush, groupMetres: 4f, groupSize: 12f, sink: new Vector2(0.01f, 0.03f)),
				// Cressa: grey-hairy cushionlets under white-pink stars.
				D("FlowersCressa", DetailKind.Flowers, 6, 0.12f, 0.05f, 0.25f, "#6a6a58", "#a8a890", accents: A("#f2dfe4", "#d8a8b8"),
					snowBury: 0.9f, healthy: paleHealthy, dry: paleDry, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.01f, 0.02f)),
				// The wardenbloom (fantasy): glassy salt-crystal petals with a rose heart, never swaying (a Rigid blade).
				D("FlowersWardenbloom", DetailKind.Flowers, 5, 0.18f, 0.06f, 0.2f, "#8a8a7a", "#f8f8f4", accents: A("#f8f8f4", "#e8c0c8"),
					snowBury: 0.9f, groupMetres: 3f, groupSize: 5f, sink: new Vector2(0.01f, 0.02f)),
				// Bead glasswort (Halopeplis): clumps of bead-jointed succulent stems, green going crimson.
				D("Halopeplis", DetailKind.Shrub, 8, 0.2f, 0.1f, 0.15f, "#6a8a3a", "#a83a2a", snowBury: 0.6f,
					variant: FloraVariant.ShrubSucculent, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.01f, 0.03f)),
				// Desert cotton (Aerva): grey-woolly subshrubs under white woolly spikes.
				D("Aerva", DetailKind.Shrub, 6, 0.7f, 0.14f, 0.3f, "#9a9a88", "#aaaa98", accents: A("#ece8dc"), snowBury: 0.4f,
					variant: FloraVariant.ShrubHerb, groupMetres: 4f, groupSize: 5f, sink: new Vector2(0.02f, 0.05f)),
				// Aizoon: a flat rosette of fleshy red-green leaves under yellow stars, after rain.
				D("Aizoon", DetailKind.Cushion, 40, 0.06f, 0.15f, 0.2f, "#8a5a3a", "#6a7a3a", accents: A("#e0c040"), snowBury: 1f,
					variant: FloraVariant.CushionThrift, groupMetres: 4f, groupSize: 6f, sink: new Vector2(0.01f, 0.02f)),
				// The escarpment aloe: a rosette of spotted grey-green spiny leaves, a coral-red flower spike.
				D("HerbAloe", DetailKind.Rosette, 14, 0.6f, 0.08f, 0.3f, "#6a7a6a", "#c8c8b0", segments: 3, accents: A("#d0502a"), snowBury: 0.2f,
					variant: FloraVariant.RosetteAgave, groupMetres: 5f, groupSize: 3f, sink: new Vector2(0.03f, 0.06f)),
				// Hopper crystals: hollow stepped cubic halite growing clear-white on the brine rims.
				D("DebrisSaltHopper", DetailKind.DebrisNeedle, 10, 0f, 0.08f, 0.4f, "#f4f2ee", "#e6e4de", accents: A("#ffffff", "#d8d4cc"), snowBury: 1f,
					groupMetres: 3f, groupSize: 8f, sink: new Vector2(0f, 0.01f)),
				// The bar trade's litter: off-cuts and broken bars, brown-banded trim chips, doum-fibre bindings, donkey dung.
				D("DebrisAmole", DetailKind.DebrisNeedle, 12, 0f, 0.2f, 0.5f, "#ece8e0", "#d8d0c0", accents: A("#8a7458", "#c8b088", "#4a3e30"), snowBury: 1f,
					groupMetres: 5f, groupSize: 8f, sink: new Vector2(0f, 0.01f)),
				// ── Steppe (biome round, 2026-10-26): the Sary-Arka's own grasses, forbs, crops and litter, on the existing kinds ──
				// Tyrsa (Stipa capillata): hair-awned feather grass without plumes, late-summer gold.
				D("GrassTyrsa", DetailKind.Grass, 12, 0.6f, 0.003f, 0.2f, "#6a6236", "#b0a060", lean: 0.3f, accents: A("#c8a860"),
					healthy: paleHealthy, dry: paleDry, variant: FloraVariant.GrassPlume, groupMetres: 5f, groupSize: 10f, sink: new Vector2(0.02f, 0.04f)),
				// Tipchak (Festuca valesiaca): the low grey-green fine fescue of the turf, sparse panicles.
				D("GrassTipchak", DetailKind.Grass, 16, 0.2f, 0.002f, 0.12f, "#5a6a5a", "#8a9a88", lean: 0.2f, accents: A("#a89c78"),
					variant: FloraVariant.GrassPlume, groupMetres: 3f, groupSize: 8f, sink: new Vector2(0.01f, 0.03f)),
				// Chiy (Achnatherum splendens): the giant saline-lowland tussock, silver-purple open panicles, by sors, wells and springs.
				D("GrassChiy", DetailKind.Grass, 14, 2f, 0.008f, 0.4f, "#6a6a46", "#b8b088", lean: 0.15f, accents: A("#a08a9a"), snowBury: 0.2f,
					healthy: paleHealthy, dry: paleDry, variant: FloraVariant.GrassPlume, groupMetres: 8f, groupSize: 6f, sink: new Vector2(0.03f, 0.06f)),
				// Zhitnyak (Agropyron cristatum): crested-wheatgrass comb spikes. LATER (I9b): the flat comb head.
				D("GrassZhitnyak", DetailKind.Grass, 12, 0.5f, 0.004f, 0.18f, "#4e6036", "#8a9a5a", lean: 0.25f, accents: A("#a8a070"),
					variant: FloraVariant.GrassPlume, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.02f, 0.04f)),
				// Schrenk's tulips: the April steppe red, yellow, white and rose for two weeks, two broad grey leaves each.
				D("FlowersTulipSteppe", DetailKind.Flowers, 7, 0.2f, 0.05f, 0.25f, "#6a8a7a", "#7a9a8a", accents: A("#c8202a", "#f0c828", "#f4f0e8", "#d86a8a"),
					snowBury: 0.8f, groupMetres: 6f, groupSize: 14f, sink: new Vector2(0.01f, 0.02f)),
				// Phlomis tuberosa: metre stems with rosy-purple whorls in the dry valleys.
				D("FlowersPhlomis", DetailKind.Flowers, 5, 0.9f, 0.08f, 0.25f, "#4a5a34", "#5a6a3e", accents: A("#9a5a8a"),
					snowBury: 0.3f, groupMetres: 4f, groupSize: 6f, sink: new Vector2(0.02f, 0.04f)),
				// Steppe statice (Goniolimon): flat wiry heads of white-pink papery flowers over leathery rosettes.
				D("FlowersGoniolimon", DetailKind.Flowers, 7, 0.35f, 0.12f, 0.25f, "#5a6a5a", "#6a7a6a", accents: A("#e8c8d0", "#f4ecee"),
					snowBury: 0.5f, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.01f, 0.03f)),
				// Gagea, the gold star of the steppe, earliest spring.
				D("FlowersGagea", DetailKind.Flowers, 7, 0.1f, 0.02f, 0.2f, "#6a8a4a", "#7a9a54", accents: A("#f0d030"),
					snowBury: 1f, groupMetres: 3f, groupSize: 10f, sink: new Vector2(0.005f, 0.015f)),
				// Safflower: spiny grey-green stems, orange-yellow thistle heads (ditch-fed plots by the Horde towns).
				D("FlowersSafflower", DetailKind.Flowers, 6, 0.7f, 0.08f, 0.3f, "#6a7a5a", "#7a8a64", accents: A("#f09a20", "#f0c030"),
					snowBury: 0.4f, groupMetres: 8f, groupSize: 20f, sink: new Vector2(0.01f, 0.03f)),
				// Tartary buckwheat: red stems under green-white flower sprays.
				D("FlowersTartaryBuckwheat", DetailKind.Flowers, 6, 0.6f, 0.06f, 0.3f, "#5a7a3a", "#8a3a2a", accents: A("#e8ecd8", "#d8e0c0"),
					snowBury: 0.4f, groupMetres: 8f, groupSize: 20f, sink: new Vector2(0.01f, 0.03f)),
				// White-earth wormwood (Artemisia terrae-albae): THE steppe scent, a grey-silver feathery semi-shrub, rust-gold in autumn.
				D("WormwoodWhiteEarth", DetailKind.Shrub, 6, 0.25f, 0.2f, 0.2f, "#9aa090", "#b8bca8", accents: A("#c8a050"), snowBury: 0.6f,
					groupMetres: 4f, groupSize: 10f, sink: new Vector2(0.02f, 0.04f)),
				// Biyurgun (Anabasis salsa): jointed succulent tufts, grey-green going rose-purple in autumn.
				D("Biyurgun", DetailKind.Shrub, 7, 0.15f, 0.12f, 0.15f, "#7a8a6a", "#a06a8a", snowBury: 0.8f,
					variant: FloraVariant.ShrubSucculent, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.01f, 0.03f)),
				// Izen (Kochia prostrata): a wiry grey-green fodder subshrub going rust-red.
				D("Izen", DetailKind.Shrub, 6, 0.4f, 0.15f, 0.2f, "#7a8a6a", "#9a5a3a", accents: A("#a85a30"), snowBury: 0.5f,
					groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.02f, 0.04f)),
				// Nanophyton: hard grey hedgehog cushions on the porphyry grit.
				D("Nanophyton", DetailKind.Cushion, 40, 0.1f, 0.12f, 0.2f, "#8a9080", "#9aa090", snowBury: 0.8f,
					variant: FloraVariant.CushionSpiny, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.01f, 0.02f)),
				// Wandering manna lichen: loose grey-white knobbly balls blown into drifts in the hollows.
				D("LichenManna", DetailKind.Lichen, 2, 0.03f, 0.02f, 0.15f, "#c8c4b4", "#dcd8c8", accents: A("#a8a490"), snowBury: 1f,
					variant: FloraVariant.LichenClump, groupMetres: 4f, groupSize: 12f, sink: new Vector2(0.005f, 0.015f)),
				// The black soil crust between the tussocks: cyanobacterial skin with green-gold Xanthoparmelia rosettes.
				D("LichenSoilCrust", DetailKind.Lichen, 2, 0.01f, 0.006f, 0.15f, "#2a2620", "#3a3628", accents: A("#a8a84a"), snowBury: 1f,
					variant: FloraVariant.LichenLava, groupMetres: 4f, groupSize: 10f, sink: new Vector2(0.005f, 0.015f)),
				// A harvester-ant disc: a metre of bare ground ringed with chaff and seed husks.
				D("DebrisHarvesterDisc", DetailKind.DebrisNeedle, 10, 0f, 0.3f, 0.6f, "#8a6e50", "#c8b480", accents: A("#d8c890", "#6a4e34"), snowBury: 1f,
					groupMetres: 5f, groupSize: 3f, sink: new Vector2(0f, 0.01f)),
				// Shed feather-grass awns windrowed into silver mats, straw.
				D("DebrisAwns", DetailKind.DebrisNeedle, 12, 0f, 0.2f, 0.5f, "#e0dccc", "#c8bc98", accents: A("#d8cc98", "#b8a878"), snowBury: 1f,
					groupMetres: 6f, groupSize: 10f, sink: new Vector2(0f, 0.01f)),
				// A bleached saiga skull with amber lyre horns, now and then a horse's (a bone tint on the branch's bark). LATER (I9b): skull meshes.
				D("DebrisSaigaSkull", DetailKind.DebrisBranch, 2, 0.3f, 0.02f, 0.25f, "#e8e2d4", "#c8a868", snowBury: 1f,
					groupMetres: 6f, groupSize: 2f, sink: new Vector2(0f, 0.02f)),

				// ── Estuary (biome round, 2026-10-26): the Frisian Wadden's saltings, on the existing kinds in the draft's colours ──
				// Small cordgrass (Spartina maritima): stiff tussocks on the pioneer mud, paired erect spikes.
				D("GrassCordgrass", DetailKind.Grass, 12, 0.4f, 0.012f, 0.2f, "#4a6a4a", "#7a947a", lean: 0.15f, accents: A("#8a7a4a"),
					variant: FloraVariant.GrassPlume, groupMetres: 4f, groupSize: 10f, sink: new Vector2(0.02f, 0.04f)),
				// Saltmarsh-grass (Puccinellia): decumbent glaucous blades, purple-tinged panicles.
				D("GrassSaltmarsh", DetailKind.Grass, 14, 0.22f, 0.008f, 0.2f, "#5a7a6a", "#8aa498", lean: 0.6f, accents: A("#8a6a7a"),
					variant: FloraVariant.GrassPlume, groupMetres: 5f, groupSize: 14f, sink: new Vector2(0.01f, 0.03f)),
				// Red fescue of the grazed kwelder: cropped to a lawn, red-purple at the base.
				D("GrassFescueRed", DetailKind.Grass, 14, 0.08f, 0.006f, 0.2f, "#4a5a3a", "#6a7a4a", lean: 0.3f, accents: A("#7a3a3a"),
					variant: FloraVariant.GrassPlume, groupMetres: 5f, groupSize: 14f, sink: new Vector2(0.01f, 0.02f)),
				// Saltmarsh rush (Juncus gerardii): the dark "black marsh" patches with brown capsule clusters.
				D("GrassRushGerard", DetailKind.Grass, 12, 0.35f, 0.012f, 0.2f, "#2e3e2a", "#4a5a34", lean: 0.2f, accents: A("#5a3a20"),
					variant: FloraVariant.GrassRush, groupMetres: 4f, groupSize: 10f, sink: new Vector2(0.02f, 0.04f)),
				// Sea couch (Elytrigia atherica): rigid blue-grey spikes on the creek levees and dike feet.
				D("GrassSeaCouch", DetailKind.Grass, 10, 0.8f, 0.02f, 0.25f, "#6a8a8a", "#9ab0b0", lean: 0.2f, accents: A("#a8a888"),
					variant: FloraVariant.GrassPlume, groupMetres: 5f, groupSize: 8f, sink: new Vector2(0.02f, 0.04f)),
				// Sea club-rush (Bolboschoenus maritimus): triangular stems and rust spikelet clusters beside the channels.
				D("GrassSeaClubrush", DetailKind.Grass, 10, 1f, 0.025f, 0.25f, "#4a6a3a", "#7a9a4a", lean: 0.25f, accents: A("#7a4a2a"),
					variant: FloraVariant.GrassRush, groupMetres: 5f, groupSize: 8f, sink: new Vector2(0.02f, 0.04f)),
				// The upper estuary's reed (low salinity): dark purple plumes fading to buff, along the river channels only.
				D("ReedsBrackish", DetailKind.Reeds, 14, 2.4f, 0.015f, 0.35f, "#8a9a5a", "#5e7a4a", lean: 0.2f, accents: A("#4a2a3a", "#c8b898"),
					variant: FloraVariant.ReedsPlume, groupMetres: 6f, groupSize: 16f, sink: new Vector2(0.03f, 0.06f)),
				// Sea lavender (Limonium vulgare): flat-topped mauve corymbs over basal rosettes, the July haze.
				D("FlowersSeaLavender", DetailKind.Flowers, 8, 0.35f, 0.06f, 0.3f, "#4a6a4a", "#5a7a52", accents: A("#8a6ac0", "#b8a0e0", "#9a7ad0"),
					groupMetres: 5f, groupSize: 14f, sink: new Vector2(0.01f, 0.02f)),
				// Thrift (Armeria): tight cushions, globe heads on slender scapes, along the dike flanks.
				D("FlowersThrift", DetailKind.Flowers, 6, 0.2f, 0.05f, 0.25f, "#4a6a3a", "#5e7e44", accents: A("#e890b0", "#f0b8cc"),
					groupMetres: 4f, groupSize: 10f, sink: new Vector2(0.01f, 0.02f)),
				// The kwelder's small flowers: sea milkwort, lesser sea-spurrey, scurvy-grass, sea arrowgrass.
				D("FlowersKwelder", DetailKind.Flowers, 8, 0.12f, 0.04f, 0.25f, "#4a6a3a", "#6a7a3a", accents: A("#e8a8b8", "#e88aa8", "#f4f4ee", "#6a7a3a"),
					groupMetres: 4f, groupSize: 12f, sink: new Vector2(0.01f, 0.02f)),
				// Golden samphire (Limbarda): fleshy linear leaves and gold rayed heads, the warm reach's levee flower.
				D("FlowersGoldenSamphire", DetailKind.Flowers, 5, 0.5f, 0.06f, 0.3f, "#6a8a5a", "#7a9a62", accents: A("#e8c030"),
					groupMetres: 5f, groupSize: 8f, sink: new Vector2(0.02f, 0.04f)),
				// Glasswort (Salicornia): jointed succulent candelabra, green in summer, claret and orange in autumn — the pioneer.
				D("Glasswort", DetailKind.Shrub, 12, 0.25f, 0.01f, 0.15f, "#5a8a3a", "#6e9a40", accents: A("#8a2a3a", "#c8602a"), snowBury: 0.8f,
					variant: FloraVariant.ShrubSucculent, groupMetres: 5f, groupSize: 16f, sink: new Vector2(0.02f, 0.04f)),
				// Annual sea-blite (Suaeda maritima): bushy cylindrical leaves, glaucous to purple-red.
				D("SeaBlite", DetailKind.Shrub, 8, 0.25f, 0.015f, 0.18f, "#5a7a6a", "#8a3a4a", snowBury: 0.8f,
					variant: FloraVariant.ShrubSucculent, groupMetres: 5f, groupSize: 10f, sink: new Vector2(0.02f, 0.04f)),
				// Sea plantain: a rosette of fleshy strap leaves under slender spikes.
				D("SeaPlantain", DetailKind.BroadHerb, 6, 0.2f, 0.25f, 0.15f, "#4a6a3a", "#5e7e44", segments: 3, accents: A("#8a8a5a", "#e8d870"), snowBury: 0.5f,
					groupMetres: 5f, groupSize: 6f, sink: new Vector2(0.02f, 0.04f)),
				// Scurvy-grass: glossy kidney leaves under white four-petalled clusters, the sailors' herb.
				D("ScurvyGrass", DetailKind.BroadHerb, 6, 0.2f, 0.18f, 0.15f, "#3a6a2a", "#4e7e34", segments: 3, accents: A("#f4f4ee"), snowBury: 0.5f,
					groupMetres: 4f, groupSize: 6f, sink: new Vector2(0.02f, 0.04f)),
				// Sea wormwood: feathery silver foliage and brown-gold flower spikes on the high marsh.
				D("SeaWormwood", DetailKind.DryShrub, 14, 0.4f, 0.2f, 0.25f, "#a8b0a4", "#c0c8bc", snowBury: 0.5f, accents: A("#a08a4a"),
					groupMetres: 5f, groupSize: 5f, sink: new Vector2(0.02f, 0.04f)),
				// Teek: the marsh's flood-line drift of straw-gold dead reed and cordgrass matted with wrack, feathers, a cork float.
				D("Teek", DetailKind.DebrisWrack, 8, 0f, 0.3f, 0.6f, "#c8b888", "#3a3420", accents: A("#e8e4d8", "#8a6a3a"), snowBury: 1f,
					groupMetres: 6f, groupSize: 8f, sink: new Vector2(0f, 0.015f)),
				// Flax (the water-retted linseed fields): fine stems, sky-blue flowers, round bolls.
				D("CropFlax", DetailKind.Shrub, 8, 0.8f, 0.04f, 0.25f, "#6a8a5a", "#7a9a68", accents: A("#7a9ae0", "#b8a070"), snowBury: 0.4f,
					variant: FloraVariant.ShrubHerb, groupMetres: 8f, groupSize: 20f, sink: new Vector2(0.02f, 0.05f)),
				// Gold-of-pleasure (Camelina): pale yellow flower sprays and pear-shaped pods.
				D("CropCamelina", DetailKind.Shrub, 8, 0.7f, 0.04f, 0.25f, "#6a8a4a", "#8aa05a", accents: A("#e8e08a", "#c8b878"), snowBury: 0.4f,
					variant: FloraVariant.ShrubHerb, groupMetres: 8f, groupSize: 20f, sink: new Vector2(0.02f, 0.05f)),
				// Madder (Rubia): sprawling whorled leaves, tiny yellow-green flowers, red root-crowns at the soil.
				D("CropMadder", DetailKind.Shrub, 6, 1f, 0.2f, 0.3f, "#4a6a34", "#5e7e40", accents: A("#a8b050", "#a03a2a"), snowBury: 0.4f,
					variant: FloraVariant.ShrubHerb, groupMetres: 8f, groupSize: 16f, sink: new Vector2(0.02f, 0.05f)),
				// ── Beach (biome round, 2026-10-26): the Baltic amber strand's own grasses, flowers, runners, cushions, litter and drift,
				// cool band (grey dune) and warm band (sea-oats and sea-grape), on the existing kinds ──
				// Marram (Ammophila): rolled, stiff, sharp grey-green blades in tussocks, the straw spike.
				D("GrassMarram", DetailKind.Grass, 14, 0.9f, 0.004f, 0.3f, "#6a7a5a", "#a8b090", lean: 0.25f, accents: A("#c8b890"),
					healthy: paleHealthy, dry: paleDry, variant: FloraVariant.GrassEar, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.03f, 0.06f)),
				// Lyme grass (Leymus): broad glaucous blue blades under wheat-like spikes.
				D("GrassLyme", DetailKind.Grass, 12, 0.8f, 0.012f, 0.3f, "#4a6a7a", "#8aa0b0", lean: 0.3f, accents: A("#a8a080"),
					healthy: paleHealthy, dry: paleDry, variant: FloraVariant.GrassEar, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.03f, 0.06f)),
				// Grey hair-grass (Corynephorus): fine blue-grey tufts, purple panicles.
				D("GrassGreyHair", DetailKind.Grass, 18, 0.25f, 0.0015f, 0.18f, "#6a7a7a", "#a8a0b0", lean: 0.3f, accents: A("#a890b0"),
					healthy: paleHealthy, dry: paleDry, variant: FloraVariant.GrassPlume, groupMetres: 4f, groupSize: 10f, sink: new Vector2(0.01f, 0.03f)),
				// Sea-oats (Uniola): tall arching blades under heavy golden flat oat panicles, the warm strand's grass.
				D("GrassSeaOats", DetailKind.Grass, 12, 1.4f, 0.008f, 0.3f, "#6a7a4a", "#a8a868", lean: 0.35f, accents: A("#d0b070"),
					healthy: paleHealthy, dry: paleDry, variant: FloraVariant.GrassEar, groupMetres: 5f, groupSize: 8f, sink: new Vector2(0.03f, 0.06f)),
				// The cool strand's flowers: sea holly's steel-blue spiny heads, sea rocket lilac, sea pea purple.
				D("FlowersAmberStrand", DetailKind.Flowers, 6, 0.3f, 0.05f, 0.3f, "#8aa0b0", "#6a8aa0", accents: A("#6a7ab8", "#c8a0d8", "#8a4aa0"),
					snowBury: 0.9f, healthy: paleHealthy, dry: paleDry, groupMetres: 6f, groupSize: 6f, sink: new Vector2(0.01f, 0.02f)),
				// The warm strand's flowers: sea daffodil's white lilies, beach sunflower, pink beach bean.
				D("FlowersWarmStrand", DetailKind.Flowers, 6, 0.35f, 0.05f, 0.3f, "#5a7a3a", "#6a8a4a", accents: A("#f4f2ea", "#f0c020", "#d87aa8"),
					snowBury: 0.9f, healthy: paleHealthy, dry: paleDry, groupMetres: 6f, groupSize: 6f, sink: new Vector2(0.01f, 0.02f)),
				// Railroad vine (Ipomoea pes-caprae) runs 10 m over the warm sand; sea bindweed (Calystegia soldanella) scrambles on the cool.
				D("BeachVineRailroad", DetailKind.Creeper, 30, 0.1f, 0.09f, 1.4f, "#3e6a2e", "#548a3a", accents: A("#c84a9a", "#7a4a30"), snowBury: 0.9f,
					variant: FloraVariant.CreeperBeachVine, groupMetres: 6f, groupSize: 8f, sink: new Vector2(0.005f, 0.015f)),
				D("SeaBindweedRunner", DetailKind.Creeper, 30, 0.08f, 0.08f, 1f, "#3e6a3a", "#548a4a", accents: A("#f0c0d0", "#f4f0ec"), snowBury: 0.9f,
					variant: FloraVariant.CreeperBeachVine, groupMetres: 6f, groupSize: 8f, sink: new Vector2(0.005f, 0.015f)),
				// Sea kale (Crambe): wavy glaucous cabbage leaves under white honey-scented flower domes.
				D("SeaKale", DetailKind.BroadHerb, 6, 0.5f, 0.5f, 0.15f, "#7a9aa0", "#8aaab0", segments: 3, accents: A("#f4f2e8"), snowBury: 0.2f,
					groupMetres: 8f, groupSize: 3f, sink: new Vector2(0.02f, 0.04f)),
				// Sea sandwort (Honckenya): succulent yellow-green star mats, the first green above the tide.
				D("SeaSandwort", DetailKind.Cushion, 60, 0.05f, 0.1f, 0.2f, "#8aa040", "#a0b450", snowBury: 1f,
					variant: FloraVariant.CushionMoss, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.005f, 0.015f)),
				// Reindeer and cup lichens of the grey dune (red cup tips).
				D("LichenGreyDune", DetailKind.Lichen, 2, 0.08f, 0.006f, 0.15f, "#c8ccb8", "#a8b098", accents: A("#b03a2a", "#c8ccb8"), snowBury: 1f,
					variant: FloraVariant.LichenClump, groupMetres: 6f, groupSize: 12f, sink: new Vector2(0.005f, 0.015f)),
				// Dune star moss (Syntrichia): rosettes gold-green wet, brown dry, the dune-binding moss.
				D("MossDuneStar", DetailKind.Cushion, 72, 0.05f, 0.09f, 0.2f, "#8a9a3a", "#7a6a4a", snowBury: 1f,
					variant: FloraVariant.CushionMoss, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.005f, 0.015f)),
				// The strandline: black eelgrass and bladderwrack mats, lignite crumbs and a glint of amber.
				D("DebrisAmberWrack", DetailKind.DebrisWrack, 34, 0.3f, 0.035f, 1f, "#1e1a12", "#3a3022", accents: A("#2a2418", "#5a4a30", "#d88a20"), snowBury: 1f,
					groupMetres: 8f, groupSize: 10f, sink: new Vector2(0f, 0.01f)),
				// Amber pebbles 1–4 cm, translucent honey, cherry and bone; the warm band's drift is copal.
				D("AmberDrift", DetailKind.DebrisNeedle, 12, 0f, 0.05f, 0.5f, "#d88a20", "#e0c080", accents: A("#d88a20", "#8a2a10", "#e8d8a0"), snowBury: 1f,
					groupMetres: 6f, groupSize: 10f, sink: new Vector2(0f, 0.01f)),
				// Drift seeds of the warm sea: sea-hearts, nickar nuts and coconut husk halves.
				D("SeaBeans", DetailKind.DebrisNeedle, 8, 0f, 0.07f, 0.5f, "#5a3a22", "#8a6a44", accents: A("#6a4a2a", "#9a7a50", "#c8b090"), snowBury: 1f,
					groupMetres: 6f, groupSize: 5f, sink: new Vector2(0f, 0.01f)),
				// Lugworm casts on the cool wet sand, ghost-crab burrows with their fans of sand balls on the warm.
				D("LugwormCasts", DetailKind.DebrisNeedle, 12, 0f, 0.06f, 0.5f, "#6a6252", "#4a4236", accents: A("#6a6252", "#5a5244"), snowBury: 1f,
					groupMetres: 4f, groupSize: 10f, sink: new Vector2(0f, 0.01f)),
				D("CrabBurrows", DetailKind.DebrisNeedle, 10, 0f, 0.08f, 0.5f, "#a89a80", "#2a2622", accents: A("#a89a80", "#e8dcc0", "#2a2622"), snowBury: 1f,
					groupMetres: 4f, groupSize: 8f, sink: new Vector2(0f, 0.01f)),
				// Fallen coconut fronds and husks under the palms.
				D("DebrisCoconut", DetailKind.DebrisPalm, 3, 2.5f, 0.6f, 1f, "#8a7a4a", "#a89a60", accents: A("#6a4a2a", "#7a5a34"), snowBury: 1f,
					groupMetres: 6f, groupSize: 4f, sink: new Vector2(0f, 0.01f)),
				// ── Hills (biome round, 2026-10-26): the terraced schist hill country's own grasses, forbs, herbs, crops and litter ──
				// Sheep's fescue hill turf, burning tawny in summer.
				D("GrassSheepFescue", DetailKind.Grass, 18, 0.15f, 0.003f, 0.15f, "#4a5a2a", "#8a9048", lean: 0.3f, accents: A("#8a7a5a"),
					variant: FloraVariant.GrassPlume, groupMetres: 4f, groupSize: 10f, sink: new Vector2(0.01f, 0.03f)),
				// Silverbeard (Hyparrhenia hirta) on the warm slopes: red-brown stems, silky white hairy spikelets.
				D("GrassSilverbeard", DetailKind.Grass, 12, 0.9f, 0.005f, 0.25f, "#6a6a3a", "#b08a4a", lean: 0.35f, accents: A("#e8e4dc", "#a05a3a"), snowBury: 0.3f,
					healthy: paleHealthy, dry: paleDry, variant: FloraVariant.GrassPlume, groupMetres: 5f, groupSize: 10f, sink: new Vector2(0.02f, 0.05f)),
				// Mat-grass (Nardus stricta) of the cold fells: bleached wiry tufts, one-sided spikes.
				D("GrassMatgrass", DetailKind.Grass, 16, 0.25f, 0.002f, 0.15f, "#5a6a3a", "#b0aa80", lean: 0.3f, accents: A("#6a5a6a"),
					variant: FloraVariant.GrassPlume, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.01f, 0.03f)),
				// Hairy melick on the rocks and terrace walls: fluffy cream plumes.
				D("GrassMelick", DetailKind.Grass, 12, 0.5f, 0.004f, 0.18f, "#5a6a40", "#9aa070", lean: 0.35f, accents: A("#f0ece0"),
					variant: FloraVariant.GrassPlume, groupMetres: 3f, groupSize: 6f, sink: new Vector2(0.02f, 0.04f)),
				// Asphodel, the grazed hills' signature: branched candelabras of white stars with brown midstripes over strap rosettes.
				D("FlowersAsphodel", DetailKind.Flowers, 5, 1f, 0.06f, 0.25f, "#5a6a3a", "#6a7a44", accents: A("#f4f2ec", "#8a5a3a"),
					snowBury: 0.3f, groupMetres: 5f, groupSize: 8f, sink: new Vector2(0.02f, 0.04f)),
				// The hills' spring: hoop-petticoat daffodil, grape hyacinth, Romulea lilac, sowbread pink.
				D("FlowersHillSpring", DetailKind.Flowers, 7, 0.15f, 0.04f, 0.25f, "#4a6a34", "#5a7a3a", accents: A("#f0d040", "#4a4ab0", "#a080d0", "#d878a8"),
					snowBury: 0.8f, groupMetres: 3f, groupSize: 10f, sink: new Vector2(0.01f, 0.02f)),
				// On the thin stony soil: silver nailwort, purple viper's-bugloss, hawkweed, centaury.
				D("FlowersSchist", DetailKind.Flowers, 7, 0.3f, 0.05f, 0.25f, "#5a6a4a", "#6a7a54", accents: A("#e8e8e0", "#6a4ab0", "#f0c830", "#e070a0"),
					snowBury: 0.6f, groupMetres: 3f, groupSize: 10f, sink: new Vector2(0.01f, 0.03f)),
				// The terrace and field edges: corn poppy, crown daisy, wild gladiolus, fumitory.
				D("FlowersTerraceWeeds", DetailKind.Flowers, 7, 0.5f, 0.05f, 0.3f, "#4a6a34", "#5a7a3a", accents: A("#d82818", "#f0d020", "#c03a8a", "#d8a0c0"),
					snowBury: 0.4f, groupMetres: 3f, groupSize: 10f, sink: new Vector2(0.01f, 0.03f)),
				// The fells' thrift, sheep's-bit and rock saxifrage.
				D("FlowersFell", DetailKind.Flowers, 7, 0.12f, 0.04f, 0.25f, "#4a6a3a", "#5a7a42", accents: A("#e090b0", "#8090e0", "#f4f4ee"),
					snowBury: 1f, groupMetres: 3f, groupSize: 10f, sink: new Vector2(0.01f, 0.02f)),
				// Saffron crocus on the terraces: lilac-purple cups, red stigmas, grass leaves.
				D("FlowersSaffron", DetailKind.Flowers, 7, 0.12f, 0.04f, 0.2f, "#4a6a3a", "#5a7a42", accents: A("#8a5ac0", "#c02010"),
					snowBury: 0.8f, groupMetres: 8f, groupSize: 20f, sink: new Vector2(0.01f, 0.02f)),
				// Two-row hill barley: nodding bearded heads.
				D("GrassBarleyHill", DetailKind.Grass, 10, 0.7f, 0.02f, 0.2f, "#6a8a3a", "#c8b070", lean: 0.25f, accents: A("#c8b070"), snowBury: 0.4f,
					healthy: paleHealthy, dry: paleDry, variant: FloraVariant.GrassEar, groupMetres: 8f, groupSize: 20f, sink: new Vector2(0.02f, 0.04f)),
				// Bush-trained goblet vines on a short stake: gnarled black stock, palmate leaves, purple clusters, gold-russet in autumn.
				D("CropVineGoblet", DetailKind.Shrub, 6, 1.1f, 0.3f, 0.35f, "#4a7a2c", "#6a8a34", accents: A("#3a1a3a", "#3a2e24"), snowBury: 0.3f,
					variant: FloraVariant.ShrubHerb, groupMetres: 8f, groupSize: 20f, sink: new Vector2(0.03f, 0.06f)),
				// Lavender (Lavandula latifolia / stoechas): grey-green cushions under purple spikes with rabbit-ear bracts.
				D("HerbLavender", DetailKind.Shrub, 6, 0.5f, 0.25f, 0.25f, "#6a7a5e", "#7a8a6a", accents: A("#6a4ab0", "#b070c0"), snowBury: 0.4f,
					groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.02f, 0.05f)),
				// Immortelle (Helichrysum): a silver mound with flat mustard heads, on the schist.
				D("HerbImmortelle", DetailKind.Shrub, 6, 0.35f, 0.2f, 0.2f, "#a8ac98", "#b8bca8", accents: A("#d8b030"), snowBury: 0.5f,
					groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.02f, 0.04f)),
				// Rue: a blue-grey lacy clump with yellow cups, by the hamlets and hill-forts.
				D("HerbRue", DetailKind.Shrub, 6, 0.6f, 0.12f, 0.25f, "#7a9088", "#8aa098", accents: A("#e0c830"), snowBury: 0.4f,
					variant: FloraVariant.ShrubHerb, groupMetres: 4f, groupSize: 6f, sink: new Vector2(0.02f, 0.04f)),
				// Winter savory: a low dark cushion with white-lilac flowers.
				D("HerbSavory", DetailKind.Cushion, 48, 0.15f, 0.12f, 0.25f, "#3e5a2e", "#4a6a36", accents: A("#e0d0e8"), snowBury: 0.8f,
					variant: FloraVariant.CushionThrift, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.01f, 0.02f)),
				// Giant fennel (Ferula communis): a feathery rosette under a 2.2 m stem of gold umbels, drying to straw.
				D("GiantFennel", DetailKind.Shrub, 5, 2.2f, 0.15f, 0.25f, "#5a8a3a", "#6a9a44", accents: A("#e8c020"), snowBury: 0.2f,
					variant: FloraVariant.ShrubHerb, groupMetres: 5f, groupSize: 4f, sink: new Vector2(0.03f, 0.06f)),
				// Caesar's mushroom: orange caps out of white egg volvas, gold gills, under the cork and downy oaks.
				D("MushroomCaesar", DetailKind.Mushroom, 4, 0.15f, 0.08f, 0.25f, "#e0601a", "#e87a2a", accents: A("#f4f0e6", "#e8c040", "#e0601a", "#f0ece0"),
					snowBury: 1f, variant: FloraVariant.MushroomForest, groupMetres: 4f, groupSize: 3f, sink: new Vector2(0.005f, 0.015f)),
				// The dry country's weathering: crustose map lichen, orange Xanthoria and grey Parmelia rosettes flat on the schist.
				D("LichenSchist", DetailKind.Lichen, 2, 0.015f, 0.005f, 0.12f, "#c8c040", "#e0802a", accents: A("#a8aca0"), snowBury: 1f,
					variant: FloraVariant.LichenLava, groupMetres: 4f, groupSize: 10f, sink: new Vector2(0.005f, 0.015f)),
				// The litter of grazed stony hills: schist shards, curled oak and olive leaves, snail shells, wool tufts, cork crumbs.
				D("DebrisHill", DetailKind.DebrisNeedle, 12, 0f, 0.2f, 0.5f, "#8a6a3a", "#6a7458", accents: A("#7e7a6c", "#ece4d4", "#e8e0d0"), snowBury: 1f,
					groupMetres: 6f, groupSize: 10f, sink: new Vector2(0f, 0.01f)),

				// ── Wetlands (biome round, 2026-10-26): the Batiha reed marsh's own plants, on the existing kinds in the draft's colours ──
				// Qasab, the giant reed: culms of 4–5 m, leaves flagging sideways, purple-brown plumes silvering with age.
				D("ReedsQasab", DetailKind.Reeds, 14, 4.5f, 0.025f, 0.4f, "#8a9a5a", "#5e7a3a", lean: 0.25f, accents: A("#6a4a5a", "#c8c0b8"),
					variant: FloraVariant.ReedsPlume, groupMetres: 6f, groupSize: 20f, sink: new Vector2(0.03f, 0.06f)),
				// Bardi, narrow-leaf cattail: strap leaves under a slim chocolate spike with a pale pollen spike above.
				D("ReedsBardi", DetailKind.Reeds, 12, 2.8f, 0.012f, 0.3f, "#5a7a3a", "#7a9a4a", lean: 0.2f, accents: A("#4a2e1e", "#c8a84a"),
					variant: FloraVariant.ReedsPlume, groupMetres: 6f, groupSize: 14f, sink: new Vector2(0.03f, 0.06f)),
				// Bulrush (Schoenoplectus): round leafless grey-green stems standing in the water, brown spikelet tufts near the tip.
				D("GrassBulrush", DetailKind.Grass, 10, 2f, 0.016f, 0.3f, "#4a6a4a", "#6e8a6e", lean: 0.15f, accents: A("#6a4a2a"),
					variant: FloraVariant.GrassRush, groupMetres: 5f, groupSize: 12f, sink: new Vector2(0.03f, 0.06f)),
				// Knotgrass (Paspalum distichum): the grazed buffalo lawn.
				D("GrassKnotgrass", DetailKind.Grass, 14, 0.12f, 0.008f, 0.2f, "#3e5a2a", "#7a9a4a", lean: 0.5f, accents: A("#b0a070"),
					variant: FloraVariant.GrassPlume, groupMetres: 5f, groupSize: 14f, sink: new Vector2(0.01f, 0.02f)),
				// Flat-sedge (Cyperus): leafy tufts under bronze umbels.
				D("GrassFlatsedge", DetailKind.Grass, 10, 0.9f, 0.016f, 0.25f, "#4a6a34", "#7a8a4a", lean: 0.3f, accents: A("#8a6a3a"),
					variant: FloraVariant.GrassPlume, groupMetres: 5f, groupSize: 8f, sink: new Vector2(0.02f, 0.04f)),
				// Saltgrass (Aeluropus): stiff glaucous mats on the sabkha.
				D("GrassSaltgrass", DetailKind.Grass, 14, 0.22f, 0.006f, 0.2f, "#6a7a6a", "#9aaa9a", lean: 0.3f, accents: A("#b8b0a0"),
					variant: FloraVariant.GrassPlume, groupMetres: 5f, groupSize: 12f, sink: new Vector2(0.01f, 0.03f)),
				// Sweet flag (Acorus): sword leaves with a side horn-spadix, in the cool reach.
				D("GrassSweetFlag", DetailKind.Grass, 8, 1f, 0.03f, 0.25f, "#5a6a3a", "#7a9a4a", lean: 0.2f, accents: A("#8a8a3a"),
					variant: FloraVariant.GrassRush, groupMetres: 5f, groupSize: 6f, sink: new Vector2(0.02f, 0.04f)),
				// Flowering rush: pink umbels with red-brown stamens on tall scapes.
				D("FlowersButomus", DetailKind.Flowers, 5, 1.3f, 0.05f, 0.3f, "#5a7a3a", "#6e8e44", accents: A("#e8a0b0", "#8a3a2a"),
					groupMetres: 6f, groupSize: 6f, sink: new Vector2(0.02f, 0.04f)),
				// The warm reach's levee flowers: water mint, marsh mallow, fleabane, Persian mallow.
				D("FlowersBatiha", DetailKind.Flowers, 8, 0.6f, 0.05f, 0.3f, "#4a6a34", "#6a8a44", accents: A("#b8a0d0", "#f0c8d0", "#e8c030", "#c83a6a"),
					groupMetres: 5f, groupSize: 10f, sink: new Vector2(0.02f, 0.04f)),
				// The cool reach's delta flowers: yellow loosestrife, marsh woundwort, water forget-me-not, marsh sow-thistle.
				D("FlowersDelta", DetailKind.Flowers, 8, 0.8f, 0.05f, 0.3f, "#4a6a34", "#6a8a44", accents: A("#e8c830", "#b05a8a", "#7aa0e0", "#f0d040"),
					groupMetres: 5f, groupSize: 10f, sink: new Vector2(0.02f, 0.04f)),
				// The marsh's own litter: broken culms, plume fluff, curled leaf strips, cut ends.
				D("DebrisReed", DetailKind.DebrisNeedle, 12, 0f, 0.3f, 0.5f, "#c8b88a", "#8a7a52", accents: A("#c8b88a", "#8a7a52", "#e8e4d4"), snowBury: 1f,
					groupMetres: 6f, groupSize: 10f, sink: new Vector2(0f, 0.01f)),
				// Reed harvest: cut-reed stooks and bound bundles in a patch of stubble.
				D("ReedSheaves", DetailKind.DebrisBamboo, 3, 1.2f, 0.12f, 0.5f, "#c8b078", "#a89a6a", snowBury: 0.5f,
					groupMetres: 8f, groupSize: 4f, sink: new Vector2(0f, 0.02f)),
				// Halocnemum: jointed glaucous succulent knots turning coral-red, the salt flats' own (not Estuary's glasswort).
				D("Halocnemum", DetailKind.Cushion, 20, 0.12f, 0.1f, 0.25f, "#7a9a7a", "#b04a3a", snowBury: 0.8f,
					variant: FloraVariant.CushionMoss, groupMetres: 5f, groupSize: 8f, sink: new Vector2(0.01f, 0.02f)),
				// Licorice: pinnate leaves and lilac-blue pea spikes on the levees.
				D("Licorice", DetailKind.BroadHerb, 6, 1f, 0.3f, 0.2f, "#4a6a3a", "#5e7e44", segments: 3, accents: A("#9a8ad0"), snowBury: 0.4f,
					groupMetres: 5f, groupSize: 5f, sink: new Vector2(0.02f, 0.04f)),
				// Water plantain: oval long-stalked leaves and airy white-pink panicles in the shallows.
				D("WaterPlantain", DetailKind.BroadHerb, 6, 0.8f, 0.25f, 0.2f, "#4a7a3a", "#5e8e44", segments: 3, accents: A("#f0e4e8"), snowBury: 0.4f,
					groupMetres: 5f, groupSize: 5f, sink: new Vector2(0.02f, 0.04f)),
				// Shaggy ink caps on the dung-rich levees: white scaly columns inking black at the rim.
				D("MushroomInkcap", DetailKind.Mushroom, 4, 0.15f, 0.04f, 0.3f, "#ece8e0", "#e0dcd2", accents: A("#ece8e0", "#1e1a18"), snowBury: 1f,
					variant: FloraVariant.MushroomSwamp, groupMetres: 4f, groupSize: 4f, sink: new Vector2(0.005f, 0.015f)),
				// Pointed spired snail shells and mussel valves in heaps: the marl marsh's signature.
				D("ShellsMelanopsis", DetailKind.Shells, 6, 0.03f, 0f, 0.35f, "#ecebe0", "#6a6a60",
					accents: new[] { H("#ecebe0"), H("#6a6a60"), H("#4a4a52"), H("#d8d4c8") }, snowBury: 0f,
					groupMetres: 5f, groupSize: 10f, sink: new Vector2(0f, 0.01f)),
				// Potsherds: buff, red-slipped, turquoise-glazed, cuneiform-stamped brick bats, shed by the tells.
				D("Sherds", DetailKind.DebrisNeedle, 10, 0f, 0.1f, 0.5f, "#c89a6a", "#8a3a22", accents: A("#c89a6a", "#8a3a22", "#3a9a9a", "#b8a070"), snowBury: 1f,
					groupMetres: 6f, groupSize: 8f, sink: new Vector2(0f, 0.01f)),
				// Buffalo-dung fuel cakes, slapped to dry and stacked in domes.
				D("DungCakes", DetailKind.DebrisNeedle, 6, 0f, 0.2f, 0.4f, "#4a3e2a", "#5a4c34", accents: A("#4a3e2a", "#5a4c34"), snowBury: 1f,
					groupMetres: 6f, groupSize: 5f, sink: new Vector2(0f, 0.015f)),
				// The marsh's red-awned rice: bronze-red awns over a gold panicle (not the swamp's gold rice).
				D("CropRiceAnbar", DetailKind.Shrub, 10, 1.2f, 0.04f, 0.25f, "#5a8a3a", "#7a9a48", accents: A("#a04a2a", "#c8a050"), snowBury: 0.4f,
					variant: FloraVariant.ShrubHerb, groupMetres: 8f, groupSize: 24f, sink: new Vector2(0.02f, 0.05f)),
				// Six-row barley: glaucous leaves and long-awned heads (the cool reach's only grain).
				D("CropBarleySixRow", DetailKind.Shrub, 10, 0.8f, 0.04f, 0.25f, "#6a8a6a", "#8aa070", accents: A("#d8c070"), snowBury: 0.4f,
					variant: FloraVariant.ShrubHerb, groupMetres: 8f, groupSize: 24f, sink: new Vector2(0.02f, 0.05f)),
				// Sugarcane: purple-green culms under arching leaves, water in the furrows.
				D("CropSugarcane", DetailKind.Shrub, 6, 3f, 0.2f, 0.3f, "#6a4a5a", "#7a9a4a", accents: A("#6a9a3a"), snowBury: 0.3f,
					variant: FloraVariant.ShrubHerb, groupMetres: 8f, groupSize: 16f, sink: new Vector2(0.02f, 0.05f)),

				// ── Jungle (biome round, 2026-10-26): the Edo-Yoruba rainforest's own plants, on the existing kinds in the draft's colours ──
				// Shade grass (Olyra type): short bamboo-like lance blades on wiry stems, sparse.
				D("GrassForestBroad", DetailKind.Grass, 12, 0.45f, 0.012f, 0.25f, "#2a4a1e", "#4a7a2e", lean: 0.4f,
					groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.02f, 0.04f)),
				// Spear grass (Imperata): stiff upright blades under silky white plumes, of the fallows and the pavements.
				D("GrassSpear", DetailKind.Grass, 10, 1.0f, 0.006f, 0.2f, "#4a5a2a", "#8a9a4a", lean: 0.3f, accents: A("#f2f0ea"), variant: FloraVariant.GrassPlume,
					groupMetres: 8f, groupSize: 16f, sink: new Vector2(0.02f, 0.04f)),
				// Alligator pepper (Aframomum): leafy canes, pink-white trumpets at the ground, scarlet pods.
				D("FlowersAlligatorPepper", DetailKind.Flowers, 6, 1.3f, 0.05f, 0.3f, "#3a6a2a", "#4a7a34", accents: A("#f0c8d0", "#c8301e"), snowBury: 0.4f,
					groupMetres: 6f, groupSize: 8f, sink: new Vector2(0.01f, 0.03f)),
				// Spiral ginger (Costus): corkscrew stems under a red cone with white-yellow flowers.
				D("FlowersCostus", DetailKind.Flowers, 6, 1.6f, 0.05f, 0.3f, "#3a6a2a", "#4a7a34", accents: A("#b8201e", "#f4ecc8"), snowBury: 0.4f,
					groupMetres: 6f, groupSize: 6f, sink: new Vector2(0.01f, 0.03f)),
				// Forest balsam (Impatiens): succulent pink-red spurred flowers over glossy leaves.
				D("FlowersBalsam", DetailKind.Flowers, 8, 0.4f, 0.04f, 0.3f, "#3e6a2a", "#557f30", accents: A("#e0507a", "#f08aa8"), snowBury: 0.4f,
					groupMetres: 4f, groupSize: 10f, sink: new Vector2(0.01f, 0.02f)),
				// Blood lily (Scadoxus): one scarlet pincushion sphere on a speckled stalk, a rainy-season rarity.
				D("FlowersBloodLily", DetailKind.Flowers, 3, 0.5f, 0.04f, 0.3f, "#3a6a2a", "#4a7a34", accents: A("#e02a1a"), snowBury: 0.4f,
					groupMetres: 6f, groupSize: 3f, sink: new Vector2(0.01f, 0.02f)),
				// Arrowroot (Thaumatococcus): single huge glossy paddle leaves on tall petioles, the leaf-wrap of every market.
				D("Arrowroot", DetailKind.BroadHerb, 5, 1.8f, 0.6f, 0.2f, "#2e5a24", "#4a7a30", segments: 3, snowBury: 0.2f,
					groupMetres: 5f, groupSize: 6f, sink: new Vector2(0.03f, 0.06f)),
				// Palisota: a rosette of strap leaves under a pink-white flower head and red berries, the understorey's jewel.
				D("Palisota", DetailKind.Rosette, 8, 1.2f, 0.07f, 0.15f, "#3a6a2a", "#4e7e34", segments: 3, accents: A("#e8c8d0", "#b81e1e"), snowBury: 0.2f, variant: FloraVariant.RosetteYucca,
					groupMetres: 6f, groupSize: 4f, sink: new Vector2(0.03f, 0.06f)),
				// Pteris/Tectaria: broad dark fronds with pale midribs.
				D("FernRainforest", DetailKind.Fern, 7, 1.0f, 0.45f, 0.03f, "#2a5a20", "#3e7028", segments: 4, snowBury: 0.4f,
					groupMetres: 4f, groupSize: 6f, sink: new Vector2(0.04f, 0.08f)),
				// Spikemoss mats with a blue-green iridescent sheen on the wet rock and the ravine floor.
				D("Selaginella", DetailKind.Creeper, 80, 0.04f, 0.12f, 0.7f, "#2a5a5a", "#3e7a6a", snowBury: 1.0f, variant: FloraVariant.CreeperIvy,
					groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.005f, 0.015f)),
				// Cubitermes termite mounds: grey-brown clay stalks under stacked caps. LATER (I9b): a mound mesh of its own; stalked caps stand in.
				D("TermiteCaps", DetailKind.Mushroom, 4, 0.45f, 0.1f, 0.3f, "#5a4a3a", "#7a6a52", accents: A("#6a5a48", "#8a7a62"), snowBury: 1.0f, variant: FloraVariant.MushroomCluster,
					groupMetres: 6f, groupSize: 3f, sink: new Vector2(0.005f, 0.015f)),
				// Termitomyces: tall pointed-umbo caps on white stems, rainy-season food.
				D("MushroomTermite", DetailKind.Mushroom, 4, 0.25f, 0.04f, 0.35f, "#8a7a6a", "#a89a88", accents: A("#e8e0d0", "#8a7a6a"), snowBury: 1.0f, variant: FloraVariant.MushroomForest,
					groupMetres: 4f, groupSize: 4f, sink: new Vector2(0.005f, 0.015f)),
				// Lampcap (fantasy): clusters of tiny bell caps, pale green. LATER (I2m(d) glow material): their night glow; pale caps stand in.
				D("MushroomLampcap", DetailKind.Mushroom, 12, 0.06f, 0.02f, 0.15f, "#9ad0a0", "#7ac090", accents: A("#c8f0c0", "#a8e0a8"), snowBury: 1.0f, variant: FloraVariant.MushroomCave,
					groupMetres: 3f, groupSize: 5f, sink: new Vector2(0.005f, 0.015f)),
				// The floor in relief: bronze leathery leaves, winged obeche seeds, split kola pods, palm-nut husks, a striped snail shell.
				D("DebrisRainforest", DetailKind.DebrisNeedle, 12, 0f, 0.25f, 0.6f, "#6a4a2a", "#4a3020", accents: A("#8a5a2a", "#2a1a10", "#c8b090"), snowBury: 1.0f,
					groupMetres: 6f, groupSize: 12f, sink: new Vector2(0f, 0.01f)),
				// Canopy fall under lianas and iroko: metre-long Entada pods, breadfruit heads, monkey-chewed fruit.
				D("DebrisPods", DetailKind.DebrisNeedle, 5, 0f, 0.3f, 0.5f, "#6a4a2a", "#4a6a2a", accents: A("#6a4a2a", "#4a6a2a", "#b8a070"), snowBury: 1.0f,
					groupMetres: 8f, groupSize: 4f, sink: new Vector2(0f, 0.01f)),
				// Scent leaf: a bushy aromatic herb with pale flower spikes, a herb grove's.
				D("HerbScentLeaf", DetailKind.Shrub, 6, 0.9f, 0.1f, 0.25f, "#4a7a2e", "#5a8a3a", accents: A("#e8e8d8"), snowBury: 0.4f, variant: FloraVariant.ShrubHerb,
					groupMetres: 4f, groupSize: 6f, sink: new Vector2(0.02f, 0.05f)),
				// Yam: heart-leaved vines twining up stakes, one to a mound.
				D("CropYam", DetailKind.Creeper, 30, 0.9f, 0.2f, 0.9f, "#3a6a2a", "#4a7a34", snowBury: 0.8f, variant: FloraVariant.CreeperBeachVine,
					groupMetres: 8f, groupSize: 20f, sink: new Vector2(0.005f, 0.015f)),
				// Egusi: melon runners, yellow flowers and striped green melons under the field stumps.
				D("CropEgusi", DetailKind.Creeper, 30, 0.3f, 0.2f, 0.9f, "#4a6a2a", "#5a7a34", accents: A("#f0d040", "#4a7a2a"), snowBury: 0.9f, variant: FloraVariant.CreeperBeachVine,
					groupMetres: 8f, groupSize: 20f, sink: new Vector2(0.005f, 0.015f)),
				// African rice (Oryza glaberrima): red-brown hulls on the bottomland.
				D("CropRiceAfrican", DetailKind.Grass, 10, 1.1f, 0.02f, 0.2f, "#7a9a5a", "#c8b070", lean: 0.2f, accents: A("#8a4a2a"), variant: FloraVariant.GrassEar,
					groupMetres: 8f, groupSize: 20f, sink: new Vector2(0.02f, 0.04f)),
				// ── Bamboo Forest (biome round, 2026-10-26): the Heian bamboo hills' own plants, on the existing kinds in the draft's colours ──
				// Susuki (Miscanthus): arching clumps, silver-pink plumes turning white in autumn.
				D("GrassSusuki", DetailKind.Grass, 14, 1.6f, 0.01f, 0.3f, "#3e5a2a", "#8a9a5a", lean: 0.6f, accents: A("#d8c8c0"), variant: FloraVariant.GrassPlume,
					groupMetres: 6f, groupSize: 12f, sink: new Vector2(0.03f, 0.06f)),
				// Hakone grass: arching golden-green cascades on wet banks and cliff feet.
				D("GrassHakone", DetailKind.Grass, 12, 0.45f, 0.008f, 0.25f, "#4a6a2a", "#b0c050", lean: 0.8f,
					groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.02f, 0.04f)),
				// Mondo grass: dark low strap tufts with cobalt berries, the temple ground cover.
				D("GrassRyunohige", DetailKind.Grass, 16, 0.18f, 0.004f, 0.2f, "#1e3018", "#2e4a24", lean: 0.3f, accents: A("#2a3a8a"), variant: FloraVariant.GrassPlume,
					groupMetres: 4f, groupSize: 10f, sink: new Vector2(0.01f, 0.03f)),
				// Red spider lily: bare stalks, crimson spidery umbels at the equinox on paddy bunds and graves.
				D("FlowersHiganbana", DetailKind.Flowers, 6, 0.45f, 0.04f, 0.3f, "#3e5a2a", "#557f30", accents: A("#c8141a", "#e83a2a"), snowBury: 0.4f,
					groupMetres: 6f, groupSize: 10f, sink: new Vector2(0.01f, 0.02f)),
				// The autumn seven: pink nadeshiko, yellow ominaeshi, mauve fujibakama, blue kikyo.
				D("FlowersAkiNanakusa", DetailKind.Flowers, 10, 0.6f, 0.04f, 0.3f, "#4a6a34", "#5a7a3a", accents: A("#e8a0c0", "#e8d040", "#c8a0c8", "#6a5ac8"), snowBury: 0.4f,
					groupMetres: 6f, groupSize: 12f, sink: new Vector2(0.01f, 0.03f)),
				// Fringed iris: pale lavender over glossy fans, the bamboo-shade flower.
				D("FlowersShaga", DetailKind.Flowers, 8, 0.4f, 0.05f, 0.3f, "#3a6a2a", "#4e7e34", accents: A("#c8b8e0", "#e8a030"), snowBury: 0.4f,
					groupMetres: 5f, groupSize: 8f, sink: new Vector2(0.01f, 0.02f)),
				// Dogtooth violet: nodding reflexed pink-purple over mottled leaves, a spring-only ravine flower.
				D("FlowersKatakuri", DetailKind.Flowers, 14, 0.15f, 0.04f, 0.3f, "#4a6a3a", "#5a7a44", accents: A("#c86aa8"), snowBury: 0.4f,
					groupMetres: 5f, groupSize: 14f, sink: new Vector2(0.01f, 0.02f)),
				// Bamboo shoots: brown-spotted sheath cones 0.2-1.2 m, the grove's spring. LATER (I9b P29): season fade.
				D("Takenoko", DetailKind.DebrisBamboo, 8, 0.7f, 0.12f, 0.4f, "#8a6a40", "#6a4a2a", snowBury: 0.6f,
					groupMetres: 5f, groupSize: 5f, sink: new Vector2(0f, 0.02f)),
				// Moso culm sheaths: big papery tan blades blotched dark brown, curled.
				D("DebrisCulmSheath", DetailKind.DebrisNeedle, 12, 0f, 0.25f, 0.6f, "#b89a6a", "#4a2a18", accents: A("#b89a6a", "#4a2a18", "#c4b070"), snowBury: 1.0f,
					groupMetres: 6f, groupSize: 12f, sink: new Vector2(0f, 0.01f)),
				// The bamboo-cutter's stubs: oblique-cut hollow culm stumps with pale node rings.
				D("DebrisCutCulm", DetailKind.DebrisBamboo, 4, 0.35f, 0.07f, 0.4f, "#c8c08a", "#8a8a52", snowBury: 0.8f,
					groupMetres: 6f, groupSize: 4f, sink: new Vector2(0f, 0.02f)),
				// Japanese painted fern: silver-grey fronds with wine-red midribs.
				D("FernKurawarabi", DetailKind.Fern, 6, 0.6f, 0.35f, 0.03f, "#8a9aa0", "#a8b4b0", segments: 4, snowBury: 0.4f,
					groupMetres: 4f, groupSize: 6f, sink: new Vector2(0.04f, 0.08f)),
				// Royal fern: woolly cinnamon fiddleheads in spring, gathered as sansai.
				D("FernZenmai", DetailKind.Fern, 6, 0.8f, 0.4f, 0.04f, "#5a7a34", "#a8784a", segments: 4, snowBury: 0.4f,
					groupMetres: 4f, groupSize: 5f, sink: new Vector2(0.04f, 0.08f)),
				// Houttuynia: heart leaves under four white bracts, the grove herb of shaded villages.
				D("HerbDokudami", DetailKind.Shrub, 8, 0.3f, 0.12f, 0.25f, "#3e6a2a", "#557f30", accents: A("#f4f2ea"), snowBury: 0.4f, variant: FloraVariant.ShrubHerb,
					groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.02f, 0.04f)),
				// Swertia: white stars striped purple, a bitter tonic of sunny banks.
				D("HerbSenburi", DetailKind.Shrub, 6, 0.6f, 0.1f, 0.25f, "#4a6a34", "#5a7a3a", accents: A("#ece8f0", "#7a4a8a"), snowBury: 0.4f, variant: FloraVariant.ShrubHerb,
					groupMetres: 4f, groupSize: 5f, sink: new Vector2(0.02f, 0.04f)),
				// Geranium thunbergii: pink and white saucers, the proof-of-efficacy herb.
				D("HerbGennosho", DetailKind.Shrub, 6, 0.4f, 0.12f, 0.25f, "#4a6a34", "#5a7a3a", accents: A("#e8a0b8", "#f4eef0"), snowBury: 0.4f, variant: FloraVariant.ShrubHerb,
					groupMetres: 4f, groupSize: 5f, sink: new Vector2(0.02f, 0.04f)),
				// Veiled lady: a white lacy net skirt under an olive cap, only in the bamboo groves.
				D("MushroomKinugasa", DetailKind.Mushroom, 5, 0.2f, 0.05f, 0.3f, "#8a8a52", "#a0a064", accents: A("#f4f2ea", "#6a6a3a"), snowBury: 1.0f, variant: FloraVariant.MushroomForest,
					groupMetres: 4f, groupSize: 3f, sink: new Vector2(0.005f, 0.015f)),
				// Matsutake: stout brown-capped stems in rings under the red pines.
				D("MushroomMatsutake", DetailKind.Mushroom, 6, 0.12f, 0.07f, 0.3f, "#7a5a3a", "#9a7a52", accents: A("#c8b490", "#e8dcc0"), snowBury: 1.0f, variant: FloraVariant.MushroomForest,
					groupMetres: 4f, groupSize: 5f, sink: new Vector2(0.005f, 0.015f)),
				// Shiitake: brown cracked caps shelved on fallen logs. LATER (I9b): LogShelf placement; ground clusters stand in.
				D("MushroomShiitake", DetailKind.Mushroom, 8, 0.06f, 0.04f, 0.2f, "#7a5a3a", "#9a7a52", accents: A("#b89a72", "#e8dcc0"), snowBury: 1.0f, variant: FloraVariant.MushroomCluster,
					groupMetres: 3f, groupSize: 5f, sink: new Vector2(0.005f, 0.015f)),
				// Cushion moss domes on stones, roots and lantern bases, the moss garden's look.
				D("MossKoke", DetailKind.Cushion, 70, 0.14f, 0.1f, 0.25f, "#2e5a1a", "#4e7e2a", snowBury: 1.0f, variant: FloraVariant.CushionMoss,
					groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.01f, 0.02f)),
				// Paddy rice: tufted green turning gold with drooping panicles.
				D("CropRice", DetailKind.Grass, 14, 0.9f, 0.014f, 0.2f, "#5a9a3a", "#d8b850", lean: 0.5f, accents: A("#d8b850"), variant: FloraVariant.GrassEar,
					groupMetres: 8f, groupSize: 20f, sink: new Vector2(0.02f, 0.04f)),
				// Buckwheat: red stems under white froth flowers.
				D("CropSoba", DetailKind.Flowers, 8, 0.6f, 0.05f, 0.3f, "#8a3a2a", "#6a8a4a", accents: A("#f4f0ea"), snowBury: 0.4f,
					groupMetres: 8f, groupSize: 20f, sink: new Vector2(0.01f, 0.03f)),
				// Hemp: tall palmate stems grown for fibre.
				D("CropAsa", DetailKind.Shrub, 7, 2.0f, 0.12f, 0.2f, "#4a6a2a", "#6a8a3a", snowBury: 0.4f, variant: FloraVariant.ShrubHerb,
					groupMetres: 8f, groupSize: 20f, sink: new Vector2(0.02f, 0.05f)),
				// ── Mangrove (biome round, 2026-10-26): the Swahili coast's mangal, on the existing kinds in the draft's colours ──
				// Saltcouch (Sporobolus virginicus): stiff distichous creeping couch on the ukanda rims.
				D("GrassSaltCouch", DetailKind.Grass, 12, 0.3f, 0.008f, 0.2f, "#5a7a5a", "#9aaa88", lean: 0.2f, accents: A("#a89a70"),
					variant: FloraVariant.GrassPlume, groupMetres: 5f, groupSize: 12f, sink: new Vector2(0.01f, 0.03f)),
				// Fimbristylis: fine sedge of the landward mud, brown spikelet balls.
				D("GrassSedgeFimbristylis", DetailKind.Grass, 12, 0.5f, 0.008f, 0.2f, "#4a6a34", "#6a8a44", lean: 0.3f, accents: A("#6a4a24"),
					variant: FloraVariant.GrassPlume, groupMetres: 5f, groupSize: 10f, sink: new Vector2(0.02f, 0.04f)),
				// Sea purslane (Sesuvium): trailing red succulent stems and pink stars.
				D("FlowersSesuvium", DetailKind.Flowers, 6, 0.08f, 0.05f, 0.3f, "#5a7a3a", "#a04a4a", accents: A("#e070b0", "#f0a0cc"),
					groupMetres: 4f, groupSize: 10f, sink: new Vector2(0.005f, 0.015f)),
				// Arabian jasmine round the houses: a low twiggy shrublet of white stars.
				D("FlowersAsmini", DetailKind.Flowers, 5, 0.5f, 0.05f, 0.3f, "#4a6a34", "#5e7e40", accents: A("#f6f4ee"),
					groupMetres: 4f, groupSize: 5f, sink: new Vector2(0.01f, 0.03f)),
				// Pencil pneumatophores of Avicennia: grey-brown, a barnacle collar at mid-tide — the carpet.
				D("PneumatophoresAvicennia", DetailKind.RootKnobs, 30, 0.22f, 0.012f, 0.7f, "#7a6e5c", "#a89c86", snowBury: 0f,
					variant: FloraVariant.Pneumatophores, groupMetres: 6f, groupSize: 16f, sink: new Vector2(0.03f, 0.06f)),
				// Stout woody cones of Sonneratia, oyster-crusted at the foot.
				D("PneumatophoresSonneratia", DetailKind.RootKnobs, 8, 0.7f, 0.04f, 1f, "#6e6454", "#b0a690", snowBury: 0f,
					variant: FloraVariant.Pneumatophores, groupMetres: 8f, groupSize: 8f, sink: new Vector2(0.04f, 0.08f)),
				// Bruguiera knee roots: looped knuckle-knees with corky lenticels, arcs rather than the swamp's cones.
				D("KneeRoots", DetailKind.RootKnobs, 14, 0.3f, 0.05f, 0.8f, "#8a7a68", "#b8a894", snowBury: 0f,
					variant: FloraVariant.CypressKnees, groupMetres: 5f, groupSize: 8f, sink: new Vector2(0.03f, 0.06f)),
				// Rhizophora propagules: green-brown pencils stuck upright in the mud and lying, with seedlings.
				D("Propagules", DetailKind.Shrub, 10, 0.45f, 0.02f, 0.15f, "#5a6a2a", "#7a8a3a", snowBury: 0.2f,
					variant: FloraVariant.ShrubHorsetail, groupMetres: 5f, groupSize: 10f, sink: new Vector2(0.02f, 0.04f)),
				// Mangrove fern (Acrostichum): leathery fronds of 2–3 m, rust fertile tips, in the landward gaps.
				D("MangroveFern", DetailKind.Fern, 6, 2.2f, 0.6f, 0.04f, "#4a7a2a", "#6a9a3a", segments: 4, accents: A("#8a4a1e"), snowBury: 0.2f,
					groupMetres: 5f, groupSize: 5f, sink: new Vector2(0.04f, 0.08f)),
				// Giant mud whelks (Terebralia): dark spired shells grazing in hundreds.
				D("MudWhelks", DetailKind.Shells, 8, 0.03f, 0f, 0.3f, "#3a2a1e", "#6a5a4a",
					accents: new[] { H("#3a2a1e"), H("#6a5a4a"), H("#4a3a2a") }, snowBury: 0f,
					groupMetres: 8f, groupSize: 14f, sink: new Vector2(0f, 0.01f)),
				// Crab chimneys: fiddler and sesarmid burrow turrets with their excavated mud balls.
				D("CrabChimneys", DetailKind.DebrisNeedle, 14, 0f, 0.05f, 0.4f, "#4a4236", "#2a2620", accents: A("#4a4236", "#1e1a14", "#6a604a"), snowBury: 1f,
					groupMetres: 4f, groupSize: 14f, sink: new Vector2(0f, 0.01f)),
				// Yellow-red fallen Rhizophora leaves, crab-shredded, with propagule caps: the floor's colour.
				D("MangroveLitter", DetailKind.DebrisNeedle, 12, 0f, 0.25f, 0.5f, "#b8a040", "#a86a2a", accents: A("#b8a040", "#a86a2a", "#6a3a1e"), snowBury: 1f,
					groupMetres: 6f, groupSize: 10f, sink: new Vector2(0f, 0.01f)),
				// Curled leathery algal-mat flakes lifting off the ukanda.
				D("AlgalMatChips", DetailKind.DebrisNeedle, 12, 0f, 0.1f, 0.5f, "#3e2e1e", "#6a5030", accents: A("#3e2e1e", "#6a5030", "#8a7048"), snowBury: 1f,
					groupMetres: 5f, groupSize: 12f, sink: new Vector2(0f, 0.012f)),
				// Rihani, sweet basil: a bushy herb with purple flower spikes.
				D("Rihani", DetailKind.Shrub, 5, 0.5f, 0.15f, 0.2f, "#4a7a2a", "#5e9034", accents: A("#7a4a8a"), snowBury: 0.4f,
					variant: FloraVariant.ShrubHerb, groupMetres: 4f, groupSize: 4f, sink: new Vector2(0.02f, 0.04f)),
				// Finger millet (ufuta's neighbour in the shamba fields): fingered dark-brown heads.
				D("CropFingerMillet", DetailKind.Shrub, 8, 0.9f, 0.05f, 0.25f, "#6a8a3a", "#7a9a48", accents: A("#5a3a20"), snowBury: 0.4f,
					variant: FloraVariant.ShrubHerb, groupMetres: 8f, groupSize: 24f, sink: new Vector2(0.02f, 0.05f)),
				// Sesame (ufuta): white-pink bell flowers and upright capsules.
				D("CropSesame", DetailKind.Shrub, 8, 1.2f, 0.06f, 0.25f, "#5a7a3a", "#6e8e44", accents: A("#f0d8e0", "#8a7a4a"), snowBury: 0.4f,
					variant: FloraVariant.ShrubHerb, groupMetres: 8f, groupSize: 20f, sink: new Vector2(0.02f, 0.05f)),
				// Flood rice of the cleared back mangal (the wet reach), tall on its mud bunds.
				D("CropRiceMangrove", DetailKind.Shrub, 10, 1.4f, 0.04f, 0.25f, "#5a8a3a", "#7a9a48", accents: A("#c8a050"), snowBury: 0.4f,
					variant: FloraVariant.ShrubHerb, groupMetres: 8f, groupSize: 24f, sink: new Vector2(0.02f, 0.05f)),
				// ── Lake and River (biome round, 2026-10-26): the clear lake's strand and the Rhenish river's margin, on the existing kinds ──
				// Shoreweed (Littorella): stiff awl-leaf lawns on the wave-washed sand, and the hair-fine needle spike-rush.
				D("GrassShoreweed", DetailKind.Grass, 14, 0.08f, 0.004f, 0.25f, "#3e5a2e", "#6a8a44", lean: 0.1f,
					groupMetres: 3f, groupSize: 12f, sink: new Vector2(0.01f, 0.02f)),
				D("GrassNeedleSpike", DetailKind.Grass, 16, 0.06f, 0.0015f, 0.25f, "#4a6a34", "#7a9a4a", lean: 0.2f,
					groupMetres: 3f, groupSize: 14f, sink: new Vector2(0.01f, 0.02f)),
				// Lake bulrush (Schoenoplectus lacustris): round leafless dark culms, brown spikelet clusters; the lake's silhouette far out in the water.
				D("LakeBulrush", DetailKind.Reeds, 6, 2.4f, 0.012f, 0.35f, "#2e4a3a", "#4a6a52", lean: 0.15f, accents: A("#6a4a2a"),
					groupMetres: 6f, groupSize: 14f, sink: new Vector2(0.04f, 0.08f)),
				// The strand turf: strand forget-me-not, creeping spearwort, lake thrift, lake lobelia.
				D("FlowersStrand", DetailKind.Flowers, 8, 0.12f, 0.04f, 0.35f, "#3e6a28", "#557f30", accents: A("#6aa8e8", "#e8d040", "#e09ab8", "#d8c8ec"),
					groupMetres: 3f, groupSize: 12f, sink: new Vector2(0.01f, 0.02f)),
				// Water horsetail (Equisetum fluviatile): unbranched green-banded pins in the shallows.
				D("HorsetailWater", DetailKind.Shrub, 6, 1f, 0.03f, 0.25f, "#3a6a2a", "#4e7e34", snowBury: 0.6f,
					variant: FloraVariant.ShrubHorsetail, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.02f, 0.05f)),
				// The strandline wrack: reed chaff, needles, bark strips and gull feathers.
				D("DebrisDriftLine", DetailKind.DebrisNeedle, 12, 0f, 0.25f, 0.5f, "#a08a5a", "#4a3a26", accents: A("#a08a5a", "#4a3a26", "#e8e4dc"), snowBury: 1f,
					groupMetres: 5f, groupSize: 12f, sink: new Vector2(0f, 0.01f)),
				// Rotted oak pile heads of the drowned pile-towns, black with sponge and moss caps. LATER (I7): fields of 20-200 at 1 m spacing.
				D("PileStumps", DetailKind.RootKnobs, 4, 0.3f, 0f, 1.2f, "#1e1a14", "#3a3a2a", snowBury: 0.6f,
					variant: FloraVariant.CypressKnees, groupMetres: 12f, groupSize: 6f, sink: new Vector2(0.03f, 0.06f)),
				// Galingale (Cyperus fuscus): the mud annual, dark brown spikelet clusters.
				D("GrassGalingale", DetailKind.Grass, 12, 0.15f, 0.003f, 0.22f, "#4a5a2e", "#7a8a44", lean: 0.4f, accents: A("#2e2218"),
					variant: FloraVariant.GrassPlume, groupMetres: 3f, groupSize: 10f, sink: new Vector2(0.01f, 0.02f)),
				// Bur-reed (Sparganium erectum): keeled sword leaves and spiky green burr balls, in slow water.
				D("BurReed", DetailKind.Reeds, 8, 1.2f, 0.02f, 0.4f, "#3a5a2a", "#6a8a3a", lean: 0.35f, accents: A("#6a8a3a", "#6a4a2a"),
					groupMetres: 5f, groupSize: 10f, sink: new Vector2(0.04f, 0.08f)),
				// The Stromtal flowers (long-leaved speedwell, meadow-rue, ragwort, marsh spurge) and the gravel bar's pioneers.
				D("FlowersStromtal", DetailKind.Flowers, 6, 0.9f, 0.05f, 0.35f, "#3e6a28", "#557f30", accents: A("#5a6ad0", "#e8e090", "#f0b020", "#c8d040"),
					groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.01f, 0.02f)),
				D("FlowersFloodbank", DetailKind.Flowers, 8, 0.35f, 0.05f, 0.35f, "#5a7a3a", "#6e8e44", accents: A("#f0c8d0", "#e8c020", "#f0e040", "#c88a8a"),
					groupMetres: 4f, groupSize: 14f, sink: new Vector2(0.01f, 0.02f)),
				// Flood brash: twigs, reed stems and bark lodged against the stones, bleached.
				D("DebrisBrash", DetailKind.DebrisBranch, 5, 0.9f, 0.02f, 0.6f, "#a89a80", "#c8bc9c", snowBury: 1f,
					groupMetres: 6f, groupSize: 6f, sink: new Vector2(0f, 0.02f)),
				// ── Rocky Terrain (biome round, 2026-10-26) ── The Pamir's sparse cover: hummock sedge on the salt-ringed pamir floors,
				// hard steel-blue fescue tussocks, woolly rosettes, silver locoweed cushions, yellow and white crust lichens, dragonhead,
				// star asters, a bleached argali skull, and the mineral stonewool of the airless and thin-aired talus.
				D("GrassPamirSedge", DetailKind.Grass, 14, 0.08f, 0.005f, 0.2f, "#3a3424", "#8a8452", lean: 0.3f, accents: A("#4a3a24"),
					variant: FloraVariant.GrassPlume, groupMetres: 3f, groupSize: 10f, sink: new Vector2(0.02f, 0.04f)),
				D("GrassAlaiFescue", DetailKind.Grass, 12, 0.16f, 0.004f, 0.25f, "#4a5a5a", "#8aa0a0", lean: 0.3f, accents: A("#a8a080"),
					variant: FloraVariant.GrassPlume, groupMetres: 3f, groupSize: 8f, sink: new Vector2(0.02f, 0.04f)),
				D("GrassStonewool", DetailKind.Grass, 16, 0.05f, 0.002f, 0.2f, "#c8ccd0", "#e8ecf0", lean: 0.1f,
					groupMetres: 3f, groupSize: 8f, sink: new Vector2(0.01f, 0.03f)),
				D("FlowersAllardia", DetailKind.Flowers, 6, 0.06f, 0.05f, 0.15f, "#c0c4bc", "#c0c4bc", accents: A("#d8a0c8", "#e0b040"),
					groupMetres: 3f, groupSize: 6f, sink: new Vector2(0.01f, 0.02f)),
				D("FlowersStarAster", DetailKind.Flowers, 6, 0.11f, 0.04f, 0.2f, "#5a6a4a", "#5a6a4a", accents: A("#a890d0", "#e8c040"),
					groupMetres: 4f, groupSize: 6f, sink: new Vector2(0.01f, 0.02f)),
				D("CushionOxytropis", DetailKind.Cushion, 40, 0.09f, 0.07f, 0.3f, "#b8bcb0", "#c4c8bc", accents: A("#b03a8a", "#f0ecf0"), snowBury: 1f,
					variant: FloraVariant.CushionMossCampion, groupMetres: 4f, groupSize: 6f, sink: new Vector2(0.01f, 0.02f)),
				D("LichenAcarospora", DetailKind.Lichen, 2, 0.04f, 0.006f, 0.12f, "#d8b030", "#e4c448", accents: A("#c8a020"), snowBury: 1f,
					variant: FloraVariant.LichenClump, groupMetres: 4f, groupSize: 10f, sink: new Vector2(0.005f, 0.015f)),
				D("LichenLecanora", DetailKind.Lichen, 2, 0.03f, 0.006f, 0.12f, "#d8d8cc", "#e8e8dc", accents: A("#c87a2a"), snowBury: 1f,
					variant: FloraVariant.LichenClump, groupMetres: 4f, groupSize: 10f, sink: new Vector2(0.005f, 0.015f)),
				D("HerbDracocephalum", DetailKind.Shrub, 6, 0.17f, 0.12f, 0.2f, "#7a8a7a", "#8a9a8a", accents: A("#6a5ab0"), snowBury: 0.5f,
					variant: FloraVariant.ShrubHerb, groupMetres: 4f, groupSize: 5f, sink: new Vector2(0.02f, 0.04f)),
				D("DebrisArgaliSkull", DetailKind.DebrisBranch, 3, 0.5f, 0.05f, 0.5f, "#ece4d4", "#d8cebc", snowBury: 1f,
					groupMetres: 8f, groupSize: 3f, sink: new Vector2(0f, 0.02f)),

				// ── Geyser Basin (biome round, 2026-10-26) ── The hot ground's own cover: the warm halo's bent lawn, hot-spring panic grass,
				// fell rush, monkeyflower, fringed gentian, campion, thermal moss, adder's-tongue, moonwort, crottle lichen, the microbial
				// mats and splash sinter, mud spatter, a film of sulphur at the vent lips, wool and horse litter.
				D("GrassThermalBent", DetailKind.Grass, 16, 0.12f, 0.003f, 0.2f, "#2e5a1e", "#8ac44a", lean: 0.2f, healthy: new Color(0.7f, 1f, 0.55f), dry: new Color(0.7f, 1f, 0.55f),
					groupMetres: 3f, groupSize: 10f, sink: new Vector2(0.02f, 0.04f)),
				D("GrassHotPanic", DetailKind.Grass, 12, 0.3f, 0.006f, 0.25f, "#5a6a52", "#9aa88a", lean: 0.4f, accents: A("#8a6a8a"),
					variant: FloraVariant.GrassPlume, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.02f, 0.04f)),
				D("GrassFellRush", DetailKind.Grass, 12, 0.18f, 0.0015f, 0.2f, "#4a4a2a", "#a8622a", lean: 0.2f,
					variant: FloraVariant.GrassRush, groupMetres: 3f, groupSize: 8f, sink: new Vector2(0.02f, 0.04f)),
				D("FlowersHverMonkey", DetailKind.Flowers, 10, 0.25f, 0.05f, 0.2f, "#5a9a3a", "#5a9a3a", accents: A("#f0c820", "#a83a20"),
					groupMetres: 3f, groupSize: 6f, sink: new Vector2(0.01f, 0.02f)),
				D("FlowersThermalGentian", DetailKind.Flowers, 6, 0.25f, 0.04f, 0.2f, "#4a6a3a", "#4a6a3a", accents: A("#4a48c8"),
					groupMetres: 4f, groupSize: 6f, sink: new Vector2(0.01f, 0.02f)),
				D("FlowersHolurt", DetailKind.Flowers, 5, 0.12f, 0.05f, 0.2f, "#6a7a7a", "#6a7a7a", accents: A("#f4f2ec", "#b8a0b0"),
					groupMetres: 3f, groupSize: 6f, sink: new Vector2(0.01f, 0.02f)),
				D("MossThermal", DetailKind.Cushion, 60, 0.03f, 0.06f, 0.15f, "#2e6a1a", "#5aa83a", snowBury: 1f,
					variant: FloraVariant.CushionMossCampion, groupMetres: 4f, groupSize: 8f, sink: new Vector2(0.005f, 0.015f)),
				D("AdderTongue", DetailKind.Shrub, 5, 0.08f, 0.08f, 0.1f, "#5a8a3a", "#6a9a4a", snowBury: 0.5f,
					variant: FloraVariant.ShrubHerb, groupMetres: 3f, groupSize: 4f, sink: new Vector2(0.01f, 0.03f)),
				D("Moonwort", DetailKind.Shrub, 5, 0.12f, 0.08f, 0.1f, "#6a8a5a", "#7a9a6a", accents: A("#c8a84a"), snowBury: 0.5f,
					variant: FloraVariant.ShrubHerb, groupMetres: 3f, groupSize: 3f, sink: new Vector2(0.01f, 0.03f)),
				D("LichenCrottle", DetailKind.Lichen, 2, 0.03f, 0.006f, 0.12f, "#c8c0b8", "#d8d0c8", accents: A("#b08a90"), snowBury: 1f,
					variant: FloraVariant.LichenClump, groupMetres: 4f, groupSize: 10f, sink: new Vector2(0.005f, 0.015f)),
				D("MatHverasly", DetailKind.Lichen, 4, 0.03f, 0.03f, 0.2f, "#c8641e", "#e0b040", accents: A("#6a7a2a", "#f0e0b0"), snowBury: 0f,
					variant: FloraVariant.LichenLava, groupMetres: 4f, groupSize: 10f, sink: new Vector2(0.005f, 0.015f)),
				D("SinterSpicules", DetailKind.Lichen, 6, 0.04f, 0.01f, 0.1f, "#ecebe4", "#d8d4c8", accents: A("#d8b8b0"), snowBury: 0.5f,
					variant: FloraVariant.LichenClump, groupMetres: 3f, groupSize: 8f, sink: new Vector2(0.005f, 0.015f)),
				D("MudSpatter", DetailKind.DebrisNeedle, 8, 0f, 0.2f, 0.3f, "#7a7570", "#a8a29a", accents: A("#4e4a46"), snowBury: 1f,
					groupMetres: 4f, groupSize: 6f, sink: new Vector2(0f, 0.01f)),
				D("SulphurFilm", DetailKind.Lichen, 3, 0.02f, 0.008f, 0.1f, "#e8d870", "#f0e48a", snowBury: 0.5f,
					variant: FloraVariant.LichenClump, groupMetres: 3f, groupSize: 6f, sink: new Vector2(0.005f, 0.015f)),
				D("DebrisWool", DetailKind.DebrisNeedle, 6, 0f, 0.15f, 0.4f, "#d8ccb0", "#3a302a", accents: A("#d8ccb0", "#3a302a"), snowBury: 1f,
					groupMetres: 5f, groupSize: 6f, sink: new Vector2(0f, 0.01f)),
				D("DebrisHorse", DetailKind.DebrisNeedle, 6, 0f, 0.2f, 0.4f, "#4a3a28", "#5a4a34", accents: A("#4a3a28", "#6a6a66"), snowBury: 1f,
					groupMetres: 5f, groupSize: 6f, sink: new Vector2(0f, 0.01f)),
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
					deadwood = new[]
					{
						new DeadwoodSpecies { Name = "FallenLog_Oak", Form = DeadwoodForm.FallenLog, Length = 10f, Radius = 0.4f, BarkFamily = Bark.Brown,
							BarkTint = H("#e8e0d8"), WoodA = H("#c8a878"), WoodB = H("#8a6844"), Stubs = 3 },
						new DeadwoodSpecies { Name = "FallenLog_Conifer", Form = DeadwoodForm.FallenLog, Length = 12f, Radius = 0.32f, BarkFamily = Bark.Pine,
							BarkTint = H("#e0d8d0"), WoodA = H("#d8b888"), WoodB = H("#a07a50"), Stubs = 4 },
						new DeadwoodSpecies { Name = "FallenLog_Birch", Form = DeadwoodForm.FallenLog, Length = 7f, Radius = 0.2f, BarkFamily = Bark.Birch,
							BarkTint = H("#f0ece6"), WoodA = H("#b89a70"), WoodB = H("#7a5e40"), Stubs = 2 },
						new DeadwoodSpecies { Name = "Stump_Broken", Form = DeadwoodForm.Stump, Length = 0.9f, Radius = 0.35f, BarkFamily = Bark.Brown,
							BarkTint = H("#e8e0d8"), WoodA = H("#c8a878"), WoodB = H("#7a5a38") },
						new DeadwoodSpecies { Name = "Stump_Cut", Form = DeadwoodForm.Stump, Length = 0.45f, Radius = 0.3f, BarkFamily = Bark.Pine,
							BarkTint = H("#e8e0d8"), WoodA = H("#e0c498"), WoodB = H("#b08a5a"), Sawn = true },
						new DeadwoodSpecies { Name = "Stump_Bracket", Form = DeadwoodForm.Stump, Length = 0.7f, Radius = 0.38f, BarkFamily = Bark.Brown,
							BarkTint = H("#d8d0c8"), WoodA = H("#a88a62"), WoodB = H("#6a4e30"), Brackets = 5,
							Bands = new[] { H("#e8dcc0"), H("#a07a50"), H("#6a5038") } },
						new DeadwoodSpecies { Name = "Driftwood", Form = DeadwoodForm.Driftwood, Length = 4f, Radius = 0.16f, BarkFamily = Bark.Dead,
							BarkTint = H("#f4f0ea"), WoodA = H("#c8c2b6"), WoodB = H("#a8a296"), Stubs = 3 },
						new DeadwoodSpecies { Name = "PetrifiedLog", Form = DeadwoodForm.PetrifiedLog, Length = 7f, Radius = 0.42f, BarkFamily = Bark.Dead,
							BarkTint = H("#c89a7a"), WoodA = H("#6a4434"), WoodB = H("#9a7a64"),
							Bands = new[] { H("#5a3a2e"), H("#b0482e"), H("#d89a4a"), H("#e8c890"), H("#8a5a6a"), H("#c8beb0"), H("#a0302a") } },
						new DeadwoodSpecies { Name = "RootPlate", Form = DeadwoodForm.RootPlate, Length = 3.2f, Radius = 0.35f, BarkFamily = Bark.Brown,
							BarkTint = H("#9a8a7a"), WoodA = H("#4a3a28"), WoodB = H("#7a6448"), Stubs = 12,
							Bands = new[] { H("#8a8a84"), H("#6a6a64") } },
						// Tundra (biome round, 2026-10-26): a whole rafted spruce trunk, sanded silver, half buried on the bars and coasts.
						new DeadwoodSpecies { Name = "DriftwoodTrunk", Form = DeadwoodForm.Driftwood, Length = 10f, Radius = 0.35f, BarkFamily = Bark.Dead,
							BarkTint = H("#f0ece4"), WoodA = H("#c8c2b6"), WoodB = H("#a8a296"), Stubs = 6 },
						// Glacier (biome round, 2026-10-26): avalanche-snapped birch, the bark half stripped.
						new DeadwoodSpecies { Name = "FallenLog_AvalancheBirch", Form = DeadwoodForm.FallenLog, Length = 4.5f, Radius = 0.15f, BarkFamily = Bark.Birch,
							BarkTint = H("#e8d0c0"), WoodA = H("#c8b090"), WoodB = H("#8a6e50"), Stubs = 2 },
						// Forest (biome round, 2026-10-26): a fallen grey beech, a beech snag shelved with tinder-fungus hooves, and the
						// hollow veteran oak. LATER (I9b): a Hollow deadwood form (3 m, walk-in, open on one side); a broad shelved stump stands in.
						new DeadwoodSpecies { Name = "FallenLog_Beech", Form = DeadwoodForm.FallenLog, Length = 12f, Radius = 0.45f, BarkFamily = Bark.Smooth,
							BarkTint = H("#e0e0d8"), WoodA = H("#d0b890"), WoodB = H("#8a7050"), Stubs = 4 },
						new DeadwoodSpecies { Name = "Snag_Beech", Form = DeadwoodForm.Stump, Length = 6f, Radius = 0.5f, BarkFamily = Bark.Smooth,
							BarkTint = H("#d8d8d0"), WoodA = H("#b89a70"), WoodB = H("#6a5038"), Brackets = 6,
							Bands = new[] { H("#8a7a6a"), H("#6a5a4c"), H("#5a4a3e") } },
						new DeadwoodSpecies { Name = "HollowOak", Form = DeadwoodForm.Stump, Length = 3f, Radius = 0.8f, BarkFamily = Bark.Brown,
							BarkTint = H("#d8d0c8"), WoodA = H("#6a4e30"), WoodB = H("#3a2a1c"), Brackets = 2,
							Bands = new[] { H("#c8b890"), H("#8a6a48"), H("#5a4630") } },
						// Woodland (biome round, 2026-10-26): a shed pollard limb, dry and grey; a cut coppice stool's pale faces; an oak
						// stump with beefsteak fungus. LATER (I9b): the stool's ring of 12–20 cut stubs; a broad sawn stump stands in.
						new DeadwoodSpecies { Name = "FallenLimb_Oak", Form = DeadwoodForm.FallenLog, Length = 6f, Radius = 0.25f, BarkFamily = Bark.Dead,
							BarkTint = H("#c8c0b0"), WoodA = H("#c8b08a"), WoodB = H("#8a7458"), Stubs = 3 },
						new DeadwoodSpecies { Name = "Stump_Coppice", Form = DeadwoodForm.Stump, Length = 0.35f, Radius = 0.45f, BarkFamily = Bark.Brown,
							BarkTint = H("#e0d8d0"), WoodA = H("#d8c098"), WoodB = H("#b09870"), Sawn = true },
						new DeadwoodSpecies { Name = "Stump_Beefsteak", Form = DeadwoodForm.Stump, Length = 0.6f, Radius = 0.4f, BarkFamily = Bark.Brown,
							BarkTint = H("#d8d0c8"), WoodA = H("#a88a62"), WoodB = H("#6a4e30"), Brackets = 3,
							Bands = new[] { H("#c04a3a"), H("#8a2a22"), H("#6a2018") } },
						// Alpine Meadow (biome round, 2026-10-26): the cut grey stubs of the dwarf pine along the lanes hacked through it.
						new DeadwoodSpecies { Name = "Stump_Mugo", Form = DeadwoodForm.Stump, Length = 0.3f, Radius = 0.09f, BarkFamily = Bark.Pine,
							BarkTint = H("#a8a098"), WoodA = H("#c8b8a0"), WoodB = H("#8a7a68"), Sawn = true },
						// Mountain Slope (biome round, 2026-10-26): the nurse log, a fallen yellowwood moss-sleeved its whole length, ferns
						// sprouting from it (no stumps: the sacred forests are not felled).
						new DeadwoodSpecies { Name = "FallenLog_Podocarp", Form = DeadwoodForm.FallenLog, Length = 14f, Radius = 0.5f, BarkFamily = Bark.MossSleeve,
							BarkTint = H("#e8f0d8"), WoodA = H("#c8a070"), WoodB = H("#8a6440"), Stubs = 4 },
						// Scrubland (biome round, 2026-10-26): a standing saguaro skeleton, its wooden ribs bare, tatters of skin and the
						// woodpeckers' boots; a fallen ironwood, silver outside, iron-dark at the core. LATER (I9b): the rib cage form; a pale
						// tall stump stands in.
						new DeadwoodSpecies { Name = "Snag_Saguaro", Form = DeadwoodForm.Stump, Length = 5f, Radius = 0.3f, BarkFamily = Bark.Dead,
							BarkTint = H("#d8c8a8"), WoodA = H("#a08a68"), WoodB = H("#5a4a38"),
							Bands = new[] { H("#a08a68"), H("#5a4a38"), H("#c0a888") } },
						new DeadwoodSpecies { Name = "FallenLog_Ironwood", Form = DeadwoodForm.FallenLog, Length = 5f, Radius = 0.25f, BarkFamily = Bark.ShredGrey,
							BarkTint = H("#e0dcd4"), WoodA = H("#6a4a3a"), WoodB = H("#2e2220"), Stubs = 4 },
						// Savanna (biome round, 2026-10-26): a fallen leadwood, silver and termite-galleried; a mopane snapped and stripped by
						// elephants.
						new DeadwoodSpecies { Name = "FallenLog_Leadwood", Form = DeadwoodForm.FallenLog, Length = 9f, Radius = 0.4f, BarkFamily = Bark.SnakeSkin,
							BarkTint = H("#e0dcd4"), WoodA = H("#b0a898"), WoodB = H("#7a3a24"), Stubs = 4 },
						new DeadwoodSpecies { Name = "Stump_ElephantBroken", Form = DeadwoodForm.Stump, Length = 1.4f, Radius = 0.3f, BarkFamily = Bark.Fibrous,
							BarkTint = H("#d8d0c8"), WoodA = H("#d8c098"), WoodB = H("#8a6844"), Brackets = 0,
							Bands = new[] { H("#e8d8b8"), H("#a88a62"), H("#6a4e30") } },
						// Grassland (biome round, 2026-10-26): a lightning-split lone pear standing dead and silver, the one deadwood of a treeless plain.
						new DeadwoodSpecies { Name = "Snag_Pear", Form = DeadwoodForm.Stump, Length = 6f, Radius = 0.3f, BarkFamily = Bark.Checker,
							BarkTint = H("#c8c4bc"), WoodA = H("#c8bca8"), WoodB = H("#8a7a64"), Brackets = 0,
							Bands = new[] { H("#a89a84"), H("#7a6a58"), H("#5a4c40") } },
						// Plains (biome round, 2026-10-26): bleached cottonwood driftwood on the bars, a lightning-split cottonwood snag in the galleries.
						new DeadwoodSpecies { Name = "FallenLog_Cottonwood", Form = DeadwoodForm.Driftwood, Length = 9f, Radius = 0.4f, BarkFamily = Bark.Dead,
							BarkTint = H("#ece8e0"), WoodA = H("#c8c2b6"), WoodB = H("#a8a296"), Stubs = 6 },
						new DeadwoodSpecies { Name = "Snag_Cottonwood", Form = DeadwoodForm.Stump, Length = 7f, Radius = 0.6f, BarkFamily = Bark.CottonwoodRidge,
							BarkTint = H("#d8d4cc"), WoodA = H("#c8bca8"), WoodB = H("#8a7a64"), Brackets = 0,
							Bands = new[] { H("#a89a84"), H("#7a6a58"), H("#5a4c40") } },
						// Steppe (biome round, 2026-10-26): a twisted grey saxaul standing dead on the kum, a fallen elm bough in the dry valleys.
						new DeadwoodSpecies { Name = "Snag_Saxaul", Form = DeadwoodForm.Stump, Length = 2.5f, Radius = 0.15f, BarkFamily = Bark.SaxaulTwist,
							BarkTint = H("#c8c0b0"), WoodA = H("#b8a888"), WoodB = H("#7a6a54"), Brackets = 0,
							Bands = new[] { H("#a89a84"), H("#7a6a58"), H("#5a4c40") } },
						new DeadwoodSpecies { Name = "FallenLimb_Elm", Form = DeadwoodForm.FallenLog, Length = 4f, Radius = 0.15f, BarkFamily = Bark.Karagach,
							BarkTint = H("#e0d8d0"), WoodA = H("#c8a878"), WoodB = H("#8a6844"), Stubs = 3 },
						// Estuary (biome round, 2026-10-26): the rotted oak stakes of a lost revetment, weed-skirted, in lines along the old dike.
						new DeadwoodSpecies { Name = "DikeStakes", Form = DeadwoodForm.Stump, Length = 0.7f, Radius = 0.08f, BarkFamily = Bark.Dead,
							BarkTint = H("#a8a498"), WoodA = H("#8a8478"), WoodB = H("#5a5648") },
						// Beach (biome round, 2026-10-26): sand-blasted silver logs on the wrack line, the snags of a forest a walking dune buried.
						new DeadwoodSpecies { Name = "Driftwood_Silver", Form = DeadwoodForm.Driftwood, Length = 5f, Radius = 0.2f, BarkFamily = Bark.Dead,
							BarkTint = H("#c8c4b8"), WoodA = H("#c0bcb0"), WoodB = H("#a0a094"), Stubs = 3 },
						new DeadwoodSpecies { Name = "DuneSnag", Form = DeadwoodForm.Stump, Length = 3f, Radius = 0.25f, BarkFamily = Bark.Dead,
							BarkTint = H("#c8c4b8"), WoodA = H("#b8b4a8"), WoodB = H("#8a867a"), Brackets = 0,
							Bands = new[] { H("#b0aca0"), H("#8a867a"), H("#6a665c") } },
						// Coral Reef (biome round, 2026-10-26): coconut trunks and husks sunk on the shelf, shipworm-bored.
						new DeadwoodSpecies { Name = "DriftPalm", Form = DeadwoodForm.Driftwood, Length = 7f, Radius = 0.18f, BarkFamily = Bark.Palm,
							BarkTint = H("#a8a090"), WoodA = H("#8a7a5a"), WoodB = H("#6a5a40"), Stubs = 2 },
						// Abyss (biome round, 2026-10-26): a sunk log bored to honeycomb and pickled black; a leviathan's rib arc and vertebra.
						// LATER (I7): the leviathan fall as a piece (skull, vertebral row, rib vault, bone-worm plumes).
						new DeadwoodSpecies { Name = "BoredWood", Form = DeadwoodForm.FallenLog, Length = 4f, Radius = 0.3f, BarkFamily = Bark.Dead,
							BarkTint = H("#2a2622"), WoodA = H("#1a1612"), WoodB = H("#0e0c0a"), Stubs = 2 },
						new DeadwoodSpecies { Name = "LeviathanRib", Form = DeadwoodForm.Driftwood, Length = 9f, Radius = 0.35f, BarkFamily = Bark.Dead,
							BarkTint = H("#d8ccb0"), WoodA = H("#d8ccb0"), WoodB = H("#a89c84"), Stubs = 0 },
						new DeadwoodSpecies { Name = "LeviathanVertebra", Form = DeadwoodForm.Stump, Length = 1.2f, Radius = 0.7f, BarkFamily = Bark.Dead,
							BarkTint = H("#d8ccb0"), WoodA = H("#c8bc9c"), WoodB = H("#8a7e64"), Brackets = 0,
							Bands = new[] { H("#c02020"), H("#d8ccb0"), H("#a89c84") } },
						// Jungle (biome round, 2026-10-26): a fallen giant on its plank buttresses, moss-grown and shelved with bracket fungi; an
						// iroko killed by lightning, silver, hornbill-holed. LATER (I9b): the buttressed root end and the Lampcap clusters.
						new DeadwoodSpecies { Name = "FallenLog_Buttressed", Form = DeadwoodForm.FallenLog, Length = 16f, Radius = 0.9f, BarkFamily = Bark.Olive,
							BarkTint = H("#e0e8d0"), WoodA = H("#c8a070"), WoodB = H("#8a5a3a"), Stubs = 6, Brackets = 4 },
						new DeadwoodSpecies { Name = "Snag_Iroko", Form = DeadwoodForm.Stump, Length = 9f, Radius = 0.7f, BarkFamily = Bark.Dead,
							BarkTint = H("#c8c4bc"), WoodA = H("#c8bca8"), WoodB = H("#8a7a64"), Brackets = 0,
							Bands = new[] { H("#a89a84"), H("#7a6a58"), H("#5a4c40") } },
						// Bamboo Forest (biome round, 2026-10-26): a mossy fallen cedar with shiitake shelves, a silver red-pine snag, culms
						// collapsed criss-cross. LATER (I9b): a collapsed-culm cluster form; a lone fallen culm stands in.
						new DeadwoodSpecies { Name = "FallenLog_Sugi", Form = DeadwoodForm.FallenLog, Length = 14f, Radius = 0.6f, BarkFamily = Bark.Fibrous,
							BarkTint = H("#e0e8d0"), WoodA = H("#d8a878"), WoodB = H("#9a5a3a"), Stubs = 3, Brackets = 3 },
						new DeadwoodSpecies { Name = "Snag_Akamatsu", Form = DeadwoodForm.Stump, Length = 7f, Radius = 0.3f, BarkFamily = Bark.Dead,
							BarkTint = H("#c8c4bc"), WoodA = H("#c8bca8"), WoodB = H("#8a7a64"), Brackets = 0,
							Bands = new[] { H("#a89a84"), H("#7a6a58"), H("#5a4c40") } },
						new DeadwoodSpecies { Name = "Culm_Fallen", Form = DeadwoodForm.FallenLog, Length = 7f, Radius = 0.07f, BarkFamily = Bark.Bamboo,
							BarkTint = H("#c8c08a"), WoodA = H("#d8cc90"), WoodB = H("#a89a5a"), Stubs = 0 },
						// Desert (biome round, 2026-10-26): a dead desert date scoured silver on its deflated mound (a tall stump stands in for the
						// snag form: LATER (I9b)); the Ténéré's fossil wood, honey-brown and black agatised, broken in rounds.
						new DeadwoodSpecies { Name = "Snag_Balanites", Form = DeadwoodForm.Stump, Length = 3.5f, Radius = 0.22f, BarkFamily = Bark.Dead,
							BarkTint = H("#e0dcd4"), WoodA = H("#c8c2b6"), WoodB = H("#8a8478"),
							Bands = new[] { H("#c8c2b6"), H("#a8a296"), H("#d8d2c6") } },
						new DeadwoodSpecies { Name = "PetrifiedLog_Tenere", Form = DeadwoodForm.PetrifiedLog, Length = 5f, Radius = 0.35f, BarkFamily = Bark.Dead,
							BarkTint = H("#a87a50"), WoodA = H("#8a5a30"), WoodB = H("#2a1e18"),
							Bands = new[] { H("#8a5a30"), H("#2a1e18"), H("#c89a5a"), H("#5a3a22"), H("#e0c090") } },
						// Badlands (biome round, 2026-10-26): a silver peeling oleaster snag in the washes; the hollow stump of a fallen plane in a gorge.
						new DeadwoodSpecies { Name = "Snag_Oleaster", Form = DeadwoodForm.Stump, Length = 3f, Radius = 0.2f, BarkFamily = Bark.OleasterStrip,
							BarkTint = H("#d8d4cc"), WoodA = H("#c0b8a8"), WoodB = H("#7a6e5e"),
							Bands = new[] { H("#c0b8a8"), H("#9a8e7e"), H("#d0c8b8") } },
						new DeadwoodSpecies { Name = "Stump_PlaneHollow", Form = DeadwoodForm.Stump, Length = 2.2f, Radius = 0.9f, BarkFamily = Bark.PlaneFlake,
							BarkTint = H("#e8e4dc"), WoodA = H("#6a4e30"), WoodB = H("#3a2a1c"), Brackets = 2,
							Bands = new[] { H("#c8b890"), H("#8a6a48"), H("#5a4630") } },
						// Salt Flat (biome round, 2026-10-26): a dead acacia on an old shoreline, silver-grey, salt wicked white up its foot.
						new DeadwoodSpecies { Name = "Snag_SaltedAcacia", Form = DeadwoodForm.Stump, Length = 2.5f, Radius = 0.15f, BarkFamily = Bark.Dead,
							BarkTint = H("#e8e6e0"), WoodA = H("#c8c4b8"), WoodB = H("#8a8478"),
							Bands = new[] { H("#f2f0ea"), H("#c8c4b8"), H("#a8a296") } },
						// Hills (biome round, 2026-10-26): a cut gnarled olive stump throwing sucker shoots on the terraces.
						new DeadwoodSpecies { Name = "Stump_Olive", Form = DeadwoodForm.Stump, Length = 0.6f, Radius = 0.4f, BarkFamily = Bark.Olive,
							BarkTint = H("#e0dcd0"), WoodA = H("#c8a878"), WoodB = H("#8a6844"), Sawn = true },
						// Wetlands (biome round, 2026-10-26): a drowned palm standing in the water, a hollow pollard willow log.
						new DeadwoodSpecies { Name = "Snag_DrownedPalm", Form = DeadwoodForm.Stump, Length = 4f, Radius = 0.18f, BarkFamily = Bark.Dead,
							BarkTint = H("#8a8678"), WoodA = H("#a89a80"), WoodB = H("#6a5e4a"), Brackets = 0 },
						new DeadwoodSpecies { Name = "FallenLog_Willow", Form = DeadwoodForm.FallenLog, Length = 5f, Radius = 0.25f, BarkFamily = Bark.Dead,
							BarkTint = H("#a8a498"), WoodA = H("#b8a888"), WoodB = H("#7a6a50"), Stubs = 4 },
						// Mangrove (biome round, 2026-10-26): a silver-grey die-back Rhizophora skeleton on its prop-root cage, and the cut-pole
						// stumps of the coppiced coupes with their red heartwood faces.
						new DeadwoodSpecies { Name = "Snag_Mangrove", Form = DeadwoodForm.Stump, Length = 3.5f, Radius = 0.22f, BarkFamily = Bark.Dead,
							BarkTint = H("#a8a8a0"), WoodA = H("#b8b0a0"), WoodB = H("#7a7468"), Brackets = 0 },
						new DeadwoodSpecies { Name = "PoleStumps", Form = DeadwoodForm.Stump, Length = 0.6f, Radius = 0.1f, BarkFamily = Bark.Brown,
							BarkTint = H("#8a5a44"), WoodA = H("#a8503a"), WoodB = H("#6a3a24"), Sawn = true },
						// Lake (biome round, 2026-10-26): a bark-stripped log lying drowned in the silt, a dugout half sunk, the stumps of a forest a
						// rise drowned. LATER (I9b): the logboat's hollowed form; a long black log stands in.
						new DeadwoodSpecies { Name = "Log_Sunken", Form = DeadwoodForm.FallenLog, Length = 10f, Radius = 0.3f, BarkFamily = Bark.Dead,
							BarkTint = H("#4a4a40"), WoodA = H("#5a5a4e"), WoodB = H("#3a3a30"), Stubs = 1 },
						new DeadwoodSpecies { Name = "Logboat_Wreck", Form = DeadwoodForm.FallenLog, Length = 7f, Radius = 0.45f, BarkFamily = Bark.Dead,
							BarkTint = H("#1e1a14"), WoodA = H("#2a241c"), WoodB = H("#14100c"), Stubs = 0 },
						new DeadwoodSpecies { Name = "DrownedStump", Form = DeadwoodForm.Stump, Length = 1f, Radius = 0.5f, BarkFamily = Bark.Dead,
							BarkTint = H("#3a3a30"), WoodA = H("#4a4a3e"), WoodB = H("#2a2a22"), Brackets = 0 },
						// River (biome round, 2026-10-26): a whole uprooted tree stranded on a bar, silver-stripped, its root fan upstream; the
						// tangle of a log jam; a waterlogged trunk on the run bed; a beaver's gnawed cone stump. LATER (I9b): the jam as one piece of
						// 5-12 logs with brash (a heaped root plate stands in).
						new DeadwoodSpecies { Name = "Snag_Uprooted", Form = DeadwoodForm.RootPlate, Length = 3f, Radius = 0.4f, BarkFamily = Bark.Dead,
							BarkTint = H("#a8a090"), WoodA = H("#b8b0a0"), WoodB = H("#7a7468"), Stubs = 10,
							Bands = new[] { H("#8a8a84"), H("#6a6a64") } },
						new DeadwoodSpecies { Name = "LogJam", Form = DeadwoodForm.RootPlate, Length = 3.6f, Radius = 0.3f, BarkFamily = Bark.Dead,
							BarkTint = H("#b0a898"), WoodA = H("#c0b8a8"), WoodB = H("#8a8274"), Stubs = 16,
							Bands = new[] { H("#a89a80"), H("#8a8274") } },
						new DeadwoodSpecies { Name = "Log_Sunk_River", Form = DeadwoodForm.FallenLog, Length = 8f, Radius = 0.35f, BarkFamily = Bark.Dead,
							BarkTint = H("#3e3c34"), WoodA = H("#4e4a3e"), WoodB = H("#2e2c24"), Stubs = 2 },
						new DeadwoodSpecies { Name = "Stump_Beaver", Form = DeadwoodForm.Stump, Length = 0.45f, Radius = 0.14f, BarkFamily = Bark.Brown,
							BarkTint = H("#8a7a62"), WoodA = H("#c8a878"), WoodB = H("#a88a5a"), Sawn = true },
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
