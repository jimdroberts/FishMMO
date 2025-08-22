#if UNITY_EDITOR
using System;

namespace MeshyAI
{
	/// <summary>
	/// Contains URLs for downloading generated 3D model files in various formats.
	/// </summary>
	[Serializable]
	public class MeshyModelUrls
	{
		/// <summary>URL to the OBJ file.</summary>
		public string obj;
		/// <summary>URL to the GLB file.</summary>
		public string glb;
		/// <summary>URL to the FBX file.</summary>
		public string fbx;
		/// <summary>URL to the USDZ file.</summary>
		public string usdz;
		/// <summary>URL to the MTL file (material for OBJ).</summary>
		public string mtl;
	}
}
#endif