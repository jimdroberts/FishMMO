using System;
using System.Linq;
using Microsoft.Extensions.Configuration;
using FishMMO.Database.Npgsql;
using Microsoft.EntityFrameworkCore;
public class Probe
{
    static int Main(string[] args) { return Run(args[0]); }
    public static int Run(string settingsPath)
    {
        try
        {
            Console.WriteLine("System.Core runtime: " + typeof(System.Linq.Expressions.Expression).Assembly.Location);
            var config = new ConfigurationBuilder().AddJsonFile(settingsPath).Build();
            var factory = new NpgsqlDbContextFactory(config);
            using (var context = factory.CreateDbContext())
            {
                Console.WriteLine("Context created; inspecting model...");
                Console.WriteLine("Entity count: " + context.Model.GetEntityTypes().Count());
                Console.WriteLine("Reading login registry...");
                Console.WriteLine("Login rows (up to one): " + context.LoginServers.AsNoTracking().Take(1).Count());
                Console.WriteLine("World rows (up to one): " + context.WorldServers.AsNoTracking().Take(1).Count());
                Console.WriteLine("Scene rows (up to one): " + context.SceneServers.AsNoTracking().Take(1).Count());
                Console.WriteLine("Async registry count: " + context.LoginServers.AsNoTracking().Take(1).CountAsync().GetAwaiter().GetResult());
                var synthetic = context.LoginServers.FromSqlRaw(
                    "SELECT -900001::bigint AS id, {0}::text AS name, TIMESTAMP '2025-01-02 03:04:05' AS time_created, TIMESTAMP '2025-01-02 03:04:05' AS last_pulse, {1}::text AS address, {2}::integer AS port",
                    "AotProbe", "127.0.0.1", 7770).SingleAsync().GetAwaiter().GetResult();
                if (synthetic.ID != -900001 || synthetic.Name != "AotProbe" || synthetic.Address != "127.0.0.1" || synthetic.Port != 7770 || context.Entry(synthetic).State != EntityState.Unchanged)
                    throw new Exception("Async materialization/tracking mismatch.");
                Console.WriteLine("PASS: asynchronous query and tracked entity materialization without database writes.");
                using (var connection = new global::Npgsql.NpgsqlConnection(new NpgsqlDbConfiguration(config).ConnectionString))
                {
                    connection.OpenAsync().GetAwaiter().GetResult();
                    using (var command = new global::Npgsql.NpgsqlCommand("SELECT @txt::text, @n::integer, @b::bigint, @g::uuid, @t::timestamp, @bytes::bytea, @a::integer[], @ok::boolean, @dec::numeric, @nil::text", connection))
                    {
                        string text = new string('é', 4096);
                        var guid = Guid.NewGuid();
                        var time = new DateTime(2025, 1, 2, 3, 4, 5, DateTimeKind.Unspecified);
                        command.Parameters.AddWithValue("txt", text);
                        command.Parameters.AddWithValue("n", 123);
                        // Exercise a non-default CLR type supported by the bigint handler.
                        command.Parameters.Add(new global::Npgsql.NpgsqlParameter("b", global::NpgsqlTypes.NpgsqlDbType.Bigint) { Value = 42 });
                        command.Parameters.AddWithValue("g", guid);
                        command.Parameters.AddWithValue("t", time);
                        command.Parameters.AddWithValue("bytes", new byte[] { 1, 2, 3 });
                        command.Parameters.AddWithValue("a", new int[] { 1, 2, 3 });
                        command.Parameters.AddWithValue("ok", true);
                        command.Parameters.AddWithValue("dec", 12.5m);
                        command.Parameters.Add(new global::Npgsql.NpgsqlParameter("nil", global::NpgsqlTypes.NpgsqlDbType.Text) { Value = DBNull.Value });
                        using (var reader = command.ExecuteReader())
                        {
                            if (!reader.Read() || reader.GetString(0) != text || reader.GetInt32(1) != 123 || reader.GetInt64(2) != 42 || reader.GetGuid(3) != guid || reader.GetDateTime(4) != time || !Enumerable.SequenceEqual((byte[])reader[5], new byte[] { 1, 2, 3 }) || !Enumerable.SequenceEqual((int[])reader[6], new int[] { 1, 2, 3 }) || !reader.GetBoolean(7) || reader.GetDecimal(8) != 12.5m || !reader.IsDBNull(9))
                                throw new Exception("Parameter roundtrip mismatch.");
                        }
                    }
                    Console.WriteLine("PASS: ten parameter roundtrips, including length-cache updates, nullable values, and alternate CLR types.");
                    using (var invalid = new global::Npgsql.NpgsqlCommand("SELECT @bad::smallint", connection))
                    {
                        invalid.Parameters.Add(new global::Npgsql.NpgsqlParameter("bad", global::NpgsqlTypes.NpgsqlDbType.Smallint) { Value = int.MaxValue });
                        bool rejected = false;
                        try { invalid.ExecuteScalar(); }
                        catch (OverflowException) { rejected = true; }
                        if (!rejected) throw new Exception("Expected the original overflow exception, without a reflection wrapper.");
                    }
                }
                Console.WriteLine("PASS: read-only EF/driver compatibility probe.");
            }
            return 0;
        }
        catch (Exception ex)
        {
            for (var current = ex; current != null; current = current.InnerException)
            {
                Console.WriteLine("EXCEPTION TYPE: " + current.GetType().FullName);
                if (current is System.IO.FileNotFoundException) Console.WriteLine("Missing assembly/file: " + ((System.IO.FileNotFoundException)current).FileName);
                if (current is System.Net.Sockets.SocketException) Console.WriteLine("Socket error: " + ((System.Net.Sockets.SocketException)current).SocketErrorCode + " native=" + ((System.Net.Sockets.SocketException)current).NativeErrorCode);
                if (current is NotImplementedException) Console.WriteLine("NotImplemented: " + current.Message);
                Console.WriteLine(current.StackTrace);
            }
            return 1;
        }
    }
}
