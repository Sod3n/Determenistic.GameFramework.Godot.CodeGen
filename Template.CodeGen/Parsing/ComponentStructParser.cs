using System.Text.RegularExpressions;
using Template.CodeGen.Models;

namespace Template.CodeGen.Parsing;

/// <summary>
/// Parses hand-written IComponent struct .cs files to extract field info.
/// </summary>
public static class ComponentStructParser
{
    private static readonly Regex StructRegex = new(
        @"public\s+struct\s+(?<name>\w+)\s*:\s*IComponent",
        RegexOptions.Compiled);

    private static readonly Regex StableIdRegex = new(
        @"\[StableId\(""(?<id>[^""]+)""\)\]",
        RegexOptions.Compiled);

    private static readonly Regex FieldRegex = new(
        @"^\s*public\s+(?<type>[\w.<>,\s]+?)\s+(?<name>\w+)\s*[;=]",
        RegexOptions.Compiled | RegexOptions.Multiline);

    /// <summary>
    /// Parse a .cs file containing an IComponent struct. Returns null if not an IComponent.
    /// </summary>
    public static ComponentDescriptor? ParseFile(string filePath)
    {
        var content = File.ReadAllText(filePath);
        return Parse(content);
    }

    public static ComponentDescriptor? Parse(string content)
    {
        var structMatch = StructRegex.Match(content);
        if (!structMatch.Success) return null;

        var name = structMatch.Groups["name"].Value;

        var stableIdMatch = StableIdRegex.Match(content);
        var stableId = stableIdMatch.Success ? stableIdMatch.Groups["id"].Value : "";

        var descriptor = new ComponentDescriptor
        {
            ComponentName = name,
            StableId = stableId,
        };

        // Extract fields from inside the struct body
        var braceStart = content.IndexOf('{', structMatch.Index);
        if (braceStart < 0) return descriptor;

        var braceDepth = 0;
        var braceEnd = braceStart;
        for (int i = braceStart; i < content.Length; i++)
        {
            if (content[i] == '{') braceDepth++;
            else if (content[i] == '}') { braceDepth--; if (braceDepth == 0) { braceEnd = i; break; } }
        }

        var body = content[braceStart..braceEnd];

        foreach (Match fieldMatch in FieldRegex.Matches(body))
        {
            var typeName = fieldMatch.Groups["type"].Value.Trim();
            var fieldName = fieldMatch.Groups["name"].Value;

            // Skip constants and static fields
            if (typeName.Contains("const") || typeName.Contains("static")) continue;

            descriptor.Fields.Add(new FieldDescriptor
            {
                Name = fieldName,
                TypeName = typeName,
            });
        }

        return descriptor;
    }
}
