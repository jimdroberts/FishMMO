using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// ScriptableObject cache for storing a list of character names.
	/// </summary>
	[CreateAssetMenu(fileName = "New Name Cache", menuName = "FishMMO/Character/Name Cache", order = 1)]
	public class NameCache : CachedScriptableObject<NameCache>, ICachedObject
	{
		/// <summary>
		/// List of available character names.
		/// </summary>
		public List<string> Names;
	}
}