using System.Text.Json;

var options = new JsonSerializerOptions {
    WriteIndented = false,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
};

var vaultJson = File.ReadAllText(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PlatypusTools", "Vault", "vault.encrypted"));
var result = JsonSerializer.Deserialize<Dictionary<string, JsonElement>>(vaultJson);

Console.WriteLine($"Raw JSON keys: {string.Join(", ", result.Keys)}");

// Now deserialize like the app does
var parsed = JsonSerializer.Deserialize<VaultFile>(vaultJson, options);
Console.WriteLine($"Salt is empty: {string.IsNullOrEmpty(parsed.Salt)}");
Console.WriteLine($"Salt length: {parsed.Salt?.Length ?? 0}");
Console.WriteLine($"Iv is empty: {string.IsNullOrEmpty(parsed.Iv)}");
Console.WriteLine($"Hash is empty: {string.IsNullOrEmpty(parsed.Hash)}");
Console.WriteLine($"KdfIterations: {parsed.KdfIterations}");
Console.WriteLine($"Data length: {parsed.Data?.Length ?? 0}");

public class VaultFile
{
    public int Version { get; set; } = 1;
    public string Salt { get; set; } = string.Empty;
    public string Iv { get; set; } = string.Empty;
    public string Data { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
    public int KdfIterations { get; set; } = 600000;
}
