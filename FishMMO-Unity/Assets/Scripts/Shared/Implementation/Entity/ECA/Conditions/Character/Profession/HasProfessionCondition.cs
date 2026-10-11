using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// ECA condition placeholder: the character practises a profession. ALWAYS FALSE today.
	/// </summary>
	/// <remarks>
	/// <para>FishMMO has no profession system yet (Jim, 2026-10-10: portals take a profession
	/// condition as a placeholder; there is no Archaeology or any other profession). The condition
	/// exists so portals and other content can be authored against a profession key now and start
	/// working the day professions land, without re-authoring.</para>
	/// <para>Because portal requirements are any-of, a portal whose only requirement is this
	/// condition cannot be opened by it — give such a portal a key or another condition too.</para>
	/// <para><b>When professions exist:</b> resolve the character's profession record here, compare
	/// <see cref="ProfessionKey"/> (case-insensitive) and <see cref="MinimumRank"/>, and delete the
	/// warning.</para>
	/// </remarks>
	[Serializable]
	public class HasProfessionCondition : BaseCondition
	{
		[Tooltip("The profession's key, e.g. \"archaeology\". Placeholder: no profession system exists yet, so this condition is always false.")]
		public string ProfessionKey;

		[Tooltip("The rank the character must have reached in it. Placeholder.")]
		[Min(0)]
		public int MinimumRank;

		/// <inheritdoc />
		public override bool Evaluate(ICharacter initiator, EventData eventData = null)
		{
			// No profession system exists; nothing can satisfy this yet. See remarks.
			return false;
		}

		/// <inheritdoc />
		public override string GetTooltipContribution()
		{
			return string.IsNullOrWhiteSpace(ProfessionKey) ? null : $"Requires the {ProfessionKey} profession";
		}
	}
}
