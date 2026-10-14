using System.Collections.Generic;
using System.Threading.Tasks;
using Godot;

namespace NSP.View;

// 포스터(A1 세로)용 스틸 전용 카메라. 게임 플레이에는 전혀 관여하지 않는다.
//
//  - 게임 시점(PlayerSeatRig/Camera3D)은 건드리지 않는다. 이 노드는 current = false 로 둔다.
//  - F8  : 프리뷰 토글. 이 카메라 시점으로 화면을 바꾸고, 실제로 저장될 세로 비율만
//          남기고 바깥을 어둡게 덮는다. 마스크 안쪽 = 저장될 PNG 와 1:1 로 같은 그림이다.
//  - F9  : CaptureResolution 크기의 오프스크린 SubViewport 로 한 장 렌더해서 PNG 저장.
//
// 위치 · 회전 · FOV 는 인스펙터에서 그대로 만지면 된다(에디터 Preview 체크박스도 동작).
// FOV 는 Keep Aspect = Height 기준의 '세로' 화각이다 — 가로 화각은 저장 비율에서 나온다.
// 5000x7000(5:7)이면 세로 80도 → 가로 약 62도.
//
// 조명은 게임 본편 세팅을 그대로 쓴다. 이 컷만 밝히고 싶으면 ExposureBoost /
// FillLightEnergy 를 올린다 — 둘 다 캡처(와 프리뷰) 동안만 적용되고 씬에는 남지 않는다.
public partial class PosterCamera : Camera3D
{
    [ExportGroup("출력")]
    // A1 세로 300dpi 급. i3 내장그래픽에서 메모리로 죽으면 3508x4961(A1 150dpi)로 낮춘다.
    [Export] public Vector2I CaptureResolution = new(5000, 7000);
    [Export] public string OutputDirectory = "user://poster";
    [Export] public Viewport.Msaa CaptureMsaa = Viewport.Msaa.Disabled;
    [Export] public int ShadowAtlasSize = 4096;
    // 드라이버가 뒤집어 주는 경우에만 켠다(보통 필요 없다).
    [Export] public bool FlipVertical = false;

    [ExportGroup("가리기")]
    // 화면 전체를 덮는 게임 UI(시계 · POWER · 자막 · CAM 라벨 · Tab 아이콘 …)를 전부 끈다.
    // CanvasLayer 는 애초에 오프스크린 SubViewport 에 안 찍히지만, 프리뷰를 실제 결과와
    // 똑같이 보여 주려면 화면에서도 꺼야 한다.
    [Export] public bool HideGameUi = true;
    // 1인칭 팔/손. 캐릭터는 2D 로 따로 그려 얹으므로 기본값은 끈다.
    [Export] public bool HidePlayerCharacter = true;
    // 3D 공간에 박혀 있는 글자판(Label3D)들 — 이름으로 찾아 끈다.
    [Export] public string[] ExtraHideNodeNames =
    {
        "CapacityLabel",      // 스위치박스 위 "POWER 3 / 3"
        "M01_BezelLabel",     // 모니터 베젤 "MONITOR 01 — FACILITY"
        "M02_BezelLabel",     // 모니터 베젤 "MONITOR 02 — CCTV"
    };

    [ExportGroup("이 컷 전용 조명")]
    // 월드 Environment 를 복제해서 노출만 곱한다. 원본(게임 본편)은 그대로다.
    [Export(PropertyHint.Range, "0.25,4,0.01")] public float ExposureBoost = 1.0f;
    // 0 이면 보조광 없음. 책상 형태가 안 보일 때만 0.2~0.6 정도로 올린다.
    [Export(PropertyHint.Range, "0,5,0.01")] public float FillLightEnergy = 0.0f;
    [Export] public Color FillLightColor = new(0.72f, 0.80f, 0.95f);
    // 카메라 기준 로컬 오프셋. 기본은 카메라 바로 앞 — 위에서 책상을 덮는 소프트 필.
    [Export] public Vector3 FillLightOffset = new(0f, -0.15f, -0.45f);
    [Export(PropertyHint.Range, "0.5,12,0.1")] public float FillLightRange = 6f;

