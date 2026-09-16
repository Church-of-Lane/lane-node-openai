# Command line options

Every field the node page's form edits can also be set on the command line. Options go after `--`, which separates them
from `dotnet run`'s own:

```sh
dotnet run -- --provider ollama --model llama3.1:8b --pool default --no-browser
```

Both `--option value` and `--option=value` work. An option never takes another option as its value, so a forgotten value
is reported rather than silently swallowing what follows.

See the list at any time with:

```sh
dotnet run -- --help
```

## How options and saved settings fit together

The node remembers the settings it was last started with (see [Where settings are stored](README.md#where-settings-are-stored)).
On startup those are loaded first, then anything given on the command line is laid over them, and the result is what the
page shows when it opens. Fields you don't set keep their saved values.

Nothing you pass is written to disk by itself. The settings file is only rewritten when the node is started, exactly as
when you start it from the page.

So the command line is a way to preload the form, not a separate configuration file. You still open the page and press
**Start** (which is also where a security key is tapped).

## Lane

| Option | Field | Default |
| --- | --- | --- |
| `--lane-url <url>` | Address of the Lane server; `ws://`, `wss://`, `http://` or `https://` | `ws://127.0.0.1:5070` |
| `--pool <name>` | Which pool of nodes to join | `default` |
| `--name <name>` (or `--node-name`) | How this node shows up on the server | your computer's name |
| `--concurrency <n>` | How many requests this node handles at once; at least 1 | `4` |

```sh
dotnet run -- --lane-url wss://lane.example.com/ws --pool research --name workstation --concurrency 8
```

## Provider

| Option | Field |
| --- | --- |
| `--provider <id>` | Template to start from: `openrouter`, `gemini`, `ollama` or `custom` |
| `--model <id>` | Model to answer with |
| `--api-key <key>` | API key for the endpoint |
| `--endpoint <url>` | Base URL of the API, e.g. `https://api.openai.com/v1` |
| `--chat-completions-path <path>` | Path under the endpoint, usually `chat/completions` |
| `--models-path <path>` | Path under the endpoint, usually `models` |
| `--header "<Name: value>"` | An extra request header; repeat the option for more |
| `--no-headers` | Send no extra headers |

`--provider` refills the endpoint, both paths, the headers and the capabilities from that template, just as picking a
template on the page does. Options that come with it still win, so this keeps Gemini's paths and capabilities while
sending the requests somewhere else:

```sh
dotnet run -- --provider gemini --endpoint https://my-proxy.example/v1
```

Setting an endpoint, a path or a header **without** `--provider` switches the template to `custom`, again matching the
page:

```sh
dotnet run -- --endpoint http://localhost:1234/v1 --model local-model --no-headers
```

`--header` replaces the template's headers rather than adding to them, so give every header you want in one run:

```sh
dotnet run -- --provider openrouter --header "X-Title: Lane" --header "HTTP-Referer: https://example.com"
```

With Google Gemini, `--model random` sends each request to a random text-output model.

### API keys

`--api-key` is held in memory for the endpoint it was given for, and is never written to disk or sent back to the page —
the same treatment as a key typed into the form. It is remembered until the app closes.

Note that anything on a command line is visible to other processes and usually lands in your shell history. The
environment variables are the quieter option, and are still read when you pass nothing:

```sh
export OPENROUTER_API_KEY=sk-or-...
export GEMINI_API_KEY=AIza...
dotnet run
```

Each is only used with its own template's endpoint.

## Capabilities

| Option | Field |
| --- | --- |
| `--capabilities <a,b,...>` | What the model supports |
| `--no-capabilities` | Report no capabilities |

Names are case-insensitive, and the option can be repeated or comma-separated. Like `--header`, it replaces the
template's list instead of adding to it. `dotnet run -- --help` prints the names this build accepts; at the time of
writing they are `Tools`, `ParallelTools`, `Streaming`, `Images`, `PromptCaching`, `StructuredOutput`, `StopSequences`
and `Thinking`.

```sh
dotnet run -- --capabilities Tools,Streaming --capabilities Images
```

## Identity

| Option | Field |
| --- | --- |
| `--identity <source>` | `security-key` or `key-file` |
| `--key-file <path>` | Key pair file to use |
| `--security-key-credential-id <base64url>` | Credential id of a registered security key |
| `--security-key-public-key <base64>` | Its public key, as SubjectPublicKeyInfo |

`--key-file` on its own implies `--identity key-file`, as generating a key file on the page does, and the path is made
absolute:

```sh
dotnet run -- --key-file ~/keys/node-key.pem
```

The file has to exist before the node starts; the command line will not create one. Generate it on the page, or point
the option at a key file you already have.

The two security key options are there for completeness — they are normally set by clicking **Register security key** on
the page, which is the only way to get the values from your key in the first place. They are useful for moving a
registration to another machine, where the values can be read out of the settings file:

```sh
dotnet run -- --identity security-key \
  --security-key-credential-id lwF6aVjzkLedg... \
  --security-key-public-key MFkwEwYHKoZIzj0CAQ...
```

Whichever source you use, the node's identity is only put to work when it starts: a key file is read then, and a
security key is tapped then.

## Running the app itself

| Option | What it does |
| --- | --- |
| `--urls <url>` | Where to serve the page; default `http://localhost:5075` |
| `--no-browser` | Don't open the page on startup |
| `--no-update-check` | Don't check whether this node or the Lane packages are behind |
| `--help` | Print the options and exit |

`--urls` and the other standard ASP.NET Core host options (`--environment`, `--contentRoot`, `--applicationName`) are
passed through to the web host.

Serving the page anywhere other than `localhost` stops security keys from working in the browser, as WebAuthn requires a
secure origin. Use a key pair file there, or reach the page through an SSH tunnel.

## Validation

Options are checked before the app starts. A bad one prints a message and exits with status 1 without serving anything:

```
$ dotnet run -- --provider bogus
Unknown provider 'bogus'. Choose one of openrouter, gemini, ollama, custom.
```

This covers the shape of what you pass — unknown options, missing values, a `--concurrency` below 1, a `--header`
without a colon, an unknown provider, capability or identity source. Whether the settings actually work — that the Lane
URL and endpoint are reachable, that a required API key is present, that the model exists — is checked when the node
starts, and problems are reported on the page as they are for anything typed into the form.
