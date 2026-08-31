using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;
using CoreDawn.FPS;
using CoreDawn.Interaction;
using CoreDawn.Inventories;
using CoreDawn.Managers;
using CoreDawn.Sim;
using CoreDawn.Factory;

namespace CoreDawn.Data
{
    /// <summary>카테고리 표시명 — 빌드 메뉴·인스펙터가 공용.</summary>
    public static class BuildingCategoryNames
    {
        public static string Korean(BuildingCategory c) => c switch
        {
            BuildingCategory.Production => "생산",
            BuildingCategory.Logistics  => "물류",
            BuildingCategory.Defense    => "방어",
            _ => c.ToString(),
        };
    }
}
