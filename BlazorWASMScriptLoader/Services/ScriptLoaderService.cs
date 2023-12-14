using Microsoft.AspNetCore.Components;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using System.Reflection;
using System.Text;

namespace BlazorWASMScriptLoader;

// requires "Microsoft.CodeAnalysis.CSharp"
// can be added via nuget
public class ScriptLoaderService(NavigationManager navigationManager)
{
    HttpClient _httpClient { get; } = new() { BaseAddress = new(navigationManager.BaseUri) };
    readonly ParseOptions opt = CSharpParseOptions.Default.WithKind(SourceCodeKind.Script);
    List<MetadataReference>? references = null;

    public async Task<(Assembly, object)> CompileToDLLAssembly(string sourceCode, string assemblyName = "", bool release = false)
    {
        if (string.IsNullOrEmpty(assemblyName)) assemblyName = Path.GetRandomFileName();

        async Task<List<MetadataReference>> GetReferences()
        {
            var appAssemblies = Assembly.GetEntryAssembly()!.GetReferencedAssemblies().Select(Assembly.Load).Append(typeof(object).Assembly);
            var references = new List<MetadataReference>();
            foreach (var assembly in appAssemblies)
            {
                try
                {
                    using var tmp = await _httpClient.GetStreamAsync($"./_framework/{assembly.GetName().Name}.dll");
                    references.Add(MetadataReference.CreateFromStream(tmp));
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"metadataReference not loaded: {assembly} {ex.Message}");
                    // assembly may be located elsewhere ... handle if needed
                }
            }
            return references;
        }

        CSharpCompilation compilation = CSharpCompilation.CreateScriptCompilation(
            assemblyName,
            syntaxTree: SyntaxFactory.ParseSyntaxTree(sourceCode, opt),
            references: references ??= await GetReferences(),
            options: new CSharpCompilationOptions(
                OutputKind.DynamicallyLinkedLibrary,
                concurrentBuild: false,
                optimizationLevel: release ? OptimizationLevel.Release : OptimizationLevel.Debug
            )
        );
        using var ms = new MemoryStream();
        EmitResult result = compilation.Emit(ms);
        if (!result.Success)
        {
            var errors = new StringBuilder();
            IEnumerable<Diagnostic> failures = result.Diagnostics.Where(diagnostic => diagnostic.IsWarningAsError || diagnostic.Severity == DiagnosticSeverity.Error);
            foreach (Diagnostic diagnostic in failures)
            {
                var startLinePos = diagnostic.Location.GetLineSpan().StartLinePosition;
                var err = $"Line: {startLinePos.Line} Col:{startLinePos.Character} Code: {diagnostic.Id} Message: {diagnostic.GetMessage()}";
                errors.AppendLine(err);
                Console.Error.WriteLine(err);
            }
            throw new Exception(errors.ToString());
        }
        ms.Seek(0, SeekOrigin.Begin);
        var load = Assembly.Load(ms.ToArray());
        var entryPoint = load.EntryPoint ?? compilation.GetEntryPoint(default) switch //load.EntryPoint is output null.
        {
            { ContainingNamespace.MetadataName: var namespace_name, ContainingType.MetadataName: var class_name, MetadataName: var method_name }
                => load.GetType($"{namespace_name}.{class_name}")?.GetMethod(method_name)
        };
        var submission = entryPoint!.CreateDelegate<Func<object[], Task<object>>>();
        return (load, await submission([null!, null!]));
    }
}