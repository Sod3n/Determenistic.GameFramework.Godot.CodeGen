using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using Template.CodeGen.Models;

namespace Template.CodeGen.Parsing;

public class TscnParser
{
    private static readonly Regex ExtResourceRegex = new(
        @"\[ext_resource\s+.*?path=""(?<path>[^""]+)"".*?id=""(?<id>[^""]+)""\]",
        RegexOptions.Compiled);

    private static readonly Regex ScriptRegex = new(
        @"^script\s*=\s*ExtResource\(""(?<id>[^""]+)""\)",
        RegexOptions.Compiled);

    private static readonly Regex MetadataRegex = new(
        @"^metadata/(?<key>\S+)\s*=\s*(?<value>.+)$",
        RegexOptions.Compiled);

    private static readonly Regex PropertyRegex = new(
        @"^(?<key>[a-zA-Z_]\w*)\s*=\s*(?<value>.+)$",
        RegexOptions.Compiled);

    /// <summary>Known component structs by name, populated from .cs files before parsing .tscn files.</summary>
    public Dictionary<string, ComponentDescriptor> KnownComponents { get; set; } = new();

    public EntityDescriptor? Parse(string tscnContent, string sourceFile = "")
    {
        var extResources = ParseExtResources(tscnContent);
        var nodes = ParseNodes(tscnContent, extResources);

        var entityNode = nodes.FirstOrDefault(n => IsFrameworkScript(n.ScriptPath, "FrameworkEntity"));
        if (entityNode == null) return null;

        var descriptor = new EntityDescriptor
        {
            SourceFile = sourceFile,
            EntityName = GetStringProperty(entityNode, "EntityName") ?? entityNode.Name,
            StableIdSeed = GetStringProperty(entityNode, "StableIdSeed") ?? Guid.NewGuid().ToString(),
        };

        // Match children of the entity node — handles both root entity (parent=".") and nested entity (parent="Entity" or parent="SomePath/Entity")
        var entityParentPaths = new HashSet<string> { ".", entityNode.Name };
        if (entityNode.ParentPath != null)
            entityParentPaths.Add(entityNode.ParentPath == "." ? entityNode.Name : $"{entityNode.ParentPath}/{entityNode.Name}");

        foreach (var child in nodes.Where(n => n.ParentPath != null && entityParentPaths.Contains(n.ParentPath)))
        {
            if (child == entityNode) continue;

            if (IsFrameworkScript(child.ScriptPath, "FrameworkComponent"))
                descriptor.Components.Add(ParseComponent(child, descriptor.StableIdSeed));
            else if (IsFrameworkScript(child.ScriptPath, "FrameworkPhysicsBody"))
                descriptor.Physics = ParsePhysics(child);
            else if (IsFrameworkScript(child.ScriptPath, "FrameworkNavAgent"))
                descriptor.NavAgent = ParseNavAgent(child);
            else if (IsFrameworkScript(child.ScriptPath, "FrameworkState"))
                descriptor.HasState = true;
            else if (IsFrameworkScript(child.ScriptPath, "FrameworkSkin"))
                descriptor.HasSkin = true;
            else if (IsFrameworkScript(child.ScriptPath, "FrameworkVisualBinding"))
                descriptor.VisualBindings.Add(ParseVisualBinding(child));
            else if (IsFrameworkScript(child.ScriptPath, "FrameworkChildEntity"))
                descriptor.ChildEntities.Add(ParseChildEntity(child, nodes));
            else if (TryMatchComponentNode(child.ScriptPath, out var compName))
            {
                if (KnownComponents.TryGetValue(compName, out var knownComp))
                    descriptor.Components.Add(MergeComponentDefaults(knownComp, child));
                else
                    Console.WriteLine($"  [warn] ComponentNode '{compName}' not found in known components (script: {child.ScriptPath})");
            }
        }

        // Parse ExtraComponents from entity node metadata
        var extraComponents = GetStringProperty(entityNode, "ExtraComponents");
        if (extraComponents != null)
            descriptor.ExtraComponentTypes.AddRange(ParseStringArray(extraComponents));

        return descriptor;
    }

