using System.Collections.Generic;

namespace TmgBoard
{
    /// <summary>배치 가능한 지형 모듈 목록. Godot판 autoload/TerrainCatalog.gd +
    /// TerrainModuleDef.gd 포팅 — 텍스처는 여기서 들고 있지 않고, 쓰는 쪽에서
    /// FileName으로 Resources.Load("Terrains/" + FileName)해서 그때그때
    /// 불러온다(Unity의 Resources.Load는 내부적으로 캐시하므로 여러 번
    /// 불러도 괜찮다). texture/mask 이미지 둘 다 1mm = 1px로 제작됐다 — mask는
    /// Godot판에서도 실제로는 어디서도 안 쓰이던 값이라(정밀 모양 클릭 판정
    /// 등 미구현 기능용으로 남겨둔 것으로 보임) 여기서도 그대로 옮기지만
    /// 안 쓴다.</summary>
    public static class TerrainCatalog
    {
        public class Module
        {
            public string Id = "";
            public string DisplayName = "";
            public string FileName = ""; // Terrains/ 아래 파일명(확장자 없이)
            public int SizeValue;
        }

        public static readonly List<Module> Modules = new List<Module>
        {
            new Module { Id = "straight", DisplayName = "직선 지형", FileName = "직선 지형", SizeValue = 2 },
            new Module { Id = "l_shape", DisplayName = "L 지형", FileName = "L 지형", SizeValue = 2 },
            new Module { Id = "slope", DisplayName = "경사 지형", FileName = "경사 지형", SizeValue = 2 },
            new Module { Id = "hill", DisplayName = "언덕 지형", FileName = "언덕 지형", SizeValue = 2 },
            new Module { Id = "bush", DisplayName = "부쉬", FileName = "부쉬", SizeValue = 0 },
        };

        public static Module Get(string id)
        {
            foreach (var m in Modules)
            {
                if (m.Id == id)
                {
                    return m;
                }
            }
            return null;
        }
    }
}
