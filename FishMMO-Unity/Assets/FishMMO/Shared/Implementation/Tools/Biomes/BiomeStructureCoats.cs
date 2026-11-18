using System;
using System.Collections.Generic;
using System.Globalization;
using FishMMO.Shared.Celestial;
using UnityEngine;

namespace FishMMO.Shared.Biomes
{
	/// <summary>
	/// A COAT the weather of a biome lays over its buildings on the GPU (<see cref="BiomeWeathering.Coat"/>; decisions 25, 31, 33 and
	/// every finish a biome draft named from 2026-10-16 to 2026-10-25): lichen, rime, salt bloom, soot, a tide-mark… Drawn by
	/// FishStructureCoat.hlsl from a recipe (<see cref="BiomeStructureCoats"/>) on top of the structure weathering every building
	/// already wears; no prefab, no material, no texture of its own. Append-only (stored by value).
	/// </summary>
	/// <remarks>
	/// Not <see cref="BiomeStructureFinish"/>: a finish is a material the cut lays a piece in (moss, char, algae, mirrored by the
	/// structure kit); a coat is drawn where the piece stands, from the biome under it, so it needs no new piece and no re-cut.
	/// </remarks>
	public enum BiomeStructureCoat : byte
	{
		/// <summary>No coat: the building draws exactly as before.</summary>
		None = 0,
		/// <summary>Grey crustose lichen with orange rosettes on stone, wood bleached silver (Tundra, Hills, Alpine).</summary>
		Lichen,
		/// <summary>Rime feathers grown into the wind, snow lodged on sills (Alpine and the ice biomes).</summary>
		Rimed,
		/// <summary>Moss sleeves, usnea tufts and ferns on every sill (cloud forest, Mountain Slope).</summary>
		Epiphytic,
		/// <summary>Timber burnt chocolate-black on its sun faces, silver on its shade (Alpine Meadow).</summary>
		Sunburnt,
		/// <summary>Rain and algae streaks under sills and eaves on whitewash, a damp line at the plinth (Valley).</summary>
		Streaked,
		/// <summary>Wind-blown tawny dust drifted against the windward feet, sun-bleached timber (Plains).</summary>
		Dusted,
		/// <summary>White efflorescence up to a metre on brick, a green damp tide-mark above it (Farmland).</summary>
		Saltpetre,
		/// <summary>Pale silt caked to flood height with a sharp tide-mark, mud crazing below (Wetlands).</summary>
		Silted,
		/// <summary>Soft tuff rounded at the arrises, rill runnels under sills, a salt-white foot (Badlands).</summary>
		Ashworn,
		/// <summary>Raised silver grain, edges worn pale, sand at the windward foot (Beach).</summary>
		Sandblasted,
		/// <summary>Limewash surviving under eaves, black rain streaks, orange lichen on copings (Castle).</summary>
		Limeworn,
		/// <summary>A glossy black-brown ferromanganese patina thickest on tops and edges, foram dust (Abyssal Plain).</summary>
		Noduled,
		/// <summary>Manganese-black film, rust rusticles, white serpulid crust (Abyss).</summary>
		Encrusted,
		/// <summary>A thin white calcite film and drip streaks, a damp dark foot (Cave).</summary>
		Dripcrust,
		/// <summary>Boat tar, black-brown and glossy, low on the timber, a green-black waterline (River).</summary>
		Tarred,
		/// <summary>Pink coralline crust on every face, barnacles, red-weed fringe (Coastal Water).</summary>
		Coralline,
		/// <summary>Living coral heads on the tops, soft corals, white bite scars (Coral Reef).</summary>
		Reefgrown,
		/// <summary>White soda bloom rising up the feet in tide-marks, pink near the lake (Crater).</summary>
		Efflorescent,
		/// <summary>Sand banked to sill height on the windward side, black varnish streaks (Desert, Ice Sheet's snow).</summary>
		Drifted,
		/// <summary>Electrostatic dust fur on every face, thickest on edges, a dust collar at the foot (Dust Sea).</summary>
		Staticveil,
		/// <summary>The foot swallowed by a glassy cryolava skin, hoar on the shaded side (Cryovolcanic Plain).</summary>
		Engulfed,
		/// <summary>White salt rot on the bottom metre, a clay splash line, orange lichen on roofs (Estuary).</summary>
		Saltbloom,
		/// <summary>An ivy mantle climbing the walls, gypsum leach-drips below the joints (Forest Ruins).</summary>
		Reclaimed,
		/// <summary>White silica bloom up the lower courses, orange condensate streaks under eaves (Geyser Basin).</summary>
		Sintered,
		/// <summary>Smoke-black over openings and upper storeys, map lichen low down (Fortress).</summary>
		Sooted,
		/// <summary>A rock-flour film and meltwater tide-marks on the lower walls, rust bleed (Glacier).</summary>
		Floured,
		/// <summary>A clear drip-ice glaze, short icicles, white sublimation bloom (Ice Cave).</summary>
		Glazed,
		/// <summary>Plaster flaking to the coursing, red sand to sill height, orange lichen (High Desert).</summary>
		Spalled,
		/// <summary>Laterite fluted and streaked by rain, the foot splash-reddened, algae in the shade (Jungle).</summary>
		Rainwashed,
		/// <summary>Stucco sloughed in plates baring the core, black biofilm curtains (Jungle Ruins).</summary>
		Stuccofall,
		/// <summary>Sun-ablated ice: sun-cup pitting, windward rime beards, blue hollows (Ice Palace).</summary>
		Sublimed,
		/// <summary>Hoar feathers grown toward the jets, fallout on every ledge (Ice Geyser Field; the draft's "Plume-rimed").</summary>
		PlumeRimed,
		/// <summary>Black lapilli grit in the joints and on sills, lower courses scoured matte (Lava Tube).</summary>
		Cinderdrift,
		/// <summary>Micrometeorite gardening: matte faces, rounded arrises, pinprick pits, a regolith skirt (Impact Basin).</summary>
		Starworn,
		/// <summary>At the waterline goose-barnacle beards and sargassum, sun-silvered timber (Deep Ocean).</summary>
		Gooseneck,
		/// <summary>Frost-wedged joints filled with ice lenses; icicles and drift caps ONLY with air (Ice Ruin).</summary>
		Icejacked,
		/// <summary>A horizontal tide-mark against the scene's sea: crust below high water, a tannin band above (Mangrove).</summary>
		Tideline,
		/// <summary>Joints opened by roots, black crust and lichen rosettes, flaking sandstone (Jungle Temple).</summary>
		Rootheave,
		/// <summary>An amber hydrocarbon glaze on every sky-facing surface with drip runs (Methane Lake).</summary>
		Waxed,
		/// <summary>A honey-orange oxalate patina on sun faces, black crust in hollows, orange lichen (Marble Palace).</summary>
		Ochred,
		/// <summary>Windward faces scoured and glazed, gaps packed with wind slab, lee drifts (Ice Sheet).</summary>
		Windpacked,
		/// <summary>Black glass drips up the foot, spatter blobs on the tops, heat crazing (Molten Surface).</summary>
		Spatterglazed,
		/// <summary>Kershef melt: rounded tops, rain runnels, a salt-white foot, sand on the windward side (Oasis).</summary>
		Saltmelt,
		/// <summary>A clear glaze of frozen sea spray, leaning icicles, salt-bloom streaks; needs air (Ice Shelf).</summary>
		Sprayglazed,
		/// <summary>Sunken cold-water coral on the high edges, salmon polyps, a silt mantle (Ocean).</summary>
		Coldreef,
		/// <summary>The time-locked look: the years do NOT show; crisp, unfaded, hoar glitter on the lee (Permanent Ice).</summary>
		Pristine,
		/// <summary>Radiolysis: tops browned dark, frost redeposited on the undersides and shade (Radiation Plain).</summary>
		Sputtered,
		/// <summary>Thermal spall: onion-skin exfoliation on sun faces, crack nets, flakes at the foot (Regolith Plain).</summary>
		Sunsplit,
		/// <summary>Salt-block faces fluted by night dew into runnels, corners rounded like melting sugar (Salt Flat).</summary>
		Dewfluted,
		/// <summary>Dried-salt bloom on windward faces, orange and grey rosettes, black tar lichen at the foot (Rocky Coast).</summary>
		Saltscoured,
		/// <summary>Red-earth termite runways up walls and posts, fire-blackened bases (Savanna).</summary>
		Termitecast,
		/// <summary>Hairline faults stepping the courses, a regolith skirt (Rille).</summary>
		Riftstepped,
		/// <summary>Puddled caliche: wall tops melted to domes, basal coving, mud fans and runnel flutes (Scrubland).</summary>
		Rainmelt,
		/// <summary>Diurnal frost and sun: a crumbling foot, pale spall scars on dark faces, a salt line 0.4 m up, checked beams (Rocky Terrain).</summary>
		Sunfrost,
		/// <summary>Falling stones: uphill faces pocked and chipped, spalled corners, a skirt of stones heaped against the uphill wall (Scree).</summary>
		Stoneshot,
		/// <summary>Sessile growth on the faces turned into the current (the prevailing wind), manganese-black tops, bare lee faces (Seamount).</summary>
		Currentgrown,
		/// <summary>Soft snow pillowed on every roof, sill and ledge; no icicles (they hang only from hearth-warmed eaves, a per-piece flag no piece carries yet).</summary>
		Snowladen,
		/// <summary>Steppe storms: dust drifted against the lee walls, a sand-blast belt to a metre on the windward side, felt greyed (Steppe).</summary>
		Burandrift,
		/// <summary>Yellow sulphur bloom, white sal-ammoniac fur in the joints, iron blackened, acid pitting (needs air), a frost collar (Sulphur Flats).</summary>
		Fumed,
		/// <summary>Acid fog: sky and windward faces etched matte, honeycomb pits, timber bleached to straw, tarnish streaks under ledges (Sulphuric Cloud Deck).</summary>
		Acidbitten,
		/// <summary>A tea-brown flood band to a height hashed per piece (0.4–1.6 m), buff silt above its sharp edge, biofilm in the shade (Swamp Cave).</summary>
		Teastain,
		/// <summary>A dark olive algal band at the LOCAL water's surface, a lime-white crust just above it, damp above that (Swamp Ruins).</summary>
		Sinkline,
		/// <summary>Black sooty mould on windward daub and under eaves, moss beads on post tops, an ochre flood line where water stands (Swamp Temple).</summary>
		Mistmould,
		/// <summary>A hematite rind on sky and windward stone, heat-blued checks, fines banked on the lee foot, tops browned (Runaway Greenhouse Plain).</summary>
		Kilnbaked,
		/// <summary>Amber haze settled on tops and lee faces; windward faces sand-polished bright to 1.5 m and dark sand at their foot, both needing air (Tholin Plain).</summary>
		Hazefall,
		/// <summary>Each piece cut by one or two strike-slip planes, rust brine wicking up the cracks, hoar in the joints with air (Tidal Fracture).</summary>
		Sheared,
		/// <summary>Faces into the current scoured pale and smooth, a grey silt drape on every upward face, spat and sprigs, iron stained black (Underwater Canyon).</summary>
		Flowscoured,
		/// <summary>Black tephra drifted on ledges and lee faces, silver lichen scale (needs air), sulphur stain, thatch greyed (Volcanic).</summary>
		Cinderdusted,
		/// <summary>Thermal spall scars at the arrises, a red-orange oxidation flush low down, sulphur in the joints, tops smoke-browned (Volcanic Cave).</summary>
		Heatspall,
		/// <summary>Limewash flaking to grey lava, bright after the spring rewash day and greying through the shared clock's year (Volcanic Temple).</summary>
		Rewashed,
		/// <summary>Timber silver-grey and split, daub crazed, dust to sill height on windward walls, no moss (Wasteland).</summary>
		Sapless,
		/// <summary>Black-rust sulphide down-current (the lee of the prevailing way), rust streaks, brass glints (Subsurface Ocean Vent).</summary>
		Smokecrust,
	}

