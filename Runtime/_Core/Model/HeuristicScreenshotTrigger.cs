namespace XRLumina._Core.Model
{
    /// <summary>휴리스틱 스크린샷을 생성한 조건과 부가 정보를 표현한다.</summary>
    internal readonly struct HeuristicScreenshotTrigger
    {
        internal string Type { get; }
        internal float CapturedAt { get; }
        internal string ObjectName { get; }
        internal string ControllerInput { get; }

        internal HeuristicScreenshotTrigger(
            string type,
            float capturedAt,
            string objectName = "",
            string controllerInput = "")
        {
            Type = type;
            CapturedAt = capturedAt;
            ObjectName = objectName ?? "";
            ControllerInput = controllerInput ?? "";
        }
    }
}
