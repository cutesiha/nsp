using System.Collections.Generic;
using System.Linq;
using Godot;
using NSP.Data;

namespace NSP.Tools;

// CHARACTER STUDIO — HAIR SELECTION
//
// 8명에게 머리카락을 하나씩 **직접 골라 보고 저장하는** 자리다. 게임 흐름과는 완전히
// 떨어져 있고, 실제 캐릭터 모델에는 아무것도 적용하지 않는다(지시서 §8 · §10).
//
//   godot --path . res://scenes/tools/CharacterHairStudio.tscn
//   (에디터에서 씬을 열고 F6 으로 실행해도 된다)
//
// 화면
//   왼쪽   캐릭터 8명 · 베이스 성별 · 지금 고른 내용
//   가운데 3D 미리보기 — 드래그로 회전, 휠로 확대, [상반신] 로 얼굴 접사
//   오른쪽 헤어 목록(썸네일) · 위치/회전/크기/색 · 위치 초기화 · 저장
//
// 성능: 목록은 카탈로그(json)와 미리 구운 썸네일(png)만 읽는다. 3D 장면에는 **지금
// 고른 헤어 하나만** 올린다(지시서 §4).
public partial class CharacterHairStudio : Node3D
{
    private const string ThumbDir = "res://assets/characters/hair/thumbs";

    private static readonly (string Name, string Hex)[] Palette =
    {
        ("검정", "#14100E"), ("흑갈색", "#2A1E18"), ("짙은 갈색", "#4A332A"),
        ("갈색", "#684A39"), ("밝은 갈색", "#8A6A4B"), ("금발", "#C9A86A"),
        ("적갈색", "#6E2F24"), ("회색", "#8A8784"), ("백발", "#D8D5CE"),
    };

    // ── 상태 ────────────────────────────────────────────────────────────

    private HairCatalog _cat;
    private HairAssignmentStore _store;

    private string _charId = "admin";
    private string _body = "male";
    private string _hairId = "";
    private Vector3 _pos, _rot;
    private float _scale = 1f;
    private string _colorHex = "#2A1E18", _colorName = "흑갈색";
    // 기본값은 **남녀 팩을 다 보여주는 것**이다. 여성 팩(105개)이 남성 팩(9개)보다 훨씬
    // 많아서, 남성 베이스에서도 그쪽을 쓸 수 있어야 고를 거리가 생긴다. 끄면 지금 베이스
    // 성별의 팩만 남는다.
    private bool _showOtherPack = true;
    private bool _dirty;
    private bool _muteSliders;

    // ── 3D ──────────────────────────────────────────────────────────────

    private SubViewport _sub;
    private Node3D _camTarget, _camPivot;
    private Camera3D _cam;
    private Node3D _baseRoot;
    private Skeleton3D _skel;
    private BoneAttachment3D _attach;
    private MeshInstance3D _hairMesh;
    private StandardMaterial3D _hairMat;

    private float _yaw = 20f, _pitch = -8f, _dist = 2.6f;
    private bool _closeUp;
    private bool _dragging;

    // ── UI ──────────────────────────────────────────────────────────────

    private ItemList _hairList;
    private readonly List<HairEntry> _listed = new();
    private readonly Dictionary<string, Button> _charButtons = new();
    private Button _maleBtn, _femaleBtn, _closeUpBtn;
    private Label _summary, _status, _hairInfo;
    private readonly Dictionary<string, HSlider> _sliders = new();
    private readonly Dictionary<string, Label> _sliderValues = new();
    private ColorPickerButton _colorBtn;

    public override void _Ready()
    {
        _cat = HairCatalog.Load();
        _store = HairAssignmentStore.Load();

        Build3D();
        BuildUi();

        if (_cat.Entries.Count == 0)
            SetStatus("카탈로그가 비어 있다 — 먼저 res://scenes/tools/HairAssetBuilder.tscn 을 한 번 돌려라", true);

        SelectCharacter("admin");
    }

    // ── 3D 장면 ─────────────────────────────────────────────────────────

