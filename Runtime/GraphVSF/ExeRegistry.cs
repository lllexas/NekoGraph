using System;
using System.Collections.Generic;
using System.Reflection;

namespace NekoGraph
{
    /// <summary>
    /// Execute facade：对外保留旧名字 ExeRegistry。
    /// 真实注册结果已收敛到 VFSResourceRegistry 的 VFSResourceDriver。
    /// </summary>
    public static class ExeRegistry
    {
        public static void Initialize()
        {
            VFSResourceRegistry.Initialize();
        }

        /// <summary>
        /// 手动注册执行能力（程序化注册，优先级高于属性扫描）。
        /// </summary>
        public static void Register(string suffix, VFSExecuteDelegate handler, Type dataType = null)
        {
            VFSResourceRegistry.RegisterExecute(suffix, handler, dataType);
        }

        /// <summary>
        /// 尝试获取后缀对应的执行处理器。
        /// </summary>
        public static bool TryGetHandler(string suffix, out VFSExecuteDelegate handler)
        {
            if (VFSResourceRegistry.TryGetDriver(suffix, out var driver) && driver.Execute != null)
            {
                handler = driver.Execute;
                return true;
            }

            handler = null;
            return false;
        }

        /// <summary>
        /// 获取后缀对应的内容数据类型。
        /// 若未注册或未声明 DataType 则返回 null。
        /// </summary>
        public static Type GetDataType(string suffix)
        {
            return VFSResourceRegistry.GetDataType(suffix);
        }

        /// <summary>
        /// 获取 DataType 对应的内容格式（若类型上标注了 [VFSContentKind]）。
        /// </summary>
        public static VFSContentKind? GetContentKindFromDataType(Type dataType)
        {
            if (dataType == null) return null;
            var attr = dataType.GetCustomAttribute<VFSContentKindAttribute>();
            return attr?.Kind;
        }

        /// <summary>
        /// 获取所有已注册的后缀名。
        /// </summary>
        public static List<string> GetAllSuffixes()
        {
            return VFSResourceRegistry.GetAllSuffixes();
        }
    }
}
