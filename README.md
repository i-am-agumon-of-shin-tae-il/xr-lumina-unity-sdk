# XRLumina Unity SDK 연동 레퍼런스

이 문서는 Unity 애플리케이션을 XRLumina 데스크톱 앱과 연결하는 데 필요한 설정과 API를 설명합니다.

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
3. Inspector에서 Camera, Head, Body를 연결합니다.
4. XRLumina 데스크톱 앱을 실행합니다.
5. Unity 애플리케이션을 실행합니다.
6. 데스크톱 앱에서 연결된 장치를 선택합니다.
7. 데스크톱 앱 또는 Unity에서 테스트를 시작합니다.
8. 테스트 종료 명령을 받은 뒤 Unity 콘텐츠를 종료 처리합니다.

[{XRLuminaClient 프리팹을 씬 Hierarchy에 배치한 화면} 스크린샷]

`XRLuminaClient`는 씬이 변경돼도 유지됩니다. 씬마다 중복 배치하지 마십시오.

## 2. Inspector 필수 설정

[`XRLuminaClient.prefab`](./Runtime/Prefabs/XRLuminaClient.prefab)에는 연동에 필요한 컴포넌트가 포함돼 있습니다.

### [XRLuminaClientController](./Runtime/Client/XRLuminaClientController.cs)

데스크톱 앱 연결, 세션, 테스트 시작·종료 요청과 명령 수신을 담당합니다. 별도의 연결 메서드는 호출하지 않습니다.

### [MirroringController](./Runtime/Features/MirroringController.cs)

| 항목 | 설정 |
| --- | --- |
| Stream Enabled | 화면 미러링을 사용하면 활성화 |
| Source Camera | 데스크톱 앱에 표시할 게임 Camera |
| Width / Height | 전송할 화면 크기 |
| Frames Per Second | 화면 전송 FPS |
| Jpeg Quality | 전송 이미지 품질 |

`Source Camera`가 비어 있으면 활성 `Main Camera`가 사용될 수 있지만 명시적으로 연결하는 것을 권장합니다.

### [InteractionController](./Runtime/Features/InteractionController.cs)

| 항목 | 설정 |
| --- | --- |
| Head | HMD Camera 또는 머리 Transform |
| Body | Player Root 또는 XR Origin Transform |
| Frames Per Second | 위치·회전 기록 FPS |
| Joystick Deadzone | 조이스틱 제스처 감지 임계값 |

일반적인 XR Rig 연결은 다음과 같습니다.

```text
XR Origin 또는 Player Root  → Body
└── HMD Camera              → Head
```

- `Head.position`: 사용자 위치와 히트맵에 사용
- `Head.rotation`: 머리 회전 분석에 사용
- `Body.position`: 이동 거리와 이동 속도 분석에 사용

`Head`와 `Body` 중 하나가 비어 있으면 `Main Camera`가 대신 사용될 수 있습니다. 같은 값이 중복 기록되지 않도록 두 참조를 모두 지정하십시오.

### [EyeTrackingController](./Runtime/Features/EyeTrackingController.cs)

| 항목 | 설정 |
| --- | --- |
| Source Camera | 시선 측정에 사용할 HMD Camera |
| Gaze Layer Mask | 시선 측정 대상 레이어 |
| Max Distance | 시선 측정 최대 거리 |
| Frames Per Second | 시선 기록 FPS |

시선 측정 대상 오브젝트에는 Collider가 있어야 하며 해당 레이어가 `Gaze Layer Mask`에 포함돼야 합니다.

[{XRLuminaClient Inspector에서 Source Camera, Head, Body를 연결한 화면} 스크린샷]

## 3. 데스크톱 앱 연결과 세션

Unity 애플리케이션이 실행되면 SDK가 XRLumina 데스크톱 앱을 자동 탐색하고 연결을 유지합니다.

