#ifndef FISHMMO_WATER_BREAKER_COMMON_INCLUDED
#define FISHMMO_WATER_BREAKER_COMMON_INCLUDED

// The breakers: the waves the open sea hands over to where it fades out.
//
// WHY A MODEL OF THEIR OWN
// The sea is an FFT height field, and a height field cannot curl: every attempt to make it break —
// a shoaling gain, a depth limit, a sine surf train, a forward shear thrown into a "barrel" — made
// nothing that broke and spoiled the waves it was applied to. So the sea fades to still water at the
// break depth (FishWaterCalm, from _FishWaterBreakDepth) and a breaker is drawn there on geometry of
// its own: a sheet laid along that same contour by WaterBreakers and shaped here, in a vertex shader,
// from the shore's clock.
//
// Everything that has to agree about a breaker reads this file — the sheet (FishWaterBreaker.shader),
// its spray and mist (FishWaterSpray.shader), the whitewater it leaves on the still water inshore (the
// ocean, FishWaterForwardPass.hlsl) and the foam that whitewater strands (FishWaterFoamMemory.shader) —
// so a lip that lands, the bore it becomes and the swash that bore runs up the beach are ONE wave.
//
// The includer declares _FishWaterWind before this, as every shore pass already does.

#include "FishWaterSurf.hlsl"

// ── The life of one breaker, in fractions of its wave period ─────────────
// Rise: from nothing, the swell stands up out of the still water at the break line.
#define FISH_BREAKER_RISE 0.16
// Curl: from here the face steepens, stands vertical and throws its lip.
#define FISH_BREAKER_CURL 0.10
// Land: the lip hits the water in front of the face and the tube collapses into whitewater.
#define FISH_BREAKER_LAND 0.34
// Gone: what collapsed has flattened into the bore running inshore; nothing is drawn until the next.
#define FISH_BREAKER_GONE 0.52

// Where the crest stands when the wave has risen, in heights shoreward of the break line.
#define FISH_BREAKER_BIRTH 1.4
// How far ahead of its crest a thrown lip lands, in heights: where its whitewater starts running in.
#define FISH_BREAKER_REACH 1.3

/// <summary>One breaker, at one point of the break line, now.</summary>
struct FishWaterBreakerState
{
	/// Where it is in its life, 0 to 1 — see the stages above.
	float t;
	/// Metres it stands at full height: this wave's, since waves come in sets.
	float height;
	/// Metres of the cross-section's horizontal scale: the height of the average wave here, squeezed
	/// when the water between the break line and the beach is too narrow for it. Independent of the
	/// set, so a big wave of a set breaks where every wave here breaks.
	float scaleX;
	/// How far the crest runs shoreward over a whole life, in units of scaleX.
	float travel;
	/// 1 where the breaker has all the water it wants; less where it is squeezed into less, down to 0.08.
	float squeeze;
	/// 0 a crest that only crumbles down its face, 1 a full plunging barrel.
	float curl;
	/// 0 spilling, 1 plunging: how hard the lip is thrown, which is what throws spray.
	float plunging;
	/// Seconds between waves.
	float period;
	/// Metres from the break line to the water's edge.
	float room;
	/// Metres from the break line to where the lip lands.
	float landing;
	/// Metres a second the whitewater runs inshore once the lip has landed.
	float boreSpeed;
	/// Periods the whitewater takes from where the lip lands to the water's edge — often more than one:
	/// across a wide surf zone the bores of several waves are running in at once.
	float arrival;
	/// Metres the wave's back runs out behind its crest, seaward: its foot may lie past the break line.
	float backReach;
};

/// <summary>A point of a breaker's cross-section.</summary>
struct FishWaterBreakerPoint
{
	/// Metres: x shoreward of the break line, y above the still water.
	float2 position;
	/// Along the branch, per unit of its parameter; unnormalised.
	float2 tangent;
	/// Metres shoreward of the break line at which textures are read, so the foam and the ripples run
	/// on across the seam into the sea at BOTH of the sheet's bases without a jump.
	float flatX;
	/// Whitewater here, 0 to 1.
	float foam;
	/// 0 at the sheet's edges and outside its life, 1 inside: how much of it is drawn.
	float edge;
};

