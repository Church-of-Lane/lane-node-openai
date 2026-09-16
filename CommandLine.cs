using Lane.Core.Models;

namespace Lane.Node.OpenAi;

/// <summary>
/// The command line, which can set every field the page's form edits. Values given here are the node's settings when the
/// page opens, on top of whatever was saved last; they are only written to disk once the node is started.
/// </summary>
/// <param name="HostArgs">Arguments left for the web host, such as <c>--urls</c>.</param>
public sealed record CommandLine(
    string[]      HostArgs,
    NodeOverrides Overrides,
    string?       ApiKey,
    bool          OpenBrowser,
    bool          CheckUpdates)
{
    /// <summary>Host arguments passed straight through; everything else must be an option this class knows.</summary>
    private static readonly string[] HostOptions = ["--urls", "--environment", "--contentroot", "--applicationname"];

    /// <exception cref="ArgumentException">An option is unknown, or its value is missing or malformed.</exception>
    public static CommandLine Parse(string[] args)
    {
        List<string>  host         = [];
        NodeOverrides overrides    = new();
        string?       apiKey       = null;
        bool          openBrowser  = true;
        bool          checkUpdates = Environment.GetEnvironmentVariable("LANE_NO_UPDATE_CHECK") is null;

        for (int i = 0; i < args.Length; i++)
        {
            string arg = args[i];
            int    eq  = arg.IndexOf('=');

            string name     = (eq < 0 ? arg : arg[..eq]).ToLowerInvariant();
            string? inline  = eq < 0 ? null : arg[(eq + 1)..];

            // Reads this option's value, from --name=value or from the next argument. An option cannot take another
            // option as its value, so a forgotten value is an error rather than a swallowed option.
            string Value()
            {
                if (inline is not null) return inline;

                if (i + 1 >= args.Length || args[i + 1].StartsWith("--", StringComparison.Ordinal))
                    throw new ArgumentException($"{name} needs a value, as '{name} <value>' or '{name}=<value>'.");

                return args[++i];
            }

            void NoValue()
            {
                if (inline is not null) throw new ArgumentException($"{name} does not take a value.");
            }

            switch (name)
            {
                case "--no-browser":      NoValue(); openBrowser  = false; break;
                case "--no-update-check": NoValue(); checkUpdates = false; break;

                case "--lane-url":     overrides = overrides with { LaneUrl = Value() }; break;
                case "--pool":         overrides = overrides with { Pool    = Value() }; break;
                case "--name":
                case "--node-name":    overrides = overrides with { Name    = Value() }; break;
                case "--model":        overrides = overrides with { Model   = Value() }; break;
                case "--concurrency":  overrides = overrides with { Concurrency = ParseCount(Value()) }; break;

                case "--provider":     overrides = overrides with { Provider            = ParseProvider(Value()) }; break;
                case "--endpoint":     overrides = overrides with { Endpoint            = Value() }; break;
                case "--chat-completions-path": overrides = overrides with { ChatCompletionsPath = Value() }; break;
                case "--models-path":  overrides = overrides with { ModelsPath          = Value() }; break;

                case "--header":       overrides = overrides with { Headers = [.. overrides.Headers ?? [], ParseHeader(Value())] }; break;
                case "--no-headers":   NoValue(); overrides = overrides with { Headers = [] }; break;

                case "--capability":
                case "--capabilities": overrides = overrides with { Capabilities = [.. overrides.Capabilities ?? [], .. ParseCapabilities(Value())] }; break;
                case "--no-capabilities": NoValue(); overrides = overrides with { Capabilities = [] }; break;

                case "--api-key":      apiKey = Value(); break;

                case "--identity":     overrides = overrides with { Identity    = ParseIdentity(Value()) }; break;
                case "--key-file":     overrides = overrides with { KeyFilePath = Path.GetFullPath(Value().Trim()) }; break;

                case "--security-key-credential-id": overrides = overrides with { SecurityKeyCredentialId = Value() }; break;
                case "--security-key-public-key":    overrides = overrides with { SecurityKeyPublicKey    = Value() }; break;

                default:
                    if (!HostOptions.Contains(name))
                        throw new ArgumentException($"Unknown option '{name}'. Run with --help to see the options.");

                    host.Add(arg);
                    if (inline is null && i + 1 < args.Length) host.Add(args[++i]);
                    break;
            }
        }

        return new CommandLine([.. host], overrides, apiKey, openBrowser, checkUpdates);
    }

    public static bool WantsHelp(string[] args) => args.Any(a => a is "--help" or "-h" or "-?" or "/?");

    private static int ParseCount(string value) =>
        int.TryParse(value, out int count) && count >= 1
            ? count
            : throw new ArgumentException("--concurrency must be a whole number of at least 1.");

    private static string ParseProvider(string value) =>
        ProviderTemplates.All.FirstOrDefault(t => string.Equals(t.Id, value.Trim(), StringComparison.OrdinalIgnoreCase))?.Id
        ?? throw new ArgumentException($"Unknown provider '{value}'. Choose one of {ProviderIds}.");

    private static HeaderSetting ParseHeader(string value)
    {
        int colon = value.IndexOf(':');

        if (colon <= 0) throw new ArgumentException($"--header wants 'Name: value', not '{value}'.");

        return new HeaderSetting(value[..colon].Trim(), value[(colon + 1)..].Trim());
    }

    private static List<string> ParseCapabilities(string value)
    {
        List<string> names = [];

        foreach (string one in value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Enum.TryParse(one, ignoreCase: true, out ModelCapabilities _))
                throw new ArgumentException($"Unknown capability '{one}'. Choose from {CapabilityNames}.");

            names.Add(one);
        }

        return names;
    }

    private static IdentitySource ParseIdentity(string value) => value.Trim().ToLowerInvariant() switch
    {
        "security-key" or "securitykey" or "key" => IdentitySource.SecurityKey,
        "key-file" or "keyfile" or "file"        => IdentitySource.KeyFile,
        _ => throw new ArgumentException($"--identity must be security-key or key-file, not '{value}'.")
    };

    private static string ProviderIds => string.Join(", ", ProviderTemplates.All.Select(t => t.Id));

    private static string[] Capabilities =>
        [.. Enum.GetNames<ModelCapabilities>().Where(n => n != nameof(ModelCapabilities.None))];

    private static string CapabilityNames => string.Join(", ", Capabilities);

    /// <summary>The capability names in indented rows of four, for the usage text.</summary>
    private static string CapabilityList => string.Join("," + Environment.NewLine,
        Capabilities.Chunk(4).Select(row => "                                          " + string.Join(", ", row)));

    public static string Usage =>
        $"""
        A Lane node that answers every request with one model from any OpenAI-compatible API.

        Usage: dotnet run -- [options]

        Every option below sets a field of the node's form. What you don't set keeps its saved value, and nothing is
        written to disk until the node is started, from the page or elsewhere.

        Lane
          --lane-url <url>              Address of the Lane server (ws://, wss://, http:// or https://)
          --pool <name>                 Pool of nodes to join
          --name <name>                 How this node shows up on the server (also --node-name)
          --concurrency <n>             How many requests this node handles at once

        Provider
          --provider <id>               Template to start from: {ProviderIds}
                                        Picking one refills the endpoint, paths, headers and capabilities below
          --model <id>                  Model to answer with ('{ProviderTemplates.RandomModel}' with {ProviderTemplates.Gemini.Label})
          --api-key <key>               API key for the endpoint; kept in memory only, never written to disk
          --endpoint <url>              Base URL of the API, e.g. {ProviderTemplates.Custom.Endpoint}
          --chat-completions-path <p>   Path under the endpoint, usually chat/completions
          --models-path <p>             Path under the endpoint, usually models
          --header "<Name: value>"      Extra request header; repeat for more, replaces the template's headers
          --no-headers                  Send no extra headers

        Setting an endpoint, a path or a header without --provider switches the template to {ProviderTemplates.CustomId},
        as changing those fields on the page does.

        Capabilities
          --capabilities <a,b,...>      What the model supports; repeat or comma-separate, replaces the
                                        template's. Choose from:
        {CapabilityList}
          --no-capabilities             Report no capabilities

        Identity
          --identity <source>           security-key or key-file
          --key-file <path>             Key pair file to use, which implies --identity key-file
          --security-key-credential-id <base64url>   Registered security key, normally set by registering
          --security-key-public-key <base64>         one on the page instead

        Other
          --urls <url>                  Where to serve the page (default http://localhost:5075)
          --no-browser                  Don't open the page on startup
          --no-update-check             Don't check whether this node or the Lane packages are behind
          --help                        Show this text

        API keys can also come from the environment: {string.Join(", ", ProviderTemplates.All.Select(t => t.KeyVariable).OfType<string>())}.
        """;
}

