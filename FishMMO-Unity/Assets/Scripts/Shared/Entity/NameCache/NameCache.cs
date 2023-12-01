using System.Collections.Generic;
using UnityEngine;

namespace FishMMO.Shared
{
	[CreateAssetMenu(fileName = "New Name Cache", menuName = "Character/Name Cache", order = 1)]
	public class NameCache : CachedScriptableObject<NameCache>, ICachedObject
	{
		public List<string> Names;
	}
}