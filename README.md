# DSPDecompiler

This repo is meant to be used in conjunction with the forked [ILSpy](https://github.com/Velociraptor115-DSPModding/ILSpy) repo's custom-decompile branch.  
Meaning the folder structure has to be
```
- Repos
    |- DSPDecompiler
    |- ILSpy (custom-decompile)
```

## Minimal Usage:

Run the below command from the DSPDecompiler project directory  
`dotnet run -c Release "path/to/dll" "path/to/output"`