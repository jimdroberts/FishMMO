/*using System;
using MySql.Data.MySqlClient;

public static class MySQL
{
	private string lastConnection = "";//returns the last attempted connection string regardless of failure
	private string lastSuccessfulConnection = "";//returns the last successful connection string
	private MySqlConnection connection = null;

	public MySqlConnection Connection { get { return connection; } }

	public MySQL()
	{
	}

	public void Connect()
	{
		string lastConnectionOmittedPassword = "SERVER=" + Configuration.MySQLIP + "; DATABASE=" + Configuration.MySQLDatabase + "; UID=" + Configuration.MySQLUID + ";";
		lastConnection = lastConnectionOmittedPassword + " PASSWORD=" + Configuration.MySQLPassword + ";";
		if (connection != null)
		{
			Disconnect();
		}
		try
		{
			Console.WriteLine("[MySQL] Connecting to {0}", lastConnectionOmittedPassword + " PASSWORD=Omitted;");
			connection = new MySqlConnection(lastConnection);
			connection.Open();
			lastSuccessfulConnection = lastConnection;
			Console.WriteLine("[MySQL] Connected");
		}
		catch (Exception e)
		{
			Console.WriteLine("[MySQL] {0}", e);
		}
	}

	public void Disconnect()
	{
		Console.WriteLine("[MySQL] Disconnecting...");
		if (connection == null)
		{
			Console.WriteLine("[MySQL] Disconnected");
			return;
		}
		connection.Close();
		connection = null;
		Console.WriteLine("[MySQL] Disconnected");
	}
}*/