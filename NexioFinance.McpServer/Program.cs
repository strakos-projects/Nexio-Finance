using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using NexioFinance.Data;

namespace NexioFinance.McpServer
{
    class Program
    {
        static async Task Main(string[] args)
        {
            string configFileName = "mcp_server_config.json";
            string configFilePath = Path.Combine(AppContext.BaseDirectory, configFileName);
            string placeholderPath = @"C:\Replace_This_With_Your_NexioFinance_Bin_Path";

            if (!File.Exists(configFilePath))
            {
                var defaultConfig = new { WpfAppDatabaseDirectory = placeholderPath };
                string configJson = JsonSerializer.Serialize(defaultConfig, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(configFilePath, configJson);

                Console.Error.WriteLine($"[WARNING] Configuration file '{configFileName}' was not found, so a default one was created.");
                Console.Error.WriteLine($"[ACTION REQUIRED] Please open '{configFilePath}' and set the correct path to your Nexio Finance database directory.");
                return;
            }

            var config = new ConfigurationBuilder()
                .SetBasePath(AppContext.BaseDirectory)
                .AddJsonFile(configFileName, optional: false, reloadOnChange: true)
                .Build();

            string? wpfAppPath = config["WpfAppDatabaseDirectory"];

            if (string.IsNullOrWhiteSpace(wpfAppPath) || wpfAppPath == placeholderPath)
            {
                Console.Error.WriteLine($"[WARNING] The database path in '{configFileName}' has not been configured.");
                Console.Error.WriteLine($"[ACTION REQUIRED] Please update 'WpfAppDatabaseDirectory' to the actual path of your Nexio Finance app.");
                return;
            }

            string dbPath = Path.Combine(wpfAppPath, "nexio_data.db");
            if (!File.Exists(dbPath))
            {
                Console.Error.WriteLine($"[ERROR] Database file not found at: {dbPath}");
                Console.Error.WriteLine("[HINT] Make sure the path in your config is correct and that you have run the main Nexio Finance app at least once to create the database.");
                return;
            }

            var optionsBuilder = new DbContextOptionsBuilder<AppDbContext>();
            optionsBuilder.UseSqlite($"Data Source={dbPath}");

            // ====================================================================
            // INICIALIZACE MCP SERVERU (Čistý JSON-RPC protokol)
            // ====================================================================

            // Informační logy pro vývojáře MUSÍ jít do Error streamu, aby to nerozbilo datovou komunikaci s AI
            Console.Error.WriteLine("[INFO] Nexio Finance MCP Server is listening...");

            // Nasloucháme na standardním vstupu (stdin).
            // Tento cyklus běží neustále. Jakmile AI klient ukončí spojení, ReadLineAsync vrátí null a program skončí.
            string? line;
            while ((line = await Console.In.ReadLineAsync()) != null)
            {
                try
                {
                    // Přečteme zprávu od AI klienta
                    var request = JsonDocument.Parse(line);
                    string method = request.RootElement.TryGetProperty("method", out var methodProp) ? methodProp.GetString() ?? "" : "";

                    // MCP posílá i tzv. notifikace (zprávy bez ID), na které se neodpovídá
                    if (!request.RootElement.TryGetProperty("id", out var idElement)) continue;

                    // 1. AI SE PŘEDSTAVUJE (Handshake)
                    if (method == "initialize")
                    {
                        SendResponse(idElement, new
                        {
                            protocolVersion = "2024-11-05",
                            capabilities = new { tools = new { } },
                            serverInfo = new { name = "nexio-finance-mcp", version = "1.0.0" }
                        });
                    }

                    // 2. AI SE PTÁ NA SEZNAM FUNKCÍ
                    else if (method == "tools/list")
                    {
                        SendResponse(idElement, new
                        {
                            tools = new[]
                            {
                                new
                                {
                                    name = "get_account_balances",
                                    description = "Returns current balances for all financial accounts (including crypto wallets).",
                                    inputSchema = new
                                    {
                                        type = "object",
                                        properties = new { } // Funkce aktuálně nevyžaduje žádné parametry (např. ID účtu)
                                    }
                                }
                            }
                        });
                    }

                    // 3. AI VOLÁ KONKRÉTNÍ FUNKCI
                    else if (method == "tools/call")
                    {
                        var paramsElement = request.RootElement.GetProperty("params");
                        string toolName = paramsElement.GetProperty("name").GetString() ?? "";

                        if (toolName == "get_account_balances")
                        {
                            using var context = new AppDbContext(optionsBuilder.Options);

                            var accounts = await context.Accounts
                                .Select(a => new
                                {
                                    AccountName = a.Name,
                                    Currency = a.Currency,
                                    CurrentBalance = a.InitialBalance
                                })
                                .ToListAsync();

                            string balancesJson = JsonSerializer.Serialize(accounts, new JsonSerializerOptions { WriteIndented = true });

                            // Vrátíme výsledek ve formátu, který vyžaduje MCP standard
                            SendResponse(idElement, new
                            {
                                content = new[]
                                {
                                    new { type = "text", text = balancesJson }
                                }
                            });
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"[ERROR] Message parsing failed: {ex.Message}");
                }
            }
        }

        // Pomocná metoda pro správné zformátování a odeslání JSON-RPC odpovědi
        static void SendResponse(JsonElement id, object result)
        {
            var response = new
            {
                jsonrpc = "2.0",
                id = id,
                result = result
            };

            // Datová odpověď pro AI MUSÍ jít do běžného výstupu (stdout)
            Console.WriteLine(JsonSerializer.Serialize(response));
        }
    }
}   