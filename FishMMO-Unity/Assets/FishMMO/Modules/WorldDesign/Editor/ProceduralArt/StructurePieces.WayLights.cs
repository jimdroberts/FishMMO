#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using FishMMO.Shared.Biomes;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// The way and cave lights the biome suite's drafts name (2026-10-26, NEW-LIGHTS): beacons kindled at dusk, oil lamps in
	/// niches, lanterns on posts, fire bowls and hearths, torches and the cold glows of the airless worlds, each its own kit
	/// piece named by its <see cref="BiomeWayLightKind"/> (laid by id only, so no template query draws one), two variants,
	/// in the medieval and fantasy register of the drafts. What shape each is (post, wall light, niche, bowl, beacon), what it
	/// gives off and the light standing in for a wall one are <see cref="BiomeWayLights"/>' table; every builder here gives off
	/// that emitter (held by the tests).
	/// </summary>
	/// <remarks>
	/// Wall lights and niches are built as the kit's wall lights are: the back at z = 0 against the wall, the light standing out
	/// along −z, the fixing at the origin (hung at a light's height by the street pass, PointOfInterestWallLights; on the rock by
	/// a mine's drifts). Ground lights stand on y = 0, their front −z. The glows' tints are <see cref="GlowHueOf"/>, the open
	/// fires' looks (a beacon's, a brimstone flame's) <see cref="PyreLookOf"/>.
	/// </remarks>
	public static partial class StructurePieces
	{
		private static void AddWayLights(List<StructurePiece> list)
		{
			// Beacons: laid on a tower's lookout (or a hill) by PointOfInterestWayBeacons, kindled at dusk.
			WayLight(list, BiomeWayLightKind.Beacon, StructureStyle.Iron, "A beacon, 1.7 m: an iron fire basket heaped with split logs, v0 on a three-legged iron stand, v1 on a squat stone drum.", WayBeacon, StructureSize.Medium);
			WayLight(list, BiomeWayLightKind.SignalPyre, StructureStyle.Timber, "A signal pyre on a hearth of laid slabs: v0 a crib of logs in crossed courses, v1 a cone of poles over brushwood.", WaySignalPyre, StructureSize.Medium);
			WayLight(list, BiomeWayLightKind.WartFire, StructureStyle.Stone, "A wart fire, a hill's beacon: v0 a turf-walled fire pit on a cairn, v1 a stack of peats on a low drystone platform.", WayWartFire, StructureSize.Medium);
			WayLight(list, BiomeWayLightKind.BlubberPyre, StructureStyle.Iron, "A blubber pyre: v0 an iron pot of rendered fat on an iron stand over a stone ring, v1 a long stone trough of fat on a cairn.", WayBlubberPyre, StructureSize.Medium);
			WayLight(list, BiomeWayLightKind.FivePits, StructureStyle.Earth, "A five-pit beacon: five fire pits in a row along a rammed-earth platform, v0 clay rims, v1 stone rings on a stone platform.", WayFivePits, StructureSize.Medium);
			// Lamps and lanterns on posts.
			WayLight(list, BiomeWayLightKind.AmberLamp, StructureStyle.Timber, "A horn lantern with amber panes on a driftwood post: v0 set on its top, v1 hung from a crooked arm.", WayAmberLamp);
			WayLight(list, BiomeWayLightKind.CarvedLantern, StructureStyle.Stone, "A squat tuff lantern pierced with stars and crosses on a tuff post: v0 square under a pyramid cap, v1 octagonal under a dome.", WayCarvedLantern);
			WayLight(list, BiomeWayLightKind.StoneLantern, StructureStyle.Stone, "A stone lantern: v0 a tall one (base, round post, a screened firebox, a curled cap, a finial), v1 a low one on three legs under a broad cap.", WayStoneLantern);
			WayLight(list, BiomeWayLightKind.PaperLantern, StructureStyle.Timber, "A round paper lantern on a bamboo bracket from a wall: v0 a globe, v1 a long one.", WayPaperLantern);
			WayLight(list, BiomeWayLightKind.PaperLanternPost, StructureStyle.Timber, "A round paper lantern hung from the arm of a bamboo post, 2.2 m: v0 a globe, v1 a long one.", WayPaperLanternPost);
			WayLight(list, BiomeWayLightKind.NicheLamp, StructureStyle.Stone, "A clay oil lamp in a niche built out from a wall, a soot tongue over it: v0 of rough stone, v1 plastered under a gabled head.", WayNicheLamp);
			WayLight(list, BiomeWayLightKind.NicheLampPost, StructureStyle.Timber, "A carved post with a lamp niche in its head, a clay lamp in it: v0 timber, v1 a stone pillar.", WayNicheLampPost);
			WayLight(list, BiomeWayLightKind.Bellglow, StructureStyle.Timber, "A small bronze bell on a weed-hung post, its mouth glowing: v0 hung from an arm, v1 in a yoke on the post's top.", WayBellglow);
			WayLight(list, BiomeWayLightKind.LampPillar, StructureStyle.Stone, "A lamp pillar, 3.6 m: a stone shaft with tiers of lamp brackets, v0 three tiers of arms, v1 three stone rings of lamp cups.", WayLampPillar, StructureSize.Medium);
			WayLight(list, BiomeWayLightKind.Fanal, StructureStyle.Iron, "A ship's stern lantern, a tall hexagonal iron frame of horn panes under a domed cap: v0 on an iron stand, v1 on a short timber post.", WayFanal);
			WayLight(list, BiomeWayLightKind.PiercedJar, StructureStyle.Earth, "A terracotta lamp-jar pierced with triangles, hung from an iron hook on a wall: v0 round-bellied, v1 tall.", WayPiercedJar);
			WayLight(list, BiomeWayLightKind.PiercedJarPillar, StructureStyle.Stone, "A pierced terracotta lamp-jar set on a pillar of stacked slabs, 1.6 m: v0 round-bellied, v1 tall.", WayPiercedJarPillar);
			WayLight(list, BiomeWayLightKind.FireAltar, StructureStyle.Stone, "A waist-high stone fire altar: v0 stepped in three, a bowl on top; v1 a square altar with a carved band and horned corners round a basin.", WayFireAltar);
			WayLight(list, BiomeWayLightKind.IronCruse, StructureStyle.Iron, "An iron double cruse lamp hung from a hook on a wall: v0 a straight hook, v1 a scrolled one.", WayIronCruse);
			WayLight(list, BiomeWayLightKind.IronCrusePost, StructureStyle.Timber, "An iron double cruse lamp hung from a hook on a short driftwood post: v0 one hook, v1 a crossbar.", WayIronCrusePost);
			WayLight(list, BiomeWayLightKind.SlabHearth, StructureStyle.Stone, "A fire in a stone bowl on a pillar of stacked slabs: v0 tall and narrow, v1 low and broad with a corbelled top.", WaySlabHearth);
			WayLight(list, BiomeWayLightKind.Svetets, StructureStyle.Timber, "A splinter-light: an iron fork holding a burning splinter over an ember trough, v0 on a timber post, v1 on an iron tripod.", WaySvetets);
			WayLight(list, BiomeWayLightKind.IceLantern, StructureStyle.Stone, "A hollow frosted ice cylinder on a stone block, a candle inside: v0 straight, v1 tapering on a round block.", WayIceLantern);
			WayLight(list, BiomeWayLightKind.SunLens, StructureStyle.Stone, "A crystal lens in a ring frame on a stone post that gives back the day after sunset: v0 tilted to the sky, v1 upright in a fork.", WaySunLens);
			WayLight(list, BiomeWayLightKind.SergeLamp, StructureStyle.Timber, "A carved post with an iron fat-lamp cup on a hook: v0 a banded post, v1 a post with a carved head.", WaySergeLamp);
			WayLight(list, BiomeWayLightKind.KolaSpike, StructureStyle.Iron, "A soapstone cup-lamp on an iron spike driven into the ground or the ice: v0 straight, v1 twisted with a crook.", WayKolaSpike);
			WayLight(list, BiomeWayLightKind.CrescentStandard, StructureStyle.Timber, "A tall pole with a glowing crescent finial and hanging tassels: v0 the crescent alone, v1 on a crossbar with two tassels.", WayCrescentStandard);
			WayLight(list, BiomeWayLightKind.ClayLamp, StructureStyle.Earth, "A many-spouted clay oil lamp in a mud niche in a wall: v0 four spouts, v1 three under a rounded head.", WayClayLamp);
			WayLight(list, BiomeWayLightKind.ClayLampPost, StructureStyle.Timber, "A many-spouted clay oil lamp on a carved post: v0 four spouts on a dish, v1 three on a forked head.", WayClayLampPost);
			WayLight(list, BiomeWayLightKind.DrownedLamp, StructureStyle.Iron, "A sunken lamp, a green-glowing jar in a weed-grown iron cage: v0 on a stake, v1 hung from a bent stake.", WayDrownedLamp);
			WayLight(list, BiomeWayLightKind.TeaTorch, StructureStyle.Timber, "A resin-heart pine torch wedged in a crack of a wall with stones, a soot tongue over it: v0 leaning out, v1 two torches.", WayTeaTorch);
			WayLight(list, BiomeWayLightKind.HearthRing, StructureStyle.Stone, "A ring of stones round a small fire on the ground: v0 eight stones, v1 with three stones holding a kettle.", WayHearthRing).Collides = false;
			WayLight(list, BiomeWayLightKind.Polycandelon, StructureStyle.Stone, "A ring of glass oil-cups on three chains from a stone post's iron crook: v0 six cups, v1 a double ring of eight.", WayPolycandelon);
			WayLight(list, BiomeWayLightKind.CastorLamp, StructureStyle.Earth, "A clay saucer lamp under a little rib hood on a wall bracket: v0 a shelf, v1 a hook and a hanging saucer.", WayCastorLamp);
			WayLight(list, BiomeWayLightKind.CastorLampPost, StructureStyle.Timber, "A clay saucer lamp under a little rib hood on a ringed palm-trunk post: v0 on its top, v1 on a side arm.", WayCastorLampPost);
			WayLight(list, BiomeWayLightKind.Kriseilu, StructureStyle.Iron, "A flat iron oil dish with a wick spout and a rat-tail handle hung from a twisted hook on a staff: v0 one dish, v1 two on a crossbar.", WayKriseilu);
			WayLight(list, BiomeWayLightKind.WheelLamp, StructureStyle.Stone, "An iron wheel crown on a stone post, an oil lamp hung inside its spokes: v0 six spokes, v1 eight and a finial.", WayWheelLamp);
			WayLight(list, BiomeWayLightKind.VolvaMirror, StructureStyle.Stone, "A tilted metal mirror disc in a ring frame on a stone post, 2.4 m: v0 on a fork, v1 on a crossbar.", WayVolvaMirror);
			WayLight(list, BiomeWayLightKind.Plummet, StructureStyle.Stone, "A square stone post whose arm hangs a glass plumb-bob dead still on a wire, 2.6 m: v0 one arm, v1 a gallows of two.", WayPlummet);
			WayLight(list, BiomeWayLightKind.FrogLamp, StructureStyle.Iron, "An open iron oil lamp hung from a hook driven into a wall: v0 a pear-shaped pan, v1 a round one with a drip cup.", WayFrogLamp);
			WayLight(list, BiomeWayLightKind.HornBole, StructureStyle.Stone, "A flagstone niche built out from a gable or a dyke end, a horn-paned shutter at its mouth: v0 the shutter half open, v1 two flags high.", WayHornBole);
			WayLight(list, BiomeWayLightKind.HornBolePillar, StructureStyle.Stone, "A pillar of laid flags with a horn-paned lamp niche in its head: v0 square, v1 tapering.", WayHornBolePillar);
			WayLight(list, BiomeWayLightKind.Chiragh, StructureStyle.Stone, "A stone lamp-bowl in a rubble niche in a wall, a small dim flame: v0 flat-headed, v1 arched.", WayChiragh);
			WayLight(list, BiomeWayLightKind.ChiraghPillar, StructureStyle.Stone, "A stone lamp-bowl on a waist-high rubble pillar: v0 square, v1 round.", WayChiraghPillar);
			WayLight(list, BiomeWayLightKind.SaltLamp, StructureStyle.Stone, "An oil wick in a hollowed block of rock salt in a wall niche, the salt glowing: v0 one block, v1 two stacked.", WaySaltLamp);
			WayLight(list, BiomeWayLightKind.SaltLampCairn, StructureStyle.Stone, "An oil wick in a hollowed block of rock salt on a cairn post: v0 a tall cairn, v1 a squat one.", WaySaltLampCairn);
			WayLight(list, BiomeWayLightKind.SoapstoneBowl, StructureStyle.Stone, "A carved soapstone fire bowl on a waist-high plastered pillar: v0 round, v1 square with a stone cap.", WaySoapstoneBowl);
			WayLight(list, BiomeWayLightKind.Kokeul, StructureStyle.Earth, "A pine-knot fire in a clay-plastered niche built out from a wall, its flue over it: v0 square, v1 rounded.", WayKokeul);
			WayLight(list, BiomeWayLightKind.KokeulPillar, StructureStyle.Earth, "A squat clay pillar with a pine-knot fire niche and a flue: v0 square, v1 round.", WayKokeulPillar);
			WayLight(list, BiomeWayLightKind.TrayFire, StructureStyle.Timber, "A fire in a shallow painted clay tray on a wooden tripod: v0 lashed at the top, v1 on a ring.", WayTrayFire);
			WayLight(list, BiomeWayLightKind.TuituiTorch, StructureStyle.Timber, "A candlenut torch: kernels skewered on a leaf midrib lashed to a pole, v0 one skewer, v1 two crossed.", WayTuituiTorch);
			WayLight(list, BiomeWayLightKind.BarkTorch, StructureStyle.Timber, "A rolled birch-bark torch held in a split stake: v0 upright, v1 leaning.", WayBarkTorch);
			WayLight(list, BiomeWayLightKind.KazanFire, StructureStyle.Iron, "A cauldron-brazier on three hoof-shaped legs: v0 round-bellied with ring handles, v1 deep and straight.", WayKazanFire);
			WayLight(list, BiomeWayLightKind.BrimLamp, StructureStyle.Earth, "A clay cup of sulphur and oil burning low and blue in a rammed-earth niche: v0 square, v1 rounded.", WayBrimLamp);
			WayLight(list, BiomeWayLightKind.BrimLampPost, StructureStyle.Earth, "A clay cup of sulphur and oil burning low and blue on a rammed-earth post: v0 square, v1 stepped.", WayBrimLampPost);
			WayLight(list, BiomeWayLightKind.PishaStake, StructureStyle.Iron, "An iron fork-basket of resinous pine splinters on a driven stake: v0 four prongs, v1 a ring basket.", WayPishaStake);
			WayLight(list, BiomeWayLightKind.CopperKandil, StructureStyle.Timber, "A three-wick tinned-copper oil lamp hung on a chain from a tripod of poles: v0 a bowl, v1 a lidded lamp.", WayCopperKandil);
			WayLight(list, BiomeWayLightKind.Candelabrum, StructureStyle.Iron, "A tall tripod candelabrum, its oil cup on a knopped shaft: v0 one cup, v1 three on arms.", WayCandelabrum);
			WayLight(list, BiomeWayLightKind.NoduleLamp, StructureStyle.Stone, "A hollow nodule shell on a rib post, a glowing chip inside: v0 on one rib, v1 between two.", WayNoduleLamp);
			WayLight(list, BiomeWayLightKind.Lurelight, StructureStyle.Iron, "A wrought-iron cage holding a glowing lure-orb: v0 on a curved rib, v1 on a basalt post.", WayLurelight);
			WayLight(list, BiomeWayLightKind.NaphthaLamp, StructureStyle.Earth, "A perforated clay lantern on a pitch-daubed reed-bundle pillar: v0 one bundle, v1 three bound together.", WayNaphthaLamp);
			WayLight(list, BiomeWayLightKind.Hearthwarden, StructureStyle.Stone, "A hearth-warden: a seated stone figure, hands on its knees, bearing a fire bowl on its head; v1 kneeling on a plinth.", WayHearthwarden);
			WayLight(list, BiomeWayLightKind.Tenamaztli, StructureStyle.Stone, "Three carved hearthstones with faces under a clay griddle, a fire between them: v0 on the ground, v1 on a low clay platform.", WayTenamaztli).Collides = false;
			WayLight(list, BiomeWayLightKind.KukuiTorch, StructureStyle.Timber, "Candlenut kernels strung on a midrib in a leaf sheath, set in a stone socket: v0 one torch, v1 a pair.", WayKukuiTorch);
			WayLight(list, BiomeWayLightKind.PohoLamp, StructureStyle.Stone, "A stone bowl lamp of nut oil: v0 on the ground, v1 on a flat stone.", WayPohoLamp).Collides = false;
			WayLight(list, BiomeWayLightKind.Lucerna, StructureStyle.Stone, "A bronze many-wick oil lamp in a limewashed wall niche: v0 square-headed, v1 arched.", WayLucerna);
			WayLight(list, BiomeWayLightKind.LucernaPost, StructureStyle.Timber, "A bronze many-wick oil lamp on a shelf under a little gabled roof on a squared post, 2 m: v0 four wicks, v1 two.", WayLucernaPost);
			WayLight(list, BiomeWayLightKind.VigilLamp, StructureStyle.Stone, "One candle in a squat stone hood on a cut dead-oak post: v0 flat hood, v1 gabled.", WayVigilLamp);
			WayLight(list, BiomeWayLightKind.Shellwick, StructureStyle.Stone, "A great shell holding oil and a floating wick: v0 on a stone stand, v1 on a timber tripod.", WayShellwick);
			WayLight(list, BiomeWayLightKind.Gledpan, StructureStyle.Iron, "A wrought-iron pan holding a glowing ember-stone, on a chain from a stake's arm: v0 an iron stake, v1 a timber one.", WayGledpan);
			WayLight(list, BiomeWayLightKind.WispLantern, StructureStyle.Timber, "A pale wisp caged in a bog-iron basket on a crooked post: v0 on its top, v1 hung from its crook.", WayWispLantern);
			WayLight(list, BiomeWayLightKind.Alminha, StructureStyle.Stone, "A stone pillar, 2.1 m, with a gabled niche: a painted panel and a candle in a tin lantern inside; v1 on a stepped base.", WayAlminha);
			WayLight(list, BiomeWayLightKind.GourdLantern, StructureStyle.Timber, "A candle in a pierced gourd hung from a crooked pole: v0 one gourd, v1 a long gourd on a forked pole.", WayGourdLantern);
			WayLight(list, BiomeWayLightKind.LampCairn, StructureStyle.Stone, "A crescent stone oil lamp on a waist-high cairn, its flame low and wide: v0 round cairn, v1 a slab-topped one.", WayLampCairn);
			WayLight(list, BiomeWayLightKind.WaysideLantern, StructureStyle.Timber, "A candle lantern in a small gabled shrine-box on a post: v0 open-fronted, v1 with a pane door.", WayWaysideLantern);
			WayLight(list, BiomeWayLightKind.LanterneDesMorts, StructureStyle.Stone, "A lantern of the dead: a tall hollow stone column, 4.5 m, an open lantern chamber under a pyramid cap and a cross; v1 octagonal.", WayLanterneDesMorts, StructureSize.Medium);
			WayLight(list, BiomeWayLightKind.CandleNiche, StructureStyle.Stone, "Candles in a niche built out from a wall, a soot fan over it: v0 three candles, v1 two under an arched head.", WayCandleNiche);
		}

		/// <summary>One suite light's piece: its kind's id, the "light" tag only (laid by id), not walked into where it is hung on a wall.</summary>
		private static StructurePiece WayLight(List<StructurePiece> list, BiomeWayLightKind kind, StructureStyle style, string description, Action<StructureDraft> builder,
			StructureSize size = StructureSize.Small)
		{
			StructurePiece p = Piece(list, BiomeWayLights.PieceId(kind), style, size, description, builder, "light");
			if (BiomeWayLights.MountOf(kind) == BiomeWayLightMount.Wall)
			{
				p.Collides = false;
			}
			return p;
		}

		/// <summary>
		/// The look of a piece's own fire (<see cref="SceneEmitterKind.Pyre"/>, ScenePyre): a beacon's is kindled at dusk, a
		/// brimstone lamp's burns blue. False and false for every other piece.
		/// </summary>
		public static void PyreLookOf(string pieceId, out bool atDusk, out bool blue)
		{
			atDusk = false;
			blue = false;
			if (!Enum.TryParse(pieceId, false, out BiomeWayLightKind kind) || !Enum.IsDefined(typeof(BiomeWayLightKind), kind) || kind.ToString() != pieceId)
			{
				return;
			}
			atDusk = BiomeWayLights.ClassOf(kind) == BiomeWayLightClass.Beacon;
			blue = kind == BiomeWayLightKind.BrimLamp || kind == BiomeWayLightKind.BrimLampPost;
		}

		/// <summary>The tint of a suite light's cold glow (2026-10-26), or null for a piece that is not one.</summary>
		private static SceneGlowHue? SuiteGlowHueOf(string pieceId)
		{
			switch (pieceId)
			{
				case nameof(BiomeWayLightKind.Bellglow): return SceneGlowHue.Violet;
				case nameof(BiomeWayLightKind.SunLens): return SceneGlowHue.Amber;
				case nameof(BiomeWayLightKind.CrescentStandard): return SceneGlowHue.Rune;
				case nameof(BiomeWayLightKind.DrownedLamp): return SceneGlowHue.Green;
				case nameof(BiomeWayLightKind.VolvaMirror): return SceneGlowHue.Rune;
				case nameof(BiomeWayLightKind.Plummet): return SceneGlowHue.Rune;
				case nameof(BiomeWayLightKind.NoduleLamp): return SceneGlowHue.Green;
				case nameof(BiomeWayLightKind.Lurelight): return SceneGlowHue.Blue;
				case nameof(BiomeWayLightKind.Gledpan): return SceneGlowHue.Amber;
				case nameof(BiomeWayLightKind.WispLantern): return SceneGlowHue.Faerie;
				default: return null;
			}
		}

		// ── Shared parts ──────────────────────────────────────────────

		/// <summary>A squared post from below the ground to <paramref name="top"/>.</summary>
		private static void WlPost(StructureDraft d, float top, float width, StructureMaterial material, int key)
			=> d.Add(StructureShapes.Beam(new Vector3(0f, -0.35f, 0f), new Vector3(0f, top, 0f), width, width, material, key)).Grain = Vector3.up;

		/// <summary>A round pole from below the ground to <paramref name="top"/>.</summary>
		private static void WlPole(StructureDraft d, float top, float radius, StructureMaterial material, int key)
			=> d.Add(StructureShapes.Rod(new Vector3(0f, -0.35f, 0f), new Vector3(0f, top, 0f), radius, d.Seg(8, 5), material, key));

		/// <summary>A crooked length through <paramref name="points"/>, tapering from <paramref name="from"/> to <paramref name="to"/> thick (a driftwood post, a bough).</summary>
		private static void WlCrooked(StructureDraft d, Vector3[] points, float from, float to, StructureMaterial material, int key)
		{
			for (int i = 0; i + 1 < points.Length; i++)
			{
				float w = Mathf.Lerp(from, to, i / (float)Mathf.Max(1, points.Length - 2));
				d.Add(StructureShapes.Beam(points[i], points[i + 1], w, w, material, key + i)).Grain = (points[i + 1] - points[i]).normalized;
			}
		}

		/// <summary>A pillar of <paramref name="courses"/> rough courses up to <paramref name="top"/>, each turned a little (stacked slabs, a cairn, rubble).</summary>
		private static void WlStack(StructureDraft d, float top, float width, int courses, StructureMaterial material, int key, float taper = 0f)
		{
			float h = (top + 0.1f) / courses;
			for (int i = 0; i < courses; i++)
			{
				float w = width * (1f - taper * i / Mathf.Max(1, courses - 1)) * d.Range(key + i, 1, 0.92f, 1.06f);
				d.Add(StructureShapes.Block(new Vector3(d.Range(key + i, 2, -0.02f, 0.02f), -0.1f + h * (i + 0.5f), d.Range(key + i, 3, -0.02f, 0.02f)),
					new Vector3(w, h * 1.02f, w * d.Range(key + i, 4, 0.9f, 1.05f)), Quaternion.Euler(0f, d.Range(key + i, 5, -12f, 12f), 0f), material, key + i,
					StructureRole.Structure, d.Bevel(0.02f)));
			}
		}

		/// <summary>A solid bowl standing at <paramref name="at"/>: a foot, the side out to its lip, the hollow back down to its middle.</summary>
		private static void WlBowl(StructureDraft d, Vector3 at, float radius, float height, StructureMaterial material, int key, StructureRole role = StructureRole.Structure)
		{
			var profile = new[]
			{
				new Vector2(0f, 0f), new Vector2(radius * 0.45f, 0f), new Vector2(radius * 0.9f, height * 0.55f), new Vector2(radius, height),
				new Vector2(radius * 0.86f, height), new Vector2(radius * 0.4f, height * 0.4f), new Vector2(0f, height * 0.32f),
			};
			d.Add(StructureShapes.Lathe(profile, d.Seg(8, 5), material, key, true, 0f, false, false, role).Transform(Matrix4x4.Translate(at)));
		}

		/// <summary>Charred fuel heaped in a bowl or a basket, its top at <paramref name="at"/>.</summary>
		private static void WlCoals(StructureDraft d, Vector3 at, float radius, float height, int key)
		{
			d.Add(StructureShapes.Lathe(new[] { new Vector2(radius, -height), new Vector2(radius * 0.8f, -height * 0.2f), new Vector2(radius * 0.35f, 0f), new Vector2(0f, 0.02f) },
				d.Seg(8, 5), StructureMaterial.Ash, key, true, 0f, false, false, StructureRole.Detail).Transform(Matrix4x4.Translate(at)));
		}

		/// <summary>A small flame (always alight) rising from <paramref name="at"/>, <paramref name="size"/> tall.</summary>
		private static void WlFlame(StructureDraft d, Vector3 at, float size, int key, StructureMaterial material = StructureMaterial.Lamplight)
			=> d.Add(StructureShapes.Sphere(at + Vector3.up * size * 0.5f, new Vector3(size * 0.32f, size * 0.5f, size * 0.32f), d.Seg(5, 4), 3, material, key, StructureRole.Detail));

		/// <summary>A back plate against the wall at z = 0, centred on the fixing.</summary>
		private static void WlPlate(StructureDraft d, float width, float height, StructureMaterial material, int key, float y = 0f)
			=> d.Add(StructureShapes.Box(new Vector3(0f, y, 0.008f), new Vector3(width, height, 0.016f), material, key));

		/// <summary>
		/// A niche built out from a wall (its back against the wall at z = 0, open along −z), its opening <paramref name="width"/>
		/// × <paramref name="height"/> centred on the fixing, <paramref name="deep"/> out, its walls <paramref name="thick"/>:
		/// head 0 flat, 1 gabled, 2 rounded. Returns the middle of its floor.
		/// </summary>
		private static Vector3 WlNiche(StructureDraft d, float width, float height, float deep, float thick, StructureMaterial material, int key, int head)
		{
			float y0 = -height * 0.5f, y1 = height * 0.5f, outer = width + 2f * thick;
			d.Add(StructureShapes.Box(new Vector3(0f, 0f, -0.015f), new Vector3(outer, height + 2f * thick, 0.03f), material, key));
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(s * (width + thick) * 0.5f, 0f, -deep * 0.5f), new Vector3(thick, height + 2f * thick, deep), d.Bevel(0.01f), material, key + 1 + (s + 1) / 2));
			}
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, y0 - thick * 0.5f, -deep * 0.5f - 0.02f), new Vector3(outer + 0.04f, thick, deep + 0.04f), d.Bevel(0.01f), material, key + 3));
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, y1 + thick * 0.5f, -deep * 0.5f), new Vector3(outer, thick, deep), d.Bevel(0.01f), material, key + 4));
			if (head == 1)
			{
				float half = outer * 0.5f + 0.03f, rise = outer * 0.3f;
				var outline = new[] { new Vector2(-half, 0f), new Vector2(half, 0f), new Vector2(0f, rise) };
				d.Add(StructureShapes.Prism(outline, -deep - 0.03f, 0f, material, key + 5).Transform(Matrix4x4.Translate(new Vector3(0f, y1 + thick, 0f))));
			}
			else if (head == 2)
			{
				var outline = new List<Vector2>();
				int steps = d.Fine ? 6 : 3;
				for (int i = 0; i <= steps; i++)
				{
					float a = Mathf.PI * i / steps;
					outline.Add(new Vector2(Mathf.Cos(a) * outer * 0.5f, Mathf.Sin(a) * outer * 0.32f));
				}
				d.Add(StructureShapes.Prism(outline, -deep, 0f, material, key + 5).Transform(Matrix4x4.Translate(new Vector3(0f, y1 + thick, 0f))));
			}
			return new Vector3(0f, y0, -deep * 0.5f);
		}

		/// <summary>A soot stain on the wall over a flame or a niche's head: a thin dark tongue just off the wall's face (z = 0).</summary>
		private static void WlSoot(StructureDraft d, Vector3 foot, float width, float height, int key, float z = -0.002f)
		{
			if (!d.Mid)
			{
				return;
			}
			var outline = new[] { new Vector2(-width * 0.5f, 0f), new Vector2(width * 0.5f, 0f), new Vector2(width * 0.15f, height), new Vector2(-width * 0.15f, height) };
			d.Add(StructureShapes.Prism(outline, z - 0.004f, z, StructureMaterial.Ash, key, StructureRole.Detail).Transform(Matrix4x4.Translate(new Vector3(foot.x, foot.y, 0f))));
		}

		/// <summary>A clay saucer lamp with <paramref name="spouts"/> spouts at <paramref name="at"/> (its foot), a flame at each; the first flame's place.</summary>
		private static Vector3 WlSaucer(StructureDraft d, Vector3 at, float radius, int spouts, StructureMaterial material, int key, float phase = 0f)
		{
			WlBowl(d, at, radius, radius * 0.5f, material, key);
			Vector3 first = at + Vector3.up * radius * 0.5f;
			for (int i = 0; i < spouts; i++)
			{
				float a = phase + i * Mathf.PI * 2f / spouts - Mathf.PI * 0.5f;
				var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
				Vector3 lip = at + dir * radius * 0.9f + Vector3.up * radius * 0.42f, tip = at + dir * radius * 1.45f + Vector3.up * radius * 0.5f;
				d.Add(StructureShapes.Beam(lip, tip, radius * 0.3f, radius * 0.22f, material, key + 1 + i, StructureRole.Detail));
				WlFlame(d, tip + Vector3.up * radius * 0.1f, Mathf.Max(0.035f, radius * 0.6f), key + 20 + i);
				if (i == 0)
				{
					first = tip + Vector3.up * radius * 0.3f;
				}
			}
			return first;
		}

		/// <summary>An iron cage of <paramref name="bars"/> bars round <paramref name="centre"/>, hoops top and bottom.</summary>
		private static void WlCage(StructureDraft d, Vector3 centre, float radius, float height, int bars, int key)
		{
			for (int i = 0; i < bars; i++)
			{
				float a = i * Mathf.PI * 2f / bars;
				var r = new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
				d.Add(StructureShapes.Beam(centre + r - Vector3.up * height * 0.5f, centre + r * 0.6f + Vector3.up * height * 0.5f, 0.014f, 0.014f, StructureMaterial.Iron, key + i, StructureRole.Detail));
			}
			d.Add(Ring(centre - Vector3.up * height * 0.5f, radius, 0.018f, Quaternion.identity, d.Seg(10, 6), StructureMaterial.Iron, key + 20, StructureRole.Detail));
			d.Add(Ring(centre + Vector3.up * height * 0.5f, radius * 0.6f, 0.016f, Quaternion.identity, d.Seg(8, 5), StructureMaterial.Iron, key + 21, StructureRole.Detail));
		}

		/// <summary>A horizontal arm from a post's side out along −z, ending in a short hook down; the hook's foot.</summary>
		private static Vector3 WlArm(StructureDraft d, float y, float reach, float thick, StructureMaterial material, int key, float x = 0f)
		{
			d.Add(StructureShapes.Beam(new Vector3(x, y, 0.02f), new Vector3(x, y, -reach), thick, thick, material, key));
			d.Add(StructureShapes.Beam(new Vector3(x, y - 0.32f, -0.02f), new Vector3(x, y - 0.02f, -reach * 0.55f), thick * 0.7f, thick * 0.7f, material, key + 1, StructureRole.Detail));
			Vector3 hook = new Vector3(x, y - thick * 0.5f, -reach + 0.04f);
			d.Add(StructureShapes.Beam(hook, hook + Vector3.down * 0.08f, 0.012f, 0.012f, StructureMaterial.Iron, key + 2, StructureRole.Detail));
			return hook + Vector3.down * 0.08f;
		}

		/// <summary>Small panes let into a face (the light seen through a carved or pierced lantern): <paramref name="count"/> slivers round a ring.</summary>
		private static void WlPiercings(StructureDraft d, Vector3 centre, float radius, float size, int count, int key, float phase = 0f)
		{
			if (!d.Mid)
			{
				return;
			}
			for (int i = 0; i < count; i++)
			{
				float a = phase + i * Mathf.PI * 2f / count;
				var p = centre + new Vector3(Mathf.Cos(a) * radius, 0f, Mathf.Sin(a) * radius);
				d.Add(StructureShapes.Block(p, new Vector3(size, size * 1.6f, 0.012f), Quaternion.Euler(0f, -a * Mathf.Rad2Deg + 90f, 45f), StructureMaterial.Glass, key + i, StructureRole.Detail));
			}
		}

		// ── Beacons ───────────────────────────────────────────────────

		/// <summary>An iron fire basket of split logs (dark by day, kindled at dusk).</summary>
		private static void WayBeacon(StructureDraft d)
		{
			int seg = d.Seg(10, 6);
			float foot;
			if (d.Variant == 0)
			{
				foot = 0.95f;
				for (int i = 0; i < 3; i++)
				{
					float a = i * Mathf.PI * 2f / 3f;
					d.Add(StructureShapes.Beam(new Vector3(Mathf.Cos(a) * 0.55f, 0f, Mathf.Sin(a) * 0.55f), new Vector3(Mathf.Cos(a) * 0.16f, foot, Mathf.Sin(a) * 0.16f), 0.05f, 0.05f, StructureMaterial.Iron, 1 + i));
				}
				d.Add(Ring(new Vector3(0f, 0.45f, 0f), 0.36f, 0.03f, Quaternion.identity, seg, StructureMaterial.Iron, 5, StructureRole.Detail));
			}
			else
			{
				foot = 0.8f;
				d.Add(StructureShapes.Cylinder(0.5f, -0.1f, foot, seg, StructureMaterial.Stone, 1));
				d.Add(StructureShapes.Cylinder(0.56f, foot - 0.08f, foot, seg, StructureMaterial.Stone, 2, false, StructureRole.Detail));
			}
			BeaconBasket(d, foot, 0.2f, 0.5f, 0.6f, 8, 20);
		}

		/// <summary>A fire basket from <paramref name="foot"/>: staves from a ring out to its lip, hoops, the logs heaped in it, its fire given off.</summary>
		private static void BeaconBasket(StructureDraft d, float foot, float footR, float lipR, float deep, int staves, int key)
		{
			for (int i = 0; i < staves; i++)
			{
				float a = i * Mathf.PI * 2f / staves;
				d.Add(StructureShapes.Beam(new Vector3(Mathf.Cos(a) * footR, foot, Mathf.Sin(a) * footR), new Vector3(Mathf.Cos(a) * lipR, foot + deep, Mathf.Sin(a) * lipR), 0.03f, 0.03f, StructureMaterial.Iron, key + i, StructureRole.Detail));
			}
			int seg = d.Seg(12, 6);
			d.Add(Ring(new Vector3(0f, foot + deep, 0f), lipR, 0.03f, Quaternion.identity, seg, StructureMaterial.Iron, key + 20, StructureRole.Detail));
			d.Add(Ring(new Vector3(0f, foot + deep * 0.45f, 0f), Mathf.Lerp(footR, lipR, 0.45f), 0.025f, Quaternion.identity, seg, StructureMaterial.Iron, key + 21, StructureRole.Detail));
			d.Add(StructureShapes.Cylinder(footR + 0.02f, foot - 0.02f, foot + 0.03f, seg, StructureMaterial.Iron, key + 22, false, StructureRole.Detail));
			// The split logs, crossed in the basket and standing proud of its lip.
			int logs = d.Fine ? 6 : 4;
			for (int i = 0; i < logs; i++)
			{
				float a = i * Mathf.PI / logs + d.Range(key + 30 + i, 1, -0.2f, 0.2f);
				float y = foot + deep * (0.45f + 0.5f * i / logs);
				var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a)) * lipR * 0.85f;
				d.Add(StructureShapes.Beam(new Vector3(0f, y, 0f) - dir, new Vector3(0f, y + 0.08f, 0f) + dir, 0.07f, 0.07f, StructureMaterial.Timber, key + 30 + i, StructureRole.Detail));
			}
			WlCoals(d, new Vector3(0f, foot + deep * 0.6f, 0f), lipR * 0.8f, deep * 0.45f, key + 40);
			d.Emit(new Vector3(0f, foot + deep + 0.1f, 0f), SceneEmitterKind.Pyre);
		}

		/// <summary>A signal pyre on a hearth of laid slabs: a crib of logs, or a cone of poles over brushwood.</summary>
		private static void WaySignalPyre(StructureDraft d)
		{
			for (int i = 0; i < 4; i++)
			{
				float a = i * Mathf.PI * 0.5f + 0.3f;
				d.Add(StructureShapes.Block(new Vector3(Mathf.Cos(a) * 0.45f, 0.04f, Mathf.Sin(a) * 0.45f), new Vector3(0.9f, 0.14f, 0.7f), Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f),
					StructureMaterial.Fieldstone, 1 + i, StructureRole.Structure, d.Bevel(0.03f)));
			}
			float top;
			if (d.Variant == 0)
			{
				int courses = d.Fine ? 6 : 4;
				for (int c = 0; c < courses; c++)
				{
					float y = 0.2f + c * 0.18f, half = 0.62f - c * 0.05f;
					for (int s = -1; s <= 1; s += 2)
					{
						Vector3 off = c % 2 == 0 ? new Vector3(0f, 0f, s * (half - 0.08f)) : new Vector3(s * (half - 0.08f), 0f, 0f);
						Vector3 run = c % 2 == 0 ? Vector3.right * half : Vector3.forward * half;
						d.Add(StructureShapes.Beam(new Vector3(0f, y, 0f) + off - run, new Vector3(0f, y, 0f) + off + run, 0.16f, 0.16f, StructureMaterial.Timber, 10 + c * 2 + (s + 1) / 2));
					}
				}
				top = 0.2f + courses * 0.18f;
				WlCoals(d, new Vector3(0f, top - 0.1f, 0f), 0.4f, 0.5f, 40);
			}
			else
			{
				int poles = d.Fine ? 9 : 6;
				top = 2.1f;
				for (int i = 0; i < poles; i++)
				{
					float a = i * Mathf.PI * 2f / poles;
					d.Add(StructureShapes.Beam(new Vector3(Mathf.Cos(a) * 0.75f, 0.1f, Mathf.Sin(a) * 0.75f), new Vector3(Mathf.Cos(a) * 0.06f, top, Mathf.Sin(a) * 0.06f), 0.08f, 0.08f, StructureMaterial.Timber, 10 + i));
				}
				// Brushwood packed in its foot.
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.62f, 0.1f), new Vector2(0.45f, 0.75f), new Vector2(0f, 0.95f) }, d.Seg(8, 5), StructureMaterial.Thatch, 30, true, 0.2f));
				top = 1.0f;
			}
			d.Emit(new Vector3(0f, top + 0.1f, 0f), SceneEmitterKind.Pyre);
		}

		/// <summary>A hill's beacon: a turf-walled fire pit on a cairn, or a stack of peats on a low drystone platform.</summary>
		private static void WayWartFire(StructureDraft d)
		{
			int seg = d.Seg(10, 6);
			if (d.Variant == 0)
			{
				// The cairn, its stones in two rough rings, and the turf wall of the pit on its top.
				for (int r = 0; r < 2; r++)
				{
					int n = r == 0 ? 7 : 5;
					for (int i = 0; i < n; i++)
					{
						float a = i * Mathf.PI * 2f / n + r * 0.4f;
						float rad = r == 0 ? 0.95f : 0.6f;
						d.Add(StructureShapes.Block(new Vector3(Mathf.Cos(a) * rad, 0.15f + r * 0.35f, Mathf.Sin(a) * rad), new Vector3(0.55f, 0.42f, 0.45f), Quaternion.Euler(d.Range(r * 10 + i, 1, -8f, 8f), a * Mathf.Rad2Deg, 0f),
							StructureMaterial.Fieldstone, 1 + r * 10 + i, StructureRole.Structure, d.Bevel(0.04f)));
					}
				}
				d.Add(StructureShapes.Cylinder(0.75f, -0.1f, 0.6f, seg, StructureMaterial.Fieldstone, 30));
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.55f, 0.6f), new Vector2(0.55f, 1.0f), new Vector2(0.38f, 1.0f), new Vector2(0.38f, 0.75f) }, seg, StructureMaterial.Earth, 31, true, 0f, true));
				WlCoals(d, new Vector3(0f, 0.92f, 0f), 0.4f, 0.2f, 32);
				d.Emit(new Vector3(0f, 1.05f, 0f), SceneEmitterKind.Pyre);
				return;
			}
			d.Add(StructureShapes.Cylinder(1.0f, -0.1f, 0.5f, seg, StructureMaterial.Fieldstone, 1));
			d.Add(StructureShapes.Cylinder(1.05f, 0.42f, 0.52f, seg, StructureMaterial.Fieldstone, 2, false, StructureRole.Detail));
			// Peats stacked on end in a ring, leaning in.
			int peats = d.Fine ? 10 : 6;
			for (int i = 0; i < peats; i++)
			{
				float a = i * Mathf.PI * 2f / peats;
				d.Add(StructureShapes.Block(new Vector3(Mathf.Cos(a) * 0.35f, 0.78f, Mathf.Sin(a) * 0.35f), new Vector3(0.14f, 0.5f, 0.26f), Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 14f), StructureMaterial.Earth, 10 + i, StructureRole.Detail));
			}
			WlCoals(d, new Vector3(0f, 0.75f, 0f), 0.3f, 0.2f, 30);
			d.Emit(new Vector3(0f, 1.1f, 0f), SceneEmitterKind.Pyre);
		}

		/// <summary>A blubber pyre: an iron pot of rendered fat on a stand over a stone ring, or a stone trough of fat on a cairn.</summary>
		private static void WayBlubberPyre(StructureDraft d)
		{
			int seg = d.Seg(10, 6);
			if (d.Variant == 0)
			{
				for (int i = 0; i < 7; i++)
				{
					float a = i * Mathf.PI * 2f / 7f;
					d.Add(StructureShapes.Block(new Vector3(Mathf.Cos(a) * 0.62f, 0.1f, Mathf.Sin(a) * 0.62f), new Vector3(0.36f, 0.26f, 0.3f), Quaternion.Euler(0f, a * Mathf.Rad2Deg, 0f), StructureMaterial.Fieldstone, 1 + i, StructureRole.Structure, d.Bevel(0.03f)));
				}
				for (int i = 0; i < 3; i++)
				{
					float a = i * Mathf.PI * 2f / 3f + 0.5f;
					d.Add(StructureShapes.Beam(new Vector3(Mathf.Cos(a) * 0.45f, 0f, Mathf.Sin(a) * 0.45f), new Vector3(Mathf.Cos(a) * 0.3f, 0.9f, Mathf.Sin(a) * 0.3f), 0.04f, 0.04f, StructureMaterial.Iron, 10 + i));
				}
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0f, 0.8f), new Vector2(0.25f, 0.82f), new Vector2(0.4f, 1.05f), new Vector2(0.36f, 1.3f), new Vector2(0.32f, 1.3f), new Vector2(0.3f, 1.12f), new Vector2(0f, 1.1f) },
					seg, StructureMaterial.Iron, 20, true, 0f, false));
				d.Add(StructureShapes.Cylinder(0.31f, 1.1f, 1.24f, seg, StructureMaterial.Bone, 21, false, StructureRole.Detail));
				d.Emit(new Vector3(0f, 1.4f, 0f), SceneEmitterKind.Pyre);
				return;
			}
			WlStack(d, 0.9f, 1.0f, 3, StructureMaterial.Fieldstone, 1, 0.25f);
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, 1.0f, 0f), new Vector3(1.1f, 0.2f, 0.45f), d.Bevel(0.03f), StructureMaterial.Stone, 10));
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Box(new Vector3(0f, 1.16f, s * 0.19f), new Vector3(1.1f, 0.14f, 0.07f), StructureMaterial.Stone, 11 + (s + 1) / 2));
			}
			d.Add(StructureShapes.Box(new Vector3(0f, 1.15f, 0f), new Vector3(1.0f, 0.06f, 0.3f), StructureMaterial.Bone, 13, StructureRole.Detail));
			d.Emit(new Vector3(0f, 1.3f, 0f), SceneEmitterKind.Pyre);
		}

		/// <summary>Five fire pits in a row along a rammed-earth platform (or stone rings on a stone one); the middle pit's fire given off.</summary>
		private static void WayFivePits(StructureDraft d)
		{
			bool stone = d.Variant == 1;
			StructureMaterial body = stone ? StructureMaterial.Stone : StructureMaterial.Earth;
			const float length = 5.2f, high = 0.9f;
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, high * 0.5f - 0.1f, 0f), new Vector3(length, high + 0.2f, 1.3f), d.Bevel(0.04f), body, 1));
			int seg = d.Seg(8, 5);
			for (int i = 0; i < 5; i++)
			{
				float x = (i - 2) * 1.0f;
				Matrix4x4 at = Matrix4x4.Translate(new Vector3(x, high, 0f));
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.4f, 0f), new Vector2(0.4f, 0.22f), new Vector2(0.3f, 0.22f), new Vector2(0.3f, 0.05f) }, seg,
					stone ? StructureMaterial.Fieldstone : StructureMaterial.Adobe, 10 + i, true, 0f, true).Transform(at));
				WlCoals(d, new Vector3(x, high + 0.12f, 0f), 0.28f, 0.1f, 20 + i);
			}
			d.Emit(new Vector3(0f, high + 0.3f, 0f), SceneEmitterKind.Pyre);
		}

		// ── Lamps and lanterns on posts ───────────────────────────────

		/// <summary>A horn lantern of amber panes on a driftwood post.</summary>
		private static void WayAmberLamp(StructureDraft d)
		{
			var post = new[] { new Vector3(0f, -0.35f, 0f), new Vector3(0.04f, 0.8f, 0.03f), new Vector3(-0.03f, 1.6f, -0.02f), new Vector3(0.01f, 2.05f, 0f) };
			WlCrooked(d, post, 0.15f, 0.11f, StructureMaterial.Timber, 1);
			if (d.Variant == 0)
			{
				Lantern(d, new Vector3(0.01f, 2.05f, 0f), false, 100);
				return;
			}
			Vector3 hook = WlArm(d, 1.95f, 0.5f, 0.07f, StructureMaterial.Timber, 10);
			Lantern(d, hook + Vector3.down * 0.4f, true, 100);
		}

		/// <summary>A squat tuff lantern pierced with stars and crosses on a tuff post.</summary>
		private static void WayCarvedLantern(StructureDraft d)
		{
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.55f, 0f), new Vector3(0.26f, 1.3f, 0.26f), d.Bevel(0.02f), StructureMaterial.Monolith, 1));
			const float y = 1.2f;
			if (d.Variant == 0)
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, y + 0.22f, 0f), new Vector3(0.46f, 0.44f, 0.46f), d.Bevel(0.03f), StructureMaterial.Monolith, 2));
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.36f, y + 0.44f), new Vector2(0.05f, y + 0.72f) }, 4, StructureMaterial.Monolith, 3, false, Mathf.PI * 0.25f));
				WlPiercings(d, new Vector3(0f, y + 0.22f, 0f), 0.236f, 0.07f, 4, 10, Mathf.PI * 0.5f);
			}
			else
			{
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.24f, y), new Vector2(0.26f, y + 0.4f), new Vector2(0.22f, y + 0.46f), new Vector2(0.12f, y + 0.62f), new Vector2(0f, y + 0.66f) }, 8, StructureMaterial.Monolith, 2, false, Mathf.PI / 8f));
				WlPiercings(d, new Vector3(0f, y + 0.2f, 0f), 0.258f, 0.06f, 8, 10, 0f);
			}
			d.Emit(new Vector3(0f, y + 0.22f, 0f), SceneEmitterKind.Lamp);
		}

		/// <summary>A stone lantern: tall (base, post, firebox, curled cap, finial) or low on three legs under a broad cap.</summary>
		private static void WayStoneLantern(StructureDraft d)
		{
			const int hex = 6;
			float box;
			if (d.Variant == 0)
			{
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.34f, -0.1f), new Vector2(0.34f, 0.12f), new Vector2(0.22f, 0.2f), new Vector2(0.14f, 0.24f) }, hex, StructureMaterial.Monolith, 1, false));
				d.Add(StructureShapes.Cylinder(0.12f, 0.24f, 1.0f, d.Seg(10, 6), StructureMaterial.Monolith, 2));
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.14f, 1.0f), new Vector2(0.3f, 1.1f), new Vector2(0.3f, 1.18f) }, hex, StructureMaterial.Monolith, 3, false));
				box = 1.18f;
			}
			else
			{
				for (int i = 0; i < 3; i++)
				{
					float a = i * Mathf.PI * 2f / 3f;
					d.Add(StructureShapes.Beam(new Vector3(Mathf.Cos(a) * 0.42f, -0.05f, Mathf.Sin(a) * 0.42f), new Vector3(Mathf.Cos(a) * 0.2f, 0.55f, Mathf.Sin(a) * 0.2f), 0.09f, 0.09f, StructureMaterial.Monolith, 1 + i));
				}
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.3f, 0.55f), new Vector2(0.3f, 0.63f) }, hex, StructureMaterial.Monolith, 4, false));
				box = 0.63f;
			}
			// The firebox: posts at its corners, paper screens (lit at night) between, the cap curling up at its corners.
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.17f, box), new Vector2(0.17f, box + 0.3f) }, hex, StructureMaterial.Glass, 10, false));
			for (int i = 0; i < hex; i++)
			{
				float a = i * Mathf.PI * 2f / hex;
				d.Add(StructureShapes.Beam(new Vector3(Mathf.Cos(a) * 0.2f, box, Mathf.Sin(a) * 0.2f), new Vector3(Mathf.Cos(a) * 0.2f, box + 0.3f, Mathf.Sin(a) * 0.2f), 0.05f, 0.05f, StructureMaterial.Monolith, 11 + i, StructureRole.Detail));
			}
			float capR = d.Variant == 0 ? 0.42f : 0.62f;
			d.Add(StructureShapes.Lathe(new[] { new Vector2(capR, box + 0.36f), new Vector2(capR * 0.96f, box + 0.3f), new Vector2(0.2f, box + 0.32f), new Vector2(0.1f, box + 0.5f), new Vector2(0f, box + 0.52f) }, hex, StructureMaterial.Monolith, 20, false));
			if (d.Variant == 0)
			{
				d.Add(StructureShapes.Sphere(new Vector3(0f, box + 0.6f, 0f), new Vector3(0.07f, 0.09f, 0.07f), d.Seg(8, 5), 3, StructureMaterial.Monolith, 21, StructureRole.Detail));
			}
			d.Emit(new Vector3(0f, box + 0.15f, 0f), SceneEmitterKind.Lamp);
		}

		/// <summary>A round paper lantern (lit through its paper at night) with dark rims, hung from <paramref name="top"/>; its middle.</summary>
		private static Vector3 WlPaperLantern(StructureDraft d, Vector3 top, bool tall, int key)
		{
			float h = tall ? 0.5f : 0.34f, r = tall ? 0.13f : 0.17f;
			int seg = d.Seg(10, 6);
			Vector3 foot = top + Vector3.down * (h + 0.06f);
			var profile = new List<Vector2> { new Vector2(r * 0.55f, 0f) };
			for (int i = 1; i < 4; i++)
			{
				float t = i / 4f;
				profile.Add(new Vector2(r * Mathf.Lerp(0.55f, 1f, Mathf.Sin(t * Mathf.PI)), h * t));
			}
			profile.Add(new Vector2(r * 0.55f, h));
			d.Add(StructureShapes.Lathe(profile, seg, StructureMaterial.Glass, key, true, 0f, false).Transform(Matrix4x4.Translate(foot)));
			d.Add(StructureShapes.Cylinder(r * 0.58f, -0.03f, 0.015f, seg, StructureMaterial.Dyed, key + 1, false, StructureRole.Detail).Transform(Matrix4x4.Translate(foot)));
			d.Add(StructureShapes.Cylinder(r * 0.58f, h - 0.015f, h + 0.03f, seg, StructureMaterial.Dyed, key + 2, false, StructureRole.Detail).Transform(Matrix4x4.Translate(foot)));
			d.Add(StructureShapes.Beam(foot + Vector3.up * (h + 0.03f), top, 0.01f, 0.01f, StructureMaterial.Canvas, key + 3, StructureRole.Detail));
			Vector3 mid = foot + Vector3.up * h * 0.5f;
			d.Emit(mid, SceneEmitterKind.Lamp);
			return mid;
		}

		/// <summary>A paper lantern on a bamboo bracket from a wall.</summary>
		private static void WayPaperLantern(StructureDraft d)
		{
			WlPlate(d, 0.08f, 0.3f, StructureMaterial.Bamboo, 1);
			d.Add(StructureShapes.Rod(new Vector3(0f, 0.12f, 0f), new Vector3(0f, 0.12f, -0.5f), 0.022f, d.Seg(6, 4), StructureMaterial.Bamboo, 2));
			d.Add(StructureShapes.Rod(new Vector3(0f, -0.12f, 0f), new Vector3(0f, 0.1f, -0.3f), 0.016f, d.Seg(6, 4), StructureMaterial.Bamboo, 3, StructureRole.Detail));
			WlPaperLantern(d, new Vector3(0f, 0.1f, -0.46f), d.Variant == 1, 10);
		}

		/// <summary>A paper lantern from the arm of a bamboo post.</summary>
		private static void WayPaperLanternPost(StructureDraft d)
		{
			WlPole(d, 2.2f, 0.04f, StructureMaterial.Bamboo, 1);
			d.Add(StructureShapes.Rod(new Vector3(0f, 2.1f, 0.03f), new Vector3(0f, 2.1f, -0.55f), 0.024f, d.Seg(6, 4), StructureMaterial.Bamboo, 2));
			d.Add(StructureShapes.Rod(new Vector3(0f, 1.8f, 0f), new Vector3(0f, 2.08f, -0.3f), 0.016f, d.Seg(6, 4), StructureMaterial.Bamboo, 3, StructureRole.Detail));
			WlPaperLantern(d, new Vector3(0f, 2.08f, -0.5f), d.Variant == 1, 10);
		}

		/// <summary>A clay oil lamp in a niche built out from a wall, a soot tongue above it.</summary>
		private static void WayNicheLamp(StructureDraft d)
		{
			bool plastered = d.Variant == 1;
			Vector3 floor = WlNiche(d, 0.3f, 0.34f, 0.26f, 0.08f, plastered ? StructureMaterial.Plaster : StructureMaterial.Fieldstone, 1, plastered ? 1 : 0);
			Vector3 flame = WlSaucer(d, floor + new Vector3(0f, 0f, -0.02f), 0.07f, 1, StructureMaterial.Adobe, 20);
			WlSoot(d, new Vector3(0f, 0.25f, 0f), 0.2f, 0.3f, 40);
			d.Emit(flame, SceneEmitterKind.Lamp);
		}

		/// <summary>A carved post (or a stone pillar) with a lamp niche in its head.</summary>
		private static void WayNicheLampPost(StructureDraft d)
		{
			bool stone = d.Variant == 1;
			StructureMaterial m = stone ? StructureMaterial.Stone : StructureMaterial.Timber;
			const float head = 1.45f;
			if (stone)
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, head * 0.5f - 0.15f, 0f), new Vector3(0.34f, head + 0.3f, 0.34f), d.Bevel(0.02f), m, 1));
			}
			else
			{
				WlPost(d, head, 0.22f, m, 1);
				// Carved bands down the post.
				for (int i = 0; i < 3; i++)
				{
					d.Add(StructureShapes.Box(new Vector3(0f, 0.5f + i * 0.28f, 0f), new Vector3(0.25f, 0.05f, 0.25f), m, 2 + i, StructureRole.Detail));
				}
			}
			// The head: a niche open to −z, its back the post's own, a cap over it.
			d.Add(StructureShapes.Box(new Vector3(0f, head + 0.02f, 0f), new Vector3(0.36f, 0.04f, 0.36f), m, 10));
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Box(new Vector3(s * 0.15f, head + 0.2f, 0f), new Vector3(0.06f, 0.32f, 0.36f), m, 11 + (s + 1) / 2));
			}
			d.Add(StructureShapes.Box(new Vector3(0f, head + 0.2f, 0.15f), new Vector3(0.24f, 0.32f, 0.06f), m, 13));
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, head + 0.4f, 0f), new Vector3(0.42f, 0.08f, 0.42f), d.Bevel(0.02f), m, 14));
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.2f, head + 0.44f), new Vector2(0f, head + 0.6f) }, 4, m, 15, false, Mathf.PI * 0.25f));
			Vector3 flame = WlSaucer(d, new Vector3(0f, head + 0.04f, -0.02f), 0.06f, 1, StructureMaterial.Adobe, 20);
			d.Emit(flame, SceneEmitterKind.Lamp);
		}

		/// <summary>A small bronze bell, its mouth glowing (cold Glow), on a weed-hung post.</summary>
		private static void WayBellglow(StructureDraft d)
		{
			WlPost(d, 2.0f, 0.14f, StructureMaterial.Timber, 1);
			Vector3 bell;
			if (d.Variant == 0)
			{
				Vector3 hook = WlArm(d, 1.9f, 0.45f, 0.08f, StructureMaterial.Timber, 2);
				bell = hook + Vector3.down * 0.04f;
			}
			else
			{
				// A yoke on the post's top: two cheeks and a bar, the bell hung between them.
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Beam(new Vector3(s * 0.04f, 1.95f, 0f), new Vector3(s * 0.2f, 2.45f, 0f), 0.06f, 0.06f, StructureMaterial.Timber, 2 + (s + 1) / 2));
				}
				d.Add(StructureShapes.Beam(new Vector3(-0.24f, 2.45f, 0f), new Vector3(0.24f, 2.45f, 0f), 0.07f, 0.07f, StructureMaterial.Timber, 4));
				bell = new Vector3(0f, 2.41f, 0f);
			}
			// The bell: shoulder, waist, flaring lip; its mouth's glow just inside.
			int seg = d.Seg(10, 6);
			Matrix4x4 at = Matrix4x4.Translate(bell);
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.07f, -0.02f), new Vector2(0.1f, -0.12f), new Vector2(0.15f, -0.24f), new Vector2(0.13f, -0.24f), new Vector2(0.08f, -0.12f), new Vector2(0f, -0.09f) },
				seg, StructureMaterial.Iron, 10, true, 0f, false).Transform(at));
			d.Add(StructureShapes.Cylinder(0.11f, -0.2f, -0.19f, seg, StructureMaterial.Runelight, 11, false, StructureRole.Detail).Transform(at));
			d.Add(StructureShapes.Sphere(bell + new Vector3(0f, -0.2f, 0f), new Vector3(0.03f, 0.03f, 0.03f), d.Seg(6, 4), 3, StructureMaterial.Iron, 12, StructureRole.Detail));
			// Weed hanging off the post in strands.
			if (d.Mid)
			{
				for (int i = 0; i < 4; i++)
				{
					float a = i * 1.6f;
					var top = new Vector3(Mathf.Cos(a) * 0.075f, 1.5f - i * 0.12f, Mathf.Sin(a) * 0.075f);
					d.Add(StructureShapes.Beam(top, top + new Vector3(Mathf.Cos(a) * 0.05f, -0.4f, Mathf.Sin(a) * 0.05f), 0.03f, 0.008f, StructureMaterial.Dyed, 20 + i, StructureRole.Detail));
				}
			}
			d.Emit(bell + new Vector3(0f, -0.26f, 0f), SceneEmitterKind.Glow);
		}

		/// <summary>A lamp pillar: a stone shaft with tiers of lamp brackets (arms, or stone rings of cups), a lamp flame on each.</summary>
		private static void WayLampPillar(StructureDraft d)
		{
			const float height = 3.6f;
			int seg = d.Seg(8, 6);
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.4f, -0.1f), new Vector2(0.4f, 0.25f), new Vector2(0.25f, 0.35f) }, 8, StructureMaterial.Monolith, 1, false, Mathf.PI / 8f));
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.2f, 0.35f), new Vector2(0.15f, height), new Vector2(0.2f, height + 0.08f), new Vector2(0f, height + 0.3f) }, 8, StructureMaterial.Monolith, 2, false, Mathf.PI / 8f));
			for (int t = 0; t < 3; t++)
			{
				float y = 1.4f + t * 0.85f, shaft = Mathf.Lerp(0.2f, 0.15f, (y - 0.35f) / (height - 0.35f));
				int arms = d.Fine ? 6 : 4;
				if (d.Variant == 1)
				{
					d.Add(StructureShapes.Lathe(new[] { new Vector2(shaft, y - 0.06f), new Vector2(0.4f - t * 0.05f, y), new Vector2(0.4f - t * 0.05f, y + 0.05f), new Vector2(shaft, y + 0.05f) }, seg, StructureMaterial.Monolith, 10 + t, true, 0f, true));
				}
				for (int i = 0; i < arms; i++)
				{
					float a = i * Mathf.PI * 2f / arms + t * 0.4f;
					var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
					Vector3 cup = new Vector3(0f, y + 0.05f, 0f) + dir * (0.4f - t * 0.05f - 0.06f);
					if (d.Variant == 0)
					{
						d.Add(StructureShapes.Beam(new Vector3(0f, y, 0f) + dir * shaft * 0.8f, cup - Vector3.up * 0.02f, 0.04f, 0.04f, StructureMaterial.Monolith, 20 + t * 10 + i, StructureRole.Detail));
					}
					if (d.Mid)
					{
						d.Add(StructureShapes.Cylinder(0.04f, -0.03f, 0.02f, 5, StructureMaterial.Adobe, 50 + t * 10 + i, false, StructureRole.Detail).Transform(Matrix4x4.Translate(cup)));
					}
					WlFlame(d, cup + Vector3.up * 0.02f, 0.06f, 80 + t * 10 + i);
				}
			}
			d.Emit(new Vector3(0f, 1.4f + 0.85f + 0.15f, -0.45f), SceneEmitterKind.Torch);
		}

		/// <summary>A ship's stern lantern: a tall hexagonal iron frame of horn panes under a domed cap.</summary>
		private static void WayFanal(StructureDraft d)
		{
			float foot;
			if (d.Variant == 0)
			{
				for (int i = 0; i < 3; i++)
				{
					float a = i * Mathf.PI * 2f / 3f;
					d.Add(StructureShapes.Beam(new Vector3(Mathf.Cos(a) * 0.35f, 0f, Mathf.Sin(a) * 0.35f), new Vector3(0f, 0.95f, 0f), 0.035f, 0.035f, StructureMaterial.Iron, 1 + i));
				}
				foot = 0.95f;
			}
			else
			{
				WlPost(d, 1.0f, 0.18f, StructureMaterial.Timber, 1);
				foot = 1.0f;
			}
			const int hex = 6;
			const float h = 0.62f;
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.06f, foot), new Vector2(0.2f, foot + 0.06f), new Vector2(0.2f, foot + 0.1f) }, hex, StructureMaterial.Iron, 10, false));
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.18f, foot + 0.1f), new Vector2(0.21f, foot + 0.1f + h) }, hex, StructureMaterial.Glass, 11, false));
			for (int i = 0; i < hex; i++)
			{
				float a = i * Mathf.PI * 2f / hex;
				d.Add(StructureShapes.Beam(new Vector3(Mathf.Cos(a) * 0.2f, foot + 0.1f, Mathf.Sin(a) * 0.2f), new Vector3(Mathf.Cos(a) * 0.23f, foot + 0.1f + h, Mathf.Sin(a) * 0.23f), 0.022f, 0.022f, StructureMaterial.Iron, 12 + i, StructureRole.Detail));
			}
			float top = foot + 0.1f + h;
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.26f, top), new Vector2(0.24f, top + 0.08f), new Vector2(0.14f, top + 0.2f), new Vector2(0.04f, top + 0.26f), new Vector2(0.03f, top + 0.4f), new Vector2(0f, top + 0.42f) }, hex, StructureMaterial.Iron, 20, false));
			d.Emit(new Vector3(0f, foot + 0.1f + h * 0.5f, 0f), SceneEmitterKind.Lamp);
		}

		/// <summary>A pierced terracotta lamp-jar standing on <paramref name="foot"/> (round-bellied or tall); its flame's place.</summary>
		private static Vector3 WlJar(StructureDraft d, Vector3 foot, bool tall, int key)
		{
			float h = tall ? 0.42f : 0.3f, r = tall ? 0.12f : 0.15f;
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0f, 0f), new Vector2(r * 0.6f, 0f), new Vector2(r, h * 0.35f), new Vector2(r * 0.85f, h * 0.8f), new Vector2(r * 0.4f, h), new Vector2(r * 0.45f, h + 0.04f), new Vector2(0f, h + 0.04f) },
				d.Seg(10, 6), StructureMaterial.Adobe, key, true, 0f, false).Transform(Matrix4x4.Translate(foot)));
			WlPiercings(d, foot + Vector3.up * h * 0.4f, r * 0.98f, 0.045f, 6, key + 10);
			WlPiercings(d, foot + Vector3.up * h * 0.7f, r * 0.9f, 0.035f, 6, key + 20, Mathf.PI / 6f);
			return foot + Vector3.up * h * 0.45f;
		}

		/// <summary>A pierced lamp-jar hung from an iron hook on a wall.</summary>
		private static void WayPiercedJar(StructureDraft d)
		{
			bool tall = d.Variant == 1;
			WlPlate(d, 0.07f, 0.2f, StructureMaterial.Iron, 1);
			d.Add(StructureShapes.Beam(new Vector3(0f, 0.06f, 0f), new Vector3(0f, 0.06f, -0.3f), 0.022f, 0.022f, StructureMaterial.Iron, 2));
			d.Add(StructureShapes.Beam(new Vector3(0f, 0.06f, -0.3f), new Vector3(0f, -0.02f, -0.3f), 0.014f, 0.014f, StructureMaterial.Iron, 3));
			float h = tall ? 0.46f : 0.34f;
			Vector3 foot = new Vector3(0f, -0.06f - h, -0.3f);
			d.Add(StructureShapes.Beam(new Vector3(0f, -0.02f, -0.3f), foot + Vector3.up * h, 0.008f, 0.008f, StructureMaterial.Iron, 4, StructureRole.Detail));
			d.Emit(WlJar(d, foot, tall, 10), SceneEmitterKind.Lamp);
		}

		/// <summary>A pierced lamp-jar on a pillar of stacked slabs.</summary>
		private static void WayPiercedJarPillar(StructureDraft d)
		{
			bool tall = d.Variant == 1;
			WlStack(d, 1.15f, 0.4f, 4, StructureMaterial.Sandstone, 1, 0.15f);
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, 1.18f, 0f), new Vector3(0.46f, 0.06f, 0.46f), d.Bevel(0.015f), StructureMaterial.Sandstone, 10));
			d.Emit(WlJar(d, new Vector3(0f, 1.21f, 0f), tall, 20), SceneEmitterKind.Lamp);
		}

		/// <summary>A waist-high stone fire altar: stepped with a bowl, or square with a carved band and horned corners round a basin.</summary>
		private static void WayFireAltar(StructureDraft d)
		{
			float top;
			if (d.Variant == 0)
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.1f, 0f), new Vector3(1.1f, 0.4f, 1.1f), d.Bevel(0.03f), StructureMaterial.Stone, 1));
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.45f, 0f), new Vector3(0.8f, 0.3f, 0.8f), d.Bevel(0.03f), StructureMaterial.Stone, 2));
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.72f, 0f), new Vector3(0.55f, 0.24f, 0.55f), d.Bevel(0.03f), StructureMaterial.Stone, 3));
				top = 0.84f;
				WlBowl(d, new Vector3(0f, top, 0f), 0.3f, 0.2f, StructureMaterial.Stone, 4);
				WlCoals(d, new Vector3(0f, top + 0.16f, 0f), 0.24f, 0.1f, 5);
				d.Emit(new Vector3(0f, top + 0.22f, 0f), SceneEmitterKind.Fire);
				return;
			}
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.38f, 0f), new Vector3(0.8f, 0.96f, 0.8f), d.Bevel(0.03f), StructureMaterial.Monolith, 1));
			d.Add(StructureShapes.Box(new Vector3(0f, 0.68f, 0f), new Vector3(0.84f, 0.12f, 0.84f), StructureMaterial.Monolith, 2, StructureRole.Detail));
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.9f, 0f), new Vector3(0.9f, 0.08f, 0.9f), d.Bevel(0.02f), StructureMaterial.Monolith, 3));
			for (int i = 0; i < 4; i++)
			{
				float x = (i % 2 == 0 ? -1f : 1f) * 0.38f, z = (i < 2 ? -1f : 1f) * 0.38f;
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.07f, 0.94f), new Vector2(0.05f, 1.06f), new Vector2(0f, 1.14f) }, 4, StructureMaterial.Monolith, 4 + i, false).Transform(Matrix4x4.Translate(new Vector3(x, 0f, z))));
			}
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.3f, 0.94f), new Vector2(0.3f, 1.0f), new Vector2(0.24f, 1.0f), new Vector2(0.24f, 0.95f) }, d.Seg(10, 6), StructureMaterial.Monolith, 10, true, 0f, true));
			WlCoals(d, new Vector3(0f, 1.0f, 0f), 0.24f, 0.06f, 11);
			d.Emit(new Vector3(0f, 1.06f, 0f), SceneEmitterKind.Fire);
		}

		/// <summary>An iron double cruse: two shallow pear-shaped pans one over the other, a moss wick at the upper's spout, hung from <paramref name="hook"/>; its flame.</summary>
		private static Vector3 WlCruse(StructureDraft d, Vector3 hook, int key)
		{
			Vector3 upper = hook + Vector3.down * 0.22f, lower = hook + Vector3.down * 0.3f;
			d.Add(StructureShapes.Beam(hook, upper + new Vector3(0f, 0.03f, 0.08f), 0.01f, 0.01f, StructureMaterial.Iron, key, StructureRole.Detail));
			for (int k = 0; k < 2; k++)
			{
				Vector3 c = k == 0 ? upper : lower;
				float r = k == 0 ? 0.08f : 0.1f;
				WlBowl(d, c, r, 0.04f, StructureMaterial.Iron, key + 1 + k * 3, StructureRole.Detail);
				d.Add(StructureShapes.Beam(c + new Vector3(0f, 0.03f, -r * 0.8f), c + new Vector3(0f, 0.035f, -r * 1.5f), 0.03f, 0.012f, StructureMaterial.Iron, key + 2 + k * 3, StructureRole.Detail));
			}
			Vector3 flame = upper + new Vector3(0f, 0.04f, -0.12f);
			WlFlame(d, flame, 0.06f, key + 10);
			return flame + Vector3.up * 0.03f;
		}

		/// <summary>An iron double cruse lamp hung from a hook on a wall.</summary>
		private static void WayIronCruse(StructureDraft d)
		{
			WlPlate(d, 0.07f, 0.18f, StructureMaterial.Iron, 1);
			d.Add(StructureShapes.Beam(new Vector3(0f, 0.04f, 0f), new Vector3(0f, 0.04f, -0.3f), 0.02f, 0.02f, StructureMaterial.Iron, 2));
			if (d.Variant == 1 && d.Mid)
			{
				d.Add(Ring(new Vector3(0f, 0.0f, -0.16f), 0.04f, 0.01f, Quaternion.Euler(0f, 0f, 90f), d.Seg(8, 5), StructureMaterial.Iron, 3, StructureRole.Detail));
			}
			d.Emit(WlCruse(d, new Vector3(0f, 0.03f, -0.28f), 10), SceneEmitterKind.Lamp);
		}

		/// <summary>An iron double cruse lamp hung from a hook on a short driftwood post.</summary>
		private static void WayIronCrusePost(StructureDraft d)
		{
			var post = new[] { new Vector3(0f, -0.35f, 0f), new Vector3(0.03f, 0.8f, -0.02f), new Vector3(-0.02f, 1.6f, 0.01f) };
			WlCrooked(d, post, 0.14f, 0.11f, StructureMaterial.Timber, 1);
			if (d.Variant == 0)
			{
				d.Add(StructureShapes.Beam(new Vector3(0f, 1.5f, 0f), new Vector3(0f, 1.5f, -0.32f), 0.02f, 0.02f, StructureMaterial.Iron, 5));
				d.Emit(WlCruse(d, new Vector3(0f, 1.49f, -0.3f), 10), SceneEmitterKind.Lamp);
				return;
			}
			d.Add(StructureShapes.Beam(new Vector3(0f, 1.55f, 0.3f), new Vector3(0f, 1.55f, -0.3f), 0.06f, 0.06f, StructureMaterial.Timber, 5));
			d.Emit(WlCruse(d, new Vector3(0f, 1.52f, -0.28f), 10), SceneEmitterKind.Lamp);
		}

		/// <summary>A fire in a stone bowl on a pillar of stacked slabs (tall and narrow, or low and broad with a corbelled top).</summary>
		private static void WaySlabHearth(StructureDraft d)
		{
			bool low = d.Variant == 1;
			float top = low ? 0.85f : 1.15f;
			WlStack(d, top, low ? 0.75f : 0.5f, low ? 5 : 7, StructureMaterial.Sandstone, 1, low ? 0f : 0.12f);
			if (low)
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, top + 0.04f, 0f), new Vector3(0.95f, 0.08f, 0.95f), d.Bevel(0.02f), StructureMaterial.Sandstone, 10));
				top += 0.08f;
			}
			WlBowl(d, new Vector3(0f, top, 0f), 0.32f, 0.18f, StructureMaterial.Sandstone, 11);
			WlCoals(d, new Vector3(0f, top + 0.15f, 0f), 0.26f, 0.1f, 12);
			d.Emit(new Vector3(0f, top + 0.22f, 0f), SceneEmitterKind.Fire);
		}

		/// <summary>A splinter-light: an iron fork holding a burning splinter over an ember trough.</summary>
		private static void WaySvetets(StructureDraft d)
		{
			const float top = 1.3f;
			if (d.Variant == 0)
			{
				WlPost(d, top, 0.12f, StructureMaterial.Timber, 1);
				// The trough on two short legs at the post's foot, under the splinter.
				d.Add(StructureShapes.Box(new Vector3(0f, 0.3f, -0.3f), new Vector3(0.3f, 0.12f, 0.3f), StructureMaterial.Timber, 2));
				d.Add(StructureShapes.Box(new Vector3(0f, 0.15f, -0.3f), new Vector3(0.05f, 0.3f, 0.05f), StructureMaterial.Timber, 3));
			}
			else
			{
				for (int i = 0; i < 3; i++)
				{
					float a = i * Mathf.PI * 2f / 3f;
					d.Add(StructureShapes.Beam(new Vector3(Mathf.Cos(a) * 0.35f, 0f, Mathf.Sin(a) * 0.35f), new Vector3(0f, top, 0f), 0.025f, 0.025f, StructureMaterial.Iron, 1 + i));
				}
				d.Add(StructureShapes.Box(new Vector3(0f, 0.25f, -0.3f), new Vector3(0.3f, 0.1f, 0.3f), StructureMaterial.Timber, 4));
			}
			// The fork, two tines, the splinter clamped across it and leaning out over the trough, its end alight.
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(new Vector3(0f, top, 0f), new Vector3(s * 0.05f, top + 0.18f, -0.02f), 0.014f, 0.014f, StructureMaterial.Iron, 10 + (s + 1) / 2, StructureRole.Detail));
			}
			Vector3 tip = new Vector3(0f, top + 0.05f, -0.36f);
			d.Add(StructureShapes.Beam(new Vector3(0f, top + 0.2f, 0.12f), tip, 0.022f, 0.008f, StructureMaterial.Timber, 12, StructureRole.Detail));
			WlFlame(d, tip, 0.08f, 13);
			d.Emit(tip + Vector3.up * 0.06f, SceneEmitterKind.Torch);
		}

		/// <summary>A hollow frosted ice cylinder on a stone block, a candle inside (its warm light dimmed through the ice).</summary>
		private static void WayIceLantern(StructureDraft d)
		{
			int seg = d.Seg(12, 6);
			float block;
			if (d.Variant == 0)
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.2f, 0f), new Vector3(0.55f, 0.6f, 0.55f), d.Bevel(0.03f), StructureMaterial.Stone, 1));
				block = 0.5f;
			}
			else
			{
				d.Add(StructureShapes.Cylinder(0.3f, -0.1f, 0.45f, seg, StructureMaterial.Stone, 1));
				block = 0.45f;
			}
			float topR = d.Variant == 0 ? 0.22f : 0.15f;
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.22f, block), new Vector2(topR, block + 0.6f), new Vector2(topR - 0.05f, block + 0.6f), new Vector2(0.17f, block + 0.04f) }, seg, StructureMaterial.IceBlock, 2, true, 0f, true));
			d.Add(StructureShapes.Cylinder(0.18f, block, block + 0.04f, seg, StructureMaterial.IceBlock, 3, false, StructureRole.Detail));
			d.Add(StructureShapes.Cylinder(0.03f, block + 0.04f, block + 0.2f, d.Seg(6, 4), StructureMaterial.Canvas, 4, false, StructureRole.Detail));
			WlFlame(d, new Vector3(0f, block + 0.2f, 0f), 0.06f, 5);
			d.Emit(new Vector3(0f, block + 0.3f, 0f), SceneEmitterKind.Lamp);
		}

		/// <summary>A crystal lens in a ring frame on a stone post, giving back the day after sunset (cold Glow, amber).</summary>
		private static void WaySunLens(StructureDraft d)
		{
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.7f, 0f), new Vector3(0.26f, 1.7f, 0.26f), d.Bevel(0.02f), StructureMaterial.Monolith, 1));
			Vector3 c;
			Quaternion tilt;
			if (d.Variant == 0)
			{
				d.Add(StructureShapes.Beam(new Vector3(0f, 1.55f, 0f), new Vector3(0f, 1.72f, -0.05f), 0.05f, 0.05f, StructureMaterial.Iron, 2));
				c = new Vector3(0f, 1.84f, -0.08f);
				tilt = Quaternion.Euler(-35f, 0f, 0f);
			}
			else
			{
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Beam(new Vector3(0f, 1.55f, 0f), new Vector3(s * 0.2f, 1.9f, 0f), 0.04f, 0.04f, StructureMaterial.Iron, 2 + (s + 1) / 2));
				}
				c = new Vector3(0f, 1.9f, 0f);
				tilt = Quaternion.Euler(90f, 0f, 0f);
			}
			d.Add(Ring(c, 0.18f, 0.035f, tilt, d.Seg(12, 6), StructureMaterial.Iron, 10, StructureRole.Structure));
			d.Add(StructureShapes.Sphere(Vector3.zero, new Vector3(0.17f, 0.04f, 0.17f), d.Seg(10, 6), d.Fine ? 4 : 3, StructureMaterial.Glass, 11, StructureRole.Detail).Transform(Matrix4x4.TRS(c, tilt, Vector3.one)));
			d.Add(StructureShapes.Sphere(Vector3.zero, new Vector3(0.07f, 0.045f, 0.07f), d.Seg(8, 5), 3, StructureMaterial.Faelight, 12, StructureRole.Detail).Transform(Matrix4x4.TRS(c, tilt, Vector3.one)));
			d.Emit(c + new Vector3(0f, 0f, -0.25f), SceneEmitterKind.Glow);
		}

		/// <summary>An iron fat-lamp cup hung on a hook from a carved post.</summary>
		private static void WaySergeLamp(StructureDraft d)
		{
			WlPost(d, 1.9f, 0.16f, StructureMaterial.Timber, 1);
			if (d.Variant == 0)
			{
				for (int i = 0; i < 3; i++)
				{
					d.Add(StructureShapes.Box(new Vector3(0f, 0.7f + i * 0.35f, 0f), new Vector3(0.19f, 0.06f, 0.19f), StructureMaterial.Dyed, 2 + i, StructureRole.Detail));
				}
			}
			else
			{
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.11f, 1.9f), new Vector2(0.13f, 2.0f), new Vector2(0.06f, 2.15f), new Vector2(0f, 2.18f) }, 4, StructureMaterial.Timber, 2, false, Mathf.PI * 0.25f));
			}
			Vector3 hook = new Vector3(0f, 1.6f, -0.32f);
			d.Add(StructureShapes.Beam(new Vector3(0f, 1.6f, 0f), hook, 0.02f, 0.02f, StructureMaterial.Iron, 10));
			d.Add(StructureShapes.Beam(hook, hook + Vector3.down * 0.18f, 0.01f, 0.01f, StructureMaterial.Iron, 11, StructureRole.Detail));
			Vector3 cup = hook + Vector3.down * 0.26f;
			WlBowl(d, cup, 0.08f, 0.06f, StructureMaterial.Iron, 12);
			WlFlame(d, cup + Vector3.up * 0.06f, 0.07f, 13);
			d.Emit(cup + Vector3.up * 0.1f, SceneEmitterKind.Torch);
		}

		/// <summary>A soapstone cup-lamp on an iron spike driven into the ground or the ice.</summary>
		private static void WayKolaSpike(StructureDraft d)
		{
			Vector3 cup;
			if (d.Variant == 0)
			{
				d.Add(StructureShapes.Rod(new Vector3(0f, -0.4f, 0f), new Vector3(0f, 1.15f, 0f), 0.02f, d.Seg(6, 4), StructureMaterial.Iron, 1));
				cup = new Vector3(0f, 1.15f, 0f);
			}
			else
			{
				// A twisted spike (in short turned lengths) with a crook carrying the cup out over −z.
				Vector3 last = new Vector3(0f, -0.4f, 0f);
				int steps = d.Fine ? 6 : 3;
				for (int i = 1; i <= steps; i++)
				{
					var next = new Vector3(0f, -0.4f + 1.6f * i / steps, 0f);
					d.Add(StructureShapes.Block((last + next) * 0.5f, new Vector3(0.03f, (next - last).magnitude, 0.03f), Quaternion.Euler(0f, i * 30f, 0f), StructureMaterial.Iron, 1 + i));
					last = next;
				}
				d.Add(StructureShapes.Beam(last, last + new Vector3(0f, 0.08f, -0.22f), 0.02f, 0.02f, StructureMaterial.Iron, 10));
				cup = last + new Vector3(0f, -0.04f, -0.24f);
				d.Add(StructureShapes.Beam(last + new Vector3(0f, 0.08f, -0.22f), cup + Vector3.up * 0.1f, 0.012f, 0.012f, StructureMaterial.Iron, 11, StructureRole.Detail));
			}
			WlBowl(d, cup, 0.1f, 0.07f, StructureMaterial.Stone, 20);
			WlFlame(d, cup + Vector3.up * 0.06f, 0.06f, 21);
			d.Emit(cup + Vector3.up * 0.1f, SceneEmitterKind.Torch);
		}

		/// <summary>A crescent of glowing lacquer on a tall pole, tassels hanging (cold Glow, silver).</summary>
		private static void WayCrescentStandard(StructureDraft d)
		{
			const float top = 2.5f;
			WlPole(d, top, 0.055f, StructureMaterial.Timber, 1);
			Vector3 c = new Vector3(0f, top + 0.22f, 0f);
			if (d.Variant == 1)
			{
				d.Add(StructureShapes.Beam(new Vector3(-0.35f, top - 0.15f, 0f), new Vector3(0.35f, top - 0.15f, 0f), 0.04f, 0.04f, StructureMaterial.Timber, 2));
			}
			// The crescent: an arc of short lengths, horns up, its inner edge glowing.
			int steps = d.Fine ? 9 : 5;
			Vector3 last = Vector3.zero;
			for (int i = 0; i <= steps; i++)
			{
				float a = Mathf.PI * (0.15f + 0.7f * i / steps) + Mathf.PI;
				var p = c + new Vector3(Mathf.Cos(a) * 0.2f, Mathf.Sin(a) * 0.2f + 0.04f, 0f);
				if (i > 0)
				{
					float w = 0.035f * Mathf.Sin(Mathf.PI * (i - 0.5f) / steps) + 0.012f;
					d.Add(StructureShapes.Beam(last, p, w, 0.025f, StructureMaterial.Iron, 10 + i));
					d.Add(StructureShapes.Beam(last + Vector3.up * w * 0.6f, p + Vector3.up * w * 0.6f, w * 0.5f, 0.03f, StructureMaterial.Runelight, 30 + i, StructureRole.Detail));
				}
				last = p;
			}
			d.Add(StructureShapes.Sphere(new Vector3(0f, top + 0.03f, 0f), new Vector3(0.05f, 0.05f, 0.05f), d.Seg(6, 4), 3, StructureMaterial.Iron, 3, StructureRole.Detail));
			int tassels = d.Variant == 1 ? 2 : 1;
			for (int t = 0; t < tassels; t++)
			{
				float x = tassels == 1 ? 0f : (t == 0 ? -0.3f : 0.3f);
				float y = tassels == 1 ? top - 0.02f : top - 0.17f;
				d.Add(StructureShapes.Beam(new Vector3(x, y, tassels == 1 ? -0.05f : 0f), new Vector3(x, y - 0.45f, tassels == 1 ? -0.06f : 0f), 0.06f, 0.03f, StructureMaterial.Dyed, 40 + t, StructureRole.Detail));
			}
			d.Emit(c + new Vector3(0f, 0f, -0.2f), SceneEmitterKind.Glow);
		}

		/// <summary>A many-spouted clay oil lamp in a mud niche in a wall.</summary>
		private static void WayClayLamp(StructureDraft d)
		{
			Vector3 floor = WlNiche(d, 0.38f, 0.32f, 0.28f, 0.08f, StructureMaterial.Adobe, 1, d.Variant == 1 ? 2 : 0);
			Vector3 flame = WlSaucer(d, floor + new Vector3(0f, 0f, -0.01f), 0.075f, d.Variant == 1 ? 3 : 4, StructureMaterial.Adobe, 20, Mathf.PI * 0.25f);
			WlSoot(d, new Vector3(0f, 0.24f, 0f), 0.26f, 0.26f, 40);
			d.Emit(flame, SceneEmitterKind.Lamp);
		}

		/// <summary>A many-spouted clay oil lamp on a carved post.</summary>
		private static void WayClayLampPost(StructureDraft d)
		{
			const float top = 1.5f;
			WlPost(d, top, 0.17f, StructureMaterial.Timber, 1);
			for (int i = 0; i < 2; i++)
			{
				d.Add(StructureShapes.Block(new Vector3(0f, 0.8f + i * 0.35f, 0f), new Vector3(0.2f, 0.12f, 0.2f), Quaternion.Euler(0f, 45f, 0f), StructureMaterial.Timber, 2 + i, StructureRole.Detail));
			}
			if (d.Variant == 0)
			{
				d.Add(StructureShapes.Cylinder(0.17f, top, top + 0.04f, d.Seg(10, 6), StructureMaterial.Adobe, 10));
			}
			else
			{
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Beam(new Vector3(0f, top - 0.05f, 0f), new Vector3(s * 0.14f, top + 0.04f, 0f), 0.06f, 0.06f, StructureMaterial.Timber, 10 + (s + 1) / 2));
				}
			}
			Vector3 flame = WlSaucer(d, new Vector3(0f, top + 0.04f, 0f), 0.08f, d.Variant == 1 ? 3 : 4, StructureMaterial.Adobe, 20, Mathf.PI * 0.25f);
			d.Emit(flame, SceneEmitterKind.Lamp);
		}

		/// <summary>A sunken lamp: a green-glowing jar in a weed-grown iron cage (cold Glow, green).</summary>
		private static void WayDrownedLamp(StructureDraft d)
		{
			Vector3 jar;
			if (d.Variant == 0)
			{
				WlPost(d, 1.3f, 0.1f, StructureMaterial.Timber, 1);
				jar = new Vector3(0f, 1.46f, 0f);
			}
			else
			{
				var stake = new[] { new Vector3(0f, -0.35f, 0f), new Vector3(0.05f, 1.1f, 0f), new Vector3(0f, 1.6f, -0.2f), new Vector3(0f, 1.62f, -0.42f) };
				WlCrooked(d, stake, 0.1f, 0.06f, StructureMaterial.Timber, 1);
				d.Add(StructureShapes.Beam(new Vector3(0f, 1.6f, -0.42f), new Vector3(0f, 1.32f, -0.42f), 0.01f, 0.01f, StructureMaterial.Iron, 5, StructureRole.Detail));
				jar = new Vector3(0f, 1.18f, -0.42f);
			}
			WlCage(d, jar, 0.11f, 0.3f, 5, 10);
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0f, -0.11f), new Vector2(0.07f, -0.1f), new Vector2(0.08f, 0.02f), new Vector2(0.04f, 0.1f), new Vector2(0f, 0.11f) }, d.Seg(8, 5), StructureMaterial.Glass, 30, true, 0f, false).Transform(Matrix4x4.Translate(jar)));
			d.Add(StructureShapes.Sphere(jar, new Vector3(0.04f, 0.05f, 0.04f), d.Seg(6, 4), 3, StructureMaterial.Faelight, 31, StructureRole.Detail));
			if (d.Mid)
			{
				for (int i = 0; i < 3; i++)
				{
					float a = i * 2.1f;
					var top = jar + new Vector3(Mathf.Cos(a) * 0.1f, 0.12f, Mathf.Sin(a) * 0.1f);
					d.Add(StructureShapes.Beam(top, top + new Vector3(0f, -0.35f, 0f), 0.03f, 0.008f, StructureMaterial.Dyed, 40 + i, StructureRole.Detail));
				}
			}
			d.Emit(jar, SceneEmitterKind.Glow);
		}

		/// <summary>A resin pine torch wedged into a crack of a wall with stones, a soot tongue over it.</summary>
		private static void WayTeaTorch(StructureDraft d)
		{
			// The wedging stones against the wall.
			for (int i = 0; i < 3; i++)
			{
				d.Add(StructureShapes.Block(new Vector3((i - 1) * 0.09f, -0.04f + (i % 2) * 0.05f, -0.05f), new Vector3(0.1f, 0.08f, 0.09f), Quaternion.Euler(0f, i * 20f, i * 15f), StructureMaterial.Fieldstone, 1 + i, StructureRole.Structure, d.Bevel(0.01f)));
			}
			int torches = d.Variant == 1 ? 2 : 1;
			Vector3 first = Vector3.zero;
			for (int t = 0; t < torches; t++)
			{
				float x = torches == 1 ? 0f : (t == 0 ? -0.08f : 0.08f);
				Vector3 lean = new Vector3(x * 1.5f, 0.85f, -0.5f).normalized;
				Vector3 foot = new Vector3(x, -0.06f, 0.0f), head = foot + lean * 0.5f;
				d.Add(StructureShapes.Beam(foot, head, 0.04f, 0.04f, StructureMaterial.Timber, 10 + t * 3)).Grain = lean;
				d.Add(StructureShapes.Beam(head - lean * 0.02f, head + lean * 0.12f, 0.06f, 0.06f, StructureMaterial.Ash, 11 + t * 3, StructureRole.Detail));
				if (t == 0)
				{
					first = head + lean * 0.14f;
				}
			}
			WlSoot(d, new Vector3(0f, 0.3f, 0f), 0.24f, 0.4f, 30);
			d.Emit(first, SceneEmitterKind.Torch);
		}

		/// <summary>A ring of stones round a small fire on the ground (a kettle on three stones on v1).</summary>
		private static void WayHearthRing(StructureDraft d)
		{
			int stones = d.Variant == 1 ? 7 : 8;
			for (int i = 0; i < stones; i++)
			{
				float a = i * Mathf.PI * 2f / stones + d.Range(i, 1, -0.15f, 0.15f);
				d.Add(StructureShapes.Block(new Vector3(Mathf.Cos(a) * 0.42f, 0.06f, Mathf.Sin(a) * 0.42f), new Vector3(d.Range(i, 2, 0.2f, 0.28f), d.Range(i, 3, 0.16f, 0.24f), 0.18f),
					Quaternion.Euler(d.Range(i, 4, -10f, 10f), -a * Mathf.Rad2Deg, 0f), StructureMaterial.Fieldstone, 1 + i, StructureRole.Structure, d.Bevel(0.02f)));
			}
			WlCoals(d, new Vector3(0f, 0.06f, 0f), 0.3f, 0.08f, 20);
			for (int i = 0; i < 3; i++)
			{
				float a = i * 2.2f;
				d.Add(StructureShapes.Beam(new Vector3(Mathf.Cos(a) * 0.28f, 0.03f, Mathf.Sin(a) * 0.28f), new Vector3(-Mathf.Cos(a) * 0.05f, 0.12f, -Mathf.Sin(a) * 0.05f), 0.04f, 0.04f, StructureMaterial.Timber, 30 + i, StructureRole.Detail));
			}
			if (d.Variant == 1)
			{
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0f, 0.2f), new Vector2(0.12f, 0.22f), new Vector2(0.14f, 0.34f), new Vector2(0.08f, 0.42f), new Vector2(0f, 0.43f) }, d.Seg(6, 4), StructureMaterial.Iron, 40, true, 0f, false));
				d.Add(StructureShapes.Beam(new Vector3(0.08f, 0.38f, 0f), new Vector3(0.2f, 0.36f, 0f), 0.02f, 0.02f, StructureMaterial.Iron, 41, StructureRole.Detail));
			}
			d.Emit(new Vector3(0f, 0.15f, 0f), SceneEmitterKind.Fire);
		}

		/// <summary>A ring of glass oil-cups hung on three chains from a stone post's iron crook.</summary>
		private static void WayPolycandelon(StructureDraft d)
		{
			const float top = 2.6f;
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, top * 0.5f - 0.2f, 0f), new Vector3(0.24f, top + 0.4f, 0.24f), d.Bevel(0.02f), StructureMaterial.Monolith, 1));
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, top + 0.04f, 0f), new Vector3(0.3f, 0.08f, 0.3f), d.Bevel(0.02f), StructureMaterial.Monolith, 2));
			// The crook out over −z.
			Vector3 hook = new Vector3(0f, top + 0.05f, -0.62f);
			d.Add(StructureShapes.Beam(new Vector3(0f, top - 0.2f, -0.1f), new Vector3(0f, top + 0.2f, -0.3f), 0.03f, 0.03f, StructureMaterial.Iron, 3));
			d.Add(StructureShapes.Beam(new Vector3(0f, top + 0.2f, -0.3f), new Vector3(0f, top + 0.12f, -0.62f), 0.03f, 0.03f, StructureMaterial.Iron, 4));
			d.Add(StructureShapes.Beam(new Vector3(0f, top + 0.12f, -0.62f), hook, 0.016f, 0.016f, StructureMaterial.Iron, 5));
			int cups = d.Variant == 1 ? 8 : 6;
			float r = d.Variant == 1 ? 0.3f : 0.24f;
			Vector3 ring = hook + Vector3.down * 0.55f;
			d.Add(Ring(ring, r, 0.022f, Quaternion.identity, d.Seg(12, 6), StructureMaterial.Iron, 10, StructureRole.Structure));
			for (int i = 0; i < 3; i++)
			{
				float a = i * Mathf.PI * 2f / 3f;
				d.Add(StructureShapes.Beam(hook, ring + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r), 0.008f, 0.008f, StructureMaterial.Iron, 11 + i, StructureRole.Detail));
			}
			for (int i = 0; i < cups; i++)
			{
				float a = i * Mathf.PI * 2f / cups + 0.3f;
				var c = ring + new Vector3(Mathf.Cos(a) * r, 0f, Mathf.Sin(a) * r);
				if (d.Mid)
				{
					d.Add(StructureShapes.Lathe(new[] { new Vector2(0f, -0.07f), new Vector2(0.035f, -0.04f), new Vector2(0.04f, 0.02f) }, 5, StructureMaterial.Glass, 20 + i, true, 0f, false).Transform(Matrix4x4.Translate(c)));
				}
				WlFlame(d, c + Vector3.up * 0.02f, 0.05f, 40 + i);
			}
			d.Emit(ring + Vector3.up * 0.06f, SceneEmitterKind.Lamp);
		}

		/// <summary>A clay saucer lamp under a little hood of palm ribs standing on <paramref name="at"/>; its flame.</summary>
		private static Vector3 WlHoodedSaucer(StructureDraft d, Vector3 at, int key)
		{
			Vector3 flame = WlSaucer(d, at, 0.07f, 1, StructureMaterial.Adobe, key);
			// The hood: a little lean-to of ribs over the saucer, open to −z.
			var outline = new[] { new Vector2(-0.13f, 0f), new Vector2(0.13f, 0f), new Vector2(0f, 0.12f) };
			d.Add(StructureShapes.Prism(outline, -0.08f, 0.14f, StructureMaterial.Reed, key + 30).Transform(Matrix4x4.Translate(at + Vector3.up * 0.2f)));
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(at + new Vector3(s * 0.11f, 0f, 0.1f), at + new Vector3(s * 0.11f, 0.2f, 0.1f), 0.02f, 0.02f, StructureMaterial.Reed, key + 31 + (s + 1) / 2, StructureRole.Detail));
			}
			return flame;
		}

		/// <summary>A clay saucer lamp under a little rib hood on a wall bracket (a shelf, or a hook and a hanging saucer).</summary>
		private static void WayCastorLamp(StructureDraft d)
		{
			WlPlate(d, 0.12f, 0.3f, StructureMaterial.Timber, 1);
			if (d.Variant == 0)
			{
				d.Add(StructureShapes.Box(new Vector3(0f, -0.08f, -0.16f), new Vector3(0.28f, 0.03f, 0.3f), StructureMaterial.Timber, 2));
				d.Add(StructureShapes.Beam(new Vector3(0f, -0.25f, -0.01f), new Vector3(0f, -0.1f, -0.22f), 0.03f, 0.03f, StructureMaterial.Timber, 3));
				d.Emit(WlHoodedSaucer(d, new Vector3(0f, -0.065f, -0.2f), 10), SceneEmitterKind.Lamp);
				return;
			}
			d.Add(StructureShapes.Beam(new Vector3(0f, 0.1f, 0f), new Vector3(0f, 0.1f, -0.32f), 0.03f, 0.03f, StructureMaterial.Timber, 2));
			for (int i = 0; i < 3; i++)
			{
				float a = i * Mathf.PI * 2f / 3f;
				d.Add(StructureShapes.Beam(new Vector3(0f, 0.08f, -0.3f), new Vector3(Mathf.Cos(a) * 0.08f, -0.18f, -0.3f + Mathf.Sin(a) * 0.08f), 0.008f, 0.008f, StructureMaterial.Canvas, 3 + i, StructureRole.Detail));
			}
			d.Emit(WlHoodedSaucer(d, new Vector3(0f, -0.22f, -0.3f), 10), SceneEmitterKind.Lamp);
		}

		/// <summary>A clay saucer lamp under a little rib hood on a ringed palm-trunk post.</summary>
		private static void WayCastorLampPost(StructureDraft d)
		{
			const float top = 1.6f;
			var trunk = new List<Vector2> { new Vector2(0.11f, -0.35f) };
			int rings = d.Fine ? 8 : 4;
			for (int i = 1; i <= rings; i++)
			{
				float y = -0.35f + (top + 0.35f) * i / rings;
				trunk.Add(new Vector2(0.1f, y - 0.06f));
				trunk.Add(new Vector2(0.115f, y));
			}
			d.Add(StructureShapes.Lathe(trunk, d.Seg(8, 5), StructureMaterial.Timber, 1, true, 0f, false, true));
			if (d.Variant == 0)
			{
				d.Add(StructureShapes.Cylinder(0.13f, top, top + 0.03f, d.Seg(8, 5), StructureMaterial.Timber, 2));
				d.Emit(WlHoodedSaucer(d, new Vector3(0f, top + 0.03f, 0f), 10), SceneEmitterKind.Lamp);
				return;
			}
			d.Add(StructureShapes.Beam(new Vector3(0f, top - 0.2f, 0f), new Vector3(0f, top - 0.2f, -0.32f), 0.05f, 0.05f, StructureMaterial.Timber, 2));
			d.Add(StructureShapes.Box(new Vector3(0f, top - 0.16f, -0.32f), new Vector3(0.2f, 0.03f, 0.2f), StructureMaterial.Timber, 3));
			d.Emit(WlHoodedSaucer(d, new Vector3(0f, top - 0.145f, -0.32f), 10), SceneEmitterKind.Lamp);
		}

		/// <summary>A flat iron dish with a wick spout and a rat-tail handle on a twisted hook, hung from <paramref name="hook"/>; its flame.</summary>
		private static Vector3 WlKriseilu(StructureDraft d, Vector3 hook, int key)
		{
			Vector3 dish = hook + new Vector3(0f, -0.22f, 0f);
			d.Add(StructureShapes.Block((hook + dish) * 0.5f + new Vector3(0f, 0f, 0.05f), new Vector3(0.012f, 0.22f, 0.012f), Quaternion.Euler(-20f, 0f, 0f), StructureMaterial.Iron, key, StructureRole.Detail));
			WlBowl(d, dish, 0.09f, 0.03f, StructureMaterial.Iron, key + 1, StructureRole.Detail);
			d.Add(StructureShapes.Beam(dish + new Vector3(0f, 0.025f, -0.08f), dish + new Vector3(0f, 0.03f, -0.15f), 0.03f, 0.012f, StructureMaterial.Iron, key + 2, StructureRole.Detail));
			d.Add(StructureShapes.Beam(dish + new Vector3(0f, 0.03f, 0.08f), dish + new Vector3(0f, 0.16f, 0.16f), 0.01f, 0.01f, StructureMaterial.Iron, key + 3, StructureRole.Detail));
			Vector3 flame = dish + new Vector3(0f, 0.035f, -0.15f);
			WlFlame(d, flame, 0.07f, key + 4);
			return flame + Vector3.up * 0.04f;
		}

		/// <summary>A Basque iron oil dish hung from a twisted hook on a staff (two on a crossbar on v1).</summary>
		private static void WayKriseilu(StructureDraft d)
		{
			const float top = 2.2f;
			WlPole(d, top, 0.035f, StructureMaterial.Timber, 1);
			if (d.Variant == 0)
			{
				d.Add(StructureShapes.Beam(new Vector3(0f, top - 0.05f, 0f), new Vector3(0f, top + 0.05f, -0.25f), 0.018f, 0.018f, StructureMaterial.Iron, 2));
				d.Emit(WlKriseilu(d, new Vector3(0f, top + 0.05f, -0.25f), 10), SceneEmitterKind.Torch);
				return;
			}
			d.Add(StructureShapes.Beam(new Vector3(-0.32f, top - 0.1f, 0f), new Vector3(0.32f, top - 0.1f, 0f), 0.045f, 0.045f, StructureMaterial.Timber, 2));
			Vector3 first = WlKriseilu(d, new Vector3(-0.28f, top - 0.12f, 0f), 10);
			WlKriseilu(d, new Vector3(0.28f, top - 0.12f, 0f), 20);
			d.Emit(first, SceneEmitterKind.Torch);
		}

		/// <summary>An iron wheel crown on a stone post, an oil lamp hung inside its spokes.</summary>
		private static void WayWheelLamp(StructureDraft d)
		{
			const float top = 2.0f;
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, top * 0.5f - 0.2f, 0f), new Vector3(0.28f, top + 0.4f, 0.28f), d.Bevel(0.02f), StructureMaterial.Stone, 1));
			int spokes = d.Variant == 1 ? 8 : 6;
			Vector3 hub = new Vector3(0f, top + 0.55f, 0f);
			d.Add(StructureShapes.Rod(new Vector3(0f, top, 0f), hub, 0.025f, d.Seg(6, 4), StructureMaterial.Iron, 2));
			d.Add(Ring(hub, 0.34f, 0.03f, Quaternion.Euler(90f, 0f, 0f), d.Seg(14, 8), StructureMaterial.Iron, 3, StructureRole.Structure));
			for (int i = 0; i < spokes; i++)
			{
				float a = i * Mathf.PI * 2f / spokes;
				d.Add(StructureShapes.Beam(hub, hub + new Vector3(Mathf.Cos(a) * 0.33f, Mathf.Sin(a) * 0.33f, 0f), 0.018f, 0.018f, StructureMaterial.Iron, 10 + i, StructureRole.Detail));
			}
			if (d.Variant == 1)
			{
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.03f, 0.34f), new Vector2(0.05f, 0.4f), new Vector2(0f, 0.5f) }, 4, StructureMaterial.Iron, 30, false).Transform(Matrix4x4.Translate(hub)));
			}
			Vector3 cup = hub + Vector3.down * 0.18f;
			d.Add(StructureShapes.Beam(hub, cup + Vector3.up * 0.05f, 0.008f, 0.008f, StructureMaterial.Iron, 31, StructureRole.Detail));
			WlBowl(d, cup, 0.07f, 0.05f, StructureMaterial.Iron, 32);
			WlFlame(d, cup + Vector3.up * 0.05f, 0.06f, 33);
			d.Emit(cup + Vector3.up * 0.1f, SceneEmitterKind.Lamp);
		}

		/// <summary>A tilted metal mirror disc in a ring frame on a stone post (cold Glow, white).</summary>
		private static void WayVolvaMirror(StructureDraft d)
		{
			const float top = 2.0f;
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, top * 0.5f - 0.2f, 0f), new Vector3(0.24f, top + 0.4f, 0.24f), d.Bevel(0.02f), StructureMaterial.Monolith, 1));
			Vector3 c = new Vector3(0f, top + 0.3f, -0.05f);
			if (d.Variant == 0)
			{
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Beam(new Vector3(0f, top, 0f), new Vector3(s * 0.28f, top + 0.3f, -0.05f), 0.035f, 0.035f, StructureMaterial.Iron, 2 + (s + 1) / 2));
				}
			}
			else
			{
				d.Add(StructureShapes.Beam(new Vector3(-0.3f, top + 0.3f, -0.05f), new Vector3(0.3f, top + 0.3f, -0.05f), 0.04f, 0.04f, StructureMaterial.Iron, 2));
				d.Add(StructureShapes.Beam(new Vector3(0f, top - 0.05f, 0f), new Vector3(0f, top + 0.3f, -0.05f), 0.04f, 0.04f, StructureMaterial.Iron, 3));
			}
			Quaternion tilt = Quaternion.Euler(60f, 0f, 0f);
			d.Add(Ring(c, 0.25f, 0.04f, tilt, d.Seg(14, 8), StructureMaterial.Iron, 10, StructureRole.Structure));
			d.Add(StructureShapes.Cylinder(0.24f, -0.012f, 0.012f, d.Seg(14, 8), StructureMaterial.Iron, 11).Transform(Matrix4x4.TRS(c, tilt, Vector3.one)));
			d.Add(StructureShapes.Cylinder(0.2f, 0.012f, 0.018f, d.Seg(14, 8), StructureMaterial.Runelight, 12, false, StructureRole.Detail).Transform(Matrix4x4.TRS(c, tilt, Vector3.one)));
			d.Emit(c + tilt * Vector3.up * 0.3f, SceneEmitterKind.Glow);
		}

		/// <summary>A square stone post whose arm (or gallows of two) hangs a glass plumb-bob dead still on a wire (cold Glow, white).</summary>
		private static void WayPlummet(StructureDraft d)
		{
			const float top = 2.6f;
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, top * 0.5f - 0.2f, 0f), new Vector3(0.22f, top + 0.4f, 0.22f), d.Bevel(0.02f), StructureMaterial.Monolith, 1));
			int arms = d.Variant == 1 ? 2 : 1;
			Vector3 first = Vector3.zero;
			for (int a = 0; a < arms; a++)
			{
				Vector3 dir = arms == 1 ? Vector3.back : (a == 0 ? Vector3.left : Vector3.right);
				Vector3 end = new Vector3(0f, top - 0.1f, 0f) + dir * 0.6f;
				d.Add(StructureShapes.Beam(new Vector3(0f, top - 0.1f, 0f) - dir * 0.06f, end, 0.06f, 0.06f, StructureMaterial.Iron, 10 + a * 5));
				d.Add(StructureShapes.Beam(new Vector3(0f, top - 0.5f, 0f) + dir * 0.1f, end * 0.5f + new Vector3(0f, top - 0.1f, 0f) * 0.5f, 0.035f, 0.035f, StructureMaterial.Iron, 11 + a * 5, StructureRole.Detail));
				Vector3 bob = end + Vector3.down * 1.0f;
				d.Add(StructureShapes.Beam(end, bob + Vector3.up * 0.14f, 0.006f, 0.006f, StructureMaterial.Iron, 12 + a * 5, StructureRole.Detail));
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0f, 0.14f), new Vector2(0.04f, 0.1f), new Vector2(0.07f, 0f), new Vector2(0.05f, -0.06f), new Vector2(0f, -0.12f) }, d.Seg(8, 5), StructureMaterial.Glass, 13 + a * 5, true, 0f, false).Transform(Matrix4x4.Translate(bob)));
				d.Add(StructureShapes.Sphere(bob, new Vector3(0.03f, 0.04f, 0.03f), d.Seg(6, 4), 3, StructureMaterial.Runelight, 14 + a * 5, StructureRole.Detail));
				if (a == 0)
				{
					first = bob;
				}
			}
			d.Emit(first, SceneEmitterKind.Glow);
		}

		/// <summary>An open iron oil lamp ("frog lamp") hung from a hook driven into a wall.</summary>
		private static void WayFrogLamp(StructureDraft d)
		{
			WlPlate(d, 0.05f, 0.08f, StructureMaterial.Iron, 1);
			d.Add(StructureShapes.Beam(new Vector3(0f, 0.02f, 0f), new Vector3(0f, 0.02f, -0.26f), 0.016f, 0.016f, StructureMaterial.Iron, 2));
			d.Add(StructureShapes.Beam(new Vector3(0f, 0.02f, -0.25f), new Vector3(0f, 0.06f, -0.25f), 0.012f, 0.012f, StructureMaterial.Iron, 3, StructureRole.Detail));
			Vector3 pan = new Vector3(0f, -0.2f, -0.25f);
			d.Add(StructureShapes.Beam(new Vector3(0f, 0.05f, -0.24f), pan + new Vector3(0f, 0.05f, 0.08f), 0.01f, 0.01f, StructureMaterial.Iron, 4, StructureRole.Detail));
			WlBowl(d, pan, 0.07f, 0.035f, StructureMaterial.Iron, 5, StructureRole.Detail);
			d.Add(StructureShapes.Beam(pan + new Vector3(0f, 0.025f, -0.06f), pan + new Vector3(0f, 0.03f, -0.13f), 0.025f, 0.01f, StructureMaterial.Iron, 6, StructureRole.Detail));
			if (d.Variant == 1)
			{
				WlBowl(d, pan + Vector3.down * 0.08f, 0.05f, 0.025f, StructureMaterial.Iron, 7, StructureRole.Detail);
				d.Add(StructureShapes.Beam(pan + new Vector3(0f, 0f, 0.05f), pan + new Vector3(0f, -0.06f, 0.04f), 0.008f, 0.008f, StructureMaterial.Iron, 8, StructureRole.Detail));
			}
			Vector3 flame = pan + new Vector3(0f, 0.035f, -0.13f);
			WlFlame(d, flame, 0.06f, 9);
			d.Emit(flame + Vector3.up * 0.04f, SceneEmitterKind.Lamp);
		}

		/// <summary>A horn-paned shutter (timber frame, horn lit at night) at <paramref name="at"/>, turned open by <paramref name="open"/> degrees about its left edge.</summary>
		private static void WlShutter(StructureDraft d, Vector3 hinge, float width, float height, float open, int key)
		{
			Matrix4x4 m = Matrix4x4.TRS(hinge, Quaternion.Euler(0f, -open, 0f), Vector3.one);
			d.Add(StructureShapes.Box(new Vector3(width * 0.5f, 0f, 0f), new Vector3(width - 0.04f, height - 0.04f, 0.01f), StructureMaterial.Glass, key).Transform(m));
			d.Add(StructureShapes.Box(new Vector3(0.015f, 0f, 0f), new Vector3(0.03f, height, 0.025f), StructureMaterial.Timber, key + 1, StructureRole.Detail).Transform(m));
			d.Add(StructureShapes.Box(new Vector3(width - 0.015f, 0f, 0f), new Vector3(0.03f, height, 0.025f), StructureMaterial.Timber, key + 2, StructureRole.Detail).Transform(m));
			d.Add(StructureShapes.Box(new Vector3(width * 0.5f, height * 0.5f - 0.015f, 0f), new Vector3(width, 0.03f, 0.025f), StructureMaterial.Timber, key + 3, StructureRole.Detail).Transform(m));
			d.Add(StructureShapes.Box(new Vector3(width * 0.5f, -height * 0.5f + 0.015f, 0f), new Vector3(width, 0.03f, 0.025f), StructureMaterial.Timber, key + 4, StructureRole.Detail).Transform(m));
		}

		/// <summary>A flagstone niche built out from a gable or a dyke end, a horn-paned shutter at its mouth.</summary>
		private static void WayHornBole(StructureDraft d)
		{
			float high = d.Variant == 1 ? 0.46f : 0.34f;
			Vector3 floor = WlNiche(d, 0.32f, high, 0.3f, 0.07f, StructureMaterial.Slate, 1, 0);
			Vector3 flame = WlSaucer(d, floor + new Vector3(0f, 0f, 0.02f), 0.06f, 1, StructureMaterial.Iron, 20);
			WlShutter(d, new Vector3(-0.16f, 0f, -0.305f), 0.32f, high, d.Variant == 1 ? 15f : 70f, 30);
			d.Emit(flame, SceneEmitterKind.Lamp);
		}

		/// <summary>A pillar of laid flags with a horn-paned lamp niche in its head.</summary>
		private static void WayHornBolePillar(StructureDraft d)
		{
			const float head = 1.25f;
			WlStack(d, head, 0.5f, 5, StructureMaterial.Slate, 1, d.Variant == 1 ? 0.25f : 0f);
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Box(new Vector3(s * 0.19f, head + 0.2f, 0f), new Vector3(0.07f, 0.4f, 0.46f), StructureMaterial.Slate, 20 + (s + 1) / 2));
			}
			d.Add(StructureShapes.Box(new Vector3(0f, head + 0.2f, 0.19f), new Vector3(0.31f, 0.4f, 0.07f), StructureMaterial.Slate, 22));
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, head + 0.44f, 0f), new Vector3(0.56f, 0.08f, 0.56f), d.Bevel(0.015f), StructureMaterial.Slate, 23));
			Vector3 flame = WlSaucer(d, new Vector3(0f, head, 0f), 0.06f, 1, StructureMaterial.Iron, 30);
			WlShutter(d, new Vector3(-0.155f, head + 0.2f, -0.235f), 0.31f, 0.38f, 70f, 40);
			d.Emit(flame, SceneEmitterKind.Lamp);
		}

		/// <summary>A stone lamp-bowl on <paramref name="at"/> with a short dim flame; its flame.</summary>
		private static Vector3 WlLampBowl(StructureDraft d, Vector3 at, int key)
		{
			WlBowl(d, at, 0.09f, 0.06f, StructureMaterial.Stone, key);
			Vector3 flame = at + new Vector3(0f, 0.05f, -0.03f);
			WlFlame(d, flame, 0.05f, key + 1);
			return flame + Vector3.up * 0.03f;
		}

		/// <summary>A stone lamp-bowl in a rubble niche in a wall.</summary>
		private static void WayChiragh(StructureDraft d)
		{
			Vector3 floor = WlNiche(d, 0.3f, 0.32f, 0.26f, 0.1f, StructureMaterial.Fieldstone, 1, d.Variant == 1 ? 2 : 0);
			Vector3 flame = WlLampBowl(d, floor, 20);
			WlSoot(d, new Vector3(0f, 0.26f, 0f), 0.18f, 0.24f, 40);
			d.Emit(flame, SceneEmitterKind.Lamp);
		}

		/// <summary>A stone lamp-bowl on a waist-high rubble pillar.</summary>
		private static void WayChiraghPillar(StructureDraft d)
		{
			const float top = 1.05f;
			if (d.Variant == 0)
			{
				WlStack(d, top, 0.42f, 5, StructureMaterial.Fieldstone, 1);
			}
			else
			{
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.26f, -0.1f), new Vector2(0.22f, top - 0.05f), new Vector2(0.25f, top) }, d.Seg(10, 6), StructureMaterial.Fieldstone, 1, false));
			}
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, top + 0.03f, 0f), new Vector3(0.4f, 0.06f, 0.4f), d.Bevel(0.015f), StructureMaterial.Stone, 10));
			d.Emit(WlLampBowl(d, new Vector3(0f, top + 0.06f, 0f), 20), SceneEmitterKind.Lamp);
		}

		/// <summary>One hollowed block of rock salt (lit through at night) with an oil wick in it, standing on <paramref name="at"/>; its flame.</summary>
		private static Vector3 WlSaltBlock(StructureDraft d, Vector3 at, int blocks, int key)
		{
			Vector3 top = at;
			for (int b = 0; b < blocks; b++)
			{
				float h = 0.22f - b * 0.04f;
				d.Add(StructureShapes.Block(top + Vector3.up * h * 0.5f, new Vector3(0.24f - b * 0.04f, h, 0.2f - b * 0.03f), Quaternion.Euler(0f, b * 12f, 0f), StructureMaterial.Glass, key + b, StructureRole.Structure, d.Bevel(0.02f)));
				top += Vector3.up * h;
			}
			WlFlame(d, top, 0.06f, key + 10);
			return top + Vector3.up * 0.03f;
		}

		/// <summary>An oil wick in a hollowed block of rock salt in a wall niche, the salt glowing.</summary>
		private static void WaySaltLamp(StructureDraft d)
		{
			int blocks = d.Variant == 1 ? 2 : 1;
			Vector3 floor = WlNiche(d, 0.34f, blocks == 1 ? 0.36f : 0.46f, 0.3f, 0.08f, StructureMaterial.Fieldstone, 1, 0);
			d.Emit(WlSaltBlock(d, floor, blocks, 20), SceneEmitterKind.Lamp);
		}

		/// <summary>An oil wick in a hollowed block of rock salt on a cairn post.</summary>
		private static void WaySaltLampCairn(StructureDraft d)
		{
			float top = d.Variant == 0 ? 1.1f : 0.8f;
			WlStack(d, top, d.Variant == 0 ? 0.45f : 0.6f, d.Variant == 0 ? 5 : 3, StructureMaterial.Fieldstone, 1, 0.2f);
			d.Emit(WlSaltBlock(d, new Vector3(0f, top, 0f), 1, 20), SceneEmitterKind.Lamp);
		}

		/// <summary>A carved soapstone fire bowl on a waist-high plastered pillar.</summary>
		private static void WaySoapstoneBowl(StructureDraft d)
		{
			const float top = 1.15f;
			if (d.Variant == 0)
			{
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.32f, -0.1f), new Vector2(0.27f, top - 0.1f), new Vector2(0.3f, top) }, d.Seg(10, 6), StructureMaterial.Plaster, 1, false));
				d.Add(StructureShapes.Cylinder(0.34f, -0.1f, 0.14f, d.Seg(10, 6), StructureMaterial.Fieldstone, 2));
			}
			else
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, top * 0.5f - 0.05f, 0f), new Vector3(0.5f, top + 0.1f, 0.5f), d.Bevel(0.02f), StructureMaterial.Plaster, 1));
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, top + 0.03f, 0f), new Vector3(0.6f, 0.06f, 0.6f), d.Bevel(0.015f), StructureMaterial.Fieldstone, 2));
			}
			float at = top + (d.Variant == 1 ? 0.06f : 0f);
			WlBowl(d, new Vector3(0f, at, 0f), 0.3f, 0.2f, StructureMaterial.Stone, 10);
			if (d.Mid)
			{
				d.Add(StructureShapes.Cylinder(0.302f, at + 0.1f, at + 0.13f, d.Seg(10, 6), StructureMaterial.Stone, 11, false, StructureRole.Detail));
			}
			WlCoals(d, new Vector3(0f, at + 0.17f, 0f), 0.24f, 0.1f, 12);
			d.Emit(new Vector3(0f, at + 0.24f, 0f), SceneEmitterKind.Fire);
		}

		/// <summary>A pine-knot fire on <paramref name="at"/>: a few knots heaped on their ash; the fire's place.</summary>
		private static Vector3 WlKnots(StructureDraft d, Vector3 at, float size, int key)
		{
			WlCoals(d, at + Vector3.up * size * 0.25f, size, size * 0.25f, key);
			for (int i = 0; i < 3; i++)
			{
				float a = i * 2.1f;
				var p = at + new Vector3(Mathf.Cos(a) * size * 0.4f, size * 0.25f, Mathf.Sin(a) * size * 0.4f);
				d.Add(StructureShapes.Block(p, new Vector3(size * 0.9f, size * 0.3f, size * 0.3f), Quaternion.Euler(0f, a * Mathf.Rad2Deg, 15f), StructureMaterial.Timber, key + 1 + i, StructureRole.Detail));
			}
			return at + Vector3.up * size * 0.5f;
		}

		/// <summary>A pine-knot fire in a clay-plastered niche built out from a wall, its flue over it.</summary>
		private static void WayKokeul(StructureDraft d)
		{
			Vector3 floor = WlNiche(d, 0.42f, 0.42f, 0.36f, 0.09f, StructureMaterial.Plaster, 1, d.Variant == 1 ? 2 : 0);
			// The flue: a short clay stack over the niche's head.
			float head = 0.21f + 0.09f + (d.Variant == 1 ? 0.16f : 0f);
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, head + 0.14f, -0.12f), new Vector3(0.18f, 0.28f, 0.18f), d.Bevel(0.02f), StructureMaterial.Plaster, 10));
			d.Add(StructureShapes.Box(new Vector3(0f, head + 0.281f, -0.12f), new Vector3(0.1f, 0.004f, 0.1f), StructureMaterial.Ash, 11, StructureRole.Detail));
			d.Emit(WlKnots(d, floor, 0.12f, 20), SceneEmitterKind.Fire);
		}

		/// <summary>A squat clay pillar with a pine-knot fire niche in its front and a flue on top.</summary>
		private static void WayKokeulPillar(StructureDraft d)
		{
			const float high = 1.1f;
			int seg = d.Seg(10, 6);
			if (d.Variant == 0)
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, high * 0.5f - 0.05f, 0.05f), new Vector3(0.7f, high + 0.1f, 0.6f), d.Bevel(0.03f), StructureMaterial.Plaster, 1));
			}
			else
			{
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.4f, -0.1f), new Vector2(0.36f, high - 0.1f), new Vector2(0.3f, high) }, seg, StructureMaterial.Plaster, 1, false).Transform(Matrix4x4.Translate(new Vector3(0f, 0f, 0.1f))));
			}
			// The niche's mouth framed in front of the pillar, its floor a ledge.
			const float sill = 0.45f;
			d.Add(StructureShapes.Box(new Vector3(0f, sill - 0.03f, -0.36f), new Vector3(0.46f, 0.06f, 0.3f), StructureMaterial.Plaster, 10));
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Box(new Vector3(s * 0.2f, sill + 0.2f, -0.36f), new Vector3(0.06f, 0.4f, 0.3f), StructureMaterial.Plaster, 11 + (s + 1) / 2));
			}
			d.Add(StructureShapes.Box(new Vector3(0f, sill + 0.43f, -0.36f), new Vector3(0.46f, 0.06f, 0.3f), StructureMaterial.Plaster, 13));
			d.Add(StructureShapes.Cylinder(0.08f, high, high + 0.25f, seg, StructureMaterial.Plaster, 14).Transform(Matrix4x4.Translate(new Vector3(0f, 0f, -0.05f))));
			d.Emit(WlKnots(d, new Vector3(0f, sill, -0.36f), 0.12f, 20), SceneEmitterKind.Fire);
		}

		/// <summary>A fire in a shallow painted clay tray on a wooden tripod.</summary>
		private static void WayTrayFire(StructureDraft d)
		{
			const float top = 0.95f;
			for (int i = 0; i < 3; i++)
			{
				float a = i * Mathf.PI * 2f / 3f;
				Vector3 foot = new Vector3(Mathf.Cos(a) * 0.48f, -0.05f, Mathf.Sin(a) * 0.48f);
				Vector3 head = d.Variant == 0 ? new Vector3(-Mathf.Cos(a) * 0.08f, top + 0.12f, -Mathf.Sin(a) * 0.08f) : new Vector3(Mathf.Cos(a) * 0.24f, top, Mathf.Sin(a) * 0.24f);
				d.Add(StructureShapes.Beam(foot, head, 0.05f, 0.05f, StructureMaterial.Timber, 1 + i));
			}
			if (d.Variant == 1)
			{
				d.Add(Ring(new Vector3(0f, top - 0.02f, 0f), 0.24f, 0.04f, Quaternion.identity, d.Seg(10, 6), StructureMaterial.Timber, 5, StructureRole.Structure));
			}
			else
			{
				d.Add(StructureShapes.Cylinder(0.06f, top + 0.02f, top + 0.12f, d.Seg(6, 4), StructureMaterial.Canvas, 5, false, StructureRole.Detail));
			}
			float tray = d.Variant == 0 ? top + 0.12f : top;
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.32f, 0.02f), new Vector2(0.38f, 0.08f), new Vector2(0.35f, 0.08f), new Vector2(0f, 0.05f) }, d.Seg(12, 6), StructureMaterial.Adobe, 10, true, 0f, false).Transform(Matrix4x4.Translate(new Vector3(0f, tray, 0f))));
			if (d.Mid)
			{
				d.Add(StructureShapes.Cylinder(0.375f, tray + 0.05f, tray + 0.075f, d.Seg(12, 6), StructureMaterial.Dyed, 11, false, StructureRole.Detail));
			}
			WlCoals(d, new Vector3(0f, tray + 0.12f, 0f), 0.24f, 0.08f, 12);
			d.Emit(new Vector3(0f, tray + 0.18f, 0f), SceneEmitterKind.Fire);
		}

		/// <summary>Candlenut kernels skewered on a midrib from <paramref name="foot"/> along <paramref name="up"/>; the burning top.</summary>
		private static Vector3 WlKernels(StructureDraft d, Vector3 foot, Vector3 up, int count, int key)
		{
			Vector3 top = foot + up * (0.06f * count + 0.06f);
			d.Add(StructureShapes.Beam(foot, top, 0.012f, 0.012f, StructureMaterial.Timber, key, StructureRole.Detail));
			for (int i = 0; i < count; i++)
			{
				d.Add(StructureShapes.Sphere(foot + up * (0.06f * i + 0.04f), new Vector3(0.03f, 0.028f, 0.03f), d.Seg(6, 4), 3, i == count - 1 ? StructureMaterial.Ash : StructureMaterial.Earth, key + 1 + i, StructureRole.Detail));
			}
			return top;
		}

		/// <summary>A candlenut torch: kernels skewered on a leaf midrib lashed to a pole.</summary>
		private static void WayTuituiTorch(StructureDraft d)
		{
			const float top = 1.9f;
			WlPole(d, top, 0.055f, StructureMaterial.Bamboo, 1);
			for (int i = 0; i < 2; i++)
			{
				d.Add(StructureShapes.Cylinder(0.062f, top - 0.2f - i * 0.12f, top - 0.16f - i * 0.12f, d.Seg(6, 4), StructureMaterial.Canvas, 2 + i, false, StructureRole.Detail));
			}
			Vector3 flame = WlKernels(d, new Vector3(0f, top - 0.25f, -0.06f), Vector3.up, d.Fine ? 8 : 5, 10);
			if (d.Variant == 1)
			{
				WlKernels(d, new Vector3(0.03f, top - 0.3f, -0.06f), new Vector3(0.45f, 0.9f, 0f).normalized, d.Fine ? 6 : 4, 30);
			}
			d.Emit(flame + Vector3.up * 0.06f, SceneEmitterKind.Torch);
		}

		/// <summary>A rolled birch-bark torch held in a split stake.</summary>
		private static void WayBarkTorch(StructureDraft d)
		{
			const float split = 1.3f;
			WlPost(d, split, 0.11f, StructureMaterial.Timber, 1);
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(new Vector3(0f, split - 0.02f, 0f), new Vector3(s * 0.05f, split + 0.22f, 0f), 0.04f, 0.08f, StructureMaterial.Timber, 2 + (s + 1) / 2));
			}
			Vector3 up = d.Variant == 1 ? new Vector3(0f, 0.9f, -0.42f).normalized : Vector3.up;
			Vector3 foot = new Vector3(0f, split - 0.05f, 0f), head = foot + up * 0.55f;
			d.Add(StructureShapes.Rod(foot, head, 0.035f, d.Seg(8, 5), StructureMaterial.Canvas, 10));
			d.Add(StructureShapes.Rod(head - up * 0.04f, head + up * 0.08f, 0.045f, d.Seg(8, 5), StructureMaterial.Ash, 11, StructureRole.Detail));
			d.Emit(head + up * 0.12f, SceneEmitterKind.Torch);
		}

		/// <summary>A cauldron-brazier on three hoof-shaped legs.</summary>
		private static void WayKazanFire(StructureDraft d)
		{
			bool deep = d.Variant == 1;
			const float foot = 0.32f;
			for (int i = 0; i < 3; i++)
			{
				float a = i * Mathf.PI * 2f / 3f;
				var leg = new Vector3(Mathf.Cos(a) * 0.26f, 0f, Mathf.Sin(a) * 0.26f);
				d.Add(StructureShapes.Beam(leg + Vector3.up * 0.06f, leg * 0.8f + Vector3.up * (foot + 0.08f), 0.05f, 0.05f, StructureMaterial.Iron, 1 + i));
				d.Add(StructureShapes.Block(leg + Vector3.up * 0.04f + leg.normalized * 0.03f, new Vector3(0.09f, 0.08f, 0.11f), Quaternion.Euler(0f, -a * Mathf.Rad2Deg, 0f), StructureMaterial.Iron, 4 + i, StructureRole.Detail));
			}
			var body = deep
				? new[] { new Vector2(0f, foot), new Vector2(0.2f, foot), new Vector2(0.3f, foot + 0.1f), new Vector2(0.32f, foot + 0.5f), new Vector2(0.28f, foot + 0.5f), new Vector2(0.26f, foot + 0.15f), new Vector2(0f, foot + 0.12f) }
				: new[] { new Vector2(0f, foot), new Vector2(0.18f, foot), new Vector2(0.36f, foot + 0.18f), new Vector2(0.33f, foot + 0.38f), new Vector2(0.29f, foot + 0.38f), new Vector2(0.3f, foot + 0.2f), new Vector2(0f, foot + 0.12f) };
			d.Add(StructureShapes.Lathe(body, d.Seg(12, 6), StructureMaterial.Iron, 10, true, 0f, false));
			float rim = deep ? foot + 0.5f : foot + 0.38f;
			if (!deep && d.Mid)
			{
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(Ring(new Vector3(s * 0.37f, rim - 0.06f, 0f), 0.05f, 0.015f, Quaternion.Euler(0f, 0f, 90f), d.Seg(8, 5), StructureMaterial.Iron, 11 + (s + 1) / 2, StructureRole.Detail));
				}
			}
			WlCoals(d, new Vector3(0f, rim - 0.06f, 0f), 0.24f, 0.12f, 20);
			d.Emit(new Vector3(0f, rim + 0.05f, 0f), SceneEmitterKind.Fire);
		}

		/// <summary>A clay cup of sulphur and oil burning low and blue (its flame the blue rune-light, the light a blue Pyre); the flame's place.</summary>
		private static Vector3 WlBrimCup(StructureDraft d, Vector3 at, int key)
		{
			WlBowl(d, at, 0.07f, 0.07f, StructureMaterial.Adobe, key);
			Vector3 flame = at + Vector3.up * 0.06f;
			d.Add(StructureShapes.Sphere(flame + Vector3.up * 0.02f, new Vector3(0.045f, 0.025f, 0.045f), d.Seg(6, 4), 3, StructureMaterial.Runelight, key + 1, StructureRole.Detail));
			return flame + Vector3.up * 0.04f;
		}

		/// <summary>A clay cup of sulphur and oil burning low and blue in a rammed-earth niche.</summary>
		private static void WayBrimLamp(StructureDraft d)
		{
			Vector3 floor = WlNiche(d, 0.3f, 0.3f, 0.26f, 0.1f, StructureMaterial.Earth, 1, d.Variant == 1 ? 2 : 0);
			d.Emit(WlBrimCup(d, floor, 20), SceneEmitterKind.Pyre);
		}

		/// <summary>A clay cup of sulphur and oil burning low and blue on a rammed-earth post.</summary>
		private static void WayBrimLampPost(StructureDraft d)
		{
			const float top = 1.3f;
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, top * 0.5f - 0.1f, 0f), new Vector3(0.36f, top + 0.2f, 0.36f), d.Bevel(0.03f), StructureMaterial.Earth, 1));
			if (d.Variant == 1)
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.2f, 0f), new Vector3(0.5f, 0.5f, 0.5f), d.Bevel(0.03f), StructureMaterial.Earth, 2));
			}
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, top + 0.03f, 0f), new Vector3(0.42f, 0.06f, 0.42f), d.Bevel(0.015f), StructureMaterial.Earth, 3));
			d.Emit(WlBrimCup(d, new Vector3(0f, top + 0.06f, 0f), 20), SceneEmitterKind.Pyre);
		}

		/// <summary>An iron fork-basket of resinous pine splinters on a driven stake.</summary>
		private static void WayPishaStake(StructureDraft d)
		{
			const float top = 1.45f;
			WlPost(d, top, 0.08f, StructureMaterial.Timber, 1);
			if (d.Variant == 0)
			{
				for (int i = 0; i < 4; i++)
				{
					float a = i * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
					d.Add(StructureShapes.Beam(new Vector3(0f, top - 0.05f, 0f), new Vector3(Mathf.Cos(a) * 0.12f, top + 0.25f, Mathf.Sin(a) * 0.12f), 0.016f, 0.016f, StructureMaterial.Iron, 2 + i, StructureRole.Detail));
				}
			}
			else
			{
				d.Add(Ring(new Vector3(0f, top + 0.2f, 0f), 0.12f, 0.016f, Quaternion.identity, d.Seg(8, 5), StructureMaterial.Iron, 2, StructureRole.Detail));
				d.Add(StructureShapes.Beam(new Vector3(0f, top - 0.05f, 0f), new Vector3(0f, top + 0.1f, 0f), 0.03f, 0.03f, StructureMaterial.Iron, 3));
				for (int i = 0; i < 3; i++)
				{
					float a = i * Mathf.PI * 2f / 3f;
					d.Add(StructureShapes.Beam(new Vector3(0f, top + 0.08f, 0f), new Vector3(Mathf.Cos(a) * 0.12f, top + 0.2f, Mathf.Sin(a) * 0.12f), 0.014f, 0.014f, StructureMaterial.Iron, 4 + i, StructureRole.Detail));
				}
			}
			int splints = d.Fine ? 5 : 3;
			for (int i = 0; i < splints; i++)
			{
				float a = i * 1.3f;
				d.Add(StructureShapes.Beam(new Vector3(Mathf.Cos(a) * 0.04f, top + 0.05f, Mathf.Sin(a) * 0.04f), new Vector3(Mathf.Cos(a) * 0.08f, top + 0.33f, Mathf.Sin(a) * 0.08f), 0.025f, 0.012f, StructureMaterial.Timber, 10 + i, StructureRole.Detail));
			}
			WlCoals(d, new Vector3(0f, top + 0.3f, 0f), 0.08f, 0.08f, 20);
			d.Emit(new Vector3(0f, top + 0.38f, 0f), SceneEmitterKind.Torch);
		}

		/// <summary>A three-wick tinned-copper oil lamp on a chain from a tripod of poles.</summary>
		private static void WayCopperKandil(StructureDraft d)
		{
			const float apex = 1.8f;
			for (int i = 0; i < 3; i++)
			{
				float a = i * Mathf.PI * 2f / 3f;
				d.Add(StructureShapes.Beam(new Vector3(Mathf.Cos(a) * 0.6f, -0.05f, Mathf.Sin(a) * 0.6f), new Vector3(-Mathf.Cos(a) * 0.05f, apex + 0.12f, -Mathf.Sin(a) * 0.05f), 0.05f, 0.05f, StructureMaterial.Timber, 1 + i));
			}
			d.Add(StructureShapes.Cylinder(0.08f, apex - 0.04f, apex + 0.06f, d.Seg(6, 4), StructureMaterial.Canvas, 4, false, StructureRole.Detail));
			Vector3 lamp = new Vector3(0f, apex - 0.7f, 0f);
			Chain(d, new Vector3(0f, apex - 0.04f, 0f), lamp + Vector3.up * 0.12f, 10);
			WlBowl(d, lamp, 0.11f, 0.06f, StructureMaterial.Iron, 30);
			if (d.Variant == 1)
			{
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.1f, 0.06f), new Vector2(0.06f, 0.11f), new Vector2(0f, 0.13f) }, d.Seg(8, 5), StructureMaterial.Iron, 31, true, 0f, false).Transform(Matrix4x4.Translate(lamp)));
			}
			Vector3 first = Vector3.zero;
			for (int i = 0; i < 3; i++)
			{
				float a = i * Mathf.PI * 2f / 3f - Mathf.PI * 0.5f;
				var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
				Vector3 tip = lamp + dir * 0.16f + Vector3.up * 0.05f;
				d.Add(StructureShapes.Beam(lamp + dir * 0.09f + Vector3.up * 0.04f, tip, 0.03f, 0.015f, StructureMaterial.Iron, 32 + i, StructureRole.Detail));
				WlFlame(d, tip, 0.05f, 40 + i);
				if (i == 0)
				{
					first = tip + Vector3.up * 0.04f;
				}
			}
			d.Emit(first, SceneEmitterKind.Lamp);
		}

		/// <summary>A tall tripod candelabrum, its oil cup on a knopped shaft (three on arms on v1).</summary>
		private static void WayCandelabrum(StructureDraft d)
		{
			const float top = 1.75f;
			int seg = d.Seg(8, 5);
			for (int i = 0; i < 3; i++)
			{
				float a = i * Mathf.PI * 2f / 3f;
				var foot = new Vector3(Mathf.Cos(a) * 0.3f, 0f, Mathf.Sin(a) * 0.3f);
				d.Add(StructureShapes.Beam(foot + Vector3.up * 0.03f, new Vector3(0f, 0.3f, 0f), 0.03f, 0.03f, StructureMaterial.Iron, 1 + i));
				d.Add(StructureShapes.Sphere(foot + Vector3.up * 0.03f, new Vector3(0.04f, 0.03f, 0.04f), d.Seg(6, 4), 3, StructureMaterial.Iron, 4 + i, StructureRole.Detail));
			}
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.04f, 0.28f), new Vector2(0.025f, 0.5f), new Vector2(0.05f, 0.56f), new Vector2(0.02f, 0.62f), new Vector2(0.02f, 1.1f), new Vector2(0.045f, 1.16f), new Vector2(0.018f, 1.22f), new Vector2(0.018f, top) },
				seg, StructureMaterial.Iron, 10, true, 0f, false, true));
			var cups = new List<Vector3> { new Vector3(0f, top, 0f) };
			if (d.Variant == 1)
			{
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Beam(new Vector3(0f, top - 0.25f, 0f), new Vector3(s * 0.25f, top - 0.12f, 0f), 0.02f, 0.02f, StructureMaterial.Iron, 11 + (s + 1) / 2, StructureRole.Detail));
					cups.Add(new Vector3(s * 0.25f, top - 0.12f, 0f));
				}
			}
			for (int i = 0; i < cups.Count; i++)
			{
				WlBowl(d, cups[i], 0.07f, 0.05f, StructureMaterial.Iron, 20 + i * 2);
				WlFlame(d, cups[i] + Vector3.up * 0.04f, 0.07f, 21 + i * 2);
			}
			d.Emit(new Vector3(0f, top + 0.1f, 0f), SceneEmitterKind.Torch);
		}

		/// <summary>A hollow nodule shell (its open face to −z) holding a glowing chip, at <paramref name="c"/>.</summary>
		private static void WlNodule(StructureDraft d, Vector3 c, int key)
		{
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0f, -0.25f), new Vector2(0.18f, -0.2f), new Vector2(0.25f, -0.02f), new Vector2(0.22f, 0.06f), new Vector2(0.15f, 0.04f), new Vector2(0.1f, -0.1f), new Vector2(0f, -0.13f) },
				d.Seg(10, 6), StructureMaterial.Fieldstone, key, true, 0f, false).Transform(Matrix4x4.TRS(c, Quaternion.Euler(-70f, 0f, 0f), Vector3.one)));
			d.Add(StructureShapes.Sphere(c + new Vector3(0f, 0f, -0.03f), new Vector3(0.07f, 0.06f, 0.05f), d.Seg(6, 4), 3, StructureMaterial.Runelight, key + 1, StructureRole.Detail));
		}

		/// <summary>A hollow nodule shell on a rib post (or between two), a glowing chip inside (cold Glow, green).</summary>
		private static void WayNoduleLamp(StructureDraft d)
		{
			int ribs = d.Variant == 1 ? 2 : 1;
			for (int r = 0; r < ribs; r++)
			{
				float x = ribs == 1 ? 0f : (r == 0 ? -0.28f : 0.28f);
				float lean = ribs == 1 ? 0f : -x * 0.5f;
				var rib = new[] { new Vector3(x, -0.35f, 0f), new Vector3(x + lean * 0.3f, 0.6f, 0.05f), new Vector3(x + lean * 0.7f, 1.2f, 0.04f), new Vector3(x + lean, 1.55f, -0.02f) };
				WlCrooked(d, rib, 0.1f, 0.06f, StructureMaterial.Bone, 1 + r * 5);
			}
			Vector3 c = new Vector3(0f, ribs == 1 ? 1.75f : 1.5f, -0.02f);
			if (ribs == 1)
			{
				d.Add(StructureShapes.Box(new Vector3(0f, 1.57f, 0f), new Vector3(0.14f, 0.06f, 0.14f), StructureMaterial.Bone, 12));
			}
			WlNodule(d, c, 20);
			d.Emit(c + new Vector3(0f, 0f, -0.2f), SceneEmitterKind.Glow);
		}

		/// <summary>A wrought-iron cage holding a glowing lure-orb on a curved rib (or a basalt post) (cold Glow, blue).</summary>
		private static void WayLurelight(StructureDraft d)
		{
			Vector3 orb;
			if (d.Variant == 0)
			{
				var rib = new[] { new Vector3(0f, -0.35f, 0.1f), new Vector3(0f, 0.8f, 0.12f), new Vector3(0f, 1.6f, -0.05f), new Vector3(0f, 1.95f, -0.35f), new Vector3(0f, 2.0f, -0.6f) };
				WlCrooked(d, rib, 0.12f, 0.06f, StructureMaterial.Bone, 1);
				d.Add(StructureShapes.Beam(new Vector3(0f, 2.0f, -0.6f), new Vector3(0f, 1.78f, -0.6f), 0.012f, 0.012f, StructureMaterial.Iron, 10, StructureRole.Detail));
				orb = new Vector3(0f, 1.6f, -0.6f);
			}
			else
			{
				WlStack(d, 1.5f, 0.36f, 3, StructureMaterial.Fieldstone, 1, 0.2f);
				orb = new Vector3(0f, 1.72f, 0f);
			}
			WlCage(d, orb, 0.14f, 0.34f, 6, 20);
			d.Add(StructureShapes.Sphere(orb, new Vector3(0.09f, 0.09f, 0.09f), d.Seg(8, 5), d.Fine ? 5 : 3, StructureMaterial.Runelight, 40, StructureRole.Detail));
			d.Emit(orb, SceneEmitterKind.Glow);
		}

		/// <summary>A perforated clay lantern on a pitch-daubed reed-bundle pillar (or three bundles bound together).</summary>
		private static void WayNaphthaLamp(StructureDraft d)
		{
			const float top = 1.35f;
			int bundles = d.Variant == 1 ? 3 : 1;
			int seg = d.Seg(8, 5);
			for (int b = 0; b < bundles; b++)
			{
				float a = b * Mathf.PI * 2f / 3f;
				var off = bundles == 1 ? Vector3.zero : new Vector3(Mathf.Cos(a) * 0.1f, 0f, Mathf.Sin(a) * 0.1f);
				d.Add(StructureShapes.Lathe(new[] { new Vector2(bundles == 1 ? 0.15f : 0.09f, -0.35f), new Vector2(bundles == 1 ? 0.13f : 0.08f, top) }, seg, StructureMaterial.Reed, 1 + b, true, 0f, false, true).Transform(Matrix4x4.Translate(off)));
			}
			float bandR = bundles == 1 ? 0.14f : 0.19f;
			for (int i = 0; i < 3; i++)
			{
				d.Add(StructureShapes.Cylinder(bandR, 0.3f + i * 0.4f, 0.36f + i * 0.4f, seg, StructureMaterial.Ash, 5 + i, false, StructureRole.Detail));
			}
			d.Add(StructureShapes.Cylinder(bandR + 0.01f, top, top + 0.04f, seg, StructureMaterial.Adobe, 9));
			d.Emit(WlJar(d, new Vector3(0f, top + 0.04f, 0f), false, 20), SceneEmitterKind.Torch);
		}

		/// <summary>A hearth-warden: a seated (or kneeling) stone figure bearing a fire bowl on its head.</summary>
		private static void WayHearthwarden(StructureDraft d)
		{
			bool kneeling = d.Variant == 1;
			float baseY = 0f;
			if (kneeling)
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.05f, 0f), new Vector3(0.7f, 0.3f, 0.7f), d.Bevel(0.03f), StructureMaterial.Monolith, 1));
				baseY = 0.2f;
			}
			// Folded legs and haunches, the body, the arms down to hands on the knees, the head, and the bowl on it.
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, baseY + 0.14f, -0.05f), new Vector3(0.56f, 0.28f, 0.55f), d.Bevel(0.04f), StructureMaterial.Monolith, 2));
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, baseY + 0.48f, 0.05f), new Vector3(0.44f, 0.42f, 0.32f), d.Bevel(0.05f), StructureMaterial.Monolith, 3));
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Beam(new Vector3(s * 0.24f, baseY + 0.64f, 0.05f), new Vector3(s * 0.2f, baseY + 0.3f, -0.2f), 0.11f, 0.11f, StructureMaterial.Monolith, 4 + (s + 1) / 2));
				d.Add(StructureShapes.ChamferBox(new Vector3(s * 0.18f, baseY + 0.3f, -0.24f), new Vector3(0.12f, 0.07f, 0.12f), d.Bevel(0.02f), StructureMaterial.Monolith, 6 + (s + 1) / 2, StructureRole.Detail));
			}
			Vector3 head = new Vector3(0f, baseY + 0.83f, 0.02f);
			d.Add(StructureShapes.ChamferBox(head, new Vector3(0.26f, 0.26f, 0.24f), d.Bevel(0.04f), StructureMaterial.Monolith, 8));
			if (d.Mid)
			{
				// The face: brow, nose, round staring eyes, a heavy mouth.
				d.Add(StructureShapes.Box(head + new Vector3(0f, 0.06f, -0.125f), new Vector3(0.22f, 0.04f, 0.03f), StructureMaterial.Monolith, 9, StructureRole.Detail));
				d.Add(StructureShapes.Box(head + new Vector3(0f, -0.01f, -0.13f), new Vector3(0.05f, 0.08f, 0.04f), StructureMaterial.Monolith, 10, StructureRole.Detail));
				d.Add(StructureShapes.Box(head + new Vector3(0f, -0.08f, -0.125f), new Vector3(0.12f, 0.03f, 0.03f), StructureMaterial.Monolith, 11, StructureRole.Detail));
				for (int s = -1; s <= 1; s += 2)
				{
					d.Add(StructureShapes.Cylinder(0.03f, -0.01f, 0.01f, 6, StructureMaterial.Monolith, 12 + (s + 1) / 2, false, StructureRole.Detail)
						.Transform(Matrix4x4.TRS(head + new Vector3(s * 0.06f, 0.02f, -0.12f), Quaternion.Euler(90f, 0f, 0f), Vector3.one)));
				}
			}
			float bowl = head.y + 0.13f;
			WlBowl(d, new Vector3(0f, bowl, 0.02f), 0.26f, 0.16f, StructureMaterial.Monolith, 20);
			WlCoals(d, new Vector3(0f, bowl + 0.13f, 0.02f), 0.2f, 0.08f, 21);
			d.Emit(new Vector3(0f, bowl + 0.2f, 0.02f), SceneEmitterKind.Fire);
		}

		/// <summary>Three carved hearthstones with faces under a clay griddle, a fire between them (on a low clay platform on v1).</summary>
		private static void WayTenamaztli(StructureDraft d)
		{
			float y0 = 0f;
			if (d.Variant == 1)
			{
				d.Add(StructureShapes.Cylinder(0.62f, -0.1f, 0.12f, d.Seg(10, 6), StructureMaterial.Adobe, 1));
				y0 = 0.12f;
			}
			for (int i = 0; i < 3; i++)
			{
				float a = i * Mathf.PI * 2f / 3f - Mathf.PI * 0.5f;
				var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
				Vector3 c = new Vector3(0f, y0 + 0.14f, 0f) + dir * 0.28f;
				Quaternion face = Quaternion.LookRotation(-dir, Vector3.up);
				d.Add(StructureShapes.Block(c, new Vector3(0.2f, 0.32f, 0.18f), face, StructureMaterial.Monolith, 2 + i * 4, StructureRole.Structure, d.Bevel(0.03f)));
				if (d.Mid)
				{
					// A face on its outward side: brow, nose.
					d.Add(StructureShapes.Block(c + dir * 0.095f + Vector3.up * 0.06f, new Vector3(0.14f, 0.03f, 0.02f), face, StructureMaterial.Monolith, 3 + i * 4, StructureRole.Detail));
					d.Add(StructureShapes.Block(c + dir * 0.095f + Vector3.up * 0.01f, new Vector3(0.04f, 0.06f, 0.03f), face, StructureMaterial.Monolith, 4 + i * 4, StructureRole.Detail));
				}
			}
			// The griddle laid across their tops, the fire under it.
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.36f, 0.015f), new Vector2(0.36f, 0.035f), new Vector2(0f, 0.025f) }, d.Seg(12, 6), StructureMaterial.Adobe, 20, true, 0f, false).Transform(Matrix4x4.Translate(new Vector3(0f, y0 + 0.3f, 0f))));
			WlCoals(d, new Vector3(0f, y0 + 0.08f, 0f), 0.18f, 0.08f, 21);
			d.Emit(new Vector3(0f, y0 + 0.16f, 0f), SceneEmitterKind.Fire);
		}

		/// <summary>Candlenut kernels strung on a midrib in a leaf sheath, set in a stone socket (a pair on v1); no metal.</summary>
		private static void WayKukuiTorch(StructureDraft d)
		{
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.12f, 0f), new Vector3(0.4f, 0.34f, 0.36f), d.Bevel(0.04f), StructureMaterial.Fieldstone, 1));
			int torches = d.Variant == 1 ? 2 : 1;
			Vector3 first = Vector3.zero;
			for (int t = 0; t < torches; t++)
			{
				float x = torches == 1 ? 0f : (t == 0 ? -0.09f : 0.09f);
				Vector3 up = new Vector3(x * 0.6f, 1f, 0f).normalized;
				Vector3 foot = new Vector3(x, 0.1f, 0f), sheath = foot + up * 1.2f;
				d.Add(StructureShapes.Rod(foot, sheath, 0.025f, d.Seg(6, 4), StructureMaterial.Bamboo, 2 + t * 10));
				// The ti-leaf sheath round the strung kernels, bound at its foot.
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.03f, 0f), new Vector2(0.05f, 0.1f), new Vector2(0.045f, 0.4f), new Vector2(0.03f, 0.46f) }, d.Seg(6, 4), StructureMaterial.Thatch, 3 + t * 10, true, 0f, false, true)
					.Transform(Matrix4x4.TRS(sheath, Quaternion.FromToRotation(Vector3.up, up), Vector3.one)));
				Vector3 top = WlKernels(d, sheath + up * 0.46f, up, d.Fine ? 4 : 3, 4 + t * 10);
				if (t == 0)
				{
					first = top + up * 0.06f;
				}
			}
			d.Emit(first, SceneEmitterKind.Torch);
		}

		/// <summary>A stone bowl lamp of nut oil on the ground (or on a flat stone).</summary>
		private static void WayPohoLamp(StructureDraft d)
		{
			float y = 0f;
			if (d.Variant == 1)
			{
				d.Add(StructureShapes.Block(new Vector3(0f, 0.04f, 0f), new Vector3(0.6f, 0.14f, 0.5f), Quaternion.Euler(0f, 12f, 0f), StructureMaterial.Fieldstone, 1, StructureRole.Structure, d.Bevel(0.03f)));
				y = 0.11f;
			}
			WlBowl(d, new Vector3(0f, y, 0f), 0.2f, 0.14f, StructureMaterial.Stone, 2);
			d.Add(StructureShapes.Cylinder(0.15f, y + 0.08f, y + 0.1f, d.Seg(10, 6), StructureMaterial.Ash, 3, false, StructureRole.Detail));
			Vector3 flame = new Vector3(0f, y + 0.12f, -0.1f);
			WlFlame(d, flame, 0.08f, 4);
			d.Emit(flame + Vector3.up * 0.05f, SceneEmitterKind.Lamp);
		}

		/// <summary>A bronze many-wick oil lamp (a lucerna: foot, stem, a disc of spouts, a ring handle) standing on <paramref name="at"/>; its first flame.</summary>
		private static Vector3 WlLucerna(StructureDraft d, Vector3 at, int wicks, int key)
		{
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.06f, 0f), new Vector2(0.05f, 0.02f), new Vector2(0.015f, 0.04f), new Vector2(0.015f, 0.2f), new Vector2(0.05f, 0.22f), new Vector2(0.06f, 0.25f), new Vector2(0f, 0.26f) },
				d.Seg(8, 5), StructureMaterial.Iron, key, true, 0f, false).Transform(Matrix4x4.Translate(at)));
			if (d.Mid)
			{
				d.Add(Ring(at + Vector3.up * 0.32f, 0.04f, 0.01f, Quaternion.Euler(90f, 0f, 0f), 6, StructureMaterial.Iron, key + 1, StructureRole.Detail));
				d.Add(StructureShapes.Beam(at + Vector3.up * 0.25f, at + Vector3.up * 0.29f, 0.01f, 0.01f, StructureMaterial.Iron, key + 2, StructureRole.Detail));
			}
			Vector3 first = at;
			for (int i = 0; i < wicks; i++)
			{
				float a = i * Mathf.PI * 2f / wicks + Mathf.PI * (wicks == 2 ? 0f : 0.25f);
				var dir = new Vector3(Mathf.Cos(a), 0f, Mathf.Sin(a));
				Vector3 tip = at + dir * 0.1f + Vector3.up * 0.245f;
				d.Add(StructureShapes.Beam(at + dir * 0.04f + Vector3.up * 0.24f, tip, 0.025f, 0.015f, StructureMaterial.Iron, key + 3 + i, StructureRole.Detail));
				WlFlame(d, tip, 0.05f, key + 10 + i);
				if (i == 0)
				{
					first = tip + Vector3.up * 0.04f;
				}
			}
			return first;
		}

		/// <summary>A bronze many-wick oil lamp in a limewashed wall niche.</summary>
		private static void WayLucerna(StructureDraft d)
		{
			Vector3 floor = WlNiche(d, 0.36f, 0.44f, 0.3f, 0.08f, StructureMaterial.Plaster, 1, d.Variant == 1 ? 2 : 0);
			Vector3 flame = WlLucerna(d, floor, 4, 20);
			WlSoot(d, new Vector3(0f, 0.3f, 0f), 0.24f, 0.24f, 40);
			d.Emit(flame, SceneEmitterKind.Lamp);
		}

		/// <summary>A bronze many-wick oil lamp on a shelf under a little gabled roof on a squared post.</summary>
		private static void WayLucernaPost(StructureDraft d)
		{
			const float shelf = 1.6f;
			WlPost(d, shelf + 0.55f, 0.15f, StructureMaterial.Timber, 1);
			d.Add(StructureShapes.Box(new Vector3(0f, shelf, -0.12f), new Vector3(0.36f, 0.04f, 0.4f), StructureMaterial.Timber, 2));
			d.Add(StructureShapes.Beam(new Vector3(0f, shelf - 0.3f, 0f), new Vector3(0f, shelf - 0.02f, -0.24f), 0.05f, 0.05f, StructureMaterial.Timber, 3));
			var gable = new[] { new Vector2(-0.26f, 0f), new Vector2(0.26f, 0f), new Vector2(0f, 0.2f) };
			d.Add(StructureShapes.Prism(gable, -0.36f, 0.12f, StructureMaterial.Timber, 4).Transform(Matrix4x4.Translate(new Vector3(0f, shelf + 0.5f, 0f))));
			Vector3 flame = WlLucerna(d, new Vector3(0f, shelf + 0.02f, -0.14f), d.Variant == 1 ? 2 : 4, 20);
			d.Emit(flame, SceneEmitterKind.Lamp);
		}

		/// <summary>One candle in a squat stone hood on a cut dead-oak post.</summary>
		private static void WayVigilLamp(StructureDraft d)
		{
			const float top = 1.3f;
			var post = new[] { new Vector3(0f, -0.35f, 0f), new Vector3(0.03f, 0.7f, 0.02f), new Vector3(-0.01f, top, 0f) };
			WlCrooked(d, post, 0.2f, 0.17f, StructureMaterial.Timber, 1);
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, top + 0.03f, 0f), new Vector3(0.36f, 0.06f, 0.34f), d.Bevel(0.015f), StructureMaterial.Stone, 5));
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Box(new Vector3(s * 0.14f, top + 0.2f, 0f), new Vector3(0.07f, 0.28f, 0.32f), StructureMaterial.Stone, 6 + (s + 1) / 2));
			}
			d.Add(StructureShapes.Box(new Vector3(0f, top + 0.2f, 0.13f), new Vector3(0.22f, 0.28f, 0.07f), StructureMaterial.Stone, 8));
			if (d.Variant == 0)
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, top + 0.38f, 0f), new Vector3(0.42f, 0.08f, 0.4f), d.Bevel(0.02f), StructureMaterial.Stone, 9));
			}
			else
			{
				var gable = new[] { new Vector2(-0.23f, 0f), new Vector2(0.23f, 0f), new Vector2(0f, 0.18f) };
				d.Add(StructureShapes.Prism(gable, -0.2f, 0.2f, StructureMaterial.Stone, 9).Transform(Matrix4x4.Translate(new Vector3(0f, top + 0.34f, 0f))));
			}
			d.Add(StructureShapes.Cylinder(0.025f, top + 0.06f, top + 0.18f, d.Seg(6, 4), StructureMaterial.Canvas, 10, false, StructureRole.Detail));
			WlFlame(d, new Vector3(0f, top + 0.18f, 0f), 0.05f, 11);
			d.Emit(new Vector3(0f, top + 0.22f, -0.05f), SceneEmitterKind.Lamp);
		}

		/// <summary>A great shell holding oil and a floating wick, on a stone stand (or a timber tripod).</summary>
		private static void WayShellwick(StructureDraft d)
		{
			float y;
			if (d.Variant == 0)
			{
				d.Add(StructureShapes.Lathe(new[] { new Vector2(0.22f, -0.1f), new Vector2(0.2f, 0.06f), new Vector2(0.08f, 0.12f), new Vector2(0.07f, 0.45f), new Vector2(0.14f, 0.5f) }, d.Seg(8, 5), StructureMaterial.Fieldstone, 1, false));
				y = 0.5f;
			}
			else
			{
				for (int i = 0; i < 3; i++)
				{
					float a = i * Mathf.PI * 2f / 3f;
					d.Add(StructureShapes.Beam(new Vector3(Mathf.Cos(a) * 0.3f, -0.05f, Mathf.Sin(a) * 0.3f), new Vector3(Mathf.Cos(a) * 0.08f, 0.75f, Mathf.Sin(a) * 0.08f), 0.04f, 0.04f, StructureMaterial.Timber, 1 + i));
				}
				y = 0.75f;
			}
			// The shell: a fluted dish, its ribs proud, one end drawn up into a hinge.
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0f, 0f), new Vector2(0.12f, 0.02f), new Vector2(0.24f, 0.1f), new Vector2(0.21f, 0.1f), new Vector2(0.1f, 0.05f), new Vector2(0f, 0.04f) }, d.Seg(12, 6), StructureMaterial.Bone, 10, true, 0f, false)
				.Transform(Matrix4x4.TRS(new Vector3(0f, y, 0f), Quaternion.identity, new Vector3(1f, 1f, 0.85f))));
			if (d.Mid)
			{
				for (int i = 0; i < 5; i++)
				{
					float a = (i - 2) * 0.45f - Mathf.PI * 0.5f;
					d.Add(StructureShapes.Beam(new Vector3(0f, y + 0.045f, 0.18f), new Vector3(Mathf.Cos(a) * 0.22f, y + 0.105f, 0.18f + Mathf.Sin(a) * 0.36f), 0.02f, 0.012f, StructureMaterial.Bone, 11 + i, StructureRole.Detail));
				}
			}
			Vector3 flame = new Vector3(0f, y + 0.07f, -0.02f);
			WlFlame(d, flame, 0.06f, 20);
			d.Emit(flame + Vector3.up * 0.04f, SceneEmitterKind.Lamp);
		}

		/// <summary>A wrought-iron pan holding a glowing ember-stone, on a chain from a stake's arm (cold Glow, amber).</summary>
		private static void WayGledpan(StructureDraft d)
		{
			const float top = 1.8f;
			if (d.Variant == 0)
			{
				d.Add(StructureShapes.Rod(new Vector3(0f, -0.35f, 0f), new Vector3(0f, top, 0f), 0.025f, d.Seg(6, 4), StructureMaterial.Iron, 1));
			}
			else
			{
				WlPost(d, top, 0.12f, StructureMaterial.Timber, 1);
			}
			Vector3 hook = WlArm(d, top - 0.05f, 0.45f, 0.035f, StructureMaterial.Iron, 2);
			Vector3 pan = hook + Vector3.down * 0.45f;
			Chain(d, hook, pan + Vector3.up * 0.1f, 10);
			for (int i = 0; i < 3; i++)
			{
				float a = i * Mathf.PI * 2f / 3f;
				d.Add(StructureShapes.Beam(pan + Vector3.up * 0.1f, pan + new Vector3(Mathf.Cos(a) * 0.13f, 0.04f, Mathf.Sin(a) * 0.13f), 0.008f, 0.008f, StructureMaterial.Iron, 40 + i, StructureRole.Detail));
			}
			WlBowl(d, pan, 0.15f, 0.05f, StructureMaterial.Iron, 50);
			d.Add(StructureShapes.Block(pan + Vector3.up * 0.05f, new Vector3(0.12f, 0.07f, 0.1f), Quaternion.Euler(10f, 30f, 8f), StructureMaterial.Faelight, 51, StructureRole.Detail, d.Bevel(0.02f)));
			d.Emit(pan + Vector3.up * 0.1f, SceneEmitterKind.Glow);
		}

		/// <summary>A pale wisp caged in a bog-iron basket on a crooked post (on its top, or hung from its crook) (cold Glow, faerie).</summary>
		private static void WayWispLantern(StructureDraft d)
		{
			Vector3 wisp;
			if (d.Variant == 0)
			{
				var post = new[] { new Vector3(0f, -0.35f, 0f), new Vector3(0.06f, 0.7f, 0.02f), new Vector3(-0.04f, 1.4f, -0.03f), new Vector3(0.02f, 1.7f, 0f) };
				WlCrooked(d, post, 0.14f, 0.1f, StructureMaterial.Timber, 1);
				wisp = new Vector3(0.02f, 1.9f, 0f);
			}
			else
			{
				var post = new[] { new Vector3(0f, -0.35f, 0f), new Vector3(0.05f, 1.0f, 0f), new Vector3(0f, 1.8f, -0.1f), new Vector3(0f, 2.0f, -0.35f), new Vector3(0f, 1.92f, -0.55f) };
				WlCrooked(d, post, 0.14f, 0.07f, StructureMaterial.Timber, 1);
				d.Add(StructureShapes.Beam(new Vector3(0f, 1.92f, -0.55f), new Vector3(0f, 1.72f, -0.55f), 0.012f, 0.012f, StructureMaterial.Iron, 8, StructureRole.Detail));
				wisp = new Vector3(0f, 1.55f, -0.55f);
			}
			WlCage(d, wisp, 0.13f, 0.32f, 5, 10);
			d.Add(StructureShapes.Sphere(wisp, new Vector3(0.06f, 0.08f, 0.06f), d.Seg(8, 5), d.Fine ? 4 : 3, StructureMaterial.Faelight, 40, StructureRole.Detail));
			d.Emit(wisp, SceneEmitterKind.Glow);
		}

		/// <summary>A stone pillar with a gabled niche: a painted panel and a candle in a tin lantern inside.</summary>
		private static void WayAlminha(StructureDraft d)
		{
			const float niche = 1.5f;
			if (d.Variant == 1)
			{
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.05f, 0f), new Vector3(0.7f, 0.3f, 0.7f), d.Bevel(0.02f), StructureMaterial.Stone, 1));
				d.Add(StructureShapes.ChamferBox(new Vector3(0f, 0.25f, 0f), new Vector3(0.52f, 0.12f, 0.52f), d.Bevel(0.02f), StructureMaterial.Stone, 2));
			}
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, niche * 0.5f - 0.1f, 0f), new Vector3(0.32f, niche + 0.2f, 0.32f), d.Bevel(0.02f), StructureMaterial.Stone, 3));
			d.Add(StructureShapes.ChamferBox(new Vector3(0f, niche + 0.03f, 0f), new Vector3(0.5f, 0.06f, 0.42f), d.Bevel(0.015f), StructureMaterial.Stone, 4));
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Box(new Vector3(s * 0.21f, niche + 0.26f, 0f), new Vector3(0.08f, 0.44f, 0.4f), StructureMaterial.Stone, 5 + (s + 1) / 2));
			}
			d.Add(StructureShapes.Box(new Vector3(0f, niche + 0.26f, 0.17f), new Vector3(0.36f, 0.44f, 0.06f), StructureMaterial.Stone, 7));
			d.Add(StructureShapes.Box(new Vector3(0f, niche + 0.3f, 0.135f), new Vector3(0.28f, 0.32f, 0.01f), StructureMaterial.Dyed, 8, StructureRole.Detail));
			var gable = new[] { new Vector2(-0.3f, 0f), new Vector2(0.3f, 0f), new Vector2(0f, 0.22f) };
			d.Add(StructureShapes.Prism(gable, -0.22f, 0.22f, StructureMaterial.Stone, 9).Transform(Matrix4x4.Translate(new Vector3(0f, niche + 0.48f, 0f))));
			if (d.Mid)
			{
				d.Add(StructureShapes.Box(new Vector3(0f, niche + 0.8f, 0f), new Vector3(0.03f, 0.16f, 0.03f), StructureMaterial.Stone, 10, StructureRole.Detail));
				d.Add(StructureShapes.Box(new Vector3(0f, niche + 0.83f, 0f), new Vector3(0.1f, 0.03f, 0.03f), StructureMaterial.Stone, 11, StructureRole.Detail));
			}
			Lantern(d, new Vector3(0f, niche + 0.06f, -0.04f), true, 100);
		}

		/// <summary>A candle in a pierced gourd hung from a crooked pole (a long gourd on a forked pole on v1).</summary>
		private static void WayGourdLantern(StructureDraft d)
		{
			var pole = new[] { new Vector3(0f, -0.35f, 0f), new Vector3(0.05f, 1.0f, 0f), new Vector3(0f, 1.8f, -0.12f), new Vector3(0f, 2.05f, -0.4f), new Vector3(0f, 2.0f, -0.6f) };
			WlCrooked(d, pole, 0.12f, 0.06f, StructureMaterial.Timber, 1);
			if (d.Variant == 1)
			{
				d.Add(StructureShapes.Beam(new Vector3(0f, 1.8f, -0.12f), new Vector3(0.2f, 2.15f, -0.05f), 0.05f, 0.05f, StructureMaterial.Timber, 8, StructureRole.Detail));
			}
			bool tall = d.Variant == 1;
			float h = tall ? 0.42f : 0.3f, r = tall ? 0.11f : 0.15f;
			Vector3 foot = new Vector3(0f, 2.0f - 0.18f - h, -0.6f);
			d.Add(StructureShapes.Beam(new Vector3(0f, 2.0f, -0.6f), foot + Vector3.up * h, 0.008f, 0.008f, StructureMaterial.Canvas, 9, StructureRole.Detail));
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0f, 0f), new Vector2(r * 0.8f, h * 0.08f), new Vector2(r, h * 0.35f), new Vector2(r * 0.7f, h * 0.7f), new Vector2(r * 0.35f, h * 0.85f), new Vector2(r * 0.2f, h), new Vector2(0f, h) },
				d.Seg(10, 6), StructureMaterial.Thatch, 10, true, 0f, false).Transform(Matrix4x4.Translate(foot)));
			WlPiercings(d, foot + Vector3.up * h * 0.35f, r * 0.98f, 0.035f, 7, 20);
			d.Emit(foot + Vector3.up * h * 0.35f, SceneEmitterKind.Lamp);
		}

		/// <summary>A crescent stone oil lamp on a waist-high cairn, its flame low and wide.</summary>
		private static void WayLampCairn(StructureDraft d)
		{
			const float top = 1.0f;
			WlStack(d, top, 0.55f, d.Variant == 0 ? 5 : 4, StructureMaterial.Fieldstone, 1, d.Variant == 0 ? 0.3f : 0f);
			if (d.Variant == 1)
			{
				d.Add(StructureShapes.Block(new Vector3(0f, top + 0.03f, 0f), new Vector3(0.6f, 0.06f, 0.45f), Quaternion.Euler(0f, 8f, 0f), StructureMaterial.Slate, 10, StructureRole.Structure, d.Bevel(0.01f)));
			}
			// The crescent lamp: a shallow trough bent round, its wick along the straight edge (toward −z).
			float y = top + (d.Variant == 1 ? 0.06f : 0f);
			int steps = d.Fine ? 5 : 3;
			Vector3 last = Vector3.zero;
			for (int i = 0; i <= steps; i++)
			{
				float a = Mathf.PI * i / steps;
				var p = new Vector3(Mathf.Cos(a) * 0.18f, y + 0.04f, Mathf.Sin(a) * 0.12f);
				if (i > 0)
				{
					d.Add(StructureShapes.Beam(last, p, 0.07f, 0.08f, StructureMaterial.Stone, 20 + i));
				}
				last = p;
			}
			d.Add(StructureShapes.Box(new Vector3(0f, y + 0.03f, 0.02f), new Vector3(0.34f, 0.06f, 0.08f), StructureMaterial.Stone, 30));
			d.Add(StructureShapes.Sphere(new Vector3(0f, y + 0.1f, -0.02f), new Vector3(0.14f, 0.03f, 0.025f), d.Seg(6, 4), 3, StructureMaterial.Lamplight, 31, StructureRole.Detail));
			d.Emit(new Vector3(0f, y + 0.14f, -0.03f), SceneEmitterKind.Torch);
		}

		/// <summary>A candle lantern in a small gabled shrine-box on a post (open-fronted, or with a pane door).</summary>
		private static void WayWaysideLantern(StructureDraft d)
		{
			const float box = 1.4f;
			WlPost(d, box, 0.13f, StructureMaterial.Timber, 1);
			d.Add(StructureShapes.Box(new Vector3(0f, box + 0.02f, 0f), new Vector3(0.42f, 0.04f, 0.36f), StructureMaterial.Timber, 2));
			for (int s = -1; s <= 1; s += 2)
			{
				d.Add(StructureShapes.Box(new Vector3(s * 0.19f, box + 0.24f, 0f), new Vector3(0.04f, 0.42f, 0.34f), StructureMaterial.Timber, 3 + (s + 1) / 2));
			}
			d.Add(StructureShapes.Box(new Vector3(0f, box + 0.24f, 0.16f), new Vector3(0.34f, 0.42f, 0.03f), StructureMaterial.Timber, 5));
			var gable = new[] { new Vector2(-0.28f, 0f), new Vector2(0.28f, 0f), new Vector2(0f, 0.22f) };
			d.Add(StructureShapes.Prism(gable, -0.24f, 0.22f, StructureMaterial.Shingle, 6).Transform(Matrix4x4.Translate(new Vector3(0f, box + 0.45f, 0f))));
			if (d.Variant == 1)
			{
				d.Add(StructureShapes.Box(new Vector3(0f, box + 0.24f, -0.17f), new Vector3(0.3f, 0.36f, 0.01f), StructureMaterial.Glass, 7, StructureRole.Detail));
				d.Add(StructureShapes.Box(new Vector3(0f, box + 0.24f, -0.172f), new Vector3(0.02f, 0.38f, 0.02f), StructureMaterial.Timber, 8, StructureRole.Detail));
			}
			Lantern(d, new Vector3(0f, box + 0.04f, 0f), false, 100);
		}

		/// <summary>A lantern of the dead: a tall hollow stone column, an open lantern chamber under a pyramid cap and a cross.</summary>
		private static void WayLanterneDesMorts(StructureDraft d)
		{
			bool octagonal = d.Variant == 1;
			int sides = octagonal ? 8 : 4;
			float phase = octagonal ? Mathf.PI / 8f : Mathf.PI * 0.25f;
			const float shaft = 3.6f;
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.75f, -0.1f), new Vector2(0.75f, 0.2f), new Vector2(0.62f, 0.2f), new Vector2(0.62f, 0.4f), new Vector2(0.5f, 0.4f) }, sides, StructureMaterial.Stone, 1, false, phase));
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.42f, 0.4f), new Vector2(0.36f, shaft), new Vector2(0.46f, shaft + 0.1f) }, sides, StructureMaterial.Stone, 2, false, phase));
			// The chamber: four columns round a lit core, the cap's eaves, its pyramid and the cross.
			float c0 = shaft + 0.1f, c1 = c0 + 0.6f;
			d.Add(StructureShapes.Cylinder(0.1f, c0, c1 - 0.15f, d.Seg(8, 5), StructureMaterial.Lamplight, 3, false, StructureRole.Detail));
			for (int i = 0; i < 4; i++)
			{
				float a = i * Mathf.PI * 0.5f + Mathf.PI * 0.25f;
				d.Add(StructureShapes.Cylinder(0.05f, c0, c1, d.Seg(6, 4), StructureMaterial.Stone, 4 + i).Transform(Matrix4x4.Translate(new Vector3(Mathf.Cos(a) * 0.3f, 0f, Mathf.Sin(a) * 0.3f))));
			}
			d.Add(StructureShapes.Lathe(new[] { new Vector2(0.5f, c1), new Vector2(0.5f, c1 + 0.08f), new Vector2(0.4f, c1 + 0.1f), new Vector2(0.04f, c1 + 0.75f), new Vector2(0f, c1 + 0.77f) }, sides, StructureMaterial.Stone, 10, false, phase));
			d.Add(StructureShapes.Box(new Vector3(0f, c1 + 0.9f, 0f), new Vector3(0.05f, 0.3f, 0.05f), StructureMaterial.Stone, 11));
			d.Add(StructureShapes.Box(new Vector3(0f, c1 + 0.95f, 0f), new Vector3(0.2f, 0.05f, 0.05f), StructureMaterial.Stone, 12));
			d.Emit(new Vector3(0f, c0 + 0.25f, 0f), SceneEmitterKind.Lamp);
		}

		/// <summary>Candles in a niche built out from a wall, a soot fan over it (two under an arched head on v1).</summary>
		private static void WayCandleNiche(StructureDraft d)
		{
			bool arched = d.Variant == 1;
			Vector3 floor = WlNiche(d, 0.34f, 0.3f, 0.24f, 0.08f, StructureMaterial.Plaster, 1, arched ? 2 : 0);
			int candles = arched ? 2 : 3;
			Vector3 first = Vector3.zero;
			for (int i = 0; i < candles; i++)
			{
				float x = (i - (candles - 1) * 0.5f) * 0.09f;
				float h = 0.1f + 0.04f * ((i * 7) % 3);
				var foot = floor + new Vector3(x, 0f, (i % 2) * 0.03f);
				d.Add(StructureShapes.Cylinder(0.02f, 0f, h, d.Seg(6, 4), StructureMaterial.Canvas, 10 + i, false, StructureRole.Detail).Transform(Matrix4x4.Translate(foot)));
				WlFlame(d, foot + Vector3.up * h, 0.045f, 20 + i);
				if (i == 0)
				{
					first = foot + Vector3.up * (h + 0.04f);
				}
			}
			if (d.Mid)
			{
				for (int i = 0; i < 3; i++)
				{
					WlSoot(d, new Vector3((i - 1) * 0.08f, 0.23f, 0f), 0.1f, 0.28f - 0.05f * Mathf.Abs(i - 1), 40 + i);
				}
			}
			d.Emit(first, SceneEmitterKind.Lamp);
		}
	}
}
#endif
