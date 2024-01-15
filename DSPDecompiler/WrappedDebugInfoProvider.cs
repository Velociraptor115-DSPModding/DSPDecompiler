using System.Collections.Concurrent;
using System.Reflection.Metadata;
using ICSharpCode.Decompiler.DebugInfo;
using ICSharpCode.Decompiler.IL;
using ICSharpCode.Decompiler.IL.Transforms;

using SequencePoint = ICSharpCode.Decompiler.DebugInfo.SequencePoint;

namespace DSPDecompiler;

public class EmptyDebugInfoProvider : IDebugInfoProvider
{
  public string Description => nameof(EmptyDebugInfoProvider);

  public IList<SequencePoint> GetSequencePoints(MethodDefinitionHandle method) => new List<SequencePoint>();

  public IList<Variable> GetVariables(MethodDefinitionHandle method) => new List<Variable>();

  public bool TryGetName(MethodDefinitionHandle method, int index, out string name)
  {
    name = string.Empty;
    return false;
  }

  public bool TryGetExtraTypeInfo(MethodDefinitionHandle method, int index, out PdbExtraTypeInfo extraTypeInfo)
  {
    extraTypeInfo = new PdbExtraTypeInfo();
    return false;
  }

  public string SourceFileName => string.Empty;
  
  public static readonly EmptyDebugInfoProvider Instance = new();
}

public class WrappedDebugInfoProvider(
  Dictionary<MethodDefinitionHandle, List<string>> localNameMap,
  IDebugInfoProvider? optionalBaseProvider = null,
  IAssignVariableNamesVariablesHook? assignVariableNamesHook = null
) : IDebugInfoProvider, IAssignVariableNamesVariablesHook
{
  public string Description => nameof(WrappedDebugInfoProvider);
  private IDebugInfoProvider BaseProvider => optionalBaseProvider ?? EmptyDebugInfoProvider.Instance;

  public bool TryGetExtraTypeInfo(MethodDefinitionHandle method, int index, out PdbExtraTypeInfo extraTypeInfo)
    => BaseProvider.TryGetExtraTypeInfo(method, index, out extraTypeInfo);

  public string SourceFileName => nameof(WrappedDebugInfoProvider);

  public IList<SequencePoint> GetSequencePoints(MethodDefinitionHandle method) =>
    BaseProvider.GetSequencePoints(method);

  public IList<Variable> GetVariables(MethodDefinitionHandle method)
  {
    var variables = BaseProvider.GetVariables(method).ToList();
    variables.Sort((a, b) => a.Index - b.Index);
    for (int i = 0; i < variables.Count; i++)
    {
      var intercept = NameIntercept(method, variables[i].Index);
      if (!string.IsNullOrWhiteSpace(intercept))
        variables[i] = new Variable(variables[i].Index, intercept);
    }

    return variables;
  }

  public bool TryGetName(MethodDefinitionHandle method, int index, out string name)
  {
    var intercept = NameIntercept(method, index);
    if (!string.IsNullOrWhiteSpace(intercept))
    {
      name = intercept;
      return true;
    }

    return BaseProvider.TryGetName(method, index, out name);
  }

  private string NameIntercept(MethodDefinitionHandle method, int index)
  {
    if (localNameMap.TryGetValue(method, out var localNames))
    {
      if (index < localNames.Count)
        return localNames[index];
    }

    return string.Empty;
  }

  public string? PreGenerateName(ILTransformContext context, ILVariable variable)
  {
    return assignVariableNamesHook?.PreGenerateName(context, variable);
  }

  public string PostGenerateName(ILTransformContext context, ILVariable variable, string proposedName)
  {
    return assignVariableNamesHook?.PostGenerateName(context, variable, proposedName) ?? proposedName;
  }
}

public class RecordNamesHook(
  Dictionary<MethodDefinitionHandle, CompactMethodIdentifier> methodIdentifiers,
  Dictionary<MethodDefinitionHandle, Dictionary<string, string>> ApplyLocalNameMap
) : IAssignVariableNamesVariablesHook
{
  public ConcurrentDictionary<MethodDefinitionHandle, Dictionary<string, string>> RecordLocalNameMap { get; } = new();

  public string? PreGenerateName(ILTransformContext context, ILVariable variable)
  {
    var metadataTokenOpt = variable.Function?.Method?.MetadataToken;
    if (metadataTokenOpt == null)
    {
      return null;
    }

    var metadataToken = metadataTokenOpt.Value;
    var methodDefHandle = (MethodDefinitionHandle)metadataToken;

    if (!ApplyLocalNameMap.ContainsKey(methodDefHandle))
      return null;

    var lNameMap = ApplyLocalNameMap[methodDefHandle];

    var indexStr = $"{variable.IndexInFunction}|{variable.Index}";

    if (!lNameMap.ContainsKey(indexStr))
      return null;

    var proposedName = lNameMap[indexStr];

    // if (!(variable.HasGeneratedName && proposedName.StartsWith("current")))
    // {
    //   variable.HasGeneratedName = false;
    // }
    return proposedName;
  }

  public string PostGenerateName(ILTransformContext context, ILVariable variable, string proposedName)
  {
    var metadataTokenOpt = variable.Function?.Method?.MetadataToken;
    if (metadataTokenOpt == null)
    {
      Console.WriteLine("Metadata Token null for variable generation. Please Check");
      return proposedName;
    }

    var metadataToken = metadataTokenOpt.Value;
    var methodDefHandle = (MethodDefinitionHandle)metadataToken;

    if (!RecordLocalNameMap.ContainsKey(methodDefHandle))
    {
      RecordLocalNameMap[methodDefHandle] = new();
    }

    var lNameMap = RecordLocalNameMap[methodDefHandle];

    var indexStr = $"{variable.IndexInFunction}|{variable.Index}";
    if (lNameMap.ContainsKey(indexStr) && lNameMap[indexStr] != proposedName)
    {
      Console.WriteLine(
        $"RecordLocalNameMap already contains key: {indexStr} for method: {methodIdentifiers[methodDefHandle]} with different value: {lNameMap[indexStr]} than proposed currently: {proposedName}");
    }

    lNameMap[indexStr] = proposedName;

    return proposedName;
  }
}
