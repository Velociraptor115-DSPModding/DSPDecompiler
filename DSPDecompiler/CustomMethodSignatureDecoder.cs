using System.Collections.Immutable;
using System.Reflection.Metadata;

using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.TypeSystem;

namespace DSPDecompiler;

public struct CustomGenericContext;

public class CustomMethodSignatureDecoder : ISignatureTypeProvider<string, CustomGenericContext>
{
  private FullTypeNameSignatureDecoder _fallbackDecoder;

  public CustomMethodSignatureDecoder(FullTypeNameSignatureDecoder fallbackDecoder)
  {
    _fallbackDecoder = fallbackDecoder;
  }
  
  public CustomMethodSignatureDecoder(MetadataReader metadata)
  {
    _fallbackDecoder = new FullTypeNameSignatureDecoder(metadata);
  }
  
  public string GetSZArrayType(string elementType)
  {
    return $"{elementType}[]";
  }

  public string GetArrayType(string elementType, ArrayShape shape)
  {
    return $"{elementType}[{new string(',', shape.Rank - 1)}]";
  }

  public string GetByReferenceType(string elementType)
  {
    return $"{elementType}&";
  }

  public string GetGenericInstantiation(string genericType, ImmutableArray<string> typeArguments)
  {
    return $"{genericType}<{string.Join(", ", typeArguments)}>";
  }

  public string GetPointerType(string elementType)
  {
    return $"{elementType}*";
  }

  public string GetPrimitiveType(PrimitiveTypeCode typeCode)
  {
    return typeCode switch
    {
      PrimitiveTypeCode.Boolean => "bool",
      PrimitiveTypeCode.Byte => "byte",
      PrimitiveTypeCode.Char => "char",
      PrimitiveTypeCode.Double => "double",
      PrimitiveTypeCode.Int16 => "short",
      PrimitiveTypeCode.Int32 => "int",
      PrimitiveTypeCode.Int64 => "long",
      PrimitiveTypeCode.IntPtr => "nint",
      PrimitiveTypeCode.Object => "object",
      PrimitiveTypeCode.SByte => "sbyte",
      PrimitiveTypeCode.Single => "float",
      PrimitiveTypeCode.String => "string",
      PrimitiveTypeCode.TypedReference => throw new NotImplementedException("Not sure about this one"),
      PrimitiveTypeCode.UInt16 => "ushort",
      PrimitiveTypeCode.UInt32 => "uint",
      PrimitiveTypeCode.UInt64 => "ulong",
      PrimitiveTypeCode.UIntPtr => "nuint",
      PrimitiveTypeCode.Void => "void",
      _ => throw new ArgumentOutOfRangeException(nameof(typeCode), typeCode, null)
    };
  }

  public string GetTypeFromDefinition(MetadataReader reader, TypeDefinitionHandle handle, byte rawTypeKind)
  {
    return FromFullTypeName(_fallbackDecoder.GetTypeFromDefinition(reader, handle, rawTypeKind));
  }

  public string GetTypeFromReference(MetadataReader reader, TypeReferenceHandle handle, byte rawTypeKind)
  {
    return FromFullTypeName(_fallbackDecoder.GetTypeFromReference(reader, handle, rawTypeKind));
  }

  public string GetFunctionPointerType(MethodSignature<string> signature)
  {
    throw new NotImplementedException("Not sure about this one either");
  }

  public string GetGenericMethodParameter(CustomGenericContext genericContext, int index)
  {
    return index > 0 ? $"T{index}" : "T";
  }

  public string GetGenericTypeParameter(CustomGenericContext genericContext, int index)
  {
    return index > 0 ? $"T{index}" : "T";
  }

  public string GetModifiedType(string modifier, string unmodifiedType, bool isRequired)
  {
    throw new NotImplementedException("Not sure about this one");
  }

  public string GetPinnedType(string elementType)
  {
    throw new NotImplementedException("Not sure about this one");
  }

  public string GetTypeFromSpecification(MetadataReader reader, CustomGenericContext genericContext,
    TypeSpecificationHandle handle, byte rawTypeKind)
  {
    return FromFullTypeName(_fallbackDecoder.GetTypeFromSpecification(reader, default, handle, rawTypeKind));
  }

  private static string FromFullTypeName(FullTypeName ftn)
  {
    if (!ftn.IsNested)
      return ftn.Name;
    var name = ftn.TopLevelTypeName.ReflectionName + TypeParameterModification(ftn.TopLevelTypeName.TypeParameterCount);
    for (int i = 0; i < ftn.NestingLevel; i++)
      name += "." + ftn.GetNestedTypeName(i) + TypeParameterModification(ftn.GetNestedTypeAdditionalTypeParameterCount(i));

    return name;
  }

  private static string TypeParameterModification(int typeParameterCount)
  {
    return typeParameterCount switch
    {
      0 => "",
      1 => "<T>",
      var x => $"<T,{Enumerable.Range(2, x - 1).Select(i => $"T{i}")}>"
    };
  }
}
