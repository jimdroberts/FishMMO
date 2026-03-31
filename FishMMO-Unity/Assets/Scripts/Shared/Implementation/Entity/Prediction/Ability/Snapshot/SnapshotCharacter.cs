using System.Collections.Generic;
using UnityEngine;
using FishNet.Connection;
using FishNet.Object;
using FishNet.Managing.Predicting;
#if !UNITY_SERVER
using TMPro;
#endif
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Lightweight phantom <see cref="ICharacter"/> implementation that preserves a frozen
	/// snapshot of character identity and attribute data. Created when a caster disconnects
	/// so that detached <see cref="AbilityObject"/>s can continue to resolve stat-scaled
	/// calculations via <see cref="StatScaledValue"/> and <see cref="StatScaledFloatValue"/>
	/// without a live networked character.
	/// <para>
	/// Only <see cref="TryGet{T}"/> for <see cref="ICharacterAttributeController"/> is supported.
	/// All other behaviour lookups return <c>false</c>, causing downstream systems
	/// (achievements, factions, etc.) to gracefully degrade.
	/// </para>
	/// </summary>
	public sealed class SnapshotCharacter : ICharacter
	{
		private ICharacterAttributeController attributeController;

		/// <inheritdoc/>
		public long ID { get; set; }

		/// <inheritdoc/>
		public string Name { get; }

		/// <inheritdoc/>
		public Transform Transform { get; }

		/// <inheritdoc/>
		public GameObject GameObject => Transform != null ? Transform.gameObject : null;

		/// <inheritdoc/>
		public Collider Collider { get; set; }

		/// <inheritdoc/>
		public NetworkConnection Owner => null;

		/// <inheritdoc/>
		public NetworkObject NetworkObject => null;

		/// <inheritdoc/>
		public PredictionManager PredictionManager => null;

		/// <inheritdoc/>
		public HashSet<NetworkConnection> Observers => null;

		/// <inheritdoc/>
		public bool IsTeleporting => false;

		/// <summary>
		/// Always returns <c>true</c> so that <see cref="AbilityObject.Update"/> and
		/// <see cref="AbilityObject.OnCollisionEnter"/> continue to dispatch ECA events
		/// through the phantom caster.
		/// </summary>
		/// <remarks>
		/// CONTRACT: This MUST return <c>true</c>. Both <see cref="AbilityObject.Update"/>
		/// and <see cref="AbilityObject.OnCollisionEnter"/> guard event dispatch with
		/// <c>Caster != null &amp;&amp; Caster.IsSpawned</c>. Returning <c>false</c>
		/// would silently suppress all tick and collision events on every detached
		/// ability object for the remainder of its lifetime.
		/// </remarks>
		public bool IsSpawned => true;

		/// <inheritdoc/>
		public List<Trigger> OnDamageTriggers => null;
		/// <inheritdoc/>
		public List<Trigger> OnDamagedTriggers => null;
		/// <inheritdoc/>
		public List<Trigger> OnHealTriggers => null;
		/// <inheritdoc/>
		public List<Trigger> OnHealedTriggers => null;
		/// <inheritdoc/>
		public List<Trigger> OnKillTriggers => null;
		/// <inheritdoc/>
		public List<Trigger> OnKilledTriggers => null;
		/// <inheritdoc/>
		public List<Trigger> OnResurrectTriggers => null;
		/// <inheritdoc/>
		public List<Trigger> OnResurrectedTriggers => null;

		/// <inheritdoc/>
		public int Flags { get; set; }

		/// <inheritdoc/>
		public void EnableFlags(CharacterFlags flags)
		{
			int f = Flags;
			f.EnableBit(flags);
			Flags = f;
		}

		/// <inheritdoc/>
		public void DisableFlags(CharacterFlags flags)
		{
			int f = Flags;
			f.DisableBit(flags);
			Flags = f;
		}

		/// <inheritdoc/>
		public bool IsFlagged(CharacterFlags flags)
		{
			return Flags.IsFlagged(flags);
		}

#if !UNITY_SERVER
		/// <inheritdoc/>
		public Transform MeshRoot => null;

		/// <inheritdoc/>
		public TextMeshPro CharacterNameLabel { get; set; }

		/// <inheritdoc/>
		public TextMeshPro CharacterGuildLabel { get; set; }

		/// <inheritdoc/>
		public void InstantiateRaceModelFromIndex(RaceTemplate raceTemplate, int modelIndex) { }
#endif

		/// <summary>
		/// Creates a phantom character snapshot from a live <see cref="ICharacter"/>.
		/// Copies identity data and snapshots all character attributes.
		/// </summary>
		/// <param name="liveCharacter">The live character to snapshot. Must not be null.</param>
		/// <param name="abilityObjectTransform">
		/// The transform to use as the phantom's <see cref="Transform"/> reference.
		/// Typically the <see cref="AbilityObject.Transform"/> so positional queries resolve
		/// to the projectile's location rather than a stale character position.
		/// </param>
		public SnapshotCharacter(ICharacter liveCharacter, Transform abilityObjectTransform)
		{
			ID = liveCharacter.ID;
			Name = liveCharacter.Name;
			Transform = abilityObjectTransform;
			Flags = liveCharacter.Flags;

			// Register the attribute controller directly by its known interface type.
			// SnapshotCharacter only ever needs ICharacterAttributeController; using
			// typeof(T) is resolved at compile time and is safe under IL2CPP code stripping.
			if (liveCharacter.TryGet(out ICharacterAttributeController liveAttributes))
			{
				SnapshotAttributeController snapshotAttributes = new SnapshotAttributeController(liveAttributes, this);
				attributeController = snapshotAttributes;
			}
		}

		/// <inheritdoc/>
		/// <remarks>
		/// <see cref="SnapshotCharacter"/> only supports <see cref="ICharacterAttributeController"/>.
		/// Registering other behaviour types is a no-op. This avoids <c>GetInterfaces()</c>
		/// reflection which is fragile under IL2CPP code stripping.
		/// </remarks>
		public void RegisterCharacterBehaviour(ICharacterBehaviour behaviour)
		{
			if (behaviour is ICharacterAttributeController attributeController)
			{
				this.attributeController = attributeController;
			}
		}

		/// <inheritdoc/>
		/// <remarks>
		/// <see cref="SnapshotCharacter"/> only supports <see cref="ICharacterAttributeController"/>.
		/// Unregistering other behaviour types is a no-op.
		/// </remarks>
		public void UnregisterCharacterBehaviour(ICharacterBehaviour behaviour)
		{
			if (ReferenceEquals(attributeController, behaviour))
			{
				attributeController = null;
			}
		}

		/// <inheritdoc/>
		public bool TryGet<T>(out T control) where T : class, ICharacterBehaviour
		{
			if (typeof(T) == typeof(ICharacterAttributeController) && attributeController != null)
			{
				control = attributeController as T;
				if (control != null)
				{
					return true;
				}
			}
			control = null;
			return false;
		}

		/// <inheritdoc/>
		public void Invoke(List<Trigger> triggers, EventData eventData) { }

		/// <summary>
		/// Creates a <see cref="SnapshotCharacter"/> from a live caster, freezing identity
		/// and attribute data so that detached ability objects can continue to resolve
		/// stat-scaled calculations.
		/// </summary>
		/// <param name="liveCaster">The live character to snapshot.</param>
		/// <param name="abilityObjectTransform">The ability object's transform, used as the phantom's positional reference.</param>
		/// <returns>A new <see cref="SnapshotCharacter"/> or null if <paramref name="liveCaster"/> is null.</returns>
		public static SnapshotCharacter FromLive(ICharacter liveCaster, Transform abilityObjectTransform)
		{
			if (liveCaster == null)
			{
				return null;
			}
			return new SnapshotCharacter(liveCaster, abilityObjectTransform);
		}
	}
}