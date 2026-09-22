# Lane Node (OpenAI-compatible)

## Requirements

- [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0)
- An API key for your provider (not needed for local servers such as Ollama)
- The address of a Lane server to connect to

## Download

```sh
git clone https://github.com/fr-cs-peoples/lane-node-openai.git lane-node-openai
cd lane-node-openai
```

### Install

Run the install script from the repository folder:

```sh
./install.sh      # macOS / Linux
install.bat       # Windows
```

It installs the .NET 10 SDK into your user folder if you don't have it, then builds this project. The Lane packages
(`Lane.Core`, `Lane.Nodes.Protocol`, `Lane.Node.Sdk`, `Lane.Providers`) are restored from
[nuget.org](https://www.nuget.org/profiles/ImNotJahan) during the build, always at their newest stable version. If you already have the .NET 10 SDK, `dotnet run`
does all of this on its own.

## Run

```sh
dotnet run
```

The app starts on <http://localhost:5075> and opens it in your browser.

Every field of the node's form can also be set on the command line, so you can preload it instead of filling it in each
time:

```sh
dotnet run -- --provider ollama --model llama3.1:8b --pool default
dotnet run -- --help
```

See [Command line options](COMMAND-LINE.md) for the full list.

### Update check

On startup the node quietly checks for updates and prints a note in the console if something has moved on:

| What's behind | What to do |
| --- | --- |
| This repository | `git pull` |
| The Lane packages, compared with nuget.org | `dotnet restore --force-evaluate` |

The repository check needs `git`, and both need network access; without them the check stays silent. Turn it off
with `dotnet run -- --no-update-check`, or by setting `LANE_NO_UPDATE_CHECK`.

## Use

Open the page (<http://localhost:5075> by default) and work through the form from top to bottom. Anything you pass on
the [command line](COMMAND-LINE.md) is already filled in when it opens.

### 1. Lane

| Field | Meaning |
| --- | --- |
| **Lane URL** | Address of the Lane server
| **Pool** | Which pool of nodes to join (default `default`) |
| **Node name** | How this node shows up on the server (defaults to your computer's name) |
| **Max concurrent requests** | How many requests this node handles at once |

### 2. Provider

1. Pick a **Template**: OpenRouter, Google Gemini, Ollama (local), or Custom.
2. Enter your **API key**. Ollama needs none, so leave it blank.
3. Click **List** to fetch the models your provider offers, then choose one in the **Model** field. You can also type a
   model name yourself.

**Ollama (local)** points at `http://localhost:11434/v1` and defaults to the `llama3.1:8b` model. Run `ollama serve`
and `ollama pull llama3.1:8b` first, then click **List** to see the models you have pulled.

With **Google Gemini** you can set the model to `random`. Each request then goes to a random text model, and if a model
answers 404, 429 or 503 the node tries up to three others.

For **Custom**, open **Endpoints and headers** and set:

- **Endpoint**: the API's base URL, e.g. `https://api.openai.com/v1`
- **Chat completions path** and **Models path**: usually `chat/completions` and `models`
- **Extra headers**: one `Name: value` per line, if your provider needs any

Changing any endpoint field switches the template to Custom.

#### API keys

Your API key is never saved to disk; rather, the node remembers it in memory until the app closes, and only sends it to the
endpoint you typed it for. When you come back later you'll need to enter it again, unless you set an environment variable
before starting the app:

```sh
export OPENROUTER_API_KEY=sk-or-...
export GEMINI_API_KEY=AIza...
dotnet run
```

These are only used with the matching template's endpoint.

### 3. Capabilities

Tick what the model supports (tools, streaming, images, structured output, and so on). Each template ticks sensible
defaults.

### 4. Identity

Choose a **Key source**.

**Security key**

1. Click **Register security key** and tap your key when the browser asks.
2. Each time you start the node, a prompt appears asking you to tap the key again.

**Key pair file**

1. Choose where to save the file (default: `node-key.pem` in the settings folder, see below).
2. Click **Generate key pair**.

You can also point the path at a key file you already have.

### Lane portal

The app also passes through requests to the Lane server at <http://localhost:5075/lane/>, so you can open the server's
portal through the node. Signing in there works with the node's security key or key pair file.

## Where settings are stored

Your last used settings (everything except the API key) are saved to `node-settings.json` when you start the node, in:

| OS | Folder |
| --- | --- |
| macOS | `~/Library/Application Support/Lane/` |
| Linux | `~/.config/Lane/` |
| Windows | `%APPDATA%\Lane\` |

Delete that file to go back to the defaults.