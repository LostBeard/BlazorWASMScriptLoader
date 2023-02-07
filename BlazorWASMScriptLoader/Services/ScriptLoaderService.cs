using Microsoft.AspNetCore.Components;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using System.Collections.Immutable;
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

        async Task<ImmutableArray<byte>?> GetAssemblyImage(Assembly assembly)
        {
            ImmutableArray<byte>? ret = null;
            var assmeblyName = assembly.GetName().Name;
            var assemblyUrl = $"./_framework/{assmeblyName}.dll";
            try
            {
                var tmp = await _httpClient.GetAsync(assemblyUrl);
                if (tmp.IsSuccessStatusCode)
                {
                    var bytes = await tmp.Content.ReadAsByteArrayAsync();
                    ret = ImmutableArray.Create(bytes);
                }
            }
            catch { }
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
            appAssemblies.Add(typeof(Console).Assembly);
            appAssemblies.Add(typeof(Enumerable).Assembly);
            var references = new List<MetadataReference>();
            foreach (var assembly in appAssemblies)
            {
                var image = await GetAssemblyImage(assembly);
                if (image == null)
                {
                    // assembly may be located elsewhere ... handle if needed
                    continue;
                }
                var metadataReferene = MetadataReference.CreateFromImage(image);
                references.Add(metadataReferene);
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
