using System;

namespace NekoGraph
{
    /// <summary>
    /// Query 调用上下文：只提供少量、稳定的读取态信息。
    /// </summary>
    public sealed class VFSQueryContext
    {
        public GraphAnalyser Analyser { get; set; }
        public string PackID { get; set; }
        public string VfsPath { get; set; }
        public int SubjectLevel { get; set; }
        public VFSNodeData Node { get; set; }
        public object FrontendContext { get; set; }
    }

    /// <summary>
    /// Query 返回的前端描述模型。
    /// 先保持轻量，后续再按真实需求扩字段。
    /// </summary>
    public sealed class VFSQueryResult
    {
        public string PresentationType { get; set; }
        public string Title { get; set; }
        public string Summary { get; set; }
        public object Payload { get; set; }
        public bool IsInteractive { get; set; }

        public static VFSQueryResult Create(
            string presentationType,
            string title = null,
            string summary = null,
            object payload = null,
            bool isInteractive = false)
        {
            return new VFSQueryResult
            {
                PresentationType = presentationType,
                Title = title,
                Summary = summary,
                Payload = payload,
                IsInteractive = isInteractive
            };
        }
    }
}
