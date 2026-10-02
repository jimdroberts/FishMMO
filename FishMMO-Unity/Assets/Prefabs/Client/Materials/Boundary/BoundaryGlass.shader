// The glass barrier on a scene boundary, breaking where the player pushes into it.
//
// ONE break, set entirely by distance: no timers, no restarts. The fracture pattern is fixed to the
// face, the way a real pane's would be, at three scales — coarse shards, medium, fine. The player
// presses on it with a field of pressure centred in front of their chest (_Centre), as strong as
// their distance from the boundary makes it (_Pressure) and about a body wide (_Radius). Where the
// pressure is, the glass is broken that far:
//   light pressure     coarse cracks, appearing one at a time as it rises
//   more               medium cracks inside the coarse shards
//   most               the finest fracture, right in front of the player
//   the force          (_Force, a metre or two short of the boundary) shards break loose, tilt, and
//                      are pushed outward from the player, opening gaps the world shows through;
//                      the glass dents away from them; and some pieces fall out altogether —
//                      whole coarse shards slipping down and away, and smaller pieces inside the
//                      rest — leaving holes edged by the cracked glass round them
// Walk closer and it breaks further; back off and it eases back; walk along the wall and the break
// travels with you through the fixed pattern. Only the shards' tremble while held runs on the clock.
//
// Drawn as glass throughout: refraction through the camera's opaque texture (turned on only while a
// pane is up), dispersion, sky reflection and sun glints. Procedural, fragment work only, WebGL-safe.
Shader "FishMMO/Boundary Glass"
{
	Properties
	{
		_Tint ("Glass tint", Color) = (0.86, 0.94, 0.95, 1.0)
		_CrackColour ("Crack colour", Color) = (0.95, 0.98, 1.0, 1.0)
		_ShardSize ("Coarse shard size (m)", Float) = 0.32
		_MediumScale ("Medium shards, as a share of coarse", Range(0.1, 0.8)) = 0.34
		_FineScale ("Fine shards, as a share of coarse", Range(0.05, 0.5)) = 0.13
		_CrackWidth ("Crack width (m)", Float) = 0.004
		_Refraction ("Refraction (screen fraction at full tilt)", Range(0, 0.08)) = 0.022
		_Dispersion ("Colour fringing", Range(0, 1)) = 0.35
		_MaxTiltDegrees ("Shard tilt at full fracture (degrees)", Range(0, 20)) = 8
		_Reflection ("Reflection strength", Range(0, 3)) = 1.3
		_ShardCurve ("Shard warp", Range(0, 0.3)) = 0.05
		_Glint ("Sun glint strength", Range(0, 40)) = 14
		_ReflectTilt ("Reflection tilt multiplier", Range(1, 4)) = 2
		_ProbeWeight ("Reflection probe weight", Range(0, 1)) = 0.35
		_Smudges ("Smudges and dust", Range(0, 1)) = 0.6
		_EdgeTint ("Glass edge colour", Color) = (0.55, 0.80, 0.68, 1.0)
		_SeaLevel ("Sea level (world Y)", Float) = 0
		_Expand ("Shard push at the centre (m)", Range(0, 0.3)) = 0.05
		_Dent ("Dent round the centre", Range(0, 1)) = 0.35
		_Tremble ("Shard tremble (m)", Range(0, 0.02)) = 0.002
		_MissingShare ("Share of shards that can fall out", Range(0, 1)) = 0.35
		_MissingDrop ("How far a falling shard slips (m)", Range(0, 0.5)) = 0.12
		// Set per pane by ClientBoundaryWarning through a property block, every frame.
		_Pressure ("Pressure", Range(0, 1)) = 1
		_Force ("Force", Range(0, 1)) = 0
		_Centre ("Pressure centre (world)", Vector) = (0, 0, 0, 0)
		_Radius ("Pressure radius (m)", Float) = 1.2
		_AxisU ("Face axis U (world)", Vector) = (1, 0, 0, 0)
		_AxisV ("Face axis V (world)", Vector) = (0, 1, 0, 0)
	}

	SubShader
	{
		Tags { "RenderType" = "Transparent" "Queue" = "Transparent+50" "RenderPipeline" = "UniversalPipeline" "IgnoreProjector" = "True" }

		Pass
		{
			Name "BoundaryGlass"
			Tags { "LightMode" = "UniversalForward" }
			Blend SrcAlpha OneMinusSrcAlpha
			ZWrite Off
			Cull Off

			HLSLPROGRAM
			#pragma vertex Vert
			#pragma fragment Frag
			#pragma target 3.0

			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Core.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/Lighting.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareOpaqueTexture.hlsl"
			#include "Packages/com.unity.render-pipelines.universal/ShaderLibrary/DeclareDepthTexture.hlsl"

			CBUFFER_START(UnityPerMaterial)
				half4 _Tint;
				half4 _CrackColour;
				float _ShardSize;
				float _MediumScale;
				float _FineScale;
				float _CrackWidth;
				float _Refraction;
				half _Dispersion;
				float _MaxTiltDegrees;
				half _Reflection;
				float _ShardCurve;
				half _Glint;
				float _ReflectTilt;
				half _ProbeWeight;
				half _Smudges;
				half4 _EdgeTint;
				float _SeaLevel;
				float _Expand;
				float _Dent;
				float _Tremble;
				float _MissingShare;
				float _MissingDrop;
				half _Pressure;
				half _Force;
				float4 _Centre;
				float _Radius;
				float4 _AxisU;
				float4 _AxisV;
			CBUFFER_END

			struct Attributes
			{
				float4 positionOS : POSITION;
				float3 normalOS : NORMAL;
			};

			struct Varyings
			{
				float4 positionCS : SV_POSITION;
				float3 positionWS : TEXCOORD0;
				float3 normalWS : TEXCOORD1;
			};

			Varyings Vert(Attributes input)
			{
				Varyings output;
				output.positionWS = TransformObjectToWorld(input.positionOS.xyz);
				output.positionCS = TransformWorldToHClip(output.positionWS);
				output.normalWS = TransformObjectToWorldNormal(input.normalOS);
				return output;
			}

			// ── Noise ─────────────────────────────────────────────────

			// Wrapped before hashing, so sin() never sees a large argument (where GPUs lose
			// precision and a hash starts showing stripes).
			float2 Hash2(float2 p)
			{
				p = p - 289.0 * floor(p / 289.0);
				p = float2(dot(p, float2(127.1, 311.7)), dot(p, float2(269.5, 183.3)));
				return frac(sin(p) * 43758.5453);
			}

			float ValueNoise(float2 p)
			{
				float2 i = floor(p);
				float2 f = frac(p);
				f = f * f * (3.0 - 2.0 * f);
				float a = Hash2(i).x, b = Hash2(i + float2(1, 0)).x;
				float c = Hash2(i + float2(0, 1)).x, d = Hash2(i + float2(1, 1)).x;
				return lerp(lerp(a, b, f.x), lerp(c, d, f.x), f.y);
			}

			// Bends the shard lattice: a broad curve so fractures are not polygons, and a fine
			// zigzag, which is what a real crack does as it runs.
			float2 Warp(float2 p)
			{
				float2 broad = float2(ValueNoise(p * 0.9), ValueNoise(p * 0.9 + 31.7)) - 0.5;
				float2 fine = float2(ValueNoise(p * 7.0 + 5.1), ValueNoise(p * 7.0 + 17.3)) - 0.5;
				return broad * 0.55 + fine * 0.07;
			}

			// ── Shards ────────────────────────────────────────────────

			float2 SeedOf(float2 cell, float salt)
			{
				return cell + Hash2(cell + salt);
			}

			float2 NearestCell(float2 q, float salt)
			{
				float2 base = floor(q);
				float nearest = 8.0;
				float2 found = base;
				for (int y = -1; y <= 1; y++)
				{
					for (int x = -1; x <= 1; x++)
					{
						float2 c = base + float2(x, y);
						float2 o = SeedOf(c, salt) - q;
						float d = dot(o, o);
						if (d < nearest)
						{
							nearest = d;
							found = c;
						}
					}
				}
				return found;
			}

			// Distance from a point to the edge of a given shard, along the bisector, in cell units,
			// and which shard lies across that edge.
			float EdgeDistance(float2 q, float2 cell, float salt, out float2 across)
			{
				float2 own = SeedOf(cell, salt) - q;
				float2 base = floor(q);
				float edge = 8.0;
				across = cell;
				for (int y = -2; y <= 2; y++)
				{
					for (int x = -2; x <= 2; x++)
					{
						float2 c = base + float2(x, y);
						float2 o = SeedOf(c, salt) - q;
						float2 between = o - own;
						if (dot(between, between) > 1e-5)
						{
							float d = dot(0.5 * (own + o), normalize(between));
							if (d < edge)
							{
								edge = d;
								across = c;
							}
						}
					}
				}
				return edge;
			}

			float EdgeDistance(float2 q, float2 cell, float salt)
			{
				float2 unused;
				return EdgeDistance(q, cell, salt, unused);
			}

			// A crack line of a given width in metres, never thinner than about a pixel, so a
			// hairline seen from a distance fades out instead of breaking into dots.
			float CrackLine(float metres, float width)
			{
				float pixel = max(fwidth(metres), 1e-5);
				float w = max(width, pixel * 0.9);
				return (1.0 - smoothstep(w * 0.4, w * 1.3, metres)) * saturate(width / w * 1.4);
			}

			// One finer layer of fracture inside the coarse shards: how strongly its crack shows
			// here, at this pressure. Each of its shards cracks at its own pressure, so the layer
			// fills in a crack at a time rather than switching on.
			float FractureLayer(float2 metres, float size, float salt, float pressure, float threshold,
				out float2 cell, out float edgeMetres, out float2 across)
			{
				float2 uv = metres / size;
				float2 warped = uv + Warp(uv) * 0.8;
				cell = NearestCell(warped, salt);
				float cracksAt = threshold + Hash2(cell + salt + 3.1).x * 0.25;
				float shown = saturate((pressure - cracksAt) / 0.12);
				edgeMetres = EdgeDistance(warped, cell, salt, across) * size;
				return CrackLine(edgeMetres, _CrackWidth * 0.8) * shown;
			}

			// The force at a distance from the centre: strong in front of the chest, gone well
			// inside the pressure's reach.
			float ForceAt(float radius)
			{
				float r = radius / max(_Radius * 0.7, 1e-3);
				return _Force * exp(-r * r * 2.2);
			}

			// The force as it reaches for pieces to knock out: the full width of the pressure, so
			// holes open round the player's outline and not only behind them, where their own body
			// hides them from a camera over their shoulder.
			float LooseningAt(float radius)
			{
				float r = radius / max(_Radius, 1e-3);
				return _Force * exp(-r * r * 1.6);
			}

			// Whether a coarse shard has fallen out (0 in place, 1 gone), from where it sits.
			float CoarseMissing(float2 cell, float size, float2 centrePlane)
			{
				float2 fate = Hash2(cell + 41.9);
				float forceHere = LooseningAt(length(SeedOf(cell, 0.0) * size - centrePlane));
				return step(fate.x, _MissingShare) * saturate((forceHere - (0.25 + fate.y * 0.55)) / 0.1);
			}

			// Whether a smaller piece has fallen out, from the force where it is.
			float PieceMissing(float2 cell, float forceHere)
			{
				float2 fate = Hash2(cell + 59.3);
				return step(fate.x, _MissingShare * 0.6) * saturate((forceHere - (0.3 + fate.y * 0.5)) / 0.1);
			}

			// A sky to reflect: horizon haze, deep zenith, dark ground below, and the sun's disc and
			// glow. Reflections need something with contrast in it, and a probe is not always that.
			half3 SkyColour(float3 direction, float3 sunDirection, half3 sunColour)
			{
				float up = direction.y;
				half3 horizon = half3(0.86, 0.90, 0.95);
				half3 zenith = half3(0.30, 0.50, 0.85);
				half3 ground = half3(0.16, 0.18, 0.16);
				half3 colour = up >= 0.0
					? lerp(horizon, zenith, (half)pow(saturate(up), 0.55))
					: lerp(horizon * 0.65, ground, (half)saturate(-up * 5.0));
				float toSun = saturate(dot(direction, sunDirection));
				colour += sunColour * (half)(pow(toSun, 2400.0) * 30.0 + pow(toSun, 16.0) * 0.3);
				return colour;
			}

			// The pressure at a distance from the centre: full in front of the chest, gone a body's
			// width away.
			float PressureAt(float radius)
			{
				float r = radius / max(_Radius, 1e-3);
				return _Pressure * exp(-r * r * 2.2);
			}

			// ── The pane ──────────────────────────────────────────────

			half4 Frag(Varyings input) : SV_Target
			{
				float3 offset = input.positionWS - _Centre.xyz;
				float2 plane = float2(dot(offset, _AxisU.xyz), dot(offset, _AxisV.xyz));
				float radius = length(plane);
				float pressure = PressureAt(radius);
				// The pane is only where there is pressure to show.
				float mask = smoothstep(0.02, 0.12, pressure);
				if (mask <= 0.002)
				{
					discard;
				}
				float now = _Time.y;

				// ── Coarse shards, fixed to the face, pushed out under the force ──
				float size = max(_ShardSize, 0.02);
				float2 worldPlane = float2(dot(input.positionWS, _AxisU.xyz), dot(input.positionWS, _AxisV.xyz));
				float2 centrePlane = float2(dot(_Centre.xyz, _AxisU.xyz), dot(_Centre.xyz, _AxisV.xyz));
				float2 uv = worldPlane / size;
				float2 warped = uv + Warp(uv);
				const float CoarseSalt = 0.0;

				float claimed = 0.0;
				float2 shardCell = 0;
				float2 shardMove = 0;
				float loose = 0.0;
				float missing = 0.0;
				float2 base = floor(warped);
				for (int y = -1; y <= 1; y++)
				{
					for (int x = -1; x <= 1; x++)
					{
						float2 c = base + float2(x, y);
						float2 rnd = Hash2(c + 17.3);
						// The shard's seed, back on the face in metres (the warp is small enough to
						// ignore for where a shard sits relative to the player).
						float2 seedMetres = SeedOf(c, CoarseSalt) * size;
						float2 away = seedMetres - centrePlane;
						float far = length(away);
						// Loose once the force at this shard passes its own strength.
						float forceHere = _Force * exp(-pow(far / max(_Radius * 0.7, 1e-3), 2.0) * 2.2);
						float looseHere = saturate((forceHere - rnd.x * 0.5) / 0.25);
						float2 direction = far > 1e-4 ? away / far : float2(0.0, 0.0);
						float push = (_Expand * forceHere + _Tremble * sin(now * 37.0 + rnd.y * 6.2831853) * forceHere) * looseHere;
						// Some shards fall out once the force at them passes their own threshold: they
						// slip down and away as they go, rather than vanishing.
						float missingHere = CoarseMissing(c, size, centrePlane);
						float2 move = (direction * push - float2(0.0, _MissingDrop * missingHere)) / size;
						if (claimed < 0.5)
						{
							float2 owner = NearestCell(warped - move, CoarseSalt);
							if (dot(owner - c, owner - c) < 0.25)
							{
								claimed = 1.0;
								shardCell = c;
								shardMove = move;
								loose = looseHere;
								missing = missingHere;
							}
						}
					}
				}
				// Nothing covers this pixel, or its shard has fallen out: a hole, and the world shows through.
				if (claimed < 0.5 || missing > 0.98)
				{
					discard;
				}

				float2 moved = warped - shardMove;
				float2 inShard = moved - SeedOf(shardCell, CoarseSalt);
				float2 shardRandom = Hash2(shardCell + 17.3);

				// Coarse cracks: each shard's edge shows once the pressure passes that shard's own
				// threshold, and a loose shard shows its thickness at the rim.
				float coarseAt = 0.12 + shardRandom.y * 0.25;
				float coarseShown = saturate((pressure - coarseAt) / 0.12);
				float rimWidth = _CrackWidth * (1.0 + length(shardMove) * size * 40.0);
				float2 acrossCoarse;
				float coarseEdgeMetres = EdgeDistance(moved, shardCell, CoarseSalt, acrossCoarse) * size;
				float crack = CrackLine(coarseEdgeMetres, rimWidth) * max(coarseShown, loose);
				// Where the shard next door has fallen out, this one's broken edge faces the hole:
				// the glass's thickness shows there as a bright rim.
				float holeRim = CrackLine(coarseEdgeMetres, _CrackWidth * 3.0) * CoarseMissing(acrossCoarse, size, centrePlane);

				// The finer layers, riding their coarse shard.
				float2 shardMetres = worldPlane - shardMove * size;
				float2 mediumCell, fineCell, mediumAcross, fineAcross;
				float mediumEdgeMetres, fineEdgeMetres;
				crack = max(crack, FractureLayer(shardMetres, size * _MediumScale, 7.7, pressure, 0.45, mediumCell, mediumEdgeMetres, mediumAcross));
				crack = max(crack, FractureLayer(shardMetres, size * _FineScale, 13.9, pressure, 0.72, fineCell, fineEdgeMetres, fineAcross));
				float fineShown = saturate((pressure - 0.72) / 0.2);

				// Smaller pieces fall out of the shards that are left, where the force is strongest.
				float forceAtPixel = LooseningAt(radius);
				float pieceMissing = PieceMissing(mediumCell, forceAtPixel);
				if (pieceMissing > 0.98)
				{
					discard;
				}
				holeRim = max(holeRim, CrackLine(mediumEdgeMetres, _CrackWidth * 2.2) * PieceMissing(mediumAcross, forceAtPixel));
				// What is left of a piece on its way out fades with it.
				float remaining = (1.0 - missing) * (1.0 - pieceMissing);

				// ── Optics ────────────────────────────────────────────

				float3 view = normalize(GetCameraPositionWS() - input.positionWS);
				float3 paneNormal = normalize(input.normalWS);
				paneNormal = dot(paneNormal, view) < 0.0 ? -paneNormal : paneNormal;

				// Cracked shards sit at small angles of their own, loose ones further and trembling;
				// each very slightly warped so a reflection sweeps across it instead of filling it flat.
				float crackedness = max(coarseShown * 0.35, loose);
				float tilt = radians(_MaxTiltDegrees) * crackedness;
				float2 wobble = float2(sin(now * 11.0 + shardRandom.x * 40.0), cos(now * 9.0 + shardRandom.y * 40.0)) * 0.25 * loose * _Force;
				float2 shardSlope = (shardRandom - 0.5) * 2.0 + wobble + (Hash2(fineCell + 9.7) - 0.5) * fineShown * 0.6;
				float2 slope = shardSlope * tilt + float2(inShard.y, inShard.x) * _ShardCurve * crackedness;

				// The dent: round the centre the glass is pushed away from the player.
				float2 radial = radius > 1e-4 ? plane / radius : float2(0.0, 0.0);
				float dentShape = exp(-pow(radius / max(_Radius * 0.6, 1e-3), 2.0)) * saturate(radius * 10.0);
				slope -= radial * _Dent * dentShape * _Force;

				// The crack as a groove: faces leaning apart, one lit and one in shadow.
				float2 crackScreen = float2(ddx(crack), ddy(crack));
				float2 du = ddx(plane), dv = ddy(plane);
				float det = du.x * dv.y - du.y * dv.x;
				float2 crackSlope = abs(det) > 1e-12
					? float2(dv.y * crackScreen.x - du.y * crackScreen.y, -dv.x * crackScreen.x + du.x * crackScreen.y) / det
					: float2(0.0, 0.0);
				float2 bevel = clamp(crackSlope * _CrackWidth * 0.7, -0.9, 0.9);

				// Reflection gets the tilt doubled, as a mirror does.
				float3 normal = normalize(paneNormal
					+ _AxisU.xyz * (slope.x * _ReflectTilt - bevel.x)
					+ _AxisV.xyz * (slope.y * _ReflectTilt - bevel.y));

				// Refraction through the camera's copy of the opaque scene.
				float2 screenUV = GetNormalizedScreenSpaceUV(input.positionCS);
				float paneDepth = LinearEyeDepth(input.positionCS.z, _ZBufferParams);
				float2 bend = slope / max(radians(_MaxTiltDegrees), 1e-4) * _Refraction - bevel * 0.01;
				// Nothing in front of the pane is seen through it, and nothing under the sea: the
				// copy is taken before the water draws, so refracting there would show bare sea floor.
				float behindRaw = SampleSceneDepth(screenUV + bend);
				float behindEye = LinearEyeDepth(behindRaw, _ZBufferParams);
				// OpenGL's depth buffer runs -1..1; ComputeWorldSpacePosition wants the device range.
				#if UNITY_REVERSED_Z
					float behindDevice = behindRaw;
				#else
					float behindDevice = lerp(UNITY_NEAR_CLIP_VALUE, 1.0, behindRaw);
				#endif
				float3 behindWorld = ComputeWorldSpacePosition(screenUV + bend, behindDevice, UNITY_MATRIX_I_VP);
				bool refract = behindEye > paneDepth && (behindWorld.y > _SeaLevel || GetCameraPositionWS().y < _SeaLevel);
				float2 through = refract ? screenUV + bend : screenUV;
				half3 refracted;
				refracted.r = SampleSceneColor(through + bend * _Dispersion * 0.35).r;
				refracted.g = SampleSceneColor(through).g;
				refracted.b = SampleSceneColor(through - bend * _Dispersion * 0.35).b;
				refracted *= _Tint.rgb;

				// The surface itself: faint smudges and dust.
				float grime = ValueNoise(worldPlane * 2.1) * 0.5 + ValueNoise(worldPlane * 0.6 + 7.0) * 0.35 + ValueNoise(worldPlane * 9.0) * 0.15;
				grime = smoothstep(0.45, 0.9, grime) * _Smudges;

				// What the glass reflects: its own sky and sun, blended with the scene's probe.
				Light sun = GetMainLight();
				float3 mirror = reflect(-view, normal);
				half3 environment = lerp(SkyColour(mirror, sun.direction, sun.color),
					GlossyEnvironmentReflection(mirror, (half)(0.02 + grime * 0.2), (half)1.0), (half)_ProbeWeight);

				// Schlick, glass at 4% head on; cracked shards add a little from their inner faces.
				float facing = saturate(dot(normal, view));
				half fresnel = (half)(0.04 + 0.96 * pow(1.0 - facing, 5.0));
				half shardSilver = (half)(0.6 + 0.8 * shardRandom.y);
				half reflection = saturate(fresnel * _Reflection + (half)(0.07 * crackedness) * shardSilver);

				// The sun on the glass: a hard glint and a soft sheen round it.
				float3 halfway = normalize(sun.direction + view);
				float nh = saturate(dot(normal, halfway));
				half specular = (half)(pow(nh, 1800.0) * _Glint + pow(nh, 220.0) * 0.05 * _Glint / 6.0);

				half3 haze = environment * (half)(grime * 0.08);
				half3 glass = refracted * (1.0 - reflection) + environment * reflection + sun.color * specular + haze;

				// Cracks: faces turned to the light shine, the others fall dark, and the glass shows
				// its own green at the break.
				half crackLight = (half)saturate(crack);
				float3 faceNormal = normalize(paneNormal - (_AxisU.xyz * bevel.x + _AxisV.xyz * bevel.y) * 2.5);
				half lit = (half)saturate(dot(faceNormal, sun.direction) * 0.5 + 0.5);
				half litSide = lit * lit;
				half3 faceMirror = lerp(SkyColour(reflect(-view, faceNormal), sun.direction, sun.color), environment, (half)0.3);
				half3 crackColour = lerp(_EdgeTint.rgb * 0.3, _CrackColour.rgb, litSide) * (half)(0.45 + 0.55 * litSide) + faceMirror * 0.25
					+ sun.color * (half)pow(saturate(dot(faceNormal, halfway)), 300.0) * 2.0;
				glass = lerp(glass, crackColour, crackLight * (half)0.8);

				// The rim of a hole: the glass seen edge-on, green and catching the light.
				half rim = (half)saturate(holeRim);
				half3 rimColour = lerp(_EdgeTint.rgb, half3(1.0, 1.0, 1.0), (half)0.35 + 0.4 * litSide) * (half)(0.55 + 0.45 * litSide)
					+ faceMirror * 0.3 + sun.color * (half)pow(saturate(dot(faceNormal, halfway)), 200.0) * 2.5;
				glass = lerp(glass, rimColour, rim);

				// Where the pane can bend the world it replaces what is behind it; where it cannot
				// (over the sea, or round something in front of it), it is drawn by what it adds.
				// Unbroken glass is barely there: a faint tint and its reflections.
				half added = saturate(reflection + specular + (half)(grime * 0.15) + crackLight);
				half body = refract ? (half)(0.25 + 0.75 * saturate(pressure * 1.2)) : added;
				half alpha = (half)mask * max(max(body, crackLight), rim) * (half)remaining;
				return half4(glass, saturate(alpha));
			}
			ENDHLSL
		}
	}
	FallBack Off
}