연결된 장치를 데스크톱 앱에서 선택하면 Unity에 세션이 전달됩니다.

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
    private void HandleCommand(string action)
    {
        if (action == "start")
        {
            StartContent();
        }
        else if (action == "finish")
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

[{데스크톱 앱에서 장치를 선택하고 테스트를 시작할 수 있는 화면} 스크린샷]

## 5. 측정 데이터 동작 시점

| 상태 | 화면 미러링 | 위치·인터랙션 | 시선 측정 |
| --- | --- | --- | --- |
| 장치 연결 전 | 중지 | 중지 | 중지 |
| 세션 선택 | 시작 | 대기 | 대기 |
| `start` 수신 | 유지 | 기록 시작 | 기록 시작 |
| `finish` 수신 | 중지 | 기록 종료 및 전송 | 기록 종료 및 전송 |

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
            XRLuminaInteractionEventType.Object
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

## 7. 런타임 XR Rig 연결

XR Rig가 런타임에 생성되거나 씬 전환으로 변경되는 경우 `Head`와 `Body`를 다시 연결합니다.

```csharp
using UnityEngine;
using XRLumina.Features;

public sealed class XRLuminaRigBinder : MonoBehaviour
{
    [SerializeField] private Transform playerRoot;
    [SerializeField] private Camera hmdCamera;

    /// <summary>현재 XR Rig를 SDK 인터랙션 추적 대상으로 연결한다.</summary>
    private void Start()
    {
        InteractionController.Instance.SetHead(hmdCamera.transform);
        InteractionController.Instance.SetBody(playerRoot);
    }
}
```

| API | 설명 |
| --- | --- |
| `SetHead(Transform)` | 머리 추적 Transform 교체 |
| `SetBody(Transform)` | Player Root 추적 Transform 교체 |

## 8. 샘플 씬

샘플 씬은 [`XRLuminaSampleScene.unity`](./Samples~/BasicSample/XRLuminaSampleScene.unity)에서 확인할 수 있습니다.

샘플 씬에는 데스크톱·VR 이동이 가능한 플레이어와 인터랙션 종류별 타깃 5개가 배치돼 있습니다.

[{XRLuminaSampleScene의 전체 Hierarchy 구성 화면} 스크린샷]

[{Game View에서 인터랙션 타깃 5개가 보이는 화면} 스크린샷]

### 샘플 씬 전용 데스크톱 조작

| 입력 | 동작 |
| --- | --- |
| `WASD` 또는 방향키 | 이동 |
| `Left Shift` | 빠른 이동 |
| 마우스 이동 | 시점 회전 |
| `E` 또는 마우스 왼쪽 버튼 | 화면 중앙의 타깃 실행 |
| `Esc` | 마우스 잠금 해제 |

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
- Mirroring의 `Source Camera`가 실제 게임 Camera인가
- Interaction의 `Head`가 HMD Camera인가
- Interaction의 `Body`가 Player Root 또는 XR Origin인가
- EyeTracking의 `Source Camera`가 HMD Camera인가
- 시선 대상에 Collider가 있는가
- 시선 대상 Layer가 `Gaze Layer Mask`에 포함돼 있는가
- 데스크톱 앱에서 Unity 장치가 연결 상태로 표시되는가
- 데스크톱 앱에서 올바른 테스트 세션을 선택했는가
- Unity 콘텐츠 상태를 `OnCommand`의 `start`, `finish`에 맞춰 처리하는가
- 테스트 시작 후 인터랙션 이벤트를 기록하는가

## 10. 문제 해결

### 데스크톱 앱에 장치가 나타나지 않음

- 데스크톱 앱이 실행 중인지 확인합니다.
- Unity 장치와 데스크톱 앱이 같은 네트워크인지 확인합니다.
- 방화벽에서 UDP `9998`, TCP `9999` 통신을 확인합니다.
- Unity Console에서 `[DeviceTcp]` 로그를 확인합니다.

### 테스트 시작·종료 상태가 맞지 않음

- `RequestStart()` 또는 `RequestFinish()` 호출만으로 Unity 상태를 변경하지 않았는지 확인합니다.
- `OnCommand`에서 `start`, `finish`를 수신하는지 확인합니다.
- `OnCommand` 이벤트 구독이 해제되지 않았는지 확인합니다.

### 미러링 화면이 나타나지 않음

- `Stream Enabled`가 활성화돼 있는지 확인합니다.
- `Source Camera`가 활성 상태인지 확인합니다.
- 데스크톱 앱에서 장치와 세션이 선택됐는지 확인합니다.

### Head와 Body 값이 동일함

`Head` 또는 `Body`가 비어 있으면 `Main Camera`가 대신 사용될 수 있습니다. `Head`에는 HMD Camera, `Body`에는 Player Root를 각각 지정합니다.

### 인터랙션 이벤트가 기록되지 않음

- 테스트가 시작 상태인지 확인합니다.
- `InteractionController`가 활성 상태인지 확인합니다.
- `InteractionController.Instance`가 준비된 이후 호출하는지 확인합니다.
- 이벤트 호출 코드에 올바른 enum을 전달했는지 확인합니다.

### 인터랙션이 중복 기록됨

VR 버튼과 조이스틱은 SDK가 자동 감지합니다. 같은 입력 처리에서 `RecordInteractionEvent()`도 호출하는지 확인합니다.

### 시선 데이터가 기록되지 않음

- `Source Camera`가 올바르게 연결됐는지 확인합니다.
- 시선 대상의 Collider와 Layer를 확인합니다.
- 테스트가 시작 상태인지 확인합니다.
