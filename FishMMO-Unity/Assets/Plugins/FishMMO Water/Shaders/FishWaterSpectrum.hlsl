#ifndef FISHMMO_WATER_SPECTRUM_INCLUDED
#define FISHMMO_WATER_SPECTRUM_INCLUDED

// The ocean spectrum's arithmetic, shared by the two ways it is transformed: the compute kernels in
// FishWaterFFT.compute, and the render passes in FishWaterFFT.shader for machines with no compute
// (WebGL2, GLES3). WaterSpectrum.cs is a third, CPU copy of InitialSpectrum and TimeSpectrum for
// height queries — a change here has to be made there too.

#define SIZE 256
#define LOG_SIZE 8
#define PI 3.14159265358979

// ── Parameters ────────────────────────────────────────────────────────
float _PatchMetres;      // world size of this cascade's tile
float _WindSpeed;        // metres per second
float2 _WindDirection;   // unit
float _Gravity;          // m/s², from the celestial body
float _Amplitude;        // Phillips scale
float _SmallWave;        // shortest wavelength kept, in metres
float _Directionality;   // how hard waves align to the wind
float _MinWaveNumber;    // this cascade owns k from here …
float _MaxWaveNumber;    // … to here
float _FFTTime;          // seconds
float _LoopPeriod;       // seconds; quantises the dispersion so the animation repeats exactly
float _Choppiness;       // horizontal displacement scale

// ── Helpers ───────────────────────────────────────────────────────────

float2 ComplexMul(float2 a, float2 b)
{
	return float2(a.x * b.x - a.y * b.y, a.x * b.y + a.y * b.x);
}

/// <summary>Two independent complex numbers packed in one float4, times one twiddle.</summary>
float4 ComplexMulPair(float4 v, float2 w)
{
	return float4(ComplexMul(v.xy, w), ComplexMul(v.zw, w));
}

uint Hash(uint x)
{
	x ^= x >> 16; x *= 0x7feb352du;
	x ^= x >> 15; x *= 0x846ca68bu;
	x ^= x >> 16;
	return x;
}

float Random(uint2 id, uint salt)
{
	uint h = Hash(id.x + Hash(id.y * 57u + salt * 131u));
	return (h & 0x00FFFFFFu) / 16777216.0;
}

/// <summary>Two independent standard normals, Box-Muller.</summary>
float2 Gaussian(uint2 id, uint salt)
{
	float u1 = max(1e-6, Random(id, salt));
	float u2 = Random(id, salt + 977u);
	float r = sqrt(-2.0 * log(u1));
	float theta = 2.0 * PI * u2;
	return float2(r * cos(theta), r * sin(theta));
}

/// <summary>The wave vector of a texel, in radians per metre.</summary>
float2 WaveVector(uint2 id)
{
	// Centred: the texel grid runs -N/2 … N/2, so the zero frequency sits at the middle and the
	// spectrum is symmetric about it — which is what makes h0(-k) meaningful.
	float2 n = float2(id) - SIZE * 0.5;
	return 2.0 * PI * n / max(1.0, _PatchMetres);
}

