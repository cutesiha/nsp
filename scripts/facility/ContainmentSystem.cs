using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Core;
using NSP.Data;

namespace NSP.Facility;

// ── 격리 구역 봉쇄 계통 ──────────────────────────────────────────────────
//
// **시설에 이상 개체가 계속 출몰하는 이유**가 여기 있다(지시서 §1).
//
// 결번 개체는 일반 직원으로 위장한 채 근무하면서, 정상 업무 시간의 일부를 떼어
// 봉쇄 계통을 교란한다. 그 교란은 즉시 사건이 되지 않는다 — **불안정성으로 쌓인다.**
// 쌓인 불안정성이 높을수록 격리되어 있던 개체가 빠져나올 확률이 올라간다.
//
// 두 가지를 분명히 구분한다.
//   · 격리 구역 봉쇄 시스템 — 개체가 **밖으로 나오지 못하게** 하는 시설의 근본 장치(이 파일)
//   · 관리자 복도 차폐문   — 이미 나온 개체가 **중앙제어실에 못 들어오게** 하는 방어(CorridorNet)
//
// 교란과 별개로, 결번이 풀려 있는 동안에는 봉쇄가 **저절로** 조금씩 흔들린다(그날의
// OpsProfileDef.ContainmentDriftPerSecond). 결번을 격리하면 그 흔들림도, 이상 개체 출몰도 멈춘다.
// 이 값은 근무 HUD 의 「봉쇄 불안정」 게이지로 관리자에게 그대로 보인다(ShiftHud).
//
// 그리고 인과를 1:1 로 묶지 않는다. 교란 직후에만 개체가 나오면 "출몰 = 방금 누가 손댔다"
// 가 되어 추리가 공식이 된다. 불안정성은 **천천히 가라앉는 저수지**라서, 한참 전의 교란이
// 지금의 출몰 확률로 남아 있을 수 있다(지시서 §1).
public sealed class ContainmentSystem
{
    // 내부 진실 한 건. **플레이어에게 그대로 보여 주지 않는다** — 디버그/검증 전용이다.
    public sealed class Disturbance
    {
        public float Time;
        public string ActorId = "";
        public string OriginRoomId = "";
        public float Amount;
        public string Kind = "";
    }

    // 0~100. 0 이면 완전히 안정, 100 이면 언제 터져도 이상하지 않다.
    public float Instability { get; private set; }

    // 오늘 쌓인 교란 기록(내부 진실).
    private readonly List<Disturbance> _log = new();
    public IReadOnlyList<Disturbance> DisturbanceLog => _log;

    // 가라앉는 속도(초당). 근무 하나(150초) 동안 절반쯤 가라앉는다 —
    // 한 번 교란하면 그 영향이 근무 내내 어느 정도 남는다.
    private const float DecayPerSecond = 0.22f;
    // 부르는 자(결번)가 사라진 뒤의 감쇠 배율 — 게이지가 눈에 보이게 내려가야 "멎었다" 가 읽힌다.
    private const float SettleMultiplierWithoutSummoner = 4f;

    // 시설 기록(원인 불명)을 남기는 문턱. 넘을 때마다 한 번씩.
    private static readonly float[] Thresholds = { 25f, 50f, 75f };
    private readonly HashSet<int> _reported = new();
    private float _reportCooldown;

    // start = 근무 시작 시의 불안정성(OpsProfileDef.ContainmentStartInstability).
    public void Reset(float start = 0f)
    {
        Instability = Mathf.Clamp(start, 0f, 100f);
        _log.Clear();
        _reported.Clear();
        // 시작값 아래의 문턱은 이미 지난 것으로 친다 — 근무 시작하자마자 "원인 불명" 기록이 뜨지 않게.
        for (int i = 0; i < Thresholds.Length; i++)
            if (Instability >= Thresholds[i]) _reported.Add(i);
        _reportCooldown = 0f;
    }

    // 결번 개체가 실제로 봉쇄 계통에 손을 댔다. **이 함수가 불린 경우에만** 영향이 생긴다 —
    // "CCTV 가 꺼져 있었다" 는 이유만으로 저절로 불안정해지지 않는다(지시서 §0).
    public void Disturb(float amount, string actorId, string originRoomId, string kind = "tamper")
    {
        if (amount <= 0f) return;
        Instability = Mathf.Clamp(Instability + amount, 0f, 100f);
        _log.Add(new Disturbance
        {
            Time = GameState.Instance?.DayTimeSeconds ?? 0f,
            ActorId = actorId ?? "",
            OriginRoomId = originRoomId ?? "",
            Amount = amount,
            Kind = kind,
        });
    }

