using System;
using UnityEngine;

namespace FishMMO.Shared
{
	[Serializable]
	public class FogSettings
	{
		public bool Enabled = false;
		public float ChangeRate = 0.0f;
		public FogMode Mode = FogMode.Exponential;
		public Color Color = Color.gray;
		public float Density = 0.0f;
		public float StartDistance = 0.0f;
		public float EndDistance = 0.0f;
	}
}