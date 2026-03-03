using System.Collections.Generic;
using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Lightweight read-only <see cref="ICharacterAttributeController"/> backed by frozen
	/// <see cref="CharacterAttribute"/> instances. Used by <see cref="SnapshotCharacter"/>
	/// to satisfy <see cref="StatScaledValue"/> and <see cref="StatScaledFloatValue"/> lookups
	/// for detached ability objects whose caster has disconnected.
	/// <para>
	/// Only <see cref="TryGetAttribute(int, out CharacterAttribute)"/> is functional.
	/// All mutating methods are no-ops.
	/// </para>
	/// </summary>
	public sealed class SnapshotAttributeController : ICharacterAttributeController
	{
		/// <inheritdoc/>
		public ICharacter Character { get; private set; }

		/// <inheritdoc/>
		public bool Initialized => true;

		/// <inheritdoc/>
		public Dictionary<int, CharacterAttribute> Attributes { get; }

		/// <inheritdoc/>
		public Dictionary<int, CharacterResourceAttribute> ResourceAttributes { get; }

		/// <summary>
		/// Creates a snapshot attribute controller by deep-copying the final values
		/// from a live <see cref="ICharacterAttributeController"/>.
		/// Each attribute is re-created with <c>value = original.FinalValue</c> and
		/// <c>modifier = 0</c> so that <see cref="CharacterAttribute.FinalValue"/> returns
		/// the same number as the original at the time of snapshotting.
		/// </summary>
		/// <param name="live">The live attribute controller to snapshot.</param>
		/// <param name="owner">The <see cref="SnapshotCharacter"/> that owns this controller.</param>
		public SnapshotAttributeController(ICharacterAttributeController live, ICharacter owner)
		{
			Character = owner;
			Attributes = new Dictionary<int, CharacterAttribute>();
			ResourceAttributes = new Dictionary<int, CharacterResourceAttribute>();

			if (live?.Attributes != null)
			{
				foreach (KeyValuePair<int, CharacterAttribute> kvp in live.Attributes)
				{
					CharacterAttribute original = kvp.Value;
					// Construct with value = FinalValue and modifier = 0 so CalculateFinalValue
					// returns the exact frozen value. FormulaModifier starts at 0 in the constructor.
					CharacterAttribute frozen = new CharacterAttribute(this, original.Template.ID, original.FinalValue, 0);
					Attributes[kvp.Key] = frozen;
				}
			}

			// Snapshot resource attributes as regular attributes (frozen FinalValue only).
			// We avoid constructing CharacterResourceAttribute because its ClampCurrentValue
			// accesses Character.Flags which is not meaningful for a snapshot.
			if (live?.ResourceAttributes != null)
			{
				foreach (KeyValuePair<int, CharacterResourceAttribute> kvp in live.ResourceAttributes)
				{
					CharacterResourceAttribute original = kvp.Value;
					CharacterAttribute frozen = new CharacterAttribute(this, original.Template.ID, original.FinalValue, 0);
					Attributes[kvp.Key] = frozen;
				}
			}
		}

		/// <inheritdoc/>
		public bool TryGetAttribute(CharacterAttributeTemplate template, out CharacterAttribute attribute)
		{
			if (template != null)
			{
				return Attributes.TryGetValue(template.ID, out attribute);
			}
			attribute = null;
			return false;
		}

		/// <inheritdoc/>
		public bool TryGetAttribute(int id, out CharacterAttribute attribute)
		{
			return Attributes.TryGetValue(id, out attribute);
		}

		// --- Resource lookups return false; snapshot doesn't track mutable resource state. ---

		/// <inheritdoc/>
		public bool TryGetResourceAttribute(CharacterAttributeTemplate template, out CharacterResourceAttribute attribute) { attribute = null; return false; }

		/// <inheritdoc/>
		public bool TryGetResourceAttribute(int id, out CharacterResourceAttribute attribute) { attribute = null; return false; }

		/// <inheritdoc/>
		public bool TryGetHealthAttribute(out CharacterResourceAttribute health) { health = null; return false; }

		/// <inheritdoc/>
		public bool TryGetManaAttribute(out CharacterResourceAttribute mana) { mana = null; return false; }

		/// <inheritdoc/>
		public bool TryGetStaminaAttribute(out CharacterResourceAttribute stamina) { stamina = null; return false; }

		/// <inheritdoc/>
		public float GetHealthResourceAttributeCurrentPercentage() => 0f;

		/// <inheritdoc/>
		public float GetManaResourceAttributeCurrentPercentage() => 0f;

		/// <inheritdoc/>
		public float GetStaminaResourceAttributeCurrentPercentage() => 0f;

		// --- Mutating methods are no-ops on a snapshot. ---

		/// <inheritdoc/>
		public void SetAttribute(int id, int value, int? modifier = null) { }

		/// <inheritdoc/>
		public void SetResourceAttribute(int id, int value, float currentValue, int? modifier = null) { }

		/// <inheritdoc/>
		public void AddAttribute(CharacterAttribute instance) { }

		/// <inheritdoc/>
		public void Regenerate(float deltaTime) { }

		/// <inheritdoc/>
		public void ApplyResourceState(CharacterAttributeResourceState resourceState) { }

		/// <inheritdoc/>
		public CharacterAttributeResourceState GetResourceState() => default;

		// --- ICharacterBehaviour lifecycle no-ops ---

		/// <inheritdoc/>
		public void InitializeOnce(ICharacter character) { Character = character; }

		/// <inheritdoc/>
		public void OnStartCharacter() { }

		/// <inheritdoc/>
		public void OnStopCharacter() { }
	}
}