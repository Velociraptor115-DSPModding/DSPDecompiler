using System.Text.RegularExpressions;

namespace DSPDecompiler;

public static partial class SequencePointTransformHelper
{
  [GeneratedRegex(@"^(?<indent>\s+)\/\/ (?<toReplace>sequence point: \(line (?<lineStart>\d+), col (?<colStart>\d+)\) to \(line (?<lineEnd>\d+), col (?<colEnd>\d+)\).+)$", RegexOptions.Multiline)]
  public static partial Regex SequencePointMatchRegex();
  
  public static string ReplaceWithCSharpCode(string ilText, string csFilePath)
  {
    var lines = File.ReadAllLines(csFilePath);
    var matches = SequencePointMatchRegex().Matches(ilText);
    foreach (Match match in matches)
    {
      if (!match.Success)
        continue;
      var lineStart = int.Parse(match.Groups["lineStart"].Value);
      var lineEnd = int.Parse(match.Groups["lineEnd"].Value);
      var colStart = int.Parse(match.Groups["colStart"].Value);
      var colEnd = int.Parse(match.Groups["colEnd"].Value);
      var indent = match.Groups["indent"].Value;

      string replacement = "";

      if (lineStart != lineEnd)
      {
        if (colEnd <= colStart)
        {
          Console.WriteLine($"Warning: Sequence point spans multiple lines and doesn't seem to follow indents. Skipping. {csFilePath}");
          continue;
        }

        replacement += $"{indent}// {lines[lineStart - 1].Substring(colStart - 1)}";
        for (int i = lineStart + 1; i <= lineEnd - 1; i++)
        {
          replacement += $"{indent}// {lines[i - 1].Substring(colStart - 1)}";
        }
        replacement += $"{indent}// {lines[lineEnd - 1].Substring(colStart - 1, colEnd - colStart)}";
      }
      else
      {
        replacement = $"{indent}// {lines[lineStart - 1].Substring(colStart - 1, colEnd - colStart)}";
      }
      
      ilText = ilText.Replace(match.Value, replacement);
    }
    
    return ilText;
  } 

}
