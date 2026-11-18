using System.Runtime.InteropServices;
using UnityEngine;

namespace FishMMO.Client
{
	/// <summary>Optional features of Unity's actual WebGPU device, rather than its adapter.</summary>
	public static class WebGpuDrawCapabilities
	{
		private static bool initialized, indirectFirstInstance;
		public const string FirstInstanceKeyword = "FISH_INDIRECT_FIRST_INSTANCE";

		[RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
		private static void Reset()
        {
            initialized = indirectFirstInstance = false;
            Shader.DisableKeyword(FirstInstanceKeyword);
        }

		public static bool IndirectFirstInstance
		{
			get
			{
				if (!initialized)
				{
#if UNITY_WEBGL && !UNITY_EDITOR
					indirectFirstInstance = FishMMOWebGpuHasIndirectFirstInstance() != 0;
#endif
					if (indirectFirstInstance) Shader.EnableKeyword(FirstInstanceKeyword);
                    else Shader.DisableKeyword(FirstInstanceKeyword);
					initialized = true;
				}
				return indirectFirstInstance;
			}
		}

#if UNITY_WEBGL && !UNITY_EDITOR
		[DllImport("__Internal")]
		private static extern int FishMMOWebGpuHasIndirectFirstInstance();
#endif
	}
}
