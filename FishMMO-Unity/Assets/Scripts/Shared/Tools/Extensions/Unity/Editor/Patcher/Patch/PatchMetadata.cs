namespace FishMMO.Patcher
{
	/// <summary>
	/// Represents metadata for a single patch operation on a file.
	/// Contains offset, length, and new byte data for the patch.
	/// </summary>
	public class PatchMetadata
	{
		/// <summary>
		/// The offset (in bytes) within the file where the patch should be applied.
		/// </summary>
		public long Offset { get; set; }

		/// <summary>
		/// The length (in bytes) of the patch data to be written.
		/// </summary>
		public int Length { get; set; }

		/// <summary>
		/// The new byte data to be written at the specified offset.
		/// </summary>
		public byte[] NewBytes { get; set; }
	}
}