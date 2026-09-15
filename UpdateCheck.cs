using System.Diagnostics;

namespace Lane.Node.OpenAi;

/// <summary>
/// Compares this checkout and the packed Lane packages against their upstream repositories on startup, and prints a
/// suggestion when either has moved on. Every check is best effort: no git, no network or no repository means silence.
/// </summary>
public static class UpdateCheck
{
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(10);

    /// <summary>Runs both checks in the background so startup isn't held up by the network.</summary>
    public static void RunInBackground(string contentRoot) => _ = Task.Run(() =>
    {
        try
        {
            if (FindRoot(contentRoot) is not { } root) return;

            CheckNode(root);
            CheckPackages(root);
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

    private static void CheckPackages(string root)
    {
        string stamp = Path.Combine(root, "lane-packages", "source.txt");

        if (!File.Exists(stamp)) return;

        string? repo = null, reference = null, commit = null;

        foreach (string line in File.ReadAllLines(stamp))
        {
            int equals = line.IndexOf('=');

            if (equals <= 0) continue;

            string value = line[(equals + 1)..].Trim();

            switch (line[..equals].Trim())
            {
                case "repo":   repo      = value; break;
                case "ref":    reference = value; break;
                case "commit": commit    = value; break;
            }
        }

        if (string.IsNullOrEmpty(repo) || string.IsNullOrEmpty(reference) || string.IsNullOrEmpty(commit)) return;

        if (RemoteHead(root, repo, reference) is not { } latest || latest == commit) return;

        Report(
            $"The Lane packages are behind {repo} ({reference}).",
            $"Update them by running the install script again:  {(OperatingSystem.IsWindows() ? "install.bat" : "./install.sh")}");
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