	/// <summary>The parts of a coat a recipe draws (<see cref="BiomeCoatRecipe.NeedsAir"/>: those that vanish without air).</summary>
	[Flags]
	public enum BiomeCoatParts : ushort
	{
		None = 0,
		/// <summary>The main colour on what faces the sky (tops, sills, copings).</summary>
		Skyward = 1 << 0,
		/// <summary>The main colour in the shade: north walls and undersides.</summary>
		Shade = 1 << 1,
		/// <summary>The main colour on the faces the prevailing wind strikes (or its lee).</summary>
		Windward = 1 << 2,
		/// <summary>The main colour on the sun's faces (the equator side).</summary>
		Sun = 1 << 3,
		/// <summary>The main colour on every face alike.</summary>
		Film = 1 << 4,
		/// <summary>The main colour on edges and arrises.</summary>
		Edge = 1 << 5,
		/// <summary>The second colour's runs down a wall.</summary>
		Streaks = 1 << 6,
		/// <summary>The second colour's crack lines and joints.</summary>
		Cracks = 1 << 7,
		/// <summary>The third colour's rosettes and dots.</summary>
		Speckle = 1 << 8,
		/// <summary>The foot band (and its mark).</summary>
		Foot = 1 << 9,
		/// <summary>The bleach (or the darkening) of the base colour.</summary>
		Bleach = 1 << 10,
		/// <summary>The windward polish (<see cref="BiomeCoatRecipe.Scour"/>): wind-blown sand that needs air to fly.</summary>
		Scour = 1 << 11,
		All = 0xfff,
	}

