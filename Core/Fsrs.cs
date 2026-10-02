using System.Reflection;
using System.Runtime.InteropServices;

namespace PalaceRoomViewer.Core;

// One review of one Locus: 1 = Again … 4 = Easy, and whole days since its previous review.
public readonly record struct FsrsReview(int Rating, int DeltaDays)
{
    public const int Again = 1, Good = 3;
}

public readonly record struct FsrsMemory(float Stability, float Difficulty);

// Calls the FSRS scheduler in Native/fsrs-ffi (the Rust fsrs crate) with its default FSRS-6 parameters.
public static partial class Fsrs
{
    private const string Library = "fsrs_ffi";
    private const uint AbiVersion = 1;
    public const double DesiredRetention = .9;
    private static readonly Lazy<float[]> s_parameters = new(LoadParameters);

    static Fsrs()
    {
        // Godot loads the viewer's assemblies into its own load context, which only finds native
        // libraries listed in .deps.json. The DLL is copied beside this assembly, so look there first.
        try { NativeLibrary.SetDllImportResolver(typeof(Fsrs).Assembly, Resolve); }
        catch (InvalidOperationException) { } // A resolver was already set for this assembly.
    }

    public static IReadOnlyList<float> Parameters => s_parameters.Value;
    public static float Decay => s_parameters.Value[20];

    // Replays each history (oldest first, at least one review each) into a memory state.
    public static IReadOnlyList<FsrsMemory> MemoryStates(IReadOnlyList<IReadOnlyList<FsrsReview>> histories)
    {
        _ = s_parameters.Value;
        if (histories.Count == 0) return [];
        if (histories.Any(h => h.Count == 0)) throw new ArgumentException("Every history needs at least one review.", nameof(histories));
        var reviews = histories.SelectMany(h => h).ToArray();
        var ratings = reviews.Select(r => (uint)r.Rating).ToArray();
        var deltas = reviews.Select(r => (uint)Math.Max(0, r.DeltaDays)).ToArray();
        var lengths = histories.Select(h => (uint)h.Count).ToArray();
        var stability = new float[histories.Count];
        var difficulty = new float[histories.Count];
        var parameters = s_parameters.Value;
        int code;
        unsafe
        {
            fixed (float* p = parameters)
            fixed (uint* r = ratings, d = deltas, l = lengths)
            fixed (float* s = stability, f = difficulty)
                code = fsrs_ffi_memory_states(p, r, d, l, (nuint)histories.Count, s, f);
        }
        if (code != 0) throw new ViewerException($"The FSRS scheduler rejected a review history (code {code}).");
        return stability.Select((s, i) => new FsrsMemory(s, difficulty[i])).ToArray();
    }

    // Probability of recalling a Locus after the given number of days since its last review.
    public static double Retrievability(FsrsMemory memory, double days)
    {
        var recall = fsrs_ffi_retrievability(memory.Stability, (float)Math.Max(0, days), Decay);
        return recall < 0 ? throw new ViewerException("The FSRS scheduler could not compute recall.") : recall;
    }

    private static float[] LoadParameters()
    {
        try
        {
            var version = fsrs_ffi_abi_version();
            if (version != AbiVersion)
                throw new ViewerException($"fsrs_ffi.dll is version {version}; the viewer needs version {AbiVersion}. Rebuild Native/fsrs-ffi.");
            var parameters = new float[21];
            unsafe { fixed (float* p = parameters) fsrs_ffi_default_parameters(p); }
            return parameters;
        }
        catch (Exception ex) when (ex is DllNotFoundException or EntryPointNotFoundException or BadImageFormatException)
        {
            throw new ViewerException($"The FSRS scheduler (fsrs_ffi.dll) could not be loaded.\n{ex.Message}", ex);
        }
    }

    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (name != Library) return IntPtr.Zero;
        foreach (var directory in new[] { Path.GetDirectoryName(assembly.Location), AppContext.BaseDirectory })
        {
            if (string.IsNullOrEmpty(directory)) continue;
            var candidate = Path.Combine(directory, Library + ".dll");
            if (File.Exists(candidate) && NativeLibrary.TryLoad(candidate, out var handle)) return handle;
        }
        return IntPtr.Zero;
    }

    [LibraryImport(Library)] private static partial uint fsrs_ffi_abi_version();
    [LibraryImport(Library)] private static unsafe partial int fsrs_ffi_default_parameters(float* output);
    [LibraryImport(Library)]
    private static unsafe partial int fsrs_ffi_memory_states(float* parameters, uint* ratings, uint* deltaDays, uint* lengths,
        nuint itemCount, float* outStability, float* outDifficulty);
    [LibraryImport(Library)] private static partial float fsrs_ffi_retrievability(float stability, float daysElapsed, float decay);
}