    [ExportGroup("조작")]
    [Export] public Key CaptureKey = Key.F9;
    [Export] public Key PreviewKey = Key.F8;
    [Export] public bool PreviewOnStart = false;

    private bool _busy;
    private bool _hidden;
    private bool _preview;
    private readonly List<(Node Node, bool Visible, ProcessModeEnum Mode)> _restore = new();
    private CanvasLayer _guide;
    private Label _guideLabel;
    private readonly List<ColorRect> _guideBars = new();
    private OmniLight3D _fill;
    private Camera3D _previousCamera;

    public override void _Ready()
    {
        if (Engine.IsEditorHint()) return;
        KeepAspect = KeepAspectEnum.Height;   // FOV = 세로 화각으로 고정 → 프리뷰와 저장이 일치한다
        Current = false;                      // 게임 시점을 빼앗지 않는다
        if (PreviewOnStart) CallDeferred(MethodName.SetPreview, true);
    }

    public override void _UnhandledInput(InputEvent e)
    {
        if (e is not InputEventKey { Pressed: true, Echo: false } k) return;
        if (k.Keycode == PreviewKey) { SetPreview(!_preview); GetViewport().SetInputAsHandled(); }
        else if (k.Keycode == CaptureKey) { _ = CaptureAsync(); GetViewport().SetInputAsHandled(); }
    }

    // ---------------------------------------------------------------- 프리뷰

    public void SetPreview(bool on)
    {
        if (on == _preview) return;
        _preview = on;

        if (on)
        {
            _previousCamera = GetViewport().GetCamera3D();
            ApplyShotVisibility();
            Environment = BuildExposureOverride();
            MakeCurrent();
            BuildGuide();
            GD.Print($"[PosterCamera] 프리뷰 ON — 마스크 안쪽이 저장될 그림이다. " +
                     $"{CaptureResolution.X}x{CaptureResolution.Y}, FOV {Fov:0.#}도(세로). {CaptureKey} = 저장");
        }
        else
        {
            ClearGuide();
            Environment = null;
            Current = false;
            if (_previousCamera != null && IsInstanceValid(_previousCamera)) _previousCamera.MakeCurrent();
            _previousCamera = null;
            RestoreVisibility();
            GD.Print("[PosterCamera] 프리뷰 OFF");
        }
    }

    private void BuildGuide()
    {
        ClearGuide();
        _guide = new CanvasLayer { Name = "PosterFrameGuide", Layer = 128 };
        for (int i = 0; i < 4; i++)
        {
            var bar = new ColorRect
            {
                Color = new Color(0f, 0f, 0f, 0.82f),
                MouseFilter = Control.MouseFilterEnum.Ignore,
            };
            _guideBars.Add(bar);
            _guide.AddChild(bar);
        }
        _guideLabel = new Label
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            Modulate = new Color(0.85f, 0.88f, 0.92f),
        };
        _guide.AddChild(_guideLabel);
        AddChild(_guide);