/// <summary>
/// How much of the average wave one wave of a set is: two thirds to four thirds, fixed for the whole
/// wave. The same function the swash runs up the beach by, so a big breaker is followed by a big run-up.
/// </summary>
/// <param name="wave">Which wave: the whole part of the shore's phase.</param>
/// <param name="along">Where along the shore, in the shore's own phase (FishWaterAlongShore).</param>
/// <remarks>
/// <b>Continuous along the shore.</b> The share was drawn per stretch of shore, a hundred-odd metres
/// long, and changed at a stretch's end in one step — so a wave breaking across that line had a cliff
/// in its crest, one side a third taller than the other. It is now eased from one stretch's draw to the
/// next's across the whole stretch.
/// </remarks>
float FishWaterWaveShare(float wave, float along)
{
	float stretch = along * 0.5;
	float first = floor(stretch);
	float blend = smoothstep(0.0, 1.0, stretch - first);
	float here = frac(sin(wave * 12.9898 + first * 4.1414 + 78.233) * 43758.5453);
	float next = frac(sin(wave * 12.9898 + (first + 1.0) * 4.1414 + 78.233) * 43758.5453);
	return 0.65 + 0.7 * lerp(here, next, blend);
}

/// <summary>How fast the whitewater bore runs inshore, m/s: the shallow-water speed at the break depth.</summary>
// Metres the depth the break line follows has fallen since it was traced (WaterBreakers).
float _FishWaterBreakerShift;

/// Where the break line is NOW. It was traced at one depth; the tide and the sea have moved the depth it
/// follows since (_FishWaterBreakerShift, metres shallower), and the beach's own slope here — the break
/// depth over the room to the waterline — says how far along the way in that puts it. Followed
/// smoothly, so the line is traced again only every quarter metre of depth (it was every two
/// centimetres, and each new trace snapped every breaker along the coast at once).
void FishWaterBreakerFollow(inout float2 breakXZ, float2 shoreward, inout float room)
{
	float shift = clamp(_FishWaterBreakerShift * room / max(0.05, _FishWaterBreakDepth.x), -0.3 * room, 0.3 * room);
	breakXZ += shoreward * shift;
	room = max(0.25, room - shift);
}

float FishWaterBoreSpeed()
{
	return sqrt(max(0.05, _FishWaterGravity) * max(0.05, _FishWaterBreakDepth.x));
}

