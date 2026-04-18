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
            Console.InputEncoding = System.Text.Encoding.UTF8;
            Console.OutputEncoding = System.Text.Encoding.UTF8;
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
                            tools = new object[]
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
                                },
                                new
            {
                name = "search_transactions",
                description = "Searches and filters transactions in the database. Used to answer queries about spending history, income, and specific movements. Always provide dates in YYYY-MM-DD format.",
                inputSchema = new
                {
                    type = "object",
                    properties = new
                    {
                        start_date = new { type = "string", description = "Start date (YYYY-MM-DD). E.g., '2026-04-16'." },
                        end_date = new { type = "string", description = "End date (YYYY-MM-DD). For a single specific day, use the same date as date_from." },
                        account_name = new { type = "string", description = "Exact account name (e.g., 'Peněženka', 'DOMA')." },
                        category = new { type = "string", description = "Main category name (e.g., 'Stravování', 'Drogerie')." },
                        subcategory = new { type = "string", description = "Specific subcategory name (e.g., 'Restaurace', 'Potraviny')." },
                        search_text = new { type = "string", description = "Keyword to search within the transaction description (e.g., 'Tesco')." },
                        limit = new { type = "integer", description = "Maximum number of returned records. Default is 50. Do not request more than 100 to avoid context overflow." }
                    }
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

                            // OPRAVA ZDE: Nyní počítáme i všechny transakce daného účtu
                            var accounts = await context.Accounts
                                .Select(a => new
                                {
                                    AccountName = a.Name,
                                    Currency = a.Currency,
                                    // Entity Framework automaticky sečte všechny Amount hodnoty z navázaných transakcí.
                                    // Pokud účet nemá žádné transakce, Sum() bezpečně vrátí 0.
                                    CurrentBalance = a.InitialBalance + a.Transactions.Sum(t => t.Amount)
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
                        else if (toolName == "search_transactions")
                        {
                            using var context = new AppDbContext(optionsBuilder.Options);

                            // Vyzvedneme argumenty, které AI poslalo (pokud nějaké jsou)
                            JsonElement arguments = default;
                            if (paramsElement.TryGetProperty("arguments", out var argsEl))
                            {
                                arguments = argsEl;
                            }

                            // 1. Založíme dotaz a připojíme relace z tvého EF Core modelu
                            var query = context.Transactions
                                .Include(t => t.Account)
                                .Include(t => t.Category)
                                    .ThenInclude(c => c.ParentCategory) // Potřebujeme pro zjištění hlavní kategorie
                                .AsQueryable();

                            // 2. Aplikujeme filtry, pokud je AI dodalo
                            if (arguments.ValueKind == JsonValueKind.Object)
                            {
                                // Zkusíme najít 'start_date' i 'date_from'
                                if ((arguments.TryGetProperty("start_date", out var dateFromEl) || arguments.TryGetProperty("date_from", out dateFromEl))
                                    && DateTime.TryParse(dateFromEl.GetString(), out DateTime dateFrom))
                                {
                                    query = query.Where(t => t.Date >= dateFrom);
                                }

                                // Zkusíme najít 'end_date' i 'date_to'
                                if ((arguments.TryGetProperty("end_date", out var dateToEl) || arguments.TryGetProperty("date_to", out dateToEl))
                                    && DateTime.TryParse(dateToEl.GetString(), out DateTime dateTo))
                                {
                                    query = query.Where(t => t.Date < dateTo.AddDays(1)); // Do konce dne
                                }

                                if (arguments.TryGetProperty("account_name", out var accNameEl))
                                {
                                    string accName = accNameEl.GetString()?.ToLower();
                                    query = query.Where(t => t.Account.Name.ToLower() == accName);
                                }

                                if (arguments.TryGetProperty("category", out var catEl))
                                {
                                    string catName = catEl.GetString()?.ToLower();
                                    // Hledáme buď v samotné kategorii, nebo v její nadřazené kategorii
                                    query = query.Where(t => t.Category.Name.ToLower() == catName ||
                                                            (t.Category.ParentCategory != null && t.Category.ParentCategory.Name.ToLower() == catName));
                                }

                                if (arguments.TryGetProperty("subcategory", out var subCatEl))
                                {
                                    string subCatName = subCatEl.GetString()?.ToLower();
                                    query = query.Where(t => t.Category.Name.ToLower() == subCatName);
                                }

                                if (arguments.TryGetProperty("search_text", out var textEl))
                                {
                                    string searchText = textEl.GetString()?.ToLower();
                                    query = query.Where(t => t.Description != null && t.Description.ToLower().Contains(searchText));
                                }
                            }

                            // 3. Limit (ochrana proti přehlcení kontextu)
                            // 3. Limit (ochrana proti přehlcení kontextu)
                            int limit = 50; // Tvrdý výchozí limit
                            if (arguments.ValueKind == JsonValueKind.Object && arguments.TryGetProperty("limit", out var limitEl) && limitEl.TryGetInt32(out int parsedLimit))
                            {
                                limit = Math.Min(parsedLimit, 100); // Maximálně agentovi dovolíme 100, i kdyby chtěl víc
                            }

                            // --- NOVÁ OCHRANA PROTI SPÁLENÍ TOKENŮ ---
                            // Nejdříve zjistíme, kolik by dotaz vrátil záznamů (rychlý SQL COUNT dotaz, nestahuje data)
                            int totalCount = await query.CountAsync();

                            if (totalCount > limit)
                            {
                                // Pokud je jich moc, odpovíme agentovi chybou a žádná data mu nepošleme
                                string errorMessage = $"[SYSTEM ERROR] Query matched {totalCount} transactions, which exceeds the maximum allowed limit of {limit}. Request blocked to prevent token overflow. Please refine your search by specifying narrower 'start_date' and 'end_date', or use 'category'/'search_text'.";

                                SendResponse(idElement, new
                                {
                                    content = new[]
                                    {
            new { type = "text", text = errorMessage }
        },
                                    isError = true // Signalizuje MCP klientovi, že volání nástroje selhalo a musí to zkusit jinak
                                });
                            }
                            else
                            {
                                // 4. Spuštění dotazu a formátování výsledku (STÁHNE DATA JEN KDYŽ PROJDOU LIMITEM)
                                var results = await query
                                    .OrderByDescending(t => t.Date)
                                    .Select(t => new
                                    {
                                        t.Id,
                                        Date = t.Date.ToString("yyyy-MM-dd"),
                                        Account = t.Account.Name,
                                        Category = t.Category.ParentCategory != null ? t.Category.ParentCategory.Name : t.Category.Name,
                                        Subcategory = t.Category.ParentCategory != null ? t.Category.Name : null,
                                        Amount = t.Amount,
                                        Description = t.Description ?? ""
                                    })
                                    .ToListAsync();

                                // 5. Odeslání odpovědi zpět agentovi
                                string jsonResult = results.Count > 0
                                    ? JsonSerializer.Serialize(results, new JsonSerializerOptions { WriteIndented = true })
                                    : "[]";

                                SendResponse(idElement, new
                                {
                                    content = new[]
                                    {
            new { type = "text", text = jsonResult }
        }
                                });
                            }
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