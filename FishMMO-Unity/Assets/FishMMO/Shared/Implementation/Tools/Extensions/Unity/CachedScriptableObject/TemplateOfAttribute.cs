using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// On a core field typed as <see cref="CachedScriptableObject"/> or <see cref="ScriptableObject"/> (or a list of them): the template type the field holds,
	/// named rather than referenced because it belongs to a module. The inspector offers only assets of that
	/// type (or a type deriving from it); with the module removed it accepts any cached template, and the field's existing
	/// references load as missing.
	/// </summary>
	[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
	public sealed class TemplateOfAttribute : PropertyAttribute
	{
		/// <summary>The template type's name, as <see cref="Type.Name"/> (for example "BaseAbilityTemplate").</summary>
		public string TypeName { get; }

		public TemplateOfAttribute(string typeName)
		{
			TypeName = typeName;
		}

		/// <summary>The named type among the loaded assemblies, or null when its module is not installed.</summary>
		public Type Resolve()
		{
			foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
			{
				Type[] types;
				try
				{
					types = assembly.GetTypes();
				}
				catch (System.Reflection.ReflectionTypeLoadException e)
				{
					types = e.Types;
				}
				foreach (Type type in types)
				{
					if (type != null && type.Name == TypeName && typeof(ScriptableObject).IsAssignableFrom(type))
					{
						return type;
					}
				}
			}
			return null;
		}
	}
}
