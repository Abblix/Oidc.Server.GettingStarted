# MCP Server Sample: a company task tracker used by an AI assistant

A company exposes its task tracker as an MCP server. An AI assistant uses it on an employee's behalf. The employee signs in at the company's provider and decides what the assistant may do.

What the sample shows:

- An MCP server protected by the MCP authorization flow, with Abblix OIDC Server as its authorization server.
- A client known only by its Client ID Metadata Document: no registration and no shared secret, just a `client_id` prefix the company trusts.
- The employee's consent per scope: the assistant starts with reading only.
- Step-up: when the assistant tries to change a task, the tracker answers `403`, the assistant asks for `tasks.write`, and the employee allows it.
- Real agents on the same tracker: Codex and Claude Code.

For the details, see the article [Securing an MCP server](https://www.abblix.com/en/docs/securing-an-mcp-server).

Three apps, one machine:

- `ProviderApp`, the provider: `https://localhost:5007`
- `McpServerApp`, the tracker: `https://localhost:5008/mcp`
- `McpClientApp`, the assistant: its metadata document at `https://localhost:5009/client-metadata.json`, its page at `http://127.0.0.1:5010/`

## What you need

- The .NET 11 SDK, with its development certificate trusted on this computer:

  ```shell
  dotnet dev-certs https --trust
  ```

  On Linux, also add `$HOME/.aspnet/dev-certs/trust` to `SSL_CERT_DIR`, as that command explains.

- Docker with Compose, to run the apps in containers. Without it, see [Run it with the .NET SDK](#run-it-with-the-net-sdk).
- For the agents, Node.js 22.15 or later with npm.
- For [Connect Codex](#connect-codex): Codex CLI 0.172 or later, signed in to your OpenAI account:

  ```shell
  npm install -g @openai/codex
  codex login
  ```

- For [Connect Claude Code](#connect-claude-code): Claude Code 2.1 or later, signed in to your Anthropic account:

  ```shell
  npm install -g @anthropic-ai/claude-code
  claude
  ```

## Run it in Docker

You need ports 5007, 5008 and 5010 free. From `McpServerSample`, export the development certificate once, so the containers serve it:

```shell
dotnet dev-certs https --export-path certificate/localhost.crt --format PEM --no-password
docker compose up --build
```

On Linux and macOS, make the exported files readable by the containers before `docker compose up`: `chmod a+r certificate/*`.

When the log shows `Open http://127.0.0.1:5010/ in a browser`:

- Open `http://127.0.0.1:5010/` and choose Sign in.
- Sign in as `john.doe@example.com` with the password `Jd!2024$3cur3`.
- The consent page shows who asks (Example Assistant, published at localhost:5009), where the code goes and what it gets access to: Task tracker (McpServerApp). Choose Allow.
- Back on the assistant's page, Access shows what the tracker sees of the token: subject `1234567890`, the `client_id` and scope `offline_access tasks.read`. Choose List my tasks.
- Choose Create task. The tracker answers `403` because the token lacks `tasks.write`, and the page offers to ask the provider for it. Do so: you are still signed in, so the consent page appears at once, now with `tasks.write`. Choose Allow.
- The page shows the new task and `tasks.write` in the scope. Enter the task's id and choose Close task, then List my tasks.

The log at the bottom of the page shows every step: the `401` and the metadata the assistant reads, the token issued and its scopes, each call and its answer. The token itself never reaches the browser.

Ctrl+C stops the apps. `docker compose down -v` removes the containers and the provider's key volume.

The same scenario without a browser, exiting 0 when it passes:

```shell
docker compose --profile headless up --build --exit-code-from mcp-client-app-headless
```

## Connect Codex

With the apps running, add the tracker to `~/.codex/config.toml`:

```toml
[mcp_servers.tracker]
url = "https://localhost:5008/mcp"
default_tools_approval_mode = "approve"
```

Then:

- Sign Codex in to the tracker. The browser opens the provider: sign in as `john.doe@example.com`, and the consent page asks to allow Codex, published at chatgpt.com. Choose Allow.

  ```shell
  codex mcp login tracker
  ```

- Ask Codex for your tasks:

  ```shell
  codex exec --skip-git-repo-check "Using the tracker, call who_am_i and list_my_tasks"
  ```

- Ask it to create a task. Codex reports `Insufficient scope`: the token lacks `tasks.write`. Sign in again with both scopes, and allow `tasks.write` on the consent page:

  ```shell
  codex mcp login --scopes tasks.read,tasks.write tracker
  ```

- Ask again: Codex creates the task, and can close it.

`codex mcp logout tracker` forgets the tracker's tokens; delete the `[mcp_servers.tracker]` block to remove the tracker.

## Connect Claude Code

With the apps running, add the tracker:

```shell
claude mcp add --transport http tracker https://localhost:5008/mcp
```

Then:

- Sign Claude Code in to the tracker. The browser opens the provider: sign in as `john.doe@example.com`, and the consent page asks to allow Claude Code, published at claude.ai. Choose Allow.

  ```shell
  claude mcp login tracker
  ```

- Start `claude` and ask for your tasks in the tracker.
- Ask it to create a task. The token lacks `tasks.write`, so Claude Code opens the browser on the consent page, now with `tasks.write`. Choose Allow, and Claude Code creates the task; it can close it too.

`claude mcp remove tracker` removes the tracker.

## Run it with the .NET SDK

You need ports 5007-5010 free. From the repository root, each in its own terminal, in this order:

```shell
dotnet run --project McpServerSample/ProviderApp
dotnet run --project McpServerSample/McpServerApp
dotnet run --project McpServerSample/McpClientApp
```

Then follow the steps above from `http://127.0.0.1:5010/`, and connect Codex or Claude Code the same way.
