using UnityEngine;

namespace FishMMO.Client
{
	public abstract class SettingOption : MonoBehaviour
	{
		public abstract void Initialize();
		public abstract void Load();
		public abstract void Save();
	}
}