using System.Reflection.Metadata;
using System.Text.Encodings.Web;
using System.Text.Json;

using ICSharpCode.Decompiler;

namespace DSPDecompiler;

using CJFLocalCompositeIndex = string;
using CJFLocalName = string;
using CompactJsonFormat = Dictionary<string, Dictionary<string, Dictionary<string, string>>>;

public class LocalNameMap
{
  public CompactMethodIdentifier Method { get; set; }
  public Dictionary<CJFLocalCompositeIndex, CJFLocalName> Locals { get; set; }

  private static JsonSerializerOptions _serializerOptions = new()
  {
    WriteIndented = true,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
  };

  public static string ToCompactJson(IList<LocalNameMap> data)
  {
    var groupedByType = data.GroupBy(x => x.Method.TypeName).ToDictionary(
      x => x.Key,
      x => x.ToDictionary(
        x => x.Method.NameWithOptionalSignature,
        x => x.Locals
      )
    );

    return JsonSerializer.Serialize(groupedByType, _serializerOptions);
  }
  
  public static IList<LocalNameMap> FromCompactJson(string data)
  {
    var groupedByType = JsonSerializer.Deserialize<CompactJsonFormat>(data, _serializerOptions);
    return groupedByType.SelectMany(x =>
    {
      var typeName = x.Key;
      return x.Value.Select(y =>
      {
        return new LocalNameMap()
        {
          Method = new CompactMethodIdentifier(typeName, y.Key),
          Locals = y.Value
        };
      });
    }).ToList();
  }
}

public static class LocalNameMapUtils
{
  public static List<LocalNameMap> CollectLocalNameMapsFromDirectory(string dir, string searchPattern = "*.m.json")
  {
    var mappingFiles = Directory.GetFiles(dir, searchPattern, SearchOption.AllDirectories);
    var localNameMapsRead = mappingFiles.SelectMany(mappingFile =>
    {
      var mappingFileContent = File.ReadAllText(mappingFile);
      return LocalNameMap.FromCompactJson(mappingFileContent);
    }).ToList();
    return localNameMapsRead;
  }
}

public record CompactMethodIdentifier(string TypeName, string NameWithOptionalSignature)
{
  public override string ToString() => $"{TypeName}.{NameWithOptionalSignature}";

  public static Dictionary<MethodDefinitionHandle, CompactMethodIdentifier> GenerateFrom(MetadataReader metadata)
  {
    var signatureDecoder = new CustomMethodSignatureDecoder(metadata);
    var types = metadata.TypeDefinitions;
    return types.SelectMany(x =>
    {
      var typeDef = metadata.GetTypeDefinition(x);
      var typeFullName = typeDef.GetFullTypeName(metadata);

      var methodDefHandles = typeDef.GetMethods();
      var methodNames = methodDefHandles.Select(methodDefHandle =>
      {
        var methodDef = metadata.GetMethodDefinition(methodDefHandle);
        var methodName = metadata.GetString(methodDef.Name);
        return methodName;
      });

      return methodDefHandles.Select(methodDefHandle =>
      {
        var methodDef = metadata.GetMethodDefinition(methodDefHandle);
        var methodName = metadata.GetString(methodDef.Name);
        var methodSignature = methodDef.DecodeSignature(signatureDecoder, default);
        var methodWithSignature = GenerateMinimalSignature(methodName, methodSignature);
        var duplicateMethodName = methodNames.Count(y => y == methodName) > 1;
        return new KeyValuePair<MethodDefinitionHandle, CompactMethodIdentifier>(
          methodDefHandle,
          new CompactMethodIdentifier(typeFullName.ReflectionName,
            duplicateMethodName ? methodWithSignature : methodName)
        );
      });
    }).ToDictionary();
  }

  static string GenerateMinimalSignature(string methodName, MethodSignature<string> signature)
  {
    var typeParameterStr = signature.GenericParameterCount switch
    {
      0 => "",
      1 => "<T>",
      var x => $"<T,{Enumerable.Range(2, x - 1).Select(i => $"T{i}")}>",
    };
    var parameterStr = string.Join(",", signature.ParameterTypes);
    return $"{signature.ReturnType} {methodName}{typeParameterStr}({parameterStr})";
  }
}
