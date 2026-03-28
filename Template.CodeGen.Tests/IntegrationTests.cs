using FluentAssertions;
using Template.CodeGen.Generators;
using Template.CodeGen.Models;
using Template.CodeGen.Parsing;
using Xunit;

namespace Template.CodeGen.Tests;

public class EnumParserTests
{
    [Fact]
    public void Parse_FindsPublicEnum()
    {
        var code = @"
namespace Test;
public enum MyState { Idle, Running, Dead }
";
        var result = EnumParser.Parse(code);
        result.Should().Contain("MyState");
    }

    [Fact]
    public void Parse_FindsFlagsEnum()
    {
        var code = @"
[Flags]
public enum CollisionLayer : uint
{
    None = 0,
    Physics = 1,
}
";
        var result = EnumParser.Parse(code);
        result.Should().Contain("CollisionLayer");
    }

    [Fact]
    public void Parse_IgnoresPrivateEnum()
    {
        var code = "enum InternalState { A, B }";
        var result = EnumParser.Parse(code);
        result.Should().BeEmpty();
    }

    [Fact]
    public void Parse_FindsMultipleEnums()
    {
        var code = @"
public enum A { X }
public enum B { Y }
";
        var result = EnumParser.Parse(code);
        result.Should().HaveCount(2);
        result.Should().Contain("A");
        result.Should().Contain("B");
    }

    [Fact]
    public void ParseDetailed_ImplicitValues()
    {
        var code = "public enum State { Idle, Running, Dead }";
        var enums = EnumParser.ParseDetailed(code);
        enums.Should().HaveCount(1);
        var info = enums[0];
        info.Name.Should().Be("State");
        info.IsFlags.Should().BeFalse();
        info.Members.Should().HaveCount(3);
        info.Members[0].Name.Should().Be("Idle");
        info.Members[0].Value.Should().Be(0);
        info.Members[1].Name.Should().Be("Running");
        info.Members[1].Value.Should().Be(1);
        info.Members[2].Name.Should().Be("Dead");
        info.Members[2].Value.Should().Be(2);
    }

    [Fact]
    public void ParseDetailed_ExplicitValues()
    {
        var code = @"
public enum Priority { Low = 10, Medium = 20, High = 30 }
";
        var info = EnumParser.ParseDetailed(code)[0];
        info.Members[0].Value.Should().Be(10);
        info.Members[1].Value.Should().Be(20);
        info.Members[2].Value.Should().Be(30);
    }

    [Fact]
    public void ParseDetailed_FlagsEnum()
    {
        var code = @"
[Flags]
public enum CollisionLayer : uint
{
    None = 0,
    Physics = 1,
    Coins = 2,
    Interactable = 4,
    Zone = 8,
}
";
        var info = EnumParser.ParseDetailed(code)[0];
        info.IsFlags.Should().BeTrue();
        info.Members.Should().HaveCount(5);
        info.Members[0].Value.Should().Be(0);
        info.Members[3].Name.Should().Be("Interactable");
        info.Members[3].Value.Should().Be(4);
    }

    [Fact]
    public void ParseDetailed_BitShiftValues()
    {
        var code = @"
[Flags]
public enum Layers
{
    A = 1 << 0,
    B = 1 << 1,
    C = 1 << 2,
}
";
        var info = EnumParser.ParseDetailed(code)[0];
        info.Members[0].Value.Should().Be(1);
        info.Members[1].Value.Should().Be(2);
        info.Members[2].Value.Should().Be(4);
    }

    [Fact]
    public void ParseDetailed_OrCombinations()
    {
        var code = @"
[Flags]
public enum Layers
{
    A = 1,
    B = 2,
    All = A | B,
}
";
        var info = EnumParser.ParseDetailed(code)[0];
        info.Members[2].Name.Should().Be("All");
        info.Members[2].Value.Should().Be(3);
    }

    [Fact]
    public void ParseDetailed_MixedImplicitExplicit()
    {
        var code = "public enum M { A = 5, B, C }";
        var info = EnumParser.ParseDetailed(code)[0];
        info.Members[0].Value.Should().Be(5);
        info.Members[1].Value.Should().Be(6);
        info.Members[2].Value.Should().Be(7);
    }