	/// <summary>
	/// What a coat draws (<see cref="BiomeStructureCoats.Recipe"/>): three colours (sRGB) and how far each lies where. Every weight is
	/// 0..1 unless noted; a recipe all zero draws nothing.
	/// </summary>
	public struct BiomeCoatRecipe
	{
		/// <summary>The main colour, laid by face (<see cref="Skyward"/> … <see cref="Edge"/>) in patches (<see cref="Patchiness"/>).</summary>
		public Color A;
		/// <summary>The second colour: streaks, cracks and the mark above the foot.</summary>
		public Color B;
		/// <summary>The third colour: speckles and rosettes.</summary>
		public Color C;
		/// <summary>The foot band's colour.</summary>
		public Color Foot;
		public float Skyward, Shade, Sun, Film, Edge;
		/// <summary>On the faces the prevailing wind strikes; negative: on its lee.</summary>
		public float Windward;
		public float Streaks, Cracks, Speckle;
		/// <summary>The foot band's height, metres over the ground (or over the sea's low water with <see cref="FromSea"/>).</summary>
		public float FootHeight;
		/// <summary>The foot band is measured from the scene sea's low water (a tide-mark), not from the ground.</summary>
		public bool FromSea;
		/// <summary>How much higher the foot band stands on the windward side (negative: on the lee), a share of its height.</summary>
		public float FootWind;
		/// <summary>The width, metres, of a band of the second colour just above the foot (a tide-mark); 0 none.</summary>
		public float Mark;
		/// <summary>0 an even coat … 1 in blobs.</summary>
		public float Patchiness;
		/// <summary>The coat's smoothness where it lies (0 matte … 1 glass); negative keeps the surface's own.</summary>
		public float Smoothness;
		/// <summary>Positive: the base colour bleached toward silver-grey (most on sun faces). Negative: the sky-facing faces darkened.</summary>
		public float Bleach;
		/// <summary>How much of the structure weathering (grime, splash, streaks, moss) is undone: 1 a building the years never touched.</summary>
		public float Keep;
		/// <summary>The size of the coat's pattern, metres (1 when 0).</summary>
		public float Scale;
		/// <summary>The main colour on the faces toward the slope above the piece (the ground map's slope round its origin).</summary>
		public float Uphill;
		/// <summary>How much higher the foot band stands on the uphill side, a share of its height (a skirt of fallen stones).</summary>
		public float UphillFoot;
		/// <summary>The parts that need air to form (icicles, drift, lichen): dropped on worlds thinner than Standard air.</summary>
		public BiomeCoatParts NeedsAir;
		/// <summary>The <see cref="Keep"/> taken at least on a world thinner than Standard air (a crisp airless version).</summary>
		public float AirlessKeep;
		/// <summary>
		/// The foot band is measured from the LOCAL water's surface (any water standing within 6 m of the piece, from the fine ground
		/// map round the camera: a lake, a river, a methane sea), not from the ground; a piece with no water by it has no band.
		/// I2g-e: Sinkline's algal band, Waxed's bathtub ring. Wins over <see cref="FromSea"/>.
		/// </summary>
		public bool FromWater;
		/// <summary>How much the foot band's height differs piece to piece (a hash of its origin): 0.6 → 0.4…1.6 × the height (Teastain).</summary>
		public float FootSpread;
		/// <summary>How far the main colour greys and thins through the shared clock's year since the spring renewal (local year phase 0.25): Rewashed.</summary>
		public float YearFade;
		/// <summary>The windward polish: faces the prevailing way strikes scoured bright and smooth, the weathering and the coat undone (Hazefall, Flowscoured).</summary>
		public float Scour;
		/// <summary>The polish's top, metres over the ground (a saltation belt); 0 the whole wall.</summary>
		public float ScourTop;
		/// <summary>
		/// Fault planes cut through each piece (one or two by a hash of its origin), drawn as a dark gap with a lit lip and the coat's own
		/// pattern stepped across them. Positive: vertical strike-slip planes (Sheared). Negative: planes dipping 60°, struck along the
		/// slope's contour where the ground map knows it, so the courses step down toward the hollow (Riftstepped).
		/// </summary>
		public float Faults;
		/// <summary>
		/// The depth under the scene's sea, metres, above which the growth by face gives way to a film of the second colour (Currentgrown:
		/// coralline crust only above 15 m); 0 the growth at every depth.
		/// </summary>
		public float GrowthBelow;

		/// <summary>True when the recipe draws anything.</summary>
		public bool Draws => Skyward > 0f || Shade > 0f || Sun > 0f || Film > 0f || Edge > 0f || Windward != 0f || Streaks > 0f || Cracks > 0f
			|| Speckle > 0f || FootHeight > 0f || Bleach != 0f || Keep > 0f || Uphill > 0f || Scour > 0f || Faults != 0f;
	}

