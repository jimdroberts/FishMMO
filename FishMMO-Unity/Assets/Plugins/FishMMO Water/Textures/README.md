# Texture Requirements for FishMMO Water Shader

This folder should contain the following textures for optimal water rendering:

## Required Textures

### WaterNormal.png
- **Type**: Normal Map
- **Size**: 512x512 or 1024x1024
- **Format**: BC5 (Normal map compression)
- **Description**: Tileable water surface normal map for realistic wave detail
- **Source**: Can be generated using tools like:
  - Substance Designer
  - Blender (Ocean modifier baked to normal map)
  - Online normal map generators
  - Unity's built-in water normal maps

### FoamNoise.png
- **Type**: Grayscale Noise Texture
- **Size**: 256x256 or 512x512
- **Format**: BC4 (Single channel compression)
- **Description**: Noise pattern for foam generation and variation
- **Characteristics**: 
  - High contrast
  - Good variation across the texture
  - Tileable seamlessly

### WaveNormal.png
- **Type**: Normal Map (Secondary Layer)
- **Size**: 512x512 or 1024x1024
- **Format**: BC5 (Normal map compression)
- **Description**: Secondary normal map for additional wave detail
- **Usage**: Animates in different direction/speed than primary normal

## Optional Textures

### CausticPattern.png
- **Type**: RGB Texture
- **Size**: 512x512
- **Format**: BC1 or BC7
- **Description**: Caustic light patterns for underwater lighting effects

### FoamPattern.png
- **Type**: Alpha Texture
- **Size**: 256x256
- **Format**: BC4
- **Description**: Specific foam shape patterns for enhanced foam rendering

## Texture Import Settings

For all normal maps:
```
Texture Type: Normal map
Filter Mode: Trilinear
Wrap Mode: Repeat
Generate Mip Maps: True
```

For noise/pattern textures:
```
Texture Type: Default
sRGB: False (for data textures)
Filter Mode: Trilinear
Wrap Mode: Repeat
Generate Mip Maps: True
```

## Creating Your Own Textures

### Water Normal Maps
1. Use Blender's Ocean modifier
2. Set appropriate wave scale and foam
3. Bake normal map from displaced mesh
4. Export as 16-bit PNG

### Foam Noise
1. Use Photoshop's Clouds filter
2. Apply high contrast
3. Add Gaussian blur for smoothness
4. Ensure tileable edges

### Alternative Sources
- Unity Asset Store (free water texture packs)
- Substance Share (community materials)
- Quixel Megascans (with subscription)
- Hand-painted textures for stylized look

## Performance Notes

- Use compressed formats appropriate for your platform
- Consider texture streaming for large textures
- Use lower resolution textures for distant water LODs
- Combine multiple textures into texture atlases when possible