using Microsoft.AspNetCore.Components;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Emit;
using Microsoft.CodeAnalysis.Text;
using System.Reflection;
using System.Text;

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

        Dictionary<string, MetadataReference> MetadataReferenceCache = new Dictionary<string, MetadataReference>();
        async Task<MetadataReference> GetAssemblyMetadataReference(Assembly assembly)
        {
            MetadataReference? ret = null;
            var assemblyName = assembly.GetName().Name;
            if (MetadataReferenceCache.TryGetValue(assemblyName, out ret)) return ret;
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
            if (ret == null) throw new Exception("ReferenceMetadata nto found. If using .Net 8, <WasmEnableWebcil>false</WasmEnableWebcil> must be set in the project .csproj file.");
            MetadataReferenceCache[assemblyName] = ret;
            return ret;
        }

        public async Task<Assembly> CompileToDLLAssembly(string sourceCode, string assemblyName = "", bool release = true, SourceCodeKind sourceCodeKind = SourceCodeKind.Regular)
        {
            if (string.IsNullOrEmpty(assemblyName)) assemblyName = Path.GetRandomFileName();
            var codeString = SourceText.From(sourceCode);
            var options = CSharpParseOptions.Default.WithLanguageVersion(LanguageVersion.CSharp11).WithKind(sourceCodeKind);
            var parsedSyntaxTree = SyntaxFactory.ParseSyntaxTree(codeString, options);
            var appAssemblies = Assembly.GetEntryAssembly()!.GetReferencedAssemblies().Select(o => Assembly.Load(o)).ToList();
            appAssemblies.Add(typeof(object).Assembly);
            var references = new List<MetadataReference>();
            foreach (var assembly in appAssemblies)
            {
                var metadataReference = await GetAssemblyMetadataReference(assembly);
                references.Add(metadataReference);
            }
            CSharpCompilation compilation;
            if (sourceCodeKind == SourceCodeKind.Script)
            {
                compilation = CSharpCompilation.CreateScriptCompilation(
                assemblyName,
                syntaxTree: parsedSyntaxTree,
                references: references,
                options: new CSharpCompilationOptions(
                        OutputKind.DynamicallyLinkedLibrary,
                        concurrentBuild: false,
                        optimizationLevel: release ? OptimizationLevel.Release : OptimizationLevel.Debug
                    )
                );
            }
            else
            {
                compilation = CSharpCompilation.Create(
                assemblyName,
                syntaxTrees: new[] { parsedSyntaxTree },
                references: references,
                options: new CSharpCompilationOptions(
                        OutputKind.DynamicallyLinkedLibrary,
                        concurrentBuild: false,
                        optimizationLevel: release ? OptimizationLevel.Release : OptimizationLevel.Debug
                    )
                );
            }
            using (var ms = new MemoryStream())
            {
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