    private void Build3D()
    {
        _sub = new SubViewport
        {
            Size = new Vector2I(900, 820),
            RenderTargetUpdateMode = SubViewport.UpdateMode.Always,
            TransparentBg = false,
            Msaa3D = Viewport.Msaa.Msaa4X,
        };

        var world = new Node3D { Name = "Studio" };
        _sub.AddChild(world);

        // 중립적인 스튜디오 조명 — 게임보다 밝다. 머리카락 결을 보려면 밝아야 한다.
        var env = new Godot.Environment
        {
            BackgroundMode = Godot.Environment.BGMode.Color,
            BackgroundColor = new Color(0.13f, 0.135f, 0.15f),
            AmbientLightSource = Godot.Environment.AmbientSource.Color,
            AmbientLightColor = new Color(0.62f, 0.64f, 0.70f),
            AmbientLightEnergy = 1.15f,
            TonemapMode = Godot.Environment.ToneMapper.Filmic,
            SsaoEnabled = false,
        };
        world.AddChild(new WorldEnvironment { Environment = env });

        var key = new DirectionalLight3D { LightEnergy = 1.6f, ShadowEnabled = true };
        key.RotationDegrees = new Vector3(-32f, 38f, 0f);
        world.AddChild(key);

        var fill = new DirectionalLight3D { LightEnergy = 0.75f, ShadowEnabled = false };
        fill.RotationDegrees = new Vector3(-12f, -130f, 0f);
        world.AddChild(fill);

        var rim = new DirectionalLight3D
        {
            LightEnergy = 1.1f, ShadowEnabled = false,
            LightColor = new Color(0.78f, 0.84f, 1f),
        };
        rim.RotationDegrees = new Vector3(12f, 178f, 0f);
        world.AddChild(rim);

        // 바닥 — 발이 공중에 떠 보이지 않게 한 장 깔아 둔다.
        var floor = new MeshInstance3D
        {
            Mesh = new PlaneMesh { Size = new Vector2(6f, 6f) },
            MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = new Color(0.17f, 0.175f, 0.19f),
                Roughness = 0.95f,
            },
        };
        world.AddChild(floor);

        _camTarget = new Node3D { Position = new Vector3(0f, 0.95f, 0f) };
        world.AddChild(_camTarget);
        _camPivot = new Node3D();
        _camTarget.AddChild(_camPivot);
        _cam = new Camera3D { Fov = 42f, Position = new Vector3(0f, 0f, _dist) };
        _camPivot.AddChild(_cam);

