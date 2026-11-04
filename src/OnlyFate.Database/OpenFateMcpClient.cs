using System.Text.Json;
using ModelContextProtocol.Client;

namespace OnlyFate.Database;

public sealed class OpenFateMcpClient : IOpenFateClient
{
    private readonly McpClient client;
    public string ServerVersion => client.ServerInfo?.Version ?? "unknown";
    private OpenFateMcpClient(McpClient client) => this.client = client;

    public static async Task<IOpenFateClient> CreateAsync(CancellationToken cancellationToken)
    {
        string path = Path.Combine(AppContext.BaseDirectory, "OpenFate", "node_modules", "@openfate", "bazi-mcp", "dist", "stdio.js");
        if (!File.Exists(path))
        {
            DirectoryInfo? directory = new(AppContext.BaseDirectory);
            while (directory is not null)
            {
                string candidate = Path.Combine(directory.FullName, "tools", "openfate", "node_modules", "@openfate", "bazi-mcp", "dist", "stdio.js");
                if (File.Exists(candidate)) { path = candidate; break; }
                directory = directory.Parent;
            }
        }
        if (!File.Exists(path)) throw new Step2Exception("MCP_RUNTIME_MISSING", "找不到本機 OpenFate MCP，請先安裝專案的 OpenFate 執行依賴。");
        StdioClientTransport transport = new(new StdioClientTransportOptions
        {
            Command = "node", Arguments = [path], Name = "OpenFate", ShutdownTimeout = TimeSpan.FromSeconds(3)
        });
        McpClient client = await McpClient.CreateAsync(transport, cancellationToken: cancellationToken);
        if (client.ServerInfo?.Version != "0.3.1")
        {
            await client.DisposeAsync();
            throw new Step2Exception("MCP_VERSION_MISMATCH", "OpenFate 版本與本次驗證版本不符，已停止排盤。");
        }
        return new OpenFateMcpClient(client);
    }

    public async Task<JsonElement> CallAsync(string tool, IReadOnlyDictionary<string, object?> arguments, CancellationToken cancellationToken)
    {
        var result = await client.CallToolAsync(tool, arguments, cancellationToken: cancellationToken);
        return JsonSerializer.SerializeToElement(result, Step2Json.Options);
    }

    public ValueTask DisposeAsync() => client.DisposeAsync();
}
