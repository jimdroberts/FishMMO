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
		public Type TemplateType { get; }

		/// <summary>
		/// Creates a new TemplateReferenceAttribute targeting the given template type.
		/// </summary>
		/// <param name="templateType">The ScriptableObject type to show in the picker.</param>
		public TemplateReferenceAttribute(Type templateType)
		{
			TemplateType = templateType;
		}
	}
}