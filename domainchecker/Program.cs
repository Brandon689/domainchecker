using Microsoft.Extensions.Configuration;
using System.Text.Json;

class DomainChecker
{
    private static readonly HttpClient client = new HttpClient();
    private static readonly string[] defaultTlds = new[] { ".com", ".net", ".org", ".io", ".cloud", ".agency" };
    private static string? apiKey;

    private class CheckerConfig
    {
        public string[] Tlds { get; set; }
        public int MinLength { get; set; }
        public int MaxLength { get; set; }
        public bool UseNames { get; set; }
        public int DelayMs { get; set; }
    }

    static async Task Main()
    {
        // Load API key from user secrets
        if (!LoadApiKey())
        {
            Console.WriteLine("API key not found in user secrets. Please configure it first.");
            return;
        }

        Console.WriteLine("Domain Availability Checker (Free tier limited)");

        var config = GetUserConfig();
        var words = LoadWords(config);

        DisplayWordStatistics(words);

        Console.WriteLine("\nPress Enter to start checking domains...");
        Console.ReadLine();

        await CheckDomains(words, config);
    }

    private static bool LoadApiKey()
    {
        try
        {
            var configuration = new ConfigurationBuilder()
                .AddUserSecrets<DomainChecker>()
                .Build();

            apiKey = configuration["RapidApi:ApiKey"];

            return !string.IsNullOrEmpty(apiKey);
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading API key: {ex.Message}");
            return false;
        }
    }

    private static CheckerConfig GetUserConfig()
    {
        Console.WriteLine("\nConfiguration:");

        // Source selection
        Console.Write("Check names (N) or words (W)? ");
        bool useNames = Console.ReadLine()?.Trim().ToUpper() == "N";

        // Length configuration
        Console.Write("Minimum length (default 3): ");
        int.TryParse(Console.ReadLine(), out int minLength);
        minLength = minLength == 0 ? 3 : minLength;

        Console.Write("Maximum length (default 5): ");
        int.TryParse(Console.ReadLine(), out int maxLength);
        maxLength = maxLength == 0 ? 5 : maxLength;

        // TLD configuration
        Console.Write("Use custom TLDs? (Y/N): ");
        string[] tlds = defaultTlds;
        if (Console.ReadLine()?.Trim().ToUpper() == "Y")
        {
            Console.Write("Enter TLDs separated by comma (e.g., .com,.net): ");
            var customTlds = Console.ReadLine()?.Split(',');
            if (customTlds?.Length > 0)
            {
                tlds = customTlds.Select(t => t.Trim()).Where(t => t.StartsWith(".")).ToArray();
            }
        }

        return new CheckerConfig
        {
            Tlds = tlds,
            MinLength = minLength,
            MaxLength = maxLength,
            UseNames = useNames,
            DelayMs = 50
        };
    }

    private static List<string> LoadWords(CheckerConfig config)
    {
        string filename = config.UseNames ? "../../../us.txt" : "../../../words_alpha.txt";
        try
        {
            return File.ReadAllLines(filename)
                      .Select(w => w.ToLower().Trim())
                      .Where(w => w.Length >= config.MinLength && w.Length <= config.MaxLength)
                      .Where(w => w.All(char.IsLetter))
                      .Distinct()
                      .OrderBy(w => w.Length)
                      .ToList();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error loading {filename}: {ex.Message}");
            return new List<string>();
        }
    }

    private static void DisplayWordStatistics(List<string> words)
    {
        Console.WriteLine($"\nFound {words.Count} words to check");

        var stats = words.GroupBy(w => w.Length)
                        .OrderBy(g => g.Key);

        foreach (var group in stats)
        {
            Console.WriteLine($"{group.Key} letter words: {group.Count()}");
        }
    }

    private static async Task CheckDomains(List<string> words, CheckerConfig config)
    {
        int totalChecked = 0;
        int available = 0;

        foreach (var tld in config.Tlds)
        {
            Console.WriteLine($"\nChecking domains with {tld}:");

            foreach (var word in words)
            {
                string domain = word + tld;
                bool isAvailable = await CheckDomain(domain);
                if (isAvailable) available++;
                totalChecked++;

                // Progress update
                if (totalChecked % 10 == 0)
                {
                    Console.WriteLine($"\nProgress: {totalChecked}/{words.Count * config.Tlds.Length} checked, {available} available");
                }

                Thread.Sleep(config.DelayMs);
            }
        }

        Console.WriteLine($"\nCheck complete! Found {available} available domains out of {totalChecked} checked.");
    }

    private static async Task<bool> CheckDomain(string domain)
    {
        try
        {
            var request = new HttpRequestMessage
            {
                Method = HttpMethod.Get,
                RequestUri = new Uri($"https://domainr.p.rapidapi.com/v2/status?domain={domain}"),
                Headers =
                {
                    { "x-rapidapi-key", apiKey },
                    { "x-rapidapi-host", "domainr.p.rapidapi.com" },
                },
            };

            using var response = await client.SendAsync(request);
            response.EnsureSuccessStatusCode();
            var body = await response.Content.ReadAsStringAsync();

            using var document = JsonDocument.Parse(body);
            var status = document.RootElement.GetProperty("status")[0];
            var summary = status.GetProperty("summary").GetString();

            bool isAvailable = summary == "inactive";

            Console.ForegroundColor = isAvailable ? ConsoleColor.Green : ConsoleColor.Red;
            Console.WriteLine($"{domain} is {(isAvailable ? "AVAILABLE!" : $"taken ({summary})")}");
            Console.ResetColor();

            if (isAvailable)
            {
                File.AppendAllText("available_domains.txt", $"{domain}\n");
            }

            return isAvailable;
        }
        catch (Exception ex)
        {
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine($"Error checking {domain}: {ex.Message}");
            Console.ResetColor();
            return false;
        }
    }
}
