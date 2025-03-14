## Superseded
This project is superseded by [SpawnDev.BlazorJS.CodeRunner](https://github.com/LostBeard/SpawnDev.BlazorJS.CodeRunner). It takes the methods in this repo, and some great work by others to solve some problems in newer versions of .Net Blazor and improve compatibility.

# BlazorWASMScriptLoader
Basic example of how to compile and run .Net code at runtime in Blazor WebAssembly.

### Update for .Net 8:
In .Net 8, Blazor WASM apps use .wasm files by default instead of .dll files. 
This is the Webcil format and it "addresses environments that block clients from downloading and executing DLLs". 
The problem is .wasm files do not contain the MetadataReference data like the .dll files do. A workaround is to switch back to using .dlls by using the ```<WasmEnableWebcil>false</WasmEnableWebcil>``` tag in the project .csproj file.

