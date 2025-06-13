namespace SqlServer2PlantUml.Models;

/// <summary>
/// Configuration options for PlantUML generation
/// </summary>
public class PlantUmlOptions
{
    /// <summary>
    /// Include table descriptions in the diagram
    /// </summary>
    public bool IncludeDescriptions { get; set; } = true;

    /// <summary>
    /// Include column data types in the diagram
    /// </summary>
    public bool IncludeDataTypes { get; set; } = true;

    /// <summary>
    /// Include indexes in the diagram
    /// </summary>
    public bool IncludeIndexes { get; set; } = false;

    /// <summary>
    /// Include foreign key relationships
    /// </summary>
    public bool IncludeRelationships { get; set; } = true;

    /// <summary>
    /// Maximum number of tables to include (0 for unlimited)
    /// </summary>
    public int MaxTables { get; set; } = 0;

    /// <summary>
    /// Schemas to include (empty for all schemas)
    /// </summary>
    public List<string> IncludeSchemas { get; set; } = [];

    /// <summary>
    /// Schemas to exclude
    /// </summary>
    public List<string> ExcludeSchemas { get; set; } = ["sys", "INFORMATION_SCHEMA"];

    /// <summary>
    /// Table name patterns to include (regex patterns)
    /// </summary>
    public List<string> IncludeTablePatterns { get; set; } = [];

    /// <summary>
    /// Table name patterns to exclude (regex patterns)
    /// </summary>
    public List<string> ExcludeTablePatterns { get; set; } = [];

    /// <summary>
    /// Table names to exclude (exact names, supports simple wildcards)
    /// </summary>
    public List<string> ExcludeTables { get; set; } = [];

    /// <summary>
    /// Custom PlantUML theme to use
    /// </summary>
    public string? Theme { get; set; }

    /// <summary>
    /// Custom PlantUML directives to include
    /// </summary>
    public List<string> CustomDirectives { get; set; } = [];
}
