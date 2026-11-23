using System;
using System.Collections.Generic;

namespace FishMMO.Shared.Core
{
	/// <summary>
	/// What the core character load and save read and write of a character's abilities (abilities are the
	/// Abilities module's). The save pipeline stays in the core with the character's other rows, under the same claims and
	/// versions; the module's controller implements this.
	/// </summary>
	public interface IAbilityPersistence : ICharacterBehaviour
	{
		/// <summary>Restores a crafted ability as persisted (nothing dirty).</summary>
		void RestoreAbility(long abilityID, int templateID, List<int> abilityEventIDs, long version, float remainingCooldown);

		/// <summary>
		/// Restores the known ability templates and ability events by template ID (nothing dirty). IDs this build has not
		/// loaded are added to <paramref name="unknown"/>; they stay in the database.
		/// </summary>
		void RestoreKnowledge(IReadOnlyList<int> templateIDs, List<int> unknown);

		/// <summary>
		/// Emits each ability whose state changed since it was last written, bumping its version first: (ability ID, version,
		/// template ID, ability event IDs).
		/// </summary>
		void CaptureDirtyAbilities(Action<long, long, int, List<int>> emit);

		/// <summary>Records that ability <paramref name="abilityID"/> was written at <paramref name="version"/>.</summary>
		void MarkAbilityPersisted(long abilityID, long version);

		/// <summary>True while known templates or events changed since they were last written.</summary>
		bool KnowledgeDirty { get; set; }

		/// <summary>Moves on every change to the knowledge; a write clears the mark only if it has not moved since.</summary>
		long KnowledgeVersion { get; }

		/// <summary>The known ability templates' IDs.</summary>
		HashSet<int> KnownBaseAbilities { get; }

		/// <summary>The known ability events' IDs.</summary>
		HashSet<int> KnownAbilityEvents { get; }
	}
}
