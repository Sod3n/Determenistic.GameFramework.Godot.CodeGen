using System.Text.RegularExpressions;
using Template.CodeGen.Models;

namespace Template.CodeGen.Parsing;

/// <summary>
/// Scans .cs files for public enum declarations, including member names and values.
/// </summary>
public static class EnumParser
{
    private static readonly Regex EnumHeaderRegex = new(
        @"(?<flags>\[Flags\]\s*)?public\s+enum\s+(?<name>\w+)(\s*:\s*\w+)?\s*\{(?<body>[^}]*)\}",
        RegexOptions.Compiled | RegexOptions.Singleline);

    private static readonly Regex MemberRegex = new(
        @"(?<name>\w+)\s*(?:=\s*(?<value>[^,/\r\n]+))?\s*[,}]",
        RegexOptions.Compiled);

    /// <summary>
    /// Scan all .cs files in the given directories for public enum declarations.
    /// Returns a dictionary of enum type name to EnumInfo.
    /// </summary>
    public static Dictionary<string, EnumInfo> ScanDirectories(params string[] directories)
    {
        var enums = new Dictionary<string, EnumInfo>();
        foreach (var dir in directories)
        {
            if (!Directory.Exists(dir)) continue;
            foreach (var file in Directory.GetFiles(dir, "*.cs", SearchOption.AllDirectories))
            {
                if (file.EndsWith(".g.cs")) continue;
                foreach (var info in ParseDetailed(File.ReadAllText(file)))
                    enums[info.Name] = info;
            }
        }
        return enums;
    }

    /// <summary>
    /// Parse a single source string and return all public enum type names found (names only, for backwards compat).
    /// </summary>
    public static List<string> Parse(string content)
    {
        return ParseDetailed(content).Select(e => e.Name).ToList();
    }

    /// <summary>
    /// Parse a single .cs file and return all public enum type names found.
    /// </summary>
    public static List<string> ParseFile(string filePath)
    {
        return Parse(File.ReadAllText(filePath));
    }

    /// <summary>
    /// Parse a source string and return full EnumInfo for each public enum found.
    /// </summary>
    public static List<EnumInfo> ParseDetailed(string content)
    {
        var results = new List<EnumInfo>();
        foreach (Match match in EnumHeaderRegex.Matches(content))
        {
            var info = new EnumInfo
            {
                Name = match.Groups["name"].Value,
                IsFlags = match.Groups["flags"].Success,
            };

            long nextValue = 0;
            // Strip line comments and append trailing comma so the last member is always matched
            var body = Regex.Replace(match.Groups["body"].Value, @"//[^\n]*", "").TrimEnd() + ",";
            foreach (Match memberMatch in MemberRegex.Matches(body))
            {
                var name = memberMatch.Groups["name"].Value;
                long value;

                if (memberMatch.Groups["value"].Success)
                {
                    var raw = memberMatch.Groups["value"].Value.Trim();
                    if (!TryEvaluate(raw, info.Members, out value))
                    {
                        // Can't evaluate — skip this enum entirely for hint purposes
                        info.Members.Clear();
                        break;
                    }
                    nextValue = value + 1;
                }
                else
                {
                    value = nextValue++;
                }

                info.Members.Add(new EnumMember { Name = name, Value = value });
            }

            results.Add(info);
        }
        return results;
    }

    /// <summary>
    /// Try to evaluate a simple enum value expression: integer literals, hex, bit shifts, and OR combinations of known members.
    /// </summary>
    private static bool TryEvaluate(string expression, List<EnumMember> knownMembers, out long value)
    {
        expression = expression.Trim();
        value = 0;

        // Handle OR expressions: A | B | C
        if (expression.Contains('|'))
        {
            long combined = 0;
            foreach (var part in expression.Split('|'))
            {
                if (!TryEvaluate(part, knownMembers, out var partVal))
                    return false;
                combined |= partVal;
            }
            value = combined;
            return true;
        }

        // Integer literal (decimal)
        if (long.TryParse(expression, out value))
            return true;

        // Hex literal: 0x...
        if (expression.StartsWith("0x", StringComparison.OrdinalIgnoreCase) &&
            long.TryParse(expression[2..], System.Globalization.NumberStyles.HexNumber, null, out value))
            return true;

        // Bit shift: 1 << N
        var shiftMatch = Regex.Match(expression, @"^(\d+)\s*<<\s*(\d+)$");
        if (shiftMatch.Success &&
            long.TryParse(shiftMatch.Groups[1].Value, out var lhs) &&
            int.TryParse(shiftMatch.Groups[2].Value, out var rhs))
        {
            value = lhs << rhs;
            return true;
        }

        // Reference to a known member
        var known = knownMembers.FirstOrDefault(m => m.Name == expression);
        if (known != null)
        {
            value = known.Value;
            return true;
        }

        return false;
    }
}
