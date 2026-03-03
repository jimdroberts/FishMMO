using System;
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
		private readonly Dictionary<Type, ICharacterBehaviour> behaviours = new Dictionary<Type, ICharacterBehaviour>();

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
		public bool IsSpawned => true;

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

			// Snapshot the attribute controller if the live character has one.
			if (liveCharacter.TryGet(out ICharacterAttributeController liveAttributes))
			{
				SnapshotAttributeController snapshotAttributes = new SnapshotAttributeController(liveAttributes, this);
				RegisterCharacterBehaviour(snapshotAttributes);
			}
		}

		/// <inheritdoc/>
		public void RegisterCharacterBehaviour(ICharacterBehaviour behaviour)
		{
			if (behaviour == null)
			{
				return;
			}

			Type[] interfaces = behaviour.GetType().GetInterfaces();
			for (int i = 0; i < interfaces.Length; ++i)
			{
				Type iface = interfaces[i];
				if (iface == typeof(ICharacterBehaviour))
				{
					continue;
				}
				if (!typeof(ICharacterBehaviour).IsAssignableFrom(iface))
				{
					continue;
				}
				if (!behaviours.ContainsKey(iface))
				{
					behaviours.Add(iface, behaviour);
				}
			}
		}

		/// <inheritdoc/>
		public void UnregisterCharacterBehaviour(ICharacterBehaviour behaviour)
		{
			if (behaviour == null)
			{
				return;
			}

			Type[] interfaces = behaviour.GetType().GetInterfaces();
			for (int i = 0; i < interfaces.Length; ++i)
			{
				Type iface = interfaces[i];
				if (behaviours.TryGetValue(iface, out ICharacterBehaviour existing) && existing == behaviour)
				{
					behaviours.Remove(iface);
				}
			}
		}

		/// <inheritdoc/>
		public bool TryGet<T>(out T control) where T : class, ICharacterBehaviour
		{
			Type type = typeof(T);
			if (behaviours.TryGetValue(type, out ICharacterBehaviour result))
			{
				control = result as T;
				if (control != null)
				{
					return true;
				}
			}
			control = null;
			return false;
		}
	}
}