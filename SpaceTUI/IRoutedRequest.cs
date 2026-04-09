namespace SpaceTUI
{
    /// <summary>
    /// 路由请求接口 - 用于 SpaceUIAnimator.MatchUIID 识别
    /// </summary>
    public interface IRoutedRequest
    {
        string uiid { get; }
    }
}