    public EntityDescriptor? ParseFile(string filePath)
    {
        var content = File.ReadAllText(filePath);
        return Parse(content, filePath);
    }

    private Dictionary<string, string> ParseExtResources(string content)
    {
        var resources = new Dictionary<string, string>();
        foreach (Match match in ExtResourceRegex.Matches(content))
            resources[match.Groups["id"].Value] = match.Groups["path"].Value;
        return resources;
    }

    private List<SceneNode> ParseNodes(string content, Dictionary<string, string> extResources)
    {
        var nodes = new List<SceneNode>();
        var lines = content.Split('\n');
        SceneNode? currentNode = null;

        for (int i = 0; i < lines.Length; i++)
        {
            var line = lines[i].Trim();

            if (line.StartsWith("[node "))
            {
                currentNode = ParseNodeHeader(line);
                if (currentNode != null) nodes.Add(currentNode);
                continue;
            }

            if (line.StartsWith("[") && !line.StartsWith("[node"))
            {
                currentNode = null;
                continue;
            }

            if (currentNode != null && !string.IsNullOrWhiteSpace(line))
            {
                var scriptMatch = ScriptRegex.Match(line);
                if (scriptMatch.Success)
                {
                    var resourceId = scriptMatch.Groups["id"].Value;
                    if (extResources.TryGetValue(resourceId, out var path))
                        currentNode.ScriptPath = path;
                    continue;
                }

                var metaMatch = MetadataRegex.Match(line);
                if (metaMatch.Success)
                {
                    currentNode.Metadata[metaMatch.Groups["key"].Value] = ParseValue(metaMatch.Groups["value"].Value);
                    continue;
                }

                var propMatch = PropertyRegex.Match(line);
                if (propMatch.Success)
                    currentNode.Properties[propMatch.Groups["key"].Value] = ParseValue(propMatch.Groups["value"].Value);
            }
        }

        return nodes;
    }

    private SceneNode? ParseNodeHeader(string line)
    {
        var nameMatch = Regex.Match(line, @"name=""(?<name>[^""]+)""");
        if (!nameMatch.Success) return null;

        var node = new SceneNode { Name = nameMatch.Groups["name"].Value };

        var typeMatch = Regex.Match(line, @"type=""(?<type>[^""]+)""");
        if (typeMatch.Success) node.Type = typeMatch.Groups["type"].Value;

        var parentMatch = Regex.Match(line, @"parent=""(?<parent>[^""]+)""");
        if (parentMatch.Success) node.ParentPath = parentMatch.Groups["parent"].Value;

        return node;
    }

    private static string ParseValue(string raw)
    {
        raw = raw.Trim();
        if (raw.StartsWith("\"") && raw.EndsWith("\""))
            return raw[1..^1];
        // Unwrap Godot type wrappers: NodePath("..."), Vector2(...), etc.
        if (raw.StartsWith("NodePath(\"") && raw.EndsWith("\")"))
            return raw["NodePath(\"".Length..^"\")".Length];
        var commentIdx = raw.IndexOf(" ;");
        if (commentIdx >= 0) raw = raw[..commentIdx].Trim();
        return raw;
    }

    /// <summary>
    /// Clone the struct-parsed component and overlay default values from the .tscn node properties.
    /// </summary>
    private static ComponentDescriptor MergeComponentDefaults(ComponentDescriptor structComp, SceneNode node)
    {
        var merged = new ComponentDescriptor
        {
            ComponentName = structComp.ComponentName,
            StableId = structComp.StableId,
        };

        foreach (var field in structComp.Fields)
        {
            var defaultValue = field.DefaultValue;

            // Check if the .tscn node has a property matching this field name
            if (node.Properties.TryGetValue(field.Name, out var tscnValue))
                defaultValue = tscnValue;

            merged.Fields.Add(new FieldDescriptor
            {
                Name = field.Name,
                TypeName = field.TypeName,
                DefaultValue = defaultValue,
            });
        }

        return merged;
    }

