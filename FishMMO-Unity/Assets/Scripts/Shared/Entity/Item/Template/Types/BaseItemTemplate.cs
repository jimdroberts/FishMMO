using UnityEngine;

namespace FishMMO.Shared
{
	public abstract class BaseItemTemplate : CachedScriptableObject<BaseItemTemplate>, ICachedObject
	{
		public bool IsIdentifiable;
		public uint MaxStackSize = 1;
		public float Price;
		//use this for item generation
		public int[] IconPools;
		public Texture2D Icon;

		public string Name { get { return this.name; } }
		public bool IsStackable { get { return MaxStackSize > 1; } }
	}
}