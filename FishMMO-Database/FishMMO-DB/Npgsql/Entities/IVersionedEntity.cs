
namespace FishMMO.Database.Npgsql.Entities
{
	public interface IVersionedEntity
	{
		long ID { get; set; }
		long Version { get; set; }
	}
}