    [Fact]
    public void ParseDetailed_IgnoresComments()
    {
        var code = @"
[Flags]
public enum CollisionLayer : uint
{
    None = 0,
    Physics = 1,        // Player, Cow, Walls
    Coins = 2,          // Coin pickups
    Interactable = 4,   // Grass, SellPoint, Land
    Zone = 8,           // Player interaction zone
}
";
        var info = EnumParser.ParseDetailed(code)[0];
        info.Members.Should().HaveCount(5);
        info.Members[1].Name.Should().Be("Physics");
        info.Members[1].Value.Should().Be(1);
        info.Members[4].Name.Should().Be("Zone");
        info.Members[4].Value.Should().Be(8);
    }

    [Fact]
    public void BuildHint_RegularEnum()
    {
        var info = new EnumInfo
        {
            Name = "State",
            IsFlags = false,
            Members = new List<EnumMember>
            {
                new() { Name = "Idle", Value = 0 },
                new() { Name = "Running", Value = 1 },
            }
        };
        info.BuildHint().Should().Be("Idle:0,Running:1");
    }

    [Fact]
    public void BuildHint_FlagsEnum_SkipsZeroValue()
    {
        var info = new EnumInfo
        {
            Name = "Layers",
            IsFlags = true,
            Members = new List<EnumMember>
            {
                new() { Name = "None", Value = 0 },
                new() { Name = "Physics", Value = 1 },
                new() { Name = "Coins", Value = 2 },
            }
        };
        info.BuildHint().Should().Be("Physics:1,Coins:2");
    }
}

public class ComponentNodeEnumTests
{
    [Fact]
    public void Generate_EnumFieldNoHint_ExportedAsPlainInt()
    {
        var comp = new ComponentDescriptor
        {
            ComponentName = "TestComponent",
            Fields = new List<FieldDescriptor>
            {
                new() { Name = "Layer", TypeName = "CollisionLayer", IsEnum = true },
            }
        };
        var code = ComponentNodeGenerator.Generate(comp);
        code.Should().Contain("[Export] public int Layer;");
    }

    [Fact]
    public void Generate_EnumFieldWithHint_ExportedWithPropertyHintEnum()
    {
        var comp = new ComponentDescriptor
        {
            ComponentName = "TestComponent",
            Fields = new List<FieldDescriptor>
            {
                new() { Name = "State", TypeName = "UnitState", IsEnum = true, EnumHint = "Idle:0,Running:1,Dead:2" },
            }
        };
        var code = ComponentNodeGenerator.Generate(comp);
        code.Should().Contain("[Export(PropertyHint.Enum, \"Idle:0,Running:1,Dead:2\")]");
        code.Should().Contain("public int State;");
    }

    [Fact]
    public void Generate_FlagsEnumWithHint_ExportedWithPropertyHintFlags()
    {
        var comp = new ComponentDescriptor
        {
            ComponentName = "TestComponent",
            Fields = new List<FieldDescriptor>
            {
                new() { Name = "Layer", TypeName = "CollisionLayer", IsEnum = true,
                         EnumIsFlags = true, EnumHint = "Physics:1,Coins:2,Interactable:4" },
            }
        };
        var code = ComponentNodeGenerator.Generate(comp);
        code.Should().Contain("[Export(PropertyHint.Flags, \"Physics:1,Coins:2,Interactable:4\")]");
        code.Should().Contain("public int Layer;");
    }

    [Fact]
    public void Generate_EnumFieldWithDefault_ExportedAsIntWithDefault()
    {
        var comp = new ComponentDescriptor
        {
            ComponentName = "TestComponent",
            Fields = new List<FieldDescriptor>
            {
                new() { Name = "Layer", TypeName = "CollisionLayer", IsEnum = true,
                         EnumHint = "None:0,Physics:1", DefaultValue = "4" },
            }
        };
        var code = ComponentNodeGenerator.Generate(comp);
        code.Should().Contain("public int Layer = 4;");
    }
}

