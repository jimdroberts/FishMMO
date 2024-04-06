using UnityEngine;

namespace FishMMO.Shared
{
	[RequireComponent(typeof(SceneObjectNamer))]
	public class Banker : Interactable
	{
		public override string Title { get { return "Banker"; } }
	}
}