        _baseRoot = new Node3D { Name = "Base" };
        world.AddChild(_baseRoot);
    }

    private void LoadBase(string sex)
    {
        foreach (Node c in _baseRoot.GetChildren()) { _baseRoot.RemoveChild(c); c.QueueFree(); }
        _skel = null; _attach = null; _hairMesh = null;

        HairBaseEntry b = _cat.Base(sex);
        if (b == null) { SetStatus($"{sex} 베이스를 카탈로그에서 찾지 못했다", true); return; }
        var packed = ResourceLoader.Load<PackedScene>(b.ScenePath);
        if (packed == null) { SetStatus($"{b.ScenePath} 를 불러오지 못했다", true); return; }

        var inst = packed.Instantiate<Node3D>();
        _baseRoot.AddChild(inst);
        _skel = HairSourceSplitter.FindSkeleton(inst);
        if (_skel == null) { SetStatus("베이스에 Skeleton3D 가 없다", true); return; }

        // 눈썹은 원본 텍스처가 팩에 빠져 있어서 하얗게 뜬다. 미리보기에서만 머리색을
        // 따라가게 칠한다 — 원본 파일이나 게임 캐릭터는 건드리지 않는다.
        TintEyebrows();

        int head = _skel.FindBone(b.HeadBone);
        _attach = new BoneAttachment3D { BoneName = b.HeadBone, BoneIdx = head };
        _skel.AddChild(_attach);

        _hairMat = new StandardMaterial3D
        {
            AlbedoColor = Color.FromString(_colorHex, Colors.Black),
            Roughness = 0.62f, Metallic = 0f,
            // 머리카락은 얇은 면으로 만들어져 있다. 한쪽만 그리면 속이 비어 보인다.
            CullMode = BaseMaterial3D.CullModeEnum.Disabled,
        };
        _hairMesh = new MeshInstance3D { MaterialOverride = _hairMat, Visible = false };
        _attach.AddChild(_hairMesh);

        FrameCamera();
    }

    // 캐릭터나 베이스를 바꿔도 보고 있던 배율을 유지한다 — 얼굴을 당겨 보던 중에
    // 다른 캐릭터로 넘어가면 허리가 잡히던 문제가 있었다.
    private void FrameCamera()
    {
        HairBaseEntry b = _cat.Base(_body);
        if (b == null || _camTarget == null) return;
        _camTarget.Position = new Vector3(0f,
            _closeUp ? b.SkullCenter.Y - 0.02f : b.BodyHeight * 0.52f, 0f);
    }

    private void TintEyebrows()
    {
        if (_skel == null) return;
        foreach (MeshInstance3D mi in _skel.GetChildren().OfType<MeshInstance3D>())
        {
            if (!mi.Name.ToString().StartsWith("Eyebrow")) continue;
            mi.MaterialOverride = new StandardMaterial3D
            {
                AlbedoColor = Color.FromString(_colorHex, Colors.Black).Darkened(0.25f),
                Roughness = 0.8f,
                CullMode = BaseMaterial3D.CullModeEnum.Disabled,
            };
        }
    }

    // ── UI ──────────────────────────────────────────────────────────────

    private void BuildUi()
    {
        var layer = new CanvasLayer { Layer = 10 };
        AddChild(layer);

        var bg = new ColorRect { Color = new Color(0.07f, 0.075f, 0.085f) };
        bg.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        layer.AddChild(bg);

        var page = new VBoxContainer { OffsetLeft = 10, OffsetTop = 8, OffsetRight = -10, OffsetBottom = -8 };
        page.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        page.AddThemeConstantOverride("separation", 8);
        layer.AddChild(page);

        var title = new Label { Text = "CHARACTER STUDIO — HAIR SELECTION" };
        Style(title, 22, new Color(0.88f, 0.9f, 0.92f));
        page.AddChild(title);

        var row = new HBoxContainer { SizeFlagsVertical = Control.SizeFlags.ExpandFill };
        row.AddThemeConstantOverride("separation", 10);
        page.AddChild(row);

        row.AddChild(LeftPanel());
        row.AddChild(CenterPanel());
        row.AddChild(RightPanel());

        _status = new Label { Text = "드래그 = 회전 · 휠 = 확대/축소" };
        Style(_status, 13, new Color(0.55f, 0.58f, 0.6f));
        page.AddChild(_status);
    }

    private Control LeftPanel()
    {
        var box = Panel(260);
        var col = (VBoxContainer)box.GetChild(0);

        col.AddChild(Head("CHARACTER"));
        foreach ((string id, string label, _) in HairAssignmentStore.Characters)
        {
            var b = new Button { Text = label, Alignment = HorizontalAlignment.Left, ToggleMode = true };
            Style(b, 15, new Color(0.84f, 0.86f, 0.88f));
            string captured = id;
            b.Pressed += () => SelectCharacter(captured);
            _charButtons[id] = b;
            col.AddChild(b);
        }

        col.AddChild(Gap(10));
        col.AddChild(Head("BODY"));
        var bodyRow = new HBoxContainer();
        bodyRow.AddThemeConstantOverride("separation", 6);
        _maleBtn = ToggleBtn("MALE", () => SetBody("male"));
        _femaleBtn = ToggleBtn("FEMALE", () => SetBody("female"));
        _maleBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        _femaleBtn.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
        bodyRow.AddChild(_maleBtn);
        bodyRow.AddChild(_femaleBtn);
        col.AddChild(bodyRow);

        col.AddChild(Gap(12));
        col.AddChild(Head("현재 선택"));
        _summary = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        Style(_summary, 14, new Color(0.78f, 0.82f, 0.84f));
        col.AddChild(_summary);

        col.AddChild(Gap(0, expand: true));

        var save = new Button { Text = "[ 선택 결과 저장 ]" };
        Style(save, 16, new Color(0.65f, 0.9f, 0.7f));
        save.CustomMinimumSize = new Vector2(0, 42);
        save.Pressed += SaveAll;
        col.AddChild(save);
        return box;
    }

    private Control CenterPanel()
    {
        var holder = new VBoxContainer { SizeFlagsHorizontal = Control.SizeFlags.ExpandFill };
        holder.AddThemeConstantOverride("separation", 6);

        var container = new SubViewportContainer
        {
            Stretch = true,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            MouseFilter = Control.MouseFilterEnum.Stop,
        };
        container.AddChild(_sub);
        container.GuiInput += OnViewportInput;
        holder.AddChild(container);

        var tools = new HBoxContainer();
        tools.AddThemeConstantOverride("separation", 8);
        _closeUpBtn = ToggleBtn("상반신 확대", ToggleCloseUp);
        tools.AddChild(_closeUpBtn);
        tools.AddChild(Btn("정면", () => { _yaw = 0f; _pitch = -4f; }));
        tools.AddChild(Btn("측면", () => { _yaw = 90f; _pitch = -4f; }));
        tools.AddChild(Btn("뒤", () => { _yaw = 180f; _pitch = -4f; }));
        tools.AddChild(Btn("앞/뒤 뒤집기", () =>
        {
            _rot.Y = Mathf.Wrap(_rot.Y + 180f, -180f, 180f);
            PushSliders(); Apply(); Mark();
        }));
        holder.AddChild(tools);
        return holder;
    }

    private Control RightPanel()
    {
        var box = Panel(380);
        var col = (VBoxContainer)box.GetChild(0);

        col.AddChild(Head("HAIR"));

        var filter = new HBoxContainer();
        filter.AddThemeConstantOverride("separation", 4);
        foreach ((string key, string label) in new[]
                 {
                     ("all", "전체"), ("short", "짧은"), ("medium", "중간"),
                     ("long", "긴"), ("volume", "긴·풍성"),
                 })
        {
            string k = key;
            filter.AddChild(Btn(label, () => { _filter = k; RebuildHairList(); }));
        }
        col.AddChild(filter);

        var other = new CheckBox { Text = "남녀 헤어 모두 보기", ButtonPressed = _showOtherPack };
        Style(other, 13, new Color(0.7f, 0.73f, 0.75f));
        other.Toggled += on => { _showOtherPack = on; RebuildHairList(); };
        col.AddChild(other);

        _hairList = new ItemList
        {
            SizeFlagsVertical = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 330),
            IconMode = ItemList.IconModeEnum.Top,
            MaxColumns = 0,
            FixedIconSize = new Vector2I(96, 96),
            SameColumnWidth = true,
            AllowReselect = true,
        };
        _hairList.ItemSelected += OnHairPicked;
        col.AddChild(_hairList);

        _hairInfo = new Label { AutowrapMode = TextServer.AutowrapMode.WordSmart };
        Style(_hairInfo, 12, new Color(0.6f, 0.63f, 0.66f));
        col.AddChild(_hairInfo);

        col.AddChild(Gap(8));
        col.AddChild(Head("POSITION (m)"));
        col.AddChild(Slider("px", "X", -0.25f, 0.25f, 0.001f));
        col.AddChild(Slider("py", "Y", -0.25f, 0.25f, 0.001f));
        col.AddChild(Slider("pz", "Z", -0.25f, 0.25f, 0.001f));

        col.AddChild(Head("ROTATION (°)"));
        col.AddChild(Slider("rx", "X", -180f, 180f, 0.5f));
        col.AddChild(Slider("ry", "Y", -180f, 180f, 0.5f));
        col.AddChild(Slider("rz", "Z", -180f, 180f, 0.5f));

        col.AddChild(Head("SCALE"));
        col.AddChild(Slider("sc", "×", HairFit.MinScale, HairFit.MaxScale, 0.005f));

        col.AddChild(Head("HAIR COLOR"));
        var pal = new HBoxContainer();
        pal.AddThemeConstantOverride("separation", 3);
        foreach ((string name, string hex) in Palette)
        {
            string n = name, h = hex;
            var sw = new Button { CustomMinimumSize = new Vector2(30, 26), TooltipText = $"{name} {hex}" };
            var st = new StyleBoxFlat { BgColor = Color.FromString(hex, Colors.Black) };
            sw.AddThemeStyleboxOverride("normal", st);
            sw.AddThemeStyleboxOverride("hover", st);
            sw.AddThemeStyleboxOverride("pressed", st);
            sw.Pressed += () => SetColor(h, n);
            pal.AddChild(sw);
        }
        col.AddChild(pal);

        _colorBtn = new ColorPickerButton { CustomMinimumSize = new Vector2(0, 30), EditAlpha = false };
        _colorBtn.ColorChanged += c => SetColor("#" + c.ToHtml(false), "직접 지정");
        col.AddChild(_colorBtn);

        col.AddChild(Gap(8));
        var reset = new Button { Text = "위치 초기화 (머리 크기에 맞춰 자동 정렬)" };
        Style(reset, 14, new Color(0.86f, 0.8f, 0.6f));
        reset.CustomMinimumSize = new Vector2(0, 36);
        reset.Pressed += () => { AutoFit(); SetStatus("머리 크기 기준으로 다시 맞췄다"); };
        col.AddChild(reset);

        return box;
    }

    private string _filter = "all";

    // ── 캐릭터 · 베이스 ─────────────────────────────────────────────────

    private void SelectCharacter(string id)
    {
        _charId = id;
        HairAssignment a = _store.Get(id);
        _body = a.Body;
        _hairId = a.HairId;
        _pos = a.Position; _rot = a.RotationDeg;
        _scale = a.Scale.X > 0.001f ? a.Scale.X : 1f;
        _colorHex = a.ColorHex; _colorName = a.ColorName;

        foreach ((string cid, Button b) in _charButtons) b.ButtonPressed = cid == id;
        LoadBase(_body);
        RebuildHairList();
        if (_hairId != "") SelectInList(_hairId);
        PushSliders();
        Apply();
        Refresh();
    }

    private void SetBody(string sex)
    {
        if (_body == sex) return;
        _body = sex;
        HairEntry cur = _cat.ById(_hairId);
        LoadBase(sex);
        if (cur != null && cur.Sex != sex && !_showOtherPack)
        {
            _hairId = "";
            SetStatus($"베이스를 {sex.ToUpper()} 로 바꿨다 — 고른 헤어는 다른 팩이라 선택을 비웠다. " +
                      "그 헤어를 그대로 쓰려면 '남녀 헤어 모두 보기' 를 켜라");
        }
        RebuildHairList();
        if (_hairId != "") SelectInList(_hairId);
        AutoFitIfNeeded();
        Apply();
        Refresh();
        Mark();
    }

    // ── 헤어 목록 ───────────────────────────────────────────────────────

    private void RebuildHairList()
    {
        _hairList.Clear();
        _listed.Clear();
        if (_cat.Entries.Count == 0) return;

        // 지금 베이스의 팩을 앞에 둔다 — 남성 베이스면 M- 가 먼저, 그 뒤로 F- 전부.
        IEnumerable<HairEntry> src = _showOtherPack
            ? _cat.Hairs(_body).Concat(_cat.Entries.Where(e => e.Kind == "hair" && e.Sex != _body))
            : _cat.Hairs(_body);

        if (_filter == "volume")
            src = src.Where(e => e.LengthClass == "long").OrderByDescending(e => e.AreaRatio);
        else if (_filter != "all")
            src = src.Where(e => e.LengthClass == _filter);

        foreach (HairEntry e in src)
        {
            _listed.Add(e);
            int i = _hairList.AddItem(e.Id, Thumb(e.Id));
            _hairList.SetItemTooltip(i, $"{e.Name}\n{e.MeshPath}\n삼각 {e.Tris} · 원본 {e.SourceNode}");
        }
        _hairList.Visible = true;
        _hairInfo.Text = $"{_listed.Count}개 — {(_showOtherPack ? "남녀 전체" : _body.ToUpper())} · 필터 {_filter}";
    }

    private readonly Dictionary<string, Texture2D> _thumbs = new();

    // 썸네일은 미리 구워 둔 PNG 다. 한 번 읽은 것은 들고 있는다(지시서 §4 "썸네일을
    // 캐싱하는 구조"). 에디터가 임포트해 둔 것이 있으면 그것을, 아직 없으면 파일에서
    // 직접 읽는다 — 구운 직후에도 목록이 비어 보이지 않게.
    private Texture2D Thumb(string id)
    {
        if (_thumbs.TryGetValue(id, out Texture2D t)) return t;
        string p = $"{ThumbDir}/{id}.png";
        if (ResourceLoader.Exists(p)) return _thumbs[id] = ResourceLoader.Load<Texture2D>(p);
        if (!FileAccess.FileExists(p)) return _thumbs[id] = null;
        Image img = Image.LoadFromFile(p);
        return _thumbs[id] = img == null ? null : ImageTexture.CreateFromImage(img);
    }

    private void SelectInList(string id)
    {
        int i = _listed.FindIndex(e => e.Id == id);
        if (i < 0) return;
        _hairList.Select(i);
        _hairList.EnsureCurrentIsVisible();
    }

    private void OnHairPicked(long index)
    {
        if (index < 0 || index >= _listed.Count) return;
        HairEntry e = _listed[(int)index];
        bool changed = e.Id != _hairId;
        _hairId = e.Id;
        if (changed) AutoFit(); else Apply();
        Refresh();
        Mark();
    }

    // ── 장착 · 자동 맞춤 ────────────────────────────────────────────────

    private void AutoFitIfNeeded()
    {
        if (_hairId == "") { Apply(); return; }
        if (_pos == Vector3.Zero && _scale == 1f) AutoFit();
        else Apply();
    }

    private void AutoFit()
    {
        HairEntry e = _cat.ById(_hairId);
        HairBaseEntry b = _cat.Base(_body);
        if (e == null || b == null || _skel == null) { Apply(); return; }
        int head = _skel.FindBone(b.HeadBone);
        Transform3D rest = head >= 0 ? _skel.GetBoneGlobalRest(head) : Transform3D.Identity;
        HairFit.Auto(e, b, rest, HairCatalog.PackYaw(e.SourceFile), out _pos, out _rot, out _scale);
        PushSliders();
        Apply();
        Refresh();
    }

    private void Apply()
    {
        if (_hairMesh == null) return;
        HairEntry e = _cat.ById(_hairId);
        if (e == null) { _hairMesh.Visible = false; return; }
        var mesh = ResourceLoader.Load<Mesh>(e.MeshPath);
        if (mesh == null) { _hairMesh.Visible = false; SetStatus($"{e.MeshPath} 를 불러오지 못했다", true); return; }
        _hairMesh.Mesh = mesh;
        _hairMesh.Transform = HairFit.Compose(_pos, _rot, Vector3.One * _scale);
        _hairMesh.Visible = true;
        if (_hairMat != null) _hairMat.AlbedoColor = Color.FromString(_colorHex, Colors.Black);
    }

    private void SetColor(string hex, string name)
    {
        _colorHex = hex; _colorName = name;
        if (_hairMat != null) _hairMat.AlbedoColor = Color.FromString(hex, Colors.Black);
        TintEyebrows();
        Refresh();
        Mark();
    }

    // ── 슬라이더 ────────────────────────────────────────────────────────

    private Control Slider(string key, string label, float min, float max, float step)
    {
        var row = new HBoxContainer();
        row.AddThemeConstantOverride("separation", 6);
        var l = new Label { Text = label, CustomMinimumSize = new Vector2(16, 0) };
        Style(l, 13, new Color(0.6f, 0.63f, 0.66f));
        row.AddChild(l);

        var s = new HSlider
        {
            MinValue = min, MaxValue = max, Step = step,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            CustomMinimumSize = new Vector2(0, 22),
        };
        s.ValueChanged += _ => OnSlider();
        _sliders[key] = s;
        row.AddChild(s);

        var v = new Label { CustomMinimumSize = new Vector2(62, 0), HorizontalAlignment = HorizontalAlignment.Right };
        Style(v, 12, new Color(0.78f, 0.8f, 0.82f));
        _sliderValues[key] = v;
        row.AddChild(v);
        return row;
    }

    private void OnSlider()
    {
        if (_muteSliders) return;
        _pos = new Vector3((float)_sliders["px"].Value, (float)_sliders["py"].Value, (float)_sliders["pz"].Value);
        _rot = new Vector3((float)_sliders["rx"].Value, (float)_sliders["ry"].Value, (float)_sliders["rz"].Value);
        _scale = (float)_sliders["sc"].Value;
        ShowSliderValues();
        Apply();
        Refresh();
        Mark();
    }

    private void PushSliders()
    {
        _muteSliders = true;
        _sliders["px"].Value = _pos.X; _sliders["py"].Value = _pos.Y; _sliders["pz"].Value = _pos.Z;
        _sliders["rx"].Value = _rot.X; _sliders["ry"].Value = _rot.Y; _sliders["rz"].Value = _rot.Z;
        _sliders["sc"].Value = _scale;
        _muteSliders = false;
        ShowSliderValues();
    }

    private void ShowSliderValues()
    {
        _sliderValues["px"].Text = $"{_pos.X:F3}";
        _sliderValues["py"].Text = $"{_pos.Y:F3}";
        _sliderValues["pz"].Text = $"{_pos.Z:F3}";
        _sliderValues["rx"].Text = $"{_rot.X:F1}";
        _sliderValues["ry"].Text = $"{_rot.Y:F1}";
        _sliderValues["rz"].Text = $"{_rot.Z:F1}";
        _sliderValues["sc"].Text = $"{_scale:F3}";
    }

    // ── 저장 ────────────────────────────────────────────────────────────

    private void Mark() => _dirty = true;

    private void SaveAll()
    {
        HairAssignment a = _store.Get(_charId);
        HairEntry e = _cat.ById(_hairId);
        a.Body = _body;
        a.HairId = _hairId;
        a.HairPath = e?.MeshPath ?? "";
        a.ColorHex = _colorHex;
        a.ColorName = _colorName;
        a.Position = _pos;
        a.RotationDeg = _rot;
        a.Scale = Vector3.One * _scale;

        Error err = _store.Save();
        _dirty = false;
        SetStatus(err == Error.Ok
            ? $"{HairAssignmentStore.Label(_charId)} 저장 — {_store.LastSavedTo} " +
              $"(고른 캐릭터 {HairAssignmentStore.Characters.Count(c => _store.HasChoice(c.Id))}/8명)"
            : $"저장 실패: {err}", err != Error.Ok);
        Refresh();
    }

    // ── 표시 ────────────────────────────────────────────────────────────

    private void Refresh()
    {
        HairEntry e = _cat.ById(_hairId);
        EmployeeDef def = HairAssignmentStore.LoadDef(_charId);
        string codename = HairAssignmentStore.Label(_charId);

        _summary.Text =
            $"CHARACTER: {codename}\n" +
            $"BODY: {_body.ToUpper()}" + (def != null && !string.IsNullOrEmpty(def.Gender) ? $"  ({def.Gender})" : "") + "\n" +
            $"HAIR: {(_hairId == "" ? "(아직 안 고름)" : _hairId)}\n" +
            $"COLOR: {_colorName} {_colorHex}\n" +
            $"POSITION: {_pos.X:F3}, {_pos.Y:F3}, {_pos.Z:F3}\n" +
            $"ROTATION: {_rot.X:F1}, {_rot.Y:F1}, {_rot.Z:F1}\n" +
            $"SCALE: {_scale:F3}\n" +
            (_dirty ? "\n● 저장 안 함" : "");

        foreach ((string cid, Button b) in _charButtons)
        {
            string label = HairAssignmentStore.Label(cid);
            b.Text = _store.HasChoice(cid) ? $"{label}  ✓" : label;
        }

        if (e != null)
            _hairInfo.Text = $"{e.Name} · 삼각 {e.Tris} · 크기 {e.Size.X * 100f:F0}×{e.Size.Y * 100f:F0}×" +
                             $"{e.Size.Z * 100f:F0}cm · 주름비 {e.AreaRatio:F2}\n{e.MeshPath}";
    }

    private void SetStatus(string s, bool bad = false)
    {
        if (_status == null) { GD.Print(s); return; }
        _status.Text = s;
        _status.AddThemeColorOverride("font_color",
            bad ? new Color(0.9f, 0.45f, 0.4f) : new Color(0.55f, 0.58f, 0.6f));
    }

    // ── 자동 검증용 ─────────────────────────────────────────────────────
    //
    // 스크린샷·테스트가 UI 를 클릭하지 않고도 **사람이 누르는 것과 같은 경로**로
    // 상태를 바꿀 수 있게 열어 둔다. 스튜디오 자체 동작은 이 메서드를 쓰지 않는다.

    public void PickForTest(string characterId, string body, string hairId)
    {
        SelectCharacter(characterId);
        if (body != _body) SetBody(body);
        int i = _listed.FindIndex(e => e.Id == hairId);
        if (i < 0) { _showOtherPack = true; RebuildHairList(); i = _listed.FindIndex(e => e.Id == hairId); }
        if (i < 0) { SetStatus($"{hairId} 가 목록에 없다", true); return; }
        _hairList.Select(i);
        OnHairPicked(i);
    }

    public void SaveForTest() => SaveAll();
    // 크기를 강제하고 그 크기에 맞는 자리까지 다시 잡는다 — 크기만 바꾸면 머리에서
    // 떠 버려서 비교가 되지 않는다.
    public void ScaleForTest(float s)
    {
        HairEntry e = _cat.ById(_hairId);
        HairBaseEntry b = _cat.Base(_body);
        _scale = s;
        if (e != null && b != null && _skel != null)
        {
            int head = _skel.FindBone(b.HeadBone);
            Transform3D rest = head >= 0 ? _skel.GetBoneGlobalRest(head) : Transform3D.Identity;
            _pos = HairFit.Place(e, b, rest, HairCatalog.PackYaw(e.SourceFile), s);
        }
        PushSliders(); Apply(); Refresh();
    }
    public void CloseUpForTest(bool on) { if (_closeUp != on) ToggleCloseUp(); }
    public void OrbitForTest(float yaw, float pitch) { _yaw = yaw; _pitch = pitch; }

    public string CurrentHairId => _hairId;
    public string CurrentBody => _body;
    public Vector3 CurrentPos => _pos;
    public Vector3 CurrentRot => _rot;
    public float CurrentScale => _scale;
    public int ListedCount => _listed.Count;
    public System.Collections.Generic.IEnumerable<string> ListedIds => _listed.Select(e => e.Id);
    public MeshInstance3D HairNode => _hairMesh;
    public Skeleton3D BaseSkeleton => _skel;
    public HairCatalog Catalog => _cat;
    public HairAssignmentStore Store => _store;

    // ── 카메라 조작 ─────────────────────────────────────────────────────

    private void OnViewportInput(InputEvent ev)
    {
        if (ev is InputEventMouseButton mb)
        {
            if (mb.ButtonIndex == MouseButton.Left) _dragging = mb.Pressed;
            else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelUp) _dist *= 0.88f;
            else if (mb.Pressed && mb.ButtonIndex == MouseButton.WheelDown) _dist *= 1.14f;
            _dist = Mathf.Clamp(_dist, 0.22f, 6f);
        }
        else if (ev is InputEventMouseMotion mm && _dragging)
        {
            _yaw -= mm.Relative.X * 0.4f;
            _pitch = Mathf.Clamp(_pitch - mm.Relative.Y * 0.3f, -80f, 80f);
        }
    }

    private void ToggleCloseUp()
    {
        _closeUp = !_closeUp;
        _closeUpBtn.ButtonPressed = _closeUp;
        _dist = _closeUp ? 0.55f : 2.6f;
        _pitch = _closeUp ? -2f : -8f;
        FrameCamera();
    }

    public override void _Process(double delta)
    {
        if (_camPivot == null) return;
        _camPivot.RotationDegrees = _camPivot.RotationDegrees.Lerp(
            new Vector3(_pitch, _yaw, 0f), (float)Mathf.Min(1.0, delta * 14.0));
        _cam.Position = _cam.Position.Lerp(new Vector3(0f, 0f, _dist), (float)Mathf.Min(1.0, delta * 10.0));
    }

    // ── 작은 UI 조각 ────────────────────────────────────────────────────

    private static Control Panel(int width)
    {
        var p = new PanelContainer { CustomMinimumSize = new Vector2(width, 0) };
        p.AddThemeStyleboxOverride("panel", new StyleBoxFlat
        {
            BgColor = new Color(0.10f, 0.105f, 0.12f),
            BorderColor = new Color(0.2f, 0.21f, 0.24f),
            BorderWidthTop = 1, BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
            ContentMarginLeft = 10, ContentMarginRight = 10,
            ContentMarginTop = 8, ContentMarginBottom = 8,
        });
        var col = new VBoxContainer();
        col.AddThemeConstantOverride("separation", 5);
        p.AddChild(col);
        return p;
    }

    private static Label Head(string text)
    {
        var l = new Label { Text = text };
        Style(l, 13, new Color(0.46f, 0.72f, 0.82f));
        return l;
    }

    private static Control Gap(int h, bool expand = false) => new Control
    {
        CustomMinimumSize = new Vector2(0, h),
        SizeFlagsVertical = expand ? Control.SizeFlags.ExpandFill : Control.SizeFlags.Fill,
    };

    private static Button Btn(string text, System.Action onPress)
    {
        var b = new Button { Text = text };
        Style(b, 13, new Color(0.8f, 0.82f, 0.84f));
        b.Pressed += onPress;
        return b;
    }

    private static Button ToggleBtn(string text, System.Action onPress)
    {
        var b = new Button { Text = text, ToggleMode = true };
        Style(b, 14, new Color(0.82f, 0.84f, 0.86f));
        b.Pressed += onPress;
        return b;
    }

    private static void Style(Control c, int size, Color col)
    {
        c.AddThemeFontSizeOverride("font_size", size);
        c.AddThemeColorOverride("font_color", col);
        if (c is not Button b || c is CheckBox or CheckButton) return;

        // 기본 버튼 스킨은 밝은 회색이라 밝은 글씨가 묻힌다. 어두운 판에 맞춰 다시 칠한다.
        b.AddThemeColorOverride("font_hover_color", col.Lightened(0.25f));
        b.AddThemeColorOverride("font_pressed_color", new Color(0.12f, 0.14f, 0.16f));
        b.AddThemeColorOverride("font_focus_color", col);
        b.AddThemeStyleboxOverride("normal", ButtonBox(new Color(0.16f, 0.17f, 0.20f)));
        b.AddThemeStyleboxOverride("hover", ButtonBox(new Color(0.22f, 0.24f, 0.28f)));
        b.AddThemeStyleboxOverride("pressed", ButtonBox(new Color(0.52f, 0.72f, 0.80f)));
        b.AddThemeStyleboxOverride("focus", ButtonBox(new Color(0.16f, 0.17f, 0.20f)));
        b.AddThemeStyleboxOverride("disabled", ButtonBox(new Color(0.12f, 0.13f, 0.15f)));
    }

    private static StyleBoxFlat ButtonBox(Color bg) => new()
    {
        BgColor = bg,
        BorderColor = new Color(0.30f, 0.32f, 0.36f),
        BorderWidthTop = 1, BorderWidthBottom = 1, BorderWidthLeft = 1, BorderWidthRight = 1,
        CornerRadiusTopLeft = 3, CornerRadiusTopRight = 3,
        CornerRadiusBottomLeft = 3, CornerRadiusBottomRight = 3,
        ContentMarginLeft = 8, ContentMarginRight = 8,
        ContentMarginTop = 5, ContentMarginBottom = 5,
    };
}
