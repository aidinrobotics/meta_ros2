#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEngine;

namespace MetaSdkFix
{
    /// <summary>
    /// Meta XR Core SDK v203.0.0 이 shipping 한 RuntimeOptimizerPlugin.cs 는 #define 지시문이
    /// using 문 뒤에 있어 CS1032(Cannot define/undefine preprocessor symbols after first token)로
    /// 프로젝트 전체 컴파일을 막는다. Library/PackageCache 는 gitignore + 재임포트마다 원본으로
    /// 덮여써서 클론/Library 삭제 때마다 재발한다.
    ///
    /// 이 스크립트는 "다른 어셈블리를 참조하지 않는 독립 Editor asmdef(MetaSdkFix.Editor)"에
    /// 들어 있어, Meta 어셈블리가 깨져도 별도로 컴파일·실행된다. [InitializeOnLoad] 로 에디터가
    /// 로드될 때마다 깨진 파일을 감지해 #define 블록을 using 위로 옮기고 재컴파일을 요청한다.
    ///
    /// 이미 정상 순서면 아무것도 하지 않으므로 재컴파일 루프가 생기지 않는다.
    /// </summary>
    [InitializeOnLoad]
    internal static class MetaSdkCompileFixer
    {
        static MetaSdkCompileFixer() => TryFix();

        static void TryFix()
        {
            try
            {
                var root = Path.Combine(Directory.GetCurrentDirectory(), "Library", "PackageCache");
                if (!Directory.Exists(root)) return;

                var files = Directory
                    .GetFiles(root, "RuntimeOptimizerPlugin.cs", SearchOption.AllDirectories)
                    .Where(p => p.Replace('\\', '/').Contains("com.meta.xr.sdk.core"));

                bool patched = false;
                foreach (var f in files) patched |= FixFile(f);

                if (patched)
                {
                    Debug.Log("[MetaSdkCompileFixer] RuntimeOptimizerPlugin.cs 패치 완료 " +
                              "(#define 을 using 위로 이동). 재컴파일 요청.");
                    CompilationPipeline.RequestScriptCompilation();
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[MetaSdkCompileFixer] 패치 실패: {e.Message}");
            }
        }

        /// <returns>파일을 수정했으면 true, 이미 정상이면 false</returns>
        static bool FixFile(string path)
        {
            var lines = File.ReadAllLines(path).ToList();

            int firstUsing = lines.FindIndex(l => l.TrimStart().StartsWith("using "));
            if (firstUsing < 0) return false;

            int ns = lines.FindIndex(l => l.TrimStart().StartsWith("namespace "));
            if (ns < 0) ns = lines.Count;

            // using 이후 ~ namespace 이전에 #define/#undef 가 있으면 순서 오류
            int badDefine = lines.FindIndex(firstUsing, ns - firstUsing, l =>
            {
                var t = l.TrimStart();
                return t.StartsWith("#define") || t.StartsWith("#undef");
            });
            if (badDefine < 0) return false; // 이미 정상

            // 옮길 블록: usings 뒤 첫 전처리 지시문(#if...) ~ namespace 이전 마지막 #endif
            int start = lines.FindIndex(firstUsing, ns - firstUsing, l => l.TrimStart().StartsWith("#"));
            int end = -1;
            for (int i = ns - 1; i >= start; i--)
            {
                if (lines[i].TrimStart().StartsWith("#endif")) { end = i; break; }
            }
            if (start < 0 || end < 0 || end < start) return false;

            var block = lines.GetRange(start, end - start + 1);
            lines.RemoveRange(start, end - start + 1);
            block.Add(""); // 블록과 using 사이 빈 줄
            lines.InsertRange(firstUsing, block); // using 블록 앞에 삽입

            File.WriteAllText(path, string.Join("\n", lines) + "\n");
            return true;
        }
    }
}
#endif
