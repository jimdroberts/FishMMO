using System;
using UnityEngine;

namespace FishMMO.Shared
{
	/// <summary>
	/// Marks an int or List&lt;int&gt; field as a template ID reference.
	/// In the Inspector, the field displays an object picker for the specified
	/// CachedScriptableObject type but serializes only the deterministic ID.
	/// </summary>
	[AttributeUsage(AttributeTargets.Field, AllowMultiple = false)]
	public sealed class TemplateReferenceAttribute : PropertyAttribute
	{
		/// <summary>
		/// The CachedScriptableObject-derived type to pick from.
		/// </summary>
		public Type TemplateType => templateType ??= ResolveTypeName(typeName);

		private Type templateType;
		private readonly string typeName;

		/// <summary>
		/// Creates a new TemplateReferenceAttribute targeting the given template type.
		/// </summary>
		/// <param name="templateType">The ScriptableObject type to show in the picker.</param>
		public TemplateReferenceAttribute(Type templateType)
		{
			this.templateType = templateType;
		}

		/// <summary>
		/// Creates a TemplateReferenceAttribute naming its template type by its full name, for a core field that refers to a
		/// module's template (the scene namer's name modifier is the Naming module's): resolved when first read, and a plain
		/// ScriptableObject picker when the module is not installed.
		/// </summary>
		/// <param name="typeName">The template type's full name, e.g. "FishMMO.Shared.NameGeneration.NameModifierTemplate".</param>
		public TemplateReferenceAttribute(string typeName)
		{
			this.typeName = typeName;
		}

		private static Type ResolveTypeName(string name)
		{
			if (!string.IsNullOrEmpty(name))
			{
				foreach (System.Reflection.Assembly assembly in AppDomain.CurrentDomain.GetAssemblies())
				{
					Type found = assembly.GetType(name, false);
					if (found != null)
					{
						return found;
					}
				}
			}
			return typeof(ScriptableObject);
		}
	}
}