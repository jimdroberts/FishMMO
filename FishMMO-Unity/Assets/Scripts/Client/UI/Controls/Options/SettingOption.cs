using UnityEngine;

namespace FishMMO.Client
{
	public abstract class SettingOption
	{
		public abstract void Initialize(RectTransform transform);
		public abstract void Load();
		public abstract void Save();
	}
}