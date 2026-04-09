# FishMMO Realistic Water Shader Tutorial

## Overview

This package provides a complete realistic water shader solution that supports:
- Realistic water surface with dual-layer normal mapping
- Beach waves with procedural foam generation
- Shoreline tide effects with wave displacement
- Depth-based transparency and color blending
- Fresnel reflections and specular highlights
- Animated wave motion with customizable parameters

**Included Approaches:**
1. **Complete Shader File** (`RealisticWaterShader.shader`) - Ready to use HLSL shader
2. **ShaderGraph Tutorial** - Step-by-step guide to recreate in ShaderGraph
3. **Pre-configured Materials** - Ocean, Lake, and Beach presets

## Prerequisites

- Unity 2022.3+ with URP (Universal Render Pipeline)
- ShaderGraph package installed
- Basic understanding of ShaderGraph nodes

## Tutorial Structure

1. [Basic Water Setup](#basic-water-setup)
2. [Wave Animation](#wave-animation)
3. [Normal Mapping](#normal-mapping)
4. [Beach Waves and Foam](#beach-waves-and-foam)
5. [Shoreline Tides](#shoreline-tides)
6. [Depth and Transparency](#depth-and-transparency)
7. [Final Polish](#final-polish)

## Shader Features

The provided `RealisticWaterShader.shader` includes:

### Wave System
- **Primary Waves**: Sine-based wave displacement with directional control
- **Tide Effects**: Secondary wave system for shoreline simulation
- **Vertex Displacement**: Real geometry deformation for realistic surface

### Foam Generation
- **Depth-Based Detection**: Automatic foam at shorelines and shallow areas
- **Noise Texture Integration**: Organic foam patterns with animation
- **Customizable Intensity**: Adjustable foam distance and opacity

### Surface Rendering
- **Dual Normal Maps**: Two animated normal layers for complex surface detail
- **Fresnel Reflections**: Realistic edge transparency and reflection
- **Depth-Based Coloring**: Automatic color blending from shallow to deep water
- **Specular Highlights**: Dynamic light reflection on water surface

### Infinite Ocean System
- **Far Clip Plane Extension**: Water extends to camera's far clip plane
- **Distance-Based LOD**: Automatic quality reduction for distant water
- **Horizon Blending**: Smooth color transition to far ocean color
- **Performance Optimization**: Reduced complexity for distant water rendering

---

## Basic Water Setup

### Option 1: Use the Provided Shader (Recommended)

The package includes a complete water shader at:
`Assets/Plugins/FishMMO Water/Shaders/RealisticWaterShader.shader`

This shader is ready to use and includes all features described in this tutorial.

### Option 2: Create Your Own ShaderGraph

1. In the Project window, navigate to `Assets/Plugins/FishMMO Water/Shaders/`
2. Right-click and select `Create > Shader Graph > URP > Lit Shader Graph`
3. Name it `CustomWaterShader`

**Note:** The provided `.shadergraph` file contains detailed instructions for manual creation.

### Step 2: Configure Basic Properties

Open the shader and set these properties in the Graph Inspector:
- **Surface Type**: Transparent
- **Blending Mode**: Alpha
- **Two Sided**: Enabled
- **Depth Write**: Off
- **Depth Test**: LEqual

### Step 3: Create Base Color Properties

Add these properties to your shader:
- `WaterColor` (Color) - Deep water color (default: dark blue #0B4F6C)
- `ShallowColor` (Color) - Shallow water color (default: light blue #3E9AC7)
- `Transparency` (Float, Range 0-1) - Water transparency (default: 0.8)

---

## Wave Animation

### Step 1: Create Wave Parameters

Add these properties:
- `WaveSpeed` (Float) - Wave animation speed (default: 1.0)
- `WaveHeight` (Float) - Wave amplitude (default: 0.1)
- `WaveFrequency` (Float) - Wave frequency (default: 1.0)
- `WaveDirection` (Vector2) - Wave direction (default: 1,1)

### Step 2: Build Wave Animation Network

1. **Time Node** → Connect to a **Multiply** node with `WaveSpeed`
2. **UV Node** → **Multiply** with `WaveFrequency`
3. **Add** the time and UV results
4. **Sine** function for wave calculation
5. **Multiply** result with `WaveHeight`

### Step 3: Apply to Vertex Position

Connect the wave calculation to the **Vertex Position** output to create surface displacement.

---

## Normal Mapping

### Step 1: Create Normal Map Properties

Add these properties:
- `NormalMap` (Texture2D) - Water normal map texture
- `NormalStrength` (Float) - Normal map intensity (default: 1.0)
- `NormalSpeed` (Vector2) - Normal animation speed (default: 0.1, 0.1)
- `NormalScale` (Float) - Normal texture tiling (default: 1.0)

### Step 2: Animated Normal Sampling

1. **UV Node** → **Multiply** with `NormalScale`
2. **Time** → **Multiply** with `NormalSpeed`
3. **Add** UV and time for scrolling effect
4. **Sample Texture 2D** with the animated UV
5. **Normal Unpack** → **Multiply** with `NormalStrength`

### Step 3: Dual Layer Normals

Create two normal layers moving in different directions for more realistic water surface:
- Layer 1: Original direction and speed
- Layer 2: Perpendicular direction, different speed
- **Add** both normal results and **Normalize**

---

## Beach Waves and Foam

### Step 1: Depth Detection

Add these properties:
- `FoamDistance` (Float) - Distance for foam generation (default: 1.0)
- `FoamColor` (Color) - Foam color (default: white)
- `FoamIntensity` (Float) - Foam opacity (default: 1.0)

### Step 2: Scene Depth Setup

1. **Scene Depth** node to get depth information
2. **Screen Position** node for current pixel depth
3. **Subtract** scene depth from screen position
4. **Saturate** and **One Minus** for foam mask

### Step 3: Foam Animation

1. **Noise Texture** for foam pattern variation
2. **Time-based UV scrolling** for foam movement
3. **Multiply** depth mask with noise pattern
4. **Step** function to create sharp foam edges

---

## Shoreline Tides

### Step 1: Tide Parameters

Add these properties:
- `TideHeight` (Float) - Tide amplitude (default: 0.5)
- `TideSpeed` (Float) - Tide cycle speed (default: 0.1)
- `TideOffset` (Vector2) - Positional offset for tide variation

### Step 2: Tide Calculation

1. **Object Position** → Extract world coordinates
2. **Time** → **Multiply** with `TideSpeed`
3. **Sine** wave for periodic tide motion
4. **Multiply** with position-based variation

### Step 3: Combine with Waves

**Add** tide calculation to existing wave animation for realistic shoreline behavior.

---

## Depth and Transparency

### Step 1: Depth-Based Color

1. Use the depth calculation from foam setup
2. **Lerp** between `ShallowColor` and `WaterColor` based on depth
3. **Multiply** with base transparency

### Step 2: Fresnel Effect

Add these properties:
- `FresnelPower` (Float) - Fresnel intensity (default: 2.0)

1. **Fresnel Effect** node
2. **Power** with `FresnelPower` parameter
3. Use for edge transparency and reflection mixing

---

## Final Polish

### Step 1: Surface Smoothness

Add these properties:
- `Smoothness` (Float, Range 0-1) - Surface smoothness (default: 0.9)
- `Metallic` (Float, Range 0-1) - Metallic property (default: 0.0)

### Step 2: Emission for Highlights

1. **Fresnel** calculation for water highlights
2. **Multiply** with light color for surface reflections
3. Connect to **Emission** output

### Step 3: Final Connections

Connect all calculations to the appropriate outputs:
- **Base Color**: Depth-based color with foam
- **Normal**: Combined animated normals
- **Metallic**: Metallic property
- **Smoothness**: Smoothness property
- **Alpha**: Combined transparency with foam
- **Emission**: Surface highlights

---

## Usage Instructions

### Creating Materials

1. Create a new Material in `Materials/` folder
2. Assign the `RealisticWaterShader` to the material
3. Configure textures and parameters as needed

### Recommended Textures

Place these textures in the `Textures/` folder:
- `WaterNormal.png` - Tileable water normal map
- `FoamNoise.png` - Noise texture for foam patterns
- `WaveNormal.png` - Secondary normal for wave details

### Parameter Recommendations

**Ocean Water:**
- WaveHeight: 0.2-0.5
- WaveSpeed: 0.5-1.0
- Transparency: 0.7-0.9
- Enable Infinite Ocean: 1.0 (enabled)
- Far Ocean Fade Distance: 0.7
- Horizon Blend: 0.8-0.9
- Distance Wave Reduction: 0.8

**Lake Water:**
- WaveHeight: 0.05-0.1
- WaveSpeed: 0.2-0.5
- Transparency: 0.8-0.95
- Enable Infinite Ocean: 0.0 (disabled)

**Beach/Shoreline:**
- FoamDistance: 0.5-2.0
- TideHeight: 0.1-0.3
- TideSpeed: 0.05-0.1
- Enable Infinite Ocean: 0.5 (partial - for distant ocean views)
- Far Ocean Fade Distance: 0.6
- Horizon Blend: 0.7

---

## Troubleshooting

### Common Issues

1. **Foam not appearing**: Check that depth testing is enabled in your camera
2. **Waves too choppy**: Reduce wave frequency or increase smoothing
3. **Performance issues**: Reduce texture resolution or simplify normal layers

### Optimization Tips

- Use texture atlases for multiple water textures
- LOD system for distant water surfaces
- Adjust shader complexity based on distance

---

## Infinite Ocean System

The shader includes an advanced infinite ocean system that creates the illusion of endless water extending to the horizon.

### How It Works

1. **Vertex Expansion**: Water vertices are dynamically pushed toward the camera's far clip plane
2. **Distance-Based LOD**: Wave height, normal intensity, and foam effects reduce with distance
3. **Color Blending**: Smooth transition from near water colors to distant ocean colors
4. **Performance Optimization**: Reduced shader complexity for distant water areas

### Setup for Infinite Ocean

1. **Create Water Plane**: Create a simple plane (any size works, even small 10x10 units)
2. **Position at Water Level**: Place the plane at your desired water surface height
3. **Apply Water Material**: Use `OceanWater.mat` or create a new material with the water shader
4. **Enable Infinite Ocean**: Set `Enable Infinite Ocean` to 1.0 in material properties
5. **Configure Far Ocean Color**: Set a darker, atmospheric color for distant water
6. **Adjust Camera Far Clip**: Set your camera's far clip plane to desired ocean distance (1000+ recommended)
7. **Fine-tune Parameters**:
   - **Far Ocean Fade Distance**: Where transition begins (0.7 recommended)
   - **Horizon Blend**: Color transition intensity (0.8-0.9 recommended)
   - **Distance Wave Reduction**: Wave reduction at distance (0.8 recommended)

### Best Practices

- **Ocean Scenes**: Enable infinite ocean for large bodies of water
- **Lake Scenes**: Disable for enclosed water bodies  
- **Beach Scenes**: Use partial setting (0.5) for distant ocean views
- **Performance**: Far ocean reduces computational cost for distant pixels

### Infinite Ocean Troubleshooting

**Ocean doesn't extend to horizon:**
- Increase camera far clip plane distance (1000+ units)
- Ensure `Enable Infinite Ocean` is set to 1.0
- Check that water plane is positioned correctly at water level
- Verify the water material is using the `FishMMO/RealisticWater` shader

**Ocean looks flat/boring at distance:**
- Adjust `Far Ocean Color` to a more atmospheric blue-grey
- Increase `Horizon Blend` value (0.8-0.9)
- Set `Distance Wave Reduction` to 0.7-0.8 for subtle distant waves

**Twitchy/jittery shoreline:**
- Increase `Foam Smoothness` (0.3-0.6) for smoother foam transitions
- Adjust `Shoreline Smoothing` (0.2-0.4) to reduce shoreline jitter
- Lower `Foam Cutoff` value for more gradual foam appearance
- Reduce `Wave Frequency` if waves are too chaotic near shore

**Performance issues with infinite ocean:**
- Lower `Distance Wave Reduction` to reduce distant wave calculations
- Reduce texture resolution for normal maps
- Consider using simpler lighting for distant water

**Ocean has hard edges:**
- Increase `Far Ocean Fade Distance` for smoother transition
- Adjust `Horizon Blend` for gradual color blending
- Ensure camera far clip plane is sufficient

### Technical Details

The infinite ocean system uses the camera's far clip plane distance to:
- Calculate normalized distance from camera to water surface
- Progressively reduce wave animation complexity
- Blend to atmospheric far ocean colors
- Maintain visual quality while optimizing performance

---

## Advanced Features

### Caustics

Add caustic light patterns using animated texture projection:
1. Create caustic texture
2. Project from light direction
3. Animate with wave motion
4. Add to surface emission

### Underwater Effects

Extend the shader for underwater rendering:
1. Depth-based fog calculation
2. Light scattering simulation
3. Particle integration

### Weather Integration

Dynamic weather effects:
1. Rain ripple simulation
2. Storm wave intensification
3. Wind direction influence

---

## File Structure

```
Assets/Plugins/FishMMO Water/
├── README.md (comprehensive tutorial)
├── QUICKSTART.md (quick setup guide)
├── Shaders/
│   ├── RealisticWaterShader.shader (complete HLSL shader)
│   └── RealisticWaterShader.shadergraph (ShaderGraph tutorial)
├── Materials/
│   ├── OceanWater.mat (deep ocean preset)
│   ├── LakeWater.mat (calm lake preset)
│   └── BeachWater.mat (shoreline preset)
└── Textures/
    └── README.md (texture requirements guide)
```

---

## Credits and References

This shader tutorial is designed for FishMMO and demonstrates advanced water rendering techniques suitable for MMO environments.

For additional resources and updates, check the project documentation.