public class IntegrationTests
{
    private const string FullCowTscn = @"
[gd_scene format=3 uid=""uid://cow_test""]

[ext_resource type=""Script"" path=""res://Scripts/Framework/FrameworkEntity.cs"" id=""1_entity""]
[ext_resource type=""Script"" path=""res://Scripts/Framework/FrameworkComponent.cs"" id=""2_comp""]
[ext_resource type=""Script"" path=""res://Scripts/Framework/FrameworkPhysicsBody.cs"" id=""3_phys""]
[ext_resource type=""Script"" path=""res://Scripts/Framework/FrameworkNavAgent.cs"" id=""4_nav""]
[ext_resource type=""Script"" path=""res://Scripts/Framework/FrameworkState.cs"" id=""5_state""]
[ext_resource type=""Script"" path=""res://Scripts/Framework/FrameworkSkin.cs"" id=""6_skin""]
[ext_resource type=""Script"" path=""res://Scripts/Framework/FrameworkVisualBinding.cs"" id=""7_bind""]

[node name=""Cow"" type=""Node3D""]
script = ExtResource(""1_entity"")
EntityName = ""Cow""
StableIdSeed = ""cow-stable-seed-001""

[node name=""CowComponent"" type=""Node"" parent="".""]
script = ExtResource(""2_comp"")
ComponentName = ""CowComponent""
FieldNames = PackedStringArray(""Exhaust"", ""MaxExhaust"", ""IsMilking"", ""HouseId"", ""SpawnPosition"", ""FollowingPlayer"")
FieldTypes = PackedStringArray(""int"", ""int"", ""bool"", ""Entity"", ""Vector2"", ""Entity"")

[node name=""Physics"" type=""Node"" parent="".""]
script = ExtResource(""3_phys"")
BodyType = ""CharacterBody2D""
ShapeType = ""Circle""
ShapeRadius = 0.5
CollisionLayer = 1
CollisionMask = 3

[node name=""NavAgent"" type=""Node"" parent="".""]
script = ExtResource(""4_nav"")
MaxSpeed = 10
TargetDesiredDistance = 2.0
PathDesiredDistance = 1.0
Radius = 0.5

[node name=""State"" type=""Node"" parent="".""]
script = ExtResource(""5_state"")

[node name=""Skin"" type=""Node"" parent="".""]
script = ExtResource(""6_skin"")

[node name=""CapacityBinding"" type=""Node"" parent="".""]
script = ExtResource(""7_bind"")
ComponentField = ""Exhaust""
TargetNodePath = ""Capacity""
TargetProperty = ""text""
";

    [Fact]
    public void FullPipeline_CowEntity_GeneratesAllOutputs()
    {
        var parser = new TscnParser();
        var entity = parser.Parse(FullCowTscn, "Cow.tscn");
        entity.Should().NotBeNull();
        entity!.EntityName.Should().Be("Cow");

        var componentCode = ComponentGenerator.Generate(entity.Components[0]);
        componentCode.Should().Contain("public struct CowComponent : IComponent");
        componentCode.Should().Contain("public int Exhaust;");
        componentCode.Should().Contain("public Entity HouseId;");

        var defCode = DefinitionGenerator.Generate(entity);
        defCode.Should().Contain("public static partial class CowDefinition");
        defCode.Should().Contain("typeof(CowComponent)");
        defCode.Should().Contain("typeof(CharacterBody2D)");
        defCode.Should().Contain("typeof(NavigationAgent2D)");

        var vmCode = ViewModelGenerator.Generate(entity);
        vmCode.Should().Contain("public partial class CowViewModel : EntityViewModel");
        vmCode.Should().Contain("CowDefinitionModel");
        vmCode.Should().Contain("SkinComponentModel");

        var vizCode = VisualizerGenerator.Generate(new List<Models.EntityDescriptor> { entity });
        vizCode.Should().Contain("public partial class EntityVisualizer");
        vizCode.Should().Contain("CowPrefab_Generated");
        vizCode.Should().Contain("ObservableList<CowComponent");
    }

    [Fact]
    public void FullPipeline_MinimalWallEntity()
    {
        var tscn = @"
[gd_scene format=3]
[ext_resource type=""Script"" path=""res://Scripts/Framework/FrameworkEntity.cs"" id=""1_e""]
[ext_resource type=""Script"" path=""res://Scripts/Framework/FrameworkComponent.cs"" id=""2_c""]
[node name=""Wall"" type=""Node3D""]
script = ExtResource(""1_e"")
EntityName = ""Wall""
StableIdSeed = ""wall-seed""
[node name=""WallComponent"" type=""Node"" parent="".""]
script = ExtResource(""2_c"")
ComponentName = ""WallComponent""
";
        var parser = new TscnParser();
        var entity = parser.Parse(tscn);
        entity.Should().NotBeNull();

        var compCode = ComponentGenerator.Generate(entity!.Components[0]);
        compCode.Should().Contain("public struct WallComponent : IComponent");

        var defCode = DefinitionGenerator.Generate(entity);
        defCode.Should().Contain("public static partial class WallDefinition");
        defCode.Should().NotContain("CharacterBody2D");
    }

    [Fact]
    public void FullPipeline_StableIds_AreDeterministic()
    {
        var parser = new TscnParser();
        var e1 = parser.Parse(FullCowTscn);
        var e2 = parser.Parse(FullCowTscn);
        e1!.Components[0].StableId.Should().Be(e2!.Components[0].StableId);
    }

    [Fact]
    public void FullPipeline_MultipleEntities_VisualizerRegistersAll()
    {
        var parser = new TscnParser();
        var cow = parser.Parse(FullCowTscn)!;
        var wallTscn = @"
[gd_scene format=3]
[ext_resource type=""Script"" path=""res://Scripts/Framework/FrameworkEntity.cs"" id=""1_e""]
[ext_resource type=""Script"" path=""res://Scripts/Framework/FrameworkComponent.cs"" id=""2_c""]
[node name=""Wall"" type=""Node3D""]
script = ExtResource(""1_e"")
EntityName = ""Wall""
StableIdSeed = ""wall-seed""
[node name=""WallComponent"" type=""Node"" parent="".""]
script = ExtResource(""2_c"")
ComponentName = ""WallComponent""
";
        var wall = parser.Parse(wallTscn)!;

        var vizCode = VisualizerGenerator.Generate(new List<EntityDescriptor> { cow, wall });
        vizCode.Should().Contain("CowPrefab_Generated");
        vizCode.Should().Contain("WallPrefab_Generated");
        vizCode.Should().Contain("partial void OnSpawnCow");
        vizCode.Should().Contain("partial void OnSpawnWall");
    }

    [Fact]
    public void FullPipeline_EnumField_GeneratesCorrectCode()
    {
        var tscn = @"
[gd_scene format=3]
[ext_resource type=""Script"" path=""res://Scripts/Framework/FrameworkEntity.cs"" id=""1_e""]
[ext_resource type=""Script"" path=""res://Scripts/Framework/FrameworkComponent.cs"" id=""2_c""]
[node name=""Turret"" type=""Node3D""]
script = ExtResource(""1_e"")
EntityName = ""Turret""
StableIdSeed = ""turret-seed""
[node name=""TurretComponent"" type=""Node"" parent="".""]
script = ExtResource(""2_c"")
ComponentName = ""TurretComponent""
FieldNames = PackedStringArray(""Layer"", ""Health"")
FieldTypes = PackedStringArray(""CollisionLayer"", ""int"")
FieldDefaults = PackedStringArray(""CollisionLayer.Physics"", """")
";
        var parser = new TscnParser();
        var entity = parser.Parse(tscn);
        entity.Should().NotBeNull();

        // Simulate what Program.cs does: mark enum fields
        var knownEnums = new HashSet<string> { "CollisionLayer" };
        foreach (var comp in entity!.Components)
            foreach (var field in comp.Fields)
                if (knownEnums.Contains(field.TypeName))
                    field.IsEnum = true;

        // Component struct
        var compCode = ComponentGenerator.Generate(entity.Components[0]);
        compCode.Should().Contain("public CollisionLayer Layer;");

        // Definition
        var defCode = DefinitionGenerator.Generate(entity);
        defCode.Should().Contain("component.Layer = CollisionLayer.Physics;");
        defCode.Should().Contain("component.Health = 0;");

        // ComponentNode
        var nodeCode = ComponentNodeGenerator.Generate(entity.Components[0]);
        nodeCode.Should().Contain("[Export] public int Layer");
    }
}
