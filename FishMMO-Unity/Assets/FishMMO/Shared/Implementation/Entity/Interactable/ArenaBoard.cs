using System.Collections.Generic;
using FishMMO.Shared.Core;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// A board players interact with to queue for arenas. See <see cref="IArenaBoard"/>.
	/// </summary>
	/// <remarks>
	/// Authored like a dungeon entrance: a prefab with this component and an interaction whose
	/// action is <see cref="SendArenaBoardBroadcastAction"/>. The arenas it offers are the
	/// templates listed here; the server refuses a queue request for any other.
	/// </remarks>
	public class ArenaBoard : Interactable, IArenaBoard
	{
		private string title = "Arena";

		[Tooltip("Arenas this board offers, in the order the panel lists them.")]
		public List<ArenaTemplate> Arenas = new List<ArenaTemplate>();

		private int[] templateIDs;

		public override string Title { get { return title; } }

		IReadOnlyList<int> IArenaBoard.ArenaTemplateIDs => ResolveTemplateIDs();

		bool IArenaBoard.Offers(int arenaTemplateID)
		{
			int[] ids = ResolveTemplateIDs();
			for (int i = 0; i < ids.Length; ++i)
			{
				if (ids[i] == arenaTemplateID)
				{
					return true;
				}
			}
			return false;
		}

		/// <summary>
		/// Template IDs, computed once. The list is authored data and does not change at runtime.
		/// </summary>
		private int[] ResolveTemplateIDs()
		{
			if (templateIDs != null)
			{
				return templateIDs;
			}

			var ids = new List<int>(Arenas?.Count ?? 0);
			if (Arenas != null)
			{
				foreach (ArenaTemplate template in Arenas)
				{
					if (template != null && template.ID != 0 && !ids.Contains(template.ID))
					{
						ids.Add(template.ID);
					}
				}
			}
			templateIDs = ids.ToArray();
			return templateIDs;
		}
	}
}
