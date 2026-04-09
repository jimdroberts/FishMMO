# Quick Setup Guide

## Getting Started with FishMMO Water Shader

### 1. Import Process
After extracting this package to your project:

1. Navigate to `Assets/Plugins/FishMMO Water/`
2. The shader `RealisticWaterShader.shader` is ready to use
3. Choose from pre-configured materials or create your own

### 2. Create Your First Water Material
1. Right-click in `Materials/` folder
2. Create → Material
3. Assign the `FishMMO/RealisticWater` shader to the material
4. Adjust parameters as needed

### 3. Quick Parameters for Different Water Types

**Ocean/Sea Water:**
- Wave Height: 0.2-0.4
- Wave Speed: 1.0-1.5
- Foam Distance: 1.5-2.5
- Transparency: 0.7-0.8
- Enable Infinite Ocean: 1.0 ✨
- Far Ocean Fade Distance: 0.7
- Horizon Blend: 0.8-0.9

**Lake/Pond Water:**
- Wave Height: 0.02-0.08
- Wave Speed: 0.2-0.5
- Foam Distance: 0.5-1.0
- Transparency: 0.85-0.95
- Enable Infinite Ocean: 0.0 (disabled)

**Beach/Shoreline Water:**
- Wave Height: 0.05-0.15
- Wave Speed: 0.8-1.2
- Wave Frequency: 2.0-4.0
- Foam Distance: 1.0-3.0
- Enable Infinite Ocean: 0.5 (partial)
- Tide Height: 0.1-0.3

### 4. Infinite Ocean Setup ✨

**Option A: Automatic Setup (Recommended)**
1. Create empty GameObject in your scene
2. Add the `InfiniteOceanSetup` script component
3. Assign the `OceanWater` material to the Ocean Material field
4. Set your desired water level (Y position)
5. Set target camera and far clip distance (2000+ recommended)
6. Click "Setup Infinite Ocean" button or play the scene

**Option B: Manual Setup**
1. Create a plane at your water level (any size works)
2. Apply ocean material with `FishMMO/RealisticWater` shader
3. Set "Enable Infinite Ocean" to 1.0
4. Configure camera far clip plane (1000+ units)
5. Adjust infinite ocean parameters:
   - **Far Ocean Color**: Darker atmospheric blue
   - **Far Ocean Fade Distance**: 0.7
   - **Horizon Blend**: 0.9

### 5. Adding Textures
Place your water textures in the `Textures/` folder and assign them:
- Normal Map → Primary water surface detail
- Secondary Normal Map → Additional wave complexity  
- Foam Noise → Foam pattern variation

### 6. Performance Tips
- Use infinite ocean for large water bodies
- Disable infinite ocean for small lakes/ponds
- Adjust texture resolution based on view distance
- Consider using simplified materials for reflections
- Enable GPU instancing for multiple water bodies

### 7. Troubleshooting
**Foam not visible:** Enable depth testing on your camera
**Waves too intense:** Reduce Wave Height and increase Wave Frequency  
**Performance issues:** Lower texture resolution or disable infinite ocean
**Infinite ocean not working:** Ensure water plane is large enough
**Blocky distant water:** Increase "Distance Wave Reduction" value

### 8. Pre-made Materials

Use the included materials as starting points:
- **OceanWater.mat**: Large ocean with infinite horizon
- **LakeWater.mat**: Calm enclosed water body
- **BeachWater.mat**: Shoreline water with enhanced foam

For detailed instructions and advanced features, see the main README.md file.