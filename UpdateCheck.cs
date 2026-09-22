using System.Diagnostics;
using System.Reflection;
using System.Text.Json;

namespace Lane.Node.OpenAi;

/// <summary>
/// Compares this checkout against its upstream repository and the Lane packages against nuget.org on startup, and
/// prints a suggestion when either has moved on. Every check is best effort: no git, no network or no repository means silence.
/// </summary>
public static class UpdateCheck
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>Runs both checks in the background so startup isn't held up by the network.</summary>
    public static void RunInBackground(string contentRoot) => _ = Task.Run(async () =>
    {
        try
        {
            if (FindRoot(contentRoot) is { } root) CheckNode(root);

            await CheckPackagesAsync();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
        }
    });

    /// <summary>The repository folder: the nearest ancestor holding this project file.</summary>
    private static string? FindRoot(string contentRoot)
    {
        foreach (string start in new[] { contentRoot, AppContext.BaseDirectory })
        {
            for (DirectoryInfo? dir = new(start); dir is not null; dir = dir.Parent)
                if (File.Exists(Path.Combine(dir.FullName, "Lane.Node.OpenAi.csproj")))
                    return dir.FullName;
        }

        return null;
    }

    // --- this repository -----------------------------------------------------

    private static void CheckNode(string root)
    {
        if (!Directory.Exists(Path.Combine(root, ".git"))) return;

        if (Git(root, "rev-parse", "HEAD") is not { } head) return;

        // Prefer the branch's upstream; fall back to origin and the branch's own name.
        string remote, branch;

        if (Git(root, "rev-parse", "--abbrev-ref", "--symbolic-full-name", "@{u}") is { } upstream &&
            upstream.IndexOf('/') is var slash && slash > 0)
        {
            remote = upstream[..slash];
            branch = upstream[(slash + 1)..];
        }
        else
        {
            branch = Git(root, "rev-parse", "--abbrev-ref", "HEAD") ?? "";
            remote = "origin";

            if (branch is "" or "HEAD") return; // Detached: nothing to compare against.
        }

        if (RemoteHead(root, remote, branch) is not { } latest || latest == head) return;

        // A remote commit already in our history means we're simply ahead of it.
        if (Git(root, "merge-base", "--is-ancestor", latest, head) is not null) return;

        Report(
            "This node's repository has new commits.",
            $"Update it with:  git -C \"{root}\" pull");
    }

    // --- the Lane packages ---------------------------------------------------

    private static readonly string[] LanePackages = ["Lane.Core", "Lane.Nodes.Protocol", "Lane.Node.Sdk", "Lane.Providers"];

    private static async Task CheckPackagesAsync()
    {
        using HttpClient http = new() { Timeout = Timeout };

        List<string> behind = [];

        foreach (string id in LanePackages)
        {
            if (LoadedVersion(id) is not { } current) continue;
            if (await LatestVersionAsync(http, id) is not { } latest || latest <= current) continue;

            behind.Add($"{id} {current} -> {latest}");
        }

        if (behind.Count == 0) return;

        Report(
            $"Newer Lane packages are on NuGet: {string.Join(", ", behind)}.",
            "Pick them up with:  dotnet restore --force-evaluate");
    }

    /// <summary>The package version of the Lane assembly this node runs with, or null if it can't be read.</summary>
    private static Version? LoadedVersion(string id)
    {
        try
        {
            Assembly assembly = Assembly.Load(new AssemblyName(id));

            // Packing stamps the package version (plus "+<commit>") as the informational version.
            string? informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;

            if (informational is not null && Version.TryParse(informational.Split('+', '-')[0], out Version? version))
                return version;

            return assembly.GetName().Version;
        }
        catch (Exception ex) when (ex is FileNotFoundException or FileLoadException or BadImageFormatException)
        {
            return null;
        }
    }

    /// <summary>The newest stable version of <paramref name="id"/> on nuget.org, or null if it can't be read.</summary>
    private static async Task<Version?> LatestVersionAsync(HttpClient http, string id)
    {
        try
        {
            string url = $"https://api.nuget.org/v3-flatcontainer/{id.ToLowerInvariant()}/index.json";

            using JsonDocument index = JsonDocument.Parse(await http.GetStringAsync(url));

            Version? latest = null;

            foreach (JsonElement entry in index.RootElement.GetProperty("versions").EnumerateArray())
            {
                // Skip prereleases ("1.0.0-beta"), which contain a dash.
                if (entry.GetString() is { } text && !text.Contains('-') &&
                    Version.TryParse(text, out Version? version) && (latest is null || version > latest))
                    latest = version;
            }

            return latest;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException or
                                         KeyNotFoundException or InvalidOperationException)
        {
            return null;
        }
    }

    // --- helpers -------------------------------------------------------------

    /// <summary>The commit a branch or tag points at in <paramref name="remote"/>, or null if it can't be read.</summary>
    private static string? RemoteHead(string root, string remote, string reference)
    {
        if (Git(root, "ls-remote", remote, reference) is not { } output || output.Length == 0) return null;

        // "<sha>\trefs/heads/<branch>" per line; tags may also list the dereferenced "refs/tags/<tag>^{}".
        foreach (string line in output.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            int tab = line.IndexOf('\t');

            if (tab > 0 && !line.EndsWith("^{}", StringComparison.Ordinal)) return line[..tab].Trim();
        }

        return null;
    }

    /// <summary>Runs git in <paramref name="root"/>, returning its trimmed output, or null if it failed.</summary>
    private static string? Git(string root, params string[] arguments)
    {
        ProcessStartInfo info = new("git")
        {
            WorkingDirectory       = root,
            RedirectStandardOutput = true,
            RedirectStandardError  = true,
            UseShellExecute        = false
        };

        foreach (string argument in arguments) info.ArgumentList.Add(argument);

        // Never let git stop to ask for credentials; an unreachable remote should just mean no check.
        info.Environment["GIT_TERMINAL_PROMPT"] = "0";
        info.Environment["GIT_ASKPASS"]         = "echo";
        info.Environment["GCM_INTERACTIVE"]     = "never";

        try
        {
            using Process? git = Process.Start(info);

            if (git is null) return null;

            string output = git.StandardOutput.ReadToEnd();

            if (!git.WaitForExit(Timeout))
            {
                git.Kill(entireProcessTree: true);
                return null;
            }

            return git.ExitCode == 0 ? output.Trim() : null;
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException or IOException)
        {
            return null; // git isn't installed, most likely.
        }
    }

    private static void Report(string headline, string action)
    {
        Console.WriteLine();
        Console.WriteLine($"  Update available: {headline}");
        Console.WriteLine($"  {action}");
        Console.WriteLine();
    }
}