/// <summary>
/// The breaker at a point of the break line.
/// </summary>
/// <param name="breakXZ">The point, in world metres: on the contour where the water is the break depth deep.</param>
/// <param name="shoreward">Unit direction to the shore.</param>
/// <param name="room">Metres from here to the water's edge.</param>
/// <param name="confidence">How sure the shoreward direction is (FishWaterShoreFacing): 0 midway between two shores.</param>
/// <param name="heightScale">The material's breaker height, times the sea's.</param>
/// <param name="curlScale">The material's barrel.</param>
/// <param name="space">
/// Metres shoreward the breaker may reach without meeting another (WaterBreakers.Trace): where the
/// water stops getting shallower — the crest of a shoal, the far side of which another breaker is
/// coming the other way — or where the break line bends round a headland or an island tighter than
/// that. The room itself where nothing nearer stops it.
/// </param>
/// <remarks>
/// <para>
/// <b>The beach decides the breaker.</b> The Iribarren number — the beach's slope against the
/// wave's steepness — separates the breakers of a real surf zone: below about 0.5 a wave SPILLS, its
/// crest crumbling down its own face; between 0.5 and 3.3 it PLUNGES, throwing its lip out into a
/// barrel; past 3.3 it SURGES up the face unbroken. The slope is the MEAN from the break line to
/// the water's edge — the break depth over the room — so every pass that asks gets the same answer
/// from the same two numbers, with no slope to sample and none to disagree about.
/// </para>
/// <para>
/// <b>Timed from the beach back out.</b> The shore's clock says when the swash at the waterline
/// begins to rush up (FishWaterSurfCycles). The lip must land earlier by exactly the time its
/// whitewater takes to run in from where it lands, so the phase here is that clock, plus the landing
/// stage, plus that run.
/// </para>
/// </remarks>
FishWaterBreakerState FishWaterBreakerAt(float2 breakXZ, float2 shoreward, float room, float confidence,
	float heightScale, float curlScale, float space)
{
	FishWaterBreakerState b;
	b.room = max(0.25, room);
	b.period = max(0.5, _FishWaterSwashPeriod);
	b.boreSpeed = FishWaterBoreSpeed();

	float seaHeight = max(0.0, _FishWaterBreakDepth.z);
	float breakDepth = max(0.05, _FishWaterBreakDepth.x);
	float deepWavelength = max(4.0, _FishWaterSwashSea.y);

	float slope = breakDepth / b.room;
	float iribarren = slope / sqrt(max(1e-4, max(0.05, seaHeight) / deepWavelength));
	b.plunging = smoothstep(0.35, 0.9, iribarren) * (1.0 - smoothstep(2.5, 3.5, iribarren));
	float spilling = 1.0 - smoothstep(0.35, 0.9, iribarren);
	float surging = smoothstep(2.5, 3.5, iribarren);
	// A face steeper than about one in 1.6 is rock, not beach: the waves slap it and are thrown back.
	float cliff = smoothstep(0.6, 1.0, slope);
	b.curl = saturate(b.plunging + 0.3 * spilling) * curlScale;

	// A lee shore takes a share of the sea; a surging one never stands up to break.
	float2 waterline = breakXZ + shoreward * b.room;
	float2 along = FishWaterAlongShore(waterline);
	float average = seaHeight * FishWaterShoreExposure(shoreward, confidence) * (1.0 - cliff)
		* (1.0 - 0.5 * surging) * max(0.0, heightScale);

	/* The crest runs in at half the shallow-water speed while it breaks: slower than the bore it
	 * becomes, so the barrel stays in view, and it is the bore that then carries the wave to the beach.
	 * Everything horizontal is squeezed where there is not the water for it — a steep coast puts the
	 * break line a few metres off the rocks. */
	float safeAverage = max(0.02, average);
	b.travel = 0.5 * b.boreSpeed * b.period / safeAverage;
	/* Fitted to its WHOLE life, in average heights: the crest's run until the wave has flattened, and
	 * the face ahead of it — up to 1.8 of a wave's own height, and a wave of a set stands up to 1.6 of
	 * the average. Fitted to the moment the lip lands, as it was, a breaker still reached two thirds
	 * again past its space (measured on a port of this profile over 1440 seas); like this, three
	 * quarters of it at most. */
	float extent = FISH_BREAKER_BIRTH + b.travel * FISH_BREAKER_GONE + 3.0;
	/* Squeezed into the SPACE it has, not only the room. Every cross-section throws its water square to
	 * the break line; round a headland or an island those directions converge, and where the throw
	 * outran the bend the neighbouring cross-sections crossed and the sheet turned inside out — barrels
	 * forming outward. Over a shoal with no dry land near, the room was the way to a shore a hundred
	 * metres off, and the breakers from all round it were thrown into its middle and through each other. */
	float squeeze = clamp(0.85 * min(b.room, max(0.25, space)) / (safeAverage * extent), 0.02, 1.0);
	b.squeeze = squeeze;
	b.scaleX = safeAverage * squeeze;
	// A wave squeezed into a tight bend has no room to throw a barrel: it spills instead.
	b.curl *= smoothstep(0.15, 0.5, squeeze);
	b.landing = (FISH_BREAKER_BIRTH + b.travel * FISH_BREAKER_LAND + FISH_BREAKER_REACH) * b.scaleX;
	/* How far the wave's BACK runs out behind the crest, seaward, m. A breaking wave is a long swell with
	 * a steep front: its back falls away over about a third of the shallow-water wavelength (T·√(gh)) —
	 * ten metres behind a metre-high crest on an eight-second sea — at four to seven degrees. The back
	 * used to run only from the break line to the crest, 1.4 heights at the start, so the whole wave was
	 * a ridge two or three metres wide standing on flat water, 11–18 degrees at the back and, squeezed,
	 * over 30: a fin. Only as far seaward as the open sea is still nearly flat under it — the first
	 * 13.5 % of its fade, where the FFT is at 5 % of its height — so the foot never cuts through a moving
	 * sea: on a steep beach that is a few metres and the wave stays steep, as a steep beach's are. */
	float breakFull = max(breakDepth * 1.05, _FishWaterBreakDepth.y);
	float stillFlat = 0.135 * (breakFull - breakDepth) * b.room / breakDepth;
	b.backReach = min(0.35 * b.period * b.boreSpeed * squeeze, stillFlat);
	/* Not clamped. It was held to 0.6 of a period, so on a gentle beach — thirty metres of surf zone,
	 * a bore at three metres a second — each wave's whitewater died a quarter of the way in and the
	 * rest of the surf zone was dead-flat glass. A bore takes as many periods as it takes; the phase
	 * below still has each one reach the waterline as a swash starts, only an earlier wave's. */
	b.arrival = min(6.0, max(0.0, b.room - b.landing) / (b.boreSpeed * b.period));

	/* PEAKS. Waves square onto a straight beach stood up along their whole length at the same
	 * height — a close-out, which reads as a wall. Real surf stands up in peaks a few tens of metres
	 * apart, the shoulders lower; the same field delays the shoulders in the shore's phase
	 * (FishWaterAlongShore), so the curl peels outward from every peak, and the swash with it. */
	float peak = FishWaterSurfPeak(waterline);
	float cycles = _FishWaterSwashCycles + along.x + FISH_BREAKER_LAND + b.arrival;
	b.t = frac(cycles);
	/* And squeezed, it stands up less: a wave with no room to break in fades rather than becoming a
	 * thin sliver of water on edge. From three quarters of its room down, not two fifths: squeezed to
	 * 0.43 it kept its full height at under half its width, a fin 33 degrees at the back. */
	b.height = average * lerp(0.4, 1.2, peak) * FishWaterWaveShare(floor(cycles), along.x)
		* smoothstep(0.15, 0.75, squeeze);
	// None where the open sea's waves cannot reach at this tide: a lagoon's or a pool's shore has no surf.
	b.height *= FishWaterOpenSea(breakXZ);
	return b;
}

