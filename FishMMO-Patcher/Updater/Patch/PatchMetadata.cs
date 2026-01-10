namespace FishMMO.Patcher
{
	public class PatchMetadata
	{
		public long Offset { get; set; }
		public int Length { get; set; }
		public byte[] NewBytes { get; set; }
	}
}