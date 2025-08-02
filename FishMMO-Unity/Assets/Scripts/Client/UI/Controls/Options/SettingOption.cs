using UnityEngine;

namespace FishMMO.Client
{
	public abstract class SettingOption : MonoBehaviour
	{
		/// <summary>
		/// Initializes the setting option, preparing it for use.
		/// </summary>
		public abstract void Initialize();
		/// <summary>
		/// Loads the setting value from configuration or persistent storage.
		/// </summary>
		public abstract void Load();
		/// <summary>
		/// Saves the setting value to configuration or persistent storage.
		/// </summary>
		public abstract void Save();
	}
}