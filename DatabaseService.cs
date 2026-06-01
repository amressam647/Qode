using System;
using System.Collections.Generic;
using System.Data;
using System.Text;
using System.Threading.Tasks;
using Microsoft.Data.SqlClient;

namespace LocalCursor.Services
{
    public class DatabaseService
    {
        private string _connectionString = "";
        private bool _isConnected;

        public bool IsConnected => _isConnected;
        public string CurrentDatabase { get; private set; } = "";

        /// <summary>
        /// Configures the database connection.
        /// </summary>
        public void Configure(string server, string database, string? userId = null, string? password = null, bool integratedSecurity = true)
        {
            var builder = new SqlConnectionStringBuilder
            {
                DataSource = server,
                InitialCatalog = database,
                IntegratedSecurity = integratedSecurity,
                TrustServerCertificate = true
            };

            if (!integratedSecurity && !string.IsNullOrEmpty(userId))
            {
                builder.UserID = userId;
                builder.Password = password;
            }

            _connectionString = builder.ConnectionString;
            CurrentDatabase = database;
        }

        /// <summary>
        /// Tests the connection to the database.
        /// </summary>
        public async Task<string> TestConnectionAsync()
        {
            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();
                _isConnected = true;
                return $"✓ Connected to {CurrentDatabase} on {conn.DataSource}";
            }
            catch (Exception ex)
            {
                _isConnected = false;
                return $"✗ Connection failed: {ex.Message}";
            }
        }

        /// <summary>
        /// Executes a SELECT query and returns results as formatted text.
        /// </summary>
        public async Task<string> ExecuteQueryAsync(string sql)
        {
            if (string.IsNullOrEmpty(_connectionString))
                return "Error: Database not configured. Use DB_CONNECT first.";

            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                using var cmd = new SqlCommand(sql, conn);
                cmd.CommandTimeout = 30;

                using var reader = await cmd.ExecuteReaderAsync();
                return FormatDataReader(reader);
            }
            catch (Exception ex)
            {
                return $"Query Error: {ex.Message}";
            }
        }

        /// <summary>
        /// Executes a non-query (INSERT, UPDATE, DELETE, CREATE, ALTER).
        /// </summary>
        public async Task<string> ExecuteNonQueryAsync(string sql)
        {
            if (string.IsNullOrEmpty(_connectionString))
                return "Error: Database not configured. Use DB_CONNECT first.";

            try
            {
                using var conn = new SqlConnection(_connectionString);
                await conn.OpenAsync();

                using var cmd = new SqlCommand(sql, conn);
                cmd.CommandTimeout = 30;

                int affected = await cmd.ExecuteNonQueryAsync();
                return $"✓ Query executed successfully. Rows affected: {affected}";
            }
            catch (Exception ex)
            {
                return $"Query Error: {ex.Message}";
            }
        }

        /// <summary>
        /// Gets table schema information.
        /// </summary>
        public async Task<string> GetSchemaAsync()
        {
            var sql = @"
                SELECT 
                    t.TABLE_SCHEMA,
                    t.TABLE_NAME,
                    c.COLUMN_NAME,
                    c.DATA_TYPE,
                    c.IS_NULLABLE
                FROM INFORMATION_SCHEMA.TABLES t
                JOIN INFORMATION_SCHEMA.COLUMNS c 
                    ON t.TABLE_NAME = c.TABLE_NAME AND t.TABLE_SCHEMA = c.TABLE_SCHEMA
                WHERE t.TABLE_TYPE = 'BASE TABLE'
                ORDER BY t.TABLE_SCHEMA, t.TABLE_NAME, c.ORDINAL_POSITION";

            return await ExecuteQueryAsync(sql);
        }

        /// <summary>
        /// Lists all databases on the server.
        /// </summary>
        public async Task<string> ListDatabasesAsync()
        {
            var sql = "SELECT name FROM sys.databases WHERE database_id > 4 ORDER BY name";
            return await ExecuteQueryAsync(sql);
        }

        /// <summary>
        /// Lists all tables in the current database.
        /// </summary>
        public async Task<string> ListTablesAsync()
        {
            var sql = "SELECT TABLE_SCHEMA, TABLE_NAME FROM INFORMATION_SCHEMA.TABLES WHERE TABLE_TYPE = 'BASE TABLE' ORDER BY TABLE_SCHEMA, TABLE_NAME";
            return await ExecuteQueryAsync(sql);
        }

        private string FormatDataReader(SqlDataReader reader)
        {
            var sb = new StringBuilder();
            var columns = new List<string>();

            // Header
            for (int i = 0; i < reader.FieldCount; i++)
            {
                columns.Add(reader.GetName(i));
            }
            sb.AppendLine(string.Join("\t| ", columns));
            sb.AppendLine(new string('-', columns.Count * 15));

            // Rows
            int rowCount = 0;
            while (reader.Read() && rowCount < 100) // Limit to 100 rows
            {
                var values = new List<string>();
                for (int i = 0; i < reader.FieldCount; i++)
                {
                    var val = reader.IsDBNull(i) ? "NULL" : reader.GetValue(i)?.ToString() ?? "";
                    values.Add(val.Length > 50 ? val.Substring(0, 47) + "..." : val);
                }
                sb.AppendLine(string.Join("\t| ", values));
                rowCount++;
            }

            sb.AppendLine($"\n[{rowCount} rows returned]");
            return sb.ToString();
        }
    }
}
