namespace CAP.Avalonia.ViewModels.Logic.IsaPlayground;

/// <summary>
/// The ISA sample programs the playground offers in its picker: the shipped
/// samples from <c>examples/isa/</c>. Missing files are skipped, so a partial
/// installation degrades instead of failing; never throws.
/// </summary>
public sealed class IsaSampleProgramCatalog
{
    /// <summary>Directory (relative to a repo/install root) that holds the shipped samples.</summary>
    private const string SamplesRelativePath = "examples/isa";

    /// <summary>How many parent directories <see cref="LoadDefault"/> walks up at most.</summary>
    private const int MaxWalkUpLevels = 8;

    private IsaSampleProgramCatalog(IReadOnlyList<IsaSampleProgram> samples)
    {
        Samples = samples;
    }

    /// <summary>The samples that were found on disk, in picker order.</summary>
    public IReadOnlyList<IsaSampleProgram> Samples { get; }

    /// <summary>
    /// Loads the catalog from a directory that directly contains the sample files.
    /// Samples whose file is absent are skipped.
    /// </summary>
    /// <param name="samplesDirectory">Directory holding the <c>.asm</c> sample files.</param>
    public IsaSampleProgramCatalog(string samplesDirectory)
        : this(LoadSamples(samplesDirectory))
    {
    }

    /// <summary>
    /// Walks up from the application base directory looking for <c>examples/isa</c>
    /// (same strategy as <c>ExampleDesignsService</c>) and loads the samples found
    /// there. Yields an empty catalog when the directory does not exist — never throws.
    /// </summary>
    public static IsaSampleProgramCatalog LoadDefault()
    {
        var current = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
        for (int level = 0; current != null && level < MaxWalkUpLevels; level++)
        {
            var candidate = Path.Combine(current.FullName, SamplesRelativePath);
            if (Directory.Exists(candidate))
            {
                return new IsaSampleProgramCatalog(candidate);
            }

            current = current.Parent;
        }

        return new IsaSampleProgramCatalog(Array.Empty<IsaSampleProgram>());
    }

    private static IReadOnlyList<IsaSampleProgram> LoadSamples(string samplesDirectory)
    {
        var samples = new List<IsaSampleProgram>();
        TryAdd(samples, samplesDirectory, "count-to-5.asm", "IsaPlayground.SampleCountTo5");
        TryAdd(samples, samplesDirectory, "add-two-numbers.asm", "IsaPlayground.SampleAddTwoNumbers");
        TryAdd(samples, samplesDirectory, "multiply-3x4.asm", "IsaPlayground.SampleMultiply3x4");
        return samples;
    }

    private static void TryAdd(List<IsaSampleProgram> samples, string directory, string fileName, string displayNameKey)
    {
        var path = Path.Combine(directory, fileName);
        if (File.Exists(path))
        {
            samples.Add(new IsaSampleProgram(fileName, displayNameKey, File.ReadAllText(path)));
        }
    }
}
