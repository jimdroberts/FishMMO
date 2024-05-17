using UnityEngine;

namespace FishMMO.Shared
{
	public interface ISceneObject
	{
		public long ID { get; set; }
		public GameObject GameObject { get; }
	}
}