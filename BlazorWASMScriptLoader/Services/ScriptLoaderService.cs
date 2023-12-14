using Microsoft.AspNetCore.Components;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using System.Reflection;
using System.Text;

namespace BlazorWASMScriptLoader;

// requires "Microsoft.CodeAnalysis.CSharp"
// can be added via nuget
public class ScriptLoaderService(NavigationManager navigationManager)
{
    HttpClient _httpClient { get; } = new() { BaseAddress = new(navigationManager.BaseUri) };


    public async Task<Assembly> CompileToDLLAssembly(string sourceCode, string assemblyName = "", bool release = false)
    {
        if (string.IsNullOrEmpty(assemblyName)) assemblyName = Path.GetRandomFileName();
        var codeString = SourceText.From(sourceCode);
        var options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp12);
        var parsedSyntaxTree = SyntaxFactory.ParseSyntaxTree(codeString, options);
        var appAssemblies = Assembly.GetEntryAssembly()!.GetReferencedAssemblies().Select(Assembly.Load).Append(typeof(object).Assembly);
        var references = new List<MetadataReference>();
        foreach (var assembly in appAssemblies)
        {
            try
            {
                var tmp = await _httpClient.GetAsync($"./_framework/{assembly.GetName().Name}.dll");
                if (tmp.IsSuccessStatusCode)
                    references.Add(MetadataReference.CreateFromImage(await tmp.Content.ReadAsByteArrayAsync()));
            }
            catch (Exception ex)
            {
                Console.WriteLine($"metadataReference not loaded: {assembly} {ex.Message}");
                // assembly may be located elsewhere ... handle if needed
            }
        }
        CSharpCompilation compilation = CSharpCompilation.Create(
            assemblyName,
            syntaxTrees: [parsedSyntaxTree],
            references: references,
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
        return Assembly.Load(ms.ToArray());
    }
}