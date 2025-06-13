namespace SqlServer2PlantUml.Models;

/// <summary>
/// Represents a database table structure
/// </summary>
public class DatabaseTable
{
    public string Name { get; set; } = string.Empty;
    public string Schema { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // Added Type property
    public List<DatabaseColumn> Columns { get; set; } = [];
    public List<DatabaseIndex> Indexes { get; set; } = [];
    public string? Description { get; set; }
}

/// <summary>
/// Represents a database column
/// </summary>
public class DatabaseColumn
{
    public string Name { get; set; } = string.Empty;
    public string DataType { get; set; } = string.Empty;
    public int? MaxLength { get; set; }
    public int? Precision { get; set; }
    public int? Scale { get; set; }
    public int? NumericPrecision { get; set; } 
    public int? NumericScale { get; set; }
    public bool IsNullable { get; set; }
    public bool IsPrimaryKey { get; set; }
    public bool IsIdentity { get; set; }
    public bool IsForeignKey { get; set; }
    public string? DefaultValue { get; set; }
    public string? Description { get; set; }
    public string? ReferencedSchema { get; set; }
    public string? ReferencedTable { get; set; }
    public string? ReferencedColumn { get; set; }
    public int OrdinalPosition { get; set; }
}

/// <summary>
/// Represents a database index
/// </summary>
public class DatabaseIndex
{
    public string Name { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty;
    public bool IsUnique { get; set; }
    public bool IsPrimaryKey { get; set; }
    public List<string> Columns { get; set; } = [];
}

/// <summary>
/// Represents a database relationship
/// </summary>
public class DatabaseRelationship
{
    public string ConstraintName { get; set; } = string.Empty;
    public string FromSchema { get; set; } = string.Empty;
    public string FromTable { get; set; } = string.Empty;
    public string FromColumn { get; set; } = string.Empty;
    public string ToSchema { get; set; } = string.Empty;
    public string ToTable { get; set; } = string.Empty;
    public string ToColumn { get; set; } = string.Empty;
}

/// <summary>
/// Represents the complete database schema
/// </summary>
public class DatabaseSchema
{
    public string DatabaseName { get; set; } = string.Empty;
    public List<DatabaseTable> Tables { get; set; } = [];
    public List<DatabaseRelationship> Relationships { get; set; } = []; // Added Relationships property
    public DateTime GeneratedAt { get; set; } = DateTime.UtcNow;
}
