using Template.CodeGen.Generators;
using Template.CodeGen.Models;
using Template.CodeGen.Parsing;

namespace Template.CodeGen;

public class Program
{
    public static int Main(string[] args)
    {
        var scenesDir = GetArg(args, "--scenes") ?? "Client/Template.Godot/templates";
        var serverOutput = GetArg(args, "--server-output") ?? "Server/Template.Shared";
        var clientOutput = GetArg(args, "--client-output") ?? "Client/Template.Godot/Scripts/Visuals";
        var frameworkOutput = GetArg(args, "--framework-output") ?? "Client/Template.Godot/Scripts/Framework/Components";
        var dryRun = args.Contains("--dry-run");

        Console.WriteLine($"[CodeGen] Scanning: {scenesDir}");
        Console.WriteLine($"[CodeGen] Server output: {serverOutput}");
        Console.WriteLine($"[CodeGen] Client output: {clientOutput}");

        if (!Directory.Exists(scenesDir))
        {
            Console.Error.WriteLine($"[CodeGen] Error: Directory not found: {scenesDir}");
            return 1;
        }

        var filesWritten = 0;

        // Phase 1: Scan hand-written component structs
        var componentsDir = Path.Combine(serverOutput, "Components");
        var knownComponents = new Dictionary<string, ComponentDescriptor>();

        if (Directory.Exists(componentsDir))
        {
            foreach (var csFile in Directory.GetFiles(componentsDir, "*.cs", SearchOption.AllDirectories))
            {
                // Skip generated files
                if (csFile.EndsWith(".g.cs")) continue;

                var comp = ComponentStructParser.ParseFile(csFile);
                if (comp != null)
                {
                    knownComponents[comp.ComponentName] = comp;
                    Console.WriteLine($"[CodeGen] Found component: {comp.ComponentName} ({comp.Fields.Count} fields) in {Path.GetFileName(csFile)}");
                }
            }
        }

        // Also scan Entities folder (for PlayerEntity etc.)
        var entitiesDir = Path.Combine(serverOutput, "Entities");
        if (Directory.Exists(entitiesDir))
        {
            foreach (var csFile in Directory.GetFiles(entitiesDir, "*.cs", SearchOption.AllDirectories))
            {
                if (csFile.EndsWith(".g.cs")) continue;
                var comp = ComponentStructParser.ParseFile(csFile);
                if (comp != null)
                {
                    knownComponents[comp.ComponentName] = comp;
                    Console.WriteLine($"[CodeGen] Found component: {comp.ComponentName} ({comp.Fields.Count} fields) in {Path.GetFileName(csFile)}");
                }
            }
        }

        // Phase 1b: Scan for enum types
        var knownEnums = EnumParser.ScanDirectories(componentsDir, entitiesDir);
        foreach (var info in knownEnums.Values)
            Console.WriteLine($"[CodeGen] Found enum: {info.Name} ({info.Members.Count} members{(info.IsFlags ? ", flags" : "")})");

        // Mark enum fields on known components
        foreach (var comp in knownComponents.Values)
            foreach (var field in comp.Fields)
                ApplyEnumInfo(field, knownEnums);

        // Phase 2: Generate ComponentNode editor wrappers
        if (!Directory.Exists(frameworkOutput)) Directory.CreateDirectory(frameworkOutput);

        foreach (var comp in knownComponents.Values)
        {
            var nodeCode = ComponentNodeGenerator.Generate(comp);
            var nodePath = Path.Combine(frameworkOutput, $"{comp.ComponentName}Node.cs");
            filesWritten += WriteIfChanged(nodePath, nodeCode, dryRun);
        }

        // Phase 3: Parse .tscn files with known components
        var parser = new TscnParser { KnownComponents = knownComponents };
        var entities = new List<EntityDescriptor>();

        var tscnFiles = Directory.GetFiles(scenesDir, "*.tscn", SearchOption.AllDirectories);
        Console.WriteLine($"[CodeGen] Found {tscnFiles.Length} .tscn files");

        foreach (var file in tscnFiles)
        {
            var descriptor = parser.ParseFile(file);
            if (descriptor != null)
            {
                entities.Add(descriptor);
                Console.WriteLine($"[CodeGen] Found entity: {descriptor.EntityName} in {Path.GetFileName(file)}");
            }
        }

        // Mark enum fields on parsed entity components
        foreach (var entity in entities)
            foreach (var comp in entity.Components)
                foreach (var field in comp.Fields)
                    ApplyEnumInfo(field, knownEnums);

        if (entities.Count == 0)
        {
            Console.WriteLine("[CodeGen] No FrameworkEntity nodes found. Nothing to generate.");
            return 0;
        }

        // Phase 4: Generate server + client code
        Console.WriteLine($"[CodeGen] Generating code for {entities.Count} entities...");

        foreach (var entity in entities)
        {
            // Generate component struct only if no hand-written .cs exists
            foreach (var comp in entity.Components)
            {
                if (!knownComponents.ContainsKey(comp.ComponentName))
                {
                    var code = ComponentGenerator.Generate(comp);
                    var path = Path.Combine(serverOutput, "Components", $"{comp.ComponentName}.g.cs");
                    filesWritten += WriteIfChanged(path, code, dryRun);
                }
            }

            var defCode = DefinitionGenerator.Generate(entity);
            var defPath = Path.Combine(serverOutput, "Definitions", $"{entity.EntityName}Definition.g.cs");
            filesWritten += WriteIfChanged(defPath, defCode, dryRun);

            var vmCode = ViewModelGenerator.Generate(entity);
            var vmPath = Path.Combine(clientOutput, $"{entity.EntityName}ViewModel.g.cs");
            filesWritten += WriteIfChanged(vmPath, vmCode, dryRun);

            var viewCode = ViewGenerator.Generate(entity);
            var viewPath = Path.Combine(clientOutput, $"{entity.EntityName}View.g.cs");
            filesWritten += WriteIfChanged(viewPath, viewCode, dryRun);
        }

        Console.WriteLine($"[CodeGen] Done. {filesWritten} files {(dryRun ? "would be" : "")} written.");
        return 0;
    }

    private static int WriteIfChanged(string path, string content, bool dryRun)
    {
        var dir = Path.GetDirectoryName(path);
        if (dir != null && !Directory.Exists(dir)) Directory.CreateDirectory(dir);

        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path);
            if (existing == content) { Console.WriteLine($"  [skip] {path} (unchanged)"); return 0; }
        }

        if (dryRun) Console.WriteLine($"  [dry-run] {path}");
        else { File.WriteAllText(path, content); Console.WriteLine($"  [write] {path}"); }
        return 1;
    }

    private static void ApplyEnumInfo(FieldDescriptor field, Dictionary<string, EnumInfo> knownEnums)
    {
        if (!knownEnums.TryGetValue(field.TypeName, out var info)) return;
        field.IsEnum = true;
        field.EnumIsFlags = info.IsFlags;
        if (info.Members.Count > 0)
            field.EnumHint = info.BuildHint();
    }

    private static string? GetArg(string[] args, string name)
    {
        for (int i = 0; i < args.Length - 1; i++)
            if (args[i] == name) return args[i + 1];
        return null;
    }
}
