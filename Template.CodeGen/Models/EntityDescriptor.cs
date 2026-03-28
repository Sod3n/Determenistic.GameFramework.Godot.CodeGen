namespace Template.CodeGen.Models;

/// <summary>
/// Describes a complete entity parsed from a .tscn file.
/// </summary>
public class EntityDescriptor
{
    public string EntityName { get; set; } = "";
    public string StableIdSeed { get; set; } = "";
    public string SourceFile { get; set; } = "";

    public List<ComponentDescriptor> Components { get; set; } = new();
    public PhysicsConfig? Physics { get; set; }
    public NavAgentConfig? NavAgent { get; set; }
    public bool HasState { get; set; }
    public bool HasSkin { get; set; }
    public List<VisualBinding> VisualBindings { get; set; } = new();
    public List<ChildEntityDescriptor> ChildEntities { get; set; } = new();
    /// <summary>Extra component type names to include in [EntityDefinition] attribute.</summary>
    public List<string> ExtraComponentTypes { get; set; } = new();
}

public class ComponentDescriptor
{
    public string ComponentName { get; set; } = "";
    public string StableId { get; set; } = "";
    public List<FieldDescriptor> Fields { get; set; } = new();
    public bool IsMarkerComponent => Fields.Count == 0;
}

public class FieldDescriptor
{
    public string Name { get; set; } = "";
    public string TypeName { get; set; } = "";
    public string? DefaultValue { get; set; }
    public bool IsEnum { get; set; }
    /// <summary>Pre-formatted Godot property hint string for enum fields, e.g. "None:0,Physics:1,Coins:2".</summary>
    public string? EnumHint { get; set; }
    /// <summary>Whether the enum uses [Flags] (renders as checkboxes instead of dropdown).</summary>
    public bool EnumIsFlags { get; set; }
}

public class EnumInfo
{
    public string Name { get; set; } = "";
    public bool IsFlags { get; set; }
    public List<EnumMember> Members { get; set; } = new();

    /// <summary>Build the Godot PropertyHint string. For flags, skips 0-value members.</summary>
    public string BuildHint()
    {
        var members = IsFlags ? Members.Where(m => m.Value != 0) : Members;
        return string.Join(",", members.Select(m => $"{m.Name}:{m.Value}"));
    }
}

public class EnumMember
{
    public string Name { get; set; } = "";
    public long Value { get; set; }
}

public class ChildEntityDescriptor
{
    public string ChildName { get; set; } = "";
    public bool ParentToEntity { get; set; } = true;
    public bool DestroyOnUnparent { get; set; } = true;
    /// <summary>Component type to store the child entity ID in. Defaults to primary component.</summary>
    public string? StoreInComponent { get; set; }
    /// <summary>Field on the target component to store the child entity ID.</summary>
    public string? StoreInField { get; set; }
    public PhysicsConfig? Physics { get; set; }
}

public enum PhysicsBodyType
{
    CharacterBody2D,
    StaticBody2D,
    Area2D
}

public enum CollisionShapeType
{
    Circle,
    Rectangle
}

public class PhysicsConfig
{
    public PhysicsBodyType BodyType { get; set; } = PhysicsBodyType.StaticBody2D;
    public CollisionShapeType ShapeType { get; set; } = CollisionShapeType.Circle;
    public float ShapeRadius { get; set; } = 0.5f;
    public float ShapeWidth { get; set; } = 1.0f;
    public float ShapeHeight { get; set; } = 1.0f;
    public uint CollisionLayer { get; set; } = 1;
    public uint CollisionMask { get; set; } = 1;
    public bool Monitoring { get; set; } = true;
    public bool Monitorable { get; set; } = false;
}

public class NavAgentConfig
{
    public float MaxSpeed { get; set; } = 10f;
    public float TargetDesiredDistance { get; set; } = 2.0f;
    public float PathDesiredDistance { get; set; } = 1.0f;
    public float Radius { get; set; } = 0.5f;
}

public class VisualBinding
{
    public string ComponentField { get; set; } = "";
    public string TargetNodePath { get; set; } = "";
    public string TargetProperty { get; set; } = "text";
    public string? Expression { get; set; }
}
