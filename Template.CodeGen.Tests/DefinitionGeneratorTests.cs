using FluentAssertions;
using Template.CodeGen.Generators;
using Template.CodeGen.Models;
using Xunit;

namespace Template.CodeGen.Tests;

public class DefinitionGeneratorTests
{
    [Fact]
    public void Generate_ContainsEntityDefinitionAttribute()
    {
        var entity = CreateCowEntity();
        var code = DefinitionGenerator.Generate(entity);
        code.Should().Contain("[EntityDefinition(");
        code.Should().Contain("typeof(Transform2D)");
        code.Should().Contain("typeof(CowComponent)");
    }

    [Fact]
    public void Generate_ContainsPhysicsTypes()
    {
        var entity = CreateCowEntity();
        var code = DefinitionGenerator.Generate(entity);
        code.Should().Contain("typeof(CharacterBody2D)");
        code.Should().Contain("typeof(CollisionShape2D)");
    }

    [Fact]
    public void Generate_ContainsNavAgentType()
    {
        var entity = CreateCowEntity();
        var code = DefinitionGenerator.Generate(entity);
        code.Should().Contain("typeof(NavigationAgent2D)");
    }

    [Fact]
    public void Generate_ContainsStateAndSkin()
    {
        var entity = CreateCowEntity();
        var code = DefinitionGenerator.Generate(entity);
        code.Should().Contain("typeof(StateComponent)");
        code.Should().Contain("typeof(SkinComponent)");
    }

    [Fact]
    public void Generate_ContainsCreateMethod()
    {
        var entity = CreateCowEntity();
        var code = DefinitionGenerator.Generate(entity);
        code.Should().Contain("public static Entity Create(Context ctx, Vector2 position)");
        code.Should().Contain("ctx.CreateEntity<CowComponent>();");
    }

    [Fact]
    public void Generate_InitializesDefaults()
    {
        var entity = CreateCowEntity();
        var code = DefinitionGenerator.Generate(entity);
        code.Should().Contain("component.Exhaust = 0;");
        code.Should().Contain("component.IsMilking = false;");
        code.Should().Contain("component.HouseId = Entity.Null;");
    }

    [Fact]
    public void Generate_AddsPhysicsBody()
    {
        var entity = CreateCowEntity();
        var code = DefinitionGenerator.Generate(entity);
        code.Should().Contain("CharacterBody2D.Default");
        code.Should().Contain("CollisionShape2D.CreateCircle(0.5f)");
    }

    [Fact]
    public void Generate_IsPartialClass()
    {
        var entity = CreateCowEntity();
        var code = DefinitionGenerator.Generate(entity);
        code.Should().Contain("public static partial class CowDefinition");
    }

    [Fact]
    public void Generate_MinimalEntity()
    {
        var entity = new EntityDescriptor
        {
            EntityName = "Wall",
            Components = new List<ComponentDescriptor> { new() { ComponentName = "WallComponent" } }
        };
        var code = DefinitionGenerator.Generate(entity);
        code.Should().Contain("public static partial class WallDefinition");
        code.Should().NotContain("CharacterBody2D");
        code.Should().NotContain("NavigationAgent2D");
    }

    [Fact]
    public void Generate_RectangleCollision()
    {
        var entity = new EntityDescriptor
        {
            EntityName = "House",
            Components = new List<ComponentDescriptor> { new() { ComponentName = "HouseComponent" } },
            Physics = new PhysicsConfig { BodyType = PhysicsBodyType.StaticBody2D, ShapeType = CollisionShapeType.Rectangle, ShapeWidth = 2.0f, ShapeHeight = 3.0f }
        };
        var code = DefinitionGenerator.Generate(entity);
        code.Should().Contain("CollisionShape2D.CreateRectangle(new Vector2(2f, 3f))");
    }

    [Fact]
    public void Generate_ContainsOnEntityCreatedHook()
    {
        var entity = CreateCowEntity();
        var code = DefinitionGenerator.Generate(entity);
        code.Should().Contain("OnEntityCreated(ctx, entity, ref component, childEntities);");
        code.Should().Contain("static partial void OnEntityCreated(Context ctx, Entity entity, ref CowComponent component, Dictionary<string, Entity> childEntities);");
    }

