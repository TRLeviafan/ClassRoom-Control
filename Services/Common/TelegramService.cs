using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

namespace ClassRoom_Control.Services.Common;

public class TelegramChatCandidate
{
    public string Id { get; set; } = string.Empty;
    public string Title { get; set; } = string.Empty;
    public string Type { get; set; } = string.Empty; // "channel", "group", "supergroup"

    public override string ToString() => string.IsNullOrWhiteSpace(Title) ? Id : Title;
}

public static class TelegramService
{
    private static readonly HttpClient _httpClient = new() { Timeout = TimeSpan.FromSeconds(60) };

    // Default placeholder developer chat ID and bot token if not configured by user yet
    public const string DefaultBotToken = ""; 
    public const string DefaultDevChatId = "";

    public static string GetEffectiveBotToken(string? customToken = null)
    {
        if (!string.IsNullOrWhiteSpace(customToken))
            return customToken.Trim();

        try
        {
            if (!string.IsNullOrWhiteSpace(Teacher.AppSettings.Current.TelegramBotToken))
                return Teacher.AppSettings.Current.TelegramBotToken.Trim();
        }
        catch { }

        if (!string.IsNullOrWhiteSpace(AppSecrets.TelegramBotToken))
            return AppSecrets.TelegramBotToken.Trim();

        return DefaultBotToken;
    }

    public static string GetEffectiveDevChatId(string? customDevId = null)
    {
        if (!string.IsNullOrWhiteSpace(customDevId))
            return customDevId.Trim();

        try
        {
            if (!string.IsNullOrWhiteSpace(Teacher.AppSettings.Current.DeveloperChatId))
                return Teacher.AppSettings.Current.DeveloperChatId.Trim();
        }
        catch { }

        if (!string.IsNullOrWhiteSpace(AppSecrets.DeveloperChatId))
            return AppSecrets.DeveloperChatId.Trim();

        return DefaultDevChatId;
    }

    public static async Task<(bool ok, string username, string firstName)> GetBotInfoAsync(string? token = null)
    {
        string botToken = GetEffectiveBotToken(token);
        if (string.IsNullOrWhiteSpace(botToken))
            return (false, "@ClassRoomControlBot", "ClassRoom Bot");

        try
        {
            string url = $"https://api.telegram.org/bot{botToken}/getMe";
            var response = await _httpClient.GetAsync(url);
            var json = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);
            bool ok = doc.RootElement.TryGetProperty("ok", out var okProp) && okProp.GetBoolean();
            if (ok && doc.RootElement.TryGetProperty("result", out var res))
            {
                string username = res.TryGetProperty("username", out var u) ? "@" + u.GetString() : "@ClassRoomBot";
                string firstName = res.TryGetProperty("first_name", out var fn) ? fn.GetString() ?? "ClassRoom Bot" : "ClassRoom Bot";
                return (true, username, firstName);
            }
        }
        catch { }

