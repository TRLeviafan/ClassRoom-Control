using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace ClassRoom_Control.Services.Common;

public class SecretsData
{
    public string TelegramBotToken { get; set; } = string.Empty;
    public string DeveloperChatId { get; set; } = string.Empty;
}

public static class AppSecrets
{
    private static SecretsData _secrets = new();

    public static string TelegramBotToken => _secrets.TelegramBotToken;
    public static string DeveloperChatId => _secrets.DeveloperChatId;
    public static bool HasBotToken => !string.IsNullOrWhiteSpace(_secrets.TelegramBotToken);

    static AppSecrets()
    {
        Load();
    }

    public static void Load()
    {
        var candidatePaths = new List<string>();

        string[] fileNames = new[] { "secrets.json", "secret.json", "Secrets.json", "Secret.json" };

        // 1. Проверяем AppDomain.CurrentDomain.BaseDirectory и все его родительские папки
        string? dir = AppDomain.CurrentDomain.BaseDirectory;
        for (int i = 0; i < 5 && !string.IsNullOrEmpty(dir); i++)
        {
            foreach (var fn in fileNames)
            {
                candidatePaths.Add(Path.Combine(dir, fn));
            }
            dir = Path.GetDirectoryName(dir);
        }

        // 2. Проверяем CurrentDirectory и родителей
        dir = Directory.GetCurrentDirectory();
        for (int i = 0; i < 5 && !string.IsNullOrEmpty(dir); i++)
        {
            foreach (var fn in fileNames)
            {
                candidatePaths.Add(Path.Combine(dir, fn));
            }
            dir = Path.GetDirectoryName(dir);
        }

        // 3. Проверяем %AppData%/ClassRoom Control/
        string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "ClassRoom Control");
        foreach (var fn in fileNames)
        {
            candidatePaths.Add(Path.Combine(appData, fn));
        }

        foreach (var path in candidatePaths)
        {
            try
            {
                if (File.Exists(path))
                {
                    string json = File.ReadAllText(path);
                    using var doc = JsonDocument.Parse(json);
                    var root = doc.RootElement;

                    string token = string.Empty;
                    string devId = string.Empty;

                    // Вариант 1: { "Telegram": { "BotToken": "...", "DeveloperChatId": "..." } }
                    if (root.TryGetProperty("Telegram", out var tgObj) || root.TryGetProperty("telegram", out tgObj))
                    {
                        token = ExtractString(tgObj, "BotToken", "botToken", "Token", "token", "bot_token");
                        devId = ExtractString(tgObj, "DeveloperChatId", "developerChatId", "ChatId", "chatId", "chat_id", "dev_chat_id");
                    }

                    // Вариант 2: корневые свойства
                    if (string.IsNullOrEmpty(token))
                    {
                        token = ExtractString(root, "TelegramBotToken", "telegramBotToken", "BotToken", "botToken", "Token", "token", "bot_token");
                    }

                    if (string.IsNullOrEmpty(devId))
                    {
                        devId = ExtractString(root, "DeveloperChatId", "developerChatId", "ChatId", "chatId", "chat_id", "dev_chat_id");
                    }

                    if (!string.IsNullOrWhiteSpace(token) || !string.IsNullOrWhiteSpace(devId))
                    {
                        _secrets = new SecretsData
                        {
                            TelegramBotToken = token.Trim(),
                            DeveloperChatId = devId.Trim()
                        };
                        return;
                    }
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"Error reading secrets from {path}: {ex.Message}");
            }
        }
    }

    private static string ExtractString(JsonElement element, params string[] propertyNames)
    {
        foreach (var name in propertyNames)
        {
            if (element.TryGetProperty(name, out var prop) && prop.ValueKind == JsonValueKind.String)
            {
                string? val = prop.GetString();
                if (!string.IsNullOrWhiteSpace(val)) return val;
            }
        }
        return string.Empty;
    }
}
