using System.Globalization;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;

namespace DynamicIsland.Services;

public sealed record AiQuotaInfo(
    string ProviderName,
    double? RemainingPercent,
    long? RemainingTokens,
    long? LimitTokens,
    string? ResetText,
    string StatusText,
    bool HasData,
    bool IsError = false);

/// <summary>
/// Consulta la cuota o tokens restantes del servicio de IA elegido
/// (Claude, Codex, Antigravity o endpoint/presupuesto personalizado).
/// </summary>
public sealed class AiUsageService
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(10) };

    public AiQuotaInfo Current { get; private set; } = new("IA", null, null, null, null, "Sin consultar", false);
    public event Action? Changed;

    public async Task RefreshAsync(Settings settings)
    {
        if (!settings.ShowAiUsage)
        {
            Current = new(LabelFor(settings.AiProvider), null, null, null, null, "Desactivado", false);
            Changed?.Invoke();
            return;
        }

        AiQuotaInfo info = settings.AiProvider switch
        {
            AiProvider.Claude => await FetchClaudeAsync(settings),
            AiProvider.Codex => await FetchCodexAsync(settings),
            AiProvider.Antigravity => FetchAntigravity(settings),
            AiProvider.Custom => await FetchCustomAsync(settings),
            _ => new("IA", null, null, null, null, "No soportado", false, true)
        };

        Current = info;
        Changed?.Invoke();
    }

    public static string LabelFor(AiProvider p) => p switch
    {
        AiProvider.Claude => "Claude",
        AiProvider.Codex => "Codex",
        AiProvider.Antigravity => "Antigravity",
        AiProvider.Custom => "Personalizado",
        _ => "IA"
    };

    // ---------------- Claude (OAuth local de Claude Code o API Key) ----------------
    private static async Task<AiQuotaInfo> FetchClaudeAsync(Settings settings)
    {
        string? oauthToken = ReadClaudeOAuthToken();
        if (!string.IsNullOrEmpty(oauthToken))
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/api/oauth/usage");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", oauthToken);
                req.Headers.TryAddWithoutValidation("anthropic-beta", "oauth-2025-04-20");
                req.Headers.UserAgent.ParseAdd("DynamicIsland/1.0");

                using var res = await Http.SendAsync(req);
                if (res.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                    var root = doc.RootElement;
                    if (root.TryGetProperty("five_hour", out var fh) &&
                        fh.TryGetProperty("utilization", out var utilEl) &&
                        utilEl.TryGetDouble(out double usedPct))
                    {
                        double remPct = Math.Clamp(100.0 - usedPct, 0, 100);
                        string? reset = fh.TryGetProperty("resets_at", out var rs) ? FormatResetIso(rs.GetString()) : null;
                        return new("Claude", remPct, null, null, reset, "Claude Code (5h)", true);
                    }
                }
            }
            catch { /* continúa al fallback */ }
        }

        string apiKey = settings.GetAiApiKey();
        if (string.IsNullOrEmpty(apiKey))
            apiKey = Environment.GetEnvironmentVariable("ANTHROPIC_API_KEY") ?? "";

        if (!string.IsNullOrEmpty(apiKey))
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.anthropic.com/v1/models");
                req.Headers.TryAddWithoutValidation("x-api-key", apiKey);
                req.Headers.TryAddWithoutValidation("anthropic-version", "2023-06-01");
                using var res = await Http.SendAsync(req);
                if (TryReadRateHeaders(res.Headers,
                    "anthropic-ratelimit-tokens-remaining",
                    "anthropic-ratelimit-tokens-limit",
                    out long rem, out long lim))
                {
                    double pct = lim > 0 ? Math.Clamp(rem * 100.0 / lim, 0, 100) : 0;
                    return new("Claude", pct, rem, lim, null, "API Anthropic", true);
                }
                if (res.IsSuccessStatusCode)
                    return ManualOrStatus("Claude", settings, "API Key válida (sin cabecera de cuota)");
            }
            catch (Exception ex)
            {
                return new("Claude", null, null, null, null, "Error de red: " + ex.Message, false, true);
            }
        }

        return ManualOrStatus("Claude", settings, "Sin ~/.claude/.credentials.json ni API Key");
    }

    private static string? ReadClaudeOAuthToken()
    {
        try
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".claude", ".credentials.json");
            if (!File.Exists(path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("claudeAiOauth", out var oauth) &&
                oauth.TryGetProperty("accessToken", out var tok))
                return tok.GetString();
        }
        catch { }
        return null;
    }

    // ---------------- Codex (auth.json local de Codex CLI o API Key OpenAI) ----------------
    private static async Task<AiQuotaInfo> FetchCodexAsync(Settings settings)
    {
        string? codexToken = ReadCodexToken();
        if (!string.IsNullOrEmpty(codexToken))
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, "https://chatgpt.com/backend-api/wham/usage");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", codexToken);
                req.Headers.UserAgent.ParseAdd("DynamicIsland/1.0");

                using var res = await Http.SendAsync(req);
                if (res.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                    if (doc.RootElement.TryGetProperty("rate_limit", out var rl) &&
                        rl.TryGetProperty("primary_window", out var pw) &&
                        pw.TryGetProperty("used_percent", out var usedEl) &&
                        usedEl.TryGetDouble(out double usedPct))
                    {
                        double remPct = Math.Clamp(100.0 - usedPct, 0, 100);
                        string? reset = pw.TryGetProperty("reset_after_seconds", out var sec) && sec.TryGetInt64(out long s)
                            ? FormatResetSeconds(s) : null;
                        return new("Codex", remPct, null, null, reset, "Codex CLI", true);
                    }
                }
            }
            catch { }
        }

        // Fallback: último rate_limit guardado en sesiones locales de Codex
        if (TryReadCodexSessionLimit(out double sessionRemPct, out string? sessionReset))
            return new("Codex", sessionRemPct, null, null, sessionReset, "Sesión local Codex", true);

        string apiKey = settings.GetAiApiKey();
        if (string.IsNullOrEmpty(apiKey))
            apiKey = Environment.GetEnvironmentVariable("OPENAI_API_KEY") ?? "";

        if (!string.IsNullOrEmpty(apiKey))
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, "https://api.openai.com/v1/models");
                req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
                using var res = await Http.SendAsync(req);
                if (TryReadRateHeaders(res.Headers,
                    "x-ratelimit-remaining-tokens",
                    "x-ratelimit-limit-tokens",
                    out long rem, out long lim))
                {
                    double pct = lim > 0 ? Math.Clamp(rem * 100.0 / lim, 0, 100) : 0;
                    return new("Codex", pct, rem, lim, null, "API OpenAI", true);
                }
                if (res.IsSuccessStatusCode)
                    return ManualOrStatus("Codex", settings, "API Key válida (sin cabecera de cuota)");
            }
            catch (Exception ex)
            {
                return new("Codex", null, null, null, null, "Error de red: " + ex.Message, false, true);
            }
        }

        return ManualOrStatus("Codex", settings, "Sin ~/.codex/auth.json ni API Key");
    }

    private static string? ReadCodexToken()
    {
        try
        {
            string path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "auth.json");
            if (!File.Exists(path)) return null;
            using var doc = JsonDocument.Parse(File.ReadAllText(path));
            if (doc.RootElement.TryGetProperty("tokens", out var t) &&
                t.TryGetProperty("access_token", out var tok))
                return tok.GetString();
        }
        catch { }
        return null;
    }

    private static bool TryReadCodexSessionLimit(out double remPct, out string? reset)
    {
        remPct = 0; reset = null;
        try
        {
            string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), ".codex", "sessions");
            if (!Directory.Exists(dir)) return false;
            var latest = new DirectoryInfo(dir).GetFiles("*.jsonl", SearchOption.AllDirectories)
                .OrderByDescending(f => f.LastWriteTimeUtc).FirstOrDefault();
            if (latest == null) return false;
            foreach (string line in File.ReadLines(latest.FullName).Reverse().Take(40))
            {
                if (!line.Contains("rate_limits", StringComparison.Ordinal)) continue;
                using var doc = JsonDocument.Parse(line);
                if (doc.RootElement.TryGetProperty("payload", out var p) &&
                    p.TryGetProperty("rate_limits", out var rl) &&
                    rl.TryGetProperty("primary", out var pr) &&
                    pr.TryGetProperty("used_percent", out var u) &&
                    u.TryGetDouble(out double used))
                {
                    remPct = Math.Clamp(100.0 - used, 0, 100);
                    return true;
                }
            }
        }
        catch { }
        return false;
    }

    // ---------------- Antigravity (modelo activo local + presupuesto manual opcional) ----------------
    private static AiQuotaInfo FetchAntigravity(Settings settings)
    {
        string model = ReadAntigravityModel() ?? "Antigravity";
        if (settings.AiManualLimitTokens > 0)
        {
            long rem = Math.Max(0, settings.AiManualLimitTokens - settings.AiManualUsedTokens);
            double pct = Math.Clamp(rem * 100.0 / settings.AiManualLimitTokens, 0, 100);
            return new("Antigravity", pct, rem, settings.AiManualLimitTokens, null, model, true);
        }
        // Sin límite manual configurado: inicia llena (100%) mostrando el modelo activo
        return new("Antigravity", 100.0, null, null, null, model, true);
    }

    private static string? ReadAntigravityModel()
    {
        try
        {
            string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            foreach (string sub in new[] { "antigravity-cli", "antigravity" })
            {
                string path = Path.Combine(home, ".gemini", sub, "settings.json");
                if (!File.Exists(path)) continue;
                using var doc = JsonDocument.Parse(File.ReadAllText(path));
                if (doc.RootElement.TryGetProperty("model", out var m))
                    return m.GetString();
            }
        }
        catch { }
        return null;
    }

    // ---------------- Personalizado (Endpoint JSON o presupuesto manual) ----------------
    private static async Task<AiQuotaInfo> FetchCustomAsync(Settings settings)
    {
        if (!string.IsNullOrWhiteSpace(settings.AiCustomEndpoint) &&
            Uri.TryCreate(settings.AiCustomEndpoint.Trim(), UriKind.Absolute, out var uri))
        {
            try
            {
                using var req = new HttpRequestMessage(HttpMethod.Get, uri);
                string key = settings.GetAiApiKey();
                if (!string.IsNullOrEmpty(key))
                    req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", key);

                using var res = await Http.SendAsync(req);
                if (res.IsSuccessStatusCode)
                {
                    using var doc = JsonDocument.Parse(await res.Content.ReadAsStringAsync());
                    var r = doc.RootElement;
                    long? rem = TryLong(r, "remaining_tokens", "tokens_remaining", "remaining");
                    long? lim = TryLong(r, "limit_tokens", "tokens_limit", "limit", "total");
                    double? pct = TryDouble(r, "remaining_percent", "percent_remaining");
                    if (pct == null && rem.HasValue && lim is > 0)
                        pct = Math.Clamp(rem.Value * 100.0 / lim.Value, 0, 100);
                    if (pct.HasValue || rem.HasValue)
                        return new("IA", pct, rem, lim, null, "Endpoint personalizado", true);
                }
                return new("IA", null, null, null, null, $"HTTP {(int)res.StatusCode}", false, true);
            }
            catch (Exception ex)
            {
                return new("IA", null, null, null, null, "Error: " + ex.Message, false, true);
            }
        }

        return ManualOrStatus("IA", settings, "Configura un endpoint JSON o límite manual");
    }

    private static AiQuotaInfo ManualOrStatus(string name, Settings settings, string fallbackStatus)
    {
        if (settings.AiManualLimitTokens > 0)
        {
            long rem = Math.Max(0, settings.AiManualLimitTokens - settings.AiManualUsedTokens);
            double pct = Math.Clamp(rem * 100.0 / settings.AiManualLimitTokens, 0, 100);
            return new(name, pct, rem, settings.AiManualLimitTokens, null, "Límite manual", true);
        }
        return new(name, null, null, null, null, fallbackStatus, false);
    }

    // ---------------- Utilidades ----------------
    private static bool TryReadRateHeaders(HttpResponseHeaders headers, string remKey, string limKey, out long rem, out long lim)
    {
        rem = lim = 0;
        return headers.TryGetValues(remKey, out var rv) && long.TryParse(rv.FirstOrDefault(), out rem) &&
               headers.TryGetValues(limKey, out var lv) && long.TryParse(lv.FirstOrDefault(), out lim) && lim > 0;
    }

    private static long? TryLong(JsonElement el, params string[] names)
    {
        foreach (string n in names)
            if (el.TryGetProperty(n, out var v) && v.TryGetInt64(out long val)) return val;
        return null;
    }

    private static double? TryDouble(JsonElement el, params string[] names)
    {
        foreach (string n in names)
            if (el.TryGetProperty(n, out var v) && v.TryGetDouble(out double val)) return val;
        return null;
    }

    private static string? FormatResetIso(string? iso)
    {
        if (string.IsNullOrEmpty(iso) || !DateTimeOffset.TryParse(iso, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var dt))
            return null;
        return FormatResetSeconds((long)(dt - DateTimeOffset.UtcNow).TotalSeconds);
    }

    private static string? FormatResetSeconds(long seconds)
    {
        if (seconds <= 0) return null;
        var ts = TimeSpan.FromSeconds(seconds);
        return ts.TotalHours >= 1 ? $"Reinicia en {(int)ts.TotalHours}h {ts.Minutes}m" : $"Reinicia en {Math.Max(1, ts.Minutes)}m";
    }

    public static string FormatTokens(long n) => n switch
    {
        >= 1_000_000 => $"{n / 1_000_000.0:0.#}M",
        >= 1_000 => $"{n / 1_000.0:0.#}k",
        _ => n.ToString(CultureInfo.InvariantCulture)
    };
}
