#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace FishMMO.Shared.WorldDesign
{
	/// <summary>
	/// Keeps the boundary glass material in the client's addressables, so client builds carry its
	/// shader and the client can find it by name.
	/// </summary>
	/// <remarks>
	/// The client makes its panes from <c>Shader.Find</c> at runtime, and a build includes no
	/// shader just because code names it. A material in <see cref="WorldEditorAssets.ClientStaticGroup"/>
	/// is what pulls the shader into client bundles, and the "Client" group never reaches a
	/// server build. The same arrangement the weather presenters use.
	/// </remarks>
	public static class BoundaryGlassAssets
	{
		public const string ShaderName = "FishMMO/Boundary Glass";
		public const string MaterialPath = "Assets/Prefabs/Client/Materials/Boundary/Boundary Glass.mat";

		/// <summary>Creates the material if it is missing and registers it. Null if the shader is missing.</summary>
		public static Material Ensure()
		{
			Material material = AssetDatabase.LoadAssetAtPath<Material>(MaterialPath);
			if (material == null)
			{
				Shader shader = Shader.Find(ShaderName);
				if (shader == null)
				{
					Debug.LogError($"[Boundary glass] Shader '{ShaderName}' was not found, so client builds will show no boundary glass.");
					return null;
				}
				WorldEditorAssets.EnsureFolder(System.IO.Path.GetDirectoryName(MaterialPath).Replace('\\', '/'));
				material = new Material(shader) { name = "Boundary Glass" };
				AssetDatabase.CreateAsset(material, MaterialPath);
			}
			WorldEditorAssets.RegisterAddressable(material, WorldEditorAssets.ClientStaticGroup);
			return material;
		}
	}
}
#endif