/// <summary>A cubic Bézier and its derivative.</summary>
float2 FishWaterBezier(float2 a, float2 b, float2 c, float2 d, float u, out float2 tangent)
{
	float v = 1.0 - u;
	tangent = 3.0 * (v * v * (b - a) + 2.0 * v * u * (c - b) + u * u * (d - c));
	return v * v * v * a + 3.0 * v * v * u * b + 3.0 * v * u * u * c + u * u * u * d;
}

/// <summary>
/// A point on a thrown lip, this far along it from the crest: a spiral whose curvature grows toward
/// its tip, turning through <paramref name="turn"/> radians over <paramref name="len"/> metres.
/// </summary>
/// <remarks>
/// Described by the angle it has turned through rather than by a formula for its position, because
/// that is what a lip is — a sheet of water that starts out level and bends ever faster as it falls —
/// and because a curl written that way stays a curl at any length and any angle, where a Bézier
/// folds over itself past half a turn. Integrated in ten steps.
/// </remarks>
float2 FishWaterLip(float2 crest, float len, float turn, float s)
{
	float2 p = crest;
	float ds = s / 10.0;
	float inverse = 1.0 / max(1e-4, len);
	[unroll]
	for (int i = 0; i < 10; i++)
	{
		float phi = -turn * pow((i + 0.5) * ds * inverse, 1.7);
		p += ds * float2(cos(phi), sin(phi));
	}
	return p;
}

