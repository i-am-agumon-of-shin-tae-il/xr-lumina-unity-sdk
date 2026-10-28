# XRLumina Unity SDK 연동 레퍼런스

이 문서는 Unity 애플리케이션을 XRLumina 데스크톱 앱과 연결하는 데 필요한 설정과 API를 설명합니다.

통신은 FlatBuffers를 사용합니다. 연결·세션·측정 메시지 형식은 [통신 규약](#공개-통신-규약-v1)에서 확인할 수 있습니다.

연동 후 사용할 수 있는 기능은 다음과 같습니다.

- 데스크톱 앱의 장치 연결 및 세션 선택
- Unity 화면 미러링
- 테스트 시작·종료 상태 동기화
- 사용자 위치·회전 추적
- 인터랙션 이벤트 기록
- 시선 데이터 기록
- 테스트 종료 시 측정 데이터 전송

## 1. 연동 순서

1. Unity Package Manager에서 XRLumina SDK 패키지를 프로젝트에 추가합니다.
2. [`XRLuminaClient.prefab`](./Runtime/Prefabs/XRLuminaClient.prefab)을 최초 실행 씬에 배치합니다.
3. XRLuminaClientController의 Head에 HMD Camera Transform, Body에 플레이어 루트를 연결합니다.
4. XRLumina 데스크톱 앱을 실행합니다.
5. Unity 애플리케이션을 실행합니다.
6. 데스크톱 앱에서 연결된 장치를 선택합니다.
7. 데스크톱 앱 또는 Unity에서 테스트를 시작합니다.
8. 테스트 종료 명령을 받은 뒤 Unity 콘텐츠를 종료 처리합니다.

![XRLuminaClient 프리팹을 씬 Hierarchy에 배치한 화면](./README/Screenshot%202026-09-10%20at%2020.03.35.png)

`XRLuminaClient` 프리팹은 씬이 변경돼도 유지됩니다. 씬마다 중복 배치하지 마십시오.

## 2. Inspector 필수 설정

[`XRLuminaClient.prefab`](./Runtime/Prefabs/XRLuminaClient.prefab)에는 연동에 필요한 컴포넌트가 포함돼 있습니다.

### [XRLuminaClientController](./Runtime/Client/XRLuminaClientController.cs)

데스크톱 앱 연결, 세션, 테스트 시작·종료 요청과 명령 수신을 담당합니다. 별도의 연결 메서드는 호출하지 않습니다.

| 항목 | 설정 |
| --- | --- |
| Head | HMD Camera Transform |
| Body | Player Root 또는 XR Origin Transform |

![XRLuminaClientController의 Head와 Body 연결 화면](./README/Screenshot%202026-09-28%20at%2014.10.59.png)

일반적인 XR Rig 연결은 다음과 같습니다.

```text
XR Origin 또는 Player Root  → Body
└── HMD Camera              → Head
```

- `Head.position`: 사용자 위치와 히트맵에 사용
- `Head.rotation`: 머리 회전 분석에 사용
- `Body.position`: 이동 거리와 이동 속도 분석에 사용

`Head`와 `Body`를 모두 지정하십시오. 누락된 참조를 HMD나 다른 카메라로 대체하지 않습니다. 아이트래킹 구간은 `Body.position`으로 나누며 시선과 캡처는 HMD를 사용합니다.

### [MirroringController](./Runtime/Features/Controllers/MirroringController.cs)

| 항목 | 설정 |
| --- | --- |
| Stream Enabled | 화면 미러링을 사용하면 활성화 |
| Width / Height | 전송할 화면 크기 |
| Frames Per Second | 화면 전송 FPS |
| Jpeg Quality | 전송 이미지 품질 |

![MirroringController Inspector 설정 화면](./README/Screenshot%202026-09-28%20at%2014.12.22.png)

미러링·아이트래킹·휴리스틱 캡처는 모두 `XRLuminaClientController.Head`에 붙은 Camera를 사용합니다.

### [InteractionController](./Runtime/Features/Controllers/InteractionController.cs)

| 항목 | 설정 |
| --- | --- |
| Frames Per Second | 위치·회전 기록 FPS |
| Joystick Deadzone | 조이스틱 제스처 감지 임계값 |
| Heuristic Capture Camera | 조건 충족 시 PNG로 캡처할 HMD Camera. 비어 있으면 `Camera.main` 사용 |
| Stationary Duration Seconds / Movement Threshold | 멈춤 판정 시간과 XZ 누적 이동거리 기준 |
| Yaw Window Seconds / Required Reversals / Minimum Swing Degrees | 머리 방향 반전 판정 기준 |
| Interaction Window Seconds / Required Count | 동일 대상·입력 반복 판정 기준 |
| Interaction Layer Mask / Max Distance | Controller 자동 감지 시 정면 Raycast 범위 |
| Heuristic Capture Width / Height | 캡처 PNG 해상도 |

### [EyeTrackingController](./Runtime/Features/Controllers/EyeTrackingController.cs)

| 항목 | 설정 |
| --- | --- |
| Gaze Layer Mask | 시선 측정 대상 레이어 |
| Max Distance | 시선 측정 최대 거리 |
| Frames Per Second | 시선 기록 FPS |

시선 측정 대상 오브젝트에는 Collider가 있어야 하며 해당 레이어가 `Gaze Layer Mask`에 포함돼야 합니다.

파노라마는 정지 구간 시작 시 바라본 방향이 중앙에 오도록 가로 픽셀을 순환 이동합니다. 시선 충돌점도 같은 촬영 위치와 이동 각도를 기준으로 변환하여 전송합니다. 따라서 아이트래킹의 `points`는 캡처 기준 좌표이며, `cameraPose`는 위치 `(0, 0, 0)`, 회전 `(0, 0, 0, 1)`입니다. 인터랙션의 월드 좌표에는 적용되지 않습니다.

## 3. 데스크톱 앱 연결과 세션

Unity 애플리케이션이 실행되면 SDK가 응답하는 데스크톱 앱을 모두 탐색하여 HMD 목록에 표시됩니다. 목록 표시용 통신만으로 특정 데스크톱에 고정되지 않습니다.

데스크톱에서 HMD를 선택하면 해당 데스크톱에 고정되고 세션이 전달됩니다. 다른 데스크톱의 목록에서는 숨겨집니다. 다른 HMD를 선택하면 기존 HMD의 해제를 확인한 뒤 새 HMD를 선택하며, 해제된 HMD는 다른 데스크톱의 목록에 다시 표시됩니다. 선택 연결이 종료되어도 고정은 해제됩니다.

### 세션 준비 확인

```csharp
using UnityEngine;
using XRLumina.Client;

public sealed class XRLuminaSessionExample : MonoBehaviour
{
    /// <summary>SDK 세션 준비 이벤트를 구독한다.</summary>
    private void Start()
    {
        XRLuminaClientController.Instance.SessionReady += HandleSessionReady;
    }

    /// <summary>데스크톱 앱에서 전달된 세션을 확인한다.</summary>
    private void HandleSessionReady()
    {
        var client = XRLuminaClientController.Instance;
        Debug.Log($"Session UUID: {client.Session.uuid}, Seq: {client.Session.seq}");
    }

    /// <summary>SDK 세션 이벤트 구독을 해제한다.</summary>
    private void OnDestroy()
    {
        if (XRLuminaClientController.Instance != null)
        {
            XRLuminaClientController.Instance.SessionReady -= HandleSessionReady;
        }
    }
}
```

| API | 설명 |
| --- | --- |
| `Session` | 현재 세션 정보. 준비 전에는 `null`일 수 있음 |
| `IsAuthenticated` | 세션 인증 완료 여부 |
| `IsMeasuring` | SDK가 관리하는 현재 측정 여부 |
| `SessionReady` | 새 세션이 준비됐을 때 발생하는 이벤트 |

## 4. 테스트 시작과 종료

테스트는 데스크톱 앱에서 제어하거나 Unity에서 요청할 수 있습니다.

### Unity에서 시작·종료 요청

```csharp
using XRLumina.Client;

bool startSent = XRLuminaClientController.Instance?.RequestStart() ?? false;
bool finishSent = XRLuminaClientController.Instance?.RequestFinish() ?? false;
```

`RequestStart()`와 `RequestFinish()`의 반환값은 요청 패킷 전송 여부입니다. 실제 테스트 상태는 데스크톱 앱이 다시 보내는 `start`, `finish` 명령을 기준으로 처리해야 합니다.

### 데스크톱 앱 명령 수신

```csharp
using UnityEngine;
using XRLumina.Client;

public sealed class XRLuminaTestLifecycle : MonoBehaviour
{
    /// <summary>데스크톱 앱의 테스트 명령을 구독한다.</summary>
    private void Start()
    {
        XRLuminaClientController.Instance.OnCommand += HandleCommand;
    }

    /// <summary>수신한 테스트 명령을 Unity 콘텐츠 상태에 반영한다.</summary>
    private void HandleCommand(XRLumina.Protocol.TestAction action)
    {
        if (action == XRLumina.Protocol.TestAction.Start)
        {
            StartContent();
        }
        else if (action == XRLumina.Protocol.TestAction.Finish)
        {
            FinishContent();
        }
    }

    /// <summary>콘텐츠 테스트를 시작한다.</summary>
    private void StartContent()
    {
        Debug.Log("테스트 시작");
    }

    /// <summary>콘텐츠 테스트를 종료한다.</summary>
    private void FinishContent()
    {
        Debug.Log("테스트 종료");
    }

    /// <summary>SDK 명령 이벤트 구독을 해제한다.</summary>
    private void OnDestroy()
    {
        if (XRLuminaClientController.Instance != null)
        {
            XRLuminaClientController.Instance.OnCommand -= HandleCommand;
        }
    }
}
```

Unity에서 시작 버튼을 눌렀더라도 `RequestStart()` 호출 즉시 콘텐츠를 시작하지 말고 `start` 명령을 받은 시점에 시작하는 것을 권장합니다. 종료도 동일하게 `finish` 명령을 기준으로 처리합니다.

![데스크톱 앱에서 장치를 선택하고 테스트를 시작할 수 있는 화면](./README/Screenshot%202026-09-10%20at%2020.07.37.png)

## 5. 측정 데이터 동작 시점

| 상태 | 화면 미러링 | 위치·인터랙션 | 시선 측정 | 휴리스틱 캡처 |
| --- | --- | --- | --- | --- |
| 장치 연결 전 | 중지 | 중지 | 중지 | 중지 |
| 세션 선택 | 시작 | 대기 | 대기 | 대기 |
| `start` 수신 | 유지 | 기록 시작 | 기록 시작 | 조건 추적 시작 |
| `finish` 수신 | 중지 | 기록 종료 및 전송 | 기록 종료 및 전송 | PNG 전송 및 저장 요청 |

테스트 데이터는 데스크톱 앱이 전달한 세션에 연결됩니다. 세션 선택과 테스트 시작이 완료되기 전에 발생한 이벤트는 정상적인 테스트 데이터로 처리되지 않을 수 있습니다.

## 6. 인터랙션 이벤트 연동

### 지원 이벤트

| enum | 의미 |
| --- | --- |
| `Task` | 과업 시작·완료 등 과업 상태 변화 |
| `Npc` | NPC 대화 또는 선택 |
| `Gesture` | 제스처 또는 조이스틱 동작 |
| `Controller` | 버튼, 트리거 또는 그립 입력 |
| `Object` | 일반 오브젝트 선택 또는 조작 |

### 이벤트 기록

게임의 실제 인터랙션이 성공한 시점에 `RecordInteractionEvent()`를 호출합니다.

```csharp
using UnityEngine;
using XRLumina._Core.Model;
using XRLumina.Features;

public sealed class XRLuminaInteractionExample : MonoBehaviour
{
    /// <summary>오브젝트 인터랙션 성공 이벤트를 기록한다.</summary>
    public void RecordObjectInteraction()
    {
        InteractionController.Instance?.RecordInteractionEvent(
            XRLumina.Protocol.EventKind.Object
        );
    }
}
```

Unity UI Button, XR Interaction Toolkit의 Select 이벤트, 자체 Raycast 또는 충돌 처리에 연결할 수 있습니다.

이벤트 위치에는 호출 시점의 `Head.position`이 기록됩니다. 선택한 오브젝트 위치나 Raycast 충돌점이 자동으로 기록되지는 않습니다.

### VR 입력 자동 기록

테스트 측정 중 SDK는 좌·우 컨트롤러 입력을 자동 감지합니다.

| 입력 | 기록 이벤트 |
| --- | --- |
| Primary / Secondary Button | `Controller` |
| Trigger / Grip Button | `Controller` |
| 조이스틱이 Deadzone 밖으로 이동 | `Gesture` |

자동 감지되는 입력에서 `RecordInteractionEvent()`도 직접 호출하면 동일 조작이 중복 기록될 수 있습니다.

### 휴리스틱 화면 캡처

`InteractionController`는 테스트 측정 중 HMD 위치·Yaw와 Controller 입력을 함께 추적하고 다음 조건에서 HMD 카메라 화면을 PNG로 캡처합니다.

| 조건 | 판정 기준 |
| --- | --- |
| 사용자 멈춤 | HMD의 Y축을 제외한 XZ 누적 이동거리가 3초 동안 0.15 Unity Unit 이하 |
| 머리 움직임 변화 | 5초 이내 좌우 Yaw 방향이 3회 이상 반전되고 각 방향의 회전량이 30도 이상 |
| 반복 인터랙션 | 동일한 GameObject와 동일한 Controller 입력이 5초 이내 3회 이상 발생 |

캡처 이미지는 `HeuristicScreenshot` FlatBuffers 메시지로 데스크톱에 전송됩니다. 테스트 종료 시 `FlushGroup.Heuristics`의 Flush가 전송되며, 데스크톱 앱의 업로드·저장 처리이 완료돼야 종료 흐름이 계속됩니다.

기본 캡처 해상도는 1280×720이고 `InteractionController` Inspector에서 해상도, 판정 기준, Raycast LayerMask와 최대 거리를 조정할 수 있습니다. `Heuristic Capture Camera`가 비어 있으면 `Camera.main`을 사용합니다.

Controller의 Primary, Secondary, Trigger, Grip 입력은 HMD 카메라 정면 Raycast 대상과 함께 자동으로 반복 여부를 판정합니다. 게임 로직에서 실제 상호작용 성공 대상을 알고 있는 경우에는 다음 오버로드를 사용합니다.

```csharp
InteractionController.Instance?.RecordInteractionEvent(
    XRLumina.Protocol.EventKind.Object,
    targetGameObject,
    XRLumina.Protocol.ControllerInput.Trigger,
    XRLumina.Protocol.ControllerHand.Right
);
```

`targetGameObject`와 `ControllerInput`·`ControllerHand` enum 값이 모두 같은 호출만 하나의 반복 인터랙션으로 계산합니다. 같은 입력을 자동 감지와 직접 호출 양쪽에서 기록하면 중복 집계될 수 있으므로 게임의 실제 성공 시점을 직접 연결한 경우 자동 Raycast 대상과 입력 이름이 겹치지 않게 구성합니다.

## 7. 런타임 XR Rig 연결

XR Rig가 런타임에 생성되거나 씬 전환으로 변경되는 경우 `Head`와 `Body`를 다시 연결합니다.

```csharp
using UnityEngine;
using XRLumina.Client;

public sealed class XRLuminaRigBinder : MonoBehaviour
{
    [SerializeField] private Transform playerRoot;
    [SerializeField] private Camera hmdCamera;

    /// <summary>현재 XR Rig를 SDK 인터랙션 추적 대상으로 연결한다.</summary>
    private void Start()
    {
        XRLuminaClientController.Instance.SetHead(hmdCamera.transform);
        XRLuminaClientController.Instance.SetBody(playerRoot);
    }
}
```

| API | 설명 |
| --- | --- |
| `SetHead(Transform)` | 머리 추적 Transform 교체 |
| `SetBody(Transform)` | Player Root 추적 Transform 교체 |

## 8. 샘플 씬

샘플 씬은 [`XRLuminaSampleScene.unity`](./Samples~/BasicSample/XRLuminaSampleScene.unity)에서 확인할 수 있습니다.

샘플 씬에는 데스크톱·모바일·VR 이동이 가능한 플레이어와 인터랙션 종류별 타깃 5개가 배치돼 있습니다.

![XRLuminaSampleScene의 Hierarchy와 인터랙션 타깃이 표시된 Game View](./README/Screenshot%202026-09-10%20at%2020.08.52.png)

### 샘플 씬 전용 데스크톱 조작

| 입력 | 동작 |
| --- | --- |
| `WASD` 또는 방향키 | 이동 |
| `Left Shift` | 빠른 이동 |
| 마우스 이동 | 시점 회전 |
| `E` 또는 마우스 왼쪽 버튼 | 화면 중앙의 타깃 실행 |
| `Esc` | 마우스 잠금 해제 |

### 샘플 씬 전용 모바일 조작

테스트 씬의 `MobileControls` Canvas에 좌하단 이동 패드와 우하단 시점 패드가 배치되어 있습니다. Editor와 PC·모바일 실행에서 표시되며, 기기의 VR 실행에서는 숨깁니다.

| 입력 | 동작 |
| --- | --- |
| 왼쪽 조이패드 | 카메라 방향 기준 앞뒤·좌우 이동 |
| 오른쪽 조이패드 | 좌우 방향 전환 및 상하 시점 회전 |
| 두 패드 동시 터치 | 이동과 시점 회전 동시 조작 |
| 중앙 과녁 버튼 클릭·탭 | 화면 중앙의 타깃 실행(E키·마우스 클릭과 동일) |

손을 떼거나 앱이 포커스를 잃으면 입력을 초기화합니다. 패드는 화면 안전 영역에 맞춰 배치되며 화면 회전 시 위치를 갱신합니다. PC·VR 조작은 기존 방식을 사용합니다.

`XRLuminaSamplePlayerController`의 `Mobile Look Speed`로 회전 속도를 조절할 수 있습니다. Editor 테스트 씬에서는 별도 미리보기 설정 없이 패드가 바로 표시되며, HMD 시뮬레이터 감지로 숨기지 않습니다. PC에서는 기본 마우스 이동으로 시점을 회전합니다. `Esc`로 커서를 풀면 조이패드를 드래그하거나 중앙 버튼을 클릭할 수 있고, UI 밖의 화면을 클릭하면 마우스 시점 조작으로 돌아갑니다. 커서를 푼 상태에서도 마우스 오른쪽 버튼 드래그로 시점을 회전할 수 있습니다. 이 기능은 샘플 전용이며 SDK 런타임에는 UI를 추가하지 않습니다.

### 샘플 씬 전용 VR 조작

| 입력 | 동작 |
| --- | --- |
| HMD | Sample Camera 위치와 회전 갱신 |
| 왼손 조이스틱 | Sample Player 이동 |
| 오른손 조이스틱 X축 | Sample Player 좌우 회전 |
| 오른손 트리거 | HMD Camera 정면의 타깃 실행 |

위 조작은 [`XRLuminaSamplePlayerController`](./Samples~/BasicSample/XRLuminaSamplePlayerController.cs)에 구현된 샘플 전용 기능입니다. 타깃 이벤트 연결은 [`XRLuminaSampleInteractionTarget`](./Samples~/BasicSample/XRLuminaSampleInteractionTarget.cs)에서 확인할 수 있습니다. XRLumina SDK가 프로젝트의 이동 또는 타깃 선택 방식을 강제하지 않습니다.

샘플 타깃은 HMD Camera 정면에서 최대 `6m`까지 Raycast해 선택합니다. 컨트롤러가 가리키는 방향을 사용하는 방식이 아닙니다.

## 9. 연동 확인 목록

- `XRLuminaClient`가 씬에 하나만 배치돼 있는가
- Client의 `Head`에 실제 HMD Camera 컴포넌트가 있는가
- Client의 `Head`가 HMD Camera인가
- Client의 `Body`가 Player Root 또는 XR Origin인가
- 시선 대상에 Collider가 있는가
- 시선 대상 Layer가 `Gaze Layer Mask`에 포함돼 있는가
- 데스크톱 앱에서 Unity 장치가 연결 상태로 표시되는가
- 데스크톱 앱에서 올바른 테스트 세션을 선택했는가
- Unity 콘텐츠 상태를 `OnCommand`의 `TestAction.Start`, `TestAction.Finish`에 맞춰 처리하는가
- 테스트 시작 후 인터랙션 이벤트를 기록하는가
- 휴리스틱 캡처 Camera와 반복 인터랙션 대상 Collider·Layer가 올바른가

## 10. 문제 해결

### 데스크톱 앱에 장치가 나타나지 않음

- 데스크톱 앱이 실행 중인지 확인합니다.
- Unity 장치와 데스크톱 앱이 같은 네트워크인지 확인합니다.
- 방화벽에서 UDP `9998`, TCP `9999` 통신을 확인합니다.
- Unity Console에서 `[XRLumina]` 로그를 확인합니다.

### 테스트 시작·종료 상태가 맞지 않음

- `RequestStart()` 또는 `RequestFinish()` 호출만으로 Unity 상태를 변경하지 않았는지 확인합니다.
- `OnCommand`에서 `TestAction.Start`, `TestAction.Finish`를 수신하는지 확인합니다.
- `OnCommand` 이벤트 구독이 해제되지 않았는지 확인합니다.

### 미러링 화면이 나타나지 않음

- `Stream Enabled`가 활성화돼 있는지 확인합니다.
- `XRLuminaClientController.Head`의 Camera가 활성 상태인지 확인합니다.
- 데스크톱 앱에서 장치와 세션이 선택됐는지 확인합니다.

### Head와 Body 값이 동일함

`Head`에는 HMD Camera Transform, `Body`에는 Player Root를 각각 지정합니다. 두 참조에 같은 Transform을 지정하지 않습니다.

### 인터랙션 이벤트가 기록되지 않음

- 테스트가 시작 상태인지 확인합니다.
- `InteractionController`가 활성 상태인지 확인합니다.
- `InteractionController.Instance`가 준비된 이후 호출하는지 확인합니다.
- 이벤트 호출 코드에 올바른 enum을 전달했는지 확인합니다.

### 인터랙션이 중복 기록됨

VR 버튼과 조이스틱은 SDK가 자동 감지합니다. 같은 입력 처리에서 `RecordInteractionEvent()`도 호출하는지 확인합니다.

### 시선 데이터가 기록되지 않음

- `XRLuminaClientController.Head`와 `Body`가 올바르게 연결됐는지 확인합니다.
- 시선 대상의 Collider와 Layer를 확인합니다.
- 테스트가 시작 상태인지 확인합니다.

## 11. USB 로컬 연결

업데이트한 SDK와 XRLumina 데스크톱 앱을 함께 사용합니다. `XRLuminaClientController`의 `Usb Enabled`는 기본 활성화이며 `Usb Port` 기본값은 `10000`입니다. USB 포트는 루프백에만 열립니다. 기존 네트워크 연결과 동일한 size-prefixed FlatBuffers Envelope와 장치 선택·세션 처리를 사용합니다.

1. USB 데이터 케이블로 기기와 PC를 연결합니다.
2. 기기에서 SDK가 포함된 Unity 앱을 실행합니다.
3. 데스크톱 앱에서 테스트 인증 후 **USB 장치 찾기 → 장치 선택 → 선택한 USB 장치 연결**을 누릅니다.
4. 기기의 USB 접근 또는 컴퓨터 신뢰 요청을 승인합니다.
5. 기존 HMD 목록에 표시된 장치를 선택해 측정을 시작합니다.

### Android

패키지의 `XRLuminaUsb.androidlib`가 AOA accessory와 로컬 TCP 사이의 바이트를 중계합니다. ADB 및 USB 디버깅은 필요하지 않습니다. 장치가 `android.hardware.usb.accessory`를 지원해야 하며, USB 접근은 사용자가 승인해야 합니다. 플러그인은 `NineFive / XRLumina` accessory만 연결합니다. 앱이 실행된 상태에서 연결을 처리하며 잠금·백그라운드에서의 지속 실행은 포함하지 않습니다.

Android 플러그인은 API 35로 컴파일하며 JNI 의존성과 ProGuard 보존 규칙을 포함합니다. 데스크톱에는 장치에 접근할 수 있는 USB 권한/드라이버가 필요합니다. Windows에서는 AOA 모드 전환 전후의 장치 모두에 적절한 드라이버가 있어야 합니다. Linux에서는 해당 장치의 udev 권한을 설정합니다.

### iOS / iPadOS

Unity C# `TcpListener`가 로컬 포트 `10000`에서 대기하고 데스크톱 앱이 usbmux로 이 포트에 접속합니다. Unity iOS 쪽에 PeerTalk 라이브러리를 추가할 필요는 없습니다. iPhone/iPad가 해당 컴퓨터를 신뢰해야 하며 Unity 앱이 실행 중이어야 합니다. Windows 데스크톱에는 Apple Mobile Device Support, Linux에는 usbmuxd가 필요합니다. macOS는 시스템 서비스를 사용합니다.

연결 종료 시 기존 측정 상태와 선택을 해제하며, 앱을 실행한 채 케이블을 다시 연결하면 데스크톱이 선택했던 USB 장치에 재접속합니다. USB 연결 경로 자체는 Wi-Fi·인터넷을 사용하지 않습니다. 데스크톱 인증과 서버 업로드는 별도의 네트워크 기능입니다.

## 엔진 루프에서 사용하는 공통 클라이언트

공통 API는 `XRLumina.Client.XRLuminaClient`입니다. 엔진이 생성·시작·Tick·종료를 호출합니다. 코어는 Unity API를 참조하지 않으며, Unity 컴포넌트는 같은 API를 호출하는 어댑터입니다.

| 어셈블리 | 내용 | 엔진 의존성 |
|---|---|---|
| `Google.FlatBuffers` | FlatBuffers 런타임 | 없음 |
| `XRLumina.Protocol` | `Runtime/Protocol/`의 생성 코드 | 없음 |
| `XRLumina.Core` | `Runtime/_Core/`의 연결·프레이밍·선택·세션·요청 응답 | 없음, `noEngineReferences=true` |
| `XRLumina.Runtime` | `Client`·`Features/Controllers`의 Unity 컴포넌트, `Features/Services`의 데이터 수집·캡처·코루틴, `Features/Models`의 Unity 전용 모델 | Unity |

자체 C# 엔진은 위의 공통 DLL 세 개를 참조할 수 있습니다. 소스로 통합한다면 `Runtime/_Core/**/*.cs`, `Runtime/Protocol/**/*.cs`, `Runtime/ThirdParty/FlatBuffers/*.cs`를 포함하고 Unity 어댑터인 `Runtime/Client/`, `Runtime/Features/`는 포함하지 않습니다. Unity의 `.asmdef`와 `.meta`는 일반 C# 호스트에 필요하지 않습니다. C# 9 컴파일러와 .NET Standard 2.1 API 수준, TCP/UDP·백그라운드 스레드를 사용할 수 있는 환경을 기준으로 합니다. 다른 언어의 엔진은 같은 wire 규약과 API 역할을 구현하거나 별도 언어 바인딩을 제공해야 합니다.

### 생명주기 API

| API | 호출 시점과 역할 |
|---|---|
| `new XRLuminaClient(options)` | 엔진 스레드에서 생성. 클라이언트 ID·이름·모델과 연결 설정을 전달하며 설정은 생성 시 복사 |
| `Start()` | 연결·탐색 및 선택한 USB 루프백 리스너 시작. 중복 호출은 무시 |
| `Tick(double deltaSeconds)` | 엔진의 프레임/자체 루프에서 호출. 수신 결과, 선택·세션·측정 상태, Flush 제한 시간과 콜백 진행 |
| `RequestStart()` / `RequestFinish()` | 인증된 세션의 데스크톱에 측정 시작·종료 요청. 실제 상태는 수신 Command로 확정 |
| `Send(MessageUnion message)` | 엔진이 규약에 맞춰 만든 측정 메시지를 전송. 선택된 세션이 없으면 false |
| `SendMirrorFrame(byte[] jpeg)` | 엔진이 인코딩한 JPEG를 미러링 메시지로 전송 |
| `Flush(group, frameIntervalSeconds, onResult, timeoutSeconds)` | 현재 Session.seq로 그룹 업로드 요청. 요청 ID 반환, 결과는 이후 Tick에서 한 번만 전달 |
| `Stop()` | 연결·선택·세션 정리. 다시 Start 가능. 대기 Flush는 다음 Tick에서 `client stopped`로 완료 |
| `Dispose()` | 최종 종료. 대기 콜백도 폐기하며 이후 호출 불가 |

`ClientId`는 필수인 고정 ID입니다. `Host`가 비어 있으면 UDP 탐색, 값이 있으면 그 주소로 직접 TCP 접속합니다. 기본 TCP 포트는 9999, 탐색 UDP 포트는 9998입니다. 코어의 `UsbEnabled` 기본값은 false이며, 활성화하면 기본 10000번의 로컬 USB 브리지 접속을 수락합니다. 실제 AOA/usbmux 플랫폼 브리지는 호스트가 별도로 제공해야 합니다.

생성, Start, Tick, 송신, Stop, Dispose는 **같은 엔진 스레드**에서 호출합니다. 백그라운드 네트워크 스레드는 바이트 I/O와 메시지 해석을 수행하며 엔진 상태 반영은 큐를 거쳐 Tick에서 수행합니다. `CommandReceived`, `SessionReady`, `SessionAccepted`, `MeasurementStarted`, `MeasurementFinished`, `Disconnected`, `Faulted` 및 Flush 결과 콜백도 Tick을 호출한 스레드에서 실행됩니다. Tick 재진입은 허용하지 않습니다.

`Tick`에는 일시정지·timeScale과 무관한 비음수 경과 초를 전달합니다. Tick을 호출하지 않으면 수신 상태·사용자 이벤트·Flush 제한 시간이 진행되지 않습니다. 이미 도착한 응답은 같은 Tick의 제한 시간 판정보다 먼저 처리합니다. Stop의 취소 결과가 필요하면 Dispose 전에 `Tick(0)`을 호출하고, 결과가 필요 없으면 바로 Dispose할 수 있습니다. 콜백 예외는 Tick 호출자에게 전달되므로 엔진의 오류 처리 정책을 적용하십시오.

세션·측정 이벤트 안에서 Stop 또는 Dispose를 호출하면 해당 메시지의 후속 SDK 이벤트와 미전송 데이터 처리를 중단합니다. 콜백에서 Stop 후 Start를 호출해도 이전 실행의 처리를 새 실행으로 이어가지 않습니다.

### 자체 루프 예시

다음은 Unity API 없이 실행하는 C# 호스트 루프입니다. 엔진에서는 같은 역할을 자신의 초기화·프레임·종료 콜백에 배치하면 됩니다.

```csharp
using System;
using System.Diagnostics;
using System.Threading;
using XRLumina.Client;

using var quit = new CancellationTokenSource();
using var client = new XRLuminaClient(new XRLuminaClientOptions
{
    ClientId = "persistent-client-id",
    Model = "Custom engine",
    Name = "Client 1",
    Host = "127.0.0.1",
});

Console.CancelKeyPress += (sender, args) =>
{
    args.Cancel = true;
    quit.Cancel();
};
client.CommandReceived += action => Console.WriteLine($"Command: {action}");
client.SessionReady += () => Console.WriteLine($"Session: {client.Session.uuid}");
client.Faulted += error => Console.Error.WriteLine(error.Message);

client.Start();
var clock = Stopwatch.StartNew();
double previous = clock.Elapsed.TotalSeconds;
while (!quit.IsCancellationRequested)
{
    double now = clock.Elapsed.TotalSeconds;
    client.Tick(now - previous);
    previous = now;
    // 엔진이 수집한 데이터는 client.Send(...)로 전달한다.
    Thread.Sleep(1);
}
client.Stop();
client.Tick(0);
```

데이터 송신에는 `XRLumina.Protocol.MessageUnion.FromTracking(...)`, `FromInteractionEvent(...)`, `FromMesh(...)` 등의 생성 API를 사용합니다. `Vector3T`·`PoseT`·`QuaternionT` 등 생성된 데이터 모델에 변환한 숫자를 넣으며 엔진의 Transform·Texture를 코어로 넘기지 않습니다. `Send`는 측정 메시지만 허용하고 Hello·선택·세션 등 제어 메시지는 코어가 관리합니다. 반환값 true는 송신 큐에 들어갔다는 의미이며 분석 업로드 완료를 뜻하지 않습니다.

종료 명령을 받으면 잔여 데이터를 먼저 송신하고 해당 그룹에 대해 다음과 같이 요청합니다. Flush 완료까지 엔진은 Tick을 계속 호출해야 합니다. 연결 종료·새 세션 채택·시간 초과 시에도 실패 결과를 Tick에서 전달합니다.

```csharp
client.Flush(XRLumina.Protocol.FlushGroup.Interaction, 0.2f, (ok, error) =>
{
    Console.WriteLine(ok ? "업로드 완료" : $"업로드 실패: {error}");
}, timeoutSeconds: 10);
```

### Unity 어댑터의 연결

`XRLuminaClientController`가 Awake에서 SystemInfo 정보로 코어를 생성하고, Start에서 시작하며, Update에서 `Tick(Time.unscaledDeltaTime)`, OnDestroy에서 Dispose를 호출합니다. 통신 로그도 코어의 Faulted 이벤트를 받아 Unity에서 기록합니다. 화면 캡처·위치·회전·입력 수집과 Unity 코루틴은 `Runtime/Features/`에 있습니다.

기존 프리팹을 사용하는 프로젝트는 이 생명주기를 별도로 중복 실행하지 않습니다. 필요하면 `XRLuminaClientController.Client`에서 공통 API에 접근할 수 있지만, 해당 인스턴스의 Tick과 종료는 컨트롤러가 담당합니다. 자체 루프에서는 MonoBehaviour를 생성하지 않고 코어를 직접 소유합니다.

## 공개 통신 규약 v1

Unity SDK는 아래의 FlatBuffers 메시지 규약으로 데스크톱 앱과 통신합니다. 엔진에 독립적인 C# 클라이언트 사용 방법은 [공통 클라이언트](#엔진-루프에서-사용하는-공통-클라이언트)를 참고하십시오.

### 프로토콜 구성

FlatBuffers 생성 코드는 `Runtime/Protocol/`에, C# 런타임 **25.9.23**과 Apache 2.0 라이선스는 `Runtime/ThirdParty/FlatBuffers/`에 포함됩니다. 이 패키지의 프로토콜 구성 요소를 사용하여 SDK를 연동합니다. 생성 코드는 직접 편집하지 마십시오.

### 프레이밍

모든 메시지는 FlatBuffers의 **size-prefixed Envelope**입니다.

```text
[본문 길이: uint32 Little Endian][FlatBuffers Envelope 본문]
```

- 길이는 접두사 4바이트를 제외한 바이트 수이며 최대 64 MiB입니다.
- 본문의 FlatBuffers file identifier는 `XRLM`, `protocol_version`은 `1`입니다.
- 메시지 종류는 Envelope의 `message` union으로 구분합니다. 별도 종류 바이트나 JSON 헤더는 없습니다.
- TCP·USB는 동일한 바이트 규격과 해석기를 사용합니다. 수신 청크 경계는 메시지 경계가 아닙니다. 길이 접두사를 먼저 모으고 본문 전체를 수신한 뒤 해석하며, 이어지는 메시지는 계속 읽습니다.
- 표준 `FinishSizePrefixedEnvelopeBuffer`/`finishSizePrefixedEnvelopeBuffer`로 인코딩합니다. 일반 `Finish`로 작성한 버퍼에 커스텀 헤더를 붙이지 마십시오.
- 잘못된 크기는 연결을 종료합니다. 데스크톱은 경계가 알려진 손상 메시지를 건너뛰며, SDK는 손상 메시지 수신 시 연결을 종료하고 다시 탐색합니다.

### 연결

- LAN: 데스크톱 TCP `9999`로 연결합니다.
- 탐색: UDP `9998`에 `DiscoveryRequest`를 브로드캐스트합니다. 응답 `DiscoveryResponse`의 `port`, `server_instance_id`와 응답 발신 IP로 TCP 연결 대상을 구성합니다. UDP 데이터그램에도 동일한 size-prefixed Envelope 하나를 넣습니다.
- USB: 데스크톱의 Android AOA 또는 iOS usbmux 연결이 제공하는 양방향 스트림으로 동일한 Envelope를 교환합니다. Unity USB 참조 구현의 로컬 SDK 포트는 `10000`입니다. AOA/usbmux의 연결 협상은 각 전송 방식의 규격을 그대로 사용합니다.
- 연결마다 `Hello`를 전송합니다. 동일 장치의 USB·LAN 연결은 같은 `id`를 사용합니다.
- 끊어진 연결은 새 연결에서 `Hello`부터 다시 시작합니다. 메시지 자체는 영속 전송이나 exactly-once 전달을 보장하지 않습니다.

### 제어 순서

1. 클라이언트 → 데스크톱: `Hello(model, name, id, available)`.
2. 데스크톱 → 클라이언트: `Selection(Select, selection_id)`.
3. 클라이언트 → 데스크톱: `SelectionReply`에 같은 action·selection_id와 `ok`를 반환합니다. 다른 데스크톱이 소유 중이면 `ok=false`, `error`를 반환합니다.
4. 소유권이 바뀌면 각 연결에 `Availability`를 알립니다. 소유한 연결에는 true, 다른 연결에는 false입니다. 미선택이면 모두 true입니다.
5. 데스크톱 → 선택된 클라이언트: `Session(uuid, seq, selection_id)`.
6. 클라이언트 → 데스크톱: 같은 uuid·selection_id의 `SessionAck`. 동일 uuid의 재전달은 연결 복구이며 새 테스트로 초기화하지 않습니다.
7. 데스크톱 → 클라이언트: `Command(Start/Finish)`. 클라이언트에서 시작·종료를 요청할 때는 `LifecycleRequest`를 보내고 데스크톱의 Command로 상태를 동기화합니다.
8. 측정 중 기능에 맞는 메시지를 전송합니다. 종료 시 마지막 데이터와 각 그룹의 `Flush`를 순서대로 전송하고 같은 id의 `ApiResponse`를 기다립니다. `ok=false`면 업로드 실패입니다.
9. 선택 해제는 `Selection(Release)`와 같은 action·selection_id의 `SelectionReply`를 교환합니다. 측정 중 해제는 거부합니다.

데스크톱의 선택·세션 명령과 측정 데이터를 받는 클라이언트는 장치 선택과 측정 상태를 유지해야 합니다. 세션의 `seq`는 `Flush.project_member_seq`에 사용합니다. Flush id는 요청별로 구분하며, `group`은 스키마의 `FlushGroup` enum이며 `Interaction=1`, `EyeTracking=2`, `Heuristics=3`입니다. `Unknown=0`은 유효한 업로드 그룹이 아닙니다. 그룹별 마지막 데이터를 먼저 보내야 합니다. 현재 Unity 참조 구현은 Flush 응답을 최대 10초 기다립니다.

### 메시지 종류

| union 타입 | 방향 | 용도 |
|---|---|---|
| Hello | 클라이언트 → 데스크톱 | 장치 정보와 선택 가능 여부 |
| Availability | 클라이언트 → 데스크톱 | 연결별 선택 가능 여부 변경 |
| Selection | 데스크톱 → 클라이언트 | 선택·해제 요청 |
| SelectionReply | 클라이언트 → 데스크톱 | 선택·해제 결과 |
| Session | 데스크톱 → 클라이언트 | 테스트 UUID·참여자 seq |
| SessionAck | 클라이언트 → 데스크톱 | 세션 채택 확인 |
| Command | 데스크톱 → 클라이언트 | 측정 시작·종료 |
| LifecycleRequest | 클라이언트 → 데스크톱 | 측정 시작·종료 요청 |
| Flush | 클라이언트 → 데스크톱 | 그룹 데이터 조합·업로드 요청 |
| ApiResponse | 데스크톱 → 클라이언트 | Flush의 id에 대응하는 결과 |
| MirrorFrame | 클라이언트 → 데스크톱 | format=`ImageFormat.Jpeg`, data=JPEG 파일 바이트 |
| Tracking | 클라이언트 → 데스크톱 | 시점별 head·body 위치와 오일러 회전 |
| InteractionEvent | 클라이언트 → 데스크톱 | 종류·연산·횟수·위치·시각 |
| Mesh | 클라이언트 → 데스크톱 | NavMesh 정점과 삼각형 인덱스 |
| Gaze | 클라이언트 → 데스크톱 | 캡처 기준 좌표계의 시선 프레임 |
| Capture | 클라이언트 → 데스크톱 | 파노라마 PNG 파일 |
| CameraPose | 클라이언트 → 데스크톱 | 캡처 기준 카메라 위치와 쿼터니언 |
| HeuristicScreenshot | 클라이언트 → 데스크톱 | 발생 원인·시각·PNG 파일 |
| DiscoveryRequest | UDP 클라이언트 → 데스크톱 | 호스트 탐색 |
| DiscoveryResponse | UDP 데스크톱 → 클라이언트 | 접속 포트와 호스트 ID |

### 값의 의미

- 좌표는 Unity 기준 X=오른쪽, Y=위, Z=앞인 좌표계입니다. 거리 기준은 1 unit=1 m로 맞춥니다. 다른 엔진은 보내기 전에 변환합니다.
- Tracking.position과 Mesh.vertices는 월드 좌표입니다. Tracking.rotation은 도 단위 오일러 회전입니다. InteractionEvent.position도 월드 좌표이며 `has_y=false`이면 Y가 없는 평면 위치로 처리합니다.
- Gaze.points는 `inverse(captureRotation) * (worldPoint - capturePosition)`으로 변환한 캡처 기준 좌표입니다. CameraPose도 같은 기준이며 현재 SDK는 원점 위치와 항등 회전 `(0,0,0,1)`을 전송합니다.
- Tracking.timestamp와 InteractionEvent.timestamp는 UTC Unix epoch 기준 밀리초(`int64`)입니다. 분석 서버용 시각 문자열은 데스크톱에서 업로드할 때 생성합니다.
- GazeFrame.time과 HeuristicScreenshot.captured_at은 클라이언트 실행 중의 초 단위 시간입니다. Unity SDK의 시선은 `Time.time`, 휴리스틱은 `Time.realtimeSinceStartup`을 사용합니다. GazeFrame.frame은 클라이언트의 프레임 식별자입니다.
- Flush.frame_interval은 초 단위 측정 간격이며 그룹에서 사용하지 않으면 0입니다.
- Capture/HeuristicScreenshot은 PNG 한 장을 메시지 하나로 전송합니다. 청크 번호·업로드 ID·전체 크기 같은 반복 메타데이터를 보내지 않습니다. TCP·USB의 실제 바이트 분할은 스트림 계층에서 처리하며, 파일을 포함한 Envelope 본문이 64 MiB를 넘으면 거부합니다.
- name은 업로드할 파일명입니다. CameraPose와 Gaze는 구조화된 필드로 전송하며, 분석 서버가 요구하는 JSON 파일은 데스크톱이 업로드 시 생성합니다.
- 분류값은 모두 byte enum입니다. 이벤트는 `EventKind`·`EventOperation`, 캡처 원인은 `HeuristicTrigger`, 입력은 `ControllerHand`·`ControllerInput`, 이미지 형식은 `ImageFormat`을 사용합니다. 이름·UUID·오류 설명 등 실제 텍스트 데이터만 문자열로 전송합니다. Tracking의 Pose는 고정 크기 struct입니다.

### Unity에 의존하지 않는 송신 예제

생성된 C# 파일과 Google.FlatBuffers 런타임만 참조한 코드입니다. `stream`은 TCP 또는 USB의 양방향 Stream입니다.

```csharp
var builder = new Google.FlatBuffers.FlatBufferBuilder(1024);
var hello = XRLumina.Protocol.MessageUnion.FromHello(new XRLumina.Protocol.HelloT
{
    Id = "my-client-id",
    Model = "Custom engine",
    Name = "Client 1",
    Available = true,
});
var envelope = XRLumina.Protocol.Envelope.Pack(builder,
    new XRLumina.Protocol.EnvelopeT { ProtocolVersion = 1, Message = hello, });
XRLumina.Protocol.Envelope.FinishSizePrefixedEnvelopeBuffer(builder, envelope);
var bytes = builder.SizedByteArray();
stream.Write(bytes, 0, bytes.Length);
```

Google FlatBuffers C# 런타임은 [공식 v25.9.23 원본](https://github.com/google/flatbuffers/tree/v25.9.23/net/FlatBuffers)을 포함합니다. 라이선스는 `Runtime/ThirdParty/FlatBuffers/LICENSE.txt`에 있으며, Unity용 `Google.FlatBuffers.asmdef`를 추가했습니다.
