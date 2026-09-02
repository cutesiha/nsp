using System.Collections.Generic;
using Godot;

namespace NSP.Core;

// res:// 폴더를 런타임에 스캔하는 헬퍼.
//
// 내보낸(exported) 빌드에서는 .tres 리소스가 .tres.remap 으로, 임포트되는 파일은
// .import 로 나타나기 때문에 `fileName.EndsWith(".tres")` 같은 검사가 전부 실패한다.
// (에디터 / headless 실행에서는 접미사가 없다.) 이 헬퍼가 접미사를 벗겨 논리 경로만
// 돌려주므로 호출부는 환경에 상관없이 동일하게 동작한다.
public static class ResourceDir
{
    public static IEnumerable<string> ListFiles(string folder, string extension)
    {
        using var dir = DirAccess.Open(folder);
        if (dir == null)
        {
            GD.PushWarning($"ResourceDir: 폴더를 찾지 못했습니다: {folder}");
            yield break;
        }
        if (!folder.EndsWith("/")) folder += "/";

        var seen = new HashSet<string>();
        dir.ListDirBegin();
        for (string name = dir.GetNext(); name != ""; name = dir.GetNext())
        {
            if (dir.CurrentIsDir()) continue;

            string logical = name;
            if (logical.EndsWith(".remap") || logical.EndsWith(".import"))
                logical = logical.GetBaseName(); // "cat.tres.remap" -> "cat.tres"

            if (!logical.EndsWith(extension) || !seen.Add(logical)) continue;
            yield return folder + logical;
        }
        dir.ListDirEnd();
    }
}