        GetViewport().SizeChanged += LayoutGuide;
        LayoutGuide();
    }

    private void ClearGuide()
    {
        if (_guide == null) return;
        GetViewport().SizeChanged -= LayoutGuide;
        _guideBars.Clear();
        _guideLabel = null;
        _guide.QueueFree();
        _guide = null;
    }

    // 저장 비율(5:7 등)에 맞는 사각형만 남기고 네 변을 덮는다.
    // Keep Aspect = Height 라서 세로 화각은 화면과 저장본이 같다 → 화면 세로를 꽉 쓰고
    // 가로만 비율대로 줄이면 그게 정확히 저장될 범위가 된다.
    private void LayoutGuide()
    {
        if (_guide == null || _guideBars.Count < 4 || _guideLabel == null) return;
        Vector2 screen = GetViewport().GetVisibleRect().Size;
        float aspect = CaptureResolution.Y > 0 ? (float)CaptureResolution.X / CaptureResolution.Y : 1f;

        float h = screen.Y;
        float w = h * aspect;
        if (w > screen.X) { w = screen.X; h = w / aspect; }   // 창이 세로로 길 때
        float x = (screen.X - w) * 0.5f;
        float y = (screen.Y - h) * 0.5f;

        _guideBars[0].Position = Vector2.Zero;            _guideBars[0].Size = new Vector2(x, screen.Y);
        _guideBars[1].Position = new Vector2(x + w, 0f);  _guideBars[1].Size = new Vector2(screen.X - x - w, screen.Y);
        _guideBars[2].Position = new Vector2(x, 0f);      _guideBars[2].Size = new Vector2(w, y);
        _guideBars[3].Position = new Vector2(x, y + h);   _guideBars[3].Size = new Vector2(w, screen.Y - y - h);

        _guideLabel.Position = new Vector2(x + 12f, y + 8f);
        _guideLabel.Text = $"POSTER  {CaptureResolution.X}x{CaptureResolution.Y}   FOV {Fov:0.#} (V)   [{CaptureKey}] 저장";
    }

    // ---------------------------------------------------------------- 캡처

    public async Task<string> CaptureAsync()
    {
        if (_busy) { GD.Print("[PosterCamera] 이미 캡처 중이다."); return null; }
        _busy = true;

        int w = Mathf.Clamp(CaptureResolution.X, 16, 16384);
        int h = Mathf.Clamp(CaptureResolution.Y, 16, 16384);
        GD.Print($"[PosterCamera] 캡처 시작 {w}x{h} — 프레임이 몇 초 멈춘다. " +
                 $"(렌더 타깃만 대략 {(w * (long)h * 8L) / (1024 * 1024)}MB)");

        bool wasHidden = _hidden;
        SubViewport vp = null;
        string path = null;
        try
        {
            if (!wasHidden) ApplyShotVisibility();
            ApplyFillLight();
            await Frame();
            await Frame();

            vp = new SubViewport
            {
                Name = "PosterCaptureViewport",
                Size = new Vector2I(w, h),
                RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
                RenderTargetClearMode = SubViewport.ClearMode.Always,
                TransparentBg = false,
                Msaa3D = CaptureMsaa,
                PositionalShadowAtlasSize = Mathf.Max(1024, ShadowAtlasSize),
                HandleInputLocally = false,
                GuiDisableInput = true,
            };
            GetTree().Root.AddChild(vp);
            vp.World3D = GetViewport().World3D;   // 같은 방 · 같은 조명을 그대로 본다

            var cam = new Camera3D
            {
                Fov = Fov,
                Near = Near,
                Far = Far,
                KeepAspect = KeepAspectEnum.Height,
                CullMask = CullMask,
                Environment = BuildExposureOverride(),
            };
            vp.AddChild(cam);
            cam.GlobalTransform = GlobalTransform;
            cam.Current = true;

            // CRT 모니터 화면(각자 SubViewport)과 글로우가 자리를 잡게 몇 프레임 돌린다.
            for (int i = 0; i < 4; i++) await Frame();

            var img = vp.GetTexture()?.GetImage();
            if (img == null)
            {
                GD.PushError("[PosterCamera] 렌더 결과를 못 읽었다. 해상도를 낮춰 보라(3508x4961).");
                return null;
            }
            if (FlipVertical) img.FlipY();

            string dir = string.IsNullOrWhiteSpace(OutputDirectory) ? "user://poster" : OutputDirectory.TrimEnd('/');
            DirAccess.MakeDirRecursiveAbsolute(dir);
            string stamp = Time.GetDatetimeStringFromSystem().Replace("-", "").Replace(":", "").Replace("T", "_");
            path = $"{dir}/poster_{w}x{h}_{stamp}.png";

            var err = img.SavePng(path);
            if (err != Error.Ok)
            {
                GD.PushError($"[PosterCamera] 저장 실패({err}) → {path}");
                return null;
            }
            GD.Print($"[PosterCamera] 저장 완료 → {ProjectSettings.GlobalizePath(path)}");
        }
        finally
        {
            vp?.QueueFree();
            ClearFillLight();
            if (!wasHidden) RestoreVisibility();
            _busy = false;
        }
        return path;
    }

    private async Task Frame()
        => await ToSignal(RenderingServer.Singleton, RenderingServer.SignalName.FramePostDraw);

    // ---------------------------------------------------------------- 가리기

    private void ApplyShotVisibility()
    {
        if (_hidden) return;
        _hidden = true;
        _restore.Clear();

        var names = new HashSet<string>();
        if (ExtraHideNodeNames != null)
            foreach (var n in ExtraHideNodeNames)
                if (!string.IsNullOrWhiteSpace(n)) names.Add(n);
        if (HidePlayerCharacter) names.Add("PlayerCharacter");

        HideWalk(GetTree().Root, names);
    }

    private void HideWalk(Node node, HashSet<string> names)
    {
        foreach (var child in node.GetChildren())
        {
            // 모니터 화면 내용은 각자 SubViewport 안에서 그려진다 — 절대 건드리지 않는다.
            if (child is SubViewport) continue;
            if (child == this || child == _guide) continue;

            bool hide = (HideGameUi && child is CanvasLayer) || names.Contains(child.Name);
            if (hide && TryGetVisible(child, out bool was))
            {
                _restore.Add((child, was, child.ProcessMode));
                // 끄기만 해서는 안 된다 — HUD 중에는 _Process 에서 매 프레임 자기를 다시
                // 켜는 것이 있다(ShiftHud 가 그렇다). 처리까지 멈춰야 꺼진 채로 남는다.
                // 프리뷰/캡처가 끝나면 ProcessMode 와 visible 을 원래대로 돌려놓는다.
                child.ProcessMode = ProcessModeEnum.Disabled;
                child.Set(Node3D.PropertyName.Visible, false);
                continue;   // 자식들도 같이 사라지므로 더 내려갈 필요가 없다
            }
            HideWalk(child, names);
        }
    }

    private static bool TryGetVisible(Node n, out bool visible)
    {
        visible = true;
        if (n is not (Node3D or CanvasItem or CanvasLayer)) return false;
        visible = (bool)n.Get(Node3D.PropertyName.Visible);
        return true;
    }

    private void RestoreVisibility()
    {
        if (!_hidden) return;
        _hidden = false;
        foreach (var (node, visible, mode) in _restore)
        {
            if (!IsInstanceValid(node)) continue;
            node.Set(Node3D.PropertyName.Visible, visible);
            node.ProcessMode = mode;
        }
        _restore.Clear();
    }

    // ---------------------------------------------------------------- 이 컷 전용 조명

    // 월드 Environment 를 복제해 노출만 올린다. 카메라별 override 라서 본편 조명은 그대로다.
    private Godot.Environment BuildExposureOverride()
    {
        if (Mathf.IsEqualApprox(ExposureBoost, 1f)) return null;
        var src = FindWorldEnvironment()?.Environment;
        if (src == null) return null;
        var env = (Godot.Environment)src.Duplicate(true);
        env.TonemapExposure = src.TonemapExposure * ExposureBoost;
        return env;
    }

    private WorldEnvironment FindWorldEnvironment()
    {
        var root = Owner ?? GetTree().CurrentScene;
        if (root == null) return null;
        foreach (var c in root.GetChildren())
            if (c is WorldEnvironment we) return we;
        return null;
    }

    private void ApplyFillLight()
    {
        ClearFillLight();
        if (FillLightEnergy <= 0f) return;
        _fill = new OmniLight3D
        {
            Name = "PosterFillLight",
            Position = FillLightOffset,
            LightColor = FillLightColor,
            LightEnergy = FillLightEnergy,
            LightSpecular = 0.2f,
            OmniRange = FillLightRange,
            ShadowEnabled = false,
        };
        AddChild(_fill);
    }

    private void ClearFillLight()
    {
        if (_fill == null) return;
        _fill.QueueFree();
        _fill = null;
    }

    public override void _ExitTree()
    {
        if (_guide != null && IsInstanceValid(_guide)) GetViewport().SizeChanged -= LayoutGuide;
    }
}