    private static bool TryMatchComponentNode(string? scriptPath, out string componentName)
    {
        componentName = "";
        if (string.IsNullOrEmpty(scriptPath)) return false;
        var fileName = Path.GetFileNameWithoutExtension(scriptPath);
        // Strip .g if present (e.g. "CowComponentNode.g.cs" → "CowComponentNode")
        if (fileName.EndsWith(".g")) fileName = fileName[..^2];
        if (!fileName.EndsWith("Node")) return false;
        componentName = fileName[..^"Node".Length];
        return !string.IsNullOrEmpty(componentName);
    }

    private static bool IsFrameworkScript(string? scriptPath, string scriptName)
    {
        if (string.IsNullOrEmpty(scriptPath)) return false;
        return scriptPath.Contains($"/{scriptName}.cs") || scriptPath.EndsWith($"{scriptName}.cs");
    }

    private static string? GetStringProperty(SceneNode node, string key)
    {
        return node.Properties.TryGetValue(key, out var val) ? val :
               node.Metadata.TryGetValue(key, out val) ? val : null;
    }

    private ComponentDescriptor ParseComponent(SceneNode node, string entitySeed)
    {
        var componentName = GetStringProperty(node, "ComponentName") ?? node.Name;
        var descriptor = new ComponentDescriptor
        {
            ComponentName = componentName,
            StableId = GenerateDeterministicGuid(entitySeed, componentName),
        };

        var fieldNames = GetStringProperty(node, "FieldNames");
        var fieldTypes = GetStringProperty(node, "FieldTypes");
        var fieldDefaults = GetStringProperty(node, "FieldDefaults");

        if (fieldNames != null && fieldTypes != null)
        {
            var names = ParseStringArray(fieldNames);
            var types = ParseStringArray(fieldTypes);
            var defaults = fieldDefaults != null ? ParseStringArray(fieldDefaults) : new List<string>();

            for (int i = 0; i < Math.Min(names.Count, types.Count); i++)
            {
                descriptor.Fields.Add(new FieldDescriptor
                {
                    Name = names[i],
                    TypeName = types[i],
                    DefaultValue = i < defaults.Count && !string.IsNullOrEmpty(defaults[i]) ? defaults[i] : null
                });
            }
        }

        foreach (var (key, value) in node.Metadata)
        {
            if (key.StartsWith("field_"))
            {
                var fieldName = key["field_".Length..];
                if (!descriptor.Fields.Any(f => f.Name == fieldName))
                {
                    var parts = value.Split(':', 2);
                    descriptor.Fields.Add(new FieldDescriptor
                    {
                        Name = fieldName,
                        TypeName = parts[0].Trim(),
                        DefaultValue = parts.Length > 1 ? parts[1].Trim() : null
                    });
                }
            }
        }

        return descriptor;
    }

    private PhysicsConfig ParsePhysics(SceneNode node)
    {
        var config = new PhysicsConfig();
        if (GetStringProperty(node, "BodyType") is string bt)
            if (Enum.TryParse<PhysicsBodyType>(bt, true, out var bodyType)) config.BodyType = bodyType;
        if (GetStringProperty(node, "ShapeType") is string st)
            if (Enum.TryParse<CollisionShapeType>(st, true, out var shapeType)) config.ShapeType = shapeType;
        if (GetStringProperty(node, "ShapeRadius") is string sr && float.TryParse(sr, NumberStyles.Float, CultureInfo.InvariantCulture, out var radius)) config.ShapeRadius = radius;
        if (GetStringProperty(node, "ShapeWidth") is string sw && float.TryParse(sw, NumberStyles.Float, CultureInfo.InvariantCulture, out var width)) config.ShapeWidth = width;
        if (GetStringProperty(node, "ShapeHeight") is string sh && float.TryParse(sh, NumberStyles.Float, CultureInfo.InvariantCulture, out var height)) config.ShapeHeight = height;
        if (GetStringProperty(node, "CollisionLayer") is string cl)
        {
            if (uint.TryParse(cl, NumberStyles.Integer, CultureInfo.InvariantCulture, out var layer))
                config.CollisionLayer = layer;
        }
        if (GetStringProperty(node, "CollisionMask") is string cm)
        {
            if (uint.TryParse(cm, NumberStyles.Integer, CultureInfo.InvariantCulture, out var mask))
                config.CollisionMask = mask;
        }
        if (GetStringProperty(node, "Monitoring") is string mon)
            config.Monitoring = mon.Equals("true", StringComparison.OrdinalIgnoreCase);
        if (GetStringProperty(node, "Monitorable") is string mable)
            config.Monitorable = mable.Equals("true", StringComparison.OrdinalIgnoreCase);
        return config;
    }

