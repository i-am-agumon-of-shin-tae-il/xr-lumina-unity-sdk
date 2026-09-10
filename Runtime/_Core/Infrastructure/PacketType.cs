namespace XRLumina._Core.Infrastructure
{
    /// <summary>TCP 패킷 페이로드의 해석 방식.</summary>
    internal enum PacketType : byte
    {
        Json = 1,
        MirrorFrame = 2,
        InteractionTracking = 3,
        InteractionEvent = 4,
        InteractionMesh = 5,
        EyeTrackingGaze = 6,
        EyeTrackingCapture = 7,
        EyeTrackingCameraPose = 8,
    }
}