/// <summary>
/// A point of the breaker's cross-section.
/// </summary>
/// <param name="branch">0 the back, from the break line up to the crest; 1 the lip, from the crest to its tip; 2 the face, from the crest down to the trough in front.</param>
/// <param name="p">0 to 1 along the branch.</param>
/// <param name="edgeMetres">How wide the sheet's edges fade, where it meets the sea.</param>
/// <remarks>
/// <para>
/// <b>Three sheets from one crest, because that is the shape of the water.</b> A plunging wave is a
/// gentle back rising to a crest, a lip thrown forward off that crest, and a face falling from the
/// crest to the trough under the lip; the barrel is the hollow between the lip and the face. One
/// sheet cannot be both a lip and a face, and anything that tried had either a hump that never curled
/// or a curl with nothing under it.
/// </para>
/// <para>
/// <b>Both bases are flat water on the still sea.</b> The back rises out of the sea level horizontally
/// at the break line — exactly where the sea has faded to nothing — and the face runs out horizontally
/// into the still water in front. Their textures are read at their true horizontal position, the
/// point the sea itself reads, so the foam and ripples carry across both seams.
/// </para>
/// <para>
/// The stages: RISE grows a smooth hump; from CURL the face swings from a front slope through
/// vertical to hollow while the lip lengthens and turns; at LAND the lip's tip has fallen to the water;
/// then everything shortens, lowers and whitens into the bore, and is flat by GONE.
/// </para>
/// </remarks>
FishWaterBreakerPoint FishWaterBreakerProfile(FishWaterBreakerState b, int branch, float p, float edgeMetres)
{
	FishWaterBreakerPoint o;
	o.position = 0.0;
	o.tangent = float2(1.0, 0.0);
	o.flatX = 0.0;
	o.foam = 0.0;
	o.edge = 0.0;
	float t = b.t;
	if (t >= FISH_BREAKER_GONE || b.height < 0.01)
	{
		return o;
	}

	float amount = saturate(b.curl);
	float rise = smoothstep(0.0, FISH_BREAKER_RISE, t);
	float progress = smoothstep(FISH_BREAKER_CURL, FISH_BREAKER_LAND, t);
	float collapse = smoothstep(FISH_BREAKER_LAND, FISH_BREAKER_LAND + 0.06, t);
	float settle = smoothstep(FISH_BREAKER_LAND + 0.03, FISH_BREAKER_GONE, t);
	// Whitewater once the lip is down, thinning as the wave flattens into the bore.
	float churned = collapse * (1.0 - 0.6 * settle);

	float heightMetres = rise * (1.0 - settle) * (1.0 + 0.12 * progress) * b.height;
	float crestMetres = (FISH_BREAKER_BIRTH + b.travel * t) * b.scaleX;
	float2 crest = float2(crestMetres, heightMetres);

	if (branch == 0)
	{
		// The back: level out of the still water, steepening under the crest only as a wave throws. Its
		// foot is seaward of the break line when the wave is long (b.backReach), and it fades in over the
		// first part of it, where it lies on the sea's own nearly flat water.
		float foot = min(0.0, crestMetres - b.backReach);
		float back = crestMetres - foot;
		float shoulder = lerp(0.4, lerp(0.36, 0.14, amount), progress) * back;
		float2 tangent;
		float2 q = FishWaterBezier(float2(foot, 0.0), float2(foot + 0.35 * back, 0.0),
			float2(crestMetres - shoulder, heightMetres), crest, p, tangent);
		o.position = q;
		o.tangent = tangent;
		o.flatX = q.x;
		o.foam = churned * smoothstep(0.45, 1.0, p);
		o.edge = smoothstep(foot, foot + edgeMetres - 0.3 * foot, q.x);
	}
	else if (branch == 1)
	{
		// The lip: nothing until the face steepens, then lengthening and turning as it is thrown.
		float len = heightMetres * 1.45 * progress * amount;
		float turn = progress * radians(lerp(40.0, 215.0, amount));
		len = lerp(len, 0.35 * heightMetres, collapse);
		turn = lerp(turn, 1.4, collapse);

		/* And falling. However it curls, by the moment it lands its tip is at the water: the drop the
		 * spiral alone leaves is taken up by gravity, most at the tip and none at the crest. */
		float tipHeight = FishWaterLip(crest, len, turn, len).y;
		float fall = smoothstep(0.5, 1.0, progress) * max(0.0, tipHeight);
		float s = p * len;
		float2 q = FishWaterLip(crest, len, turn, s);
		q.y -= fall * p * p;
		float phi = -turn * pow(p, 1.7);
		float2 tangent = float2(cos(phi), sin(phi)) * max(1e-4, len);
		tangent.y -= 2.0 * fall * p;
		// Its reach shoreward squeezed like the rest of it, or a squeezed wave throws past its space.
		q.x = crestMetres + (q.x - crestMetres) * b.squeeze;
		tangent.x *= b.squeeze;
		q.y = max(0.0, q.y);
		o.position = q;
		o.tangent = tangent;
		o.flatX = crestMetres + s;
		// White at the thrown edge; a spilling crest is white all over as it crumbles.
		float feather = smoothstep(0.45, 1.0, p) * progress * lerp(0.5, 1.0, amount);
		o.foam = max(feather, max(churned, (1.0 - amount) * progress * 0.8));
		o.edge = 1.0;
	}
	else
	{
		/* The face: a front slope that steepens — and only a plunging wave takes it through vertical
		 * to hollow under the lip. A SPILLING one never stands up: its front stays a slope with white
		 * water tumbling down it. Swinging every face to vertical turned the spilling waves of a
		 * gentle beach into a thin wall with a flat top, like a kerb laid along the water. */
		float bend = smoothstep(0.0, 0.85, progress);
		float plunge = amount * amount;
		float beta = lerp(0.0, lerp(-0.55, -1.95, plunge), bend);
		float ahead = lerp(1.8, lerp(1.15, -0.35, plunge), bend);
		beta = lerp(beta, -0.45, collapse);
		ahead = lerp(ahead, 1.3, collapse);
		// Horizontally squeezed like the rest of the breaker.
		float troughMetres = max(0.3 * crestMetres, crestMetres + heightMetres * ahead * b.squeeze);
		float reach = heightMetres * lerp(0.7, 0.5, progress);
		float runOut = heightMetres * lerp(0.7, 0.35, progress) * b.squeeze;
		float2 tangent;
		float2 q = FishWaterBezier(crest, crest + reach * float2(cos(beta) * b.squeeze, sin(beta)),
			float2(troughMetres - runOut, 0.0), float2(troughMetres, 0.0), p, tangent);
		q.y = max(0.0, q.y);
		o.position = q;
		o.tangent = tangent;
		/* Textures down the face by distance along it, not by where it stands: a face near vertical
		 * barely moves in x from crest to trough, so read by position its ripples and foam smeared
		 * into one flat colour — a slab. Back to the true position over the last quarter, where it
		 * runs out into the still water and has to read the sea's own points. */
		float faceMetres = heightMetres + abs(troughMetres - crestMetres);
		o.flatX = lerp(crestMetres + p * faceMetres, q.x, smoothstep(0.7, 1.0, p));
		// A spilling crest's white water tumbles down the upper face.
		o.foam = max((1.0 - amount) * progress * (1.0 - smoothstep(0.2, 0.8, p)), churned);
		o.edge = smoothstep(0.0, edgeMetres, (1.0 - p) * faceMetres);
	}

	// Fading in as it rises out of the sea and out as it flattens back into it.
	o.edge *= smoothstep(0.0, 0.05, t) * (1.0 - smoothstep(FISH_BREAKER_GONE - 0.06, FISH_BREAKER_GONE, t));
	return o;
}

