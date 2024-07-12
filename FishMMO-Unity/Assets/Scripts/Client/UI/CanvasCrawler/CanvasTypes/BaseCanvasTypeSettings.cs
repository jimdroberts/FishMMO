using FishMMO.Shared;
using UnityEngine;

namespace FishMMO.Client
{
	public abstract class BaseCanvasTypeSettings
	{
		public abstract void ApplySettings(object component, Configuration configuration);

		public Color ParseColor(string name, Configuration configuration)
		{
			if (!configuration.TryGet($"{name}ColorR", out string colorR) ||
				!byte.TryParse(colorR, out byte R))
			{
				R = 0;
			}
			if (!configuration.TryGet($"{name}ColorG", out string colorG) ||
				!byte.TryParse(colorG, out byte G))
			{
				G = 0;
			}
			if (!configuration.TryGet($"{name}ColorB", out string colorB) ||
				!byte.TryParse(colorB, out byte B))
			{
				B = 0;
			}
			if (!configuration.TryGet($"{name}ColorA", out string colorA) ||
				!byte.TryParse(colorA, out byte A))
			{
				A = 0;
			}
			return TinyColor.ToUnityColor(new TinyColor(R, G, B, A));
		}
	}
}