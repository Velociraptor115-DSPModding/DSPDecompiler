using System.Reflection;
using System.Text.Json;

using ICSharpCode.Decompiler;

namespace DSPDecompiler;

public static class DecompilerSettingsExtensions
{
  private static JsonSerializerOptions _serializerOptions = new JsonSerializerOptions()
  {
    WriteIndented = true,
    AllowTrailingCommas = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
  }; 
  
  public static void SaveToJson(this DecompilerSettings settings, string path)
  {
    var json = new Dictionary<string, bool>();
    var type = typeof(DecompilerSettings);
    var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
    foreach (PropertyInfo property in properties)
    {
      if (property.PropertyType != typeof(bool))
        continue;
      var value = (bool)property.GetValue(settings);
      json[property.Name] = value;
    }
    File.WriteAllText(path, JsonSerializer.Serialize(json, _serializerOptions));
  }

  public static void ReadFromJson(this DecompilerSettings settings, string path)
  {
    var json = JsonSerializer.Deserialize<Dictionary<string, bool>>(File.ReadAllText(path), _serializerOptions);
    var type = typeof(DecompilerSettings);
    var properties = type.GetProperties(BindingFlags.Instance | BindingFlags.Public);
    foreach (PropertyInfo property in properties)
    {
      if (json.TryGetValue(property.Name, out bool value))
      {
        property.SetValue(settings, value);
      }
    }
  }
}
