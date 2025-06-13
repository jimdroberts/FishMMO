using UnityEngine;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	public class ArchetypeController : CharacterBehaviour, IArchetypeController
	{
		public ArchetypeTemplate Template { get; }
	}
}