        return (false, "@ClassRoomControlBot", "ClassRoom Bot");
    }

    public static async Task<(bool success, string chatTitle, string error)> CheckBotAccessAsync(string botToken, string chatTarget)
    {
        if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(chatTarget))
            return (false, string.Empty, "Токен бота или имя/ID чата не указаны.");

        try
        {
            string url = $"https://api.telegram.org/bot{botToken}/getChat?chat_id={Uri.EscapeDataString(chatTarget)}";
            var response = await _httpClient.GetAsync(url);
            var json = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);
            bool ok = doc.RootElement.TryGetProperty("ok", out var okProp) && okProp.GetBoolean();
            if (ok && doc.RootElement.TryGetProperty("result", out var res))
            {
                string title = string.Empty;
                if (res.TryGetProperty("title", out var titleProp))
                    title = titleProp.GetString() ?? string.Empty;
                else if (res.TryGetProperty("username", out var userProp))
                    title = "@" + userProp.GetString();

                return (true, title, string.Empty);
            }

            string desc = doc.RootElement.TryGetProperty("description", out var descProp) ? descProp.GetString() ?? "Ошибка доступа" : "Не удалось связаться с чатом";
            return (false, string.Empty, desc);
        }
        catch (Exception ex)
        {
            return (false, string.Empty, ex.Message);
        }
    }

    public static async Task<List<TelegramChatCandidate>> DiscoverRecentChatsAsync(string botToken)
    {
        var result = new List<TelegramChatCandidate>();
        if (string.IsNullOrWhiteSpace(botToken)) return result;

        try
        {
            string url = $"https://api.telegram.org/bot{botToken}/getUpdates";
            var response = await _httpClient.GetAsync(url);
            var json = await response.Content.ReadAsStringAsync();

            using var doc = JsonDocument.Parse(json);
            if (doc.RootElement.TryGetProperty("ok", out var ok) && ok.GetBoolean() &&
                doc.RootElement.TryGetProperty("result", out var updates) &&
                updates.ValueKind == JsonValueKind.Array)
            {
                var seen = new HashSet<string>();

                foreach (var update in updates.EnumerateArray())
                {
                    JsonElement chat = default;
                    if (update.TryGetProperty("message", out var msg) && msg.TryGetProperty("chat", out chat)) { }
                    else if (update.TryGetProperty("channel_post", out var cp) && cp.TryGetProperty("chat", out chat)) { }
                    else if (update.TryGetProperty("my_chat_member", out var mcm) && mcm.TryGetProperty("chat", out chat)) { }

                    if (chat.ValueKind == JsonValueKind.Object)
                    {
                        string id = chat.TryGetProperty("id", out var idProp) ? idProp.GetInt64().ToString() : string.Empty;
                        string title = chat.TryGetProperty("title", out var tProp) ? tProp.GetString() ?? string.Empty : string.Empty;
                        string type = chat.TryGetProperty("type", out var typeProp) ? typeProp.GetString() ?? string.Empty : string.Empty;

                        if (!string.IsNullOrEmpty(id) && !seen.Contains(id) && type != "private")
                        {
                            seen.Add(id);
                            result.Add(new TelegramChatCandidate
                            {
                                Id = id,
                                Title = string.IsNullOrWhiteSpace(title) ? $"Чат #{id}" : title,
                                Type = type
                            });
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"Failed to discover chats: {ex.Message}");
        }

        return result;
    }

    public static async Task<(bool success, string error)> SendLessonNotificationAsync(
        string botToken,
        string chatTarget,
        string lessonTitle,
        TimeSpan duration,
        string? videoFilePath,
        string? cloudLink)
    {
        if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(chatTarget))
            return (false, "Токен бота или целевой чат не указаны.");

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"🎬 <b>Запись урока: {lessonTitle}</b>");
            sb.AppendLine($"📅 Дата: {DateTime.Now:dd.MM.yyyy HH:mm}");
            sb.AppendLine($"⏱ Длительность: {duration:mm\\:ss}");

            if (!string.IsNullOrWhiteSpace(cloudLink))
            {
                sb.AppendLine($"\n🔗 <b>Ссылка на просмотр в облаке:</b>\n{cloudLink}");
            }

            string caption = sb.ToString();

            // If file is available and under 50 MB, upload it directly via sendVideo
            const long maxTgFileSize = 50 * 1024 * 1024; // 50 MB
            if (!string.IsNullOrEmpty(videoFilePath) && File.Exists(videoFilePath) && new FileInfo(videoFilePath).Length < maxTgFileSize)
            {
                using var form = new MultipartFormDataContent();
                form.Add(new StringContent(chatTarget), "chat_id");
                form.Add(new StringContent(caption), "caption");
                form.Add(new StringContent("HTML"), "parse_mode");

                using var fileStream = File.OpenRead(videoFilePath);
                using var streamContent = new StreamContent(fileStream);
                streamContent.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("video/mp4");
                form.Add(streamContent, "video", Path.GetFileName(videoFilePath));

                string sendVideoUrl = $"https://api.telegram.org/bot{botToken}/sendVideo";
                var resp = await _httpClient.PostAsync(sendVideoUrl, form);
                var respJson = await resp.Content.ReadAsStringAsync();

                using var doc = JsonDocument.Parse(respJson);
                if (doc.RootElement.TryGetProperty("ok", out var ok) && ok.GetBoolean())
                    return (true, string.Empty);
            }

            // Fallback to text message
            var textPayload = new
            {
                chat_id = chatTarget,
                text = caption,
                parse_mode = "HTML"
            };

            var jsonContent = new StringContent(JsonSerializer.Serialize(textPayload), Encoding.UTF8, "application/json");
            string sendMsgUrl = $"https://api.telegram.org/bot{botToken}/sendMessage";
            var textResp = await _httpClient.PostAsync(sendMsgUrl, jsonContent);
            var textJson = await textResp.Content.ReadAsStringAsync();

            using var textDoc = JsonDocument.Parse(textJson);
            if (textDoc.RootElement.TryGetProperty("ok", out var textOk) && textOk.GetBoolean())
                return (true, string.Empty);

            string err = textDoc.RootElement.TryGetProperty("description", out var desc) ? desc.GetString() ?? "Ошибка отправки" : "Ошибка отправки";
            return (false, err);
        }
        catch (Exception ex)
        {
            return (false, ex.Message);
        }
    }

    public static async Task<bool> SendBugReportAsync(string botToken, string devChatId, string tag, string message, string? errorDetails)
    {
        if (string.IsNullOrWhiteSpace(botToken) || string.IsNullOrWhiteSpace(devChatId))
            return false;

        try
        {
            var sb = new StringBuilder();
            sb.AppendLine($"🚨 <b>БАГ-РЕПОРТ {tag}</b>");
            sb.AppendLine($"💻 ПК: {Environment.MachineName} ({Environment.UserName})");
            sb.AppendLine($"🪟 ОС: {Environment.OSVersion}");
            sb.AppendLine($"⏰ Время: {DateTime.Now:dd.MM.yyyy HH:mm:ss}");
            sb.AppendLine($"\n📝 <b>Сообщение:</b>\n{message}");

            if (!string.IsNullOrWhiteSpace(errorDetails))
            {
                sb.AppendLine($"\n⚠️ <b>Детали ошибки:</b>\n<code>{errorDetails}</code>");
            }

            var payload = new
            {
                chat_id = devChatId,
                text = sb.ToString(),
                parse_mode = "HTML"
            };

            var jsonContent = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");
            string url = $"https://api.telegram.org/bot{botToken}/sendMessage";
            var resp = await _httpClient.PostAsync(url, jsonContent);
            return resp.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }
}