	/// <summary>
	/// The coat recipes (one per <see cref="BiomeStructureCoat"/>, from each finish's draft) and the table row the shader reads
	/// (<see cref="Texels"/>, built by FloraPaletteMap, one row per biome on the scene's flora map). Pure.
	/// </summary>
	/// <remarks>
	/// <para>
	/// <b>The row</b>, <see cref="TexelsPerBiome"/> RGBA texels, linear colours (FishStructureCoat.hlsl reads them in this order):
	/// 0 A.rgb + strength · 1 B.rgb + cover · 2 C.rgb + speckle · 3 foot.rgb + foot height m · 4 skyward, shade, windward (signed), sun ·
	/// 5 film, edge, streaks, cracks · 6 patchiness, smoothness (&lt; 0 keep), bleach (signed), keep · 7 mark m, foot from (0 the ground,
	/// 1 the sea's low water, 2 the local water), scale m, foot wind (signed) · 8 uphill, uphill foot, foot spread, year fade ·
	/// 9 scour, scour top m, faults (signed), growth below m. (Texels 8.zw and 9 since I2g-e; a recipe that leaves them zero draws as
	/// before.)
	/// </para>
	/// <para>
	/// <b>Defaults:</b> a biome with no coat (or strength 0) has a row of zeros, which the shader skips; a scene in which no biome has a
	/// coat publishes the table off (<see cref="AnyCoat"/>), so every building draws exactly as before 2026-10-25.
	/// </para>
	/// <para>
	/// Not drawn yet (no data on the GPU): Plume-rimed's hoar grown toward the NEAREST jet (it grows into the prevailing wind), and
	/// every face turned toward a vent or a lava (Fumed's bloom, Heatspall's flush, Smokecrust's fleece on warm faces: the geothermal
	/// heat mask lives on the CPU; Heatspall's flush is a foot band instead), courses and blocks displaced (a coat colours; it moves no
	/// vertex: Sheared's and Riftstepped's offsets step the coat's own pattern across a drawn fault, not the masonry). Stoneshot's uphill
	/// side, Riftstepped's contour and the local water (<see cref="BiomeCoatRecipe.FromWater"/>) are read from the fine ground map round
	/// the camera (800 m): a piece farther off has none until the camera nears it.
	/// Snowladen's icicles under hearth-warmed eaves (no piece says it has a hearth: none drawn).
	/// </para>
	/// </remarks>
	public static class BiomeStructureCoats
	{
		/// <summary>RGBA texels a biome's row holds.</summary>
		public const int TexelsPerBiome = 10;

		private static readonly Dictionary<BiomeStructureCoat, BiomeCoatRecipe> recipes = Build();

		/// <summary>The recipe of a coat (all zero for None or an unknown value).</summary>
		public static BiomeCoatRecipe Recipe(BiomeStructureCoat coat) => recipes.TryGetValue(coat, out BiomeCoatRecipe r) ? r : default;

		/// <summary>True when the coat has a recipe (every member but None).</summary>
		public static bool Knows(BiomeStructureCoat coat) => recipes.ContainsKey(coat);

		/// <summary>"Plume-rimed", "plumerimed" → PlumeRimed: a draft's spelling (case, spaces and hyphens ignored); false for none.</summary>
		public static bool TryParse(string name, out BiomeStructureCoat coat)
		{
			coat = BiomeStructureCoat.None;
			if (string.IsNullOrWhiteSpace(name))
			{
				return false;
			}
			string key = name.Replace("-", string.Empty).Replace(" ", string.Empty).Trim();
			foreach (BiomeStructureCoat value in (BiomeStructureCoat[])Enum.GetValues(typeof(BiomeStructureCoat)))
			{
				if (value != BiomeStructureCoat.None && string.Equals(value.ToString(), key, StringComparison.OrdinalIgnoreCase))
				{
					coat = value;
					return true;
				}
			}
			return false;
		}

