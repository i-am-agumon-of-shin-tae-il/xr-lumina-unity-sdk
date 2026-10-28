using XRLumina.Protocol;

namespace XRLumina._Core.Model
{
    /// <summary>휴리스틱 스크린샷을 생성한 조건과 부가 정보를 표현한다.</summary>
    internal readonly struct HeuristicScreenshotTrigger
    {
        internal HeuristicTrigger Type { get; }
        internal ControllerHand Hand { get; }
        internal float CapturedAt { get; }
        internal string ObjectName { get; }
        internal ControllerInput ControllerInput { get; }

        internal HeuristicScreenshotTrigger(
            HeuristicTrigger type,
            float capturedAt,
            string objectName = "",
            ControllerInput controllerInput = ControllerInput.None,
            ControllerHand hand = ControllerHand.Unknown)
        {
            Type = type;
            CapturedAt = capturedAt;
            ObjectName = objectName ?? "";
            ControllerInput = controllerInput;
            Hand = hand;
        }
    }
}