    [Fact]
    public void Generate_MinimalEntity_OnEntityCreatedHook()
    {
        var entity = new EntityDescriptor
        {
            EntityName = "Wall",
            Components = new List<ComponentDescriptor> { new() { ComponentName = "WallComponent" } }
        };
        var code = DefinitionGenerator.Generate(entity);
        code.Should().Contain("OnEntityCreated(ctx, entity, ref component, childEntities);");
        code.Should().Contain("static partial void OnEntityCreated(Context ctx, Entity entity, ref WallComponent component, Dictionary<string, Entity> childEntities);");
    }

    [Fact]
    public void Generate_NoComponents_OnEntityCreatedHookWithoutRef()
    {
        var entity = new EntityDescriptor { EntityName = "Empty" };
        var code = DefinitionGenerator.Generate(entity);
        code.Should().Contain("OnEntityCreated(ctx, entity, childEntities);");
        code.Should().Contain("static partial void OnEntityCreated(Context ctx, Entity entity, Dictionary<string, Entity> childEntities);");
    }

    [Fact]
    public void Generate_EnumField_DefaultIsDefault()
    {
        var entity = new EntityDescriptor
        {
            EntityName = "Test",
            Components = new List<ComponentDescriptor>
            {
                new()
                {
                    ComponentName = "TestComponent",
                    Fields = new List<FieldDescriptor>
                    {
                        new() { Name = "Layer", TypeName = "CollisionLayer", IsEnum = true },
                    }
                }
            }
        };
        var code = DefinitionGenerator.Generate(entity);
        code.Should().Contain("component.Layer = default;");
    }

    [Fact]
    public void Generate_EnumField_NumericDefaultIsCast()
    {
        var entity = new EntityDescriptor
        {
            EntityName = "Test",
            Components = new List<ComponentDescriptor>
            {
                new()
                {
                    ComponentName = "TestComponent",
                    Fields = new List<FieldDescriptor>
                    {
                        new() { Name = "Layer", TypeName = "CollisionLayer", IsEnum = true, DefaultValue = "4" },
                    }
                }
            }
        };
        var code = DefinitionGenerator.Generate(entity);
        code.Should().Contain("component.Layer = (CollisionLayer)4;");
    }

    [Fact]
    public void Generate_EnumField_NamedDefaultPassedThrough()
    {
        var entity = new EntityDescriptor
        {
            EntityName = "Test",
            Components = new List<ComponentDescriptor>
            {
                new()
                {
                    ComponentName = "TestComponent",
                    Fields = new List<FieldDescriptor>
                    {
                        new() { Name = "Layer", TypeName = "CollisionLayer", IsEnum = true, DefaultValue = "CollisionLayer.Physics" },
                    }
                }
            }
        };
        var code = DefinitionGenerator.Generate(entity);
        code.Should().Contain("component.Layer = CollisionLayer.Physics;");
    }

    private static EntityDescriptor CreateCowEntity() => new()
    {
        EntityName = "Cow",
        Components = new List<ComponentDescriptor>
        {
            new()
            {
                ComponentName = "CowComponent",
                Fields = new List<FieldDescriptor>
                {
                    new() { Name = "Exhaust", TypeName = "int" },
                    new() { Name = "MaxExhaust", TypeName = "int" },
                    new() { Name = "IsMilking", TypeName = "bool" },
                    new() { Name = "HouseId", TypeName = "Entity" },
                }
            }
        },
        Physics = new PhysicsConfig { BodyType = PhysicsBodyType.CharacterBody2D, ShapeType = CollisionShapeType.Circle, ShapeRadius = 0.5f, CollisionLayer = 1, CollisionMask = 3 },
        NavAgent = new NavAgentConfig { MaxSpeed = 10f, TargetDesiredDistance = 2.0f, Radius = 0.5f },
        HasState = true,
        HasSkin = true
    };
}
