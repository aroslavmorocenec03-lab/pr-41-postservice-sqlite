using Microsoft.Data.Sqlite;
using PostService.CommonTypes;
using PostService.Models;

namespace PostService.DataAccess;

public sealed class PostingRepository : IPostingRepository
{
    private readonly string _connectionString;

    public PostingRepository(string connectionString = "Data Source=PostingDb.db;")
        => _connectionString = connectionString;

    public void CreateDb()
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            CREATE TABLE IF NOT EXISTS Postings (
                Id INTEGER PRIMARY KEY,
                [From] TEXT NOT NULL,
                [To] TEXT NOT NULL,
                Content TEXT NOT NULL,
                DeliveryType INTEGER NOT NULL,
                Weight REAL NOT NULL,
                Width REAL NOT NULL,
                Height REAL NOT NULL,
                Depth REAL NOT NULL,
                [Value] REAL NULL,
                Price REAL NOT NULL,
                Description TEXT NULL,
                CreatedAt INTEGER NOT NULL);
            """;
        cmd.ExecuteNonQuery();
    }

    private const string Columns = "Id, [From], [To], Content, DeliveryType, Weight, Width, Height, Depth, [Value], Price, Description, CreatedAt";

    public List<Posting> GetList()
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM Postings ORDER BY Id;";
        using var reader = cmd.ExecuteReader();
        var result = new List<Posting>();
        while (reader.Read()) result.Add(ReadPosting(reader));
        return result;
    }

    public Posting? GetById(int postingId)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = $"SELECT {Columns} FROM Postings WHERE Id = @Id;";
        cmd.Parameters.AddWithValue("@Id", postingId);
        using var reader = cmd.ExecuteReader();
        return reader.Read() ? ReadPosting(reader) : null;
    }

    public int Create(Posting posting)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = """
            INSERT INTO Postings ([From], [To], Content, DeliveryType, Weight,
                Width, Height, Depth, [Value], Price, Description, CreatedAt)
            VALUES (@From, @To, @Content, @DeliveryType, @Weight,
                @Width, @Height, @Depth, @Value, @Price, @Description, @CreatedAt)
            RETURNING Id;
            """;
        AddFields(cmd, posting);
        cmd.Parameters.AddWithValue("@CreatedAt", new DateTimeOffset(posting.CreatedAt).ToUnixTimeSeconds());
        return Convert.ToInt32(cmd.ExecuteScalar());
    }

    public int Update(Posting posting)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        // Дата створення не змінюється під час редагування.
        cmd.CommandText = """
            UPDATE Postings SET [From] = @From, [To] = @To, Content = @Content,
                DeliveryType = @DeliveryType, Weight = @Weight, Width = @Width,
                Height = @Height, Depth = @Depth, [Value] = @Value,
                Price = @Price, Description = @Description
            WHERE Id = @Id;
            """;
        AddFields(cmd, posting);
        cmd.Parameters.AddWithValue("@Id", posting.Id);
        return cmd.ExecuteNonQuery();
    }

    public int Delete(int postingId)
    {
        using var conn = OpenConnection();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "DELETE FROM Postings WHERE Id = @Id;";
        cmd.Parameters.AddWithValue("@Id", postingId);
        return cmd.ExecuteNonQuery();
    }

    private SqliteConnection OpenConnection()
    {
        var conn = new SqliteConnection(_connectionString);
        try { conn.Open(); return conn; }
        catch { conn.Dispose(); throw; }
    }

    private static void AddFields(SqliteCommand cmd, Posting posting)
    {
        cmd.Parameters.AddWithValue("@From", posting.From);
        cmd.Parameters.AddWithValue("@To", posting.To);
        cmd.Parameters.AddWithValue("@Content", posting.Content);
        cmd.Parameters.AddWithValue("@DeliveryType", (int)posting.DeliveryType);
        cmd.Parameters.AddWithValue("@Weight", posting.Weight);
        cmd.Parameters.AddWithValue("@Width", posting.Width);
        cmd.Parameters.AddWithValue("@Height", posting.Height);
        cmd.Parameters.AddWithValue("@Depth", posting.Depth);
        cmd.Parameters.AddWithValue("@Value", posting.Value.HasValue ? (object)posting.Value.Value : DBNull.Value);
        cmd.Parameters.AddWithValue("@Price", posting.Price);
        cmd.Parameters.AddWithValue("@Description", (object?)posting.Description ?? DBNull.Value);
    }

    private static Posting ReadPosting(SqliteDataReader reader) => new()
    {
        Id = reader.GetInt32(reader.GetOrdinal("Id")),
        From = reader.GetString(reader.GetOrdinal("From")),
        To = reader.GetString(reader.GetOrdinal("To")),
        Content = reader.GetString(reader.GetOrdinal("Content")),
        DeliveryType = (DeliveryType)reader.GetInt32(reader.GetOrdinal("DeliveryType")),
        Weight = reader.GetFloat(reader.GetOrdinal("Weight")),
        Width = reader.GetFloat(reader.GetOrdinal("Width")),
        Height = reader.GetFloat(reader.GetOrdinal("Height")),
        Depth = reader.GetFloat(reader.GetOrdinal("Depth")),
        Value = reader.IsDBNull(reader.GetOrdinal("Value")) ? null : reader.GetFloat(reader.GetOrdinal("Value")),
        Price = reader.GetFloat(reader.GetOrdinal("Price")),
        Description = reader.IsDBNull(reader.GetOrdinal("Description")) ? null : reader.GetString(reader.GetOrdinal("Description")),
        CreatedAt = DateTimeOffset.FromUnixTimeSeconds(reader.GetInt64(reader.GetOrdinal("CreatedAt"))).UtcDateTime
    };
}
