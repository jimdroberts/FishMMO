using System;
using UnityEngine;

namespace FishMMO.Shared
{
	[Serializable]
	public class SceneTeleporterDetails
	{
		internal string From;
		public string ToScene;
		public Vector3 ToPosition;
		public Quaternion ToRotation;
	}
}