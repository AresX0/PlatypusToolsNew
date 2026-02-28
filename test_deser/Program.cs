using System;
using System.IO;
using System.Text.Json;

// FINAL FIX: CamelCase + CaseInsensitive
var vaultFileOptions = new JsonSerializerOptions
{
    WriteIndented = true,
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
};

var vaultPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "PlatypusTools", "Vault", "vault.encrypted");
var json = File.ReadAllText(vaultPath);

Console.WriteLine("=== Test 1: Reading EXISTING vault file (PascalCase keys) with CamelCase policy ===");
var parsed = JsonSerializer.Deserialize<VaultFile>(json, vaultFileOptions);
Console.WriteLine($"Salt empty: {string.IsNullOrEmpty(parsed.Salt)}");
Console.WriteLine($"Salt length: {parsed.Salt?.Length ?? 0}");
Console.WriteLine($"Iv empty: {string.IsNullOrEmpty(parsed.Iv)}");
Console.WriteLine($"Hash empty: {string.IsNullOrEmpty(parsed.Hash)}");
Console.WriteLine($"Data length: {parsed.Data?.Length ?? 0}");
Console.WriteLine($"KdfIterations: {parsed.KdfIterations}");

Console.WriteLine("\n=== Test 2: Round-trip with CONSISTENT CamelCase options ===");
var testObj = new VaultFile { Salt = "test+salt/value=", Iv = "testIv", Data = "testData", Hash = "testHash", KdfIterations = 600000 };
var serialized = JsonSerializer.Serialize(testObj, vaultFileOptions);
Console.WriteLine($"Serialized: {serialized.Substring(0, Math.Min(200, serialized.Length))}");

var roundTripped = JsonSerializer.Deserialize<VaultFile>(serialized, vaultFileOptions);
Console.WriteLine($"Round-trip Salt: '{roundTripped.Salt}' (original: '{testObj.Salt}')");
Console.WriteLine($"Salt preserved: {roundTripped.Salt == testObj.Salt}");

Console.WriteLine("\n=== Test 3: Can we read PascalCase with PropertyNameCaseInsensitive? ===");
var caseInsensitiveOpts = new JsonSerializerOptions 
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true 
};
var parsed2 = JsonSerializer.Deserialize<VaultFile>(json, caseInsensitiveOpts);
Console.WriteLine($"With CaseInsensitive - Salt empty: {string.IsNullOrEmpty(parsed2.Salt)}");
Console.WriteLine($"With CaseInsensitive - Salt length: {parsed2.Salt?.Length ?? 0}");

public class VaultFile
{
    public int Version { get; set; } = 1;
    public string Salt { get; set; } = string.Empty;
    public string Iv { get; set; } = string.Empty;
    public string Data { get; set; } = string.Empty;
    public string Hash { get; set; } = string.Empty;
    public int KdfIterations { get; set; } = 600000;
}