		/// <summary>True when any of these weatherings lays a coat that draws (the table is published on only then).</summary>
		public static bool AnyCoat(IEnumerable<BiomeWeathering> weatherings)
		{
			if (weatherings == null)
			{
				return false;
			}
			foreach (BiomeWeathering w in weatherings)
			{
				if (w.Coats)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// A biome's row of the coat table (<see cref="TexelsPerBiome"/> texels, layout in the remarks) for a weathering on a world of
		/// <paramref name="air"/>: the weathering's coat with its cover, strength and tint, the parts that need air dropped (and the
		/// crisp keep taken) on a world thinner than Standard. All zero when it lays no coat.
		/// </summary>
		public static Vector4[] Texels(in BiomeWeathering weathering, AtmosphereKind air)
		{
			var texels = new Vector4[TexelsPerBiome];
			if (!weathering.Coats)
			{
				return texels;
			}
			BiomeCoatRecipe r = Recipe(weathering.Coat);
			if (air < AtmosphereKind.Standard && r.NeedsAir != BiomeCoatParts.None)
			{
				r = WithoutAir(r);
			}
			if (!r.Draws)
			{
				return texels;
			}
			Color tint = weathering.CoatTint;
			Color a = tint.a > 0f ? Color.Lerp(r.A, new Color(tint.r, tint.g, tint.b, 1f), Mathf.Clamp01(tint.a)) : r.A;
			Vector3 la = Linear(a), lb = Linear(r.B), lc = Linear(r.C), lf = Linear(r.Foot);
			texels[0] = new Vector4(la.x, la.y, la.z, Mathf.Clamp01(weathering.CoatStrength));
			texels[1] = new Vector4(lb.x, lb.y, lb.z, Mathf.Clamp01(weathering.CoatCover));
			texels[2] = new Vector4(lc.x, lc.y, lc.z, r.Speckle);
			texels[3] = new Vector4(lf.x, lf.y, lf.z, Mathf.Max(0f, r.FootHeight));
			texels[4] = new Vector4(r.Skyward, r.Shade, r.Windward, r.Sun);
			texels[5] = new Vector4(r.Film, r.Edge, r.Streaks, r.Cracks);
			texels[6] = new Vector4(r.Patchiness, r.Smoothness, r.Bleach, r.Keep);
			texels[7] = new Vector4(Mathf.Max(0f, r.Mark), FootFrom(r), r.Scale > 0f ? r.Scale : 1f, r.FootWind);
			texels[8] = new Vector4(r.Uphill, r.UphillFoot, Mathf.Clamp(r.FootSpread, 0f, 0.95f), Mathf.Clamp01(r.YearFade));
			texels[9] = new Vector4(Mathf.Clamp01(r.Scour), Mathf.Max(0f, r.ScourTop), Mathf.Clamp(r.Faults, -1f, 1f), Mathf.Max(0f, r.GrowthBelow));
			return texels;
		}

		/// <summary>What a recipe's foot band is measured from, as the row carries it: 0 the ground, 1 the sea's low water, 2 the local water.</summary>
		public static float FootFrom(in BiomeCoatRecipe r) => r.FromWater ? 2f : r.FromSea ? 1f : 0f;

		/// <summary>The recipe with the parts that need air zeroed and the airless keep taken.</summary>
		public static BiomeCoatRecipe WithoutAir(BiomeCoatRecipe r)
		{
			BiomeCoatParts gone = r.NeedsAir;
			if ((gone & BiomeCoatParts.Skyward) != 0) r.Skyward = 0f;
			if ((gone & BiomeCoatParts.Shade) != 0) r.Shade = 0f;
			if ((gone & BiomeCoatParts.Windward) != 0) r.Windward = 0f;
			if ((gone & BiomeCoatParts.Sun) != 0) r.Sun = 0f;
			if ((gone & BiomeCoatParts.Film) != 0) r.Film = 0f;
			if ((gone & BiomeCoatParts.Edge) != 0) r.Edge = 0f;
			if ((gone & BiomeCoatParts.Streaks) != 0) r.Streaks = 0f;
			if ((gone & BiomeCoatParts.Cracks) != 0) r.Cracks = 0f;
			if ((gone & BiomeCoatParts.Speckle) != 0) r.Speckle = 0f;
			if ((gone & BiomeCoatParts.Foot) != 0) { r.FootHeight = 0f; r.Mark = 0f; }
			if ((gone & BiomeCoatParts.Bleach) != 0) r.Bleach = 0f;
			if ((gone & BiomeCoatParts.Scour) != 0) r.Scour = 0f;
			r.Keep = Mathf.Max(r.Keep, r.AirlessKeep);
			return r;
		}

		/// <summary>An sRGB colour in linear light (the IEC formula, so this stays pure: Color.linear is an engine call).</summary>
		public static Vector3 Linear(Color srgb) => new Vector3(ToLinear(srgb.r), ToLinear(srgb.g), ToLinear(srgb.b));

		private static float ToLinear(float c) => c <= 0.04045f ? c / 12.92f : (float)Math.Pow((c + 0.055) / 1.055, 2.4);

		/// <summary>"#rrggbb" → its colour (sRGB).</summary>
		public static Color Hex(string hex)
		{
			string h = hex.TrimStart('#');
			int v = int.Parse(h, NumberStyles.HexNumber, CultureInfo.InvariantCulture);
			return new Color(((v >> 16) & 0xff) / 255f, ((v >> 8) & 0xff) / 255f, (v & 0xff) / 255f, 1f);
		}

		private static BiomeCoatRecipe R(string a, string b = "#000000", string c = "#000000", string foot = "#000000",
			float sky = 0f, float shade = 0f, float wind = 0f, float sun = 0f, float film = 0f, float edge = 0f,
			float streaks = 0f, float cracks = 0f, float speckle = 0f, float footH = 0f, bool sea = false, float footWind = 0f, float mark = 0f,
			float patch = 0.4f, float smooth = -1f, float bleach = 0f, float keep = 0f, float scale = 1f,
			BiomeCoatParts air = BiomeCoatParts.None, float airlessKeep = 0f, float uphill = 0f, float uphillFoot = 0f,
			bool water = false, float footSpread = 0f, float yearFade = 0f, float scour = 0f, float scourTop = 0f, float faults = 0f,
			float growthBelow = 0f)
		{
			return new BiomeCoatRecipe
			{
				A = Hex(a), B = Hex(b), C = Hex(c), Foot = Hex(foot),
				Skyward = sky, Shade = shade, Windward = wind, Sun = sun, Film = film, Edge = edge,
				Streaks = streaks, Cracks = cracks, Speckle = speckle,
				FootHeight = footH, FromSea = sea, FootWind = footWind, Mark = mark,
				Patchiness = patch, Smoothness = smooth, Bleach = bleach, Keep = keep, Scale = scale,
				NeedsAir = air, AirlessKeep = airlessKeep, Uphill = uphill, UphillFoot = uphillFoot,
				FromWater = water, FootSpread = footSpread, YearFade = yearFade, Scour = scour, ScourTop = scourTop, Faults = faults,
				GrowthBelow = growthBelow,
			};
		}

		/// <summary>
		/// Each finish as its draft described it (the colours are the drafts' where they named one). Living and air-borne parts
		/// (lichen, moss, rime, wind-blown dust and spray) need air; a coat whose every part does is drawn only under Standard air or more.
		/// </summary>
		private static Dictionary<BiomeStructureCoat, BiomeCoatRecipe> Build()
		{
			const BiomeCoatParts All = BiomeCoatParts.All;
			return new Dictionary<BiomeStructureCoat, BiomeCoatRecipe>
			{
				[BiomeStructureCoat.Lichen] = R("#8e9184", c: "#d0781e", sky: 0.45f, shade: 0.2f, speckle: 0.7f, patch: 0.7f, smooth: 0.12f, bleach: 0.35f, air: All),
				[BiomeStructureCoat.Rimed] = R("#eef3f6", c: "#ffffff", sky: 0.5f, wind: 0.75f, edge: 0.3f, speckle: 0.3f, patch: 0.35f, smooth: 0.3f, air: All),
				[BiomeStructureCoat.Epiphytic] = R("#4a6a2a", b: "#2e3a20", c: "#b4c098", sky: 0.7f, shade: 0.45f, wind: 0.2f, streaks: 0.2f, speckle: 0.5f, patch: 0.6f, smooth: 0.05f, air: All),
				[BiomeStructureCoat.Sunburnt] = R("#3a2418", c: "#4e6a30", sun: 0.8f, speckle: 0.2f, patch: 0.25f, smooth: 0.08f, bleach: 0.3f),
				[BiomeStructureCoat.Streaked] = R("#24221e", b: "#343a2c", foot: "#4e5a3a", shade: 0.3f, streaks: 0.85f, footH: 0.45f, mark: 0.08f, patch: 0.4f),
				[BiomeStructureCoat.Dusted] = R("#b8946a", foot: "#b08a5a", wind: 0.4f, footH: 0.6f, footWind: 0.8f, patch: 0.5f, smooth: 0.05f, bleach: 0.45f, air: All),
				[BiomeStructureCoat.Saltpetre] = R("#7e7e74", b: "#4a5a3a", foot: "#ece8de", film: 0.15f, footH: 1f, mark: 0.15f, patch: 0.55f, smooth: 0.2f),
				[BiomeStructureCoat.Silted] = R("#b4b0a4", b: "#6e6a60", foot: "#b4b0a4", cracks: 0.35f, footH: 1.2f, mark: 0.05f, patch: 0.2f, smooth: 0.05f),
				[BiomeStructureCoat.Ashworn] = R("#c8b8a0", b: "#6a6056", c: "#ece8e0", foot: "#e6e2d8", edge: 0.5f, streaks: 0.5f, speckle: 0.2f, footH: 0.5f, patch: 0.45f),
				[BiomeStructureCoat.Sandblasted] = R("#c8c0b0", foot: "#d8c8a0", wind: 0.2f, edge: 0.6f, footH: 0.45f, footWind: 1f, patch: 0.3f, smooth: 0.05f, bleach: 0.6f,
					air: BiomeCoatParts.Windward | BiomeCoatParts.Foot),
				[BiomeStructureCoat.Limeworn] = R("#e8e6de", b: "#2a2a28", c: "#d07a20", shade: 0.55f, streaks: 0.6f, speckle: 0.35f, patch: 0.45f, bleach: 0.1f,
					air: BiomeCoatParts.Speckle),
				[BiomeStructureCoat.Noduled] = R("#2a1e16", c: "#d8ccb0", sky: 0.5f, film: 0.35f, edge: 0.5f, speckle: 0.25f, patch: 0.3f, smooth: 0.6f),
				[BiomeStructureCoat.Encrusted] = R("#1c1a18", b: "#8a4a22", c: "#e8e4da", sky: 0.3f, film: 0.5f, streaks: 0.5f, speckle: 0.45f, patch: 0.45f, smooth: 0.25f),
				[BiomeStructureCoat.Dripcrust] = R("#ece8de", b: "#f2eee6", foot: "#3a362e", sky: 0.3f, film: 0.25f, streaks: 0.7f, footH: 0.4f, patch: 0.4f, smooth: 0.35f),
				[BiomeStructureCoat.Tarred] = R("#2a1c12", b: "#1e2418", foot: "#2a1c12", streaks: 0.3f, footH: 1.5f, mark: 0.12f, patch: 0.25f, smooth: 0.55f),
				[BiomeStructureCoat.Coralline] = R("#c87890", b: "#8a2a30", c: "#e8e0d0", sky: 0.3f, film: 0.45f, streaks: 0.2f, speckle: 0.4f, patch: 0.5f, smooth: 0.1f),
				[BiomeStructureCoat.Reefgrown] = R("#c89a6a", b: "#f0ece4", c: "#e86a8a", sky: 0.55f, film: 0.3f, cracks: 0.25f, speckle: 0.5f, patch: 0.75f, smooth: 0.1f),
				[BiomeStructureCoat.Efflorescent] = R("#f0ece6", b: "#c8a8a4", c: "#f4f2ee", foot: "#f0ece6", speckle: 0.15f, footH: 0.7f, mark: 0.1f, patch: 0.5f),
				[BiomeStructureCoat.Drifted] = R("#e0d0b0", b: "#2a2018", foot: "#d8b880", wind: 0.3f, streaks: 0.35f, footH: 0.9f, footWind: 1f, patch: 0.3f, bleach: 0.4f,
					air: BiomeCoatParts.Windward | BiomeCoatParts.Foot),
				[BiomeStructureCoat.Staticveil] = R("#8a8478", foot: "#7a746a", film: 0.6f, edge: 0.8f, footH: 0.25f, patch: 0.15f, smooth: 0.02f),
				[BiomeStructureCoat.Engulfed] = R("#e8f0f4", b: "#8aa8b4", c: "#f0f6f8", foot: "#b8d0d8", shade: 0.4f, speckle: 0.2f, footH: 1f, mark: 0.05f, patch: 0.3f, smooth: 0.8f),
				[BiomeStructureCoat.Saltbloom] = R("#d4782a", b: "#8a8478", c: "#d4782a", foot: "#eeeae2", sky: 0.25f, speckle: 0.4f, footH: 1f, mark: 0.08f, patch: 0.6f, bleach: 0.45f,
					air: BiomeCoatParts.Skyward | BiomeCoatParts.Speckle),
				[BiomeStructureCoat.Reclaimed] = R("#2e4a22", b: "#e8e4dc", sky: 0.4f, shade: 0.5f, wind: 0.3f, film: 0.2f, streaks: 0.35f, patch: 0.8f, smooth: 0.08f, air: All),
				[BiomeStructureCoat.Sintered] = R("#f2f0ea", b: "#a8622e", foot: "#f2f0ea", streaks: 0.5f, footH: 0.8f, patch: 0.5f, bleach: 0.4f),
				[BiomeStructureCoat.Sooted] = R("#1a1816", b: "#141210", c: "#b8b84a", film: 0.25f, streaks: 0.5f, speckle: 0.3f, patch: 0.55f, air: BiomeCoatParts.Speckle),
				[BiomeStructureCoat.Floured] = R("#c8c8c0", b: "#7a5a3e", foot: "#b8b8b0", film: 0.2f, streaks: 0.25f, footH: 0.9f, mark: 0.05f, patch: 0.35f),
				[BiomeStructureCoat.Glazed] = R("#e4f0f6", b: "#f6fafc", c: "#ffffff", sky: 0.4f, film: 0.35f, streaks: 0.5f, speckle: 0.25f, patch: 0.25f, smooth: 0.85f),
				[BiomeStructureCoat.Spalled] = R("#b0805a", b: "#6a4a34", c: "#d07a20", foot: "#b8643a", film: 0.25f, cracks: 0.45f, speckle: 0.2f, footH: 0.7f, footWind: 0.6f, patch: 0.85f),
				[BiomeStructureCoat.Rainwashed] = R("#1e2a1e", b: "#2a2016", foot: "#a0482a", shade: 0.5f, streaks: 0.8f, footH: 0.5f, patch: 0.5f, smooth: 0.2f, air: All),
				[BiomeStructureCoat.Stuccofall] = R("#8a8070", b: "#141614", c: "#8a2a1e", film: 0.35f, streaks: 0.6f, speckle: 0.15f, patch: 0.9f, smooth: 0.05f),
				[BiomeStructureCoat.Sublimed] = R("#d8e6ee", b: "#6a9ac0", c: "#f4f8fa", wind: 0.4f, sun: 0.5f, edge: 0.4f, cracks: 0.25f, speckle: 0.35f, patch: 0.4f, smooth: 0.35f,
					air: BiomeCoatParts.Windward),
				[BiomeStructureCoat.PlumeRimed] = R("#eef4f8", c: "#ffffff", sky: 0.5f, wind: 0.7f, speckle: 0.4f, patch: 0.3f, smooth: 0.25f),
				[BiomeStructureCoat.Cinderdrift] = R("#1e1c1c", b: "#2a2626", c: "#3a2e24", sky: 0.55f, cracks: 0.4f, speckle: 0.2f, patch: 0.4f, smooth: 0.02f, bleach: 0.2f),
				[BiomeStructureCoat.Starworn] = R("#a8a49c", c: "#2a2826", foot: "#8a8478", edge: 0.5f, speckle: 0.3f, footH: 0.3f, patch: 0.3f, smooth: 0.02f, bleach: 0.5f),
				[BiomeStructureCoat.Gooseneck] = R("#e8e4dc", b: "#5a6a2a", c: "#e8e4dc", foot: "#6a5a40", speckle: 0.35f, footH: 1f, sea: true, mark: 0.3f, patch: 0.45f, bleach: 0.55f),
				[BiomeStructureCoat.Icejacked] = R("#e8f2f6", b: "#cfe6f0", c: "#f4f8fa", sky: 0.5f, edge: 0.4f, streaks: 0.4f, cracks: 0.7f, speckle: 0.2f, patch: 0.35f, smooth: 0.4f,
					air: BiomeCoatParts.Skyward | BiomeCoatParts.Streaks | BiomeCoatParts.Speckle, airlessKeep: 0.5f),
				[BiomeStructureCoat.Tideline] = R("#8a8070", b: "#3a2a1a", c: "#1e2020", foot: "#8a8070", speckle: 0.3f, footH: 1.2f, sea: true, mark: 0.4f, patch: 0.3f),
				[BiomeStructureCoat.Rootheave] = R("#1a1a16", b: "#1e1a14", c: "#e8dcc0", film: 0.25f, edge: 0.3f, streaks: 0.3f, cracks: 0.6f, speckle: 0.45f, patch: 0.6f,
					air: BiomeCoatParts.Speckle),
				// The bathtub ring (I2g-e): a darker amber band at the local lake's surface, a pale wax line above it.
				[BiomeStructureCoat.Waxed] = R("#b07a2a", b: "#8a5a1a", foot: "#5a3a12", sky: 0.85f, streaks: 0.55f, footH: 0.35f, mark: 0.08f, patch: 0.2f, smooth: 0.8f,
					water: true),
				[BiomeStructureCoat.Ochred] = R("#c8884a", b: "#1e1c1a", c: "#d8781e", sun: 0.6f, edge: 0.4f, streaks: 0.35f, speckle: 0.35f, patch: 0.5f, air: BiomeCoatParts.Speckle),
				[BiomeStructureCoat.Windpacked] = R("#f0f4f8", b: "#f4f8fa", foot: "#eef2f6", wind: -0.8f, cracks: 0.4f, footH: 1.2f, footWind: -1f, patch: 0.2f, smooth: 0.5f, air: All),
				[BiomeStructureCoat.Spatterglazed] = R("#141212", b: "#0e0c0c", c: "#1a1614", foot: "#141212", streaks: 0.4f, cracks: 0.35f, speckle: 0.45f, footH: 0.6f, patch: 0.35f, smooth: 0.8f),
				[BiomeStructureCoat.Saltmelt] = R("#c8b49a", b: "#8a7a64", foot: "#ece8e0", edge: 0.4f, streaks: 0.45f, footH: 0.6f, footWind: 0.6f, patch: 0.4f),
				[BiomeStructureCoat.Sprayglazed] = R("#dfeef4", b: "#f2f6f8", c: "#ffffff", wind: 0.7f, film: 0.2f, streaks: 0.5f, speckle: 0.3f, patch: 0.3f, smooth: 0.85f, air: All),
				[BiomeStructureCoat.Coldreef] = R("#e8e4dc", c: "#e8806a", sky: 0.45f, edge: 0.45f, speckle: 0.35f, patch: 0.55f, smooth: 0.1f),
				[BiomeStructureCoat.Pristine] = R("#eef4f8", b: "#dfeaf2", c: "#f4f8fc", shade: 0.2f, cracks: 0.35f, speckle: 0.2f, patch: 0.3f, keep: 1f),
				[BiomeStructureCoat.Sputtered] = R("#dfe8ee", c: "#6a4a6a", shade: 0.6f, speckle: 0.15f, patch: 0.3f, smooth: 0.2f, bleach: -0.55f),
				[BiomeStructureCoat.Sunsplit] = R("#c8c0b0", b: "#3a3630", foot: "#9a9488", sun: 0.55f, edge: 0.3f, cracks: 0.6f, footH: 0.2f, patch: 0.6f, smooth: 0.05f),
				[BiomeStructureCoat.Dewfluted] = R("#f4f2ee", b: "#c8c4bc", edge: 0.5f, streaks: 0.8f, patch: 0.2f, smooth: 0.45f, bleach: 0.3f),
				[BiomeStructureCoat.Saltscoured] = R("#f0ece6", b: "#e8e4dc", c: "#d07020", foot: "#1a1a18", wind: 0.5f, streaks: 0.3f, speckle: 0.5f, footH: 0.35f, patch: 0.5f, bleach: 0.4f,
					air: BiomeCoatParts.Speckle | BiomeCoatParts.Windward),
				[BiomeStructureCoat.Termitecast] = R("#9a5a3a", b: "#8a4a2a", c: "#d0782a", foot: "#3a2a22", film: 0.15f, streaks: 0.55f, cracks: 0.2f, speckle: 0.3f, footH: 0.5f, patch: 0.55f,
					air: BiomeCoatParts.Speckle),
				// The step (I2g-e): dipping faults struck along the rille wall's contour, the coat stepped down across them.
				[BiomeStructureCoat.Riftstepped] = R("#8a847a", b: "#1e1c1a", foot: "#8a847a", edge: 0.2f, cracks: 0.7f, footH: 0.3f, patch: 0.3f, smooth: 0.02f,
					faults: -0.8f),
				[BiomeStructureCoat.Rainmelt] = R("#ece4d4", b: "#6a5444", foot: "#8a6a4e", sky: 0.5f, edge: 0.3f, streaks: 0.55f, footH: 0.35f, patch: 0.4f),
				[BiomeStructureCoat.Sunfrost] = R("#8a8690", b: "#ece8e0", foot: "#a8a296", sun: 0.4f, edge: 0.45f, cracks: 0.3f, footH: 0.3f, mark: 0.06f, patch: 0.6f, smooth: 0.1f, bleach: 0.4f),
				[BiomeStructureCoat.Stoneshot] = R("#c8c4bc", c: "#5a5650", foot: "#7a766e", film: 0.12f, edge: 0.5f, cracks: 0.2f, speckle: 0.3f, footH: 0.45f,
					patch: 0.55f, smooth: 0.05f, b: "#4a4640", uphill: 0.75f, uphillFoot: 1f),
				// The depth term (I2g-e): above 15 m only the coralline crust (B, the pink).
				[BiomeStructureCoat.Currentgrown] = R("#e0a070", b: "#c86a7a", c: "#d8b030", wind: 0.8f, cracks: 0.15f, speckle: 0.45f, patch: 0.6f, smooth: 0.1f, bleach: -0.5f,
					growthBelow: 15f),
				[BiomeStructureCoat.Snowladen] = R("#f4f7fa", c: "#ffffff", foot: "#eef2f6", sky: 0.95f, edge: 0.2f, wind: 0.15f, speckle: 0.15f, footH: 0.35f, patch: 0.15f,
					smooth: 0.25f, air: BiomeCoatParts.All),
				// I2g-e: the coats of decisions 82–96. "No moss" in a draft is a partial keep (the structure weathering's moss and damp undone).
				[BiomeStructureCoat.Burandrift] = R("#b09a78", c: "#8a7050", foot: "#b8a07a", wind: -0.35f, speckle: 0.15f, footH: 0.7f, footWind: -1f, patch: 0.4f, smooth: 0.05f,
					bleach: 0.35f, keep: 0.3f, scour: 0.6f, scourTop: 1f, air: BiomeCoatParts.Windward | BiomeCoatParts.Foot | BiomeCoatParts.Scour),
				[BiomeStructureCoat.Fumed] = R("#d8c040", b: "#ece8dc", c: "#3a3428", foot: "#f0eee4", sky: 0.4f, film: 0.25f, cracks: 0.45f, speckle: 0.3f, footH: 0.3f, patch: 0.55f,
					smooth: 0.15f, bleach: -0.3f, keep: 0.3f, air: BiomeCoatParts.Cracks | BiomeCoatParts.Speckle | BiomeCoatParts.Bleach),
				[BiomeStructureCoat.Acidbitten] = R("#b8b4a8", b: "#8a6a2a", c: "#2a2622", sky: 0.45f, wind: 0.4f, film: 0.2f, streaks: 0.55f, speckle: 0.5f, patch: 0.35f, smooth: 0f,
					bleach: 0.5f, keep: 0.4f, scale: 0.6f),
				[BiomeStructureCoat.Teastain] = R("#1a1a14", b: "#b8a888", foot: "#6a4022", shade: 0.3f, footH: 1f, mark: 0.25f, patch: 0.6f, smooth: 0.15f, footSpread: 0.6f,
					air: BiomeCoatParts.Shade),
				[BiomeStructureCoat.Sinkline] = R("#3e4234", b: "#e4e2d6", foot: "#3a4220", shade: 0.25f, film: 0.1f, footH: 0.45f, mark: 0.12f, patch: 0.5f, smooth: 0.2f,
					water: true, footSpread: 0.33f),
				[BiomeStructureCoat.Mistmould] = R("#1e1e1a", b: "#141412", c: "#4a6a2a", foot: "#7a5a2e", shade: 0.25f, wind: 0.45f, streaks: 0.65f, speckle: 0.4f, footH: 0.4f,
					mark: 0.05f, patch: 0.55f, smooth: 0.08f, water: true, air: All & ~BiomeCoatParts.Foot),
				[BiomeStructureCoat.Kilnbaked] = R("#6a3424", b: "#3a4a60", c: "#1e1a18", foot: "#4a3a30", sky: 0.5f, wind: 0.45f, streaks: 0.2f, cracks: 0.6f, speckle: 0.35f,
					footH: 0.25f, footWind: -0.8f, patch: 0.4f, smooth: 0.1f, bleach: -0.2f, keep: 0.4f),
				[BiomeStructureCoat.Hazefall] = R("#5a3a1e", c: "#7a4420", foot: "#2a1c12", sky: 0.55f, shade: 0.2f, wind: -0.6f, speckle: 0.35f, footH: 0.4f, footWind: 1f,
					patch: 0.3f, smooth: 0.05f, keep: 0.4f, scour: 0.8f, scourTop: 1.5f, air: BiomeCoatParts.Scour | BiomeCoatParts.Foot),
				[BiomeStructureCoat.Sheared] = R("#dfe8ee", b: "#8a4a22", c: "#f0f4f6", shade: 0.15f, streaks: 0.25f, cracks: 0.45f, speckle: 0.25f, patch: 0.3f, smooth: 0.3f,
					keep: 0.3f, faults: 0.9f, air: BiomeCoatParts.Speckle),
				[BiomeStructureCoat.Flowscoured] = R("#8a8a80", b: "#1e1c1a", c: "#d89878", sky: 0.6f, streaks: 0.3f, speckle: 0.4f, patch: 0.45f, smooth: 0.05f, bleach: 0.3f,
					scour: 0.6f),
				[BiomeStructureCoat.Cinderdusted] = R("#1c1a1a", b: "#c8a830", c: "#b8bcb4", sky: 0.6f, wind: -0.45f, streaks: 0.2f, speckle: 0.4f, patch: 0.45f, smooth: 0.02f,
					bleach: 0.3f, keep: 0.3f, air: BiomeCoatParts.Speckle),
				[BiomeStructureCoat.Heatspall] = R("#b8aa98", b: "#c8b030", c: "#c8b8a8", foot: "#a8482a", edge: 0.55f, cracks: 0.4f, speckle: 0.3f, footH: 0.9f, patch: 0.5f,
					smooth: 0.05f, bleach: -0.4f, keep: 0.3f),
				[BiomeStructureCoat.Rewashed] = R("#f0ece2", b: "#8a3a22", c: "#1e1c1a", film: 0.55f, streaks: 0.35f, speckle: 0.15f, patch: 0.75f, smooth: 0.1f, yearFade: 0.8f),
				[BiomeStructureCoat.Sapless] = R("#a89878", b: "#2a2622", c: "#4a2a1a", foot: "#b0a080", wind: 0.25f, cracks: 0.5f, speckle: 0.15f, footH: 1f, footWind: 1f,
					patch: 0.4f, smooth: 0.03f, bleach: 0.6f, keep: 0.5f, air: BiomeCoatParts.Windward | BiomeCoatParts.Foot),
				[BiomeStructureCoat.Smokecrust] = R("#2a1e18", b: "#6a3a1e", c: "#c8a040", wind: -0.7f, film: 0.15f, streaks: 0.4f, speckle: 0.3f, patch: 0.5f, smooth: 0.35f,
					keep: 0.4f),
			};
		}
	}
}