    private ChildEntityDescriptor ParseChildEntity(SceneNode node, List<SceneNode> allNodes)
    {
        var storeInField = GetStringProperty(node, "StoreInField");
        var descriptor = new ChildEntityDescriptor
        {
            ChildName = GetStringProperty(node, "ChildName") ?? node.Name,
            ParentToEntity = GetStringProperty(node, "ParentToEntity")?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? true,
            DestroyOnUnparent = GetStringProperty(node, "DestroyOnUnparent")?.Equals("true", StringComparison.OrdinalIgnoreCase) ?? true,
            StoreInComponent = GetStringProperty(node, "StoreInComponent"),
            StoreInField = string.IsNullOrEmpty(storeInField) ? null : storeInField,
        };

        // Parse children of the child entity node (e.g. FrameworkPhysicsBody)
        // Handle both root-level (parent="InteractionZone") and nested (parent="Entity/InteractionZone")
        var childFullPath = node.ParentPath == "." ? node.Name : $"{node.ParentPath}/{node.Name}";
        foreach (var grandchild in allNodes.Where(n => n.ParentPath == node.Name || n.ParentPath == childFullPath))
        {
            if (IsFrameworkScript(grandchild.ScriptPath, "FrameworkPhysicsBody"))
                descriptor.Physics = ParsePhysics(grandchild);
        }

        return descriptor;
    }

    private NavAgentConfig ParseNavAgent(SceneNode node)
    {
        var config = new NavAgentConfig();
        if (GetStringProperty(node, "MaxSpeed") is string ms && float.TryParse(ms, NumberStyles.Float, CultureInfo.InvariantCulture, out var maxSpeed)) config.MaxSpeed = maxSpeed;
        if (GetStringProperty(node, "TargetDesiredDistance") is string tdd && float.TryParse(tdd, NumberStyles.Float, CultureInfo.InvariantCulture, out var v1)) config.TargetDesiredDistance = v1;
        if (GetStringProperty(node, "PathDesiredDistance") is string pdd && float.TryParse(pdd, NumberStyles.Float, CultureInfo.InvariantCulture, out var v2)) config.PathDesiredDistance = v2;
        if (GetStringProperty(node, "Radius") is string r && float.TryParse(r, NumberStyles.Float, CultureInfo.InvariantCulture, out var rad)) config.Radius = rad;
        return config;
    }

    private VisualBinding ParseVisualBinding(SceneNode node)
    {
        return new VisualBinding
        {
            ComponentField = GetStringProperty(node, "ComponentField") ?? "",
            TargetNodePath = GetStringProperty(node, "TargetNodePath") ?? "",
            TargetProperty = GetStringProperty(node, "TargetProperty") ?? "text",
            Expression = GetStringProperty(node, "Expression"),
        };
    }

    private static List<string> ParseStringArray(string value)
    {
        value = value.Trim();
        if (value.StartsWith("PackedStringArray("))
            value = value["PackedStringArray(".Length..^1];
        else if (value.StartsWith("["))
            value = value[1..^1];

        return value.Split(',')
            .Select(s => s.Trim().Trim('"'))
            .Where(s => !string.IsNullOrEmpty(s))
            .ToList();
    }

    public static string GenerateDeterministicGuid(string seed, string name)
    {
        var input = $"{seed}:{name}";
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        var guidBytes = new byte[16];
        Array.Copy(hash, guidBytes, 16);
        guidBytes[6] = (byte)((guidBytes[6] & 0x0F) | 0x50);
        guidBytes[8] = (byte)((guidBytes[8] & 0x3F) | 0x80);
        return new Guid(guidBytes).ToString();
    }
}

internal class SceneNode
{
    public string Name { get; set; } = "";
    public string Type { get; set; } = "";
    public string? ParentPath { get; set; }
    public string? ScriptPath { get; set; }
    public Dictionary<string, string> Properties { get; set; } = new();
    public Dictionary<string, string> Metadata { get; set; } = new();
}
