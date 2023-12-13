using Microsoft.AspNetCore.Components;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using System.Reflection;

namespace BlazorWASMScriptLoader
{
    // requires "Microsoft.CodeAnalysis.CSharp"
    // can be added via nuget
    public class ScriptLoaderService
    {
        HttpClient _httpClient = new HttpClient();

        public ScriptLoaderService(NavigationManager navigationManager)
        {
            _httpClient.BaseAddress = new Uri(navigationManager.BaseUri);
        }

        async Task<MetadataReference?> GetAssemblyMetadataReference(Assembly assembly)
        {
            MetadataReference? ret = null;
            var assemblyName = assembly.GetName().Name;
            var assemblyUrl = $"./_framework/{assemblyName}.dll";
            try
            {
                var tmp = await _httpClient.GetAsync(assemblyUrl);
                if (tmp.IsSuccessStatusCode)
                {
                    var bytes = await tmp.Content.ReadAsByteArrayAsync();
                    ret = MetadataReference.CreateFromImage(bytes);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"metadataReference not loaded: {assembly} {ex.Message}");
            }
            return ret;
        }

        public async Task<Assembly?> CompileToDLLAssembly(string sourceCode, string assemblyName = "")
        {
            if (string.IsNullOrEmpty(assemblyName)) assemblyName = Path.GetRandomFileName();
            var codeString = SourceText.From(sourceCode);
            var options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11);
            var parsedSyntaxTree = SyntaxFactory.ParseSyntaxTree(codeString, options);
            var appAssemblies = Assembly.GetEntryAssembly()?.GetReferencedAssemblies().Select(o => Assembly.Load(o)).ToList();
            appAssemblies.Add(typeof(object).Assembly);
            var references = new List<MetadataReference>();
            foreach (var assembly in appAssemblies)
            {
                var metadataReference = await GetAssemblyMetadataReference(assembly);
                if (metadataReference == null)
                {
                    // assembly may be located elsewhere ... handle if needed
                    continue;
                }
                references.Add(metadataReference);
            }
            CSharpCompilation compilation = CSharpCompilation.Create(
                assemblyName,
                syntaxTrees: new[] { parsedSyntaxTree },
                references: references,
                options: new CSharpCompilationOptions(
                    OutputKind.DynamicallyLinkedLibrary,
                    concurrentBuild: false,
                    optimizationLevel: OptimizationLevel.Debug
                )
            );
            using (var ms = new MemoryStream())
            {
                EmitResult result = compilation.Emit(ms);
                if (!result.Success)
                {
                    IEnumerable<Diagnostic> failures = result.Diagnostics.Where(diagnostic =>
                        diagnostic.IsWarningAsError ||
                        diagnostic.Severity == DiagnosticSeverity.Error);

                    foreach (Diagnostic diagnostic in failures)
                    {
                        Console.Error.WriteLine("{0}: {1}", diagnostic.Id, diagnostic.GetMessage());
                    }
                    return null;
                }
                else
                {
                    ms.Seek(0, SeekOrigin.Begin);
                    var assembly = Assembly.Load(ms.ToArray());
                    return assembly;
                }
            }
        }
    }
}