    public void Tick(float delta, FacilitySimulation sim)
    {
        if (GameState.Instance?.CurrentPhase != GamePhase.Live) return;
        if (_reportCooldown > 0f) _reportCooldown -= delta;

        // 결번 개체가 근무 중인 한 봉쇄는 저절로 흔들린다 — 그 개체가 이상 개체를 불러오고 있다.
        // 감시와 상관없이 쌓이므로 어느 방을 보고 있는지로 범인을 가려낼 수는 없다.
        // 결번을 격리하면(또는 죽으면) 멈추고, 남은 흔들림은 평소의 네 배 빠르기로 가라앉는다.
        bool summonerGone = sim?.IsSaboteurNeutralized() ?? false;
        if (!summonerGone && DayFeatures.AutoIncidentsEnabled)
            Instability = Mathf.Min(100f, Instability + Mathf.Max(0f, OpsProfile.Today?.ContainmentDriftPerSecond ?? 0f) * delta);
        if (Instability <= 0f) return;

        float decay = DecayPerSecond * (summonerGone ? SettleMultiplierWithoutSummoner : 1f);
        Instability = Mathf.Max(0f, Instability - decay * delta);

        // 문턱을 넘으면 시설에 **원인 불명의** 기록이 남는다.
        //
        // 이것은 "강아지가 봉쇄 계통을 조작했다" 가 아니다. 관측되지 않은 행동은 자료가
        // 되지 않는다(지시서 §2-3). 남는 것은 객관적인 설비 이상 하나뿐이고, 그 원인을
        // 좁히는 것은 CCTV 기록 · 증언 · 동선 대조다.
        for (int i = 0; i < Thresholds.Length; i++)
        {
            if (Instability < Thresholds[i] || !_reported.Add(i)) continue;
            if (_reportCooldown > 0f) continue;
            _reportCooldown = 12f;
            EventLog.Instance?.LogEvent(LogEventType.SignalAnomaly, "", FacilitySimulation.CoreRoomIdPublic,
                "▲ 격리 구역 봉쇄 계통 - 차폐 신호 불안정 (원인 불명)");
            IncidentTracker.Anomaly(FacilitySimulation.CoreRoomIdPublic, "▲ 봉쇄 계통 불안정",
                "격리 구역 차폐 신호가 규정 범위를 벗어났습니다. 원인 미상.");
        }
        // 다시 내려가면 그 문턱은 또 보고될 수 있다(같은 일이 두 번 일어난 것이다).
        for (int i = Thresholds.Length - 1; i >= 0; i--)
            if (Instability < Thresholds[i] - 6f) _reported.Remove(i);
    }

    // ── 이상 개체 출몰 쪽이 읽는 값 ──────────────────────────────────────
    //
    // 기본 확률을 **대체하지 않고 더한다.** 불안정성이 0 이어도 개체는 나온다 —
    // 봉쇄 계통은 원래도 완벽하지 않다. 결번의 교란은 그 위에 얹히는 가중치다.
    public float AppearChanceMultiplier => 1f + 1.6f * (Instability / 100f);

    // 출몰 판정 간격을 줄인다(불안정할수록 자주 본다). 0.55 배가 하한.
    public float CheckIntervalScale => Mathf.Lerp(1f, 0.55f, Instability / 100f);

    // 검증용 — 그 시각 이전의 마지막 교란. 인과가 실제로 이어졌는지 테스트가 확인한다.
    public Disturbance LastDisturbanceBefore(float time) =>
        _log.Where(d => d.Time <= time).OrderByDescending(d => d.Time).FirstOrDefault();

    public int DisturbanceCount => _log.Count;

    public string DebugSummary(FacilitySimulation sim)
    {
        var sb = new System.Text.StringBuilder();
        sb.AppendLine($"봉쇄 불안정성 {Instability:0.0} / 교란 {_log.Count}건 (플레이어 비공개)");
        foreach (var d in _log)
            sb.AppendLine($"  {SaboteurPlan.Clock(d.Time)} {d.Kind} +{d.Amount:0.0} " +
                          $"@{sim?.RoomDisplayName(d.OriginRoomId) ?? d.OriginRoomId} " +
                          $"({sim?.GetEmployeeDef(d.ActorId)?.Codename ?? d.ActorId})");
        return sb.ToString();
    }
}
