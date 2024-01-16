using System.ComponentModel.DataAnnotations;
using System.Diagnostics;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

using ICSharpCode.Decompiler;
using ICSharpCode.Decompiler.CSharp;
using ICSharpCode.Decompiler.CSharp.ProjectDecompiler;
using ICSharpCode.Decompiler.DebugInfo;
using ICSharpCode.Decompiler.Disassembler;
using ICSharpCode.Decompiler.Metadata;
using ICSharpCode.Decompiler.Solution;
using ICSharpCode.ILSpyX.PdbProvider;

using McMaster.Extensions.CommandLineUtils;

using Microsoft.Extensions.Hosting;

namespace DSPDecompiler;

[Command(Name = nameof(DSPDecompiler), Description = "Tool for decompiling the game Dyson Sphere Program by Youthcat Studios",
  ExtendedHelpText = $@"
Examples:
    Decompile Assembly-CSharp.dll
        {nameof(DSPDecompiler)} path/to/Assembly-CSharp.dll path/to/decompilation/output
")]
[HelpOption("-h|--help")]
class Program
{
  // https://natemcmaster.github.io/CommandLineUtils/docs/advanced/generic-host.html
  // https://github.com/natemcmaster/CommandLineUtils/blob/main/docs/samples/dependency-injection/generic-host/Program.cs
  public static Task<int> Main(string[] args) =>
    new HostBuilder().RunCommandLineApplicationAsync<Program>(args);

  [FileExists]
  [Required]
  [Argument(0, "The path to Assembly-CSharp.dll", "The path to Assembly-CSharp.dll file that is being decompiled. This argument is mandatory.")]
  public string DspAssemblyPath { get; }

  [Required]
  [Argument(1, "The output directory", "The output directory. This argument is mandatory.")]
  public string OutputDirectory { get; }
  
  // [FileExists]
  [Option("-s|--settings <path>", "Path to decompiler settings json", CommandOptionType.SingleValue)]
  public string SettingsPath { get; } = ""; 

  [Option("-lv|--languageversion <version>", "C# Language version: CSharp1, CSharp2, CSharp3, " +
                                             "CSharp4, CSharp5, CSharp6, CSharp7, CSharp7_1, CSharp7_2, CSharp7_3, CSharp8_0, CSharp9_0, " +
                                             "CSharp10_0, Preview or Latest", CommandOptionType.SingleValue)]
  public LanguageVersion LanguageVersion { get; } = LanguageVersion.CSharp7_3;

  [DirectoryExists]
  [Option("-r|--referencepath <path>",
    "Path to a directory containing dependencies of the assembly that is being decompiled.",
    CommandOptionType.MultipleValue)]
  public string[] ReferencePaths { get; }

  private readonly IHostEnvironment _env;

  public Program(IHostEnvironment env)
  {
    _env = env;
  }

  private async Task<int> OnExecuteAsync(CommandLineApplication app)
  {
    TextWriter output = System.Console.Out;
    string outputDirectory = ResolveOutputDirectory(OutputDirectory);

    if (outputDirectory == null)
    {
      return ProgramExitCodes.EX_SOFTWARE;
    }
    
    Directory.CreateDirectory(outputDirectory);

    try
    {
      var projectName = Path.GetFileNameWithoutExtension(DspAssemblyPath);
      string projectFileName = Path.Combine(outputDirectory, projectName, $"{projectName}.csproj");
      var projectId = DecompileAsProjectCustom(app, DspAssemblyPath, projectFileName, Guid.NewGuid());

      var project = new ProjectItem(projectFileName, projectId.PlatformName, projectId.Guid, projectId.TypeGuid);

      SolutionCreator.WriteSolutionFile(
        Path.Combine(outputDirectory, Path.GetFileNameWithoutExtension(DspAssemblyPath) + ".sln"), new []{project});

      return 0;
    }
    catch (Exception ex)
    {
      app.Error.WriteLine(ex.ToString());
      return ProgramExitCodes.EX_SOFTWARE;
    }
    finally
    {
      output.Close();
    }
  }

  private static string? ResolveOutputDirectory(string outputDirectory)
  {
    // path is not set
    if (string.IsNullOrWhiteSpace(outputDirectory))
      return null;
    // resolve relative path, backreferences ('.' and '..') and other
    // platform-specific path elements, like '~'.
    return Path.GetFullPath(outputDirectory);
  }

  DecompilerSettings GetSettings(PEFile module)
  {
    var settings = new DecompilerSettings()
    {
      ThrowOnAssemblyResolveErrors = false,
      UseSdkStyleProjectFormat = WholeProjectDecompiler.CanUseSdkStyleProjectFormat(module),
      UseNestedDirectoriesForNamespaces = true,
    };
    string settingsPath =
      string.IsNullOrEmpty(SettingsPath)
        ? Path.Combine(OutputDirectory, "decompilerSettings.json")
        : SettingsPath;
    
    if (File.Exists(settingsPath))
      settings.ReadFromJson(settingsPath);
    settings.SetLanguageVersion(LanguageVersion);
    if (!string.IsNullOrEmpty(settingsPath))
      settings.SaveToJson(settingsPath);
    return settings;
  }

  ProjectId DecompileAsProjectCustom(CommandLineApplication app, string assemblyFileName, string projectFileName, Guid projectGuid)
  {
    var modulePrefetched =
      new PEFile(assemblyFileName,
        new FileStream(assemblyFileName, FileMode.Open, FileAccess.Read),
        PEStreamOptions.PrefetchEntireImage,
        metadataOptions: MetadataReaderOptions.None); 
    var module = new PEFile(assemblyFileName);
    
    if (!PortablePdbWriter.HasCodeViewDebugDirectoryEntry(modulePrefetched))
    {
      app.Error.WriteLine(
        $"Cannot create PDB file for {assemblyFileName}, because it does not contain a PE Debug Directory Entry of type 'CodeView'.");
      throw new Exception("Duh");
    }
    
    var resolver = new UniversalAssemblyResolver(assemblyFileName, false, module.Metadata.DetectTargetFrameworkId());
    foreach (var path in (ReferencePaths ?? Array.Empty<string>()))
    {
      resolver.AddSearchDirectory(path);
    }

    var decompilerSettings = GetSettings(module);
    
    var projectDir = Path.GetDirectoryName(projectFileName);
    var projectLocalNameMapDir = $"{projectDir}-localnamemap";
    var projectILDir = $"{projectDir}-il";

    Directory.CreateDirectory(projectLocalNameMapDir);
    Console.WriteLine("Collecting Local Name Maps");
    var localNameMapsRead = LocalNameMapUtils.CollectLocalNameMapsFromDirectory(projectLocalNameMapDir);
    
    var metadata = module.Metadata;
    Console.WriteLine("Generating Compact Method Identifiers");
    var compactMethodIdentifiers = CompactMethodIdentifier.GenerateFrom(metadata);

    var overrideLocalNameMap = new Dictionary<MethodDefinitionHandle, Dictionary<string, string>>();

    Console.WriteLine("Generating Override Local Name Map");
    foreach (var methodDefinitionHandle in metadata.MethodDefinitions)
    {
      var methodIdentifier = compactMethodIdentifiers[methodDefinitionHandle];
      var localNameMap = localNameMapsRead.Find(x => x.Method == methodIdentifier);
      if (localNameMap == null)
        continue;
      
      overrideLocalNameMap[methodDefinitionHandle] = localNameMap.Locals;
    }
    
    var recordNamesHook = new RecordNamesHook(compactMethodIdentifiers, overrideLocalNameMap);
    var wrappedGenPdb = new WrappedDebugInfoProvider(new (), null, recordNamesHook);
    

    var decompiler = new SpecificGuidWholeProjectDecompiler(decompilerSettings, projectGuid, resolver, resolver, wrappedGenPdb);
    
    Console.WriteLine("Commencing Whole Project Decompilation");
    if (Directory.Exists(projectDir))
      Directory.Delete(projectDir, true);
    Directory.CreateDirectory(projectDir);
    using var projectFileWriter = new StreamWriter(File.OpenWrite(projectFileName));
    var genPdbPath = Path.GetTempFileName();
    ProjectId? projectId;
    {
      using var genPdbStream = File.OpenWrite(genPdbPath);
      var progressStopwatch = new Stopwatch();
      progressStopwatch.Start();
      decompiler.ProgressIndicator = new Progress<DecompilationProgress>(progress =>
      {
        if (progressStopwatch.Elapsed.TotalMilliseconds > 100)
        {
          progressStopwatch.Restart();
          var percentage = (progress.UnitsCompleted / (double)progress.TotalUnits).ToString("0.00%");
          Console.WriteLine($"Decompiling... {percentage} ({progress.UnitsCompleted} / {progress.TotalUnits}) : {progress.Status}");
        }
      });
      projectId = decompiler.DecompileProject(module, projectDir, projectFileWriter, genPdbStream);
    }
    
    Console.WriteLine("Writing Local Name Maps and disassembled IL");
    
    if (Directory.Exists(projectLocalNameMapDir))
      Directory.Delete(projectLocalNameMapDir, true);
    Directory.CreateDirectory(projectLocalNameMapDir);
    
    if (Directory.Exists(projectILDir))
      Directory.Delete(projectILDir, true);
    Directory.CreateDirectory(projectILDir);
    var files = decompiler.GetFilesToDecompileTypesIn(module).ToList();
    
    var lnmProgress = new DecompilationProgress { TotalUnits = files.Count, Title = "Writing Local Name Maps" };
    var lnmProgressStopwatch = new Stopwatch();
    lnmProgressStopwatch.Start();
    IProgress<DecompilationProgress> lnmProgressReporter = new Progress<DecompilationProgress>(p =>
    {
      if (lnmProgressStopwatch.Elapsed.TotalMilliseconds > 100)
      {
        lnmProgressStopwatch.Restart();
        var percentage = (p.UnitsCompleted / (double)p.TotalUnits).ToString("0.00%");
        Console.WriteLine($"{p.Title}... {percentage} ({p.UnitsCompleted} / {p.TotalUnits}) : {p.Status}");
      }
    });
    var ilProgress = new DecompilationProgress { TotalUnits = files.Count, Title = "Writing IL" };
    var ilProgressStopwatch = new Stopwatch();
    ilProgressStopwatch.Start();
    IProgress<DecompilationProgress> ilProgressReporter = new Progress<DecompilationProgress>(p =>
    {
      if (ilProgressStopwatch.Elapsed.TotalMilliseconds > 100)
      {
        ilProgressStopwatch.Restart();
        var percentage = (p.UnitsCompleted / (double)p.TotalUnits).ToString("0.00%");
        Console.WriteLine($"{p.Title}... {percentage} ({p.UnitsCompleted} / {p.TotalUnits}) : {p.Status}");
      }
    });

    Parallel.For(0, files.Count, new ParallelOptions() { MaxDegreeOfParallelism = Environment.ProcessorCount }, i =>
    {
      var fileTypes = files[i];
      var fileName = fileTypes.Key;
      var types = fileTypes.ToList();

      var localNameMaps = types.SelectMany(x =>
      {
        var typeDef = metadata.GetTypeDefinition(x);
        var methodDefHandles = typeDef.GetMethods();
        return methodDefHandles.Select(methodDefHandle =>
        {
          var methodIdentifier = compactMethodIdentifiers[methodDefHandle];
          var isPresent = recordNamesHook.RecordLocalNameMap.TryGetValue(methodDefHandle, out var locals);
          // if (!isPresent)
          //   Console.WriteLine($"{methodIdentifier} is not present in record local name map");
          return new LocalNameMap() { Method = methodIdentifier, Locals = locals ?? new() };
        }).Where(x => x.Locals.Count > 0);
      }).ToList();

      var mappingFileName = fileName + ".m.json";
      var mappingFilePath = Path.Combine(projectLocalNameMapDir, mappingFileName);
      Directory.CreateDirectory(Path.GetDirectoryName(mappingFilePath));
      try
      {
        File.WriteAllText(mappingFilePath, LocalNameMap.ToCompactJson(localNameMaps));
      }
      catch (Exception e)
      {
        Console.WriteLine(e);
        // throw;
      }
      
      lnmProgress.Status = mappingFileName;
      Interlocked.Increment(ref lnmProgress.UnitsCompleted);
      lnmProgressReporter?.Report(lnmProgress);

      var csFilePath = Path.Combine(projectDir, fileName);
      var ilFileName = fileName + ".il";
      var ilFilePath = Path.Combine(projectILDir, ilFileName);
      Directory.CreateDirectory(Path.GetDirectoryName(ilFilePath));
      {
        using var output = new StringWriter();
        var disassembler = new ReflectionDisassembler(new PlainTextOutput(output), CancellationToken.None)
        {
          DebugInfo = TryLoadPDB(module, genPdbPath), ShowSequencePoints = true
        };
        foreach (var type in types)
        {
          disassembler.DisassembleType(module, type);
          output.WriteLine();
        }

        var outputLines = output.ToString().Split(output.NewLine);
        File.WriteAllLines(ilFilePath, SequencePointTransformHelper.ReplaceWithCSharpCode(outputLines, csFilePath));
      }
      ilProgress.Status = ilFileName;
      Interlocked.Increment(ref ilProgress.UnitsCompleted);
      ilProgressReporter?.Report(ilProgress);
    });
    
    return projectId;
  }

  IDebugInfoProvider? TryLoadPDB(PEFile module, string? pdbFilePath = null)
  {
    if (pdbFilePath == null)
      return DebugInfoUtils.LoadSymbols(module);
    return DebugInfoUtils.FromFile(module, pdbFilePath);
  }
}
