using UnityEngine;
using System.Collections.Generic;

namespace FishMMO.Shared
{
	/// <summary>
	/// Controls the archetype state for a character, providing access to the current archetype template.
	/// </summary>
	public class ArchetypeController : CharacterBehaviour, IArchetypeController
	{
		/// <summary>
		/// The archetype template currently assigned to this character.
		/// </summary>
		public ArchetypeTemplate Template { get; }
	}
}