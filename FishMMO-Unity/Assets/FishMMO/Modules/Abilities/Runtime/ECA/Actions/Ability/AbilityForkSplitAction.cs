using System;
using UnityEngine;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Action that splits an ability object into several new ones where it strikes something: a fork. The bolts fan out
	/// evenly about the old heading, each strikes once, none forks again, and none can strike the body the split came off.
	/// </summary>
	/// <remarks>
	/// <para>
	/// Put it on an OnHit event with no target selector (it acts on the object, once per hit), after whatever the hit does
	/// to its victim. The split itself is <see cref="AbilityObject.SpawnForks"/>: deterministic ids and poses, so the
	/// server, the caster's client and every observer (which runs the hit chain from the server's hit message) hold the
	/// same forks, and a hit run twice forks once.
	/// </para>
	/// <para>
	/// Not the same as <see cref="AbilityRicochetHitAction"/>, which turns the SAME object onto a new heading (a ricochet)
	/// and spawns nothing (renamed from the old fork action).
	/// </para>
	/// </remarks>
	[Serializable]
	public class AbilityForkSplitAction : BaseAction
	{
		/// <summary>How many bolts the object splits into (1 to <see cref="AbilityObject.MaxForks"/>).</summary>
		[Tooltip("How many bolts the object splits into where it strikes (1-8).")]
		[SerializeReference, SubclassSelector]
		public IIntValueProvider CountValue = new ConstantValue { Amount = 2 };

		/// <summary>The total fan, in degrees, the bolts spread across about the old heading.</summary>
		[Tooltip("Total fan in degrees the bolts spread across about the old heading. 60 means 30 either side.")]
		[SerializeReference, SubclassSelector]
		public IFloatValueProvider SpreadValue = new ConstantFloatValue { Amount = 60f };

		/// <summary>Split only off a character: a bolt that strikes a wall just ends.</summary>
		[Tooltip("Split only where the object strikes a character; a bolt that strikes scenery just ends.")]
		public bool OnlyOnCharacters = true;

		/// <inheritdoc />
		public override void Execute(ICharacter initiator, EventData eventData)
		{
			if (eventData == null || !eventData.TryGet(out AbilityCollisionEventData collision))
			{
				return;
			}
			AbilityObject abilityObject = collision.AbilityObject;
			if (abilityObject == null)
			{
				return;
			}
			int count = CountValue != null ? CountValue.GetValue(initiator, eventData) : 2;
			float spread = SpreadValue != null ? SpreadValue.GetValue(initiator, eventData) : 60f;
			GameObject struck = eventData.TargetCharacter != null ? eventData.TargetCharacter.GameObject : null;
			if (OnlyOnCharacters && struck == null)
			{
				return;
			}
			abilityObject.SpawnForks(count, spread, struck);
		}
	}
}
