using System;
using System.Collections.Generic;
using System.Reflection;

namespace NekoGraph
{
    /// <summary>
    /// Query facade：对外暴露查询能力，真实注册结果由 VFSResourceRegistry 持有。
    /// </summary>
    public static class VFSQueryRegistry
    {
        public static void Initialize()
        {
            VFSResourceRegistry.Initialize();
        }

        public static void Register(string suffix, VFSQueryDelegate handler, Type dataType = null)
        {
            VFSResourceRegistry.RegisterQuery(suffix, handler, dataType);
        }

        public static bool TryGetHandler(string suffix, out VFSQueryDelegate handler)
        {
            if (VFSResourceRegistry.TryGetDriver(suffix, out var driver) && driver.Query != null)
            {
                handler = driver.Query;
                return true;
            }

            handler = null;
            return false;
        }

        public static Type GetDataType(string suffix)
        {
            return VFSResourceRegistry.GetDataType(suffix);
        }

        public static VFSContentKind? GetContentKindFromDataType(Type dataType)
        {
            if (dataType == null) return null;
            var attr = dataType.GetCustomAttribute<VFSContentKindAttribute>();
            return attr?.Kind;
        }

        public static List<string> GetAllSuffixes()
        {
            return VFSResourceRegistry.GetQueryableSuffixes();
        }
    }
}
