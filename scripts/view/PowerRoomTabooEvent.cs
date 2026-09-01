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
//  - 3D 발전실 화면 / 결번자 모델  : FacilityCctvWorld (기존 entity.tscn GLB)
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
            sim?.SetSurveillanceTarget(RoomId);
            CCTVMonitorView.Instance?.ForceFeed(26f);

            // 2. 발전실 조명 순간 꺼짐 + 발전기 소리 끊김 + CCTV 노이즈.
            FacilityCctvWorld.Instance?.HauntSpawn(RoomId);
            Sfx.Instance?.Play("power_down", -3f);
            Sfx.Instance?.Play("electric_arc", -6f);
            CCTVMonitorView.Instance?.FlashGlitch(1f);
            CCTVMonitorView.Instance?.Shake(7f, 0.35f);

            // 3. 결번자가 직원 뒤에 소리 없이 서 있음 — 플레이어가 직접 발견할 시간.
            await Wait(1.1);

            // 4. 직원이 이상현상을 인지 — 비명. 결번자가 천천히 카메라를 바라봄.
            Sfx.Instance?.PlayScream(screamer);
            FacilityCctvWorld.Instance?.HauntLookAtCamera(1.2f);
            await Wait(1.4);

            // 5. 결번자가 카메라 앞으로 접근.
            FacilityCctvWorld.Instance?.HauntChargeCamera(1.0f);
            await Wait(1.1);

            // 카메라를 여러 번 내려친다 — 쾅 쾅 쾅.
            for (int i = 0; i < 3; i++)
            {
                FacilityCctvWorld.Instance?.HauntLunge();
                Sfx.Instance?.Play("boom", -2f, 0.9f);
                Sfx.Instance?.Play("metal_clang", -6f, 0.7f);
                CCTVMonitorView.Instance?.Shake(11f, 0.3f);
                CCTVMonitorView.Instance?.FlashGlitch(1f);
                if (i == 1) Sfx.Instance?.PlayScream(occupants.ElementAtOrDefault(1) ?? screamer);
                await Wait(0.42);
            }

            // 마지막에 결번자 웃음.
            Sfx.Instance?.PlayEntityLaugh();
            await Wait(0.7);

            // 6. CCTV 완전 차단 — SIGNAL LOST.
            Sfx.Instance?.Play("cctv_cut", -3f);
            CCTVMonitorView.Instance?.ForceSignalLost(4.5f);
            FacilityCctvWorld.Instance?.HauntEnd();
        }
        catch (Exception ex)
        {
            GD.PushError($"PowerRoomTabooEvent: 연출 중 예외 — {ex.Message}");
        }
        finally
        {
            // 7. 실제 게임 페널티 — 연출 성공 여부와 무관하게 반드시 적용한다.
            //    전력 용량 3 → 1, 발전실 두 직원 스트레스 크게 상승.
            TabooRuleSystem.Instance?.ApplyDeferredConsequence(tabooId, RoomId);
            EventLog.Instance?.LogEvent(LogEventType.PowerOutage, "", RoomId,
                "⚠ POWER SYSTEM ABNORMALITY — 전력 용량 제한 (CAPACITY LIMITED)");
            foreach (var id in occupants)
                FacilitySimulation.Instance?.AddStress(id, Config.Instance?.Data?.PowerTabooStress ?? 35f, "발전실 금기 이상현상");

            await Wait(1.2);
            if (HorrorDirector.Instance != null) HorrorDirector.Instance.CustomEventActive = false;
        }
    }

    private async Task Wait(double seconds) =>
        await ToSignal(GetTree().CreateTimer(seconds), SceneTreeTimer.SignalName.Timeout);
}
