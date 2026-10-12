using System.Collections.Generic;
using Godot;
using NSP.Data;
using NSP.Facility;

namespace NSP.View;

// 괴물이 **튀어나오기 한참 전에** 읽어 두고 한 번 그려 둔다.
//
// 왜 필요한가 — 실측(docs/PERFORMANCE_BASELINE.md D):
//
//   점프스케어 1회차 : 호출 → 화면 표시 142 ms  (모델 로드 88.8 ms 포함)
//   점프스케어 2회차 : 호출 → 화면 표시  31 ms  (로드 0 ms)
//
// 차이 111 ms 가 전부 "처음이라서" 생긴 값이다. 공포 연출에서 그 111 ms 는 성능
// 문제가 아니라 **연출이 망가지는 것**이다 — 놀라야 할 순간에 화면이 멈춘다.
//
// 두 가지를 미리 치른다.
//
//   ① 리소스 : GD.Load + Instantiate + 리그 정리 + 키 맞춤
//   ② 첫 렌더 : 셰이더 변형 컴파일 · 머티리얼 생성 · 텍스처 GPU 업로드
//
// ②가 없으면 ①만 해도 처음 보일 때 여전히 멈춘다. 그래서 작은 오프스크린 뷰포트에
// 한 번 **실제로 그려** 본다. 화면에는 나오지 않는다.
//
// 비용은 **한 프레임에 몰지 않는다.** 괴물 하나마다 프레임을 넘겨 가며 처리해,
// 준비 자체가 새로운 끊김이 되지 않게 한다.
public static class MonsterWarmup
{
    // 준비해 둘 배우들. 연출기들이 자기 배우를 등록한다.
    //
    // 괴물 모델을 쓰는 곳이 다섯이다(제어실 얼굴 · 사망 연출 · CCTV 복도 · CCTV
    // 점프스케어 · 작업실 귀신). 서로 다른 월드(SubViewport)에 있거나 동시에 떠야 해서
    // 노드는 각자 가져야 한다. 다만 **메시 · 텍스처 · 애니메이션 리소스는 공유된다** —
    // PackedScene 인스턴스는 리소스를 복제하지 않으므로 VRAM 은 늘지 않는다.
    private static readonly List<MonsterActor> _actors = new();

    // 읽어 둔 PackedScene 을 **붙잡아 둔다.**
    //
    // 준비 ①은 모델을 한 번 그려 보고 바로 지운다. 그 순간 아무도 그 PackedScene 을
    // 참조하지 않으면 리소스 캐시에서 빠질 수 있고, 그러면 ②에서 디스크를 다시 읽는다 —
    // 미리 읽어 둔 의미가 없어진다. 셋뿐이라 붙잡아 두는 비용도 작다.
    private static readonly List<PackedScene> _held = new();

    public static void Register(MonsterActor a)
    {
        // 죽은 배우(씬이 바뀌며 해제된 것)는 목록에서 치운다 — 그대로 두면 다음 준비에서
        // 해제된 노드에 자식을 붙이려다 터진다.
        _actors.RemoveAll(x => x == null || !x.IsAlive);
        if (a == null || _actors.Contains(a)) return;
        _actors.Add(a);

        // **준비가 이미 끝난 뒤에 생긴 배우**(연출기마다 만들어지는 시점이 다르다).
        // 그냥 두면 이 배우만 처음 쓸 때 멈춘다. 이 시점에는 PackedScene 이 이미
        // 리소스 캐시에 올라와 있어 곧바로 꽂아도 싸다.
        if (!Done) return;
        foreach (var def in For(Mathf.Max(1, NSP.Core.GameState.Instance?.CurrentDay ?? 1)))
            a.Preload(def);
    }

    public static void Forget(MonsterActor a) => _actors.Remove(a);

    // 이번 판에서 이미 준비를 마쳤는가.
    public static bool Done { get; private set; }

    public static void Reset()
    {
        Done = false;
        _actors.Clear();
    }

    // 그 날 나올 수 있는 개체 전부.
    // 씬이 다시 올라오면(타이틀 → 새 게임) 배우 목록이 통째로 바뀐다.
    public static void OnSceneReload() => _actors.RemoveAll(x => x == null || !x.IsAlive);

    private static List<MonsterDef> For(int day)
    {
        var list = new List<MonsterDef>();
        foreach (var m in MonsterThreatSystem.Monsters)
            if (m != null && m.FirstDay <= day && !string.IsNullOrEmpty(m.ModelPath))
                list.Add(m);
        return list;
    }

    // 준비를 시작한다. 이미 했으면 아무 일도 하지 않는다.
    public static async void Run(Node host, int day)
    {
        if (Done || host == null || !GodotObject.IsInstanceValid(host)) return;
        // 측정용 스위치 — 준비를 **끄고** 돌려 같은 기계에서 A/B 를 잴 수 있게 한다.
        // (노트북은 돌릴수록 느려지므로, 전후 비교는 반드시 같은 조건에서 번갈아 재야 한다.)
        if (System.Array.IndexOf(OS.GetCmdlineUserArgs(), "nowarm") >= 0) return;
        Done = true;

        var defs = For(day);
        if (defs.Count == 0) return;

        // ── ① 첫 렌더까지 미리 — 작은 오프스크린 뷰포트 ────────────────
        //
        // 32×32 면 셰이더 변형과 텍스처 업로드를 일으키기에 충분하고, 화면 비용은
        // 사실상 없다. 자기 월드(OwnWorld3D)를 써서 실제 장면의 조명·환경을 건드리지 않는다.
        var vp = new SubViewport
        {
            Size = new Vector2I(32, 32),
            OwnWorld3D = true,
            Disable3D = false,
            GuiDisableInput = true,
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            RenderTargetClearMode = SubViewport.ClearMode.Always,
        };
        host.AddChild(vp);

        var cam = new Camera3D { Position = new Vector3(0f, 1.1f, 2.4f), Current = true };
        vp.AddChild(cam);
        vp.AddChild(new OmniLight3D { Position = new Vector3(0.6f, 1.8f, 1.6f), OmniRange = 8f });

        foreach (var def in defs)
        {
            if (!GodotObject.IsInstanceValid(host)) return;
            if (!ResourceLoader.Exists(def.ModelPath)) continue;

            var packed = GD.Load<PackedScene>(def.ModelPath);
            if (packed == null) continue;
            if (!_held.Contains(packed)) _held.Add(packed);
            var inst = packed.Instantiate<Node3D>();
            if (inst == null) continue;
            vp.AddChild(inst);
            inst.Position = Vector3.Zero;

            // 실제로 두 프레임 그린다 — 한 프레임은 파이프라인이 준비되기 전에 끝날 수 있다.
            await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

            vp.RemoveChild(inst);
            inst.QueueFree();
            // 다음 개체는 다음 프레임에 — 준비가 끊김을 만들지 않게.
            await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
        }

        if (GodotObject.IsInstanceValid(vp)) vp.QueueFree();

        // ── ② 실제 배우들에게 모델을 미리 꽂아 둔다 ────────────────────
        //
        // 여기서는 PackedScene 이 이미 리소스 캐시에 올라와 있어 ①보다 훨씬 싸다.
        // 그래도 배우 하나마다 프레임을 넘긴다.
        foreach (var actor in _actors.ToArray())
        {
            if (!GodotObject.IsInstanceValid(host)) return;
            foreach (var def in defs)
            {
                actor?.Preload(def);
                await host.ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
            }
        }
    }
}
