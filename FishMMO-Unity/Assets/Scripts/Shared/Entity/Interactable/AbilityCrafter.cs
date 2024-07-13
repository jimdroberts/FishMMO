using UnityEngine;

namespace FishMMO.Shared
{
	[RequireComponent(typeof(SceneObjectNamer))]
	public class AbilityCrafter : Interactable
	{
		public override string Title { get { return "Ability Crafter"; } }
		public override Color TitleColor { get { return TinyColor.ToUnityColor(TinyColor.lavender); } }
	}
}