/// <summary>Fields given on the command line, laid over the settings saved last.</summary>
public sealed record NodeOverrides
{
    public string?              LaneUrl                 { get; init; }
    public string?              Provider                { get; init; }
    public string?              Endpoint                { get; init; }
    public string?              ChatCompletionsPath     { get; init; }
    public string?              ModelsPath              { get; init; }
    public List<HeaderSetting>? Headers                 { get; init; }
    public string?              Model                   { get; init; }
    public string?              Pool                    { get; init; }
    public string?              Name                    { get; init; }
    public int?                 Concurrency             { get; init; }
    public List<string>?        Capabilities            { get; init; }
    public IdentitySource?      Identity                { get; init; }
    public string?              KeyFilePath             { get; init; }
    public string?              SecurityKeyCredentialId { get; init; }
    public string?              SecurityKeyPublicKey    { get; init; }

    public bool Any =>
        this != new NodeOverrides();

    /// <summary>The settings with every field given on the command line laid over them.</summary>
    public NodeSettings Apply(NodeSettings settings)
    {
        if (Provider is not null)
        {
            ProviderTemplate template = ProviderTemplates.Find(Provider);

            settings = settings with
            {
                Provider            = template.Id,
                Endpoint            = template.Endpoint,
                ChatCompletionsPath = template.ChatCompletionsPath,
                ModelsPath          = template.ModelsPath,
                Headers             = [.. template.Headers],
                Capabilities        = [.. template.Capabilities]
            };
        }
        else if (Endpoint is not null || ChatCompletionsPath is not null || ModelsPath is not null || Headers is not null)
        {
            settings = settings with { Provider = ProviderTemplates.CustomId };
        }

        settings = settings with
        {
            LaneUrl             = LaneUrl             ?? settings.LaneUrl,
            Endpoint            = Endpoint            ?? settings.Endpoint,
            ChatCompletionsPath = ChatCompletionsPath ?? settings.ChatCompletionsPath,
            ModelsPath          = ModelsPath          ?? settings.ModelsPath,
            Headers             = Headers             ?? settings.Headers,
            Model               = Model               ?? settings.Model,
            Pool                = Pool                ?? settings.Pool,
            Name                = Name                ?? settings.Name,
            Concurrency         = Concurrency         ?? settings.Concurrency,
            Capabilities        = Capabilities        ?? settings.Capabilities,
            KeyFilePath         = KeyFilePath         ?? settings.KeyFilePath,

            // A key file given on its own is the one to use, as generating one on the page is.
            Identity = Identity ?? (KeyFilePath is not null ? IdentitySource.KeyFile : settings.Identity),

            SecurityKeyCredentialId = SecurityKeyCredentialId ?? settings.SecurityKeyCredentialId,
            SecurityKeyPublicKey    = SecurityKeyPublicKey    ?? settings.SecurityKeyPublicKey
        };

        return settings;
    }
}
