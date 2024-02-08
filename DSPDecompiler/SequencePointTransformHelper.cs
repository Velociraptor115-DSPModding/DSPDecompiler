using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DSPDecompiler;

public static partial class SequencePointTransformHelper
{
  [GeneratedRegex(@"^(?<indent>\s+)\/\/ (?<toReplace>sequence point: \(line (?<lineStart>\d+), col (?<colStart>\d+)\) to \(line (?<lineEnd>\d+), col (?<colEnd>\d+)\).+)$")]
  public static partial Regex SequencePointMatchRegex();
  
  public static List<string> ReplaceWithCSharpCode(IEnumerable<string> ilLines, string csFilePath)
  {
    var csLines = File.ReadAllLines(csFilePath);
    var output = new List<string>();
    foreach (string ilLine in ilLines)
    {
      var matches = SequencePointMatchRegex().Matches(ilLine);
      if (matches.Count == 0)
      {
        output.Add(ilLine);
        continue;
      }
      foreach (Match match in matches)
      {
        if (!match.Success)
        {
          output.Add(ilLine);
          continue;
        }
        var lineStart = int.Parse(match.Groups["lineStart"].Value);
        var lineEnd = int.Parse(match.Groups["lineEnd"].Value);
        var colStart = int.Parse(match.Groups["colStart"].Value);
        var colEnd = int.Parse(match.Groups["colEnd"].Value);
        var indent = match.Groups["indent"].Value;

        if (lineStart != lineEnd)
        {
          if (colEnd <= colStart)
          {
            Console.WriteLine($"Warning: Sequence point spans multiple lines and doesn't seem to follow indents. Skipping. {csFilePath}");
            continue;
          }

          output.Add($"{indent}// {csLines[lineStart - 1].Substring(colStart - 1)}");
          for (int i = lineStart + 1; i <= lineEnd - 1; i++)
          {
            output.Add($"{indent}// {csLines[i - 1].Substring(colStart - 1)}");
          }
          output.Add($"{indent}// {csLines[lineEnd - 1].Substring(colStart - 1, colEnd - colStart)}");
        }
        else
        {
          output.Add($"{indent}// {csLines[lineStart - 1].Substring(colStart - 1, colEnd - colStart)}");
        }
      }
    }
    
    return output;
  } 

}