/// <summary>
/// The whitewater the breakers leave on the still water between the break line and the beach:
/// x the white trail behind the running bores, y the narrow band at a bore's front (what the foam
/// memory keeps), z the slope of the water across a bore's front along the way to the shore — a bore is
/// a step in the water, not only foam on it. Zero offshore of the break line, on land, and with no shore
/// keeping time.
/// </summary>
/// <param name="xz">The point, world metres.</param>
/// <param name="depth">Metres of water here now, tide in.</param>
/// <param name="edge">Signed metres to the water's edge; positive at sea.</param>
/// <param name="shoreward">Unit direction to the nearest shore.</param>
/// <param name="confidence">How sure that direction is.</param>
/// <remarks>
/// <para>
/// <b>The breaker it came from, found from here.</b> On a beach of even slope the depth falls evenly
/// from the break depth to nothing, so how far in from the break line a point stands is its share of
/// the way in — and from that and the distance still to go, the room the breaker had. With the break
/// point, the room and the direction, this asks <see cref="FishWaterBreakerAt"/> exactly what the
/// sheet asked, and gets the same breaker: its clock, where its lip landed and how fast its bore runs.
/// </para>
/// <para>
/// <b>Several bores at once.</b> A bore runs from where the lip landed to the water's edge, reaching it
/// as the swash begins, and across a wide surf zone that takes more than one wave: the bores of the
/// last few waves are all running in together, a set of white lines one bore-length apart, each
/// thinning as it goes. Each trails white behind it, and each front is a ridge of water a fraction of
/// the breaker's height, which is what makes the lines read as water moving rather than paint.
/// </para>
/// </remarks>
float3 FishWaterBoreFoam(float2 xz, float depth, float edge, float2 shoreward, float confidence)
{
	float breakDepth = _FishWaterBreakDepth.x;
	if (!FishWaterSurfKeepsTime() || breakDepth <= 0.01 || depth >= breakDepth || depth <= 0.0
		|| edge <= 0.0 || dot(shoreward, shoreward) < 0.5)
	{
		return 0.0;
	}
	float share = 1.0 - depth / breakDepth;
	float room = min(edge / max(0.02, 1.0 - share), 600.0);
	/* Only where that reading can be true. It assumes the water shoals evenly from the break line to
	 * the nearest shore; over a shoal or up a shallow inlet it does not — the nearest dry land is off
	 * to one side, or a long way off — and the "breaker" it found was somewhere arbitrary, drawing
	 * lines of foam from waves that do not exist. A beach flatter than about one in eighty, and the
	 * ridge between two shores where the way in flips, are both that. */
	float plausible = (1.0 - smoothstep(55.0, 85.0, room / breakDepth)) * saturate(confidence * 1.5)
		* FishWaterOpenSea(xz);
	if (plausible <= 0.0)
	{
		return 0.0;
	}
	float inFromBreak = room - edge;
	FishWaterBreakerState b = FishWaterBreakerAt(xz - shoreward * inFromBreak, shoreward, room, confidence, 1.0, 1.0, room);
	if (b.height < 0.02)
	{
		return 0.0;
	}

	float spacing = b.period * b.boreSpeed;
	// Behind each front, white for about half the distance to the next one.
	float trailLength = 1.0 + 0.45 * min(spacing, b.room);
	/* A fifth of the breaker's height over about a metre: a bore's step is real, but drawn at 0.3 H
	 * over 0.7 m (up to 26 degrees on a 1.3 m sea) it caught the sky as a crisp dark-blue line
	 * painted along the front. */
	float ridgeHeight = 0.2 * b.height;
	const float RidgeWidth = 1.2;
	float power = saturate(b.height / 0.4) * lerp(0.55, 1.0, b.plunging) * plausible;
	// Periods since the most recent lip landed here; the k-th bore landed k periods before that.
	float latest = frac(b.t - FISH_BREAKER_LAND);
	float3 result = 0.0;
	[unroll]
	for (int k = 0; k < 4; k++)
	{
		float since = latest + k;
		if (since > b.arrival + 0.15)
		{
			break;
		}
		float front = b.landing + since * spacing;
		float behind = front - inFromBreak;
		/* No hard edges: a step in the whitewater is a line across the water no lace can hide. It
		 * thickens in over the water the breaker's own sheet covers, and its front rises over the last
		 * half metre. */
		float trail = exp(-max(0.0, behind) / trailLength) * smoothstep(-0.5, 0.0, behind)
			* smoothstep(0.25 * b.landing, 0.8 * b.landing, inFromBreak);
		float lead = exp(-(behind * behind) / 0.8);
		// Spent at the beach, where the swash takes over, and whitening less the further it has run.
		float strength = power * (1.0 - smoothstep(b.arrival, b.arrival + 0.15, since))
			* lerp(1.0, 0.45, saturate(since / max(0.5, b.arrival)));
		/* The ridge: a smooth step up to the bore's face, which is steep in front and trails away
		 * behind. Its slope along the way in is what tilts the surface. */
		float face = behind / RidgeWidth;
		float slope = ridgeHeight * strength * (-2.0 * face / RidgeWidth) * exp(-face * face)
			* (behind < 0.0 ? 1.0 : 0.35);
		result.x = max(result.x, trail * strength);
		result.y = max(result.y, lead * strength);
		result.z += slope;
	}
	return result;
}

#endif
