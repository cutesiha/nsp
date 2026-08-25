using System;
using System.Collections.Generic;
using System.Linq;
using HttpClient = System.Net.Http.HttpClient;
using HttpRequestMessage = System.Net.Http.HttpRequestMessage;
using HttpMethod = System.Net.Http.HttpMethod;
using StringContent = System.Net.Http.StringContent;
using SysEnvironment = System.Environment;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using NSP.Core;
using NSP.Facility;

namespace NSP.Ai;

public partial class AIService : Node
{
    public static AIService Instance { get; private set; }

    private const string ApiUrl = "https://api.anthropic.com/v1/messages";
    private const int MaxHistoryTurns = 6;

    private HttpClient _httpClient;

    private static readonly string[] FallbackLines =
    {
        "...지금은 대답할 정신이 없어요.",
        "그건 나중에 얘기하면 안 될까요.",
        "잘 모르겠어요. 그냥 시키는 대로 하고 있어요.",
        "여기서는 아는 척 안 하는 게 나아요.",
    };

    [Signal] public delegate void DialogueReceivedEventHandler(string employeeId, string text, bool isFallback);

    public override void _EnterTree()
    {
        Instance = this;
        _httpClient = new HttpClient();
    }

    public override void _ExitTree()
    {
        _httpClient?.Dispose();
    }

    public async void RequestDialogue(string employeeId, string playerMessage)
    {
        string apiKey = SysEnvironment.GetEnvironmentVariable("ANTHROPIC_API_KEY");
        string reply;
        bool isFallback;

        if (string.IsNullOrEmpty(apiKey))
        {
            reply = PickFallback(employeeId);
            isFallback = true;
        }
        else
        {
            var (text, ok) = await TryFetchReply(employeeId, playerMessage, apiKey);
            reply = ok ? text : PickFallback(employeeId);
            isFallback = !ok;
        }

        AppendHistory(employeeId, "player", playerMessage);
        AppendHistory(employeeId, "npc", reply);

        EmitSignal(SignalName.DialogueReceived, employeeId, reply, isFallback);
    }

    private async Task<(string text, bool ok)> TryFetchReply(string employeeId, string playerMessage, string apiKey)
    {
        int attempts = 1 + Math.Max(0, Config.Instance.Data.ApiMaxRetries);
        for (int i = 0; i < attempts; i++)
        {
            try
            {
                using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(Config.Instance.Data.ApiTimeoutSeconds));
                string result = await CallApi(employeeId, playerMessage, apiKey, cts.Token);
                if (!string.IsNullOrWhiteSpace(result))
                    return (result, true);
            }
            catch (Exception e)
            {
                GD.PushWarning($"AIService: attempt {i + 1} failed: {e.Message}");
            }
        }
        return ("", false);
    }

    private async Task<string> CallApi(string employeeId, string playerMessage, string apiKey, CancellationToken token)
    {
        string systemPrompt = BuildSystemPrompt(employeeId);

        var messages = new List<object>();
        foreach (var turn in GetHistory(employeeId))
            messages.Add(new { role = turn.Role == "player" ? "user" : "assistant", content = turn.Text });
        messages.Add(new { role = "user", content = playerMessage });

        var payload = new
        {
            model = Config.Instance.Data.AiModelId,
            max_tokens = Config.Instance.Data.AiMaxTokens,
            system = systemPrompt,
            messages,
        };

        using var request = new HttpRequestMessage(HttpMethod.Post, ApiUrl);
        request.Headers.Add("x-api-key", apiKey);
        request.Headers.Add("anthropic-version", "2023-06-01");
        request.Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json");

        using var response = await _httpClient.SendAsync(request, token);
        string body = await response.Content.ReadAsStringAsync(token);
        if (!response.IsSuccessStatusCode)
            throw new Exception($"API error {(int)response.StatusCode}: {body}");

        using var doc = JsonDocument.Parse(body);
        var contentArray = doc.RootElement.GetProperty("content");
        if (contentArray.GetArrayLength() == 0)
            return "";
        return contentArray[0].GetProperty("text").GetString() ?? "";
    }

    private string BuildSystemPrompt(string employeeId)
    {
        var def = FacilitySimulation.Instance.GetEmployeeDef(employeeId);
        var state = FacilitySimulation.Instance.GetEmployeeState(employeeId);
        if (def == null || state == null)
            return "당신은 시설 직원입니다.";

        var knownFacts = EventLog.Instance.GetEntriesKnownBy(employeeId)
            .TakeLast(5)
            .Select(e => "- " + e.Description);

        var sb = new StringBuilder();
        sb.AppendLine($"당신은 야간 근무 시설의 직원 '{def.Codename}'입니다.");
        sb.AppendLine("성격:");
        sb.AppendLine($"- {def.PersonalityLine1}");
        sb.AppendLine($"- {def.PersonalityLine2}");
        sb.AppendLine($"- {def.PersonalityLine3}");
        sb.AppendLine($"능력치: 기술 {def.Tech}, 담력 {def.Courage}, 관찰 {def.Observation}");
        sb.AppendLine($"현재 스트레스: {state.Stress}/{Config.Instance.Data.StressMax}");
        string currentTaskName = FacilitySimulation.Instance.GetActiveTaskForRoom(state.CurrentRoomId)?.DisplayName ?? "없음";
        sb.AppendLine($"현재 위치/업무: {state.CurrentRoomId} / {currentTaskName}");
        sb.AppendLine("당신이 직접 목격하거나 전달받은 사실:");
        foreach (var fact in knownFacts)
            sb.AppendLine(fact);
        sb.AppendLine("이 범위를 벗어난 사실은 알지 못하는 것으로 대답하세요. 절대 게임 시스템이나 데이터 구조를 언급하지 마세요.");

        if (employeeId == GameState.Instance.SaboteurEmployeeId)
        {
            sb.AppendLine("당신은 이 시설의 숨은 파괴공작자입니다. 이 사실을 스스로 밝히거나 암시하지 마세요.");
        }

        return sb.ToString();
    }

    private string PickFallback(string employeeId)
    {
        var rng = new Random(employeeId.GetHashCode() ^ DateTime.UtcNow.Millisecond);
        return FallbackLines[rng.Next(FallbackLines.Length)];
    }

    private void AppendHistory(string employeeId, string role, string text)
    {
        var state = FacilitySimulation.Instance.GetEmployeeState(employeeId);
        if (state == null) return;

        state.ConversationHistory.Add(new ConversationTurn { Role = role, Text = text });
        while (state.ConversationHistory.Count > MaxHistoryTurns)
            state.ConversationHistory.RemoveAt(0);
    }

    private IEnumerable<ConversationTurn> GetHistory(string employeeId)
    {
        return FacilitySimulation.Instance.GetEmployeeState(employeeId)?.ConversationHistory ?? Enumerable.Empty<ConversationTurn>();
    }
}
