# UnityMvpFramework

[Русский](README.ru.md)

A Unity UI framework built around MVP, VContainer, R3 and UniTask. Presenters own window behavior; Views are loaded and released through Addressables.

## Requirements

Unity **6000.3.16f1**, Git and **.NET 8 Runtime** for dependency restoration.

Runtime dependencies: VContainer **1.18.0**, UniTask **2.5.11**, R3 / R3.Unity **1.3.1**, Addressables **2.9.1**. Exact versions are pinned in `Packages/manifest.json`, `Packages/packages-lock.json` and `Assets/packages.config`.

## Setup

1. Clone the repository.
2. Before opening Unity, restore the NuGet dependencies from the repository root:

```powershell
powershell -ExecutionPolicy Bypass -File Tools/RestoreDependencies.ps1
```

For a portable .NET runtime, pass `-DotnetPath "path/to/dotnet.exe"`. The first restore requires internet access.

3. Open the project in the specified Unity version and wait for import.

This repository contains the runtime and tests. It has no application scenes or player build.

To integrate the framework, copy `Assets/MVPFramework` with its `.meta` files and install the runtime dependencies, including R3's NuGet dependencies.

## Integration contract

- Derive a View from `BaseView` and its presenter from `BasePresenter<TView>`. Put `UIWindowAttribute` on each concrete presenter.
- Place the View component on the prefab root. The View type name and Addressables address must match; View names must be unique.
- Register a scoped `ViewLoader` as `IViewLoader`, a scoped `UINavigator` as its navigation interfaces, and an `ICameraProvider` through VContainer.
- Register the configured `UIRootProvider` component. Assign roots for every used `UILayer` and an inactive temporary root. Enable scene-camera suppression only when a valid camera provider is available.
- Register each presenter with `AsWindow()` using scoped lifetime and call `RegisterWindows()` once per scope.
- Open initial windows after initialization, through a VContainer entry point. Call navigation APIs on Unity's main thread.

## Lifecycle

| API / contract | Behavior |
| --- | --- |
| `ShowAsync<T>()` | Opens a window; concurrent plain calls share the opening operation. |
| `ShowAsync<T, TPayload>()` | Supplies data through `IPayloadReceiver<TPayload>`. Close before supplying another payload. |
| `PreloadAsync<T>()` | Loads an inactive View without opening the window. |
| `Hide<T>()` | Closes and releases an open or preloaded View; cancels an opening operation. |
| `BeforeShow` | Receives a cancellation token and a `CompositeDisposable` for View subscriptions. |
| Scope disposal | Cancels navigation and releases loader-owned Addressables instances. |

The navigator clears View subscriptions on close. Cancelling an external `AttachExternalCancellation` wait cancels that caller's wait; closing the window or disposing the scope cancels the shared operation.

Layers are `Background`, `HUD`, `Window`, `Popup` and `Topmost`. Fullscreen suppression can clear and restore the scene camera's culling mask; input ownership and application-specific modal rules belong to the host application.

## Tests

Run EditMode and PlayMode suites through **Window → General → Test Runner**.

- `Assets/Tests/EditMode`: registration, payloads, repeated navigation, camera restoration, cancellation and subscription cleanup.
- `Assets/Tests/PlayMode`: real Addressables loading, cancellation, instance release, retry and recovery after a missing address.

The two empty prefabs in `Assets/Tests/PlayMode/Fixtures` and the Addressables settings exist only for integration tests. Use **Use Asset Database (fastest)** as the Addressables Play Mode Script.

## License

A license for the original code has not been selected. See [third-party notices](THIRD_PARTY_NOTICES.md).
