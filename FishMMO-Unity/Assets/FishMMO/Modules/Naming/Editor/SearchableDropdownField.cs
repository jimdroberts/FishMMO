using System;
using System.Collections.Generic;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using UnityEngine.UIElements;

namespace FishMMO.Shared.NameGeneration.Editor
{
	/// <summary>
	/// A field-shaped button that opens Unity's searchable
	/// <see cref="AdvancedDropdown"/> rather than the flat popup list a
	/// <c>DropdownField</c> uses.
	///
	/// <para>The race list holds 135 entries and the biome list 56. At that
	/// size the flat popup opens scrolled so the current entry sits under the
	/// field, which leaves a tall blank gap above the first item and gives no
	/// way to search.</para>
	/// </summary>
	public sealed class SearchableDropdownField : VisualElement
	{
		private readonly Button button;
		private readonly string title;
		private List<string> choices;
		/// <summary>Optional group per choice (parallel to <see cref="choices"/>); null shows a flat list.</summary>
		private List<string> groups;
		private int index;

		/// <summary>Raised when the selection changes, with the new display text.</summary>
		public event Action<string> OnValueChanged;

		/// <summary>Index of the current selection, or -1 when the list is empty.</summary>
		public int Index => index;

		/// <summary>Display text of the current selection ("" when the list is empty).</summary>
		public string Value => index >= 0 && index < choices.Count ? choices[index] : "";

		public IReadOnlyList<string> Choices => choices;

		/// <param name="groups">Optional group name per choice; choices sharing a group nest under it in the menu (search still spans everything).</param>
		public SearchableDropdownField(string title, IEnumerable<string> choices, int initialIndex = 0, IEnumerable<string> groups = null)
		{
			this.title = string.IsNullOrEmpty(title) ? "Select" : title;
			this.choices = choices == null ? new List<string>() : new List<string>(choices);
			this.groups = groups == null ? null : new List<string>(groups);
			index = this.choices.Count == 0 ? -1 : Mathf.Clamp(initialIndex, 0, this.choices.Count - 1);

			AddToClassList("ng-picker");
			button = new Button(ShowMenu) { text = Value };
			button.AddToClassList("ng-picker__button");
			Add(button);
		}

		/// <summary>Replace the choice list, selecting <paramref name="selectIndex"/>.</summary>
		public void SetChoices(IEnumerable<string> newChoices, int selectIndex = 0, IEnumerable<string> newGroups = null)
		{
			choices = newChoices == null ? new List<string>() : new List<string>(newChoices);
			groups = newGroups == null ? null : new List<string>(newGroups);
			index = choices.Count == 0 ? -1 : Mathf.Clamp(selectIndex, 0, choices.Count - 1);
			button.text = Value;
		}

		/// <summary>Select by index without raising <see cref="OnValueChanged"/>.</summary>
		public void SetIndexWithoutNotify(int newIndex)
		{
			index = choices.Count == 0 ? -1 : Mathf.Clamp(newIndex, 0, choices.Count - 1);
			button.text = Value;
		}

		/// <summary>Select by index, raising <see cref="OnValueChanged"/> when it changes.</summary>
		public void SetIndex(int newIndex)
		{
			int previous = index;
			SetIndexWithoutNotify(newIndex);
			if (index != previous)
			{
				OnValueChanged?.Invoke(Value);
			}
		}

		/// <summary>
		/// Recovers the choice index an <see cref="AdvancedDropdown"/> item was
		/// built for, or -1 if it did not come from this control.
		///
		/// <para>The index has to travel on the item's own type. It cannot use
		/// <see cref="AdvancedDropdownItem.id"/>: <c>AdvancedDropdownItem.AddChild</c>
		/// overwrites the id of every child it is given with
		/// <c>HashCode.Combine(parentId, childName)</c>, so an id assigned at
		/// construction is gone by the time the item is in the tree. Nor can it
		/// use position — the search results are sorted, so the order shown is
		/// not the order supplied.</para>
		/// </summary>
		public static int ResolveIndex(AdvancedDropdownItem item)
		{
			return item is IndexedItem indexed ? indexed.Index : -1;
		}

		/// <summary>An <see cref="AdvancedDropdown"/> entry that remembers which
		/// choice index it was built from. See <see cref="ResolveIndex"/>.</summary>
		public sealed class IndexedItem : AdvancedDropdownItem
		{
			public int Index { get; }

			public IndexedItem(string name, int index) : base(name)
			{
				Index = index;
			}
		}

		private void ShowMenu()
		{
			if (choices.Count == 0)
			{
				return;
			}

			Rect anchor = button.worldBound;
			var dropdown = new StringAdvancedDropdown(new AdvancedDropdownState(), title, choices,
				groups != null && groups.Count == choices.Count ? groups : null, OnPicked)
			{
				// Keep the popup at least as wide as the field it drops from.
				MinimumSize = new Vector2(Mathf.Max(anchor.width, 220f), 320f),
			};
			dropdown.Show(anchor);
		}

		private void OnPicked(AdvancedDropdownItem item)
		{
			int picked = ResolveIndex(item);
			if (picked < 0)
			{
				// Never guess: silently landing on the wrong entry is worse than
				// leaving the selection alone.
				return;
			}
			SetIndex(picked);
		}

		/// <summary>Searchable list of strings, flat or nested one level under group names.</summary>
		private sealed class StringAdvancedDropdown : AdvancedDropdown
		{
			private readonly string title;
			private readonly List<string> items;
			private readonly List<string> groups;
			private readonly Action<AdvancedDropdownItem> onPicked;

			public Vector2 MinimumSize
			{
				get => minimumSize;
				set => minimumSize = value;
			}

			public StringAdvancedDropdown(AdvancedDropdownState state, string title,
				List<string> items, List<string> groups, Action<AdvancedDropdownItem> onPicked) : base(state)
			{
				this.title = title;
				this.items = items;
				this.groups = groups;
				this.onPicked = onPicked;
			}

			protected override AdvancedDropdownItem BuildRoot()
			{
				var root = new AdvancedDropdownItem(title);
				if (groups == null)
				{
					for (int i = 0; i < items.Count; i++)
					{
						root.AddChild(new IndexedItem(items[i], i));
					}
					return root;
				}

				// One submenu per group, in order of first appearance; ungrouped entries stay at the top level.
				var byGroup = new Dictionary<string, AdvancedDropdownItem>(StringComparer.Ordinal);
				for (int i = 0; i < items.Count; i++)
				{
					string group = groups[i];
					if (string.IsNullOrEmpty(group))
					{
						root.AddChild(new IndexedItem(items[i], i));
						continue;
					}
					if (!byGroup.TryGetValue(group, out AdvancedDropdownItem parent))
					{
						parent = new AdvancedDropdownItem(group);
						byGroup[group] = parent;
						root.AddChild(parent);
					}
					parent.AddChild(new IndexedItem(items[i], i));
				}
				return root;
			}

			protected override void ItemSelected(AdvancedDropdownItem item)
			{
				onPicked?.Invoke(item);
			}
		}
	}
}
