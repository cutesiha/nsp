using System.Reflection;
using Godot;
using NSP.Core;
using NSP.Data;
using NSP.Facility;
using NSP.View;

namespace NSP.Debug;

// 인터뷰 스탠딩 일러 CRT 셰이더 확인용 캡처. 창 모드로 실행해야 한다(헤드리스는 그림을 그리지 않는다).
//
//   godot --path . res://scenes/debug/StandingShaderShot.tscn -- <저장 폴더>
//
// 인터뷰 화면(InterviewCCTVView)을 직원 6명마다 셰이더 적용 / 원본으로 한 장씩,
// 긴장 연출(PulseTension) 한 장을 PNG 로 저장하고 종료한다.
public partial class StandingShaderShot : Node
{
    private static readonly string[] Ids = { "cat", "rabbit", "sheep", "fox", "wolf", "dog" };

    public override void _Ready() => _ = Run();

    private async System.Threading.Tasks.Task Run()
    {
        var args = OS.GetCmdlineUserArgs();
        string dir = args.Length > 0 ? args[0] : ProjectSettings.GlobalizePath("user://");
        var sim = FacilitySimulation.Instance;
        GameState.Instance.ResetRun(1);
        sim.ResetRun();

        // 인터뷰 화면은 RestRosterView.SelectedEmployeeId 를 본다 — 선택만 흉내 낸다.
        var roster = new RestRosterView();
        var rosterVp = MakeVp(roster);
        var view = new InterviewCCTVView();
        var vp = MakeVp(view);
        await Frames(4);

        var selProp = typeof(RestRosterView).GetProperty("SelectedEmployeeId");
        var portrait = (TextureRect)typeof(InterviewCCTVView)
            .GetField("_portrait", BindingFlags.NonPublic | BindingFlags.Instance).GetValue(view);
        var mat = portrait.Material;
        GD.Print($"material = {(mat as ShaderMaterial)?.Shader?.ResourcePath ?? "(없음)"}");

        foreach (var id in Ids)
        {
            selProp.SetValue(roster, id);
            await Frames(3);
            var sm = (ShaderMaterial)portrait.Material;
            GD.Print($"{id}: glow_amt={sm.GetShaderParameter("glow_amt")} glow_center={sm.GetShaderParameter("glow_center")} " +
                     $"scan={sm.GetShaderParameter("scan_amt")} grain={sm.GetShaderParameter("grain_amt")}");
            Save(vp, dir, $"standing_{id}.png");
            portrait.Material = null;
            await Frames(2);
            Save(vp, dir, $"standing_{id}_raw.png");
            portrait.Material = mat;
        }

        // 긴장 연출 — 마지막 직원에서 PulseTension 직후.
        selProp.SetValue(roster, "cat");
        await Frames(3);
        view.PulseTension();
        await Frames(2);
        Save(vp, dir, "standing_cat_tension.png");

        GD.Print("saved → " + dir);
        rosterVp.QueueFree();
        GetTree().Quit();
    }

    private SubViewport MakeVp(Control v)
    {
        var vp = new SubViewport
        {
            Size = new Vector2I(800, 600), RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            Disable3D = true,
        };
        AddChild(vp);
        vp.AddChild(v);
        return vp;
    }

    private static void Save(SubViewport vp, string dir, string file)
        => vp.GetTexture().GetImage().SavePng(dir + "/" + file);

    private async System.Threading.Tasks.Task Frames(int n)
    {
        for (int i = 0; i < n; i++)
            await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);
    }
}
