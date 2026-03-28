using FluentAssertions;
using Template.CodeGen.Generators;
using Template.CodeGen.Models;
using Xunit;

namespace Template.CodeGen.Tests;

public class VisualizerGeneratorTests
{
    [Fact]
    public void Generate_ContainsExportFields()
    {
        var entities = CreateEntities();
        var code = VisualizerGenerator.Generate(entities);
        code.Should().Contain("[Export] public PackedScene CowPrefab_Generated;");
        code.Should().Contain("[Export] public PackedScene WallPrefab_Generated;");
    }

    [Fact]
    public void Generate_ContainsRegisterMethod()
    {
        var entities = CreateEntities();
        var code = VisualizerGenerator.Generate(entities);
        code.Should().Contain("private void RegisterGeneratedEntities()");
    }

    [Fact]
    public void Generate_ContainsObservableList()
    {
        var entities = CreateEntities();
        var code = VisualizerGenerator.Generate(entities);
        code.Should().Contain("ObservableList<CowComponent, DTransform2D, CowViewModel>");
        code.Should().Contain("ObservableList<WallComponent, DTransform2D, WallViewModel>");
    }

    [Fact]
    public void Generate_ContainsPartialHooks()
    {
        var entities = CreateEntities();
        var code = VisualizerGenerator.Generate(entities);
        code.Should().Contain("partial void OnSpawnCow(CowViewModel vm, Node3D visualNode);");
        code.Should().Contain("partial void OnSpawnWall(WallViewModel vm, Node3D visualNode);");
    }

    [Fact]
    public void Generate_IsPartialClass()
    {
        var entities = CreateEntities();
        var code = VisualizerGenerator.Generate(entities);
        code.Should().Contain("public partial class EntityVisualizer");
    }

    [Fact]
    public void Generate_WithSkin_GeneratesSkinHandling()
    {
        var entities = new List<EntityDescriptor>
        {
            new() { EntityName = "Cow", HasSkin = true, Components = new List<ComponentDescriptor> { new() { ComponentName = "CowComponent" } } }
        };
        var code = VisualizerGenerator.Generate(entities);
        code.Should().Contain("SkinVisualizer.UpdateSkins");
        code.Should().Contain("IsHidden");
    }

    [Fact]
    public void Generate_UsesDictionaryDispatch()
    {
        var entities = CreateEntities();
        var code = VisualizerGenerator.Generate(entities);
        code.Should().Contain("_spawnDispatch.TryGetValue(vm.GetType()");
        code.Should().Contain("private bool SpawnCow(CowViewModel cowVm)");
        code.Should().Contain("private bool SpawnWall(WallViewModel wallVm)");
    }

    [Fact]
    public void Generate_DispatchMapsAllEntities()
    {
        var entities = CreateEntities();
        var code = VisualizerGenerator.Generate(entities);
        code.Should().Contain("{ typeof(CowViewModel), vm => SpawnCow((CowViewModel)vm) }");
        code.Should().Contain("{ typeof(WallViewModel), vm => SpawnWall((WallViewModel)vm) }");
    }

    private static List<EntityDescriptor> CreateEntities() => new()
    {
        new() { EntityName = "Cow", Components = new List<ComponentDescriptor> { new() { ComponentName = "CowComponent" } } },
        new() { EntityName = "Wall", Components = new List<ComponentDescriptor> { new() { ComponentName = "WallComponent" } } }
    };
}
