using System;

namespace MeshyAI
{
	/// <summary>
	/// Contains URLs for downloading generated texture maps for a 3D model.
	/// </summary>
	[Serializable]
	public class MeshyTextureUrlsObject
	{
		/// <summary>URL to the base color (albedo) texture.</summary>
		public string base_color;
		/// <summary>URL to the metallic texture.</summary>
		public string metallic;
		/// <summary>URL to the normal map texture.</summary>
		public string normal;
		/// <summary>URL to the roughness texture.</summary>
		public string roughness;
	}
}