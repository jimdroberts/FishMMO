using FishMMO.Shared.Core;

namespace FishMMO.Shared
{
	/// <summary>
	/// Convenience over <see cref="ITooltip"/> for callers that want a whole tooltip in one call.
	/// </summary>
	public static class TooltipExtensions
	{
		/// <summary>
		/// Builds this object's complete tooltip content, icon included.
		/// </summary>
		/// <param name="source">The object to describe. May be null.</param>
		/// <returns>The content, or an empty content when the source is null.</returns>
		public static TooltipContent BuildContent(this ITooltip source)
		{
			TooltipContent content = new TooltipContent();
			if (source == null)
			{
				return content;
			}

			content.Icon = source.Icon;
			source.BuildTooltip(content);
			content.Sort();
			return content;
		}

		/// <summary>
		/// Builds this object's tooltip and flattens it to rich text.
		/// </summary>
		/// <remarks>
		/// For logs, tests, and any caller with only a plain label to write into. The client
		/// renders <see cref="TooltipContent"/> itself and does not use this.
		/// </remarks>
		public static string TooltipText(this ITooltip source)
		{
			return source.BuildContent().ToRichText();
		}
	}
}