/// <summary>
/// The Phillips spectrum: how much energy a wind-driven sea carries at each wave vector.
/// </summary>
/// <remarks>
/// <para>
/// L = V²/g is the wavelength of the longest wave that wind can raise — the whole reason a gale
/// makes long swell and a breeze makes ripples. The k⁻⁴ falloff is the equilibrium range; the
/// directional term keeps waves running with the wind; and the small-wave cut-off removes the
/// components below the grid's resolution, which would otherwise alias into noise.
/// </para>
/// <para>
/// <b>One exit.</b> The compiler inlines this and turns every early return into a write to one
/// hidden result, then warns (X4000) that on some path the result is never written — it cannot see
/// that every path returns. A result that starts at zero, and one return, says exactly the same thing.
/// </para>
/// </remarks>
float Phillips(float2 k)
{
	float spectrum = 0.0;
	float k2 = dot(k, k);

	/* Each cascade owns a BAND of the spectrum, and nothing outside it.
	 *
	 * Three tiles of 520, 118 and 27 metres can each represent wavenumbers from 2π/patch up to the
	 * Nyquist limit of their own grid, and those ranges overlap heavily — a ten-metre wave fits in
	 * all three. Without a band-pass every wave in the overlap is generated two or three times and
	 * the energies add: measured, significant wave height came out 1.8x theory at 5 m/s and 5.0x
	 * at 20, and the ratio MOVED with the wind because the overlap does. Splitting the spectrum at
	 * the crossovers makes the cascades a partition rather than three copies.
	 */
	float magnitude = sqrt(k2);
	if (k2 >= 1e-9 && magnitude >= _MinWaveNumber && magnitude < _MaxWaveNumber)
	{
		float k4 = k2 * k2;
		float L = _WindSpeed * _WindSpeed / max(0.05, _Gravity);

		float2 kn = k * rsqrt(k2);
		float directional = pow(abs(dot(kn, _WindDirection)), _Directionality);
		// Waves running against the wind exist but are far weaker; killing them outright makes a
		// surface that is suspiciously one-way.
		if (dot(kn, _WindDirection) < 0.0)
		{
			directional *= 0.12;
		}

		spectrum = _Amplitude * exp(-1.0 / (k2 * L * L)) / k4 * directional;
		// Damp everything shorter than the smallest wave this grid can carry.
		spectrum *= exp(-k2 * _SmallWave * _SmallWave);
	}
	return spectrum;
}

// ── Stage 1: the static spectrum ──────────────────────────────────────

/// <summary>h0(k) in xy and conj(h0(-k))'s amplitude in zw, for one texel.</summary>
float4 FishInitialSpectrum(uint2 id, uint seed)
{
	float2 k = WaveVector(id);
	float2 g1 = Gaussian(id, seed);
	float2 g2 = Gaussian(uint2(SIZE - id.x, SIZE - id.y) & (SIZE - 1), seed + 7919u);

	/* Multiplied by the area of ONE CELL of k-space, and this is what makes three cascades add up
	 * to one sea.
	 *
	 * Phillips returns a spectral DENSITY — energy per unit area of k-space — so turning it into
	 * the amplitude of a discrete component means multiplying by the cell that component
	 * represents, (2π/patch)². Omitting it leaves each cascade's energy scaled by its own patch
	 * size: measured, significant wave height grew as roughly the fourth power of wind speed
	 * instead of the square, because the long-wave cascade that dominates at high wind was the
	 * one most over-weighted.
	 */
	float deltaK = 2.0 * PI / max(1.0, _PatchMetres);
	float cell = deltaK * deltaK;

	float h0 = sqrt(max(0.0, Phillips(k)) * cell * 0.5);
	float h0conj = sqrt(max(0.0, Phillips(-k)) * cell * 0.5);
	return float4(g1 * h0, g2 * h0conj);
}

// ── Stage 2: evolve it to this instant ────────────────────────────────

/// <summary>The height and both horizontal displacements at this instant, for one texel.</summary>
void FishTimeSpectrum(uint2 id, float4 h0, out float4 spectrumA, out float4 spectrumB)
{
	float2 k = WaveVector(id);
	float magnitude = max(1e-4, length(k));

	/* Deep-water dispersion, QUANTISED to the loop period.
	 *
	 * Left continuous, the surface never repeats. Snapping every frequency to a multiple of 2π/T
	 * makes the whole field exactly periodic in T at a cost no eye can find. */
	float omega = sqrt(_Gravity * magnitude);
	if (_LoopPeriod > 0.0)
	{
		float base = 2.0 * PI / _LoopPeriod;
		omega = floor(omega / base) * base;
	}

	float phase = omega * _FFTTime;
	float2 forward = float2(cos(phase), sin(phase));
	float2 backward = float2(forward.x, -forward.y);

	// h(k,t) = h0(k)·e^{iωt} + conj(h0(-k))·e^{-iωt}
	float2 h = ComplexMul(h0.xy, forward) + ComplexMul(float2(h0.z, -h0.w), backward);

	/* Horizontal displacement: D(k) = -i·(k/|k|)·h(k,t). It is what turns round sine humps into
	 * sharp crests and flat troughs — the single most recognisable feature of a real sea, and the
	 * reason a pure height field always looks like a bedsheet. */
	float2 kn = k / magnitude;
	float2 dx = ComplexMul(float2(0.0, -kn.x), h);
	float2 dz = ComplexMul(float2(0.0, -kn.y), h);

	spectrumA = float4(h, dx);
	spectrumB = float4(dz, 0.0, 0.0);
}

