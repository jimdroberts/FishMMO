using System;
using System.Collections.Generic;

public class CharacterAttribute
{
	public int templateID;
	public CharacterAttributeTemplate Template { get { return CharacterAttributeTemplate.Cache[templateID]; } }

	private int baseValue;
	private int modifier;
	private int finalValue;
	private Dictionary<string, CharacterAttribute> parents = new Dictionary<string, CharacterAttribute>();
	private Dictionary<string, CharacterAttribute> children = new Dictionary<string, CharacterAttribute>();
	private Dictionary<string, CharacterAttribute> dependencies = new Dictionary<string, CharacterAttribute>();

	public delegate void AttributeUpdated(CharacterAttribute item);
	public event AttributeUpdated OnAttributeUpdated;

	protected virtual void Internal_OnAttributeChanged(CharacterAttribute item)
	{
		OnAttributeUpdated?.Invoke(item);
	}

	public int BaseValue { get { return baseValue; } }
	public void AddValue(int amount)
	{
		AddValue(amount, false);
	}
	public void AddValue(int amount, bool skipUpdate)
	{
		int tmp = baseValue + amount;
		if (baseValue != tmp)
		{
			baseValue = tmp;
			if (!skipUpdate)
			{
				UpdateValues(true);
			}
		}
	}
	public void AddModifier(int amount)
	{
		int tmp = modifier + amount;
		if (modifier != tmp)
		{
			modifier = tmp;
			if (Template.ClampFinalValue)
			{
				finalValue = (baseValue + modifier).Clamp(Template.MinValue, Template.MaxValue);
			}
			else
			{
				finalValue = baseValue + modifier;
			}
		}
	}
	public void RemoveValue(int amount)
	{
		RemoveValue(amount, false);
	}
	public void RemoveValue(int amount, bool skipUpdate)
	{
		int tmp = baseValue - amount;
		if (baseValue != tmp)
		{
			baseValue = tmp;
			if (!skipUpdate)
			{
				UpdateValues(true);
			}
		}
	}
	public int Modifier { get { return modifier; } }
	public int FinalValue { get { return finalValue; } }
	/// <summary>
	/// Returns the value as a float. 
	/// </summary>
	public float FinalValueAsFloat { get { return finalValue; } }
	/// <summary>
	/// Returns the value as a percentage instead. Value*0.01f
	/// </summary>
	public float FinalValueAsPct { get { return finalValue * 0.01f; } }

	public Dictionary<string, CharacterAttribute> Parents { get { return parents; } }
	public Dictionary<string, CharacterAttribute> Children { get { return children; } }
	public Dictionary<string, CharacterAttribute> Dependencies { get { return dependencies; } }

	public override string ToString()
	{
		return Template.Name + ": " + FinalValue;
	}

	public CharacterAttribute(int templateID, int initialValue, int initialModifier)
	{
		this.templateID = templateID;
		baseValue = initialValue;
		modifier = initialModifier;
		if (Template.ClampFinalValue)
		{
			finalValue = (baseValue + modifier).Clamp(Template.MinValue, Template.MaxValue);
		}
		else
		{
			finalValue = baseValue + modifier;
		}
	}

	public void AddParent(CharacterAttribute parent)
	{
		if (!parents.ContainsKey(parent.Template.Name))
		{
			parents.Add(parent.Template.Name, parent);
		}
	}

	public void RemoveParent(CharacterAttribute parent)
	{
		parents.Remove(parent.Template.Name);
	}

	public void AddChild(CharacterAttribute child)
	{
		if (!children.ContainsKey(child.Template.Name))
		{
			children.Add(child.Template.Name, child);
			child.AddParent(this);
			UpdateValues();
		}
	}

	public void RemoveChild(CharacterAttribute child)
	{
		children.Remove(child.Template.Name);
		child.RemoveParent(this);
		UpdateValues();
	}

	public void AddDependant(CharacterAttribute dependency)
	{
		Type dependencyType = dependency.GetType();
		if (!dependencies.ContainsKey(dependencyType.Name))
		{
			dependencies.Add(dependencyType.Name, dependency);
		}
	}

	public void RemoveDependant(CharacterAttribute dependency)
	{
		dependencies.Remove(dependency.GetType().Name);
	}

	public CharacterAttribute GetDependant(string name)
	{
		CharacterAttribute result;
		dependencies.TryGetValue(name, out result);
		return result;
	}

	public int GetDependantBaseValue(string name)
	{
		CharacterAttribute attribute;
		return (!dependencies.TryGetValue(name, out attribute)) ? 0 : attribute.BaseValue;
	}

	public int GetDependantMinValue(string name)
	{
		CharacterAttribute attribute;
		return (!dependencies.TryGetValue(name, out attribute)) ? 0 : attribute.Template.MinValue;
	}

	public int GetDependantMaxValue(string name)
	{
		CharacterAttribute attribute;
		return (!dependencies.TryGetValue(name, out attribute)) ? 0 : attribute.Template.MaxValue;
	}

	public int GetDependantModifier(string name)
	{
		CharacterAttribute attribute;
		return (!dependencies.TryGetValue(name, out attribute)) ? 0 : attribute.Modifier;
	}

	public int GetDependantFinalValue(string name)
	{
		CharacterAttribute attribute;
		return (!dependencies.TryGetValue(name, out attribute)) ? 0 : attribute.FinalValue;
	}

	public void UpdateValues()
	{
		UpdateValues(false);
	}
	public void UpdateValues(bool forceUpdate)
	{
		int oldFinalValue = finalValue;

		ApplyChildren();

		if (forceUpdate || finalValue != oldFinalValue)
		{
			foreach (CharacterAttribute parent in parents.Values)
			{
				parent.UpdateValues();
			}
		}
	}

	private void ApplyChildren()
	{
		modifier = 0;
		if (Template.Formulas != null)
		{
			foreach (KeyValuePair<CharacterAttributeTemplate, CharacterAttributeFormulaTemplate> pair in Template.Formulas)
			{
				CharacterAttribute child;
				if (children.TryGetValue(pair.Key.Name, out child))
				{
					modifier += pair.Value.CalculateBonus(this, child);
				}
			}
		}
		if (Template.ClampFinalValue)
		{
			finalValue = (baseValue + modifier).Clamp(Template.MinValue, Template.MaxValue);
		}
		else
		{
			finalValue = baseValue + modifier;
		}
		OnAttributeUpdated?.Invoke(this);
	}
}