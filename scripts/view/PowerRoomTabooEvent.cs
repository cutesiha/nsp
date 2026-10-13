using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.Taboo;
using NSP.Ui;

namespace NSP.View;

// DAY1 발전실 금기(발전실 2명 금지) 위반 시의 전용 연출 + 페널티.
// TabooRuleSystem 은 defer_consequence 금기의 결과 적용을 이 노드에 맡긴다 —
// 위반 로그는 즉시, 실제 운영 페널티(전력 용량 3→1, 스트레스)는 CCTV 연출이 끝나는 순간 적용한다.
//
// 재사용:
//  - 금기 판정 / 위반 로그        : TabooRuleSystem (그대로)
//  - 전력 용량 감소                : GameState.TriggerPowerAccident (TabooRuleSystem.ApplyDeferredConsequence 경유)
//  - CCTV 강제 전환 / 신호 차단    : FacilitySimulation.SetSurveillanceTarget + CCTVMonitorView
//  - 3D 발전실 화면 / 개체 모델  : FacilityCctvWorld (data/threats 의 세 개체)
//  - SENSOR 경고                   : AlertSystem (PROTOCOL VIOLATION / POWER SYSTEM ABNORMALITY)
//  - 스트레스                      : FacilitySimulation.AddStress (연동 지점)
//  - 공포 연출 억제                : HorrorDirector.CustomEventActive
public partial class PowerRoomTabooEvent : Node
{
    private const string RoomId = "power_room";

    private bool _wired;
    private bool _ranThisShift;
    private GamePhase _lastPhase = GamePhase.Prep;

    public override void _Process(double _)
    {
        if (!_wired && EventLog.Instance != null)
        {
            EventLog.Instance.EntryLogged += OnEntryLogged;
            _wired = true;
        }

        var phase = GameState.Instance?.CurrentPhase ?? GamePhase.Prep;
        if (phase == GamePhase.Live && _lastPhase != GamePhase.Live)
            _ranThisShift = false;
        _lastPhase = phase;
    }

    public override void _ExitTree()
    {
        if (_wired && EventLog.Instance != null)
            EventLog.Instance.EntryLogged -= OnEntryLogged;
    }

    private void OnEntryLogged()
    {
        if (_ranThisShift || GameState.Instance?.CurrentPhase != GamePhase.Live) return;
        var entries = EventLog.Instance?.GetAllEntries();
        if (entries == null || entries.Count == 0) return;
        var e = entries[^1];
        if (e.EventType != LogEventType.TabooViolation || e.RoomId != RoomId) return;

        // 이 방을 대상으로 한 "결과 지연" 금기가 활성 상태여야 한다.
        var taboo = TabooRuleSystem.Instance?.GetActiveTaboos().FirstOrDefault(t =>
            t.ConditionType == TabooConditionType.MaxHeadcountInRoom
            && t.ConditionParams.GetValueOrDefault("room_id", "").AsString() == RoomId
            && t.ConditionParams.GetValueOrDefault("defer_consequence", false).AsBool());
        if (taboo == null) return;

        _ranThisShift = true;
        _ = RunSequence(taboo.TabooId);
    }

    private async Task RunSequence(string tabooId)
    {
        var sim = FacilitySimulation.Instance;
        var occupants = sim?.GetRoomState(RoomId)?.OccupantEmployeeIds.ToList() ?? new List<string>();
        string screamer = occupants.FirstOrDefault() ?? "";

        try
        {
            if (HorrorDirector.Instance != null) HorrorDirector.Instance.CustomEventActive = true;

            // 1. CCTV 강제로 발전실 전환 + 전력/고장과 무관하게 피드 유지.
            // 개체 연출이 끝날 때까지 다른 방을 눌러도 발전실 CCTV에서 빠져나갈 수 없다.
            sim?.ForceSurveillanceTarget(RoomId, 14f);
            CCTVMonitorView.Instance?.ForceFeed(26f);

            // 2. 발전실 조명 순간 꺼짐 + 발전기 소리 끊김 + CCTV 노이즈.
            Sfx.Instance?.Play("power_down", -3f);
            Sfx.Instance?.Play("electric_arc", -6f);
            CCTVMonitorView.Instance?.FlashGlitch(1f);
            CCTVMonitorView.Instance?.Shake(7f, 0.35f);

            // 3. 설비가 무너지는 소리 — 아크가 튀고 금속이 부딪친다.
            //
            // **여기에 개체는 나오지 않는다.** 전에는 이 자리에서 귀신이 직원 뒤에 서고,
            // 카메라를 돌아보고, 얼굴을 들이밀고, 웃었다. 그러면 플레이어에게
            // "발전실 사고 = 귀신" 이라는 엉뚱한 공식이 생긴다 — 이건 금기를 어겨서 난
            // **전기 사고**이고, 귀신은 그것과 아무 상관이 없다. 개체가 나오는 자리는
            // GhostHauntSystem 이 따로 정한다.
            await Wait(0.9);

            // 4. 직원이 비명을 지른다.
            Sfx.Instance?.PlayScream(screamer);
            await Wait(0.7);

            for (int i = 0; i < 3; i++)
            {
                Sfx.Instance?.Play("boom", -2f, 0.9f);
                Sfx.Instance?.Play("metal_clang", -6f, 0.7f);
                CCTVMonitorView.Instance?.Shake(11f, 0.3f);
                CCTVMonitorView.Instance?.FlashGlitch(1f);
                if (i == 1) Sfx.Instance?.PlayScream(occupants.ElementAtOrDefault(1) ?? screamer);
                await Wait(0.42);
            }

            // 6. CCTV 완전 차단 — SIGNAL LOST.
            Sfx.Instance?.Play("cctv_cut", -3f);
            CCTVMonitorView.Instance?.ForceSignalLost(4.5f);
            sim?.ReleaseForcedSurveillance(RoomId);
        }
        catch (Exception ex)
        {
            GD.PushError($"PowerRoomTabooEvent: 연출 중 예외 — {ex.Message}");
        }
        finally
        {
            sim?.ReleaseForcedSurveillance(RoomId);
            // 7. 실제 게임 페널티 — 연출 성공 여부와 무관하게 반드시 적용한다.
            //    전력 용량 3 → 1, 발전실 두 직원 스트레스 크게 상승.
            // 이 사고는 수리 업무를 남기지 않아 방 상태만으로는 알 수 없다.
            // 먼저 사고로 등록해야 미니맵이 빨갛게 뜨고 전력 손실이 결과로 묶인다.
            IncidentTracker.Open(RoomId, "발전 설비 이상", "금기 위반",
                "발전기 점검 완료 필요", 2);
            TabooRuleSystem.Instance?.ApplyDeferredConsequence(tabooId, RoomId);
            EventLog.Instance?.LogEvent(LogEventType.PowerOutage, "", RoomId,
                "⚠ POWER SYSTEM ABNORMALITY — 전력 용량 제한 (CAPACITY LIMITED)");
            foreach (var id in occupants)
                FacilitySimulation.Instance?.AddStress(id, Config.Instance?.Data?.PowerTabooStress ?? 35f, "발전실 금기 이상현상");

            await Wait(1.2);
            // 끝나고 나서 따로 점프스케어를 예약하지 않는다 — 그것도 "발전실 사고가
            // 귀신을 부른다" 로 읽히던 자리다.
            if (HorrorDirector.Instance != null) HorrorDirector.Instance.CustomEventActive = false;
        }
    }

    private async Task Wait(double seconds) =>
        await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
}
