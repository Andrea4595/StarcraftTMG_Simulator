using System.IO;
using UnityEngine;

namespace TmgBoard
{
    /// <summary>실행 환경(에디터/실제 빌드)에서 똑같이 안전하게 동작하는 경로
    /// 계산을 한 곳에 모아둔다 — 여러 기능이 각자 따로 구현하다 저장소 구조가
    /// 바뀔 때마다 한쪽만 고쳐지고 나머지는 낡아버리는 일(예: RosterFileDialog가
    /// 리포 재구성 이후에도 예전 3단계 위 경로를 그대로 쓰고 있던 버그)을
    /// 막기 위함.</summary>
    public static class AppPaths
    {
        /// <summary>빌드된 실행 파일과 같은 위치. Application.dataPath는 빌드에서
        /// "&lt;실행파일 옆&gt;/&lt;제품명&gt;_Data"를 가리키므로, 그 한 단계 위
        /// 폴더가 실행 파일이 있는 자리다 — 에디터에서는 저장소 루트(Assets
        /// 폴더의 한 단계 위, 지금은 Unity 프로젝트 자체가 저장소 루트이므로
        /// 정확히 그 자리)가 나온다. 스크린샷 기능이 이미 검증해서 쓰던 계산을
        /// 그대로 공유한다.</summary>
        public static string ExeDirectory()
        {
            return Directory.GetParent(Application.dataPath).FullName;
        }
    }
}
