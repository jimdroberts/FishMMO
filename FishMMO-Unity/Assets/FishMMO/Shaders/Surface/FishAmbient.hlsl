#ifndef FISH_AMBIENT_INCLUDED
#define FISH_AMBIENT_INCLUDED

// The sky's ambient light as the scene is lit by it: SkySystem.ApplyAmbient's trilight (sky above,
// the horizon round, the ground below), published as globals every time it sets RenderSettings.
//
// Not SampleSH. Particles and curtains are drawn with Graphics.RenderMesh, and nothing binds light-probe
// coefficients to such a draw: SampleSH read zeros there, so rain, snow, splashes and the distant
// curtains were lit by the sun and its cloud shadow alone — orange under a low sun, and darker than the
// ground and the fog under a storm, where a white flake read as a speck of dirt.
half4 _FishAmbientSky;
half4 _FishAmbientEquator;
half4 _FishAmbientGround;

// The trilight Unity itself builds from those three colours: up-facing gets the sky, down-facing the
// ground, and what faces the horizon the equator, blended by the direction's height.
half3 FishTrilight(half3 direction)
{
    half3 d = normalize(direction);
    half up = saturate(d.y);
    half down = saturate(-d.y);
    half level = 1.0 - up - down;
    return _FishAmbientSky.rgb * up + _FishAmbientEquator.rgb * level + _FishAmbientGround.rgb * down;
}

#endif
