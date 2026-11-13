# Fight'N Rage → Android: análisis técnico

Estado a 2026-11-13. Analizado sobre la versión GOG `v220120` (`FIGHT_N_RAGE.exe`, 2,5 MB).

## Conclusión

El juego es un ensamblado .NET Framework 4.0 (C#), **sin ofuscar**, con un motor propio
(`SebaGames.Framework`) que separa el juego de la plataforma mediante interfaces.
La versión de PC implementa esas interfaces con OpenTK 3.1 + FMOD 1.7.4.

**Estrategia:** no descompilar ni redistribuir el juego. La APK contiene solo un backend
nuevo para Android (SDL2 + OpenGL ES + audio) y un launcher. El usuario aporta su
`FIGHT_N_RAGE.exe` + carpeta `data` (GOG o Steam); el launcher carga el exe como
ensamblado y lo arranca con nuestro backend.

## Punto de entrada original

```csharp
// OpenTK_Implementation.Program.Main
var desc = SebaGames.Framework.AppDescriptor.Default;
new OpenTK_Implementation.OTKCore(null /*InitConfig*/).Initialize(desc);
```

Nuestro launcher hará lo mismo con `new AndroidCore().Initialize(AppDescriptor.Default)`.

## Interfaces a implementar (namespace `SebaGames.Framework`)

| Interfaz | Miembros | Implementación PC |
|---|---|---|
| ICoreEngine | 29 (Initialize, Close, Display/Input/SoundEngine, SystemInfo, eventos OnStart/OnClose/OnUpdate/OnRender/OnError/OnLosesFocus/OnRegainFocus/OnPassedOneSecond, informes FPS, TargetFrequency) | OTKCore |
| IDisplayEngine | 6 (MainSurface, CreateSurface, CreateBitmap×2, IsShaderAvailable, CreateShader) | OTKDisplayEngine |
| IDrawableSurface | 25 (Width/Height, Clear, matrices, Color, BlendMode, Smoothing, Shader, Draw, DrawVertexs×2, GetBitmap, ClipRect, Renderize…) | OTKFrameBuffer |
| IBitmap | 10 (Get/SetPixel(s), Save…) | OTKTexture |
| IShader | 6 (SetParameter×4, SetTexture) | OTKShader |
| IInputEngine | 8 (GetJoyPadStates, GetMouseState, eventos KeyPressed/KeyReleased/WriteChar) | OTKInputEngine |
| IAudioEngine / ISound / ISoundInstance / IMusic / IAudioPlayback | ~31 | FModAudioImplementation.FMOD_AudioEngine |

El propio exe trae `Empty_AudioImplementation.Empty_AudioEngine` (audio mudo): útil para la fase 1.

## Bucle principal (OTKCore.BaseInitialization)

- Ventana de `AppDescriptor.DisplayWidth × DisplayHeight`; superficie principal `MainSurfaceWidth × MainSurfaceHeight`.
- Frecuencia: `ForceFrequency` o refresco del monitor si Vsync (mínimo 60), o `MaxFps`; tope 500, por defecto 60. `mIdealDelta = 1/freq`.
- Orden: Input.Initialize → Display.Initialize(mainW, mainH) → Audio.Initialize → crea backbuffer → rellena `App.SystemInfo` → `OnStart()` → bucle (UpdateFrame/RenderFrame) → Dispose de display, audio y ventana.

## Dependencias de Windows

- `System.Windows.Forms`: solo en `GogWindowsLauncher.Program`, `OTKCore` e `Info` (MessageBox, Clipboard, Cursor, Screen). No las usamos.
- `System.Drawing`: Size/Rectangle/Color (existen en .NET moderno) y Bitmap/Icon solo en el backend de PC.
- `GalaxyCSharp` (GOG): `GogWindowsLauncher.Program` y `StatsListener`. En Steam será Steamworks. Habrá que neutralizarlo (stub o parche en el dispositivo con Mono.Cecil).
- Datos: `data/*.dat`, comprimidos con GZip; los lee el propio juego.
- Mandos: el PC usa `gamecontrollerdb.txt` (formato SDL2).

## Plan

1. **Fase 1:** launcher + AndroidCore + gráficos (GLES 3) + mando, audio mudo.
2. **Fase 2:** audio (FMOD 2.x Android u otro motor).
3. **Fase 3:** controles táctiles, ajustes, soporte de la versión Steam.

## Pendiente

- Leer el código del backend de PC (OTKFrameBuffer.Draw/DrawVertexs, GLHelper.Prepare, OTKShader, OTKInputEngine.UpdateGamepad) para replicar su comportamiento.
- Comprobar el exe de Steam.
- Formato de los shaders (GLSL de escritorio → GLSL ES).