// ── Stage 3: the inverse FFT ──────────────────────────────────────────

uint BitReverse(uint value)
{
	uint result = 0;
	[unroll]
	for (uint i = 0; i < LOG_SIZE; i++)
	{
		result = (result << 1) | ((value >> i) & 1u);
	}
	return result;
}

/// <summary>Inverse twiddle: e^{+2πi·j/m}. The sign is what makes this a synthesis, not an analysis.</summary>
float2 Twiddle(uint j, uint m)
{
	float angle = 2.0 * PI * float(j) / float(m);
	return float2(cos(angle), sin(angle));
}

// ── Stage 4: displacement, slope and folding ──────────────────────────

/// <summary>The checkerboard sign that undoes the half-grid shift of a centred spectrum.</summary>
float FishCheckerboard(uint2 id)
{
	return ((id.x + id.y) & 1u) ? -1.0 : 1.0;
}

/// <summary>
/// Displacement and derivatives for one texel, from the transformed spectra at it and its four
/// neighbours (wrapped: the field is periodic, so the edges join).
/// </summary>
/// <remarks>
/// <para>
/// <b>The checkerboard sign.</b> The spectrum is indexed from -N/2, so the transform comes out
/// shifted by half the grid. Multiplying by (-1)^(x+y) puts it back. Forgetting it produces a field
/// that looks plausible and is offset by half a tile from its own normals — the kind of error that
/// reads as "the lighting is wrong somehow" rather than as a bug.
/// </para>
/// <para>
/// <b>Folding</b> is the Jacobian of the horizontal displacement. Where it goes negative the surface
/// has turned itself inside out, which is physically where a wave is breaking — so this is the foam
/// signal, derived rather than painted.
/// </para>
/// </remarks>
void FishAssemble(uint2 id,
	float4 aHere, float4 aEast, float4 aWest, float4 aNorth, float4 aSouth,
	float4 bHere, float4 bEast, float4 bWest, float4 bNorth, float4 bSouth,
	out float4 displacement, out float4 derivatives)
{
	uint2 east = uint2((id.x + 1) & (SIZE - 1), id.y);
	uint2 west = uint2((id.x + SIZE - 1) & (SIZE - 1), id.y);
	uint2 north = uint2(id.x, (id.y + 1) & (SIZE - 1));
	uint2 south = uint2(id.x, (id.y + SIZE - 1) & (SIZE - 1));

	float sign = FishCheckerboard(id);
	float signEast = FishCheckerboard(east);
	float signWest = FishCheckerboard(west);
	float signNorth = FishCheckerboard(north);
	float signSouth = FishCheckerboard(south);

	float height = aHere.x * sign;
	float dx = aHere.z * sign * _Choppiness;
	float dz = bHere.x * sign * _Choppiness;
	displacement = float4(dx, height, dz, 0.0);

	float spacing = _PatchMetres / float(SIZE);
	float2 slope = float2(aEast.x * signEast - aWest.x * signWest, aNorth.x * signNorth - aSouth.x * signSouth)
		/ (2.0 * spacing);

	float dxEast = aEast.z * signEast * _Choppiness;
	float dxWest = aWest.z * signWest * _Choppiness;
	float dzNorth = bNorth.x * signNorth * _Choppiness;
	float dzSouth = bSouth.x * signSouth * _Choppiness;
	float dxNorthSouth = aNorth.z * signNorth * _Choppiness - aSouth.z * signSouth * _Choppiness;
	float dzEastWest = bEast.x * signEast * _Choppiness - bWest.x * signWest * _Choppiness;

	float jxx = 1.0 + (dxEast - dxWest) / (2.0 * spacing);
	float jzz = 1.0 + (dzNorth - dzSouth) / (2.0 * spacing);
	float jxz = dxNorthSouth / (2.0 * spacing);
	float jzx = dzEastWest / (2.0 * spacing);
	derivatives = float4(slope, jxx * jzz - jxz * jzx, 0.0);
}

#endif
