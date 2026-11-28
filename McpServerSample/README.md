# MCP Server Sample: a company task tracker used by an AI assistant

A company exposes its task tracker as an MCP server. An AI assistant uses it on an employee's behalf. The employee signs in at the company's provider and decides what the assistant may do.

What the sample shows:

- An MCP server protected by the MCP authorization flow, with Abblix OIDC Server as its authorization server.
- A client known only by its Client ID Metadata Document: no registration and no shared secret, just a `client_id` prefix the company trusts.
- The employee's consent per scope: the assistant starts with reading only.
- Step-up: when the assistant tries to change a task, the tracker answers `403`, the assistant asks for `tasks.write`, and the employee allows it.

For the details, see the article [Securing an MCP server](https://www.abblix.com/en/docs/securing-an-mcp-server).

Three apps, one machine:

- `ProviderApp`, the provider: `https://localhost:5007`
- `McpServerApp`, the tracker: `https://localhost:5008/mcp`
- `McpClientApp`, the assistant: its metadata document at `https://localhost:5009/client-metadata.json`, its page at `http://127.0.0.1:5010/`

## Run it in Docker

You need Docker and ports 5007 and 5010 free. From `McpServerSample`:

```shell
docker compose up --build
```

When the log shows `Open http://127.0.0.1:5010/ in a browser`:

- Open `http://127.0.0.1:5010/` and choose Sign in.
- The browser warns about the certificate: the image build issued it and your computer does not trust it. Continue to localhost (in Chrome: Advanced, then Proceed to localhost).
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

## Run it with the .NET SDK

You need the .NET 11 SDK, a trusted development certificate (`dotnet dev-certs https --trust`) and ports 5007-5010 free. From the repository root, each in its own terminal, in this order:

```shell
dotnet run --project McpServerSample/ProviderApp
dotnet run --project McpServerSample/McpServerApp
dotnet run --project McpServerSample/McpClientApp
```

Then follow the steps above from `http://127.0.0.1:5010/`, without the certificate warning.
