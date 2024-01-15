using System.Reflection.Metadata;

using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp.ProjectDecompiler;
using ICSharpCode.Decompiler.DebugInfo;
using ICSharpCode.Decompiler.Metadata;

namespace DSPDecompiler;

public class SpecificGuidWholeProjectDecompiler(
  DecompilerSettings settings,
  Guid projectGuid,
  IAssemblyResolver assemblyResolver,
  AssemblyReferenceClassifier assemblyReferenceClassifier,
  IDebugInfoProvider debugInfoProvider)
  : WholeProjectDecompiler(settings, projectGuid, assemblyResolver, assemblyReferenceClassifier, debugInfoProvider)
{
  public IList<IGrouping<string, TypeDefinitionHandle>> GetFilesToDecompileTypesIn(PEFile module)
  {
    var metadata = module.Metadata;
    var files = module.Metadata.GetTopLevelTypeDefinitions().Where(td => IncludeTypeWhenDecompilingProject(module, td))
      .GroupBy(GetFileFileNameForHandle, StringComparer.OrdinalIgnoreCase).ToList();
    return files;
    
    string GetFileFileNameForHandle(TypeDefinitionHandle h)
    {
      var type = metadata.GetTypeDefinition(h);
      string file = SanitizeFileName(metadata.GetString(type.Name) + ".cs");
      string ns = metadata.GetString(type.Namespace);
      if (string.IsNullOrEmpty(ns))
      {
        return file;
      }
      else
      {
        string dir = Settings.UseNestedDirectoriesForNamespaces ? CleanUpPath(ns) : CleanUpDirectoryName(ns);
        return Path.Combine(dir, file);
      }
    }
  }
}
